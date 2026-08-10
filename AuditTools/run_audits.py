#!/usr/bin/env python3
"""Serial build+run harness for the AuditTools console audits.

Usage
    py -3 AuditTools/run_audits.py [options]

    --list              enumerate discovered projects, build/run nothing
    --filter SUBSTR     only projects whose name contains SUBSTR (repeatable)
    --only-sln          only projects referenced by LyoMir2.sln
    --jobs N            parallel workers, DEFAULT 1 (see WARNING below)
    --timeout SEC       per-step timeout, default 600
    --report PATH       report path, default AuditTools/_audit_report.json
    --selftest          run the internal logic tests and exit (no dotnet)

WARNING --jobs > 1 IS UNSAFE HERE.
    These audits are not hermetic. Several of them are mutation checks: they
    write a deliberately broken copy of a file into the SHARED source tree,
    rebuild to prove the audit turns red, then restore it. Two workers in the
    same tree will therefore see each other's half-mutated sources and report
    errors in files they never touched (a false red), or compile a mutated file
    and call it green (a false green). Others race on fixture files written to
    a fixed path under bin/, and on the GameSvr obj/ intermediate directory.
    Serial is the only mode whose results mean anything; --jobs > 1 exists for
    triaging build breakage only, never for accepting a PASS.

Design notes
    * subprocess.run is NEVER given text=True. MSBuild on this machine emits
      GBK-encoded Chinese and Python's text-mode reader raises inside the
      reader thread, which surfaces as stdout=None and silently destroys the
      log. We always capture bytes and decode ourselves with errors='replace'.
    * Build failure is detected on ': error' (plus the localized ': 错误'),
      never on the bare substring 'error', which matches directory names such
      as errorhandling\\, the word "error" in prose comments, and MSBuild's own
      "0 Error(s)" summary line, all of which yield false reds.
    * The report is rewritten after every project, atomically, so Ctrl-C
      leaves a valid partial report on disk.
    * No network access. NuGet restore is left to whatever is already cached;
      the build is invoked without an explicit restore step.
"""

import argparse
import glob
import json
import os
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(HERE)
SLN = os.path.join(REPO, "LyoMir2.sln")
DEFAULT_REPORT = os.path.join(HERE, "_audit_report.json")

# Exit-code contract for the audited programs.
EXIT_PASS = 0
EXIT_INCOMPLETE = 2  # "some checks were skipped" (e.g. missing golden corpus)

STATUS_RANK = {  # worst first
    "BUILD-ERROR": 0,
    "FAIL": 1,
    "INCOMPLETE": 2,
    "NO-ENTRY": 3,
    "PASS": 4,
    "SKIPPED": 5,
}

# --------------------------------------------------------------------------
# pure helpers (all covered by --selftest)
# --------------------------------------------------------------------------

_ERROR_MARKERS = (": error", ": 错误")


def decode_output(raw):
    """Bytes -> str, never raises. Tries UTF-8, then GBK, then latin-1.

    This exists because text=True is forbidden: MSBuild's Chinese output is
    cp936 and the text-mode reader dies on it.
    """
    if raw is None:
        return ""
    if isinstance(raw, str):
        return raw
    for encoding in ("utf-8", "cp936"):
        try:
            return raw.decode(encoding)
        except (UnicodeDecodeError, LookupError):
            pass
    return raw.decode("latin-1", errors="replace")


def build_failed(text):
    """True when build output carries a real compiler/MSBuild diagnostic.

    Matches ': error' / ': 错误' only. A bare 'error' search is a false-red
    generator: it hits paths, prose, and the '0 Error(s)' summary.
    """
    low = text or ""
    return any(marker in low for marker in _ERROR_MARKERS)


def tail_lines(text, count=40):
    """Last `count` non-trailing-blank lines, as a list."""
    if not text:
        return []
    lines = text.replace("\r\n", "\n").replace("\r", "\n").split("\n")
    while lines and not lines[-1].strip():
        lines.pop()
    return lines[-count:] if count > 0 else lines


