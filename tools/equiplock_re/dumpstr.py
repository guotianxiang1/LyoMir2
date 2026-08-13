import io, eq_re
vas = [0x6D1A98, 0x6D1C0C, 0x6C555C, 0x6C5550]
with io.open("strings.txt", "w", encoding="utf-8") as f:
    for va in vas:
        f.write("0x%08X : %r\n" % (va, eq_re.dstr(va)))
print("ok")
