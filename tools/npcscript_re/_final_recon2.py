import json, collections

reg = json.load(open("_native_registry.json", encoding="utf-8"))
bi = json.load(open("_builtins.json", encoding="utf-8"))
cs2 = json.load(open("_cs_cases2.json", encoding="utf-8"))


def grab(*keys):
    s = set()
    for k in keys:
        for kk, v in cs2.items():
            if kk.endswith("::" + k):
                s |= {x for x in v if not any(c.isupper() for c in x)}
    return s


PLAYER = grab("CallPlayerFunc", "CallPlayerMethod", "GetPlayerProperty", "SetPlayerProperty")
NPC = grab("CallNpcFunc", "CallNpcMethod", "GetNpcProperty", "SetNpcProperty")
ANIMAL = grab("GetAnimalProperty")
ITEM = grab("GetItemProperty", "SetItemProperty")
DB = grab("CallDbMethod", "GetDbProperty")
GLOBAL = grab("CallStandaloneFunction", "ExecuteBuiltinFunction", "ExecuteBuiltinProcedure",
              "TryCallYanshenFunc")

BUCKET = {"TPlayer": PLAYER, "THumanKind": PLAYER, "TPsNpc": NPC, "TAnimal": ANIMAL,
          "TBaseItem": ITEM, "TMySQLDB": DB, "(global)": GLOBAL,
          "TCreature": PLAYER | NPC | ANIMAL, "TBaseObj": PLAYER | NPC | ANIMAL | ITEM | DB,
          "TOBJECT": PLAYER | NPC | ANIMAL | ITEM | DB, "TBaseGroup": PLAYER | GLOBAL}
LABEL = {"TPlayer": "This_Player", "THumanKind": "This_Player", "TPsNpc": "This_NPC",
         "TAnimal": "This_Animal", "TBaseItem": "This_Item", "TMySQLDB": "This_DB",
         "(global)": "bare call", "TCreature": "This_Player/This_NPC/This_Animal",
         "TBaseObj": "any", "TOBJECT": "any", "TBaseGroup": "MyGroup"}

ANY = PLAYER | NPC | ANIMAL | ITEM | DB | GLOBAL

missing, wrongrecv = [], []
for r in reg:
    n = r["name"].lower()
    if n in ("create", "free"):
        continue
    if n in BUCKET[r["cls"]]:
        continue
    if n in ANY:
        wheres = [w for w, s in (("This_Player", PLAYER), ("This_NPC", NPC), ("This_Animal", ANIMAL),
                                 ("This_Item", ITEM), ("This_DB", DB), ("bare", GLOBAL)) if n in s]
        wrongrecv.append((r, wheres))
    else:
        missing.append(r)

print("=" * 100)
print("A. MISSING  -- native registration, no C# handler anywhere  (%d)" % len(missing))
for r in sorted(missing, key=lambda x: (x["cls"], x["name"])):
    print("   %-11s %-6s %s  %-28s %s" % (r["cls"], r["kind"], r["va"], LABEL[r["cls"]] + "." + r["name"], r["decl"][:78]))

print()
print("=" * 100)
print("B. WRONG RECEIVER -- C# knows the name but not on the receiver native registers it (%d)" % len(wrongrecv))
for r, w in sorted(wrongrecv, key=lambda x: (x[0]["cls"], x[0]["name"])):
    print("   %-11s %-6s %s  native=%-14s cs-has-on=%s" % (r["cls"], r["kind"], r["va"],
                                                           LABEL[r["cls"]] + "." + r["name"], w))

nb = {r["name"].lower() for r in bi if r["name"] != "a"}
print()
print("C. PascalScript runtime built-ins: native=%d  C#-known=%d  missing=%s" %
      (len(nb), len(nb & ANY), sorted(nb - ANY)))

json.dump({"missing": [dict(cls=r["cls"], name=r["name"], va=r["va"], kind=r["kind"], decl=r["decl"]) for r in missing],
           "wrongrecv": [dict(cls=r["cls"], name=r["name"], va=r["va"], cs=w) for r, w in wrongrecv]},
          open("_final_recon2.json", "w", encoding="utf-8"), ensure_ascii=False, indent=1)
