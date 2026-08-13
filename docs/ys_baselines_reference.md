# 眼神(Yanshen)逆向底本索引

> 目的：本仓 `staging` 树极大，递归搜索屡屡超时（实测全盘找 `AllFuc.pas` 会直接超时失败，
> 按名过滤+读内容的扫描也要 ~46 秒）。多个子代理在定位底本上反复浪费时间且各自得出过
> 不一致的结论。此表为实测结果，后续直接查表，不要再全盘搜。

## 1. 插件 DLL 转储（反汇编用）

| 用途 | 路径 | 说明 |
|---|---|---|
| 2.0.8 主用 | `staging\yanshen208_strparam_runtime_dump_20260719\` | `manifest.json` 写 base `0x57C40000`，但**绝对操作数未重定位**（如 `push 0x102b02e4`）。按 `0x10000000` 基读 `.rdata` 引用即可。 |
| 2.0.8 delayed | `staging\..._delayed_...\` | **已重定位**（同处为 `push 0x57ef02e4`，差 `0x47C40000`）。**被 Themida 搬到远端的函数体只在这份里有内容**，另一份是全零。 |
| 2.0.7 运行期 | `staging\questinfo_runtime_dump\yanshen2_0_7_dll.memory.bin` | 28.4 MB。配置结构体布局与 2.0.8 不同（偏移全变），但键名逐条相同 —— **跨版本复算的利器**。 |

两份 2.0.8 的 RVA 布局相同。涉及远端代码时用 delayed，并对落在 `[0x57C40000, +size)` 的
操作数减去 `0x47C40000`，即等于装载器重定位。

## 2. 宿主底本

| 用途 | 路径 |
|---|---|
| M2Server | `staging\_reunpack_work\flat_image.bin`（ImageBase `0x400000`，file_off = VA − 0x400000） |
| GameGate | `staging\_gg_reunpack_work\dump_gg2025\flat_image.bin` |

注：M2 主底本对 `施毒术` / `眼神` / `盘古` / `AllFuc` / `集成函数` 等特征串**全部 0 命中** ——
眼神的证据不在宿主里，别在这儿找。

## 3. AllFuc.pas（脚本 API 源码）

眼神的 `ys_*` 脚本 API 不是原生导出符号，而是把实参拼成 `!!!!` 魔法串、再走宿主
`GetBagItemCount` / `Give` 等 API 递进插件的**薄包装**。要看语义就读这个文件。

| 类别 | 路径 | 大小 | `!!!!` 数 |
|---|---|---|---|
| **加密原件（不可直接读）** | `staging\_ysprod\PROD_AllFuc.pas.txt`<br>`staging\_cfgdump\SCR_allfuc.txt`<br>`staging\_mnpc_prod_utf8\CommonScripts__眼神专用__AllFuc.pas.txt` | 32204 B | 0 |
| **2.0.8 解密本（推荐）** | `staging\_ys208_plain\Envir__CommonScripts__眼神专用__AllFuc.pas`<br>`staging\_ys_out\AllFuc_208_DECRYPTED.txt` | 32635 B | 88 |
| 2.0.8 解密本（另一份） | `staging\_prodrecon\plain\PROD_AllFuc_DECRYPTED.pas` | 31766 B | 88 |
| **原始捕获（明文）** | `staging\ys208_original_capture\Mir200\Envir\CommonScripts\眼神专用\AllFuc.pas`<br>`staging\ys207_original_capture\...\AllFuc.pas`<br>`staging\pas-include-context-20260714\Envir\CommonScripts\眼神专用\AllFuc.pas`<br>`staging\m2_type2_probe_20260726\Mir200\...\AllFuc.pas` | 29870 B | 87 |
| 使用例子文档 | `staging\_cfgdump\DOC_allfuc_ex.txt` | 65036 B | 0（含 65 处 `ys_` 引用） |

三份 32204 B 的是加密原件，文件头为二进制乱码，**不要拿它做证据**。

## 4. 已建立的分析方法（勿重复造轮子）

- **trampoline 安装器 ABI**：共三个 builder，**调用点实测分布如下**（`E8` rel32 全镜像穷举）：

  | builder | VA | 调用点数 | 形态 |
  |---|---|---|---|
  | 模板 A | `0x10032CC0` | 71 | 「一 dword 装一字节」数组 |
  | 模板 B | `0x10032FD0` | 30 | 同上 |
  | **裸 memcpy C** | **`0x10033340`** | **306** | 4 参数，非模板 |

  三者合计 **407**，与补丁站点总数 407 完全吻合 —— 即**每个站点恰好经由其中一个 builder**。

  **注意：C 才是主力（306/407 = 75%），A/B 合计只占 25%。** 早期文档把 C 写成「另有」的
  附注，导致 stub 还原器只实现了模板走法，在 C 的站点上一律报 "cannot align"。
  典型现场：`0x100CF496`（`刀刀切割`）`call 0x10033340`，紧随其后
  `lea ecx,[ebx+0x158]` / `push 0x102C6CC4` / `call 0x100F018C` 即状态标签调用。

  模板 A/B：`0xE8`/`0xE9` 元素后紧跟 `>0xFF` 的 dword 时由 `0x10032DE2` 改写成 rel32；
  末元素的 rel32 由调用方补。详见 `docs/ys_aclass_surgical_20260814.md`。

  **裸 memcpy C 的签名：`C(payload, len, va, va)`** —— cdecl 四参，arg3 与 arg4 同值。
  调用点实录（`0x100CF496`，特性 `刀刀切割`）：

  ```
  100CF45C  mov dword [ebp-0xB0], 0x89575653   ; 就地拼载荷
  100CF466  mov word  [ebp-0xAC], 0xF84D       ; 合计 6 字节 53 56 57 89 4D F8
  100CF47F  push 0x767BAE                      ; arg4 = 宿主 VA
  100CF484  push 0x767BAE                      ; arg3 = 宿主 VA（同值）
  100CF489  push 6                             ; arg2 = 长度
  100CF48B  push eax                           ; arg1 = 载荷缓冲(lea [ebp-0xB0])
  100CF496  call 0x10033340
  ```

  注意 `0x767BAE` 是**宿主 M2Server 的 VA**（0x400000–0x800000 段）—— C 直接改写宿主字节。

  builder 内部（`0x10033340..0x100333C5`）：
  - 收参：`[ebp+8]→ebx`(payload)、`[ebp+0xC]→edi`(len)、`[ebp+0x10]→[ebp-0x1C]`(va)、
    `[ebp+0x14]→[ebp-0x20]`(va)
  - 两道前置闸：`call 0x100329F0` 后 `cmp eax,0x64 / je` 跳过；`push 1 / call 0x11513568 /
    test al,al / jne` 跳过
  - 改页保护：`lea eax,[edi+1]`（= **len+1**，作为 size）→ `push &oldProtect / push 0x40 /
    push &size / call 0x10032A50`（`0x40` = PAGE_EXECUTE_READWRITE）
  - 地址有效性门：`cmp [ebp-0x1C],esi`(esi=0) / `jbe` 跳过 —— 比的是 **va 非零**
  - 拷贝：`push edi(len) / push ebx(payload) / push [ebp-0x20](va) / call 0x10223FD0`
    ⇒ `memcpy(dest=va, src=payload, len)`

  > **订正**：本文早前一版据 `lea eax,[edi+1]` 推测 C 是「改写既有 `E8`/`E9` 的 rel32」，
  > **该推断已被证伪** —— `edi` 是长度而非地址，`[edi+1]` 是改页 size。C 做的是
  > **整段字节替换**。同时早前把 `cmp [ebp-0x1C],esi` 记为「长度门」也是错的，
  > 它是地址有效性门。

  模板 builder A/B 的签名（据 `w/ys-atlas` 复核）：`(outObj, hookStart, hookStart, hookEnd,
  template[], len)` —— **只有 hookStart 是补丁目标，hookEnd 仅为区间上界**。把 end 误当目标
  会虚报「新增 VA」。取址时「call 前 14 条指令」的窗口太窄（漏 26 个站点），
  应按实参形状取：倒数第 1 个 host push = hookStart，第 3 个 = hookEnd。

  **重要结论（`w/ys-atlas` 证）**：101 个 trampoline 站点**全是 apply 臂**（revert 臂 0 个）；
  每个特性的 revert 臂一律用 memcpy 把原字节写回同一地址，故 trampoline 的目标 VA
  已被 memcpy 站点全覆盖，**差集为空**。共有的 306 站点上标签/目标 VA/字节数三项与
  独立提取的 g09 完全吻合、0 冲突。
- **状态标签归属法**：每条 apply/revert 臂以
  `call 0x100F018C(labelObject, "<键名>(已启动)|(未启动)")` 收尾，从安装点向前走到下一个
  标签即可把补丁站点归属到 GUI 键。全量 407 站点 → 107 特性，见
  `docs/ys_patch_label_atlas.tsv`。
- **配置键名解法**：配置序列化器两段 run（`0x10005E10..`、`0x10009EB3..`）**严格 CMP→KEY
  交替** —— 每个 `cmp dword [esi+off],0x1F4` 配它**后面**那个 `push <键串VA>`。该性质已被
  证明而非假设：全镜像 75 条 CMP 按地址排序后，相邻键串在 `.rdata` 里首尾相接、4 字节
  对齐、缺口数 0。2.0.7 偏移不同但键名逐条相同。
- **可达性**：用 `tools/ys_key_reachability.py`，**不要**用旧矩阵 —— 旧矩阵只给「键名字面量
  持有者」播种，会漏掉经中继方法接线的键（曾误报 10 条）。
- **消费者普查**：要用 `cmp dword [reg+OFF],0x1F4` 字节模式，**不要**沿
  `mov reg,[0x1031C0E0]` 跟踪指针 —— apply 臂是把配置对象当 `this` 收的，指针跟踪法在
  已落地键的对照组上会全军覆没。
- 配置单例：`[0x1031BEFC]` 与 `[0x1031C0E0]` 是同一对象 `0x10319DA8`，故 `cfg+off` 与
  `cfg2+off` 是同一套坐标。

## 5. 环境陷阱

- `dotnet run --project X -- <arg>` 在本环境**不转发 argv**；需要传参的 AuditTool 要直接
  执行生成的 exe。
- `HeroLifecycleCheck` 不传构建目录时退出码 2 是**空跑**而非断言失败；有效目录
  `D:\loym2\.claude\wt2\Build\Mir200`。
- 仓库真身在 `D:\loym2\LyoMir2-master`（`D:\loym2\.git` 已被改名为 `.git.broken-20260810`）。
- PowerShell 不支持 heredoc；提交信息用 `git commit -F <临时文件>`。反汇编脚本别命名为
  `dis.py`（与标准库 `dis` 冲突）。