def project_is_exe(csproj_text):
    """True when the csproj declares OutputType Exe/WinExe.

    A project without OutputType compiles to a library and has no entry
    point, so `dotnet run` cannot work: that is NO-ENTRY, not a failure.
    """
    text = (csproj_text or "").lower()
    return "<outputtype>exe" in text or "<outputtype>winexe" in text


def classify(is_exe, build_rc, build_text, run_rc, run_text=""):
    """Map a project's outcome onto the status taxonomy."""
    if not is_exe:
        return "NO-ENTRY"
    if build_rc is None:
        return "BUILD-ERROR"
    if build_rc != 0 or build_failed(build_text):
        return "BUILD-ERROR"
    if run_rc is None:
        return "FAIL"
    if run_rc == EXIT_PASS:
        return "PASS"
    if run_rc == EXIT_INCOMPLETE:
        return "INCOMPLETE"
    return "FAIL"


def parse_sln_projects(sln_text):
    """Names of AuditTools projects referenced by the solution."""
    names = set()
    for line in (sln_text or "").splitlines():
        if "AuditTools" not in line or ".csproj" not in line:
            continue
        for chunk in line.split('"'):
            chunk = chunk.strip()
            if not chunk.lower().endswith(".csproj"):
                continue
            parts = chunk.replace("/", "\\").split("\\")
            if len(parts) >= 2 and parts[0].lower() == "audittools":
                names.add(parts[-2])
    return names


def discover(audit_dir):
    """All audit csproj paths. Handles both flat and nested layouts."""
    found = set()
    for pattern in (
        os.path.join(audit_dir, "*", "*.csproj"),
        os.path.join(audit_dir, "*", "**", "*.csproj"),
    ):
        for path in glob.glob(pattern, recursive=True):
            norm = os.path.normpath(path)
            lowered = norm.lower()
            if os.sep + "bin" + os.sep in lowered:
                continue
            if os.sep + "obj" + os.sep in lowered:
                continue
            found.add(norm)
    return sorted(found)


def project_name(csproj_path):
    return os.path.basename(os.path.dirname(csproj_path))


# --------------------------------------------------------------------------
# process execution
# --------------------------------------------------------------------------


def run_capture(argv, cwd, timeout):
    """Run argv, capturing BYTES. Returns (rc, text). rc None on timeout."""
    try:
        completed = subprocess.run(
            argv,
            cwd=cwd,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            timeout=timeout,
            # text=True is deliberately absent. Do not add it.
        )
    except subprocess.TimeoutExpired as expired:
        return None, decode_output(expired.output) + "\n[harness] TIMEOUT"
    except FileNotFoundError as missing:
        return None, "[harness] executable not found: %s" % missing
    return completed.returncode, decode_output(completed.stdout)


def process_project(csproj, timeout, tail):
    name = project_name(csproj)
    record = {
        "name": name,
        "csproj": os.path.relpath(csproj, REPO).replace("\\", "/"),
        "status": None,
        "build_rc": None,
        "run_rc": None,
        "reason": "",
        "seconds": 0.0,
        "build_tail": [],
        "run_tail": [],
    }
    started = time.time()
    try:
        with open(csproj, "rb") as handle:
            csproj_text = decode_output(handle.read())
    except OSError as error:
        record["status"] = "BUILD-ERROR"
        record["reason"] = "unreadable csproj: %s" % error
        record["seconds"] = round(time.time() - started, 2)
        return record

    is_exe = project_is_exe(csproj_text)
    if not is_exe:
        record["status"] = "NO-ENTRY"
        record["reason"] = "no OutputType Exe: library, nothing to run"
        record["seconds"] = round(time.time() - started, 2)
        return record

    build_rc, build_text = run_capture(
        ["dotnet", "build", csproj, "-c", "Debug", "--nologo", "-v", "q"],
        REPO, timeout)
    record["build_rc"] = build_rc
    record["build_tail"] = tail_lines(build_text, tail)
    if build_rc is None:
        record["reason"] = "build timeout"
    elif build_rc != 0 or build_failed(build_text):
        record["reason"] = "build diagnostics present"

    if classify(True, build_rc, build_text, EXIT_PASS) == "BUILD-ERROR":
        record["status"] = "BUILD-ERROR"
        record["seconds"] = round(time.time() - started, 2)
        return record

    run_rc, run_text = run_capture(
        ["dotnet", "run", "--project", csproj, "-c", "Debug", "--no-build"],
        REPO, timeout)
    record["run_rc"] = run_rc
    record["run_tail"] = tail_lines(run_text, tail)
    record["status"] = classify(True, build_rc, build_text, run_rc, run_text)
    if run_rc is None:
        record["reason"] = "run timeout"
    elif record["status"] == "INCOMPLETE":
        record["reason"] = "exit 2: checks skipped"
    elif record["status"] == "FAIL":
        record["reason"] = "exit %s" % run_rc
    record["seconds"] = round(time.time() - started, 2)
    return record


