param(
    [string]$DumpPath = "D:\loym2\staging\questinfo_runtime_dump\yanshen2_0_7_dll.memory.bin"
)

$ErrorActionPreference = 'Stop'
[System.Text.Encoding]::RegisterProvider([System.Text.CodePagesEncodingProvider]::Instance)
$encoding = [System.Text.Encoding]::GetEncoding(936)
$bytes = [System.IO.File]::ReadAllBytes($DumpPath)
$dataStart = 0x2A0000
$dataLength = 0xB000
$dataText = $encoding.GetString($bytes, $dataStart, $dataLength)

$features = @(
    '切割也反伤', '火墙不反伤', '反伤带抗性', '千分比属性', '主号被攻击触发',
    '主号新切割', '临时属性', '新永久属性', '金币上限突破', '修复飘血数值',
    '自定义循环函数', '月灵不扣蓝', '月灵新伤害', '瞬移怪物', '杀怪触发',
    '自定义红名村', '沙巴克攻城范围', '沙巴克复活点', '英雄切割',
    '指定英雄放技能', '英雄不自动释放技能', 'npc自定义函数',
    '诱惑之光修改', '主号分身术', '额外技能', '真隐身术修复', '主号技能加成',
    '气功波重定义', '自定义野蛮', '概率格挡', '刺杀免伤',
    '安全区禁止诱惑和圣言', '技能弹射', '英雄分身修复', '英雄开天修复',
    '合击等级修改', '英雄技能加成', '英雄技能点数增加', '怪物伤害触发技能特效'
)

foreach ($feature in $features) {
    $key = $feature + '_说明注释'
    $keyIndex = $dataText.IndexOf($key, [System.StringComparison]::Ordinal)
    if ($keyIndex -lt 0) {
        throw "Missing note key in 2.07 dump: $key"
    }

    $textIndex = $keyIndex + $key.Length
    while ($textIndex -lt $dataText.Length -and $dataText[$textIndex] -eq [char]0) { $textIndex++ }
    $textEnd = $dataText.IndexOf([char]0, $textIndex)
    if ($textEnd -lt 0) { throw "Unterminated note text in 2.07 dump: $key" }

    [pscustomobject]@{
        Feature = $feature
        KeyOffset = ('decoded-char:0x{0:X}' -f ($dataStart + $keyIndex))
        Text = $dataText.Substring($textIndex, $textEnd - $textIndex).TrimEnd()
    }
}
