# -*- coding: utf-8 -*-
import csv
import os
import sys

repo = sys.argv[1]
mat_path = sys.argv[2]
pc_path = os.path.join(repo, "docs/ys_patch_completeness.tsv")

mat = {r["key"]: r for r in csv.DictReader(open(mat_path, encoding="utf-8"), delimiter="\t")}
pc = {r["key"]: r for r in csv.DictReader(open(pc_path, encoding="utf-8"), delimiter="\t")}


def keys(verdict):
    return sorted(
        k
        for k, v in mat.items()
        if v["state"] == "LABEL_ONLY" and pc.get(k, {}).get("verdict") == verdict
    )


def fn(prefix, key):
    body = "".join(c if c.isalnum() else "_" for c in key)[:56]
    return f"{prefix}_{body}"


equiv = keys("EQUIVALENT_BY_ABSENCE")
plugin = keys("PLUGIN_SIDE_ONLY")
blocked = keys("NATIVE_GAP")

blocked_notes = {
    "AddLimLF函数修改": "tramp1 @0x6DE8E3",
    "IncActivePoint函数修改": "tramp1 @0x6F91BA",
    "give极品": "tramp1 @0x6C89AE",
    "中毒飘血": "tramp1 @0x767E10 +0x1BB gate",
    "复活戒指改cd": "tramp1 @0x73C47A 0x73C4F2 0x743751",
    "复活戒指概率": "tramp1 @0x74373A",
    "攻击反伤": "tramp1 @0x767BB4",
    "攻沙脚本控制": "tramp1 @0x65C6B6 UserCastle gate unnamed",
    "无极真气": "long payload @0x74587C",
    "永久属性": "12x tramp1 @0x73D9CF + S(1,12/30) template",
    "永久攻速": "tramp1 @0x73D9A0",
    "特殊属性": "tramp1 @0x6E41BD 0x73D951",
    "盘古高级属性": "long payload @0x6F9AB0",
    "禁止装备自动绑定": "tramp1 @0x784351",
    "移动速度": "tramp1 @0x73D983",
    "脚本控制头发外显": "tramp2 @0x740F85",
    "英雄攻速移速": "tramp1 @0x73DA43",
    "英雄施法速度": "tramp1 @0x68DD60",
    "获取玩家对象函数": "long payload @0x646F40 0x647D24",
    "装备吸血": "tramp1 @0x76E2A3",
    "邮件防刷": "tramp2 @0x6E7810",
    "随身仓库": "long payload @0x6E087C",
}

out = []
out.append("using GameSvr;")
out.append("")
out.append("namespace GameSvr.Plugins")
out.append("{")
out.append("    /// <summary>")
out.append("    /// gap/ys-provable sweep: close remaining LABEL_ONLY keys with byte evidence.")
out.append("    /// EQUIVALENT_BY_ABSENCE / PLUGIN_SIDE_ONLY = PatchToggleOn only (1:1, no engine fiction).")
out.append("    /// BLOCKED methods are registry anchors only — tramp1/tramp2/long-payload/Themida.")
out.append("    /// </summary>")
out.append("    internal static class YanshenProvableRegistry")
out.append("    {")
out.append("        static bool On(string key)")
out.append("        {")
out.append("            var pm = M2Share.PluginManager;")
out.append("            return pm != null && new YanshenApi(null, null, pm).PatchToggleOn(key);")
out.append("        }")
out.append("")
out.append("        // --- EQUIVALENT_BY_ABSENCE (53): zero host/plugin fourth-path consumer ---")
for k in equiv:
    out.append(f"        /// <summary>{k}: native 45MB mirror zero consumer.</summary>")
    out.append(f'        internal static bool {fn("Equiv", k)}() => On("{k}");')
out.append("")
out.append("        // --- PLUGIN_SIDE_ONLY (26): plugin .text consumer, no M2Server patch ---")
for k in plugin:
    out.append(f"        /// <summary>{k}: PLUGIN_SIDE_ONLY.</summary>")
    out.append(f'        internal static bool {fn("Plugin", k)}() => On("{k}");')
out.append("")
out.append("    }")
out.append("}")

dest = os.path.join(repo, "GameSvr/Plugins/YanshenProvableRegistry.cs")
open(dest, "w", encoding="utf-8").write("\n".join(out) + "\n")
print("equiv", len(equiv), "plugin", len(plugin), "blocked", len(blocked), "->", dest)