# --------------------------------------------------------------------------
# reporting
# --------------------------------------------------------------------------


def write_report(path, payload):
    """Atomic rewrite so an interrupt cannot truncate the report."""
    temp = path + ".tmp"
    with open(temp, "w", encoding="utf-8") as handle:
        json.dump(payload, handle, ensure_ascii=False, indent=1)
    os.replace(temp, path)


def summarize(records):
    order = sorted(records, key=lambda r: (STATUS_RANK.get(r["status"], 9),
                                           r["name"].lower()))
    width = max([len(r["name"]) for r in records] + [4])
    lines = ["", "%-11s %-*s %7s  %s" % ("STATUS", width, "PROJECT",
                                         "SECONDS", "REASON"),
             "-" * (11 + width + 12 + 30)]
    for record in order:
        lines.append("%-11s %-*s %7.1f  %s" % (
            record["status"], width, record["name"],
            record.get("seconds") or 0.0, record.get("reason", "")))
    counts = {}
    for record in records:
        counts[record["status"]] = counts.get(record["status"], 0) + 1
    lines.append("")
    lines.append("totals: " + ", ".join(
        "%s=%d" % (key, counts[key])
        for key in sorted(counts, key=lambda k: STATUS_RANK.get(k, 9))))
    return "\n".join(lines), counts


# --------------------------------------------------------------------------
# selftest
# --------------------------------------------------------------------------


