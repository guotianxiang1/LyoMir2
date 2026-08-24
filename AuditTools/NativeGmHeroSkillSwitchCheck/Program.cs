using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using GameSvr;
using SystemModule;

var checks = 0;

void Check(bool condition, string label)
{
    checks++;
    if (!condition)
        throw new Exception($"FAIL {label}");
}

Check(NativeGmHeroSkillSwitch.DispatchIndex == 611, "dispatch id");
Check(NativeGmHeroSkillSwitch.RequiredPermission == 3, "permission");
Check(NativeGmHeroSkillSwitch.HandlerAddress == 0x006246C6u, "handler address");
Check(NativeGmHeroSkillSwitch.SetterAddress == 0x0073D458u, "setter address");
Check(NativeGmHeroSkillSwitch.MagicListSetterAddress == 0x0073D38Cu,
    "magic list setter address");

var owner = (TBaseObject)RuntimeHelpers.GetUninitializedObject(typeof(TPlayObject));
owner.m_MagicList = new List<TUserMagic>();
var definition = new TMagic { wMagicID = 321, sMagicName = "火球术" };
var first = new TUserMagic
{
    MagicInfo = definition,
    wMagIdx = definition.wMagicID,
    NativeRecord = new byte[40]
};
var second = new TUserMagic
{
    MagicInfo = definition,
    wMagIdx = definition.wMagicID,
    NativeRecord = new byte[40]
};
owner.m_MagicList.Add(first);
owner.m_MagicList.Add(second);

Check(first.NativeSkillEnabled && first.NativeSkillSwitchValue == TUserMagic.NativeSkillSwitchOn,
    "new skill starts enabled");
Check(NativeGmHeroSkillSwitch.TrySet(owner, definition, false),
    "known skill setter finds first entry");
Check(!first.NativeSkillEnabled &&
      first.NativeSkillSwitchValue == TUserMagic.NativeSkillSwitchOff,
    "known skill writes 0xFF disabled marker");
Check(BinaryPrimitives.ReadUInt16LittleEndian(first.NativeRecord.AsSpan(6, 2)) ==
      TUserMagic.NativeSkillSwitchOff,
    "disabled marker persists at record +0x06");
Check(second.NativeSkillEnabled && second.NativeSkillSwitchValue == TUserMagic.NativeSkillSwitchOn,
    "setter stops after first matching entry");

Check(NativeGmHeroSkillSwitch.TrySet(owner, definition, true),
    "known skill re-enable");
Check(first.NativeSkillEnabled && first.NativeSkillSwitchValue == TUserMagic.NativeSkillSwitchOn,
    "known skill writes zero enabled marker");
Check(BinaryPrimitives.ReadUInt16LittleEndian(first.NativeRecord.AsSpan(6, 2)) ==
      TUserMagic.NativeSkillSwitchOn,
    "enabled marker persists at record +0x06");

var unknown = new TMagic { wMagicID = 999, sMagicName = "不存在" };
Check(!NativeGmHeroSkillSwitch.TrySet(owner, unknown, false),
    "unknown definition is silent no-op");
Check(first.NativeSkillEnabled && second.NativeSkillEnabled,
    "unknown definition leaves entries unchanged");

var restoredRecord = new byte[40];
BinaryPrimitives.WriteUInt16LittleEndian(restoredRecord.AsSpan(6, 2),
    TUserMagic.NativeSkillSwitchOff);
var restored = new TUserMagic { NativeRecord = restoredRecord };
Check(!restored.NativeSkillEnabled &&
      restored.NativeSkillSwitchValue == TUserMagic.NativeSkillSwitchOff,
    "record decode restores native switch word");

Console.WriteLine($"PASS NativeGmHeroSkillSwitchCheck ({checks} checks)");
