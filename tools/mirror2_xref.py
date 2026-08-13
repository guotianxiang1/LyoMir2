"""Find E8/E9 rel32 and absolute-immediate references to a VA in the M2 image."""
import sys
import struct

IMG = r"D:\loym2\staging\_reunpack_work\flat_image.bin"
BASE = 0x400000


def main():
    target = int(sys.argv[1], 16)
    with open(IMG, "rb") as f:
        data = f.read()

    print("xrefs to 0x%06X" % target)
    n = len(data)
    for i in range(n - 5):
        b = data[i]
        if b in (0xE8, 0xE9):
            rel = struct.unpack_from("<i", data, i + 1)[0]
            if BASE + i + 5 + rel == target:
                print("  %-4s 0x%06X" % ("call" if b == 0xE8 else "jmp", BASE + i))
    tb = struct.pack("<I", target)
    start = 0
    while True:
        j = data.find(tb, start)
        if j < 0:
            break
        print("  imm  0x%06X  (context %s)" % (
            BASE + j,
            " ".join("%02X" % x for x in data[max(0, j - 6):j + 8])))
        start = j + 1


if __name__ == "__main__":
    main()
