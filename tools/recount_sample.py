#!/usr/bin/env python3
"""Draw a deterministic pseudo-random sample of FAITHFUL contracts for reverse spot-check.

Excludes every id the 2026-08-14 audit classified as anything other than FAITHFUL
(FIXED / MISSING / DIVERGENT / FAIL-CLOSED / BLOCKED / NOTHROUGH-in-progress), so the
sample is drawn purely from the 651 the old report called already-faithful.
"""
import csv
import random
import sys

LEDGER = r"D:\loym2\staging\equivalence_ledger_20260810.tsv"

# Every non-FAITHFUL id per docs/completeness_audit_20260814.md §1 + §4 + §5.
NON_FAITHFUL = set("""
ECON-17 ECON-38 ECON-39 ECON-40
PRICE-06 PRICE-24 PRICE-25
TRADE-10 TRADE-44 TRADE-47 TRADE-50 TRADE-61
CRAFT-19 CRAFT-46
DURA-11 DURA-23 DURA-37 DURA-38 DURA-39 DURA-40 DURA-41 DURA-42 DURA-43 DURA-44
POIS-11 POIS-16 POIS-27 POIS-30 POIS-32 POIS-33 POIS-34 POIS-38
STATE-19 STATE-31 STATE-49 STATE-50
MINE-49 MINE-56 MINE-57 MINE-58 MINE-61
GILD-29
MFLG-24
DROP-30 DROP-33 DROP-35 DROP-36 DROP-37
MOVE-02 MOVE-10 MOVE-11 MOVE-25 MOVE-31 MOVE-34 MOVE-39 MOVE-54 MOVE-57
MOVE-71 MOVE-72 MOVE-73 MOVE-74 MOVE-75 MOVE-78 MOVE-79 MOVE-82 MOVE-83
MOVE-85 MOVE-89 MOVE-90 MOVE-94 MOVE-95 MOVE-96 MOVE-97
SPWN-13 SPWN-14 SPWN-16 SPWN-17 SPWN-19 SPWN-20 SPWN-21 SPWN-22 SPWN-23
SPWN-29 SPWN-30 SPWN-31 SPWN-45 SPWN-47 SPWN-55 SPWN-56 SPWN-57
SGRP-25 SGRP-26 SGRP-30 SGRP-31 SGRP-35 SGRP-40 SGRP-41 SGRP-44
CGLD-11
QST-27 QST-28 QST-31 QST-32
""".split())


def main():
    n = int(sys.argv[1]) if len(sys.argv) > 1 else 30
    seed = int(sys.argv[2]) if len(sys.argv) > 2 else 20260814

    rows = []
    with open(LEDGER, encoding="utf-8", errors="replace") as f:
        r = csv.reader(f, delimiter="\t")
        header = next(r)
        for row in r:
            if row and row[0] not in NON_FAITHFUL:
                rows.append(row)

    print("pool(FAITHFUL candidates) = %d   excluded = %d   total = %d"
          % (len(rows), len(NON_FAITHFUL), len(rows) + len(NON_FAITHFUL)))

    rng = random.Random(seed)
    pick = rng.sample(rows, n)
    pick.sort(key=lambda x: x[0])

    for row in pick:
        cid = row[0]
        contract = row[2] if len(row) > 2 else ""
        evidence = row[3] if len(row) > 3 else ""
        strength = row[4] if len(row) > 4 else ""
        print("\n=== %s === [%s]" % (cid, strength))
        print("CONTRACT: %s" % contract)
        print("EVIDENCE: %s" % evidence)


if __name__ == "__main__":
    main()
