"""taskboard-script agent: linear disasm + annotations for CM 4417/4651 workers.

Evidence base: flat_image.bin @ ImageBase 0x400000, capstone x86-32.

The task-publish board is the native singleton at [[0x7D5D20]] (VMT 0x72868C).
Its +0x2C slot is a TSTDScript loaded from "<envir>\PsMapQuest\HelperQuest.pas"
(the board @Main script object). CM 4417/4651 both drive that one object:

  CM 4417  leaf 0x6DB1BF -> worker 0x699EB4(board, player, "@Main")
     0x699EED  mov eax,[board+0x2C]           ; the HelperQuest script object
     0x699EF0  test eax,eax / je              ; gate: null -> do nothing
     0x699EFE  mov ebx,[eax]                  ; vmt
     0x699F00  call [ebx+0x44]                ; TSTDScript.GotoLabel(player, 0, 0, "@Main")
     => run label "@Main" with This_Player=player, This_Item=0.

  CM 4651  leaf 0x6DB1D8 -> worker 0x6FC054(player, text)
     0x6FC064  cmp [board+0x2C],0 / je        ; gate: HelperQuest not loaded -> nothing
     0x6FC08B  call 0x6B8CC4(player, board+0x2C, 0, 0, len+1, textptr)
     0x6B8CC4 = player<->script text interaction bus. Top guards then a per-script
     dispatch. Board branch (edi==board+0x2C, 0x6B8E6D..0x6B8EB5):
       0x6B8CF3  cmp [player+0x73],0 / jne exit    ; m_boGhost
       0x6B8CFF  call 0x772DA8 = [player+0x74]     ; m_boDeath
       0x6B8D0C  cmp [player+0x461],0 / jne exit   ; m_boDealing
       0x6B8D19  test esi,esi / je exit            ; text != nil
       0x6B8D21  cmp [ebp+8],1 / jle exit          ; Length(text) >= 1
       0x6B8E9F  Insert("_", text, 2)              ; "@buy" -> "@_buy"
       0x6B8EB0  call 0x699EB4(board, player, label) ; SAME worker as CM 4417
     => run the client text as a label on HelperQuest, This_Player=player.

TSTDScript.GotoLabel (vmt+0x44, sub_733D84):
  0x733DC6/DD5/DE5  SetVar This_DB=[[0x7D5C40]] / This_Item=arg / This_Player=arg
  0x733DF4  if label in {"@Main","@_Main"} -> 0x733F6F: call vmt+0x38 (run main)
  0x733E15+ else strip '@', handle '~' args, look up proc in [self+0x24].
The C# equivalent of all of the above is
  M2Share.PasEngine.TryCallScriptLabel("HelperQuest", label, player)
whose ExecuteLabel canonicalises "@buy" -> "_buy" (matches Insert+strip) and maps
"@Main" -> ExecuteMain (matches the vmt+0x38 branch).

Usage:
  python tools\\taskboardscript_re.py all [count]
  python tools\\taskboardscript_re.py 0x699EB4 [count]
"""
import sys
from capstone import Cs, CS_ARCH_X86, CS_MODE_32

IMAGE = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000

with open(IMAGE, "rb") as f:
    IMG = f.read()

MD = Cs(CS_ARCH_X86, CS_MODE_32)
MD.detail = True


def rd(va, n):
    o = va - BASE
    if o < 0 or o + n > len(IMG):
        return b""
    return IMG[o:o + n]


def u32(va):
    b = rd(va, 4)
    return int.from_bytes(b, "little") if len(b) == 4 else None


def bhex(b):
    return " ".join("%02X" % x for x in b)


def as_string(va):
    """Delphi 7 AnsiString: [va-8]=refcount(-1/1) [va-4]=len chars.. NUL."""
    if va is None or va < BASE + 0x1000 or va - BASE + 4 > len(IMG):
        return None
    rc = u32(va - 8)
    ln = u32(va - 4)
    if rc is None or ln is None:
        return None
    if rc not in (0xFFFFFFFF, 1) or not (0 < ln < 400):
        return None
    data = rd(va, ln + 1)
    if len(data) < ln + 1 or data[ln] != 0:
        return None
    try:
        return data[:ln].decode("gbk")
    except Exception:
        return data[:ln].decode("latin1")


def annot(i):
    m, ops = i.mnemonic, i.op_str
    notes = []
    for tok in ops.replace(",", " ").replace("[", " ").replace("]", " ").split():
        if tok.startswith("0x") and len(tok) >= 7:
            v = int(tok, 0)
            s = as_string(v)
            if s is not None:
                notes.append("str=%r" % s)
            elif 0x7C0000 <= v <= 0x7E0000:
                notes.append("<global 0x%06X>" % v)
    if m == "call" and ops.startswith("0x"):
        t = int(ops, 0)
        notes.append("-> 0x%06X %s" % (t, bhex(rd(t, 8))))
    if m == "call" and ("+ 0x44]" in ops or "+ 0x38]" in ops or "+ 0x8c]" in ops):
        notes.append("*** VMT CALL ***")
    if m == "call" and ("+ 0x250]" in ops or "+ 0x254]" in ops):
        slot = "0x250" if "0x250" in ops else "0x254"
        notes.append("*** SM SEND via vmt+%s ***" % slot)
    return "   ; " + " ".join(notes) if notes else ""


def dump(va, count=140):
    print("WORKER 0x%06X  first bytes %s" % (va, bhex(rd(va, 8))))
    cur = va
    seen_ret = False
    for _ in range(count):
        ins = list(MD.disasm(rd(cur, 16), cur))
        if not ins:
            print("  %06X  <decode fail>" % cur)
            break
        i = ins[0]
        if seen_ret and bhex(i.bytes).startswith("55 8B EC"):
            print("  ---- (next function prologue) ----")
            break
        print("  %06X  %-26s %s %s%s"
              % (i.address, bhex(i.bytes), i.mnemonic, i.op_str, annot(i)))
        if i.mnemonic in ("ret", "retn"):
            print("  ---- ret ----")
            seen_ret = True
        cur += i.size


WORKERS = [
    (4417, 0x6DB1BF, "CM 4417 leaf (board, player, '@Main')", 12),
    (4417, 0x699EB4, "CM 4417 worker -> GotoLabel(player,0,0,label)", 60),
    (4651, 0x6DB1D8, "CM 4651 leaf (player, body text)", 12),
    (4651, 0x6FC054, "CM 4651 worker -> 0x6B8CC4 board branch", 32),
    (0, 0x6B8CC4, "player<->script text bus (top guards + dispatch)", 130),
    (0, 0x733D84, "TSTDScript.GotoLabel vmt+0x44", 70),
    (0, 0x733F6F, "GotoLabel @Main branch -> vmt+0x38", 20),
]

if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "all":
        for ident, va, tag, cnt in WORKERS:
            head = ("CM %d " % ident) if ident else ""
            print("\n########## %sworker 0x%06X  (%s) ##########" % (head, va, tag))
            dump(va, cnt)
    else:
        va = int(sys.argv[1], 0)
        cnt = int(sys.argv[2]) if len(sys.argv) > 2 else 140
        dump(va, cnt)