def selftest():
    checks = []

    def expect(label, actual, wanted):
        ok = actual == wanted
        checks.append((ok, label, actual, wanted))

    # decode_output: must survive GBK and invalid bytes without raising.
    expect("decode utf8", decode_output(b"hello"), "hello")
    expect("decode gbk chinese",
           decode_output("\u9519\u8bef".encode("cp936")), "\u9519\u8bef")
    expect("decode none", decode_output(None), "")
    expect("decode str passthrough", decode_output("x"), "x")
    got = decode_output(b"\xff\xfe\x00bad")
    checks.append((isinstance(got, str), "decode invalid bytes -> str",
                   type(got).__name__, "str"))

    # build_failed: real diagnostics vs the classic false reds.
    expect("error: real CS diagnostic",
           build_failed(r"C:\p\Program.cs(12,5): error CS1002: ; expected"),
           True)
    expect("error: localized diagnostic",
           build_failed("Program.cs(1,1): \u9519\u8bef CS1002"), True)
    expect("error: MSBuild zero-error summary",
           build_failed("    0 Warning(s)\n    0 Error(s)\n"), False)
    expect("error: path containing the word error",
           build_failed(r"D:\errorhandling\Program.cs -> ok"), False)
    expect("error: prose containing the word error",
           build_failed("// returns an error code when the gate rejects"),
           False)
    expect("error: warning is not an error",
           build_failed("Program.cs(3,1): warning CS0168: unused"), False)
    expect("error: empty", build_failed(""), False)

    # tail_lines
    expect("tail keeps last n",
           tail_lines("\n".join(str(i) for i in range(100)), 3),
           ["97", "98", "99"])
    expect("tail strips trailing blanks",
           tail_lines("a\nb\n\n\n", 2), ["a", "b"])
    expect("tail crlf", tail_lines("a\r\nb\r\n", 2), ["a", "b"])
    expect("tail empty", tail_lines(""), [])

    # project_is_exe
    expect("exe detected",
           project_is_exe("<OutputType>Exe</OutputType>"), True)
    expect("winexe detected",
           project_is_exe("<OutputType>WinExe</OutputType>"), True)
    expect("library when absent",
           project_is_exe("<TargetFramework>net8.0</TargetFramework>"), False)

    # classify matrix
    expect("classify NO-ENTRY", classify(False, 0, "", 0), "NO-ENTRY")
    expect("classify PASS", classify(True, 0, "ok", 0), "PASS")
    expect("classify INCOMPLETE", classify(True, 0, "ok", 2), "INCOMPLETE")
    expect("classify FAIL rc1", classify(True, 0, "ok", 1), "FAIL")
    expect("classify FAIL rc-1", classify(True, 0, "ok", -1), "FAIL")
    expect("classify BUILD-ERROR rc",
           classify(True, 1, "boom", None), "BUILD-ERROR")
    expect("classify BUILD-ERROR text-only",
           classify(True, 0, "x.cs(1,1): error CS1", None), "BUILD-ERROR")
    expect("classify build timeout",
           classify(True, None, "", None), "BUILD-ERROR")
    expect("classify run timeout", classify(True, 0, "ok", None), "FAIL")
    expect("classify PASS not fooled by error-ish path",
           classify(True, 0, r"D:\errorlib\a.cs -> out.dll", 0), "PASS")

    # parse_sln_projects
    sln = (
        'Project("{FAE0}") = "AlphaCheck", '
        '"AuditTools\\AlphaCheck\\AlphaCheck.csproj", "{1}"\n'
        'Project("{2150}") = "AuditTools", "AuditTools", "{2}"\n'
        'Project("{FAE0}") = "GameSvr", "GameSvr\\GameSvr.csproj", "{3}"\n'
        'Project("{FAE0}") = "BetaCheck", '
        '"AuditTools/BetaCheck/BetaCheck.csproj", "{4}"\n')
    expect("sln parse", parse_sln_projects(sln), {"AlphaCheck", "BetaCheck"})
    expect("sln parse empty", parse_sln_projects(""), set())

    # summary ordering: worst first
    records = [
        {"name": "p", "status": "PASS", "seconds": 1, "reason": ""},
        {"name": "b", "status": "BUILD-ERROR", "seconds": 1, "reason": ""},
        {"name": "i", "status": "INCOMPLETE", "seconds": 1, "reason": ""},
        {"name": "f", "status": "FAIL", "seconds": 1, "reason": ""},
    ]
    text, counts = summarize(records)
    body = [ln.split()[1] for ln in text.splitlines()
            if ln[:1].isalpha() and "STATUS" not in ln and "totals" not in ln]
    expect("summary worst-first", body, ["b", "f", "i", "p"])
    expect("summary counts", counts["PASS"], 1)

    failed = [c for c in checks if not c[0]]
    for ok, label, actual, wanted in checks:
        print("%s  %s" % ("PASS" if ok else "FAIL", label))
        if not ok:
            print("        actual=%r wanted=%r" % (actual, wanted))
    print("\nselftest: %d/%d passed" % (len(checks) - len(failed), len(checks)))
    return 1 if failed else 0


# --------------------------------------------------------------------------
# main
# --------------------------------------------------------------------------


