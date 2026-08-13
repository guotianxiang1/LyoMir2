using System.Text;
using System.Text.RegularExpressions;

// Static audit for the MONAI "missing monster class" pass — every native TAnimal-tree
// subclass that had NO C# case in AddBaseObject (the factory fell through to the native
// default sink 0x67AE5E = nil, so the race spawned NOTHING) and is being filled in.
//
// The factory dispatch is byte-verified over flat_image.bin (ImageBase 0x400000):
//   sub_679F8C @0x67A006: xor eax,eax / mov al,[monRec+0x14] (race) / add eax,-0xB /
//     cmp eax,0xEE / ja 0x67AE5E / mov al,[eax+0x67A026] (index table) /
//     jmp dword[eax*4+0x67A115] (jump table)
// Every class below records: race, native VMT, native parent VMT, native ctor EA and
// the exact overridden VMT slots (diff vs parent VMT, positive slots).  Each assertion
// pins the race number and the key override so a regression in EITHER direction fails.
//
// This is a source-text-shape audit (compile-safe, does not run the server).  Per
// REPLICATION_RULES §4.17 the regexes are intentionally loose (presence of the case /
// the base class / the override name), not exact bodies, so a more-faithful rewrite of a
// body does not spuriously fail.

var repoRoot = AuditRepoRoot.Resolve(args);

var failures = new List<string>();
void Check(bool cond, string msg)
{
    if (cond) { Console.WriteLine("  PASS  " + msg); return; }
    failures.Add(msg);
    Console.WriteLine("  FAIL  " + msg);
}

string Read(params string[] parts)
{
    var p = Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    return File.Exists(p) ? File.ReadAllText(p) : string.Empty;
}

static string StripComments(string src)
{
    if (string.IsNullOrEmpty(src)) return src;
    var sb = new StringBuilder(src.Length);
    bool inStr = false, inChar = false, inLine = false, inBlock = false;
    for (var i = 0; i < src.Length; i++)
    {
        var c = src[i];
        var n = i + 1 < src.Length ? src[i + 1] : '\0';
        if (inLine) { if (c == '\n') { inLine = false; sb.Append(c); } continue; }
        if (inBlock) { if (c == '*' && n == '/') { inBlock = false; i++; } continue; }
        if (!inStr && !inChar && c == '/' && n == '/') { inLine = true; continue; }
        if (!inStr && !inChar && c == '/' && n == '*') { inBlock = true; i++; continue; }
        if (!inChar && c == '"' && (i == 0 || src[i - 1] != '\\')) inStr = !inStr;
        else if (!inStr && c == '\'' && (i == 0 || src[i - 1] != '\\')) inChar = !inChar;
        sb.Append(c);
    }
    return sb.ToString();
}

var usrEngn = StripComments(Read("GameSvr", "UsrSystem", "UsrEngn.cs"));
Check(usrEngn.Length > 0, "UsrEngn.cs readable");
var addBase = usrEngn.IndexOf("private TBaseObject AddBaseObject(Envirnoment map",
    StringComparison.Ordinal);
Check(addBase > 0, "AddBaseObject(Envirnoment,...) found");
var switchBody = addBase > 0
    ? usrEngn.Substring(addBase, Math.Min(20000, usrEngn.Length - addBase))
    : string.Empty;

// className -> class file source (stripped)
string ClassSrc(string cls) =>
    StripComments(Read("GameSvr", "Monsters", "Monster", cls + ".cs"));

// Assert: race N is wired to `new <cls>()`, <cls> inherits <parent>, and the file exists.
void PinClass(int race, string cls, string parent, string vmt, string note)
{
    var caseRe = new Regex(@"case\s+" + race + @"\s*:\s*(?:\r?\n\s*)*Cert\s*=\s*new\s+"
        + Regex.Escape(cls) + @"\s*\(");
    Check(caseRe.IsMatch(switchBody),
        $"race {race} -> new {cls}()  [native VMT {vmt}; without the case the race hits "
        + "default sink 0x67AE5E = nil and never spawns]");
    var src = ClassSrc(cls);
    Check(src.Length > 0, $"{cls}.cs present");
    Check(Regex.IsMatch(src, @"class\s+" + Regex.Escape(cls) + @"\s*:\s*"
            + Regex.Escape(parent) + @"\b"),
        $"{cls} : {parent}  [{note}]");
}

// ---------------------------------------------------------------------------
// race 247 TParalyzationMon  VMT 0x665C18  parent TGasMothMonster(0x65F754)
//   ctor sub_66D1F8 = pure `call 0x6670E4` (GasMoth ctor, sets only m_nViewRange=7).
//   Sole VMT override Attack(+0x204)=0x66D1EC = empty forwarder `call 0x667124`
//   (=GasMoth Attack).  GasMoth's Attack body is C#'s sub_4A9C78 (via AttackTarget),
//   so TParalyzationMon behaves exactly like TGasMothMonster; the empty override is a
//   no-op.  => C# is `ParalyzationMon : GasMothMonster` with no behavioural override.
Console.WriteLine("== race 247 TParalyzationMon ==");
PinClass(247, "ParalyzationMon", "GasMothMonster", "0x665C18",
    "native parent TGasMothMonster; ctor forwards to GasMoth ctor; only VMT override is "
    + "an empty Attack(+0x204) forwarder, so behaviour == GasMothMonster");

// ---------------------------------------------------------------------------
// race 144 TIceDoor  VMT 0x66E6AC  parent TAnimal(0x71D51C)  ZERO VMT overrides.
//   ctor sub_674BF0 = TAnimal.Create + m_boStickMode=1 / m_wEffectResistance=250 /
//   m_btDirection=0 / m_nViewRange=0.  Behaviour is pure AnimalObject (a static,
//   immovable, blind ice-door obstacle).  C# parent for native TAnimal is AnimalObject.
Console.WriteLine("== race 144 TIceDoor ==");
PinClass(144, "IceDoor", "AnimalObject", "0x66E6AC",
    "native parent TAnimal == C# AnimalObject; zero VMT overrides; ctor only sets "
    + "m_boStickMode/m_wEffectResistance/m_btDirection/m_nViewRange");
{
    var src = ClassSrc("IceDoor");
    Check(Regex.IsMatch(src, @"m_boStickMode\s*=\s*true"),
        "IceDoor sets m_boStickMode=true  [native +0x75=1]");
    Check(Regex.IsMatch(src, @"m_wEffectResistance\s*=\s*250"),
        "IceDoor sets m_wEffectResistance=250  [native word +0x26C=0xFA]");
    Check(Regex.IsMatch(src, @"m_nViewRange\s*=\s*0"),
        "IceDoor sets m_nViewRange=0  [native +0x78=0; never searches]");
    Check(!Regex.IsMatch(src, @"public\s+override"),
        "IceDoor has NO override  [native VMT is byte-identical to TAnimal]");
}

Console.WriteLine();
if (failures.Count == 0)
{
    Console.WriteLine("NativeMissingMonsterClassCheck: PASS "
        + "race247=ParalyzationMon<GasMothMonster race144=IceDoor<AnimalObject");
    return 0;
}
Console.WriteLine($"NativeMissingMonsterClassCheck: FAIL ({failures.Count})");
foreach (var f in failures) Console.WriteLine("  - " + f);
return 1;
