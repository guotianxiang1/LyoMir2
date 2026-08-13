"""Crude structural sanity check for a C# file: strip comments and string/char
literals, then compare brace/paren balance between git HEAD and the working copy.
Not a compiler, but it catches a dropped or extra brace from a manual edit."""
import subprocess, sys, io, re, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8", errors="replace")

REPO = r"D:\loym2\.claude\wt2\m-npcscript"
DQ = '"'
SQ = "'"
BS = "\\"


def strip(src):
    out = []
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if c == "/" and i + 1 < n and src[i + 1] == "/":
            j = src.find("\n", i)
            i = n if j < 0 else j
            continue
        if c == "/" and i + 1 < n and src[i + 1] == "*":
            j = src.find("*/", i)
            i = n if j < 0 else j + 2
            continue
        if c == DQ:
            i += 1
            while i < n and src[i] != DQ:
                if src[i] == BS:
                    i += 1
                i += 1
            i += 1
            continue
        if c == SQ:
            i += 1
            while i < n and src[i] != SQ:
                if src[i] == BS:
                    i += 1
                i += 1
            i += 1
            continue
        out.append(c)
        i += 1
    return "".join(out)


def report(tag, src):
    t = strip(src)
    print("%-5s  {=%-6d }=%-6d bal=%-4d (=%-6d )=%-6d pbal=%-4d case=%d" % (
        tag, t.count("{"), t.count("}"), t.count("{") - t.count("}"),
        t.count("("), t.count(")"), t.count("(") - t.count(")"),
        len(re.findall(r"\bcase\b", t))))


for rel in sys.argv[1:]:
    print("==", rel)
    old = subprocess.run(["git", "show", "HEAD:" + rel], capture_output=True,
                         cwd=REPO).stdout.decode("utf-8", "replace")
    new = open(os.path.join(REPO, rel.replace("/", os.sep)), encoding="utf-8").read()
    report("HEAD", old)
    report("WORK", new)
