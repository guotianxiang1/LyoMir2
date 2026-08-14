-- P0-3 hero_data 名槽一次性迁移（禁止无标记全表对调）
-- 证据：M2 sub_689034 @0x689034  record+0x08=HeroName, +0x18=MasterName
-- C# 旧库 NameLayout=0/1 为错位布局；代码翻转后 Save 写 NameLayout=2
-- **运维窗口执行；应用层不得自动执行 swap。**

-- 阶段 A1：DDL（与 NativeSchemaProvisioner.MigrateHeroDataNameLayout 同形）
ALTER TABLE mir3.hero_data
  ADD COLUMN NameLayout TINYINT NOT NULL DEFAULT 0
  COMMENT '0=unknown 1=csharp-swapped 2=native-correct';

ALTER TABLE mir3_backup.hero_data
  ADD COLUMN NameLayout TINYINT NOT NULL DEFAULT 0;

-- 阶段 A2：只读检测（示例 — 须在应用侧用 NativeHeroBlobCodec + GBK 实现）
-- 对每个 idx：解压 Data，三槽 stride 0x49D4，读 slot08/slot18 ShortString，
-- JOIN hero_index ON idx：
--   slot08=HeroName AND slot18=MasterName → NameLayout=2（已是原生，不 swap）
--   slot08=MasterName AND slot18=HeroName → NameLayout=1（待 swap）
--   其余 → NameLayout=0（人工复核，禁止自动 swap）

-- 阶段 A3：仅 NameLayout=1 的 swap（每条 record 交换 +0x08..+0x17 与 +0x18..+0x27 共 16 字节）
-- 伪代码（须用与 NativeHeroDbFrameCodec.SwapRecordNameSlots 等价的逻辑）：
--   UPDATE hero_data SET Data=<swapped_blob>, NameLayout=2 WHERE NameLayout=1;

-- 阶段 A4：与代码同窗口上线
--   NativeHeroDbFrameCodec: HeroNameOffset=0x08, MasterNameOffset=0x18
--   Load: NameLayout=1 拒绝；NameLayout=0/1 读时 ApplyStoredNameLayout；Save 写 NameLayout=2