def main(argv=None):
    parser = argparse.ArgumentParser(
        description="Build and run the AuditTools console audits.")
    parser.add_argument("--filter", action="append", default=[],
                        metavar="SUBSTR",
                        help="only projects whose name contains SUBSTR")
    parser.add_argument("--list", action="store_true",
                        help="list discovered projects and exit")
    parser.add_argument("--only-sln", action="store_true",
                        help="restrict to projects referenced by LyoMir2.sln")
    parser.add_argument("--jobs", type=int, default=1,
                        help="parallel workers (default 1; >1 is UNSAFE, "
                             "shared-source audits race)")
    parser.add_argument("--timeout", type=int, default=600,
                        help="per-step timeout in seconds (default 600)")
    parser.add_argument("--tail", type=int, default=40,
                        help="output lines to keep per step (default 40)")
    parser.add_argument("--report", default=DEFAULT_REPORT,
                        help="report path (default AuditTools/_audit_report.json)")
    parser.add_argument("--selftest", action="store_true",
                        help="test the harness logic itself; runs no dotnet")
    args = parser.parse_args(argv)

    if args.selftest:
        return selftest()

    projects = discover(HERE)
    if not projects:
        print("no audit projects found under %s" % HERE)
        return 1

    in_sln = set()
    if os.path.exists(SLN):
        with open(SLN, "rb") as handle:
            in_sln = parse_sln_projects(decode_output(handle.read()))

    selected = projects
    if args.only_sln:
        selected = [p for p in selected if project_name(p) in in_sln]
    for needle in args.filter:
        lowered = needle.lower()
        selected = [p for p in selected if lowered in project_name(p).lower()]

    print("discovered %d audit projects, %d referenced by the sln, "
          "%d selected" % (len(projects), len(in_sln), len(selected)))

    if args.list:
        for path in selected:
            name = project_name(path)
            print("  %-6s %s" % ("[sln]" if name in in_sln else "", name))
        return 0

    if not selected:
        print("selection is empty")
        return 1

    if args.jobs > 1:
        print("WARNING --jobs=%d: these audits share one source tree and "
              "several mutate it in place. Results may be false red or "
              "false green. Use for build triage only." % args.jobs)

    payload = {
        "generated": time.strftime("%Y-%m-%dT%H:%M:%S"),
        "repo": REPO.replace("\\", "/"),
        "discovered": len(projects),
        "sln_referenced": sorted(in_sln),
        "selected": [project_name(p) for p in selected],
        "jobs": args.jobs,
        "complete": False,
        "interrupted": False,
        "results": [],
    }
    write_report(args.report, payload)

    records = []
    interrupted = False
    try:
        if args.jobs <= 1:
            for index, csproj in enumerate(selected, 1):
                name = project_name(csproj)
                print("[%d/%d] %s" % (index, len(selected), name), flush=True)
                record = process_project(csproj, args.timeout, args.tail)
                print("        -> %s (%.1fs) %s" % (
                    record["status"], record["seconds"], record["reason"]),
                    flush=True)
                records.append(record)
                payload["results"] = records
                write_report(args.report, payload)
        else:
            from concurrent.futures import ThreadPoolExecutor, as_completed
            with ThreadPoolExecutor(max_workers=args.jobs) as pool:
                futures = {
                    pool.submit(process_project, csproj, args.timeout,
                                args.tail): csproj for csproj in selected}
                done = 0
                for future in as_completed(futures):
                    record = future.result()
                    done += 1
                    print("[%d/%d] %s -> %s (%.1fs) %s" % (
                        done, len(selected), record["name"], record["status"],
                        record["seconds"], record["reason"]), flush=True)
                    records.append(record)
                    payload["results"] = records
                    write_report(args.report, payload)
    except KeyboardInterrupt:
        interrupted = True
        print("\ninterrupted: %d of %d projects done, partial report kept"
              % (len(records), len(selected)))

    payload["results"] = records
    payload["interrupted"] = interrupted
    payload["complete"] = not interrupted and len(records) == len(selected)

    if records:
        text, counts = summarize(records)
        payload["totals"] = counts
        write_report(args.report, payload)
        print(text)
    else:
        write_report(args.report, payload)
        counts = {}

    print("\nreport: %s" % args.report)
    if interrupted:
        return 130
    if counts.get("BUILD-ERROR") or counts.get("FAIL"):
        return 1
    if counts.get("INCOMPLETE"):
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
