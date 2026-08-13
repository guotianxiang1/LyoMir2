import struct, sys, io
D = open(r"D:\loym2\staging\_reunpack_work\flat_image.bin", "rb").read()
B = 0x400000
out = io.open(r"D:\loym2\.claude\wt2\cm-1\tools\cm1_re\_cm1_str.txt", "w", encoding="utf-8")
for a in sys.argv[1:]:
    va = int(a, 16)
    ln = struct.unpack("<I", D[va - B - 4:va - B])[0]
    ref = struct.unpack("<i", D[va - B - 8:va - B - 4])[0]
    raw = D[va - B:va - B + ln]
    out.write("0x%08X len=%d ref=%d\n  raw : %s\n  gbk : %s\n\n"
              % (va, ln, ref, raw.hex().upper(), raw.decode("gbk", errors="replace")))
out.close()
print("ok")
