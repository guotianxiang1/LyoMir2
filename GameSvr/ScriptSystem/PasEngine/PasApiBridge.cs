using System.Diagnostics;
using System.Text;
using SystemModule;
using SystemModule.Packet;
using GameSvr;
using GameSvr.Plugins;
using GameSvr.Services;

namespace GameSvr.PasEngine
{
    /// <summary>
    /// Yanshen tunnel command routing is integrated directly into the API bridge.
    /// When a script calls Player.GetBagItemCount("!!!!...") or
    /// Player.Give("itemName!!!!..."), the bridge auto-detects and routes to
    /// the YanshenCommandEngine for native execution.
    /// </summary>
    public partial class PasApiBridge
    {
        private PluginManager GetRunningYanshenPluginManager()
        {
            var manager = M2Share.PluginManager;
            return manager?.GetPlugin("YanshenCompat")?.State == PluginState.Running
                ? manager
                : null;
        }

        private YanshenCommandEngine GetYanshenEngine()
        {
            var manager = GetRunningYanshenPluginManager();
            return CurrentPlayer != null && manager != null
                ? new YanshenCommandEngine(CurrentPlayer, CurrentNpc, manager)
                : null;
        }

        /// <summary>
        /// Intercepts GetBagItemCount/GetBagItemCountEx calls to check for !!!! tunnel commands.
        /// If detected, routes to yanshen command engine.
        /// </summary>
        private bool TryExecuteTunnelCommand(string itemName, string apiName, out int result)
        {
            result = 0;

            if (string.IsNullOrEmpty(itemName) || !PluginManager.IsTunnelCommand(itemName))
                return false;

            // 入口选择器 sub_1005E4D0 的 8 条前缀一条也比不中时，原生不是报错，
            // 而是把串原样交还给宿主：链尾 0x1005F20F 返回 -1656，钩子
            // 0x58A05264 `cmp eax,0xFFFFF988` / 0x58BBAAF5 `je 0x58DBA7B2` 就去跑
            // TPlayObject.GetBagItemCount 0x007447C0 的原函数体。原函数体先
            // 0x7447E7 拿名字查 std 物品表（sub_74C1E0 查不到给 -1），
            // 0x7447EF `cmp [ebp-0x10],0` / `jle 0x744868` 直接跳到出口返回计数槽
            // 的初值 0 —— 也就是「按物品名查背包，`!!!!…` 不是物品，返回 0」。
            // 这里 return false 让调用点落到 CountBagItem，即那条原函数体。
            if (!PluginManager.IsNativeSelectorHit(itemName))
                return false;

            var cmd = PluginManager.ParseTunnelCommand(itemName);
            if (cmd == null) return false;

            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, apiName);
            var engine = GetYanshenEngine();
            if (engine == null) return false;

            result = engine.ExecuteCommand(cmd, apiName);

            var info = GetRunningYanshenPluginManager()?.GetPlugin("YanshenCompat");
            if (info != null) info.CommandCount++;

            return true;
        }

        /// <summary>Reference to PasScriptHost for deferred calls (CallOut/CallOutEx).</summary>
        public static PasScriptHost ScriptHost;

        public bool CurrentInputOk
        {
            get => _executionContext.Value?.InputOk ?? false;
            set => EnsureExecutionContext().InputOk = value;
        }

        public string CurrentInputStr
        {
            get => _executionContext.Value?.InputStr ?? string.Empty;
            set => EnsureExecutionContext().InputStr = value ?? string.Empty;
        }

        /// <summary>
        /// Resolve the current NPC's script path for CallOut/CallOutEx.
        /// Falls back to PsNpcscripts/default.pas if NPC context is unavailable.
        /// </summary>
        private string ResolveNpcScriptPath(NormNpc npc = null)
        {
            if (ScriptHost == null) return "PsNpcscripts/default.pas";
            npc ??= CurrentNpc;
            if (npc != null)
            {
                var resolution = ScriptHost.ResolveNpcScript(npc);
                if (resolution.Kind == NpcPasScriptResolutionKind
                        .DynamicUnavailable)
                    return null;
                if (resolution.ScriptPath != null) return resolution.ScriptPath;
            }
            return "PsNpcscripts/default.pas";
        }

        /// <summary>
        /// Intercepts Give/GiveBindItem calls to check for !!!! embedded element data.
        /// </summary>
        private bool TryExecuteTunnelGive(string itemName, int count, bool bind = false)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            if (!itemName.Contains("!!!!")) return false;

            YanshenApi.EnsureDirectCallReady(M2Share.PluginManager, "Give");
            var engine = GetYanshenEngine();
            if (engine == null) return false;

            return engine.HandleGiveWithElements(itemName, count, bind);
        }

    }

    public partial class PasApiBridge
    {
        private sealed class ScriptExecutionContext
        {
            public TPlayObject Player;
            public NormNpc Npc;
            public TBaseObject Animal;
            public TUserItem Item;
            public bool InputOk;
            public string InputStr = string.Empty;
            public PasDbBridge Database;
        }

        private sealed class ScriptExecutionScope : IDisposable
        {
            private PasApiBridge _owner;
            private readonly ScriptExecutionContext _current;
            private readonly ScriptExecutionContext _previous;

            public ScriptExecutionScope(PasApiBridge owner, ScriptExecutionContext current,
                ScriptExecutionContext previous)
            {
                _owner = owner;
                _current = current;
                _previous = previous;
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner == null) return;
                _current.Database?.Dispose();
                owner._executionContext.Value = _previous;
            }
        }

        private readonly AsyncLocal<ScriptExecutionContext> _executionContext = new();

        public TPlayObject CurrentPlayer
        {
            get => _executionContext.Value?.Player;
            set => EnsureExecutionContext().Player = value;
        }

        public NormNpc CurrentNpc
        {
            get => _executionContext.Value?.Npc;
            set => EnsureExecutionContext().Npc = value;
        }

        public TUserItem CurrentItem
        {
            get => _executionContext.Value?.Item;
            set => EnsureExecutionContext().Item = value;
        }

        public TBaseObject CurrentAnimal
        {
            get => _executionContext.Value?.Animal;
            set => EnsureExecutionContext().Animal = value;
        }

        public IDisposable PushContext(TPlayObject player, NormNpc npc,
            bool inputOk = false, string inputStr = null)
        {
            return PushContextCore(player, npc, inputOk, inputStr, null, null);
        }

        public IDisposable PushItemContext(TPlayObject player, NormNpc npc,
            bool inputOk, string inputStr, TUserItem item)
        {
            return PushContextCore(player, npc, inputOk, inputStr, item,
                _executionContext.Value?.Animal);
        }

        public IDisposable PushAnimalContext(TPlayObject player, TBaseObject animal)
        {
            return PushContextCore(player, null, false, null, null, animal);
        }

        private IDisposable PushContextCore(TPlayObject player, NormNpc npc,
            bool inputOk, string inputStr, TUserItem item, TBaseObject animal)
        {
            var previous = _executionContext.Value;
            var current = new ScriptExecutionContext
            {
                Player = player,
                Npc = npc,
                Animal = animal,
                Item = item,
                InputOk = inputOk,
                InputStr = inputStr ?? string.Empty
            };
            _executionContext.Value = current;
            return new ScriptExecutionScope(this, current, previous);
        }

        private ScriptExecutionContext EnsureExecutionContext()
        {
            var context = _executionContext.Value;
            if (context != null) return context;
            context = new ScriptExecutionContext();
            _executionContext.Value = context;
            return context;
        }

        private PasDbBridge GetCurrentDatabase()
        {
            var context = EnsureExecutionContext();
            return context.Database ??= new PasDbBridge(M2Share.g_Config?.sConnctionString);
        }

        public bool CallDbMethod(string name, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            var database = GetCurrentDatabase();
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "executescript":
                    result = PasValue.FromBool(args.Count > 0 && database.ExecuteScript(args[0].AsString()));
                    return true;
                case "executequery":
                    result = PasValue.FromInt(args.Count > 0 ? database.ExecuteQuery(args[0].AsString()) : 0);
                    return true;
                case "psfirst":
                    database.PsFirst();
                    return true;
                case "psnext":
                    result = PasValue.FromBool(database.PsNext());
                    return true;
                case "psbof":
                    result = PasValue.FromBool(database.PsBof);
                    return true;
                case "pseof":
                    result = PasValue.FromBool(database.PsEof);
                    return true;
                case "psrecordcount":
                    result = PasValue.FromInt(database.PsRecordCount);
                    return true;
                case "psfieldcount":
                    result = PasValue.FromInt(database.PsFieldCount);
                    return true;
                case "psfieldname":
                    result = PasValue.FromString(args.Count > 0 ? database.PsFieldName(args[0].AsInt()) : string.Empty);
                    return true;
                case "psfieldbyname":
                    result = PasValue.FromString(args.Count > 0 ? database.PsFieldByName(args[0].AsString()) : string.Empty);
                    return true;
                case "psfieldbypos":
                    result = PasValue.FromString(args.Count > 0 ? database.PsFieldByPos(args[0].AsInt()) : string.Empty);
                    return true;
                case "saveplayeract":
                    // Native TMySQLDB.SavePlayerAct persists engine-owned activity state.
                    return RejectUnsupportedNativeApi(out result);
                default:
                    return false;
            }
        }

        public bool GetDbProperty(string name, out PasValue result)
        {
            result = PasValue.Nil;
            var database = GetCurrentDatabase();
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "psbof": result = PasValue.FromBool(database.PsBof); return true;
                case "pseof": result = PasValue.FromBool(database.PsEof); return true;
                case "psrecordcount": result = PasValue.FromInt(database.PsRecordCount); return true;
                case "psfieldcount": result = PasValue.FromInt(database.PsFieldCount); return true;
                default: return false;
            }
        }

        public bool GetItemProperty(string name, out PasValue result)
        {
            result = PasValue.Nil;
            var item = CurrentItem;
            if (item == null) return false;
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "itemname":
                    result = PasValue.FromString(M2Share.UserEngine.GetStdItemName(item.wIndex) ?? string.Empty);
                    return true;
                case "clientitemid":
                    result = PasValue.FromInt(CurrentPlayer != null
                        ? CurrentPlayer.EnsureClientItemId(item)
                        : item.ClientItemID);
                    return true;
                case "addpa1": return TryGetItemAddValue(item, 0, out result);
                case "addpa2": return TryGetItemAddValue(item, 1, out result);
                case "addpa3": return TryGetItemAddValue(item, 2, out result);
                case "addpa4": return TryGetItemAddValue(item, 3, out result);
                case "addpa5": return TryGetItemAddValue(item, 4, out result);
                default:
                    return false;
            }
        }

        public bool SetItemProperty(string name, PasValue value)
        {
            var item = CurrentItem;
            if (item == null) return false;
            var index = (name ?? string.Empty).ToLowerInvariant() switch
            {
                "addpa1" => 0,
                "addpa2" => 1,
                "addpa3" => 2,
                "addpa4" => 3,
                "addpa5" => 4,
                _ => -1
            };
            if (index < 0) return false;
            if (item.btValue == null || item.btValue.Length < 14)
            {
                var replacement = new byte[14];
                if (item.btValue != null)
                    Buffer.BlockCopy(item.btValue, 0, replacement, 0, Math.Min(item.btValue.Length, replacement.Length));
                item.btValue = replacement;
            }
            item.btValue[index] = (byte)Math.Clamp(value.AsInt(), byte.MinValue, byte.MaxValue);
            return true;
        }

        private static bool TryGetItemAddValue(TUserItem item, int index, out PasValue result)
        {
            result = PasValue.FromInt(item.btValue != null && index < item.btValue.Length
                ? item.btValue[index]
                : 0);
            return true;
        }

        private bool ScheduleCallOut(List<PasValue> args, bool extended)
        {
            if (args.Count < 3 || ScriptHost == null || CurrentPlayer == null)
                return true;

            var npc = args[0].Type == PasValueType.Object
                ? args[0].ObjVal as NormNpc
                : null;
            npc ??= CurrentNpc;
            var delayMs = SecondsToMilliseconds(args[1].AsInt());
            var procedureName = args[2].AsString();
            if (delayMs <= 0 || string.IsNullOrWhiteSpace(procedureName)) return true;

            var scriptPath = ResolveNpcScriptPath(npc);
            if (scriptPath != null)
                ScriptHost.SchedulePlayerCall(scriptPath, procedureName,
                    CurrentPlayer, npc, delayMs, extended);
            return true;
        }

        private static int SecondsToMilliseconds(int seconds)
        {
            if (seconds <= 0) return 0;
            return seconds > int.MaxValue / 1000 ? int.MaxValue : seconds * 1000;
        }

        /// <summary>
        /// `PlayDice` sub_645200 seeds its 10-slot buffer from GROUP-0 `GetV`, not from
        /// the keyed bank:
        ///   0x64522F  33 F6           xor esi, esi        ; i = 0
        ///   0x645234  8D 4E 01        lea ecx, [esi+1]    ; index = i + 1
        ///   0x645237  33 D2           xor edx, edx        ; group  = 0
        ///   0x64523B  E8 A4 9F 09 00  call 0x6DF1E4       ; GetV(0, i+1)
        ///   0x645246  83 FE 0A        cmp esi, 0x0A       ; ten slots, index 1..10
        /// and GetV routes group 0 to the inline region (0x6DF203 `test esi,esi` ->
        /// 0x6DF20F `mov eax,[ebx+eax*4+0x808]`), which C# models as m_ScriptVGroup0.
        /// Reading m_ScriptVVars[1..10] instead can never hit: a keyed entry is filed
        /// under group*1000+index (sub_6E42CC `imul eax,edx,0x3E8 / add eax,ecx`), so a
        /// key below 1000 requires group 0 - and group-0 writes go to the inline array.
        /// </summary>
        private static int PackDiceValues(TPlayObject player, int firstIndex, int count)
        {
            uint packed = 0;
            for (var offset = 0; offset < count; offset++)
            {
                var index = firstIndex + offset;
                // Native seeds from GetV(0, i+1) (0x645237 xor edx,edx / 0x64523B call 0x6DF1E4).
                // Group-0 V lives in the inline table, not the keyed dictionary — go through
                // TryGetScriptVar so this cannot silently read a flat key < 1000.
                var value = player != null && player.TryGetScriptVar('V', 0, index, out var slot)
                    ? slot
                    : 0;
                packed |= (uint)(byte)value << (offset * 8);
            }
            return unchecked((int)packed);
        }

        private static bool RejectUnsupportedNativeApi()
        {
            return false;
        }

        private static bool RejectUnsupportedNativeApi(out PasValue result)
        {
            result = PasValue.Nil;
            return false;
        }

        /// <summary>
        /// Names already reported by <see cref="TraceUnknownPasName"/>, so that a script
        /// calling an unknown API inside a loop or an NPC tick cannot spam the console.
        /// One line per (surface, name) pair for the lifetime of the process.
        /// </summary>
        private static readonly HashSet<string> ReportedUnknownPasNames =
            new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// Observability hook for the PAS dispatch <c>default:</c> arms — i.e. the point at
        /// which a name has fallen through EVERY surface the interpreter is willing to try
        /// and the bridge therefore has no <c>case</c> for it at all. This is the "silent
        /// fall-through class". Historically such a name left no trace whatsoever: no log,
        /// no counter, no audit hook, so the class was invisible to the "127 explicit
        /// rejects" metric. In statement form the interpreter then throws
        /// <c>PasRuntimeException "函数找不到"</c> and aborts the script mid-way (earlier
        /// side-effects already persisted); in expression form it silently yields 0, which
        /// for the guards/getters in this class means fail-open or fail-wrong.
        /// <para>
        /// Called only from the interpreter's TERMINAL not-found sites, never from an
        /// individual surface's <c>default:</c>. The surfaces chain as fallbacks
        /// (<c>CallPlayerFunc</c> then <c>CallPlayerMethod</c>, and object-method before
        /// property-get), so a <c>default:</c> on one surface is a routine miss that the
        /// next surface resolves — logging there would emit a false positive on almost
        /// every legitimate call.
        /// </para>
        /// <para>
        /// Purely observational: it returns void and no caller changes the value it
        /// produced before, so dispatch behaviour is byte-for-byte unchanged. It exists so
        /// this class can never silently reappear.
        /// </para>
        /// </summary>
        internal static void TraceUnknownPasName(string surface, string name)
        {
            var key = surface + "." + (name ?? string.Empty);
            lock (ReportedUnknownPasNames)
            {
                if (!ReportedUnknownPasNames.Add(key))
                    return;
            }
            M2Share.MainOutMessage(
                $"[PasBridge] unknown PAS name '{name}' on surface {surface} " +
                "(no case in PasApiBridge; native may have a registered handler)");
        }

        /// <summary>
        /// Native CreateCampAnimal spawn core (sub_6EB7D8 -> sub_67DA68). Spawns
        /// <paramref name="monNum"/> monsters by name at (<paramref name="monX"/>,
        /// <paramref name="monY"/>) scattered [-Range,+Range] per axis, stopping the batch on
        /// the first spawn failure (native stop-on-fail, no rollback). When
        /// <paramref name="targX"/>/<paramref name="targY"/> != -1 the guard coords are anchored
        /// to the monster home fields (native writes obj+0x454/458/45C/460). Wired to the SAME
        /// standard as the already-LIVE sibling createcampmon; see the createcampanimal case
        /// comment for the identical caveats (camp tag ignored, fame-dummy fallback absent,
        /// RNG parity pending RandSeed cutover). Conservation-capped at 200.
        /// </summary>
        private static void SpawnNativeCampAnimals(Envirnoment environment,
            string monName, int monX, int monY, int monNum, int range,
            int targX, int targY)
        {
            if (environment == null || string.IsNullOrEmpty(monName) || monNum <= 0)
                return;
            monNum = Math.Min(200, monNum);
            var applyGuard = targX != -1 && targY != -1;
            for (var i = 0; i < monNum; i++)
            {
                var sx = (short)monX;
                var sy = (short)monY;
                if (range > 0)
                {
                    sx = (short)(monX - range + M2Share.RandomNumber.Random(range * 2 + 1));
                    sy = (short)(monY - range + M2Share.RandomNumber.Random(range * 2 + 1));
                }
                var monster = M2Share.UserEngine.RegenMonsterByName(environment, sx, sy, monName);
                if (monster == null)
                    break;
                if (applyGuard)
                {
                    monster.m_sHomeMap = environment.sMapName;
                    monster.m_nHomeX = (short)targX;
                    monster.m_nHomeY = (short)targY;
                }
            }
        }

        private static void ClearEnvironmentMonsters(Envirnoment environment,
            bool deleteImmediately)
        {
            if (environment == null || M2Share.ObjectManager == null)
                return;

            foreach (var actor in M2Share.ObjectManager.SnapshotEnvironmentObjects(environment))
            {
                if (actor == null || actor.m_boGhost || actor.m_btRaceServer < 50 ||
                    actor.m_btRaceServer == 158 || actor.GetPoseCreate() != null)
                    continue;

                if (deleteImmediately || actor.m_boDeath)
                {
                    actor.MakeGhost();
                    continue;
                }

                actor.m_boNoItem = true;
                actor.m_LastHiter = null;
                actor.m_TargetCret = null;
                actor.m_WAbil.HP = 0;
            }
        }

        // =====================================================================
        // PLAYER PROPERTY ACCESS (This_Player.xxx)
        // =====================================================================

        public bool GetPlayerProperty(string name, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentPlayer == null) return false;

            switch (name.ToLowerInvariant())
            {
                // Basic attributes
                case "level":           result = PasValue.FromInt(CurrentPlayer.m_Abil.Level); break;
                case "name":            result = PasValue.FromString(CurrentPlayer.m_sCharName ?? ""); break;
                case "mapname":         result = PasValue.FromString(CurrentPlayer.m_sMapName ?? ""); break;
                case "my_x": case "x":  result = PasValue.FromInt(CurrentPlayer.m_nCurrX); break;
                case "my_y": case "y":  result = PasValue.FromInt(CurrentPlayer.m_nCurrY); break;
                case "direction":       result = PasValue.FromInt(CurrentPlayer.m_btDirection); break;

                // HP/MP
                case "hp":              result = PasValue.FromInt(CurrentPlayer.m_WAbil.HP); break;
                case "maxhp": case "max_hp": result = PasValue.FromInt(CurrentPlayer.m_WAbil.MaxHP); break;
                case "mp":              result = PasValue.FromInt(CurrentPlayer.m_WAbil.MP); break;
                case "maxmp": case "max_mp": result = PasValue.FromInt(CurrentPlayer.m_WAbil.MaxMP); break;

                // Combat stats — TAbility has DC/MC/SC/AC/MAC (no "Max" prefix)
                case "dc":              result = PasValue.FromInt(CurrentPlayer.m_WAbil.DC); break;
                case "maxdc":           result = PasValue.FromInt(CurrentPlayer.m_WAbil.DC); break;
                case "mc":              result = PasValue.FromInt(CurrentPlayer.m_WAbil.MC); break;
                case "maxmc":           result = PasValue.FromInt(CurrentPlayer.m_WAbil.MC); break;
                case "sc":              result = PasValue.FromInt(CurrentPlayer.m_WAbil.SC); break;
                case "maxsc":           result = PasValue.FromInt(CurrentPlayer.m_WAbil.SC); break;
                case "ac":              result = PasValue.FromInt(CurrentPlayer.m_WAbil.AC); break;
                case "maxac":           result = PasValue.FromInt(CurrentPlayer.m_WAbil.AC); break;
                case "mac":             result = PasValue.FromInt(CurrentPlayer.m_WAbil.MAC); break;
                case "maxmac":          result = PasValue.FromInt(CurrentPlayer.m_WAbil.MAC); break;
                case "hitrate":         result = PasValue.FromInt(CurrentPlayer.m_btHitPoint); break;
                case "quickrate":       result = PasValue.FromInt(CurrentPlayer.m_btSpeedPoint); break;

                // Money/Currency
                case "goldnum":         result = PasValue.FromInt(CurrentPlayer.m_nGold); break;
                case "ybnum":           result = PasValue.FromInt(CurrentPlayer.m_nGameGold); break;
                case "gamegold":        result = PasValue.FromInt(CurrentPlayer.m_nGameGold); break;
                case "gamepoint":       result = PasValue.FromInt(CurrentPlayer.m_nGamePoint); break;
                case "paymentpoint":    result = PasValue.FromInt(CurrentPlayer.m_nPayMentPoint); break;
                case "mydiamondnum":
                    result = PasValue.FromInt(CurrentPlayer.GetNativeDiamondCount());
                    break;
                case "mylfnum":
                    if (!CurrentPlayer.TryGetNativeLingFuBalance(out var lingFuBalance))
                        return false;
                    result = PasValue.FromInt(lingFuBalance);
                    break;
                case "my_lfnum":
                case "lingfuvalue":
                    // These aliases are not published by the native TPlayer RTTI.
                    return RejectUnsupportedNativeApi(out result);
                case "nicklinfu":
                    result = PasValue.FromInt(CurrentPlayer.m_nNickLinFu);
                    break;
                case "creditpoint":
                    // The native PAS TPlayer type does not publish CreditPoint.
                    return RejectUnsupportedNativeApi(out result);
                case "glorypoint":
                    result = PasValue.FromInt(CurrentPlayer.m_CreditCard.GloryPointValue);
                    break;
                case "guildpoint":
                    // #16 C3: no dedicated native field in this exe (gm-playerattr
                    // idat-verified) -> keep fail-closed (faithful 1:1).
                    return RejectUnsupportedNativeApi(out result);
                case "jiayoupoint":
                    // #16 C2: native ObjPlayer.JiaYouPoint = Self+0xAF0 (Cardinal, read-only).
                    result = PasValue.FromInt((int)CurrentPlayer.m_dwJiaYouPoint);
                    break;
                case "activevalue":     result = PasValue.FromInt(CurrentPlayer.m_nActivePoint); break;

                // Identity
                case "job":             result = PasValue.FromInt(CurrentPlayer.m_btJob); break;
                case "gender":          result = PasValue.FromInt((int)(byte)CurrentPlayer.m_btGender); break;
                case "race":            result = PasValue.FromInt(0); break;
                case "isdead":          result = PasValue.FromBool(CurrentPlayer.m_boDeath); break;

                // Status
                case "mypkpoint":       result = PasValue.FromInt(CurrentPlayer.m_nPkPoint); break;
                case "myexp":           result = PasValue.FromInt(CurrentPlayer.m_Abil.Exp); break;
                // RTTI 已发布属性 MultiTempExpRate = obj+0xBC0（propinfo @0x6AD5EC，
                // Get=Set=FF000BC0，即直接读写字段）。getter 返回**原始整数**：
                // 无 Max(1,..) 下限、无 /100 —— 构造函数 0x6ADA18 写 0，所以 0 是
                // 合法的原生默认值，加下限会把它掩盖掉。消费点 sub_6F7AA4 @0x6F7AA4。
                case "multitempexprate": result = PasValue.FromInt(CurrentPlayer.m_nNativeMultiTempExpRate); break;
                case "dominatelevel":
                    return RejectUnsupportedNativeApi(out result);
                case "freebagnum":      result = PasValue.FromInt(Math.Max(0, BagCapacity.Of(CurrentPlayer) - CurrentPlayer.m_ItemList.Count)); break;
                case "bagitemcount":    result = PasValue.FromInt(CurrentPlayer.m_ItemList.Count()); break;

                // Guild
                case "guildname":       result = PasValue.FromString(CurrentPlayer.m_MyGuild?.sGuildName ?? ""); break;
                case "isguildlord":     result = PasValue.FromBool(CurrentPlayer.m_MyGuild != null &&
                    string.Equals(CurrentPlayer.m_MyGuild.GetChiefName(), CurrentPlayer.m_sCharName, StringComparison.OrdinalIgnoreCase)); break;
                case "guildorder":      result = PasValue.FromInt(CurrentPlayer.m_nGuildRankNo); break;

                // Marriage/Master
                case "peiyouname": case "peiyou_name": result = PasValue.FromString(CurrentPlayer.m_sDearName ?? ""); break;
                case "mastername":      result = PasValue.FromString(CurrentPlayer.m_sMasterName ?? ""); break;

                // Team
                case "isteammember":
                    result = PasValue.FromBool(CurrentPlayer.m_GroupOwner != null &&
                        CurrentPlayer.m_GroupOwner.m_GroupMembers.Contains(CurrentPlayer));
                    break;
                case "isgroupowner":    result = PasValue.FromBool(CurrentPlayer.m_GroupOwner != null && CurrentPlayer.m_GroupOwner == CurrentPlayer); break;

                // Special
                case "strparam":        result = PasValue.FromString(CurrentPlayer.m_sStrParam); break;
                case "intparam":        result = PasValue.FromInt(0); break;
                case "calloutparam":    result = PasValue.FromString(CurrentPlayer.m_sCallOutParam); break;
                case "myshengwan":      result = PasValue.FromInt(CurrentPlayer.m_nShengWan); break;
                case "attackmode":      result = PasValue.FromInt(CurrentPlayer.m_btAttatckMode); break;
                case "normalhide":      result = PasValue.FromBool(CurrentPlayer.m_boGhost); break;
                case "isstudent": case "isastudent": result = PasValue.FromBool(CurrentPlayer.m_boStudent); break;
                case "lucknum":         result = PasValue.FromInt(CurrentPlayer.m_nLuck); break;
                // Hero
                case "havevalidhero":   result = PasValue.FromBool((CurrentPlayer.m_btNativeHeroState & 3) != 0); break;
                case "herolevel":       result = PasValue.FromInt(CurrentPlayer.m_HeroObject?.m_Abil.Level ?? 0); break;
                case "herojob":         result = PasValue.FromInt(CurrentPlayer.m_HeroObject?.m_btJob ?? 0); break;
                case "herogender":      result = PasValue.FromInt(CurrentPlayer.m_HeroObject == null
                    ? 0 : (int)(byte)CurrentPlayer.m_HeroObject.m_btGender); break;

                // Team members
                case "membercount":     result = PasValue.FromInt(CurrentPlayer.m_GroupMembers?.Count ?? 0); break;

                // Target player properties (set via FindPlayerByName/FindPlayer)
                case "targetlevel":     result = PasValue.FromInt(CurrentPlayer.m_TargetPlayer?.m_Abil.Level ?? 0); break;
                case "targetgoldnum":   result = PasValue.FromInt(CurrentPlayer.m_TargetPlayer?.m_nGold ?? 0); break;
                case "targetmapname":   result = PasValue.FromString(CurrentPlayer.m_TargetPlayer?.m_sMapFileName ?? ""); break;
                case "tenyearimpress":
                case "gettrustbywine":
                    return RejectUnsupportedNativeApi(out result);

                // GM / Account / Time properties
                case "gmlevel":             result = PasValue.FromInt(CurrentPlayer.m_btPermission); break;
                case "getcreatetime":       result = PasValue.FromDouble(CurrentPlayer.m_dCreateDate); break;
                case "platlv":
                    // #16 C1: native ObjPlayer.PlatLv = Self+0xB85 (Byte, read/write).
                    result = PasValue.FromInt(CurrentPlayer.m_btPlatLv);
                    break;
                case "myexpquestvalue":
                    return RejectUnsupportedNativeApi(out result);
                case "havetimenum":
                    // Native account-time balance is loaded by the fee-user service.
                    return RejectUnsupportedNativeApi(out result);
                case "getmypositioninguild": result = PasValue.FromInt(
                    CurrentPlayer.m_MyGuild == null ? -1 : CurrentPlayer.m_nGuildRankNo); break;

                // Dynamic rooms are separate native instances, not map-name suffixes.
                case "dynroomidx":
                    result = PasValue.FromInt(CurrentPlayer.m_PEnvir?.IsDynamicRoom == true
                        ? CurrentPlayer.m_PEnvir.DynamicRoomIndex : -1);
                    break;

                case "dynroomname":
                    result = PasValue.FromString(CurrentPlayer.m_PEnvir?.IsDynamicRoom == true
                        ? CurrentPlayer.m_PEnvir.DynamicRoomName : string.Empty);
                    break;

                default:
                    result = PasValue.Nil;
                    return false;
            }
            return true;
        }

        public bool SetPlayerProperty(string name, PasValue value)
        {
            if (CurrentPlayer == null) return false;
            switch (name.ToLowerInvariant())
            {
                case "intparam": /* set context value */ return true;
                case "strparam": CurrentPlayer.m_sStrParam = value.AsString(); return true;
                case "calloutparam": CurrentPlayer.m_sCallOutParam = value.AsString(); return true;
                case "myshengwan": CurrentPlayer.SetShengWan(value.AsInt()); return true;
                case "platlv":
                    // #16 C1: native ObjPlayer.PlatLv = Self+0xB85 (Byte, RW).
                    CurrentPlayer.m_btPlatLv = (byte)value.AsInt();
                    return true;
                case "dominatelevel": return RejectUnsupportedNativeApi();
                // setter 写原始整数到 obj+0xBC0：无 *100、无「<=0 则 100」回退。
                // 也**不得**顺手清计时器 —— +0xBC0 与 +0xBB8/+0xBBC 是两个互相独立
                // 的加成，在 sub_6F7A18 里相加（0x6F7A4C），战神写 +0xBC0 时从不碰
                // +0xBB8，原先的跨字段耦合是凭空发明的。
                case "multitempexprate":
                    CurrentPlayer.m_nNativeMultiTempExpRate = value.AsInt();
                    return true;
                default: return false;
            }
        }

        // =====================================================================
        // NPC PROPERTY ACCESS (This_Npc.xxx)
        // =====================================================================

        public bool GetNpcProperty(string name, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentNpc == null) return false;

            switch (name.ToLowerInvariant())
            {
                case "name":            result = PasValue.FromString(CurrentNpc.m_sCharName ?? ""); break;
                case "mapname":         result = PasValue.FromString(CurrentNpc.m_sMapName ?? ""); break;
                case "my_x": case "x":  result = PasValue.FromInt(CurrentNpc.m_nCurrX); break;
                case "my_y": case "y":  result = PasValue.FromInt(CurrentNpc.m_nCurrY); break;
                case "inputok":         result = PasValue.FromBool(CurrentInputOk); break;
                case "inputstr":        result = PasValue.FromString(CurrentInputStr ?? string.Empty); break;
                case "racetype":        result = PasValue.FromInt(0); break;
                case "getcastleguildname": result = PasValue.FromString(GetCurrentCastle()?.m_sOwnGuild ?? string.Empty); break;
                case "getcastleloadname": result = PasValue.FromString(GetCurrentCastle()?.m_MasterGuild?.GetChiefName() ?? string.Empty); break;
                case "getcastletotalgold": result = PasValue.FromInt(GetCurrentCastle()?.m_nTotalGold ?? 0); break;
                case "getcastletodayincome": result = PasValue.FromInt(GetCurrentCastle()?.m_nTodayIncome ?? 0); break;
                case "getlistofwar": result = PasValue.FromString(GetCurrentCastle()?.GetAttackWarList() ?? string.Empty); break;
                case "getcastledoorstate":
                    var door = GetCurrentCastle()?.m_MainDoor?.BaseObject as CastleDoor;
                    result = PasValue.FromString(
                        NativeCastleHostRuntime.ResolveCastleDoorState(door));
                    break;
                case "repdoorgold": result = PasValue.FromInt(CurrentNpc.m_nPasRepDoorGold); break;
                case "repwallgold": result = PasValue.FromInt(CurrentNpc.m_nPasRepWallGold); break;
                case "hireguardgold": result = PasValue.FromInt(CurrentNpc.m_nPasHireGuardGold); break;
                case "hirearchergold": result = PasValue.FromInt(CurrentNpc.m_nPasHireArcherGold); break;
                default:                result = PasValue.FromInt(0); break;
            }
            return true;
        }

        public bool GetAnimalProperty(string name, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentAnimal == null) return false;

            switch (name.ToLowerInvariant())
            {
                case "level":
                    // TAnimal.Level 0x73AED7 `Level : Word` 只读，与 TPlayer.Level
                    // 0x72AB2F 是两条独立注册。C# 此前只有 GetPlayerProperty 那一半，
                    // This_Animal.Level 会走到「函数找不到」。
                    result = PasValue.FromInt(CurrentAnimal.m_Abil.Level);
                    return true;
                case "name":
                    result = PasValue.FromString(CurrentAnimal.m_sCharName ?? string.Empty);
                    return true;
                case "mapname":
                    result = PasValue.FromString(CurrentAnimal.m_sMapName ?? string.Empty);
                    return true;
                case "mapdesc":
                    result = PasValue.FromString(CurrentAnimal.m_PEnvir?.sMapDesc ??
                        CurrentAnimal.m_sMapName ?? string.Empty);
                    return true;
                case "my_x":
                case "x":
                    result = PasValue.FromInt(CurrentAnimal.m_nCurrX);
                    return true;
                case "my_y":
                case "y":
                    result = PasValue.FromInt(CurrentAnimal.m_nCurrY);
                    return true;
                default:
                    return false;
            }
        }

        private TUserCastle GetCurrentCastle()
        {
            return CurrentNpc?.m_Castle ?? M2Share.CastleManager?.GetCastle(0);
        }

        private bool TryHandleNativeCastleGoldClick(List<PasValue> args,
            bool takeOut)
        {
            if (args.Count != 2
                || args[0].Type != PasValueType.Object
                || args[0].ObjVal is not TPlayObject player
                || args[1].Type != PasValueType.String)
                return false;

            var castle = GetCurrentCastle();
            if (castle == null) return false;

            if (!TryParseNativeDelphiInteger(args[1].AsString(),
                    out var parsedGold))
                parsedGold = 0;
            var gold = parsedGold < 0 ? unchecked(-parsedGold) : parsedGold;
            var response = string.Empty;

            if (gold <= 0 || castle.m_MasterGuild != player.m_MyGuild
                || player.m_nGuildRankNo != 1)
            {
                response = "只有后述行会掌门人才能使用 " + castle.m_sOwnGuild;
            }
            else if (takeOut)
            {
                if (gold > castle.m_nTotalGold)
                {
                    response = "城内没有这么多金币。";
                }
                else if (!player.IncGold(gold))
                {
                    response = "您无法携带更多的金币了。";
                }
                else
                {
                    castle.m_nTotalGold -= gold;
                    player.GoldChanged();
                    if (M2Share.g_boGameLogGold)
                        M2Share.AddGameDataLog("22" + "\t" + player.m_sMapName + "\t" +
                            player.m_nCurrX + "\t" + player.m_nCurrY + "\t" +
                            player.m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" +
                            gold + "\t" + '1' + "\t" + '0');
                }
            }
            else
            {
                const int nativeCastleGoldLimit = 100_000_000;
                var totalGold = unchecked(castle.m_nTotalGold + gold);
                if (totalGold > nativeCastleGoldLimit)
                {
                    response = "你已经到达在城内存放金币的限制了";
                }
                else if (!player.DecGold(gold))
                {
                    response = "你没有那么多金币。";
                }
                else
                {
                    player.GoldChanged();
                    castle.m_nTotalGold = totalGold;
                    if (M2Share.g_boGameLogGold)
                        M2Share.AddGameDataLog("23" + "\t" + player.m_sMapName + "\t" +
                            player.m_nCurrX + "\t" + player.m_nCurrY + "\t" +
                            player.m_sCharName + "\t" + Grobal2.sSTRING_GOLDNAME + "\t" +
                            gold + "\t" + '1' + "\t" + '0');
                }
            }

            player.SendMsg(CurrentNpc, Grobal2.RM_MENU_OK, 0,
                CurrentNpc.ObjectId, 0, 0, response);
            return true;
        }

        public bool SetNpcProperty(string name, PasValue value)
        {
            if (CurrentNpc == null) return false;
            switch (name.ToLowerInvariant())
            {
                case "repdoorgold": CurrentNpc.m_nPasRepDoorGold = Math.Max(0, value.AsInt()); return true;
                case "repwallgold": CurrentNpc.m_nPasRepWallGold = Math.Max(0, value.AsInt()); return true;
                case "hireguardgold": CurrentNpc.m_nPasHireGuardGold = Math.Max(0, value.AsInt()); return true;
                case "hirearchergold": CurrentNpc.m_nPasHireArcherGold = Math.Max(0, value.AsInt()); return true;
                default: return false;
            }
        }

        private bool TryLearnPlayerMagic(string skillName)
        {
            var magicInfo = M2Share.UserEngine.FindMagic(skillName);
            if (magicInfo == null || CurrentPlayer.IsTrainingSkill(magicInfo.wMagicID))
                return false;

            var userMagic = new TUserMagic
            {
                MagicInfo = magicInfo,
                wMagIdx = magicInfo.wMagicID,
                btKey = 0,
                btLevel = Math.Min((byte)1, magicInfo.btTrainLv),
                nTranPoint = 0
            };
            CurrentPlayer.m_MagicList.Add(userMagic);
            if (CurrentPlayer.m_MagicArr != null && userMagic.wMagIdx < CurrentPlayer.m_MagicArr.Length)
                CurrentPlayer.m_MagicArr[userMagic.wMagIdx] = userMagic;
            CurrentPlayer.SendAddMagic(userMagic);
            return true;
        }

        private bool TryAddPlayerSkillExp(string skillName, int experience)
        {
            if (experience <= 0)
                return false;

            TUserMagic userMagic = null;
            foreach (var candidate in CurrentPlayer.m_MagicList)
            {
                if (candidate?.MagicInfo != null &&
                    string.Equals(candidate.MagicInfo.sMagicName, skillName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    userMagic = candidate;
                    break;
                }
            }

            if (userMagic == null)
                return false;

            var isUnionMagic = userMagic.MagicInfo.wMagicID == 69;
            if (isUnionMagic && GetEffectiveScriptMagicLevel(userMagic) >= 99)
                return false;

            var trainResult = TrainScriptMagicNative(CurrentPlayer, userMagic, experience);
            if (isUnionMagic)
            {
                SendNativeUnionSkillProgress(CurrentPlayer, userMagic);
                if (trainResult == 3)
                    CurrentPlayer.RecalcAbilitys();
            }
            return trainResult > 0;
        }

        private static void SendNativeUnionSkillProgress(TPlayObject player,
            TUserMagic userMagic)
        {
            player.SendSocket(BuildNativeUnionSkillProgressHeader(player, userMagic),
                BuildNativeUnionSkillProgressBody(userMagic));
        }

        private static ClientPacket BuildNativeUnionSkillProgressHeader(TPlayObject player,
            TUserMagic userMagic)
        {
            // sub_744E88 pushes the five wire fields in Delphi register order
            // (Param, Tag, Series, Buf, Len) before ecx=nRecog / dx=Ident:
            //   00744ED7  E8 60 36 D8 FF     call 0x4C853C   ; ax = MagicInfo.wMagicID
            //   00744EDC  50                 push eax        ; Param  = wMagicID
            //   00744EDD  6A 00              push 0          ; Tag    = 0
            //   00744EDF  6A 00              push 0          ; Series = 0
            //   00744EE1  8B 45 FC / 50      push [ebp-4]    ; Buf
            //   00744EE5  6A 14              push 0x14       ; Len    = 20
            //   00744EE7  8B CF              mov ecx, edi    ; nRecog = Self
            //   00744EE9  66 BA 45 0B        mov dx, 0xB45   ; 2885
            // 0x4C853C is `mov eax,[eax] / mov ax,[eax+0x10] / ret` = MagicInfo.wMagicID.
            return Grobal2.MakeDefaultMsg(2885, player.ObjectId,
                userMagic.MagicInfo.wMagicID, 0, 0);
        }

        private static byte[] BuildNativeUnionSkillProgressBody(TUserMagic userMagic)
        {
            var body = new byte[20];
            Buffer.BlockCopy(BitConverter.GetBytes((int)userMagic.MagicInfo.wMagicID), 0,
                body, 0, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(GetEffectiveScriptMagicLevel(userMagic)), 0,
                body, 4, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(1), 0, body, 8, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(userMagic.nTranPoint), 0,
                body, 12, sizeof(int));
            Buffer.BlockCopy(BitConverter.GetBytes(GetNativeRequiredTrain(userMagic)), 0,
                body, 16, sizeof(int));
            return body;
        }

        private bool TryLearnSkillByScript(string skillName, bool heroSkill, int initialLevel)
        {
            TBaseObject owner;
            if (heroSkill)
            {
                var hero = CurrentPlayer.m_HeroObject;
                if (hero == null || hero.m_boGhost ||
                    !hero.LearnHeroMagic(skillName))
                    return false;
                owner = hero;
            }
            else
            {
                if (!TryLearnPlayerMagic(skillName))
                    return false;
                owner = CurrentPlayer;
            }

            _ = TrySetScriptMagicLevel(owner, skillName, initialLevel, null);
            return true;
        }

        private bool TryUpgradePlayerMagic(string skillName)
        {
            var magicInfo = M2Share.UserEngine.FindMagic(skillName);
            var userMagic = magicInfo == null
                ? null
                : CurrentPlayer.GetMagicInfo(magicInfo.wMagicID);
            if (userMagic == null)
                return false;

            var currentLevel = Math.Min(userMagic.btLevel, magicInfo.btTrainLv);
            if (currentLevel >= magicInfo.btTrainLv)
                return false;

            userMagic.btLevel = (byte)(currentLevel + 1);
            CurrentPlayer.RecalcAbilitys();
            QueueScriptMagicLevelUpdate(CurrentPlayer, userMagic);
            return true;
        }

        private static bool TrySetScriptMagicLevel(TBaseObject owner, string skillName,
            int requestedLevel, int? tranPoint)
        {
            var magicInfo = owner is HeroObject
                ? M2Share.UserEngine.FindHeroMagic(skillName)
                : M2Share.UserEngine.FindMagic(skillName);
            var userMagic = magicInfo == null ? null : owner.GetMagicInfo(magicInfo.wMagicID);
            if (userMagic == null)
                return false;

            userMagic.btLevel = Math.Min(unchecked((byte)requestedLevel), magicInfo.btTrainLv);
            if (tranPoint.GetValueOrDefault() > 0)
                _ = TrainScriptMagicNative(owner, userMagic, tranPoint.Value);
            owner.RecalcAbilitys();
            QueueScriptMagicLevelUpdate(owner, userMagic);
            return true;
        }

        private static void QueueScriptMagicLevelUpdate(TBaseObject owner, TUserMagic userMagic)
        {
            var effectiveLevel = GetEffectiveScriptMagicLevel(userMagic);
            owner.SendMsg(owner, Grobal2.RM_MAGIC_LVEXP,
                userMagic.MagicInfo.wMagicID, effectiveLevel,
                userMagic.nTranPoint, GetNativeRequiredTrain(userMagic), string.Empty);
        }

        private static int TrainScriptMagicNative(TBaseObject owner, TUserMagic userMagic,
            int experience)
        {
            if (owner?.m_Abil == null || userMagic?.MagicInfo == null ||
                owner.m_Abil.Level < unchecked((ushort)GetNativeRequiredActorLevel(userMagic)))
                return 0;

            var result = 1;
            userMagic.nTranPoint = unchecked(userMagic.nTranPoint + experience);
            var requiredTrain = GetNativeRequiredTrain(userMagic);
            if (requiredTrain == -1)
                return result;

            while ((uint)requiredTrain <= (uint)userMagic.nTranPoint)
            {
                userMagic.nTranPoint = unchecked(userMagic.nTranPoint - requiredTrain);
                result = 2;
                if (userMagic.btLevel >= userMagic.MagicInfo.btTrainLv)
                    break;

                userMagic.btLevel++;
                requiredTrain = GetNativeRequiredTrain(userMagic);
                result = 3;
            }
            return result;
        }

        private static int GetNativeRequiredActorLevel(TUserMagic userMagic)
        {
            var magicInfo = userMagic.MagicInfo;
            var level = userMagic.btLevel;
            var useInitialLevel = magicInfo.wMagicID == 62 || magicInfo.wMagicID == 69 ||
                                  magicInfo.wMagicID == 106 || magicInfo.wMagicID == 110 ||
                                  magicInfo.wMagicID == 112;
            var index = useInitialLevel ? 0 : level;
            return index < 3 && magicInfo.TrainLevel != null && index < magicInfo.TrainLevel.Length
                ? magicInfo.TrainLevel[index]
                : -1;
        }

        private static int GetNativeRequiredTrain(TUserMagic userMagic)
        {
            var magicInfo = userMagic.MagicInfo;
            var level = userMagic.btLevel;
            switch (magicInfo.wMagicID)
            {
                case >= 129 and <= 131:
                    return -1;
                case 62:
                    return level < 100 ? 25 * (level + 1) * (level + 1) : -1;
                case 112:
                    return level < 100
                        ? level + 8 * level * level + 30 * level * level * level + 500
                        : -1;
                case 60:
                case 61:
                    return 0;
                case 69:
                    return level < 100 ? 200 * level + 300 : -1;
                default:
                    return level < 3 && magicInfo.MaxTrain != null && level < magicInfo.MaxTrain.Length
                        ? magicInfo.MaxTrain[level]
                        : -1;
            }
        }

        private static int GetEffectiveScriptMagicLevel(TUserMagic userMagic)
        {
            return Math.Min(userMagic.btLevel, userMagic.MagicInfo.btTrainLv);
        }

        private bool IsCurrentGroupOwner()
        {
            return CurrentPlayer?.m_GroupOwner != null
                && ReferenceEquals(CurrentPlayer.m_GroupOwner, CurrentPlayer);
        }

        private void MoveCurrentGroupToMap(string mapName)
        {
            if (!IsCurrentGroupOwner()
                || CurrentPlayer.m_PEnvir == null
                || CurrentPlayer.m_GroupMembers == null
                || string.IsNullOrEmpty(mapName))
                return;

            var targetEnvironment = M2Share.MapManager?.FindMap(mapName);
            if (targetEnvironment == null)
                return;

            var oldEnvironment = CurrentPlayer.m_PEnvir;
            var y = (short)M2Share.RandomNumber.Random(targetEnvironment.wHeight);
            var x = (short)M2Share.RandomNumber.Random(targetEnvironment.wWidth);
            CurrentPlayer.SpaceMove(targetEnvironment.sMapName, x, y, 0);

            for (var i = 0; i < Grobal2.GROUPMAX; i++)
            {
                if (i >= CurrentPlayer.m_GroupMembers.Count)
                    break;

                var member = CurrentPlayer.m_GroupMembers[i];
                if (member == null
                    || member.m_btRaceServer != Grobal2.RC_PLAYOBJECT
                    || ReferenceEquals(member, CurrentPlayer)
                    || member.m_boDeath
                    || member.m_boGhost
                    || !ReferenceEquals(member.m_PEnvir, oldEnvironment))
                    continue;

                var randomY = M2Share.RandomNumber.Random(9);
                var randomX = M2Share.RandomNumber.Random(9);
                x = unchecked((short)(CurrentPlayer.m_nCurrX + 4 - randomX));
                y = unchecked((short)(CurrentPlayer.m_nCurrY + 4 - randomY));
                member.SpaceMove(targetEnvironment.sMapName, x, y, 0);
            }
        }

        private int CountCurrentGroupOnMap(string mapName)
        {
            if (!IsCurrentGroupOwner() || CurrentPlayer.m_GroupMembers == null)
                return 0;

            var result = 0;
            var memberCount = Math.Min(CurrentPlayer.m_GroupMembers.Count,
                Grobal2.GROUPMAX);
            for (var i = 0; i < memberCount; i++)
            {
                var member = CurrentPlayer.m_GroupMembers[i];
                if (member != null
                    && !member.m_boDeath
                    && !member.m_boGhost
                    && member.m_PEnvir != null
                    && string.Equals(member.m_PEnvir.sMapName, mapName,
                        StringComparison.Ordinal))
                    result++;
            }
            return result;
        }

        private const int StudentSlotCount = 5;

        /// <summary>
        /// 战神 0x6CB003 `mov edx, 0xC350` -- the flat 50,000 gold the student
        /// pays to walk out of the school via PAS `NpcLeaveTec` (sub_6CAFF0).
        /// Hard-coded in the binary, not read from any config cell.
        /// </summary>
        private const int NativeLeaveTecGoldCost = 0xC350;
        private const uint NativeMarryRequestWindowMs = 300000u;
        private const string NativeMarryRingName = "求婚戒指";

        private static bool IsNativeMarried(TPlayObject player)
        {
            return player != null && player.m_boMarried;
        }

        private static bool TryGetNativeMarryRingSlot(TPlayObject player,
            out int slot)
        {
            slot = -1;
            if (player?.m_UseItems == null) return false;

            foreach (var ringSlot in new[] { Grobal2.U_RINGL, Grobal2.U_RINGR })
            {
                if (ringSlot < 0 || ringSlot >= player.m_UseItems.Length)
                    continue;
                var item = player.m_UseItems[ringSlot];
                if (item != null && item.wIndex > 0
                    && string.Equals(
                        M2Share.UserEngine?.GetStdItemName(item.wIndex),
                        NativeMarryRingName, StringComparison.Ordinal))
                {
                    slot = ringSlot;
                    break;
                }
            }

            return slot >= 0
                && player.m_btGender == PlayGender.Man
                && !IsNativeMarried(player)
                && player.m_nShengWan >= 5;
        }

        private static bool ConsumeNativeMarryRing(TPlayObject player,
            int slot)
        {
            if (player?.m_UseItems == null || slot < 0
                || slot >= player.m_UseItems.Length)
                return false;
            var item = player.m_UseItems[slot];
            if (item == null || item.wIndex == 0) return false;

            var itemName = M2Share.UserEngine?.GetStdItemName(item.wIndex)
                ?? NativeMarryRingName;
            player.m_UseItems[slot] = null;
            player.RecalcAbilitys();
            player.SendMsg(player, Grobal2.RM_ABILITY, 0, 0, 0, 0,
                string.Empty);
            player.SendDelItems(item);
            M2Share.AddGameDataLog(string.Join('\t', 10,
                player.m_sMapName, player.m_nCurrX, player.m_nCurrY,
                player.m_sCharName, itemName, unchecked((uint)item.MakeIndex),
                1, "求婚收取"));
            player.Dispose(item);
            return true;
        }

        private void RequestNativeMarry(TPlayObject requester,
            string targetName)
        {
            if (requester == null || string.IsNullOrEmpty(targetName)) return;
            if (!TryGetNativeMarryRingSlot(requester, out var ringSlot))
            {
                requester.SysMsg(
                    "[失败] 求婚者必须满足：未婚，男性，至少5点声望，佩带求婚戒指",
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            var requestTick = HUtil32.GetTickCount();
            if (requester.m_boStartMarry
                && (uint)(requestTick - requester.m_dwMarryRequestTime)
                <= NativeMarryRequestWindowMs)
            {
                requester.SysMsg("[失败] 你刚求过婚,请稍后再试...",
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            var target = M2Share.UserEngine?.GetPlayObject(targetName);
            if (target == null || target.m_btRaceServer != Grobal2.RC_PLAYOBJECT
                || target.m_boGhost || !target.m_boAllowMarry)
            {
                requester.SysMsg(
                    "[失败] 对方不在有效范围内或对方设置不接受任何求婚！",
                    MsgColor.Green, MsgType.Hint);
                return;
            }
            if (target.m_btGender != PlayGender.WoMan || IsNativeMarried(target))
            {
                requester.SysMsg("[失败] 对方是男性或已婚！",
                    MsgColor.Green, MsgType.Hint);
                return;
            }

            // Native 0x641F5E re-reads the marriage flag before this busy branch.
            // Preserve that ordering even though it is unreachable on this game loop.
            if (IsNativeMarried(target)
                && (!target.m_boStartMarry
                    || (uint)(requestTick - target.m_dwMarryRequestTime)
                    <= NativeMarryRequestWindowMs))
            {
                requester.SysMsg("[失败] 对方正在处理他人求婚",
                    MsgColor.Green, MsgType.Hint);
                return;
            }
            if (!ConsumeNativeMarryRing(requester, ringSlot)) return;

            SendShiMenNpcDialog(target,
                requester.m_sCharName
                + " 向你求婚，请选择接受或拒绝!\\ \\<接受/@agreemarry>\\ \\<拒绝/@dismarry>");
            requester.m_boStartMarry = true;
            requester.m_PoseBaseObject = target;
            requester.m_dwMarryRequestTime = requestTick;
            target.m_boStartMarry = true;
            target.m_PoseBaseObject = requester;
            target.m_dwMarryRequestTime = requestTick;
            requester.SysMsg("[成功]你的求婚请求已发出，正等候对方处理..",
                MsgColor.Green, MsgType.Hint);
        }

        private static void DisAgreeNativeMarry(TPlayObject player)
        {
            if (player == null || !player.m_boStartMarry) return;
            var now = HUtil32.GetTickCount();
            if ((uint)(now - player.m_dwMarryRequestTime)
                >= NativeMarryRequestWindowMs)
                return;

            player.m_boStartMarry = false;
            player.m_dwMarryRequestTime = 0;
            if (player.m_PoseBaseObject is TPlayObject peer
                && ReferenceEquals(peer.m_PoseBaseObject, player))
            {
                peer.m_boStartMarry = false;
                peer.m_dwMarryRequestTime = 0;
                peer.m_PoseBaseObject = null;
            }
            player.m_PoseBaseObject = null;
        }

        private static void CloseNativeMarryDialog(TPlayObject player,
            TBaseObject npc)
        {
            if (player != null && npc != null)
                player.SendMsg(npc, Grobal2.RM_MERCHANTDLGCLOSE, 0,
                    npc.ObjectId, 0, 0, string.Empty);
        }

        private static void AgreeNativeMarry(TPlayObject accepter,
            TBaseObject npc)
        {
            var now = HUtil32.GetTickCount();
            var peer = accepter?.m_PoseBaseObject as TPlayObject;
            var accepted = accepter != null
                && accepter.m_boAllowMarry
                && !accepter.m_boMarried
                && accepter.m_boStartMarry
                && accepter.m_btGender == PlayGender.WoMan
                && peer != null
                && (uint)(now - accepter.m_dwMarryRequestTime)
                    < NativeMarryRequestWindowMs
                && !peer.m_boMarried
                && peer.m_boStartMarry
                && peer.m_btGender == PlayGender.Man
                && ReferenceEquals(peer.m_PoseBaseObject, accepter)
                && !peer.m_boDeath;

            if (accepted)
            {
                accepter.m_boMarried = true;
                accepter.m_sDearName = peer.m_sCharName;
                accepter.m_DearHuman = peer;
                accepter.m_boStartMarry = false;
                accepter.m_dwMarryRequestTime = 0;

                peer.m_boMarried = true;
                peer.m_sDearName = accepter.m_sCharName;
                peer.m_DearHuman = accepter;
                peer.m_boStartMarry = false;
                peer.m_dwMarryRequestTime = 0;

                accepter.RefShowName();
                peer.RefShowName();
                M2Share.UserEngine?.SendBroadCastMsg(
                    $"[月老]恭喜: {peer.m_sCharName} 与 "
                    + $"{accepter.m_sCharName} 喜结良缘，祝愿他们白头偕老！",
                    MsgType.System);
                accepter.SendMsg(accepter, Grobal2.RM_MASTERRELATION, 0,
                    6, 0, 0, peer.m_sCharName);
                peer.SendMsg(peer, Grobal2.RM_MASTERRELATION, 0,
                    6, 0, 0, accepter.m_sCharName);
            }
            else if (accepter != null)
            {
                accepter.m_boStartMarry = false;
                accepter.m_PoseBaseObject = null;
                accepter.m_dwMarryRequestTime = 0;
                accepter.SysMsg("对方已经离线或请求已超时失效",
                    MsgColor.Green, MsgType.Hint);
            }

            CloseNativeMarryDialog(accepter, npc);
        }

        private static bool TryClearOfflineSpouseRelation(
            TPlayObject player, string spouseName)
        {
            var dataServer = M2Share.DataServer;
            if (player == null || dataServer == null)
                return false;
            if (!NativeMasterRelationFrameCodec.TryEncodeMarriageClear(
                    player.m_sUserID, player.m_sCharName, spouseName,
                    out var frame, out var error))
            {
                M2Share.ErrorMessage(
                    $"[Marry] 离线离婚编码失败 {player.m_sCharName}/"
                    + $"{spouseName}: {error}");
                return false;
            }

            var queryId = dataServer.NextQueryId();
            var outer = new ServerMessagePacket(
                NativeMasterRelationFrameCodec.RequestCommand, 0, 0, 0, 0);
            if (dataServer.SendRawRequest(queryId, outer, frame))
                return true;
            M2Share.ErrorMessage(
                $"[Marry] DBServer未连接，离线离婚未发送: "
                + $"{player.m_sCharName}/{spouseName}");
            return false;
        }

        private static void DivorceNativeMarry(TPlayObject player)
        {
            var spouseName = player.m_sDearName ?? string.Empty;
            if (!string.IsNullOrEmpty(spouseName))
            {
                var spouse = M2Share.UserEngine?.GetPlayObject(spouseName);
                if (spouse != null)
                {
                    if (string.Equals(spouse.m_sDearName,
                            player.m_sCharName, StringComparison.Ordinal))
                    {
                        spouse.m_boMarried = false;
                        spouse.m_sDearName = string.Empty;
                        spouse.m_PoseBaseObject = null;
                        spouse.m_DearHuman = null;
                        spouse.RefShowName();
                        spouse.SendMsg(spouse, Grobal2.RM_MASTERRELATION, 0,
                            7, 0, 0, player.m_sCharName);
                    }
                }
                else
                {
                    var serverIndex = 0;
                    if (M2Share.UserEngine != null
                        && M2Share.UserEngine.FindOtherServerUser(
                            spouseName, ref serverIndex))
                    {
                        M2Share.UserEngine.SendServerGroupMsg(
                            Grobal2.ISM_DIVORCE,
                            serverIndex, spouseName);
                    }
                    else
                    {
                        TryClearOfflineSpouseRelation(player, spouseName);
                    }
                }
            }

            player.SendMsg(player, Grobal2.RM_MASTERRELATION, 0,
                7, 0, 0, spouseName);
            player.m_sDearName = string.Empty;
            player.m_boMarried = false;
            player.m_PoseBaseObject = null;
            player.m_DearHuman = null;
            player.RefShowName();
        }

        private static void NpcDivNativeMarry(TPlayObject player,
            TBaseObject npc)
        {
            if (player != null && player.m_boMarried
                && player.DecGold(1_000_000))
            {
                player.GoldChanged();
                DivorceNativeMarry(player);
            }
            else if (player != null)
            {
                player.SysMsg("你无配偶或所携带的金币不够，不能离婚!",
                    MsgColor.Green, MsgType.Hint);
            }

            CloseNativeMarryDialog(player, npc);
        }

        private static bool IsMasterRequestCoolingDown(int now, int requestTime)
        {
            return (uint)(now - requestTime) <= 300000u;
        }

        private static void EnsureStudentSlots(TPlayObject player)
        {
            if (player.m_sStudentNames == null)
                player.m_sStudentNames = new string[StudentSlotCount];
            else if (player.m_sStudentNames.Length != StudentSlotCount)
                Array.Resize(ref player.m_sStudentNames, StudentSlotCount);

            for (var i = 0; i < StudentSlotCount; i++)
                player.m_sStudentNames[i] ??= string.Empty;
        }

        private static int FindStudentSlot(TPlayObject master, string studentName)
        {
            EnsureStudentSlots(master);
            for (var i = 0; i < StudentSlotCount; i++)
            {
                if (string.Equals(master.m_sStudentNames[i], studentName,
                        StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private static int FindEmptyStudentSlot(TPlayObject master)
        {
            EnsureStudentSlots(master);
            for (var i = 0; i < StudentSlotCount; i++)
            {
                if (string.IsNullOrEmpty(master.m_sStudentNames[i]))
                    return i;
            }
            return -1;
        }

        private static void ClearStudentRelation(TPlayObject student)
        {
            student.m_boStudent = false;
            student.m_btStudentOrder = 0;
            student.m_sMasterName = string.Empty;
            student.m_MasterHuman = null;
            student.m_MasterRequestTarget = null;
            student.m_dwMasterRequestTime = 0;
        }

        private static void ClearStudentSlot(TPlayObject master, int slot)
        {
            EnsureStudentSlots(master);
            if (slot < 0 || slot >= StudentSlotCount
                || string.IsNullOrEmpty(master.m_sStudentNames[slot]))
                return;
            master.m_sStudentNames[slot] = string.Empty;
            master.m_nStudentCount = Math.Max(0, master.m_nStudentCount - 1);
        }

        private static void SaveShiMenPlayer(TPlayObject player)
        {
            if (player != null && M2Share.UserEngine != null)
                M2Share.UserEngine.SaveHumanRcd(player);
        }

        private static bool TryClearOfflineStudentRelation(
            TPlayObject master, string studentName)
        {
            var dataServer = M2Share.DataServer;
            if (master == null || dataServer == null)
                return false;
            if (!NativeMasterRelationFrameCodec.TryEncodeClear(
                    master.m_sUserID, master.m_sCharName, studentName,
                    out var frame, out var error))
            {
                M2Share.ErrorMessage(
                    $"[ShiMen] 离线逐出编码失败 {master.m_sCharName}/" +
                    $"{studentName}: {error}");
                return false;
            }

            var queryId = dataServer.NextQueryId();
            var outer = new ServerMessagePacket(
                NativeMasterRelationFrameCodec.RequestCommand, 0, 0, 0, 0);
            if (dataServer.SendRawRequest(queryId, outer, frame))
                return true;
            M2Share.ErrorMessage(
                $"[ShiMen] DBServer未连接，离线逐出未发送: " +
                $"{master.m_sCharName}/{studentName}");
            return false;
        }

        private void SendShiMenNpcDialog(TPlayObject player, string dialog)
        {
            if (CurrentNpc == null || player == null) return;
            var body = HUtil32.GetBytes(
                CurrentNpc.m_sCharName + "/" + (dialog ?? string.Empty));
            var message = Grobal2.MakeDefaultMsg(Grobal2.SM_MERCHANTSAY,
                CurrentNpc.ObjectId, 0, 0, 1);
            player.SendSocket(message, body);
        }

        private void CloseShiMenDialog(TPlayObject player)
        {
            if (CurrentNpc != null && player != null)
                player.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTDLGCLOSE, 0,
                    CurrentNpc.ObjectId, 0, 0, string.Empty);
        }

        private static string BuildKaiChuDialog(TPlayObject master)
        {
            EnsureStudentSlots(master);
            var dialog = "请选择你要开除的徒弟";
            for (var i = 0; i < StudentSlotCount; i++)
            {
                var studentName = master.m_sStudentNames[i];
                if (!string.IsNullOrEmpty(studentName))
                    dialog += $"\\<{studentName}/@kaichu_M{i}>";
            }
            return dialog;
        }

        private static string BuildRequestBaiShiDialog(string applicantName)
        {
            return $"{applicantName} 想拜你为师，请选择接受或拒绝!\\ \\<接受/@agrbaishi>\\ \\<拒绝/@disbaishi>";
        }

        private bool TryAgreeBaiShi()
        {
            var master = CurrentPlayer;
            var student = master?.m_MasterRequestTarget;
            var valid = master != null
                && master.m_boRequestMaster
                && student != null
                && master.m_Abil.Level >= M2Share.g_Config.nMinMasterLevel
                && (uint)(HUtil32.GetTickCount()
                    - master.m_dwMasterRequestTime) < 300000u
                && master.m_nStudentCount < StudentSlotCount
                && student.m_btRaceServer == Grobal2.RC_PLAYOBJECT
                && !student.m_boGhost
                && ReferenceEquals(student.m_MasterRequestTarget, master)
                && student.m_boRequestMaster
                && !student.m_boStudent;
            var studentSlot = valid ? FindEmptyStudentSlot(master) : -1;
            if (studentSlot < 0)
            {
                if (master != null)
                {
                    master.m_boRequestMaster = false;
                    master.m_MasterRequestTarget = null;
                    master.m_dwMasterRequestTime = 0;
                    master.SysMsg("对方已经离线或请求已超时失效",
                        MsgColor.Green, MsgType.Hint);
                    CloseShiMenDialog(master);
                }
                return false;
            }

            student.m_boRequestMaster = false;
            student.m_boStudent = true;
            student.m_btStudentOrder = (byte)(studentSlot + 1);
            student.m_sMasterName = master.m_sCharName;
            student.m_MasterHuman = master;
            student.SendMsg(student, Grobal2.RM_MASTERRELATION, 0, 8,
                0, 0, master.m_sCharName);
            student.m_dwMasterRequestTime = 0;
            SaveShiMenPlayer(student);

            master.m_boMaster = true;
            master.m_boRequestMaster = false;
            master.m_dwMasterRequestTime = 0;
            master.m_sStudentNames[studentSlot] = student.m_sCharName;
            master.m_nStudentCount++;
            master.m_MasterRequestTarget = null;
            M2Share.UserEngine?.SendBroadCastMsg(
                $"恭喜：{master.m_sCharName} 新收入弟子 {student.m_sCharName}",
                MsgType.Notice);
            CloseShiMenDialog(master);
            return true;
        }

        private static void MoveShiMenPlayerNear(TPlayObject player,
            TPlayObject anchor)
        {
            if (player == null || anchor?.m_PEnvir == null) return;
            var randomY = M2Share.RandomNumber.Random(9);
            var randomX = M2Share.RandomNumber.Random(9);
            var x = unchecked((short)(anchor.m_nCurrX + 4 - randomX));
            var y = unchecked((short)(anchor.m_nCurrY + 4 - randomY));
            player.SpaceMove(anchor.m_PEnvir.sMapName, x, y, 0);
        }

        private static void MoveShiMenMasterAndStudents(TPlayObject master,
            string mapName)
        {
            if (master == null || master.m_PEnvir == null
                || master.m_nStudentCount < 1
                || master.m_nStudentCount > StudentSlotCount)
                return;

            var oldEnvironment = master.m_PEnvir;
            master.MapRandomMove(mapName, 0);
            EnsureStudentSlots(master);
            for (var i = 0; i < StudentSlotCount; i++)
            {
                var studentName = master.m_sStudentNames[i];
                if (string.IsNullOrEmpty(studentName)) continue;
                var student = M2Share.UserEngine?.GetPlayObjectEx(studentName);
                if (student == null || ReferenceEquals(student, master)
                    || student.m_boDeath
                    || !ReferenceEquals(student.m_PEnvir, oldEnvironment))
                    continue;
                MoveShiMenPlayerNear(student, master);
            }
        }

        private void MoveShiMenToMap(string mapName, int mode)
        {
            if (CurrentPlayer == null) return;
            var targetEnvironment = M2Share.MapManager?.FindMap(mapName);
            if (targetEnvironment == null) return;
            mapName = targetEnvironment.sMapName;
            if (mode == 1)
            {
                MoveShiMenMasterAndStudents(CurrentPlayer, mapName);
                return;
            }

            if ((mode != 2 && mode != 3) || !CurrentPlayer.m_boStudent
                || string.IsNullOrEmpty(CurrentPlayer.m_sMasterName)
                || CurrentPlayer.m_PEnvir == null)
                return;

            var oldEnvironment = CurrentPlayer.m_PEnvir;
            var master = M2Share.UserEngine?.GetPlayObjectEx(
                CurrentPlayer.m_sMasterName);
            if (master == null || ReferenceEquals(master, CurrentPlayer)
                || master.m_boDeath
                || !ReferenceEquals(master.m_PEnvir, oldEnvironment))
                return;

            if (mode == 2)
            {
                CurrentPlayer.MapRandomMove(mapName, 0);
                MoveShiMenPlayerNear(master, CurrentPlayer);
            }
            else
            {
                MoveShiMenMasterAndStudents(master, mapName);
            }
        }

        // =====================================================================
        // PLAYER METHOD CALLS (This_Player.xxx) - 50+ methods
        // =====================================================================

        public bool CallPlayerMethod(string method, List<PasValue> args)
        {
            if (CurrentPlayer == null) return false;

            switch (method.ToLowerInvariant())
            {
                // === Item Operations ===
                case "give":
                    if (args.Count >= 2)
                        _ = TryNativeGive(args[0].AsString(), args[1].AsInt(), false, true);
                    return true;

                case "bindgive":
                    if (args.Count >= 2)
                        _ = TryNativeGive(args[0].AsString(), args[1].AsInt(), true, false);
                    return true;

                case "loopgive":
                    _ = TryNativeLoopGive(args);
                    return true;

                case "givetimeoutitem":
                    // GiveTimeOutItem(itemName, endTime, iNum, bBind)
                    if (args.Count >= 4)
                    {
                        var toName = args[0].AsString(); var toNum = args[1].AsInt();
                        var toBind = args.Count >= 4 && args[3].AsInt() != 0;
                        if (toBind)
                            CurrentNpc.GotoLable_GiveItem(CurrentPlayer, toName, toNum);
                        else if (CurrentNpc != null)
                            CurrentNpc.GotoLable_GiveItem(CurrentPlayer, toName, toNum);
                    }
                    return true;

                case "givebinditem":
                    if (args.Count >= 2)
                        _ = TryNativeGive(args[0].AsString(), args[1].AsInt(), true, false);
                    return true;

                case "take":
                    if (args.Count < 2) return true;
                    TakeItems(args[0].AsString(), args[1].AsInt());
                    return true;

                case "takeexpand":
                    // TakeExpand(itemName, count, iParam) = sub_6DF7D4 -> sub_6DF7E8.
                    // iParam is the TRI-STATE LOCK FILTER (0=all, 1=locked-only,
                    // 2=unlocked-only), gating on sub_784710 = word[item+0x34]; see the
                    // ladder cited on TakeItemsCore. It is NOT "include equipment":
                    // sub_6DF7E8 walks [player+0x508] (m_ItemList) only and never reads
                    // m_UseItems, so worn gear is never removed. Absent 3rd arg -> 0,
                    // matching the `push 0` that `Take` (0x6DFA43) uses.
                    if (args.Count < 2) return true;
                    {
                        var itemName = args[0].AsString();
                        var count = args[1].AsInt();
                        var iParam = args.Count >= 3 ? args[2].AsInt() : NativeTakeFilterAll;
                        TakeItemsEx(itemName, count, iParam);
                    }
                    return true;

                case "delallthisitem":
                    // DelAllThisItem = sub_7409D4 -> sub_740A00: its OWN single descending
                    // pass over [player+0x508] that removes EVERY slot whose std-item index
                    // matches (0x740A62 movzx eax,word[[ebx+0x1c]] / 0x740A65 cmp), with no
                    // count, no lock filter and no Dura arithmetic — a drained-stack delete
                    // is the same whole-entry delete as a single item (0x740A75 sub_424B30 /
                    // 0x740A83 [vtbl+0x268] / 0x740AC7 sub_404690). It is NOT a `Take` loop,
                    // so it must not inherit Take's all-or-nothing pre-count.
                    if (args.Count >= 1) DelAllThisItem(args[0].AsString());
                    return true;

                case "takebodyequipbyname":
                    // Remove equipped item by name
                    if (args.Count >= 1)
                    {
                        var eqName = args[0].AsString();
                        for (int i = 0; i < CurrentPlayer.m_UseItems.Length; i++)
                        {
                            var useItem = CurrentPlayer.m_UseItems[i];
                            if (useItem != null && useItem.wIndex > 0)
                            {
                                var stdName = M2Share.UserEngine.GetStdItemName(useItem.wIndex);
                                if (string.Equals(stdName, eqName, StringComparison.OrdinalIgnoreCase))
                                {
                                    CurrentPlayer.SendDelItems(useItem);
                                    useItem.wIndex = 0;
                                    CurrentPlayer.RecalcAbilitys();
                                    break;
                                }
                            }
                        }
                    }
                    return true;

                case "takebodyequipbypos":
                    // Remove equipped item by position (0-15)
                    if (args.Count >= 1)
                    {
                        var pos = args[0].AsInt();
                        if (pos >= 0 && pos < CurrentPlayer.m_UseItems.Length)
                        {
                            var useItem = CurrentPlayer.m_UseItems[pos];
                            if (useItem != null && useItem.wIndex > 0)
                            {
                                CurrentPlayer.SendDelItems(useItem);
                                useItem.wIndex = 0;
                                CurrentPlayer.RecalcAbilitys();
                            }
                        }
                    }
                    return true;

                case "confiscatebodyitem":
                    if (args.Count >= 1)
                        NativeConfiscateBodyItem.Execute(CurrentPlayer,
                            args[0].AsInt(), CurrentNpc);
                    return true;

                case "getbagitemcount":
                case "getbagitemcountex":
                    // Check for yanshen tunnel command and execute side effects (void variant)
                    if (args.Count >= 1) { TryExecuteTunnelCommand(args[0].AsString(), method, out _); }
                    return true;

                case "notifyclientcommititem":
                    if (args.Count >= 1)
                    {
                        var commitResult = args[0].AsInt();
                        var message = args.Count >= 2 ? args[1].AsString() : string.Empty;
                        CurrentPlayer.SendDefMessage((short)Grobal2.SM_COMMIT_ITEM,
                            0, commitResult, 0, 0, message);
                    }
                    return true;

                case "notifyclientupditem":
                    // Notify client to update item at bag index (same as commititem for bag items)
                    if (args.Count >= 1)
                    {
                        var upIdx = args[0].AsInt();
                        if (upIdx >= 0 && upIdx < CurrentPlayer.m_ItemList.Count)
                        {
                            var upItem = CurrentPlayer.m_ItemList[upIdx];
                            if (upItem != null && upItem.wIndex > 0)
                                CurrentPlayer.SendUpdateItem(upItem);
                        }
                    }
                    return true;

                case "newfullmailex":
                    if (CurrentPlayer == null || args.Count != 7) return false;
                    _ = new global::GameSvr.Services.MailService().NewFullMailEx(
                        CurrentPlayer, args[0].AsString(), args[1].AsString(),
                        args[2].AsInt(), args[3].AsInt(), args[4].AsInt(),
                        args[5].AsString(), args[6].AsString());
                    return true;

                case "getstoragespacecount":
                    return true;

                case "getaroundmonnum":
                    // GetAroundMonNum([range]) — count monsters within range of player
                    if (CurrentPlayer != null && CurrentPlayer.m_PEnvir != null)
                    {
                        int range = args.Count >= 1 ? args[0].AsInt() : 5;
                        int count = 0;
                        var objList = new List<TBaseObject>();
                        if (CurrentPlayer.m_PEnvir.GetMapBaseObjects((short)CurrentPlayer.m_nCurrX, (short)CurrentPlayer.m_nCurrY, range, objList))
                        {
                            foreach (var obj in objList)
                            {
                                if (obj != null && obj.m_btRaceServer >= Grobal2.RC_MONSTER && !obj.m_boDeath)
                                    count++;
                            }
                        }
                        M2Share.MainOutMessage($"[PasBridge] GetAroundMonNum: {count} monsters in range {range}");
                    }
                    return true;

                case "getmember":
                    // GetMember(index) — get group member name by index
                    if (CurrentPlayer != null && CurrentPlayer.m_GroupMembers != null)
                    {
                        int idx = args.Count >= 1 ? args[0].AsInt() : 0;
                        if (idx >= 0 && idx < CurrentPlayer.m_GroupMembers.Count)
                            M2Share.MainOutMessage($"[PasBridge] GetMember({idx}): {CurrentPlayer.m_GroupMembers[idx].m_sCharName}");
                    }
                    return true;

                case "getownmapdesc":
                    // GetOwnMapDesc — return player's current map description string
                    if (CurrentPlayer != null)
                        M2Share.MainOutMessage($"[PasBridge] GetOwnMapDesc: {CurrentPlayer.m_PEnvir?.sMapDesc ?? CurrentPlayer.m_sMapFileName}");
                    return true;

                case "reqopenstorage":
                    // Open storage UI: send save item list to client
                    if (CurrentNpc != null)
                    {
                        CurrentPlayer.m_nStoragePage = 0;
                        CurrentPlayer.SendSaveItemList(CurrentNpc.ObjectId);
                    }
                    return true;

                case "expandstoragespace":
                    if (args.Count != 1) return false;
                    ExpandStorageSpace(args[0].AsInt());
                    return true;

                case "storageitem":
                    // Deposit item into storage by name
                    if (args.Count >= 1 && CurrentNpc != null)
                    {
                        var itemName = args[0].AsString();
                        int count = args.Count >= 2 ? args[1].AsInt() : 1;
                        int movedCount = 0;
                        for (int c = 0; c < count; c++)
                        {
                            var item = CurrentPlayer.m_ItemList.FirstOrDefault(i =>
                                i != null && string.Equals(
                                    M2Share.UserEngine.GetStdItemName(i.wIndex), itemName,
                                    StringComparison.OrdinalIgnoreCase));
                            if (item != null && CurrentPlayer.m_StorageItemList.Count <
                                Math.Clamp(CurrentPlayer.m_nStorageSpaceCount,
                                    TPlayObject.MIN_STORAGE_ITEM_COUNT, TPlayObject.MAX_STORAGE_ITEM_COUNT))
                            {
                                CurrentPlayer.m_ItemList.Remove(item);
                                CurrentPlayer.m_StorageItemList.Add(item);
                                CurrentPlayer.SendStorageItemOk(item);
                                movedCount++;
                            }
                        }
                        if (movedCount > 0)
                        {
                            CurrentPlayer.WeightChanged();
                            M2Share.UserEngine.SaveHumanRcd(CurrentPlayer);
                        }
                    }
                    return true;

                case "getbackitem":
                    // Retrieve item from storage by name
                    if (args.Count >= 1 && CurrentNpc != null)
                    {
                        var itemName = args[0].AsString();
                        int count = args.Count >= 2 ? args[1].AsInt() : 1;
                        for (int c = 0; c < count; c++)
                        {
                            var item = CurrentPlayer.m_StorageItemList.FirstOrDefault(i =>
                                i != null && string.Equals(
                                    M2Share.UserEngine.GetStdItemName(i.wIndex), itemName,
                                    StringComparison.OrdinalIgnoreCase));
                            if (item != null)
                            {
                                CurrentPlayer.m_StorageItemList.Remove(item);
                                CurrentPlayer.m_ItemList.Add(item);
                            }
                        }
                        CurrentPlayer.WeightChanged();
                        CurrentPlayer.SendSaveItemList(CurrentNpc.ObjectId);
                        M2Share.UserEngine.SaveHumanRcd(CurrentPlayer);
                    }
                    return true;

                // === Money Operations ===
                case "addlf":
                    if (args.Count != 2) return false;
                    CurrentPlayer.AddNativeLingFu(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "addlimlf":
                    if (args.Count != 2) return false;
                    CurrentPlayer.AddNativeLimitedLingFu(
                        args[0].AsInt(), args[1].AsInt());
                    return true;

                case "declf":
                    if (args.Count != 3) return false;
                    _ = args[2].AsBool();
                    _ = CurrentPlayer.DecNativeLingFu(
                        args[0].AsInt(), args[1].AsInt());
                    return true;

                case "takediamond":
                case "adddiamond":
                    return RejectUnsupportedNativeApi();

                case "scriptrequestaddybnum":
                    if (args.Count >= 1)
                        CurrentPlayer.ScriptRequestNativeYuanbao(args[0].AsInt(),
                            NativeYuanbaoManager.AddOperation);
                    return true;

                case "scriptrequestsubybnum":
                    if (args.Count >= 1)
                        CurrentPlayer.ScriptRequestNativeYuanbao(args[0].AsInt(),
                            NativeYuanbaoManager.SubtractOperation);
                    return true;

                case "scriptdestroyitem":
                    // ScriptDestroyItem(itemName, count) — destroy specified item from player bag
                    if (args.Count >= 2 && CurrentPlayer != null)
                    {
                        var itemName = args[0].AsString();
                        var count = args[1].AsInt();
                        var destroyed = 0;
                        var itemsToRemove = new List<int>();
                        for (int i = 0; i < CurrentPlayer.m_ItemList.Count && destroyed < count; i++)
                        {
                            var stdItem = M2Share.UserEngine.GetStdItem(CurrentPlayer.m_ItemList[i].wIndex);
                            if (stdItem != null && string.Equals(stdItem.Name, itemName, StringComparison.OrdinalIgnoreCase))
                            {
                                itemsToRemove.Add(i);
                                destroyed++;
                            }
                        }
                        // Remove in reverse order to preserve indices
                        for (int ri = itemsToRemove.Count - 1; ri >= 0; ri--)
                            CurrentPlayer.m_ItemList.RemoveAt(itemsToRemove[ri]);
                        if (destroyed > 0)
                        {
                            CurrentPlayer.WeightChanged();
                            M2Share.MainOutMessage($"[PasBridge] ScriptDestroyItem: destroyed {destroyed}x {itemName} for {CurrentPlayer.m_sCharName}");
                        }
                    }
                    return true;

                // === Teleport ===
                case "flyto":
                    // Native Flyto (sub_6DEF8C): (x!=0 && y!=0) moves to the exact
                    // tile (sub_6BE4D0); when either coord is 0 (16-bit test) it
                    // takes the fallback mover sub_768C7C(self, map, 1, 1) — i.e. the
                    // placeholder (1,1), NOT the zero coordinate. C# SpaceMove already
                    // tile-resolves, so mirror the coords the native passes.
                    if (args.Count >= 3)
                    {
                        var flyMap = args[0].AsString();
                        var flyX = (short)args[1].AsInt();
                        var flyY = (short)args[2].AsInt();
                        if (flyX != 0 && flyY != 0)
                            CurrentPlayer.SpaceMove(flyMap, flyX, flyY, 0);
                        else
                            CurrentPlayer.SpaceMove(flyMap, 1, 1, 0);
                    }
                    return true;

                case "mapmove": case "map":
                    // MapMove(mapname) - Teleport to same X/Y on a different map
                    if (args.Count >= 1)
                        CurrentPlayer.SpaceMove(args[0].AsString(), (short)CurrentPlayer.m_nCurrX, (short)CurrentPlayer.m_nCurrY, 0);
                    return true;

                case "randomflyto":
                    if (args.Count >= 1)
                        CurrentPlayer.MapRandomMove(args[0].AsString(), 0);
                    return true;

                case "groupfly":
                    if (args.Count >= 1)
                        MoveCurrentGroupToMap(args[0].AsString());
                    return true;

                case "couplefly":
                    // CoupleFly (wrapper sub_6E036C -> executor sub_6CEF14): teleport a married
                    // player together with their online spouse to targetMap. Faithful 7-gate
                    // fail-closed ladder via the audited NativeCoupleFlyPlanner; on MoveBoth the
                    // native random-tiles self onto targetMap (sub_768C7C = 2x Random map-move,
                    // == MapRandomMove) then lands the spouse within +4-Random(9) of self's new
                    // tile per axis (sub_768CEC). sub_772DA8(spouse)=[spouse+0x74]=death gate.
                    if (args.Count >= 1)
                    {
                        var coupleMap = args[0].AsString();
                        var dearName = CurrentPlayer.m_sDearName ?? string.Empty;
                        var spouse = string.IsNullOrEmpty(dearName)
                            ? null : M2Share.UserEngine.GetPlayObject(dearName);
                        if (NativeCoupleFlyPlanner.Plan(
                                isMarried: !string.IsNullOrEmpty(dearName),
                                spouseName: dearName,
                                targetMap: coupleMap,
                                spouseOnline: spouse != null,
                                spouseIsSelf: spouse == CurrentPlayer,
                                spouseBlocked: spouse != null && spouse.m_boDeath,
                                sameEnvironment: spouse != null && spouse.m_PEnvir == CurrentPlayer.m_PEnvir)
                            == NativeCoupleFlyOutcome.MoveBoth)
                        {
                            CurrentPlayer.MapRandomMove(coupleMap, 0);       // self -> random tile (sub_768C7C)
                            var sx = CurrentPlayer.m_nCurrX;
                            var sy = CurrentPlayer.m_nCurrY;
                            spouse.SpaceMove(coupleMap,
                                (short)(sx + 4 - M2Share.RandomNumber.Random(9)),
                                (short)(sy + 4 - M2Share.RandomNumber.Random(9)), 0);   // spouse near self (sub_768CEC)
                        }
                    }
                    return true;

                case "shimenfly":
                    if (args.Count >= 2)
                        MoveShiMenToMap(args[0].AsString(), args[1].AsInt());
                    return true;

                case "flytodynroom":
                case "flytodynenvirwithidx":
                    // Native rooms are separate environment objects, not static
                    // maps named from the room and index.
                    return RejectUnsupportedNativeApi();

                case "playbacktoaccept":
                    // Return player to previous map via m_sMoveMap/m_nMoveX/m_nMoveY
                    if (CurrentPlayer != null && !string.IsNullOrEmpty(CurrentPlayer.m_sMoveMap))
                    {
                        CurrentPlayer.SpaceMove(CurrentPlayer.m_sMoveMap, CurrentPlayer.m_nMoveX, CurrentPlayer.m_nMoveY, 0);
                        CurrentPlayer.m_sMoveMap = "";
                    }
                    return true;

                // === Messages (sent TO player) ===
                case "sysmsg":
                    if (args.Count >= 1)
                        CurrentPlayer.SysMsg(args[0].AsString(), MsgColor.Green, MsgType.Hint);
                    return true;

                case "playerdialog":
                    if (args.Count >= 1 && !string.IsNullOrEmpty(args[0].AsString()))
                        CurrentPlayer.SendDefMessage(Grobal2.SM_MERCHANTSAY,
                            CurrentNpc?.ObjectId ?? 0, 0, 0, 0,
                            "NPC/" + args[0].AsString());
                    return true;

                case "playernotice":
                    if (args.Count >= 2 && TryExecuteNoticeTunnel(args[0].AsString()))
                        return true;
                    if (args.Count >= 2 && !string.IsNullOrEmpty(args[0].AsString()))
                    {
                        var packedColor = args[1].AsInt() switch
                        {
                            1 => 0xFFDB,
                            2 => 0xFCFF,
                            3 => 0xFDFF,
                            4 => 0xFFFF,
                            _ => 0x38FF
                        };
                        CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_SYSMESSAGE, 0,
                            packedColor & 0xFF, (packedColor >> 8) & 0xFF, 0,
                            args[0].AsString());
                    }
                    return true;

                case "npcsay":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                case "closedialog":
                    if (CurrentNpc != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTDLGCLOSE, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;

                // === YB/元宝/Shop ===
                // PsShopGetGoodsList / PsShopBuyGoods 在原生 654 条注册里一条都没有，
                // 全镜像裸 ASCII 与 UTF-16LE 双 0 命中，D:\光头卧龙 生产树 3611 个脚本/配置
                // 也 0 命中（tools/npcscript_re/_invented_items.py 可复跑）。属 INVENTED，
                // 按 fail-closed 处理，别让脚本经这条路走购买发货。
                case "psshopgetgoodslist":
                case "psshopbuygoods":
                    return RejectUnsupportedNativeApi();

                case "psconsumeyb":
                    if (args.Count >= 1)
                    {
                        if (CurrentPlayer.m_nGameGold >= args[0].AsInt())
                        {
                            CurrentPlayer.m_nGameGold -= args[0].AsInt();
                            CurrentPlayer.GameGoldChanged();
                        }
                    }
                    return true;

                case "chgselfskilllv":
                    if (args.Count >= 2)
                    {
                        var skillId = args[0].AsInt();
                        var magic = CurrentPlayer.GetMagicInfo(skillId);
                        if (magic != null)
                        {
                            magic.btLevel = Math.Min(unchecked((byte)args[1].AsInt()),
                                magic.MagicInfo.btTrainLv);
                            CurrentPlayer.RecalcAbilitys();
                            QueueScriptMagicLevelUpdate(CurrentPlayer, magic);
                        }
                    }
                    return true;

                case "delselfskill":
                    if (args.Count >= 1)
                    {
                        var skillId = args[0].AsInt();
                        for (int i = CurrentPlayer.m_MagicList.Count - 1; i >= 0; i--)
                        {
                            if (CurrentPlayer.m_MagicList[i].wMagIdx == skillId)
                            {
                                CurrentPlayer.SendDelMagic(CurrentPlayer.m_MagicList[i]);
                                CurrentPlayer.m_MagicList.RemoveAt(i);
                                CurrentPlayer.RecalcAbilitys();
                                break;
                            }
                        }
                    }
                    return true;

                // === Equipment ===
                case "repairbodyequip":
                    // RepairBodyEquip - set Dura = DuraMax for equipped weapon/armor
                    // If args[0] is provided, treat it as equipment position (0-12), default 1 (weapon)
                    {
                        var eqPos = args.Count >= 1 ? args[0].AsInt() : Grobal2.U_WEAPON;
                        eqPos = Math.Max(0, Math.Min(12, eqPos));
                        var eqItem = CurrentPlayer.m_UseItems[eqPos];
                        if (eqItem != null && eqItem.wIndex > 0 && eqItem.Dura < eqItem.DuraMax)
                        {
                            eqItem.Dura = eqItem.DuraMax;
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_DURACHANGE, eqPos,
                                eqItem.Dura, eqItem.DuraMax, 0, "");
                        }
                    }
                    return true;

                case "exchangeequip_sameiden":
                    // ExchangeEquip_SameIden - swap two same-type items (bag index and equip slot)
                    // args[0]: bag item index, args[1]: equip slot (U_xxx constant)
                    if (args.Count >= 2)
                    {
                        var bagIdx = args[0].AsInt();
                        var eqSlot = args[1].AsInt();
                        if (bagIdx >= 0 && bagIdx < CurrentPlayer.m_ItemList.Count &&
                            eqSlot >= 0 && eqSlot < CurrentPlayer.m_UseItems.Length)
                        {
                            var bagItem = CurrentPlayer.m_ItemList[bagIdx];
                            var eqItem = CurrentPlayer.m_UseItems[eqSlot];
                            if (bagItem != null && bagItem.wIndex > 0)
                            {
                                // Swap: bag item goes to equip slot, equip item goes to bag
                                CurrentPlayer.m_UseItems[eqSlot] = bagItem;
                                CurrentPlayer.m_ItemList[bagIdx] = eqItem ?? new TUserItem();
                                CurrentPlayer.RecalcAbilitys();
                                CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                            }
                        }
                    }
                    return true;

                case "dodamageweapon":
                    // DoDamageWeapon - decrease weapon durability by specified amount
                    {
                        var dmgAmount = args.Count >= 1 ? args[0].AsInt() : 1;
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0 && weapon.Dura > 0)
                        {
                            weapon.Dura = (ushort)Math.Max(0, (int)weapon.Dura - dmgAmount);
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_DURACHANGE,
                                Grobal2.U_WEAPON, weapon.Dura, weapon.DuraMax, 0, "");
                            if (weapon.Dura <= 0)
                            {
                                CurrentPlayer.SysMsg(M2Share.g_sTheWeaponBroke, MsgColor.Red, MsgType.Hint);
                            }
                        }
                    }
                    return true;

                // OpenStorageMax 不在原生注册表里（四重 0 命中）。原生只有
                // OpenStorage 0x72BA3C `function OpenStorage(storageType: Integer): Integer`，
                // C# 已在 CallNpcMethod 里实现那一条。
                case "openstoragemax":
                    return RejectUnsupportedNativeApi();

                case "setweaponlucky":
                    // SetWeaponLucky(luckValue: Integer) - Sets the equipped weapon's luck value (btValue[3]).
                    // Clamps to 0-255. Triggers ability recalculation and client update.
                    if (args.Count >= 1)
                    {
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0)
                        {
                            weapon.btValue[3] = (byte)Math.Max(0, Math.Min(255, args[0].AsInt()));
                            CurrentPlayer.RecalcAbilitys();
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                        }
                    }
                    return true;

                // === Stats/Abilities ===
                case "addplayerabil":
                    if (args.Count != 3) return false;
                    var playerAbilityType = unchecked((byte)args[0].AsInt());
                    if (!TBaseObject.IsNativeTimedAbilityType(playerAbilityType))
                        return true;
                    if (!TBaseObject.IsSupportedTimedAbilityType(playerAbilityType))
                        return RejectUnsupportedNativeApi();
                    CurrentPlayer.AddTimedAbility(
                        playerAbilityType,
                        unchecked((ushort)args[1].AsInt()),
                        unchecked((ushort)args[2].AsInt()));
                    return true;

                case "addlogrec":
                    if (args.Count >= 5)
                    {
                        M2Share.AddGameDataLog(string.Join('\t',
                            args[0].AsInt(), CurrentPlayer.m_sMapName,
                            CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY,
                            CurrentPlayer.m_sCharName, args[1].AsString(),
                            args[2].AsInt(), args[3].AsInt(), args[4].AsString()));
                    }
                    return true;

                case "incactivepoint":
                    if (args.Count >= 1)
                        CurrentPlayer.IncActivePoint(args[0].AsInt());
                    return true;

                case "inc_self_lv":
                    // Increment player level (uses byte, clamp to 255)
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.m_Abil.Level = (byte)Math.Min(255, CurrentPlayer.m_Abil.Level + 1);
                        CurrentPlayer.RecalcAbilitys();
                        CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                    }
                    return true;

                case "chgsex":
                    // Change player gender. 0 = male, 1 = female.
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        CurrentPlayer.m_btGender = args[0].AsInt() == 0 ? PlayGender.Man : PlayGender.WoMan;
                        CurrentPlayer.FeatureChanged();
                    }
                    return true;

                case "chgpkselfzero":
                    // Reset player PK points to 0 and update name color
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.m_nPkPoint = 0;
                        CurrentPlayer.RefNameColor();
                    }
                    return true;

                case "turnto":
                    // TurnTo(direction: Integer) — turn player to face a direction
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        var dir = (byte)(args[0].AsInt() % 8);
                        CurrentPlayer.m_btDirection = dir;
                        CurrentPlayer.SendRefMsg(Grobal2.RM_TURN, dir, CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY, 0, "");
                    }
                    return true;

                case "dorelive":
                    // 战神 DoRelive = sub_6E13C8 (eax=self, edx=delayTime, ecx=hp):
                    //   6E13D1  test esi,esi / jle 0x6E1410      ; delay <= 0 -> silent no-op
                    //   6E13DE  imul eax,esi,0x3E8               ; delay * 1000 ms
                    //   6E13E9  mov cx,0x27B1 / call sub_766060  ; queue DELAYED REVIVE (10161)
                    //   6E1403  mov cx,0x27B0 / call sub_765E68  ; immediate NOTICE (10160)
                    // The handler for 0x27B1 is sub_766A7C case @0x766FB4: it clears the
                    // death flag (`mov byte [edi+0x74],0`), sets HP from the queued record's
                    // +0x04 field, then calls sub_7693E8 and Envir [vmt+0x0C].
                    //
                    // BLOCKED, not unspecified.  C# has the matching primitive
                    // (SendDelayMsg == sub_766060, including the same ghost gate), but
                    // native ident 10160 collides with C#'s existing
                    // Grobal2.RM_USERSAVEITEM = 10160 (SystemModule/Grobal2.cs:1164), which
                    // has a live handler in TPlayObject.Message.cs.  Wiring this would
                    // either drop the notice or cross-fire the item-save handler, so it
                    // stays fail-closed until that ident collision is adjudicated.
                    return RejectUnsupportedNativeApi();

                case "callout":
                    return ScheduleCallOut(args, false);

                case "calloutex":
                    return ScheduleCallOut(args, true);

                case "sysgivegift":
                    TrySysGiveGift(args);
                    return true;

                case "giveconfigprize":
                    // Native RTTI procedure: GiveConfigPrize(PrizeIdx, InfoStr,
                    // TempTransferFlag).  Procedure failures are intentionally
                    // silent; only argument-shape errors are rejected here.
                    if (args.Count != 3) return false;
                    _ = TryNativeGiveConfigPrize(args[0].AsInt(),
                        args[1].AsString(), args[2].AsBool());
                    return true;

                case "queryawardcode":
                    if (args.Count != 1) return false;
                    CurrentPlayer.QueryNativeAwardCode(args[0].AsString());
                    return true;

                case "lefttime":
                    if (args.Count != 3) return false;
                    var line = args[1].AsInt();
                    var seconds = args[2].AsInt();
                    if (line >= 0 && seconds > 0)
                    {
                        CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_LEFTTIME, 4,
                            seconds, line, 0, args[0].AsString());
                    }
                    return true;

                case "setawardcodeactiveparam":
                    if (args.Count != 2) return false;
                    CurrentPlayer.SetNativeAwardCodeActiveParam(
                        args[0].AsString(), args[1].AsInt());
                    return true;

                case "addglorypoint":
                case "decglorypoint":
                    return RejectUnsupportedNativeApi();

                case "addguildpoint":
                    return RejectUnsupportedNativeApi();

                // === Task/Quest System (void-returning variants) ===
                case "questinfo":
                    return ApplyQuestInfo(args);

                case "addtasktouilist":
                    if (args.Count != 2) return false;
                    CurrentPlayer.AddTaskToUIList(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "updatetaskprogress":
                    if (args.Count != 2) return false;
                    CurrentPlayer.UpdateTaskProgress(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "updatetaskdetail":
                    if (args.Count != 2) return false;
                    CurrentPlayer.UpdateTaskDetail(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "deletetaskfromuilist":
                    if (args.Count != 2) return false;
                    CurrentPlayer.DeleteTaskFromUIList(args[0].AsInt(), args[1].AsInt());
                    return true;

                // === Teleport Helpers ===
                case "autogotomap":
                    // 原生 AutoGotoMap=sub_6D3024:服务端【跨地图路由】(启动预计算最小跳数
                    // next-hop 表 sub_5F51F8 + 同边 portal tie-break sub_5F4CF4),把途经地图/坐标
                    // 数组以消息 2850(0xB22)下发给客户端做自动寻路(每节点 20 字节:
                    // ShortString[15] 地图名 + Word X + Word Y),body 原样不编码(走 vtable+0x254);
                    // 不可达(0 节点)发 SysMsg "目标不可到达"。实现见 TPlayObject.NativeAutoGotoMap.cs,
                    // 路由见 MapManager.FindNativeMapPath。
                    // PAS 调用形态: This_Player.AutoGotoMap(map, x, y)
                    if (args.Count != 3) return false;
                    CurrentPlayer?.NativeAutoGotoMap(args[0].AsString(), args[1].AsInt(),
                        args[2].AsInt());
                    return true;

                // === Dialogs ===
                case "helperdialog":
                    // 原生 HelperDialog=sub_6F376C：把带 NPC 对话标记(<返回/@main> 之类可点击链接)的
                    // 内容通过 merchant-say 下发给客户端，不是聊天提示。此前用绿字 SysMsg 渲染，导致
                    // 新手指引菜单的链接以原始文本显示、菜单失效（脚本中有 87 处调用）。
                    // 走法与 playerdialog 一致。
                    if (args.Count >= 1 && CurrentPlayer != null && !string.IsNullOrEmpty(args[0].AsString()))
                        CurrentPlayer.SendDefMessage(Grobal2.SM_MERCHANTSAY,
                            CurrentNpc?.ObjectId ?? 0, 0, 0, 0,
                            "NPC/" + args[0].AsString());
                    return true;

                // === PK/Status ===
                case "decpkpoint":
                    if (args.Count >= 1)
                    {
                        CurrentPlayer.m_nPkPoint = Math.Max(0, CurrentPlayer.m_nPkPoint - args[0].AsInt());
                        CurrentPlayer.RefNameColor();
                    }
                    return true;

                // ChangeGPSwitch 原生只注册在 TPsNpc 0x734ADC 上，已搬到 CallNpcFunc。

                // === Internal Skills (内功/LNJN) ===
                case "learnlnjn":
                    // LearnLNJN(iType): learn internal skill type (V22:1-4 = learned flag)
                    if (args.Count >= 1) { int t = args[0].AsInt(); if (t >= 1 && t <= 4) SetPlayerVar('V', 22, t, PasValue.FromInt(1)); }
                    return true;

                case "uplevellnjn":
                    // UpLevelLNJN(iType): upgrade internal skill level (V22:1-4)
                    if (args.Count >= 1) { int t = args[0].AsInt(); if (t >= 1 && t <= 4) { var c = GetPlayerVarOrZero('V', 22, t); SetPlayerVar('V', 22, t, PasValue.FromInt(c + 1)); } }
                    return true;

                case "herolearnlnjn":
                    // Native callback is nullsub_115.
                    return true;

                case "herouplevellnjn":
                    // Native callback is nullsub_116.
                    return true;

                // === Map/Player Teleport ===
                case "changemapwithoths":
                    // ChangeMapWithOths(targetMap): teleport player + nearby humans to target map
                    if (args.Count >= 1)
                    {
                        var targetMap = args[0].AsString();
                        if (CurrentPlayer.m_PEnvir != null)
                        {
                            var nearby = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(CurrentPlayer.m_PEnvir, CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY, 10, nearby);
                            foreach (var obj in nearby)
                            { if (obj is TPlayObject p && p != CurrentPlayer) p.SpaceMove(targetMap, (short)CurrentPlayer.m_nCurrX, (short)CurrentPlayer.m_nCurrY, 0); }
                        }
                        CurrentPlayer.SpaceMove(targetMap, (short)CurrentPlayer.m_nCurrX, (short)CurrentPlayer.m_nCurrY, 0);
                    }
                    return true;

                case "groupflyinrange":
                    // GroupFlyInRange(mapName, x, y, iRange): group teleport with random range.
                    // Native sub_6E07B4 -> sub_727678: each member is placed at
                    // center - iRange + Random(2*iRange) per axis (span 2*r, so the
                    // upper edge center+r is UNREACHABLE). m_GroupMembers already
                    // holds the leader + all members (leader is Add()ed into it), so
                    // it matches the native leader + 11-slot enumeration.
                    if (args.Count >= 4)
                    {
                        var m = args[0].AsString(); var cx = args[1].AsInt(); var cy = args[2].AsInt(); var r = args[3].AsInt();
                        var go = CurrentPlayer.m_GroupOwner ?? CurrentPlayer;
                        for (int i = 0; i < go.m_GroupMembers.Count; i++)
                        {
                            var mb = go.m_GroupMembers[i];
                            if (mb != null) mb.SpaceMove(m, (short)(cx - r + M2Share.RandomNumber.Random(r * 2)), (short)(cy - r + M2Share.RandomNumber.Random(r * 2)), 0);
                        }
                    }
                    return true;

                case "groupexecuteproc":
                    // GroupExecuteProc(npcObj, procName): execute script proc for all group members
                    if (args.Count >= 2 && ScriptHost != null)
                    {
                        var pn = args[1].AsString(); var sp = ResolveNpcScriptPath();
                        if (sp == null) return true;
                        var go = CurrentPlayer.m_GroupOwner ?? CurrentPlayer;
                        for (int i = 0; i < go.m_GroupMembers.Count; i++)
                        { var mb = go.m_GroupMembers[i]; if (mb != null) ScriptHost.ScheduleCall(sp, pn, mb, CurrentNpc, 0); }
                    }
                    return true;

                case "groupcallout":
                    // GroupCallOut(npc, procName, param, deltTime): group deferred script execution
                    if (args.Count >= 4 && ScriptHost != null)
                    {
                        var npc = args[0].Type == PasValueType.Object
                            ? args[0].ObjVal as NormNpc
                            : null;
                        npc ??= CurrentNpc;
                        var pn = args[1].AsString();
                        var param = args[2].AsString();
                        var dt = SecondsToMilliseconds(args[3].AsInt());
                        var sp = ResolveNpcScriptPath(npc);
                        if (sp == null) return true;
                        var go = CurrentPlayer.m_GroupOwner ?? CurrentPlayer;
                        for (int i = 0; i < go.m_GroupMembers.Count; i++)
                        {
                            var mb = go.m_GroupMembers[i];
                            if (mb == null || mb.m_boGhost) continue;
                            mb.m_sCallOutParam = param;
                            ScriptHost.SchedulePlayerCall(sp, pn, mb, npc, dt, false);
                        }
                    }
                    return true;

                case "groupflytodynroom":
                    if (args.Count != 2
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    M2Share.DynamicRoomService.GroupFlyToDynamicRoom(
                        CurrentPlayer, args[0].AsString(), args[1].AsInt());
                    return true;

                case "groupflytodynroominrange":
                    {
                        // GroupFlyToDynRoomInRange(roomName, roomIdx, x, y, iRange) = native sub_6E0734
                        // -> sub_727678. sub_6E0734: [self+0xA80]==0 (no group) -> silent no-op;
                        // sub_5FCB78(name,idx) null (no active room) -> silent no-op; else sub_727678
                        // moves the leader + every non-null group member into the resolved active room at
                        // a random cell: X = centerX - R + Random(2R), Y = centerY - R + Random(2R), with
                        // centerX=x, centerY=y, R=iRange (coordinate roles resolved in
                        // staging/eventcluster_pas_ladders_20260731.md §1). Prior BLOCKER (sub_727678
                        // register mapping) is closed.
                        if (args.Count != 5
                            || M2Share.DynamicRoomService?.IsInitialized != true)
                            return false;
                        var roomName = args[0].AsString();
                        var roomIdx = args[1].AsInt();
                        var centerX = args[2].AsInt();
                        var centerY = args[3].AsInt();
                        var radius = args[4].AsInt();
                        var leader = CurrentPlayer?.m_GroupOwner; // native [self+0xA80]; null => no group
                        if (leader == null) return true;          // no group -> silent no-op
                        if (!M2Share.DynamicRoomService.IsDynamicRoomValid(roomName, roomIdx))
                            return true;                          // no active room -> silent no-op
                        var members = leader.m_GroupMembers;
                        if (members == null) return true;
                        var span = radius * 2;                    // native Random(2 * iRange)
                        for (int i = 0; i < members.Count; i++)
                        {
                            var member = members[i];
                            if (member == null) continue;
                            var rx = span > 0
                                ? centerX - radius + M2Share.RandomNumber.Random(span)
                                : centerX;
                            var ry = span > 0
                                ? centerY - radius + M2Share.RandomNumber.Random(span)
                                : centerY;
                            // FlyToDynamicRoomIndex re-resolves the active room and moves the member into
                            // it. It adds a null/ghost/race eligibility gate that native sub_727678 lacks
                            // (ghost members would still move in native) — a minor, conservation-safe
                            // divergence; coordinates match the native range formula exactly.
                            M2Share.DynamicRoomService.FlyToDynamicRoomIndex(
                                member, roomName, roomIdx, rx, ry);
                        }
                    }
                    return true;

                case "playerenterxinfamap":
                    // PlayerEnterXinfaMap(enterType, mapLevel): enter xinfa instance map
                    if (args.Count >= 2)
                    {
                        var mn = $"XinFa_{args[0].AsInt()}_{args[1].AsInt()}";
                        var mp = M2Share.MapManager.FindMap(mn);
                        if (mp != null) CurrentPlayer.SpaceMove(mn, (short)(M2Share.RandomNumber.Random(mp.wWidth)), (short)(M2Share.RandomNumber.Random(mp.wHeight)), 0);
                        else M2Share.MainOutMessage($"[PasApiBridge] PlayerEnterXinfaMap: map '{mn}' not found for {CurrentPlayer.m_sCharName}");
                    }
                    return true;

                // === SIGN-IN / DAILY REWARDS ===
                case "signin":
                    return RejectUnsupportedNativeApi();

                case "getsigninactprize":
                    // Native prize configuration and atomic claim state are absent.
                    return RejectUnsupportedNativeApi();

                case "getsigninactprizer":
                    // Native ABI is a function with two var-string arguments.
                    return RejectUnsupportedNativeApi();

                case "signindayact":
                    if (args.Count != 0 || CurrentPlayer == null ||
                        M2Share.SignActManager == null)
                        return RejectUnsupportedNativeApi();
                    M2Share.SignActManager.SignInEveryday(
                        CurrentPlayer.m_sCharName);
                    return true;

                case "getsignindayactprizer":
                    // Native ABI is a string-returning function, not a claim method.
                    return RejectUnsupportedNativeApi();

                // === VIP / AUTHORIZATION ===
                case "setviptag":
                    if (args.Count >= 1) SetPlayerVar('V', 11, 1, PasValue.FromInt(args[0].AsInt()));
                    return true;

                case "tryopenzillion":
                    // Native treasure roll and its claim transaction are absent.
                    return RejectUnsupportedNativeApi();

                case "activeauthen":
                    return RejectUnsupportedNativeApi();

                case "activedelauthen":
                    return RejectUnsupportedNativeApi();

                case "helpotherauthen":
                    return RejectUnsupportedNativeApi();

                case "authbyhelped":
                    return RejectUnsupportedNativeApi();

                case "humanpush":
                    if (args.Count >= 1)
                        CurrentPlayer.SysMsg(args[0].AsString(), MsgColor.Green, MsgType.Hint);
                    return true;

                case "broadcastpush":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                case "vipcall":
                    // Native sub_6B7D84 @0x006B7D84 is a SILENT NO-OP: the entire VIP-broadcast body is
                    // compile-disabled dead code (`xor ebx,ebx; test bl,bl; jz loc_6B7E75` @0x006B7DAA
                    // guards it — ebx is provably 0 at the test, so the broadcast never runs). Shield:
                    // do nothing (the prior C# SendBroadCastMsg was a non-native divergence).
                    return true;

                // === CURRENCY ===
                case "decactivepoint":
                    if (args.Count >= 1)
                        CurrentPlayer.DecActivePoint(args[0].AsInt());
                    return true;

                case "setvexptobeconverted":
                    return RejectUnsupportedNativeApi();

                case "lmcreatemon":
                    // External plugin APIs are not present in the native M2 registration surface.
                    return RejectUnsupportedNativeApi();

                case "decnicklinfu":
                    if (args.Count < 1) return false;
                    CurrentPlayer.DecNativeNickLinFu(args[0].AsInt());
                    return true;
                case "incnicklinfu":
                    if (args.Count < 1) return false;
                    var nickLinFuState = M2Share.NickLinFuState;
                    CurrentPlayer.IncNativeNickLinFu(args[0].AsInt(),
                        nickLinFuState.Multiplier, nickLinFuState.Enabled);
                    return true;
                case "adddblinfutime":
                case "clearmulexptime":
                case "dominatercall":
                case "donatediam":
                case "flytoobservermap":
                case "flytowespot":
                case "reqbuilddiamond":
                case "southwildstartconvoy":
                case "southwildstartmonattack":
                    // Native player mutations require engine-owned state and persistence transactions.
                    return RejectUnsupportedNativeApi();

                case "addheroabil":
                    if (args.Count != 3) return false;
                    var timedHero = CurrentPlayer.m_HeroObject;
                    if (timedHero != null)
                    {
                        var heroAbilityType = unchecked((byte)args[0].AsInt());
                        if (!TBaseObject.IsNativeTimedAbilityType(heroAbilityType))
                            return true;
                        if (!TBaseObject.IsSupportedTimedAbilityType(heroAbilityType))
                            return RejectUnsupportedNativeApi();
                        timedHero.AddTimedAbility(
                            heroAbilityType,
                            unchecked((ushort)args[1].AsInt()),
                            unchecked((ushort)args[2].AsInt()));
                    }
                    return true;

                case "setherolevel":
                    if (args.Count < 1 || CurrentPlayer.m_HeroObject == null)
                        return false;
                    return CurrentPlayer.m_HeroObject.TrySetNativeLevel(
                        args[0].AsInt(), out _);

                case "decjiayoupoint":
                    // Native DecJiaYouPoint (sub_6F28E8): point>0 -> JiaYouPoint
                    // (Self+0xAF0, Cardinal) -= point, clamped at 0; point<=0 is a no-op.
                    // The JiaYouPoint property is read-only; this native method is its
                    // dedicated mutator path (NativeDecJiaYouPointPlanner).
                    if (args.Count >= 1)
                    {
                        var decJiaYou = args[0].AsInt();
                        if (decJiaYou > 0)
                            CurrentPlayer.m_dwJiaYouPoint =
                                CurrentPlayer.m_dwJiaYouPoint < (uint)decJiaYou
                                    ? 0u
                                    : CurrentPlayer.m_dwJiaYouPoint - (uint)decJiaYou;
                    }
                    return true;

                case "addplayerhonorvalue":
                case "subplayerhonorvalue":
                    // Native fame-statue state is not represented by player V variables.
                    return RejectUnsupportedNativeApi();

                case "createcampanimal":
                    // Native CreateCampAnimal(monName, campIdx, MonX, MonY, MonNum, Range, targX,
                    // targY) (sub_6EB7D8; decl @0x0072F82C). The sibling createcampmon (sub_6EB6B8)
                    // is a thin wrapper that CALLS this proc, so this is the shared spawn core.
                    // Wired to the SAME standard as the already-LIVE sibling createcampmon
                    // (RegenMonsterByName scatter, cap 200, guard->home anchor).
                    // CAVEATs (identical to live createcampmon): campIdx (args[1]) parsed but not
                    // applied -- no wired 32-bit camp field; fame-dummy fallback (sub_604E3C) absent
                    // so names not in the monster DB spawn nothing (batch stops, matches native
                    // stop-on-fail); RNG scatter parity pending RandSeed cutover; native feedback
                    // "已成功创建...守护坐标" (cx=0xFFDB) omitted like the sibling.
                    if (args.Count >= 6 && CurrentPlayer.m_PEnvir != null)
                        SpawnNativeCampAnimals(CurrentPlayer.m_PEnvir,
                            args[0].AsString(), args[2].AsInt(), args[3].AsInt(),
                            args[4].AsInt(), args[5].AsInt(),
                            args.Count > 6 ? args[6].AsInt() : -1,
                            args.Count > 7 ? args[7].AsInt() : -1);
                    return true;

                // === ITEM / GIVE ===
                case "giveitemwithdura":
                    // Native sub_6E15E0 applies clamped durability (min(req, DuraMax));
                    // it is a real API, not a semantics-corrupting one.
                    TryGiveItemWithDura(args, out _);
                    return true;

                case "takefullduraitem":
                    if (args.Count >= 2)
                        NativeTakeFullDuraItem.Execute(CurrentPlayer,
                            args[0].AsString(), args[1].AsInt());
                    return true;

                case "senditemstoother":
                    // Native cross-player item transfer transaction is absent.
                    return RejectUnsupportedNativeApi();

                case "dropitemdownext":
                    if (args.Count >= 2)
                    {
                        for (int c = 0; c < args[1].AsInt(); c++)
                        {
                            var ui = CurrentPlayer.CheckItems(args[0].AsString());
                            if (ui != null) CurrentPlayer.DropItemDown(ui, 1, false, null, CurrentPlayer);
                        }
                    }
                    return true;

                case "presentitem":
                    // Native scheduled-delivery transaction is absent.
                    return RejectUnsupportedNativeApi();

                case "addpointtomarkstonecharm":
                    if (args.Count >= 2)
                    {
                        int pt = args[0].AsInt();
                        bool isMaster = args[1].AsInt() != 0;
                        int vidx = isMaster ? 1 : 2;
                        SetPlayerVar('V', 16, vidx, PasValue.FromInt(GetPlayerVarOrZero('V', 16, vidx) + pt));
                    }
                    return true;

                case "clientsellercancelybdeal":
                    // Native PAS wrapper enters the seller transaction through
                    // YBDB request 117. Closing the dialog is not a substitute.
                    return RejectUnsupportedNativeApi();

                case "notifyclientupditemex":
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        int bagIdx = -1;
                        if (args[0].Type == PasValueType.String)
                        {
                            var searchName = args[0].AsString();
                            for (int fi = 0; fi < CurrentPlayer.m_ItemList.Count; fi++)
                            {
                                var fiItem = CurrentPlayer.m_ItemList[fi];
                                if (fiItem != null && fiItem.wIndex > 0 &&
                                    string.Equals(M2Share.UserEngine.GetStdItemName(fiItem.wIndex), searchName, StringComparison.OrdinalIgnoreCase))
                                {
                                    bagIdx = fi;
                                    break;
                                }
                            }
                        }
                        else
                        {
                            bagIdx = args[0].AsInt();
                        }
                        if (bagIdx >= 0 && bagIdx < CurrentPlayer.m_ItemList.Count)
                        {
                            var it = CurrentPlayer.m_ItemList[bagIdx];
                            if (it != null && it.wIndex > 0) CurrentPlayer.SendUpdateItem(it);
                        }
                    }
                    return true;

                case "takebyclientid":
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        int cid = args[0].AsInt();
                        var it = CurrentPlayer.FindClientItemIn(CurrentPlayer.m_ItemList, cid, false)
                                 ?? CurrentPlayer.FindClientItemIn(CurrentPlayer.m_ItemList, cid, true);
                        if (it != null)
                        {
                            CurrentPlayer.DelBagItem(it.MakeIndex,
                                M2Share.UserEngine.GetStdItemName(it.wIndex));
                        }
                    }
                    return true;

                case "delbagitemofall":
                    // FUN_006c1ed8: iterate m_ItemList backwards, delete all items
                    if (CurrentPlayer != null)
                    {
                        for (int i = CurrentPlayer.m_ItemList.Count - 1; i >= 0; i--)
                        {
                            var item = CurrentPlayer.m_ItemList[i];
                            if (item == null) continue;
                            CurrentPlayer.m_ItemList.RemoveAt(i);
                        }
                    }
                    return true;

                // === MISC PLAYER METHODS ===
                case "setplayerlevel":
                    // FUN_006efa3c: sets both m_Abil.Level (+0x9E) and m_nLevel (+0x7F)
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        int newLevel = Math.Max(1, Math.Min(ushort.MaxValue, args[0].AsInt()));
                        int oldLevel = CurrentPlayer.m_Abil.Level;
                        M2Share.MainOutMessage($"[SetLevel] {CurrentPlayer.m_sCharName}: {oldLevel} -> {newLevel}");
                        CurrentPlayer.m_Abil.Level = (ushort)newLevel;
                        CurrentPlayer.RecalcAbilitys();
                        CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                    }
                    return true;

                case "autorelive":
                    if (args.Count >= 3 && CurrentPlayer != null)
                    {
                        CurrentPlayer.m_boDeath = false;
                        bool bMaxHP = args.Count >= 4 && args[3].AsInt() != 0;
                        bool bMaxMP = args.Count >= 5 && args[4].AsInt() != 0;
                        CurrentPlayer.m_WAbil.HP = bMaxHP
                            ? CurrentPlayer.m_WAbil.MaxHP
                            : Math.Max(1, (int)(CurrentPlayer.m_WAbil.MaxHP * 0.3));
                        CurrentPlayer.m_WAbil.MP = bMaxMP
                            ? CurrentPlayer.m_WAbil.MaxMP
                            : Math.Max(0, (int)(CurrentPlayer.m_WAbil.MaxMP * 0.3));
                        CurrentPlayer.HealthSpellChanged();
                        CurrentPlayer.SpaceMove(args[0].AsString(), (short)args[1].AsInt(), (short)args[2].AsInt(), 0);
                        CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                        CurrentPlayer.SendRefMsg(Grobal2.RM_ALIVE, CurrentPlayer.m_btDirection, CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY, 0, "");
                    }
                    return true;

                case "setvex":
                    if (args.Count >= 3)
                        SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(), PasValue.FromInt(args[2].AsInt()));
                    return true;

                case "givehumlevelbuffer":
                    // A permanent V-variable cannot replace the native timed ability buffer.
                    return RejectUnsupportedNativeApi();

                case "unshutupself":
                    // Native sub_6BF3A8 @0x006BF3A8: acts ONLY when the player is currently self-muted
                    // (sub_6DF1E4(1,43)==1); on success it un-mutes and sends the Chinese notice
                    // "解除禁言成功！" (dword_6BF448) via SysMsg. No message when not muted.
                    if (CurrentPlayer != null && CurrentPlayer.m_boShutup)
                    {
                        CurrentPlayer.m_boShutup = false;
                        CurrentPlayer.SysMsg("解除禁言成功！", MsgColor.Green, MsgType.Hint);
                    }
                    return true;

                case "doscripthpmprecover":
                    if (args.Count >= 2 && CurrentPlayer != null)
                    {
                        CurrentPlayer.m_WAbil.HP = (int)Math.Clamp(
                            (long)CurrentPlayer.m_WAbil.HP + args[0].AsInt(),
                            0, Math.Max(0, CurrentPlayer.m_WAbil.MaxHP));
                        CurrentPlayer.m_WAbil.MP = (int)Math.Clamp(
                            (long)CurrentPlayer.m_WAbil.MP + args[1].AsInt(),
                            0, Math.Max(0, CurrentPlayer.m_WAbil.MaxMP));
                        CurrentPlayer.HealthSpellChanged();
                        CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                    }
                    return true;

                case "addplayerdailycount":
                    if (args.Count >= 2)
                    {
                        int tid = args[0].AsInt();
                        int cnt = args[1].AsInt();
                        int cur = GetPlayerVarOrZero('V', 18, tid % 50);
                        SetPlayerVar('V', 18, tid % 50, PasValue.FromInt(cur + cnt));
                    }
                    return true;

                // =====================================================================
                // HERO SYSTEM
                // =====================================================================

                case "createhero":
                    if (args.Count >= 3)
                    {
                        HeroDataService.RequestCreate(CurrentPlayer,
                            args[0].AsString(), args[1].AsInt(), args[2].AsInt());
                    }
                    return true;

                case "giveheroexp":
                    // 原版 GiveHeroExp (sub_6E2C90): hero=[player+0xBB0] 存在 ? sub_687714(hero, amount, a3, a4=0) : 返回0。
                    // a4=0 → 普通经验(受英雄200级自然上限约束)。执行器 GrantNativeHeroExperience(hero, amount,
                    // countAsFightExperience=false, directMode=false==a4) 已忠实移植 sub_687714(溢出守卫 requested*2 /
                    // 升级循环 GetLevelExp阈值+RecalcAbilitys / 200级cap / 战魂累加器)并经 PasDispatchShadowCompatCheck 审计。
                    // (证据: staging/ida_hero_intimacy_exact.txt sub_6E2C90 + ida_hero_900_898_deep.txt sub_687714。)
                    if (args.Count >= 1 && CurrentPlayer?.m_HeroObject != null)
                        CurrentPlayer.GrantNativeHeroExperience(CurrentPlayer.m_HeroObject,
                            args[0].AsInt(), false, false);
                    return true;

                case "giveherosuperexp":
                    // 原版 GiveHeroSuperExp (sub_6E2CC0): hero ? sub_687714(hero, amount, a3, a4=1) : 0。
                    // a4=1 → 超级经验(directMode=true 绕过英雄200级自然上限)。同执行器。
                    if (args.Count >= 1 && CurrentPlayer?.m_HeroObject != null)
                        CurrentPlayer.GrantNativeHeroExperience(CurrentPlayer.m_HeroObject,
                            args[0].AsInt(), false, true);
                    return true;

                case "giveherosuperexploop":
                    // 原版 GiveHeroSuperExpLoop (sub_6E2CEC): hero ? (count>0 时循环 count 次
                    // sub_687714(hero, amount, a3, a4=1)) : 0。每次超级经验(directMode=true)。
                    // args[0]=每次经验, args[1]=次数。注: sub_6E2CEC 寄存器序为 a2=amount/a3=cl/a4=count，
                    // 脚本参→寄存器映射需 PAS proc 签名表方可逐字确认；此处按 API 名(amount,count)的自然 2 参解读，
                    // 若后续证据为 3 参(amount,flag,count)则 count 改用 args[2](trivial)。见交付说明。
                    if (args.Count >= 2 && CurrentPlayer?.m_HeroObject != null)
                    {
                        var loopHero = CurrentPlayer.m_HeroObject;
                        var loopAmount = args[0].AsInt();
                        var loopCount = args[1].AsInt();
                        for (var i = 0; i < loopCount; i++)
                            CurrentPlayer.GrantNativeHeroExperience(loopHero, loopAmount, false, true);
                    }
                    return true;

                case "giveheroforceexp":
                    // 原版 giveheroforceexp (sub_6E2CBC) = `xor eax,eax; retn` 纯 no-op 桩(恒返回0)。reject 即忠实。
                    return RejectUnsupportedNativeApi();

                // TakeHeroBagExItem / TakeFromHeroBagEx 四重 0 命中。原生的英雄背包取物
                // 只有 TakeFromHeroBag 0x72B285
                // `function TakeFromHeroBag(const ItemName: string; ItemCount: Byte): Boolean`，
                // C# 已在 CallPlayerFunc 的 takefromherobag 上实现那一条。
                case "takeherobagexitem":
                case "takefromherobagex":
                    return RejectUnsupportedNativeApi();

                // HeroRename 原生只注册在 TPsNpc 0x734E90 上，已搬到 CallNpcFunc。

                case "openequipmentmascottomax":
                    // OpenEquipmentMascotToMax(ALevel) - open mascot slots on player weapon to max
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        var aLevel = args[0].AsInt();
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0)
                        {
                            weapon.ys1 = Math.Max(weapon.ys1, aLevel);
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                        }
                    }
                    return true;

                case "openequipmentmascottomaxhero":
                    // OpenEquipmentMascotToMaxHero(ALevel) - open mascot slots on hero weapon to max
                    if (args.Count >= 1 && CurrentPlayer != null && CurrentPlayer.m_HeroObject != null)
                    {
                        var aLevel = args[0].AsInt();
                        var hero = CurrentPlayer.m_HeroObject;
                        var weapon = hero.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0)
                        {
                            weapon.ys1 = Math.Max(weapon.ys1, aLevel);
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                        }
                    }
                    return true;

                case "finishcombineherotrain":
                case "createguildhero":
                case "setguildherotargxy":
                case "createprotecthero":
                    // Native hero training, guild ownership, and persistence are absent.
                    return RejectUnsupportedNativeApi();

                // =====================================================================
                // MARRIAGE / MASTER SYSTEM
                // =====================================================================

                case "agreemarry":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TBaseObject marryNpc
                        || CurrentPlayer == null)
                        return false;
                    AgreeNativeMarry(CurrentPlayer, marryNpc);
                    return true;

                case "disagreemarry":
                    if (args.Count != 0) return false;
                    DisAgreeNativeMarry(CurrentPlayer);
                    return true;

                case "npcdivmarry":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TBaseObject divorceNpc
                        || CurrentPlayer == null)
                        return false;
                    NpcDivNativeMarry(CurrentPlayer, divorceNpc);
                    return true;

                case "agreebaishi":
                    TryAgreeBaiShi();
                    return true;

                case "disagreebaishi":
                    // DisAgreeBaishi - decline master/student (baishi) request
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.SysMsg("你拒绝了拜师请求。", MsgColor.Green, MsgType.Hint);
                    }
                    return true;

                case "npcleavetec":
                    // 战神 sub_6CAFF0.  Two gates, then the shared dissolve
                    // routine sub_6C5EC8 with mode=0 (自行离开师门):
                    //   0x6CAFFA  cmp byte [ebx+0xB95],0 / je 0x6CB01E
                    //   0x6CB003  mov edx,0xC350 (50000) / call sub_6C7D64
                    //   0x6CB00F  test al,al / je 0x6CB01E
                    //   0x6CB013  xor edx,edx / call sub_6C5EC8   ; mode = 0
                    // Either gate failing lands on 0x6CB01E, which sends
                    // 0x6CB048 "你尚无师承或携带的金币不够, 不能离开！" on the
                    // cx=0xFFDB info channel.  sub_6C7D64 is DecGold: it charges
                    // only on success, so a poor student is not billed.
                    //
                    // The tail at 0x6CB035 (sub_6C7E18) runs on BOTH paths: when
                    // its edx (the PAS label argument) is non-nil it fires
                    // RM_MERCHANTDLGCLOSE (0x278F == 10127); a nil label is a
                    // no-op (0x6C7E1D `test edx,edx` / `je 0x6C7E33`).
                    if (CurrentPlayer == null) return true;
                    if (CurrentPlayer.m_boStudent
                        && CurrentPlayer.DecGold(NativeLeaveTecGoldCost))
                    {
                        CurrentPlayer.NativeLeaveMaster(0);
                    }
                    else
                    {
                        CurrentPlayer.SysMsg(
                            "你尚无师承或携带的金币不够, 不能离开！",
                            MsgColor.Green, MsgType.Hint);
                    }
                    CloseNativeMarryDialog(CurrentPlayer, CurrentNpc);
                    return true;

                case "requestmarry":
                    // Native request state is bidirectional and expires after 300 seconds.
                    // A notification-only substitute leaves AgreeMarry impossible to match.
                    return RejectUnsupportedNativeApi();

                case "requestbaishi":
                    if (args.Count < 1)
                    {
                        return true;
                    }
                    string targetName = args[0].AsString();
                    if (CurrentPlayer == null) return true;
                    if (CurrentPlayer.m_boStudent
                        || CurrentPlayer.m_Abil.Level
                        > M2Share.g_Config.nMaxApprenticeLevel)
                    {
                        CurrentPlayer.SysMsg(
                            "[失败] 拜师必须满足：无师傅，等级不高于"
                            + M2Share.g_Config.nMaxApprenticeLevel,
                            MsgColor.Green, MsgType.Hint);
                        return true;
                    }
                    int requestTick = HUtil32.GetTickCount();
                    if (CurrentPlayer.m_boRequestMaster
                        && IsMasterRequestCoolingDown(requestTick,
                            CurrentPlayer.m_dwMasterRequestTime))
                    {
                        CurrentPlayer.SysMsg("[失败] 你刚拜过师，请稍后再试",
                            MsgColor.Green, MsgType.Hint);
                        return true;
                    }
                    var target = M2Share.UserEngine.GetPlayObject(targetName);
                    if (target == null || !target.m_boAllowMaster)
                    {
                        CurrentPlayer.SysMsg(
                            "[失败] 对方不在有效范围或对方已设置拒绝收徒",
                            MsgColor.Green, MsgType.Hint);
                        return true;
                    }
                    if (target.m_Abil.Level < M2Share.g_Config.nMinMasterLevel
                        || target.m_nStudentCount >= 5)
                    {
                        CurrentPlayer.SysMsg("[失败] 对方等级不够或弟子数已满",
                            MsgColor.Green, MsgType.Hint);
                        return true;
                    }
                    if (target.m_boRequestMaster
                        && IsMasterRequestCoolingDown(requestTick,
                            target.m_dwMasterRequestTime))
                    {
                        CurrentPlayer.SysMsg(
                            "[失败] 对方正处理他人拜师，请稍后再试",
                            MsgColor.Green, MsgType.Hint);
                        return true;
                    }
                    SendShiMenNpcDialog(target,
                        BuildRequestBaiShiDialog(CurrentPlayer.m_sCharName));
                    target.m_boRequestMaster = true;
                    target.m_dwMasterRequestTime = requestTick;
                    target.m_MasterRequestTarget = CurrentPlayer;
                    CurrentPlayer.m_boRequestMaster = true;
                    CurrentPlayer.m_dwMasterRequestTime = requestTick;
                    CurrentPlayer.m_MasterRequestTarget = target;
                    CurrentPlayer.SysMsg(
                        "[成功]你的拜师请求已发出，正等候对方处理..",
                        MsgColor.Red, MsgType.Hint);
                    return true;

                // === PK System ===
                case "incpkpoint":
                    // IncPkPoint(addNum) - increment PK points
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        CurrentPlayer.m_nPkPoint += args[0].AsInt();
                        CurrentPlayer.RefNameColor();
                    }
                    return true;

                // =====================================================================
                // LEITAI ARENA SYSTEM
                // =====================================================================

                case "dosendplayertoleitai":
                    // Native arena ownership and match state are absent.
                    return RejectUnsupportedNativeApi();

                // === Variable System Pass-Throughs (delegate to standalone) ===
                case "setv":
                    if (args.Count >= 3) SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]);
                    return true;

                case "sets":
                    if (args.Count >= 3) SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]);
                    return true;

                case "groupsetv":
                    if (args.Count >= 3) SetGroupPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]);
                    return true;

                case "groupsets":
                    if (args.Count >= 3) SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]);
                    return true;

                // =====================================================================
                // 沉默回退族(silent fall-through class)——原生已注册但 C# 无 case 的 PAS 过程
                // 全部注册在 TPlayer PAS 面(registrar sub_731350, class 全局 0x006AC87C
                // = TPlayer),调用点 ABI ecx=名字 edx=handler。此前这些名字连
                // RejectUnsupportedNativeApi 都到不了:语句形式会抛
                // PasRuntimeException "函数找不到" 使脚本中途中断,表达式形式静默返回 0。
                // 下列 case 把该族变成可 grep/可计数的显式拒绝(行为等价于原来的落空
                // default,但不再中断脚本、并留下痕迹)。
                // =====================================================================

                // 观摩点(ObPoint)写入侧 —— 仍是真缺口。
                // AddGuanMoPoint sub_6EFB08 直连 MySQL:先以
                // 'Select * from gamedata.LiPaoObPoint where CharName="%s";' (0x6EFC4C)
                // 经 sub_724BE8(ExecuteQuery)探测,行数 > 0 走
                // 'Update gamedata.LiPaoObPoint Set ObPoint=ObPoint+%d where Charname="%s";'
                // (0x6EFC90,注意原版此处 Charname 大小写与 SELECT 不一致),
                // 否则走 'insert into gamedata.LiPaoObPoint(PTID, CharName, ObPoint)
                // values("%s","%s",%d);'(0x6EFCE4),两者均经 sub_724E48(ExecuteScript, cl=1)。
                // 名字取 player+0x106(shortstring),PTID 取 player+0xB09 并经
                // sub_40BC50(UpperCase, 仅 ASCII a-z)。返回值恒 0(0x6EFC01)。
                // 注意 0x6EFB64 的 jle 是有符号比较,而 ExecuteQuery 失败返回 -1/0,
                // 故探测失败会 fail-open 走 INSERT(原版无 CharName 唯一键,会产生重复行)。
                // 未接线原因:这是持久货币写入,须全有或全无;需要一个 gated MySQL store
                // (照 gild/stall 模式)才能安全落地,当前无 store ⇒ 保持拒绝。
                case "addguanmopoint":
                    return RejectUnsupportedNativeApi();

                // DeleteObPoint sub_6F0930 —— 忠实实现见 CallPlayerFunc 中同名 case:
                // 原生走的那张 TMirStringList 永不被装填,故恒 no-op。
                case "deleteobpoint":
                    return true;

                // ---------------------------------------------------------------------
                // 大药商人(TSuperMerchant,VMT 名确认自 [0x615ED4],实例全局 0x7D6D10)
                // 荣耀点/元宝 兑换族。定价**完全可移植**,不需要缺失子系统:
                //   sub_6161EC 单价 = 89.5 - 10.242 * ln(CurrentStorage[type]),
                //     常量逐字节核对:0x616248 = 08 AC 1C 5A 64 3B DF A3 02 40
                //     (x87 extended-80 = 10.242),0x616254 = 00 00 B3 42(single = 89.5);
                //     type ∉ {1,2} 时单价 = 0.0(0x6161E8 = 0.0f)。
                //   sub_61617C 荣耀报价 = Round(单价 * 数量)  (fild/fmulp/sub_403580)
                //   sub_6161A8 元宝反算 = 单价<=0 ? 0 : Round(元宝数 * 102 / 单价)
                //   仓储三元组在 TSuperMerchant:Min=+0x18+t*4 / Max=+0x20+t*4 /
                //     Current=+0x28+t*4,dirty=+0x34,由 sub_616258 从
                //     'Config\SuperMerchant.ini' 节 'GoodsInfo1'/'GoodsInfo2' 的
                //     ItemName/MinStorage/MaxStorage/CurrentStorage 载入;
                //     缺文件时 ctor@0x615FC4 默认 20 / 2500 / 1000。
                //   货品名数组 [0x7D7034] -> 0x7B45AC 是 **1 基** AnsiString 指针数组
                //     (三处一致的 base+idx*4-4:0x6E5021/0x6E5175/0x6D5709),仅 2 项:
                //     1='疗伤药包' 2='万年雪霜包'(refcnt = -1,二进制字面量)。
                // 未接线的真正原因不是定价,而是下面各自标注的事务性问题。
                // ---------------------------------------------------------------------

                // SellGoodsToGetGloryPoint sub_6E4FB0:梯度(每个码都绑定到确切 EA)——
                //   -1 挂单不符:0x6E4FF9 cmp edi,[ebx+0x9CC] jne 或
                //      0x6E5005 cmp esi,[ebx+0x9D0] jne(经理为空亦落此)
                //   -2 背包不足:0x6E502F sub_7447C0(计数) 0x6E5034 cmp/jl
                //   -3 仓储吃不下:0x6E5056 sub_615F44 != 1
                //   -4 扣物品失败:0x6E506D sub_740B04 test al,al je
                //    1 全通过:0x6E50AA
                // 副作用序:重算价 → 校验挂单 → 解析物品名(0x6E5018) → 计数 →
                //   **sub_615F44 提交仓储** → 扣物品 → sub_6E2108 加荣耀点 →
                //   日志(type 9)→ 清 +0x9CC/+0x9D0。
                // ⚠ 两处必须原样复刻否则背离:(a) sub_615F44 **不是判定而是写操作**
                //   (0x615F6E Current := Min(Current+delta, Max) + 0x615F72 dirty := 1),
                //   且**静默截断** —— 只剩 10 余量时卖 500 仍返回 1 并按 500 全额付荣耀;
                //   (b) 它在扣物品**之前**提交,故 -4 会留下已改的仓储(原生自身的不一致)。
                // 未接线原因:上述 (a)(b) 是钱物路径上的原生不一致,须与仓储子系统
                //   (SuperMerchant.ini 状态 + dirty 落盘)一并落地才能保证守恒 ⇒ 保持拒绝。
                case "sellgoodstogetglorypoint":
                    return RejectUnsupportedNativeApi();

                // ConsumeYBToBuyGoods sub_6E5104:元宝侧,用**另一对**挂单
                // +0x9D4/+0x9D8(由 QueryGoodsNumByYBNum sub_6E4F88 写)。
                // 梯度:-1 挂单不符 / -2 元宝不足([player+0x760] < 数量, 0x6E5184 jl)
                //   / -3 背包空格不足(0x6E5192 sub_7441D8 = 48 - BagList.Count, jg)
                //   / -4 仓储供不上(0x6E51B7 sub_61602C != 1) / -5 外部元宝请求提交失败
                //   (0x6E51D9 sub_6D3694, Ident=0x7D=125, sel=0x2742=10050) / 1 成功。
                // ⚠ **成功路径按设计是半个事务**:本函数只扣仓储(不可逆 + dirty)并把
                //   投递票据写入 +0x9DC/+0x9E0;**物品是由 125 型回包处理器
                //   sub_6D5344@0x6D56E0 发放的**(读 +0x9DC 校验 1..2 → sub_6C87B4 给物)。
                //   只实现本函数 = 扣了仓储却永不发货 = 物品丢失。两半须同时落地 ⇒
                //   保持拒绝(且 [player+0x760] 元宝在本移植中是外部只读的)。
                case "consumeybtobuygoods":
                    return RejectUnsupportedNativeApi();

                // QueryGloryPointByGoodsNum sub_6E4F60:声明为
                // function QueryGloryPointByGoodsNum(const goodsType, goodsNum: Integer): Integer
                // (@0x72EBE5)。eax=Self edx=goodsType ecx=goodsNum;
                // 0x6E4F72 sub_61617C 得荣耀值 → **返回该值**(不是无返回值的过程),
                // 同时把挂单写入 0x6E4F77 [ebx+0x9CC]=goodsType、0x6E4F7D [ebx+0x9D0]=荣耀值。
                // 注意原生此处**缺少 [0x7D6D10] 的空指针门**(两个兄弟 0x6E4FDC/0x6E5130 有),
                // 即经理构造失败时原生会 AV。
                // 未接线原因:它的唯一意义是为 SellGoodsToGetGloryPoint 建立挂单,
                // 单独接线会让"报价"写入一个永不被消费的挂单 ⇒ 与卖出侧同批处理。
                case "queryglorypointbygoodsnum":
                    return RejectUnsupportedNativeApi();

                // AddToBuyGoodsLogByScript sub_6FB3A0:向购物日志追加一条记录(ret 8)。
                case "addtobuygoodslogbyscript":
                    return RejectUnsupportedNativeApi();

                // SystemMsg sub_6FAFA8:按第 2 参(dl)的 5 路跳转(阈值 0x21/+2/+1/+6),
                // 各分支以不同频道调 sub_5F701C([0x7D593C]);未命中即 no-op。
                case "systemmsg":
                    return RejectUnsupportedNativeApi();

                // ShowCurrentBless sub_6EB0BC:格式化并经 vtable +0x250(dx=0xCEE)发送祝福状态。
                case "showcurrentbless":
                    return RejectUnsupportedNativeApi();

                // TaskDialog sub_6FBD04:ecx 非空时经 vtable +0x250(dx=0x1193)发任务对话。
                case "taskdialog":
                    return RejectUnsupportedNativeApi();

                // GetMyTaskDetail sub_6FBCC0:任务管理器 [0x7D6D80] 查任务 → sub_604320 取明细。
                case "getmytaskdetail":
                    return RejectUnsupportedNativeApi();

                default:
                    return false;
            }
        }

        public bool CallPlayerFunc(string method, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentPlayer == null) return false;

            switch (method.ToLowerInvariant())
            {
                // ===== 原生注册在 TPlayer 上的一批，此前只挂在 Npc/Standalone 表里 =====
                // PasInterpreter.TryInvokePlayerMethod 只试 CallPlayerFunc/CallPlayerMethod，
                // 所以 This_Player.Xxx 会抛「函数找不到」并中断整个标签。

                case "buildguild":                  // TPlayer 0x72B1DD
                    // BuildGuild(GuildStr): create a new guild
                    if (args.Count >= 2)
                        M2Share.GuildManager.AddGuild(args[0].AsString(), args[1].AsString());
                    return true;

                case "chgequipmentbreaklevel":      // TPlayer 0x72B741
                    // ChgEquipmentBreakLevel(nPos, Value, bHero, bAdd): Integer
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        var breakWeapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (breakWeapon != null && breakWeapon.wIndex > 0
                            && breakWeapon.btValue != null && breakWeapon.btValue.Length > 4)
                        {
                            breakWeapon.btValue[4] = (byte)Math.Max(0, Math.Min(255, args[0].AsInt()));
                            CurrentPlayer.RecalcAbilitys();
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                        }
                    }
                    return true;

                case "giveitemstoother":            // TPlayer 0x72B7E9
                case "reqpieceupnewyearpicture":    // TPlayer 0x72B65D
                case "startpaodian":                // TPlayer 0x72BAC0
                    // Native cross-player delivery / New Year picture / paodian scheduler
                    // all own validation and persistence that is not modelled.
                    return RejectUnsupportedNativeApi(out result);

                case "inputdialog":                 // TPlayer 0x72B910（TPsNpc 0x734944 也注册）
                    // TPlayer 版声明少一个 Hum 形参：InputDialog(MsgStr, DlgType, InputType)。
                    if (args.Count >= 2 && CurrentNpc != null)
                    {
                        var inputMsg = args[0].AsString();
                        var inputDlgType = args[1].AsInt();
                        var inputKind = args.Count >= 3 ? args[2].AsInt() : 0;
                        CurrentPlayer.SendDefMessage(Grobal2.SM_MERCHANT_QUERY,
                            CurrentNpc.ObjectId, inputKind, inputDlgType, 0, inputMsg);
                    }
                    return true;

                case "querytaskdispatch":           // TPlayer 0x72B9B8
                {
                    var acceptCnt = GetGlobalVar(100, 2);
                    var dispatchCnt = GetGlobalVar(100, 3);
                    result = PasValue.FromInt(dispatchCnt.AsInt() >= acceptCnt.AsInt() ? 0 : 1);
                    return true;
                }

                case "requestguildwar":             // TPlayer 0x72B321
                    // RequestGuildWar(TargGuildStr)
                    if (CurrentPlayer?.m_MyGuild != null)
                    {
                        var warCastle = M2Share.CastleManager.GetCastle(0);
                        if (warCastle != null)
                            warCastle.AddAttackerInfo(CurrentPlayer.m_MyGuild);
                    }
                    return true;

                case "agreebaishi":
                    result = PasValue.FromBool(TryAgreeBaiShi());
                    return true;

                case "groupflyex":
                    var groupFlyMap = args.Count >= 1 ? args[0].AsString() : string.Empty;
                    if (args.Count >= 1)
                        MoveCurrentGroupToMap(groupFlyMap);
                    result = PasValue.FromInt(CountCurrentGroupOnMap(groupFlyMap));
                    return true;

                case "psybconsum":
                    result = PasValue.FromBool(args.Count == 5
                        && args[0].ObjVal is NormNpc purchaseNpc
                        && NativePasYbPurchaseService.TrySubmitNormal(
                            CurrentPlayer, purchaseNpc, args[1].AsString(),
                            args[2].AsInt(), args[3].AsInt(), args[4].AsInt()));
                    return true;

                case "psybconsumex":
                    var executionTag = args.Count == 6 ? args[0].AsInt() : -1;
                    result = PasValue.FromBool(executionTag is >= byte.MinValue
                        and <= byte.MaxValue
                        && NativePasYbPurchaseService.TrySubmitYbShop(
                            CurrentPlayer, (byte)executionTag,
                            args[1].AsString(), args[2].AsString(),
                            args[3].AsInt(), args[4].AsInt(), args[5].AsInt()));
                    return true;

                case "chghair":
                    result = PasValue.FromBool(false);
                    if (args.Count >= 1)
                    {
                        var hair = args[0].AsInt();
                        if (hair is 0 or 1)
                        {
                            CurrentPlayer.m_btHair = (byte)hair;
                            CurrentPlayer.FeatureChanged();
                            result = PasValue.FromBool(true);
                        }
                    }
                    return true;

                case "flytodynroom":
                    if (args.Count != 3
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromInt(
                        M2Share.DynamicRoomService.FlyToDynamicRoom(
                            CurrentPlayer, args[0].AsString(),
                            args[1].AsInt(), args[2].AsInt()));
                    return true;

                case "flytodynenvirwithidx":
                    if (args.Count != 4
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromBool(
                        M2Share.DynamicRoomService.FlyToDynamicRoomIndex(
                            CurrentPlayer, args[0].AsString(),
                            args[1].AsInt(), args[2].AsInt(),
                            args[3].AsInt()));
                    return true;

                case "createhero":
                    result = PasValue.FromInt(args.Count >= 3
                        ? HeroDataService.RequestCreate(CurrentPlayer,
                            args[0].AsString(), args[1].AsInt(), args[2].AsInt())
                        : -4);
                    return true;

                // HeroRename 原生只注册在 TPsNpc 0x734E90 上，已搬到 CallNpcFunc。

                case "getmypositioninguild":
                    result = PasValue.FromInt(CurrentPlayer.m_MyGuild == null
                        ? -1 : CurrentPlayer.m_nGuildRankNo);
                    return true;

                case "reqcastlewar":
                    if (CurrentPlayer.m_MyGuild != null)
                    {
                        var castle = M2Share.CastleManager.GetCastle(0);
                        if (castle != null)
                        {
                            castle.AddAttackerInfo(CurrentPlayer.m_MyGuild);
                            result = PasValue.FromBool(true);
                            return true;
                        }
                    }
                    result = PasValue.FromBool(false);
                    return true;

                case "createmon":
                    if (args.Count >= 6)
                    {
                        var mapName = args[0].AsString();
                        var environment = string.IsNullOrEmpty(mapName)
                            ? CurrentPlayer.m_PEnvir
                            : M2Share.MapManager.FindMap(mapName);
                        var x = (short)args[1].AsInt();
                        var y = (short)args[2].AsInt();
                        if (x <= 0) x = CurrentPlayer.m_nCurrX;
                        if (y <= 0) y = CurrentPlayer.m_nCurrY;
                        var range = Math.Max(0, args[3].AsInt());
                        var monsterName = args[4].AsString();
                        var count = Math.Clamp(args[5].AsInt(), 0, 200);
                        for (var index = 0; index < count; index++)
                        {
                            var spawnX = range == 0 ? x : (short)(x - range +
                                M2Share.RandomNumber.Random(range * 2 + 1));
                            var spawnY = range == 0 ? y : (short)(y - range +
                                M2Share.RandomNumber.Random(range * 2 + 1));
                            M2Share.UserEngine.RegenMonsterByName(
                                environment, spawnX, spawnY, monsterName);
                        }
                    }
                    return true;

                case "getmember":
                    if (CurrentPlayer.m_GroupMembers != null && args.Count > 0)
                    {
                        var index = args[0].AsInt();
                        result = index >= 0 && index < CurrentPlayer.m_GroupMembers.Count
                            ? PasValue.FromObject(CurrentPlayer.m_GroupMembers[index])
                            : PasValue.Nil;
                    }
                    return true;

                // GetMemberCount sub_728184 = `mov eax,[eax+44h]; ret` —— 逐字节忠实。
                // 原生注册在 TBaseGroup PAS 面(registrar sub_731350,class 全局
                // 0x00726AB8 = TBaseGroup),与 GetMember sub_72773C 成对;C# 已把
                // GetMember 折叠到玩家面(小组由队长 TPlayObject 持有),此处照同一约定。
                // +0x44 = 成员计数(构造 @0x726BA9 置 1;移除路径 @0x726F52 `dec [ebx+44h]`,
                // 成员数组自 +0x48 起以 4 字节步进),即 C# 的 m_GroupMembers.Count。
                // 未组队时原生 group 指针为空、脚本拿不到对象;C# 以 0 表示无小组。
                case "getmembercount":
                    result = PasValue.FromInt(
                        CurrentPlayer.m_GroupOwner is TPlayObject groupCountOwner
                        && groupCountOwner.m_GroupMembers != null
                            ? groupCountOwner.m_GroupMembers.Count
                            : 0);
                    return true;

                case "getownmapdesc":
                    result = PasValue.FromString(CurrentPlayer.m_PEnvir?.sMapDesc ??
                        CurrentPlayer.m_sMapFileName ?? string.Empty);
                    return true;

                // === Variable System (This_Player.GetV/SetV etc.) ===
                case "getv":
                    result = args.Count >= 2 ? GetPlayerVar('V', args[0].AsInt(), args[1].AsInt()) : PasValue.FromInt(NativeScriptVarMiss);
                    return true;

                case "setv":
                    if (args.Count >= 3)
                    {
                        SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]);
                        result = PasValue.FromBool(true);
                    }
                    else result = PasValue.FromBool(false);
                    return true;

                case "gets":
                    result = args.Count >= 2 ? GetPlayerVar('S', args[0].AsInt(), args[1].AsInt()) : PasValue.FromInt(NativeScriptVarMiss);
                    return true;

                case "sets":
                    if (args.Count >= 3)
                    {
                        SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]);
                        result = PasValue.FromBool(true);
                    }
                    else result = PasValue.FromBool(false);
                    return true;

                case "groupsetv":
                    // sub_6E0830 answers False for an ungrouped caller (6E083F je), so the
                    // Boolean the script sees has to come from SetGroupPlayerVar itself.
                    result = PasValue.FromBool(args.Count >= 3
                        && SetGroupPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]));
                    return true;

                case "groupsets":
                    if (args.Count >= 3)
                    {
                        SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]);
                        result = PasValue.FromBool(true);
                    }
                    else result = PasValue.FromBool(false);
                    return true;

                // === Item Operations with return values ===
                case "give":
                    result = PasValue.FromBool(args.Count >= 2 &&
                        TryNativeGive(args[0].AsString(), args[1].AsInt(), false, true));
                    return true;

                case "bindgive":
                    result = PasValue.FromBool(args.Count >= 2 &&
                        TryNativeGive(args[0].AsString(), args[1].AsInt(), true, false));
                    return true;

                case "loopgive":
                    result = PasValue.FromBool(TryNativeLoopGive(args));
                    return true;

                case "take":
                    result = PasValue.FromBool(TakeItems(args[0].AsString(), args[1].AsInt()));
                    return true;

                case "takeex":
                    result = PasValue.FromBool(ExecuteNativeTakeEx(args));
                    return true;

                case "checkbagitem":
                    result = PasValue.FromBool(CountBagItem(args[0].AsString()) >= args[1].AsInt());
                    return true;

                case "checkbagitemex":
                    // checkBagItemEx(itemName, var chkNum, var chkDura, var chkAllDura): cardinal
                    // Iterate bag items, return count, expose first matched item's durability info.
                    {
                        var cbieName = args[0].AsString();
                        int cbieCnt = 0;
                        ushort cbieDura = 0;
                        ushort cbieAllDura = 0;
                        bool cbieFoundFirst = false;
                        for (int i = 0; i < CurrentPlayer.m_ItemList.Count; i++)
                        {
                            var item = CurrentPlayer.m_ItemList[i];
                            if (item != null && string.Equals(
                                M2Share.UserEngine.GetStdItemName(item.wIndex), cbieName,
                                StringComparison.OrdinalIgnoreCase))
                            {
                                cbieCnt++;
                                if (!cbieFoundFirst)
                                {
                                    cbieDura = item.Dura;
                                    cbieAllDura = item.DuraMax;
                                    cbieFoundFirst = true;
                                }
                            }
                        }
                        // Set durability into args[1]/[2]/[3] if those slots exist (var outputs)
                        // NOTE: var parameter semantics broken for struct PasValue — these
                        // assignments modify the local List copy, not the caller's variables.
                        if (args.Count >= 4)
                        {
                            args[1] = PasValue.FromInt(cbieCnt);
                            args[2] = PasValue.FromInt(cbieDura);
                            args[3] = PasValue.FromInt(cbieAllDura);
                        }
                        result = PasValue.FromInt(cbieCnt);
                    }
                    return true;

                case "getbagitemcount":
                    {
                        var itemName = args[0].AsString();
                        // Check for yanshen tunnel command first
                        if (TryExecuteTunnelCommand(itemName, method, out var tunnelResult))
                        {
                            result = PasValue.FromInt(tunnelResult);
                            return true;
                        }
                        result = PasValue.FromInt(CountBagItem(itemName));
                    }
                    return true;

                case "getbagitemcountex":
                    {
                        var itemName = args[0].AsString();
                        if (TryExecuteTunnelCommand(itemName, method, out var tunnelResult))
                        {
                            result = PasValue.FromInt(tunnelResult);
                            return true;
                        }
                        result = PasValue.FromInt(CountBagItem(itemName));
                    }
                    return true;

                case "getstoragespacecount":
                    // GetStorageSpaceCount (native 0x72BBE0) returns the raw
                    // signed 32-bit runtime field; it does not apply the
                    // physical 24..192 storage-record bounds.
                    result = PasValue.FromInt(CurrentPlayer.m_nStorageSpaceCount);
                    return true;

                // === Money Operations with return values ===
                case "addgold":
                    {
                        var ok = args.Count >= 1 && CurrentPlayer.IncGold(args[0].AsInt());
                        if (ok) CurrentPlayer.GoldChanged();
                        result = PasValue.FromBool(ok);
                    }
                    return true;

                case "decgold":
                    {
                        var ok = args.Count >= 1 && CurrentPlayer.DecGold(args[0].AsInt());
                        if (ok) CurrentPlayer.GoldChanged();
                        result = PasValue.FromBool(ok);
                    }
                    return true;

                case "checkgold":
                    result = PasValue.FromBool(CurrentPlayer.m_nGold >= args[0].AsInt());
                    return true;

                case "checklevel":
                    result = PasValue.FromBool(CurrentPlayer.m_Abil.Level >= args[0].AsInt());
                    return true;

                case "checkjob":
                    result = PasValue.FromBool(CurrentPlayer.m_btJob == args[0].AsInt());
                    return true;

                case "addlf":
                case "addlimlf":
                case "declf":
                    // See the method dispatcher: the native LingFu transaction is absent.
                    return RejectUnsupportedNativeApi(out result);

                case "addglorypoint":
                    if (args.Count != 1) return false;
                    result = PasValue.FromBool(
                        TryAddNativeGloryPoint(args[0].AsInt()));
                    return true;

                case "decglorypoint":
                    if (args.Count != 5) return false;
                    result = PasValue.FromBool(CurrentPlayer.DecNativeGloryPoint(
                        args[0].AsInt(), args[1].AsInt(), args[2].AsInt(),
                        args[3].AsBool(), args[4].AsString()));
                    return true;

                case "takediamond":
                    if (args.Count != 2) return false;
                    result = PasValue.FromBool(
                        CurrentPlayer.TakeNativeDiamond(args[0].AsInt()));
                    return true;

                case "checkdiamond":
                    return RejectUnsupportedNativeApi(out result);

                // CheckGameGold 四重 0 命中，原生注册表里没有任何 CheckGameGold /
                // PsShop* / GameGold 命名的条目。元宝余额判断在原生走的是别的入口。
                case "checkgamegold":
                    return RejectUnsupportedNativeApi(out result);

                // === Teleport with return values ===
                case "flyto":
                    // Native Flyto (sub_6DEF8C): exact tile when x!=0 && y!=0, else
                    // fallback sub_768C7C(self, map, 1, 1) placeholder coords.
                    if (args.Count >= 3)
                    {
                        var flyMap = args[0].AsString();
                        var flyX = (short)args[1].AsInt();
                        var flyY = (short)args[2].AsInt();
                        if (flyX != 0 && flyY != 0)
                            CurrentPlayer.SpaceMove(flyMap, flyX, flyY, 0);
                        else
                            CurrentPlayer.SpaceMove(flyMap, 1, 1, 0);
                    }
                    return true;

                case "canwalkxy":
                    // Check if position is walkable
                    if (args.Count >= 3)
                    {
                        var map = M2Share.MapManager.FindMap(args[0].AsString());
                        if (map != null)
                            result = PasValue.FromBool(map.CanWalk(args[1].AsInt(), args[2].AsInt(), false));
                    }
                    return true;

                // === Check operations ===
                case "activeauthen":
                    if (args.Count != 2 || args[0].AsInt() != 1 ||
                        args[1].AsInt() != 100)
                        return false;
                    result = PasValue.FromInt(
                        CurrentPlayer.ActiveNativeAuthentication100());
                    return true;

                case "helpotherauthen":
                    if (args.Count != 0)
                        return RejectUnsupportedNativeApi(out result);
                    result = PasValue.FromInt(
                        CurrentPlayer.HelpOtherNativeAuthentication());
                    return true;

                case "checkauthen":
                    result = PasValue.FromBool(args.Count >= 2 &&
                        CurrentPlayer.CheckNativeAuthentication(
                            args[0].AsInt(), args[1].AsInt()));
                    return true;

                case "activedelauthen":
                    // Native ActiveDelAuthen (sub_6F9888) — the delete-mirror of activeauthen
                    // (function form). Clears the order-1 auth byte + persists; codes match the
                    // native ladder (0 disabled / 1 ok / 2 already-cleared / persist result).
                    if (args.Count != 2 || args[0].AsInt() != 1 ||
                        args[1].AsInt() != 100)
                        return false;
                    result = PasValue.FromInt(
                        CurrentPlayer.DelActiveNativeAuthentication100());
                    return true;

                case "checkcurrmaphum":
                    if (CurrentPlayer.m_PEnvir != null)
                        result = PasValue.FromInt(CurrentPlayer.m_PEnvir.HumCount);
                    return true;

                case "checkothermaphum":
                    if (args.Count >= 1)
                    {
                        var map = M2Share.MapManager.FindMap(args[0].AsString());
                        result = map != null ? PasValue.FromInt(map.HumCount) : PasValue.FromInt(0);
                    }
                    return true;

                case "checkcurrmapmon":
                    if (CurrentPlayer.m_PEnvir != null)
                    {
                        var monsterList = new List<TBaseObject>();
                        M2Share.UserEngine.GetMapMonster(CurrentPlayer.m_PEnvir, monsterList);
                        result = PasValue.FromInt(monsterList.Count);
                    }
                    return true;

                case "checkmapmonbyname":
                    if (args.Count >= 2)
                    {
                        if (TryNpcCreatMonsTunnel(args[0].AsString(), args[1].AsString(), out var spawned))
                        {
                            result = PasValue.FromInt(spawned);
                            return true;
                        }
                        var map = M2Share.MapManager.FindMap(args[0].AsString());
                        if (map != null)
                        {
                            var monName = args[1].AsString();
                            int count = 0;
                            var monsterList = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapMonster(map, monsterList);
                            for (int i = 0; i < monsterList.Count; i++)
                            {
                                var mon = monsterList[i];
                                if (mon != null && mon.m_sCharName != null &&
                                    mon.m_sCharName.IndexOf(monName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    count++;
                            }
                            result = PasValue.FromInt(count);
                        }
                    }
                    return true;

                case "checkskill":
                    // CheckSkill(skillId): Integer - returns skill level or 0
                    if (args.Count >= 1)
                    {
                        var magic = CurrentPlayer.GetMagicInfo(args[0].AsInt());
                        result = PasValue.FromInt(magic != null ? magic.btLevel : 0);
                    }
                    return true;

                case "checkheroskill":
                    // CheckHeroSkill(skillId): Integer - returns hero skill level or 0
                    if (args.Count >= 1)
                    {
                        var magic = CurrentPlayer.GetMagicInfo(args[0].AsInt());
                        result = PasValue.FromInt(magic != null ? magic.btLevel : 0);
                    }
                    return true;

                case "ischeckbodyitem":
                    // Check if specific item is equipped
                    if (args.Count >= 1)
                    {
                        var itemName = args[0].AsString();
                        for (int i = 0; i < CurrentPlayer.m_UseItems.Length; i++)
                        {
                            var item = CurrentPlayer.m_UseItems[i];
                            if (item != null)
                            {
                                var stdName = M2Share.UserEngine.GetStdItemName(item.wIndex);
                                if (string.Equals(stdName, itemName, StringComparison.OrdinalIgnoreCase))
                                { result = PasValue.FromBool(true); return true; }
                            }
                        }
                                            }
                    return true;

                case "ismale":
                    result = PasValue.FromBool((byte)CurrentPlayer.m_btGender == 0);
                    return true;

                case "isfemale":
                    result = PasValue.FromBool((byte)CurrentPlayer.m_btGender == 1);
                    return true;

                case "ischeckmarry":
                    // Check if married
                    result = PasValue.FromBool(!string.IsNullOrEmpty(CurrentPlayer.m_sDearName));
                    return true;

                case "isguildlord":
                    result = PasValue.FromBool(CurrentPlayer.m_MyGuild != null &&
                        string.Equals(CurrentPlayer.m_MyGuild.GetChiefName(), CurrentPlayer.m_sCharName, StringComparison.OrdinalIgnoreCase));
                    return true;

                case "isfirstguildlord":
                    // IsFirstGuildLord: Boolean - check if player is the first guild leader (rank 1, member 0)
                    // In the Guild system, IsFirstGuildLord means the player is the original founder/chief
                    result = PasValue.FromBool(
                        CurrentPlayer.m_MyGuild != null &&
                        CurrentPlayer.m_nGuildRankNo == 1 &&
                        string.Equals(CurrentPlayer.m_MyGuild.GetChiefName(),
                            CurrentPlayer.m_sCharName, StringComparison.OrdinalIgnoreCase));
                    return true;

                case "iscastle":
                    // IsCastle: Boolean - check if player is currently in a castle map
                    if (CurrentPlayer.m_PEnvir != null)
                    {
                        var castle = M2Share.CastleManager.IsCastleEnvir(CurrentPlayer.m_PEnvir);
                        result = PasValue.FromBool(castle != null);
                        return true;
                    }
                    else
                    {
                        result = PasValue.FromBool(false);
                        return true;
                    }

                case "isastudent": case "isstudent":
                    result = PasValue.FromBool(CurrentPlayer.m_boStudent);
                    return true;

                case "isexistarcher":
                    // Native API belongs to the Magic Tower challenge, not castle guards.
                    return RejectUnsupportedNativeApi(out result);

                // === Status checks ===
                case "isdead":
                                        return true;

                case "chkstrinfile":
                    // ChkStrInFile(fileName, searchStr): Boolean
                    // Delegates to the standalone ChkStrInFile implementation
                    if (args.Count >= 2)
                    {
                        if (CallStandaloneFunction("chkstrinfile", args, out var csfResult))
                        { result = csfResult; return true; }
                    }
                                        return true;

                case "getactivepoint":
                    result = PasValue.FromInt(unchecked(CurrentPlayer.m_nActivePoint
                        + (M2Share.ActivityPointManager?.Calculate(CurrentPlayer) ?? 0)));
                    return true;

                case "incactivepoint":
                    if (args.Count < 1) return false;
                    result = PasValue.FromInt(CurrentPlayer.IncActivePoint(args[0].AsInt()));
                    return true;

                case "decactivepoint":
                    if (args.Count < 1) return false;
                    result = PasValue.FromInt(CurrentPlayer.DecActivePoint(args[0].AsInt()));
                    return true;

                case "signin":
                    if (args.Count != 0 || CurrentPlayer == null ||
                        M2Share.SignActManager == null)
                        return RejectUnsupportedNativeApi(out result);
                    result = PasValue.FromBool(M2Share.SignActManager.SignIn(
                        M2Share.ServerSwitches.IsBitSet(2, 0x40),
                        CurrentPlayer.m_sCharName));
                    return true;

                case "getsigninactprize":
                    if (args.Count != 0 || CurrentPlayer == null ||
                        M2Share.SignActManager == null)
                        return RejectUnsupportedNativeApi(out result);
                    result = PasValue.FromInt(M2Share.SignActManager.Claim(
                        CurrentPlayer.m_sCharName));
                    return true;

                case "getsigninactprizer":
                    // The eye-plugin compatibility tunnel owns its exact marker.
                    // Ordinary native calls use two var-string output arguments.
                    if (IsYanshenSignInTunnelCall(args))
                        return TryCallYanshenSignInTunnel(args, out result);
                    if (args.Count != 2 || M2Share.SignActManager == null)
                        return RejectUnsupportedNativeApi(out result);
                    var signActWinners = M2Share.SignActManager.GetWinners();
                    args[0] = PasValue.FromString(signActWinners.Lucky1);
                    args[1] = PasValue.FromString(signActWinners.Lucky2);
                    result = PasValue.FromString(signActWinners.Primary);
                    return true;

                case "getsignindayactprizer":
                    if (args.Count != 1 || CurrentPlayer == null ||
                        M2Share.SignActManager == null)
                        return RejectUnsupportedNativeApi(out result);
                    result = PasValue.FromString(
                        M2Share.SignActManager.GetEverydayWinners(
                            args[0].AsInt()));
                    return true;

                case "gettmpactivepoint":
                    result = PasValue.FromInt(
                        M2Share.ActivityPointManager?.Calculate(CurrentPlayer) ?? 0);
                    return true;

                case "getdynroomhumcnt":
                case "getdynroomhumnum":
                case "gethavedynroomcnt":
                case "getdynroomcnt":
                case "pshavefreedynroom":
                case "psisdynroomvalid":
                    // Native declarations are global functions, not TPlayer
                    // methods. Keep object-method shadows unavailable.
                    return RejectUnsupportedNativeApi(out result);

                // === Equipment operations ===
                case "getweaponlucky":
                    // GetWeaponLucky: Integer - read lucky value from equipped weapon (btValue[3])
                    {
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0 && weapon.btValue != null && weapon.btValue.Length > 3)
                            result = PasValue.FromInt(weapon.btValue[3]);
                        else
                            result = PasValue.FromInt(0);
                    }
                    return true;

                case "getnecklacelucky":
                    // GetNecklaceLucky: Integer - read lucky value from equipped necklace (btValue[3])
                    {
                        var necklace = CurrentPlayer.m_UseItems[Grobal2.U_NECKLACE];
                        if (necklace != null && necklace.wIndex > 0 && necklace.btValue != null && necklace.btValue.Length > 3)
                            result = PasValue.FromInt(necklace.btValue[3]);
                        else
                            result = PasValue.FromInt(0);
                    }
                    return true;

                case "upweaponnobroken":
                    // FUN_006e3044: Delphi weapon upgrade formula (no-break variant)
                    // param: upType (0=DC,1=SC,2=MC), bSureSuc, bHero
                    // Returns: 0=success, 1=break, 2=maxUpgradeExceeded, 3=noWeapon
                    {
                        int upType = args.Count >= 1 ? args[0].AsInt() : 0;
                        bool bSureSuc = args.Count >= 2 && args[1].AsBool();
                        bool bHero = args.Count >= 3 && args[2].AsBool();
                        if (upType < 0 || upType > 2)
                        {
                            result = PasValue.FromInt(2);
                            return true;
                        }
                        var weapon = bHero && CurrentPlayer.m_HeroObject != null
                            ? CurrentPlayer.m_HeroObject.m_UseItems[Grobal2.U_WEAPON]
                            : CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon == null || weapon.wIndex <= 0)
                        {
                            result = PasValue.FromInt(3);
                            return true;
                        }
                        int currentTotal = weapon.btValue[0] + weapon.btValue[1] + weapon.btValue[2];
                        if (currentTotal >= M2Share.g_Config.nUpgradeWeaponMaxPoint)
                        {
                            result = PasValue.FromInt(2);
                            return true;
                        }
                        if (bSureSuc)
                        {
                            weapon.btValue[upType] = (byte)Math.Min(255, weapon.btValue[upType] + 1);
                            CurrentPlayer.RecalcAbilitys();
                            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                            result = PasValue.FromInt(0);
                        }
                        else
                        {
                            // Delphi formulas from FUN_006e3044:
                            // DC: 1000 / (baseDc + currentDc + 3*currentDc² + 1)
                            // SC: 525 / (baseSc + currentSc + currentDc³/2 + 1)
                            // MC: 525 / (baseMc + currentMc + currentDc³/2 + 1)
                            var stdItem = M2Share.UserEngine.GetStdItem(weapon.wIndex);
                            int baseDc = stdItem?.Dc ?? 0;
                            int baseSc = stdItem?.Sc ?? 0;
                            int baseMc = stdItem?.Mc ?? 0;
                            int curDc = weapon.btValue[0];
                            int curSc = weapon.btValue[1];
                            int curMc = weapon.btValue[2];
                            int curDcCubedHalf = (curDc * curDc * curDc) / 2;

                            int upgradeRate;
                            if (upType == 0) // DC upgrade
                                upgradeRate = 1000 / (baseDc + curDc + 3 * curDc * curDc + 1);
                            else if (upType == 1) // SC upgrade
                                upgradeRate = 525 / (baseSc + curSc + curDcCubedHalf + 1);
                            else // MC upgrade
                                upgradeRate = 525 / (baseMc + curMc + curDcCubedHalf + 1);

                            bool upgraded = false;
                            // Body luck check (原版 sub_6E3044): crit(double upgrade) 当 Random(1000) < [+0x164]+10。
                            // [+0x164] 即 m_nBodyLuckLevel(小值 [-10,+5]，与武器升级 Merchant.cs:1045 / 防御幸运
                            // NativeMagicDamage.cs:246 同源)；原 C# 误读 m_dBodyLuck(×5000 累加器)导致幸运玩家几乎必暴击，
                            // 改读 level 与原版及其它消费端一致。(证据: gm-playerattr staging/gm_player_attr_commands_20260801.md)
                            if (CurrentPlayer.m_nBodyLuckLevel + 10 > M2Share.RandomNumber.Random(1000))
                            {
                                weapon.btValue[upType] = (byte)Math.Min(255, weapon.btValue[upType] + 2);
                                upgraded = true;
                            }
                            else if (M2Share.RandomNumber.Random(1000) < upgradeRate)
                            {
                                weapon.btValue[upType] = (byte)Math.Min(255, weapon.btValue[upType] + 1);
                                upgraded = true;
                            }

                            if (upgraded)
                            {
                                CurrentPlayer.RecalcAbilitys();
                                CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                                CurrentPlayer.SysMsg(M2Share.sTheWeaponRefineSuccessfull, MsgColor.Green, MsgType.Hint);
                            }

                            // Break check: Random(1000) < upgradeTotal (DC+SC+MC)
                            if (M2Share.RandomNumber.Random(1000) < currentTotal)
                            {
                                // Break weapon
                                var oldWeapon = new TUserItem(weapon);
                                CurrentPlayer.SendDelItems(oldWeapon);
                                weapon.wIndex = 0;
                                CurrentPlayer.SendRefMsg(Grobal2.RM_BREAKWEAPON, 0, 0, 0, 0, "");
                                CurrentPlayer.SysMsg(M2Share.g_sTheWeaponBroke, MsgColor.Red, MsgType.Hint);
                                result = PasValue.FromInt(1);
                                return true;
                            }
                            result = PasValue.FromInt(0); // 0 = success (no break occurred)
                        }
                    }
                    return true;

                case "getequipmentbreaklevel":
                    // GetEquipmentBreakLevel: Integer - read break/upgrade level from equipped weapon (btValue[4])
                    {
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0 && weapon.btValue != null && weapon.btValue.Length > 4)
                            result = PasValue.FromInt(weapon.btValue[4]);
                        else
                            result = PasValue.FromInt(0);
                    }
                    return true;

                case "getequipmentmascotlevel":
                    // GetEquipmentMascotLevel: Integer - read mascot level from equipped item (ys1 field)
                    {
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0)
                            result = PasValue.FromInt(weapon.ys1);
                        else
                            result = PasValue.FromInt(0);
                    }
                    return true;

                case "getequipmentmascotlevelhero":
                    // GetEquipmentMascotLevelHero: Integer - hero's mascot level
                    {
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0)
                            result = PasValue.FromInt(weapon.ys1);
                        else
                            result = PasValue.FromInt(0);
                    }
                    return true;

                // === Skill operations ===
                case "getskilllevel":
                    if (args.Count >= 1)
                    {
                        var magic = CurrentPlayer.GetMagicInfo(args[0].AsInt());
                        result = PasValue.FromInt(magic != null ? magic.btLevel : 0);
                    }
                    return true;

                case "getskilllevelext":
                case "getskilllevelbyscript":
                    result = PasValue.FromInt(-1);
                    if (args.Count >= 2)
                    {
                        TBaseObject owner = CurrentPlayer;
                        if (args[1].AsBool())
                        {
                            var hero = CurrentPlayer.m_HeroObject;
                            if (hero == null ||
                                (method.Equals("getskilllevelbyscript",
                                     StringComparison.OrdinalIgnoreCase) &&
                                 hero.m_boGhost))
                                return true;
                            owner = hero;
                        }

                        var magicInfo = owner is HeroObject
                            ? M2Share.UserEngine.FindHeroMagic(
                                args[0].AsString())
                            : M2Share.UserEngine.FindMagic(
                                args[0].AsString());
                        var magic = magicInfo == null
                            ? null
                            : owner.GetMagicInfo(magicInfo.wMagicID);
                        if (magic != null)
                            result = PasValue.FromInt(magic.btLevel);
                    }
                    return true;

                case "gethumanskillblevelbyscript":
                    if (args.Count >= 1)
                    {
                        var magic = CurrentPlayer.GetMagicInfo(args[0].AsInt());
                        result = PasValue.FromInt(magic != null ? magic.btLevel : 0);
                    }
                    return true;

                case "addskillexp":
                    result = PasValue.FromBool(args.Count >= 2 &&
                        TryAddPlayerSkillExp(args[0].AsString(), args[1].AsInt()));
                    return true;

                case "addheroskillexp":
                    result = PasValue.FromBool(args.Count >= 2 &&
                        CurrentPlayer.m_HeroObject != null &&
                        !CurrentPlayer.m_HeroObject.m_boDeath &&
                        !CurrentPlayer.m_HeroObject.m_boGhost &&
                        CurrentPlayer.m_HeroObject.AddHeroSkillExp(
                            args[0].AsString(), args[1].AsInt()));
                    return true;

                case "learnmagic":
                    result = PasValue.FromBool(args.Count >= 1 &&
                        TryLearnPlayerMagic(args[0].AsString()));
                    return true;

                case "learnskillbyscript":
                    result = PasValue.FromBool(args.Count >= 3 &&
                        TryLearnSkillByScript(args[0].AsString(),
                            args[1].AsBool(), args[2].AsInt()));
                    return true;

                case "upgradeskillbyscript":
                    result = PasValue.FromBool(false);
                    if (args.Count >= 2)
                    {
                        var heroSkill = args[1].AsBool();
                        var upgraded = heroSkill
                            ? CurrentPlayer.m_HeroObject != null &&
                              !CurrentPlayer.m_HeroObject.m_boGhost &&
                              CurrentPlayer.m_HeroObject.UpgradeHeroMagic(args[0].AsString())
                            : TryUpgradePlayerMagic(args[0].AsString());
                        result = PasValue.FromBool(upgraded);
                    }
                    return true;

                case "upgradeheroskilllv":
                    result = PasValue.FromBool(args.Count >= 1 &&
                        CurrentPlayer.m_HeroObject != null &&
                        !CurrentPlayer.m_HeroObject.m_boGhost &&
                        CurrentPlayer.m_HeroObject.UpgradeHeroMagic(args[0].AsString()));
                    return true;

                case "deleteskill":
                    result = PasValue.FromBool(false);
                    if (args.Count >= 1)
                    {
                        var magicInfo = M2Share.UserEngine.FindMagic(args[0].AsString());
                        var magic = magicInfo == null
                            ? null
                            : CurrentPlayer.GetMagicInfo(magicInfo.wMagicID);
                        if (magic != null)
                        {
                            CurrentPlayer.SendDelMagic(magic);
                            CurrentPlayer.m_MagicList.Remove(magic);
                            if (magic.wMagIdx < CurrentPlayer.m_MagicArr.Length)
                                CurrentPlayer.m_MagicArr[magic.wMagIdx] = null;
                            CurrentPlayer.RecalcAbilitys();
                            result = PasValue.FromBool(true);
                        }
                    }
                    return true;

                case "chgskillv": case "chgskilllv":
                    result = PasValue.FromBool(args.Count >= 3 &&
                        TrySetScriptMagicLevel(CurrentPlayer, args[0].AsString(),
                            args[1].AsInt(), args[2].AsInt()));
                    return true;

                case "upgradeheroskill":
                    {
                        // UpGradeHeroSkill(skill_idx: Integer, skill_exp: Integer): Boolean
                        var hero = CurrentPlayer.m_HeroObject;
                        if (args.Count >= 2 && hero != null && !hero.m_boDeath && !hero.m_boGhost)
                        {
                            var skillIdx = args[0].AsInt();
                            var skillExp = args[1].AsInt();
                            var heroMagic = hero.GetMagicInfo(skillIdx);
                            if (heroMagic != null)
                            {
                                hero.TrainSkill(heroMagic, skillExp);
                                hero.CheckMagicLevelup(heroMagic);
                                result = PasValue.FromBool(true);
                                return true;
                            }
                        }
                        result = PasValue.FromBool(false);
                    }
                    return true;

                case "calloutex":
                    return ScheduleCallOut(args, true);

                case "callout":
                    return ScheduleCallOut(args, false);

                case "updatetaskdetail":
                    if (args.Count != 2) return false;
                    CurrentPlayer.UpdateTaskDetail(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "deletetaskfromuilist":
                    if (args.Count != 2) return false;
                    CurrentPlayer.DeleteTaskFromUIList(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "giveconfigprize":
                    // The native function form is a separate RTTI symbol and
                    // remains closed until its return contract is recovered.
                    return RejectUnsupportedNativeApi(out result);

                case "notifyclientcommititem":
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        var commitResult = args[0].AsInt();
                        var message = args.Count >= 2 ? args[1].AsString() : string.Empty;
                        CurrentPlayer.SendDefMessage((short)Grobal2.SM_COMMIT_ITEM,
                            0, commitResult, 0, 0, message);
                        result = PasValue.FromBool(true);
                    }
                    else result = PasValue.FromBool(false);
                    return true;

                case "sysgivegift":
                    result = PasValue.FromBool(TrySysGiveGift(args));
                    return true;

                case "reqopenstorage":
                    // Open storage UI: send save item list to client (return-value variant)
                    if (CurrentNpc != null)
                    {
                        CurrentPlayer.m_nStoragePage = 0;
                        CurrentPlayer.SendSaveItemList(CurrentNpc.ObjectId);
                    }
                                        return true;

                case "expandstoragespace":
                    if (args.Count != 1) return false;
                    result = PasValue.FromInt(ExpandStorageSpace(args[0].AsInt()));
                    return true;

                // GetStorageItemCount 四重 0 命中；原生的仓库计数是
                // GetStorageSpaceCount 0x72BBE0 / GetAccountStorageCnt 0x72B6D5。
                // 此前这里恒返回 0，脚本无法区分「没有」与「不支持」。
                case "getstorageitemcount":
                    return RejectUnsupportedNativeApi(out result);

                case "findplayerbyname":
                    // Native signature returns TPlayer, not an object ID.
                    if (args.Count >= 1)
                    {
                        var p = M2Share.UserEngine.GetPlayObject(args[0].AsString());
                        CurrentPlayer.m_TargetPlayer = p;
                        result = PasValue.FromObject(p);
                    }
                    return true;

                case "getplayergender":
                    // GetPlayerGender(sHumanName): returns gender (-1 if offline)
                    if (args.Count >= 1)
                    {
                        var p = M2Share.UserEngine.GetPlayObject(args[0].AsString());
                        result = p != null ? PasValue.FromInt((int)(byte)p.m_btGender) : PasValue.FromInt(-1);
                    }
                    return true;

                case "findplayer":
                    if (args.Count >= 1)
                    {
                        var p = M2Share.UserEngine.GetPlayObject(args[0].AsString());
                        CurrentPlayer.m_TargetPlayer = p;
                        result = PasValue.FromBool(p != null);
                    }
                    return true;

                case "getitemnameonbody":
                    if (args.Count >= 1)
                    {
                        int pos = args[0].AsInt();
                        if (TryExecuteCastleNameTunnel(pos, out var castleName))
                        {
                            result = PasValue.FromString(castleName);
                            return true;
                        }
                        // 眼神「读取英雄装备」把 50..65 号格改读英雄身上格 0..15
                        // （桩 0x006E04E7，安装点 0x100D533D）。不命中就落回下面的
                        // 原生路径，原生对 >= 16 的格恒返回 nil。
                        if (TryReadHeroEquipName(pos, out var heroEquipName))
                        {
                            result = PasValue.FromString(heroEquipName);
                            return true;
                        }
                        if (pos >= 0 && pos < CurrentPlayer.m_UseItems.Length && CurrentPlayer.m_UseItems[pos] != null)
                        {
                            var stdItem = M2Share.UserEngine.GetStdItem(CurrentPlayer.m_UseItems[pos].wIndex);
                            result = PasValue.FromString(stdItem?.Name ?? "");
                        }
                        else result = PasValue.FromString("");
                    }
                    return true;

                case "getslavecount":
                    result = PasValue.FromInt(CurrentPlayer.m_SlaveList?.Count ?? 0);
                    return true;

                case "makeslave":
                {
                    // Native RTTI @0x72E300:
                    // MakeSlave(name, MagicLv, nCount, RoyaltySec,
                    //           BoFromHero, hpAfterSlave: Integer).
                    if (args.Count != 6 || CurrentPlayer == null)
                        return false;
                    var slave = CurrentPlayer.MakeNativeSlave(
                        args[0].AsString(),
                        args[1].AsInt(),
                        args[2].AsInt(),
                        args[3].AsInt(),
                        args[4].AsBool(),
                        args[5].AsInt());
                    result = PasValue.FromObject(slave);
                    return true;
                }

                case "makeslaveex":
                    // FUN_006bfc20: max 20 slaves, random position, 24h royalty
                    // Delphi offsets: +0x38C=m_Master, +0x482=m_btSlaveMakeLevel(max7),
                    // +0x488=Royalty(86400000ms) -> m_dwMasterRoyaltyTick
                    if (args.Count >= 3 && CurrentPlayer != null)
                    {
                        var monName = args[0].AsString();
                        int count = args[1].AsInt();
                        int level = Math.Min(args[2].AsInt(), 7);
                        if (CurrentPlayer.m_SlaveList.Count >= 20)
                        {
                                                        return true;
                        }
                        for (int i = 0; i < count; i++)
                        {
                            short x = (short)(CurrentPlayer.m_nCurrX - 2 + M2Share.RandomNumber.Random(5));
                            short y = (short)(CurrentPlayer.m_nCurrY - 2 + M2Share.RandomNumber.Random(5));
                            var slave = M2Share.UserEngine.RegenMonsterByName(
                                CurrentPlayer.m_PEnvir, x, y, monName);
                            if (slave != null)
                            {
                                slave.m_Master = CurrentPlayer;
                                // Delphi +0x484=SpawnTime (tick), +0x488=Royalty(86400000ms=24h)
                                slave.m_dwMasterRoyaltyTick = HUtil32.GetTickCount() + 86400000;
                                slave.m_btSlaveMakeLevel = (byte)level;
                                slave.m_btSlaveExpLevel = (byte)level;
                                CurrentPlayer.m_SlaveList.Add(slave);
                                // MakeSlaveEx = FUN_006bfc20 @0x6BFD02 call 0x6F784C -> SM 4469.
                                CurrentPlayer.NotifyNativeSlaveListChanged(joining: true, slave);
                            }
                        }
                                            }
                    return true;

                // === SIGN-IN / DAILY REWARDS (return value) ===
                case "getsignindayacttag":
                    if (args.Count != 0 || CurrentPlayer == null ||
                        M2Share.SignActManager == null)
                        return RejectUnsupportedNativeApi(out result);
                    result = PasValue.FromInt(
                        M2Share.SignActManager.GetYesterdayPrizeTag(
                            CurrentPlayer.m_sCharName));
                    return true;

                // 每日活动排行这一族原生一个都没注册在 TPlayer 上：
                //   GetCurrentEAPeriod / GetCurrentEAIdxByName / GetCurrentEAScoreByIdx /
                //   GetLastEAIdxByName / GetLastEANameByIdx / GetLastEAScoreByIdx
                //     -> TPsNpc 0x734DF4 / 0x734E00 / 0x734E18 / 0x734E24 / 0x734E30 / 0x734E3C
                //   GetCurrentEANameByIdx / GetEAOrderInfo / EAOrderIsStart
                //     -> TPsNpc 0x734E0C / 0x734E48 / 0x734E54 **且** global
                //        0x7299F6 / 0x729A18 / 0x729A3A
                //   GetScoreByName -> 只有 global 0x729A29
                // 已分别搬到 CallNpcFunc 与 CallStandaloneFunction。

                // === CURRENCY (return value) ===
                case "getvexptobeconverted":
                case "incvexptobeconverted":
                case "decvexptobeconverted":
                    return RejectUnsupportedNativeApi(out result);

                case "chgtenyearimpress":
                    return RejectUnsupportedNativeApi(out result);

                case "getvitalityvalue":
                    // Native vitality uses configured, persistent positive/negative pools.
                    return RejectUnsupportedNativeApi(out result);

                case "getplayerhonorvalue":
                    // Native fame-statue state is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === ITEM / GIVE (return value) ===
                case "giveitemwithdura":
                    // Native sub_6E15E0: function form — returns whether any item was
                    // given; durability clamped to min(req, DuraMax).
                    TryGiveItemWithDura(args, out var gaveWithDura);
                    result = PasValue.FromBool(gaveWithDura);
                    return true;

                case "takefullduraitem":
                    if (args.Count >= 2)
                    {
                        result = PasValue.FromInt(NativeTakeFullDuraItem.ExecuteCounting(
                            CurrentPlayer, args[0].AsString(), args[1].AsInt()));
                        return true;
                    }
                    result = PasValue.FromInt(0);
                    return true;

                case "getawarditem":
                    if (args.Count >= 1)
                    {
                        int actOrder = args[0].AsInt();
                        int itemIdx = GetPlayerVarOrZero('V', 50, actOrder);
                        result = itemIdx > 0 ? PasValue.FromString(M2Share.UserEngine.GetStdItemName(itemIdx) ?? "") : PasValue.FromString("");
                    }
                    else result = PasValue.FromString("");
                    return true;

                // === MISC PLAYER METHODS (return value) ===
                case "getvex":
                    // GetVEx = sub_6E9358 (registered @0x731E48). Its OWN miss sentinel is
                    // -100 (0x6E937C `mov esi,0xFFFFFF9C`), used when the named target
                    // player cannot be resolved: 0x6E9381 `cmp [ebp+8],0 / jne` picks Self
                    // for an empty name, otherwise sub_652784 looks the name up on
                    // [0x7D6D50]; 0x6E939A `test eax,eax / je` leaves esi at -100. Only on a
                    // resolved player does 0x6E93A3 delegate to GetV (sub_6DF1E4), whose own
                    // -1 miss (0x6DF1F1) then passes straight through.
                    if (args.Count >= 2)
                        result = GetPlayerVar('V', args[0].AsInt(), args[1].AsInt());
                    else result = PasValue.FromInt(NativeScriptVarExMiss);
                    return true;

                // GetGuildWarGold 原生只注册在 TPsNpc 0x734D34 上，已搬到 CallNpcFunc。

                case "getlistofwar":
                case "getnormalcastleflagowner":
                case "getnormalcastlescorerslt":
                case "getnormalcastlematchtakeinfo":
                case "getnormalcastletakeinfo":
                case "getcastleorddesc":
                case "takecastlestone":
                case "getcastlestoneowners":
                    // Native castle-war, match, and stone-ownership managers are absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Guild Resource ===
                case "addmyguildresource":
                case "decmyguildresource":
                case "curmyguildresource":
                case "myguilddreamcastcost":
                case "setmyguildtag":
                case "getmyguildtag":
                case "setguildparam":
                case "getguildparam":
                    // Native guild-resource, tag, and parameter persistence is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Body Cultivation (炼体/强体/提魄) ===
                case "getliantilv":
                case "getqiangtilv":
                case "gettipolv":
                case "getqiangtiphase":
                case "gettipophase":
                case "getliantilv_hero":
                case "getqiangtilv_hero":
                case "gettipolv_hero":
                case "getqiangtiphase_hero":
                case "gettipophase_hero":
                    return RejectUnsupportedNativeApi(out result);

                // === Dynamic Room Index ===
                case "getaidledynroomindex":
                case "getaidledynroomindexex":
                    // These are native TPsNpc functions, not TPlayer functions.
                    return RejectUnsupportedNativeApi(out result);

                // === Cross-Server Transfer Area ===
                case "reqstarttransferarea":
                case "reqaddremotetascore":
                case "querytascore":
                case "dectascore":
                    // Native cross-server transfer and remote-score service is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Move/Kick (player-context func variants) ===
                // MoveAllHumInMap 原生只注册在 TPsNpc 0x734ECC 上，已搬到 CallNpcFunc
                //（那边取的是 NPC 自己的地图，对应原生 Self = TPsNpc）。

                case "moveallhuminmapbylevel":
                    // MoveAllHumInMapByLevel(desMap, x, y, humLv, humForceLv, humSuperForceLv, opType): move by level
                    if (args.Count >= 7 && CurrentPlayer.m_PEnvir != null)
                    {
                        var dm = args[0].AsString(); var dx = (short)args[1].AsInt(); var dy = (short)args[2].AsInt();
                        var hl = (int)args[3].AsInt(); var op = args[6].AsInt();
                        var hl2 = new List<TBaseObject>();
                        M2Share.UserEngine.GetMapRageHuman(CurrentPlayer.m_PEnvir, 0, 0, 1000, hl2);
                        foreach (var o in hl2) { if (o is TPlayObject pl && ((op == 0 && pl.m_Abil.Level >= hl) || (op != 0 && pl.m_Abil.Level == hl))) pl.SpaceMove(dm, dx, dy, 0); }
                                            }
                    return true;

                // KickAllHumToMap 原生是全局函数 0x7299E5，不是 TPlayer 方法，
                // 已搬到 CallStandaloneFunction。

                case "groupflytodynroom":
                case "groupflytodynroominrange":
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // HERO FUNCTIONS (return value)
                // =====================================================================

                // GetHeroBagExItemCount / ...Ex 四重 0 命中。原生只有
                // GetHeroBagItemCount 0x72B279，C# 已在下方实现那一条。
                case "getherobagexitemcount":
                case "getherobagexitemcountex":
                    return RejectUnsupportedNativeApi(out result);

                case "getheroskillstr":
                    // GetHeroSkillStr - get hero skill string (concatenated skill names)
                    if (CurrentPlayer != null && CurrentPlayer.m_HeroObject != null)
                    {
                        var hero = CurrentPlayer.m_HeroObject;
                        var sb = new System.Text.StringBuilder();
                        for (int i = 0; i < hero.m_HeroMagicList.Count; i++)
                        {
                            var magic = hero.m_HeroMagicList[i];
                            if (magic != null && magic.MagicInfo != null)
                            {
                                if (sb.Length > 0) sb.Append(',');
                                sb.Append(magic.MagicInfo.sMagicName);
                                sb.Append(':');
                                sb.Append(magic.btLevel);
                            }
                        }
                        result = PasValue.FromString(sb.ToString());
                    }
                    else result = PasValue.FromString("");
                    return true;

                case "myherostate":
                    // MyHeroState(heroType) - get hero state: 0=no hero, 1=alive, 2=dead, 3=null
                    if (CurrentPlayer != null)
                    {
                        var heroType = args.Count >= 1 ? args[0].AsInt() : 0;
                        if (CurrentPlayer.m_HeroObject == null)
                        {
                            result = PasValue.FromInt(0);
                        }
                        else if (CurrentPlayer.m_HeroObject.m_boDeath)
                        {
                            result = PasValue.FromInt(2);
                        }
                        else
                        {
                            result = PasValue.FromInt(1);
                        }
                    }
                    else result = PasValue.FromInt(0);
                    return true;

                case "isinlayrabbitmascot":
                    // IsInlayRabbitMascot(itemName) - check if rabbit mascot item is inlaid on weapon
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        var itemName = args[0].AsString();
                        var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0 && weapon.ys1 > 0)
                        {
                            // Check if mascot item name matches (mascot items stored in weapon auxiliary data)
                            // For now: check if any equipped accessory item matches the name
                            for (int i = 0; i < CurrentPlayer.m_UseItems.Length; i++)
                            {
                                var eqItem = CurrentPlayer.m_UseItems[i];
                                if (eqItem != null && eqItem.wIndex > 0 &&
                                    string.Equals(M2Share.UserEngine.GetStdItemName(eqItem.wIndex), itemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    result = PasValue.FromBool(true);
                                    return true;
                                }
                            }
                            result = PasValue.FromBool(false);
                            return true;
                        }
                        result = PasValue.FromBool(false);
                        return true;
                    }
                    else
                    {
                        result = PasValue.FromBool(false);
                        return true;
                    }

                case "isinlayrabbitmascothero":
                    // IsInlayRabbitMascotHero(itemName) - check if rabbit mascot on hero weapon
                    if (args.Count >= 1 && CurrentPlayer != null && CurrentPlayer.m_HeroObject != null)
                    {
                        var itemName = args[0].AsString();
                        var hero = CurrentPlayer.m_HeroObject;
                        var weapon = hero.m_UseItems[Grobal2.U_WEAPON];
                        if (weapon != null && weapon.wIndex > 0 && weapon.ys1 > 0)
                        {
                            for (int i = 0; i < hero.m_UseItems.Length; i++)
                            {
                                var eqItem = hero.m_UseItems[i];
                                if (eqItem != null && eqItem.wIndex > 0 &&
                                    string.Equals(M2Share.UserEngine.GetStdItemName(eqItem.wIndex), itemName, StringComparison.OrdinalIgnoreCase))
                                {
                                    result = PasValue.FromBool(true);
                                    return true;
                                }
                            }
                            result = PasValue.FromBool(false);
                            return true;
                        }
                        result = PasValue.FromBool(false);
                        return true;
                    }
                    else
                    {
                        result = PasValue.FromBool(false);
                        return true;
                    }

                case "combineherotraintype":
                case "getmyguildheronum":
                    // Native hero training and guild-hero ownership state are absent.
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // MARRIAGE / MASTER FUNCTIONS (return value)
                // =====================================================================

                case "getspouse":
                    // Native signature returns TPlayer.
                    if (CurrentPlayer != null && !string.IsNullOrEmpty(CurrentPlayer.m_sDearName))
                    {
                        var spouse = M2Share.UserEngine.GetPlayObject(CurrentPlayer.m_sDearName);
                        result = PasValue.FromObject(spouse);
                    }
                    else result = PasValue.Nil;
                    return true;

                // =====================================================================
                // DYNAMIC ROOM / TIMED EVENTS / QUEST (return value)
                // =====================================================================

                case "dynroomidx":
                    return GetPlayerProperty("dynroomidx", out result);
                case "dynroomname":
                    return GetPlayerProperty("dynroomname", out result);

                case "myexpquestvalue":
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // CURRENCY / DIAMOND FUNCTIONS (return value)
                // =====================================================================

                case "makediamondwithyb":
                    // Native M2 starts an asynchronous account request; that service is absent.
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // US EXP (UNLIMITED STORAGE EXP) FUNCTIONS (return value)
                // =====================================================================

                case "addusexp":
                    if (args.Count != 3)
                        return false;
                    result = PasValue.FromInt(CurrentPlayer?.m_HeroObject == null
                        ? -1
                        : CurrentPlayer.m_HeroObject.AddUSExp(
                            args[0].AsInt(), args[1].AsInt(), args[2].AsInt()));
                    return true;

                case "chkifcanaddusexp":
                    result = PasValue.FromInt(CurrentPlayer?.m_HeroObject == null
                        ? -1
                        : CurrentPlayer.m_HeroObject.CheckIfCanAddUSExp());
                    return true;

                // =====================================================================
                // CORPS / GILD CREATION (return value)
                // =====================================================================

                case "createselfcorps":
                    // This_Player.CreateSelfCorps(name) reuses native wrapper
                    // sub_6ADD08 for both the 4524 CM opcode and this PAS
                    // registration (staging/ida_self_corps_gild_exact_20260720.txt).
                    // The name is the script arg (a2/edx); the founder is
                    // CurrentPlayer. The live create is gated on SupportsGildWrites
                    // inside TPlayObject and returns the native result code (3
                    // already-in-a-corps / 1 invalid name / 2 duplicate / 0 ok);
                    // with no store it stays fail-closed exactly as before.
                    if (CurrentPlayer.TryCreateNativeCorpsFromScript(
                            args.Count >= 1 ? args[0].AsString() : string.Empty,
                            out var createSelfCorpsCode))
                    {
                        result = PasValue.FromInt(createSelfCorpsCode);
                        return true;
                    }
                    return RejectUnsupportedNativeApi(out result);

                case "createselfgild":
                    // This_Player.CreateSelfGild(name) reuses native wrapper
                    // sub_6ADDA8 for both the 4564 CM opcode and this PAS
                    // registration. The name is the script arg (a2/edx); the
                    // founder is CurrentPlayer. The live create is gated on
                    // SupportsGildWrites and returns the native result code
                    // (555/4/5/6/2/0); with no store it stays fail-closed as before.
                    if (CurrentPlayer.TryCreateNativeGildFromScript(
                            args.Count >= 1 ? args[0].AsString() : string.Empty,
                            out var createSelfGildCode))
                    {
                        result = PasValue.FromInt(createSelfGildCode);
                        return true;
                    }
                    return RejectUnsupportedNativeApi(out result);

                case "addaccountstoragecnt":
                    return CallAddNativeAccountStorageCapacity(args, out result);

                case "getaccountstoragecnt":
                    return CallGetNativeAccountStorageCapacity(args, out result);

                case "addallgroupmemtag":
                case "addstoreitem":
                case "boindblinfu":
                case "getcastlegift":
                case "getgoodscurrentstorage":
                case "getlimitlinfu":
                case "getselfgroupmemtag":
                case "groupchktags":
                case "groupchktagv":
                case "queryallvotetopten":
                case "querygoodsnumbyybnum":
                case "queryteammemberlevelinfo":
                case "psaddcrethp":
                case "setallgroupmemtag":
                case "setselfgroupmemtag":
                case "showlingfu3":
                case "updateeverydayactorder":
                    // Native hero, group, shop, activity, and storage managers are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "getherobagitemcount":
                    result = PasValue.FromInt(args.Count >= 1
                        && (CurrentPlayer.m_btNativeHeroState & 3) != 0
                        && CurrentPlayer.m_HeroObject != null
                            ? CurrentPlayer.m_HeroObject.GetNativeBagItemCount(args[0].AsString())
                            : -1);
                    return true;

                case "takefromherobag":
                    result = PasValue.FromBool(args.Count >= 2
                        && (CurrentPlayer.m_btNativeHeroState & 3) != 0
                        && CurrentPlayer.m_HeroObject != null
                        && CurrentPlayer.m_HeroObject.TryTakeNativeBagItems(
                            args[0].AsString(), args[1].AsInt(), out _));
                    return true;

                case "newbiegiftconsume":
                    // Native request type 5 completes asynchronously before its script callback.
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // LEITAI ARENA FUNCTIONS (return value)
                // =====================================================================

                case "getleitaistate":
                case "getwarresult":
                case "getmyleitaiflag":
                    // Native arena ownership and match-result state are absent.
                    return RejectUnsupportedNativeApi(out result);

                // === SSK Skills (内含/内功技能) ===
                case "havestudysskskill":
                    return RejectUnsupportedNativeApi(out result);

                case "addsskskillexp":
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // 沉默回退族(函数面)——见 CallPlayerMethod 中同名族的说明。
                // 这些名字原生注册在 TPlayer 面(sub_731350 / class 0x006AC87C),
                // 表达式形式此前静默返回 0(fail-open / fail-wrong),现改为显式拒绝。
                // =====================================================================

                // ---------------------------------------------------------------------
                // 观摩点(ObPoint)—— 原生是**两个互不相通的存储**,不是一个账本。
                // 全镜像枚举 0x7D5EC8 的 11 处引用可证:
                //   GetObPoint    0x6F08DD/0x6F08F0
                //   DeleteObPoint 0x6F095E/0x6F0971/0x6F0995
                //   sub_7931C8    0x7933A2/B0/D3/E9/0x793403/0x79347A(关服保存并释放)
                // 该 TMirStringList(构造于 0x79236B,类名 TMirStringList @ VMT 0x49EB88)
                // **没有任何装填点**:sub_7931C8 只把它写成文本文件 'ObPoint.txt'
                // (0x793808,全镜像只有 0x79345D 一处引用 = SaveToFile,无 LoadFromFile),
                // 而 AddGuanMoPoint 只写 MySQL gamedata.LiPaoObPoint、从不碰这个表。
                // 因此原生的可观测行为是:
                //   GetObPoint    恒返回 0(IndexOf 永远命中不了,0x6F08C9 xor esi,esi)
                //   DeleteObPoint 恒 no-op(同上,0x6F096E jle 直接跳到 xor eax,eax)
                // 即余额对脚本是**只写**的。这是原版自身的半迁移遗留,不是本移植的缺口。
                // 忠实实现 = 照抄这两个恒定结果;若改成去读 LiPaoObPoint,会把
                // "永不触发" 的脚本门变成 "可能触发",属于行为背离与经济风险。
                // ---------------------------------------------------------------------

                // GetObPoint sub_6F08AC:esi 预置 0 → 由 [0x7D5EC8] 的 vtable +0x54
                // (TStringList.IndexOf)按角色名 player+0x106 查下标,cmp -1/jle 未命中即
                // 返回 0;命中才经 vtable +0x18(GetObject)取条目并读其首个 dword 为余额。
                // 由于该表永不被装填,原生恒走未命中路径。
                case "getobpoint":
                    result = PasValue.FromInt(0);
                    return true;

                // AddGuanMoPoint sub_6EFB08 是过程(0x6EFC01 xor eax,eax 为唯一汇合点,
                // 返回值恒 0,store 失败对脚本也是静默的)。这里同名登记以便表达式形式
                // 也留下痕迹;写入侧本身仍是缺口(见 CallPlayerMethod 注释)。
                case "addguanmopoint":
                    return RejectUnsupportedNativeApi(out result);

                // DeleteObPoint sub_6F0930:同 GetObPoint 走那张永不装填的
                // TMirStringList,0x6F096E 的 jle 恒成立 → 恒 no-op、返回 0(过程)。
                // 忠实实现为空操作。
                case "deleteobpoint":
                    result = PasValue.FromInt(0);
                    return true;

                // 大药商人 荣耀点/元宝 兑换梯度(见 CallPlayerMethod 注释)。
                case "queryglorypointbygoodsnum":
                    return RejectUnsupportedNativeApi(out result);
                case "sellgoodstogetglorypoint":
                    return RejectUnsupportedNativeApi(out result);
                case "consumeybtobuygoods":
                    return RejectUnsupportedNativeApi(out result);
                case "addtobuygoodslogbyscript":
                    return RejectUnsupportedNativeApi(out result);

                // CanEnterActiveMap sub_6F90DC —— 声明为
                // function CanEnterActiveMap(const MapStr: string): Boolean(@0x730C38)。
                // edx = 脚本传入的地图名;查 [0x7D660C](VMT 名确认 = **TMapManager**)的
                // sub_696228 = UpperCase(MapStr) + TStringHash[obj+0x20]。
                // ⚠ 该字典**不是"活动事件表",而是全量静态地图注册表**:装填点
                // sub_695FF0@0x6960B9 以 UpperCase(env.MapName)(env+0x44)插入每一张
                // 由 Config\MapInfo.txt / MapInfoEx.txt 载入的 TEnvironment。
                // 命中 → sub_619848(eax=[0x7D64C8] TActivePointMgr, edx=player, ecx=env):
                //   env==nil / player==nil                        → 0
                //   [authMgr 0x7D6534 +8] == 0(鉴权系统关闭)     → **1(放行)**
                //   required = [env+0x30] <= 0                     → 1(无门槛)
                //   sub_61997C(player) + [player+0xAE4] >= required → 1
                //   否则 → sub_69A934 触发脚本钩子 '@PlayerActiveWithMap' 后返回 0
                // 未命中 → 0x6F90E3 xor ebx,ebx + 0x6F90F3 je 0x6F9109 跳过一切赋值,
                //   **无 fail-open 路径,恒返回 0 = 拒绝**;但因字典含全部地图,该分支
                //   只在"地图名根本不存在"时发生,而非"没有配置活动"。
                // required 由 sub_618FB8(TActivePointMgr 初始化)从 '地图信用分配置'
                // 结构化文件(节点 Maps/Name/Value)写入:0x61912E mov [eax+0x30], ebx,
                // 是 +0x30 的唯一生产者。**未配置时 +0x30 仍是 ctor@0x6956EA 放的 TList
                // 指针(非零且极大)→ 比较必然失败 → 0**,故"无配置"绝不能简化成
                // required=0(那会反转成恒放行)。C# 映射见 NativeMapActivePointLoader
                // (sub_618FB8 @0x00618FB8 / consumer sub_619848 @0x00619848)。
                case "canenteractivemap":
                    if (args.Count < 1)
                    {
                        result = PasValue.FromBool(false);
                        return true;
                    }
                    result = PasValue.FromBool(
                        NativeMapActivePointLoader.CanEnterActiveMap(
                            CurrentPlayer, args[0].AsString()));
                    return true;

                // GetGildName sub_6F7ADC:sub_6ADAE4(player) 取行会对象,非空复制 [guild+0x10]
                // (会名),否则空串。
                case "getgildname":
                    return RejectUnsupportedNativeApi(out result);

                // GetCorpsName sub_6F7AB0:player+0xAE8 取战队对象,非空复制 [corps+0x08],否则空串。
                case "getcorpsname":
                    return RejectUnsupportedNativeApi(out result);

                // IsGroupMember sub_6B7B8C:player+0xA80 取小组对象;为空返回 0,
                // 否则 sub_72792C(group) —— 按 UpperCase 名字在 group+0x48 起的
                // 11 项固定数组里比对成员,命中返回 1。
                case "isgroupmember":
                    return RejectUnsupportedNativeApi(out result);

                // GetMyTaskState sub_6FBC98:任务管理器 [0x7D6D80] → sub_6049FC 查任务,
                // 非空则 sub_6043FC 取状态,否则 0。
                case "getmytaskstate":
                    return RejectUnsupportedNativeApi(out result);
                case "getmytaskdetail":
                    return RejectUnsupportedNativeApi(out result);
                case "systemmsg":
                    return RejectUnsupportedNativeApi(out result);
                case "showcurrentbless":
                    CurrentPlayer.ShowCurrentNativeBless();
                    return true;
                case "taskdialog":
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // 原生真空过程(genuine native nullsubs)——**忠实实现,不是缺口**。
                // 这些是 Delphi "已声明未实现" 的签名占位,原生函数体就是常量返回。
                // 逐字节核对自 D:/loym2/staging/_reunpack_work/flat_image.bin。
                // =====================================================================

                // OpenStorage sub_6F2938 = `or eax,0FFFFFFFFh; ret` → 恒返回 -1。
                case "openstorage":
                    result = PasValue.FromInt(-1);
                    return true;

                // 以下四个是 0x006F369C/A0/A4/A8 四条连续的 3 字节 `xor eax,eax; ret`,
                // 以及 sub_6F3768 同形 —— 全部恒返回 0。
                case "submitlegendbook":     // sub_6F369C
                case "getwwsqpassnum":       // sub_6F36A0
                case "joinmirmatch":         // sub_6F36A4
                case "getmirmatchprize":     // sub_6F36A8
                case "getpneumatotallevel":  // sub_6F3768
                    result = PasValue.FromInt(0);
                    return true;

                default:
                    return false;
            }
        }

        // =====================================================================
        // NPC METHOD CALLS (This_Npc.xxx) - 40+ methods
        // =====================================================================

        public bool CallNpcMethod(string method, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentNpc == null) return false;

            switch (method.ToLowerInvariant())
            {
                case "click_commititem":
                    if (args.Count >= 3 && args[0].ObjVal is TPlayObject clicker)
                    {
                        var series = args[1].AsInt() & ushort.MaxValue;
                        clicker.SendDefMessage((short)Grobal2.SM_OPEN_COMMIT_ITEM,
                            CurrentNpc.ObjectId, 0, 0, series, args[2].AsString());
                    }
                    return true;

                case "notifyclientupdbagitem":
                    if (args.Count >= 2 && args[0].ObjVal is TPlayObject updatePlayer &&
                        args[1].ObjVal is TUserItem updateItem)
                        updatePlayer.SendUpdateItem(updateItem);
                    return true;

                case "requestbaishi":
                    if (args.Count >= 2 && args[0].ObjVal is TPlayObject baishiPlayer)
                    {
                        using var context = PushItemContext(baishiPlayer, CurrentNpc,
                            CurrentInputOk, CurrentInputStr, CurrentItem);
                        CallPlayerMethod("requestbaishi", new List<PasValue> { args[1] });
                    }
                    return true;

                case "requestmarry":
                    if (args.Count != 2
                        || args[0].ObjVal is not TPlayObject marryPlayer
                        || args[1].Type != PasValueType.String)
                        return false;
                    RequestNativeMarry(marryPlayer, args[1].StrVal);
                    return true;

                case "clickupweaponnow":
                    result = PasValue.FromInt(CurrentNpc is Merchant upMerchant
                        ? upMerchant.ClickUpWeaponNow(CurrentPlayer)
                        : 0);
                    return true;

                case "clickupweaponnobreak":
                    result = PasValue.FromInt(CurrentNpc is Merchant noBreakMerchant
                        ? noBreakMerchant.ClickUpWeaponNoBreak(CurrentPlayer)
                        : 0);
                    return true;

                case "clickgetbackupweapon":
                    result = PasValue.FromInt(CurrentNpc is Merchant getBackMerchant
                        ? getBackMerchant.ClickGetBackUpWeapon(CurrentPlayer)
                        : 0);
                    return true;

                // === Dialogue ===
                case "npcdialog":
                    if (args.Count >= 2 && CurrentPlayer != null && CurrentNpc != null)
                    {
                        var dlg = args[1].AsString();
                        var body = HUtil32.GetBytes(CurrentNpc.m_sCharName + "/" + dlg);
                        var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MERCHANTSAY, CurrentNpc.ObjectId, 0, 0, 1);
                        CurrentPlayer.SendSocket(defMsg, body);
                    }
                    return true;

                case "npcdialogex":
                    // NpcDialogEx(label, dlgType, Tag)
                    if (args.Count >= 3 && CurrentPlayer != null)
                    {
                        var labelName = args[1].AsString();
                        var dlgType = args.Count >= 3 ? args[2].AsInt() : 0;
                        var tag = args.Count >= 4 ? args[3].AsString() : "";
                        CurrentPlayer.GetScriptLabel(labelName);
                        // Include dlgType and Tag in the dialog message for client-side handling
                        var dlgData = string.IsNullOrEmpty(tag)
                            ? $"{CurrentNpc.m_sCharName}/{labelName}/{dlgType}"
                            : $"{CurrentNpc.m_sCharName}/{labelName}/{dlgType}/{tag}";
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, 0, 0, 0, dlgData);
                    }
                    return true;

                case "closedialog":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTDLGCLOSE, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;

                case "inputdialog":
                    // InputDialog(Hum, MsgStr, DlgType, InputType)
                    if (args.Count >= 3 && CurrentPlayer != null && CurrentNpc != null)
                    {
                        var msg = args[1].AsString();
                        var dlgType = args[2].AsInt();
                        var inputType = args.Count >= 4 ? args[3].AsInt() : 0;
                        CurrentPlayer.SendDefMessage(Grobal2.SM_MERCHANT_QUERY, CurrentNpc.ObjectId, inputType, dlgType, 0, msg);
                    }
                    return true;

                case "npcsay":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                case "npcnotice":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                case "npcsidenotice":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                case "npcmapnotice":
                    // NpcMapNotice(msg, iColor?)
                    if (args.Count >= 1)
                    {
                        var msg = args[0].AsString();
                        var color = args.Count >= 2 ? (MsgType)args[1].AsInt() : MsgType.Notice;
                        if (CurrentNpc?.m_PEnvir != null)
                        {
                            var mapHumans = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(CurrentNpc.m_PEnvir, 0, 0, 1000, mapHumans);
                            foreach (var human in mapHumans)
                            {
                                if (human is TPlayObject player)
                                    player.SysMsg(msg, MsgColor.Green, color);
                            }
                        }
                        else
                        {
                            M2Share.UserEngine.SendBroadCastMsg(msg, color);
                        }
                    }
                    return true;

                // === Monster spawn ===
                // === Player Modifications (NPC-initiated) ===
                case "chgsex":
                    // Change player gender. 0 = male, 1 = female.
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        CurrentPlayer.m_btGender = args[0].AsInt() == 0 ? PlayGender.Man : PlayGender.WoMan;
                        CurrentPlayer.FeatureChanged();
                    }
                    return true;

                case "chgpkselfzero":
                    // Reset player PK points to 0 and update name color
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.m_nPkPoint = 0;
                        CurrentPlayer.RefNameColor();
                    }
                    return true;

                case "addlogrec":
                    // GM activity log: record admin action to main output
                    if (args.Count >= 1)
                        M2Share.MainOutMessage($"[GM] {CurrentPlayer.m_sCharName}: {args[0].AsString()}");
                    return true;

                // === YB/Shop Integration (NPC side) ===
                // 与 CallPlayerMethod 那一半同理：PsShopGetGoodsList / PsShopBuyGoods
                // 四重 0 命中，属 INVENTED，fail-closed。
                case "psshopgetgoodslist":
                case "psshopbuygoods":
                    return RejectUnsupportedNativeApi(out result);

                case "createmon":
                    if (args.Count >= 6)
                    {
                        var mapName = args[0].AsString();
                        var environment = string.IsNullOrEmpty(mapName)
                            ? CurrentNpc.m_PEnvir
                            : M2Share.MapManager.FindMap(mapName);
                        var x = (short)args[1].AsInt(); var y = (short)args[2].AsInt();
                        if (x <= 0) x = CurrentNpc.m_nCurrX; if (y <= 0) y = CurrentNpc.m_nCurrY;
                        var range = args[3].AsInt();
                        var monName = args[4].AsString();
                        var num = Math.Min(args[5].AsInt(), 200);
                        for (int i = 0; i < num; i++)
                        {
                            var sx = x; var sy = y;
                            if (range > 0)
                            {
                                sx = (short)(x - range + M2Share.RandomNumber.Random(range * 2 + 1));
                                sy = (short)(y - range + M2Share.RandomNumber.Random(range * 2 + 1));
                            }
                            M2Share.UserEngine.RegenMonsterByName(environment, sx, sy,
                                monName);
                        }
                    }
                    return true;

                case "createfameplayermon":
                    if (args.Count >= 6)
                    {
                        var mapName = args[0].AsString();
                        var monName = args[3].AsString();
                        var num = Math.Min(args[5].AsInt(), 200);
                        var x = (short)args[1].AsInt(); var y = (short)args[2].AsInt();
                        for (int i = 0; i < num; i++)
                            M2Share.UserEngine.RegenMonsterByName(mapName, x, y, monName);
                    }
                    return true;

                case "clearmon":
                    if (args.Count >= 1)
                    {
                        var mapName = args[0].AsString();
                        var map = string.IsNullOrEmpty(mapName)
                            ? CurrentNpc?.m_PEnvir
                            : M2Share.MapManager.FindMap(mapName);
                        ClearEnvironmentMonsters(map, false);
                    }
                    return true;

                case "clearmonex":
                    if (args.Count >= 2)
                    {
                        var mapName = args[0].AsString();
                        var map = string.IsNullOrEmpty(mapName)
                            ? CurrentNpc?.m_PEnvir
                            : M2Share.MapManager.FindMap(mapName);
                        ClearEnvironmentMonsters(map, args[1].AsBool());
                    }
                    return true;

                case "createdynroommon":
                    if (args.Count != 7
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    M2Share.DynamicRoomService.CreateDynamicRoomMonsters(
                        args[0].AsString(), args[1].AsInt(),
                        args[2].AsInt(), args[3].AsInt(),
                        args[4].AsInt(), args[5].AsString(),
                        args[6].AsInt());
                    return true;

                case "checkmapmonbyname":
                    return true;

                case "checkcurrmapmon":
                    return true;

                case "checkothermaphum":
                    return true;

                // === Merchants ===
                case "click_sell":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;
                case "click_buy":
                    if (CurrentPlayer != null)
                    {
                        if (CurrentNpc is Merchant merchant)
                            merchant.UserSelect_BuyItem(CurrentPlayer, 0);
                        else
                            CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDGOODSLIST, 0, CurrentNpc.ObjectId, 0, 0, "");
                    }
                    return true;
                case "click_repair":
                    if (args.Count != 1 ||
                        args[0].Type != PasValueType.Object ||
                        args[0].ObjVal is not TPlayObject repairPlayer)
                        return false;
                    repairPlayer.SendNativeScriptRepair(CurrentNpc, 1);
                    return true;
                case "click_srepair":
                    if (args.Count != 1 ||
                        args[0].Type != PasValueType.Object ||
                        args[0].ObjVal is not TPlayObject specialRepairPlayer)
                        return false;
                    specialRepairPlayer.SendNativeScriptRepair(CurrentNpc, 2);
                    return true;
                case "click_getback":
                    // Trigger get-back (storage retrieval) dialog on client
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.m_nStoragePage = 0;
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_USERGETBACKITEM, 0, CurrentNpc.ObjectId, 0, 0, "");
                    }
                    return true;
                case "click_goldchgbar":
                    // Trigger gold-to-bar exchange dialog
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;
                case "click_bartogold":
                    // Trigger bar-to-gold exchange dialog
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;
                case "click_bricktobar":
                    // Trigger brick-to-bar exchange dialog
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;
                case "click_bartobrick":
                    // Trigger bar-to-brick exchange dialog
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;
                case "click_makedrug":
                    // Trigger drug-make dialog on client
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_USERMAKEDRUGITEMLIST, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;
                case "playdice":
                    if (args.Count != 3 || args[0].ObjVal is not TPlayObject dicePlayer)
                        return false;
                    var diceLabel = args[2].AsString();
                    dicePlayer.m_sPlayDiceLabel = diceLabel;
                    dicePlayer.SendMsg(CurrentNpc, Grobal2.RM_PLAYDICE,
                        args[1].AsInt(),
                        PackDiceValues(dicePlayer, 1, 4),
                        PackDiceValues(dicePlayer, 5, 4),
                        PackDiceValues(dicePlayer, 9, 2),
                        diceLabel);
                    return true;
                case "diapeif":
                    // Native signature: procedure DiaPeif(Hum; const nCmd: integer).
                    if (args.Count != 2 ||
                        args[0].ObjVal is not TPlayObject diaPeifPlayer)
                        return false;
                    diaPeifPlayer.SelectNativeDiamondFoundryRecipe(CurrentNpc,
                        Volatile.Read(ref M2Share.DiamondFoundry),
                        args[1].AsInt());
                    return true;
                case "addnpcprop":
                    if (args.Count != 1 ||
                        args[0].Type != PasValueType.Integer)
                        return false;
                    CurrentNpc.AddNativePasProperty(args[0].AsInt());
                    return true;
                case "getcelebname":
                    result = PasValue.FromString(
                        NativeCelebrityStatueManager.GetCelebrityName(CurrentNpc));
                    return true;
                // GetCelebLv sub_64FF5C = `movzx eax, word ptr [eax+5AAh]; ret` —— 逐字节忠实。
                // 接收者是 TPsNpc(注册在 sub_738720 / class 0x0063CFA8),不是玩家:
                // 同一 +0x5AA 在 ReqBecomeCeleb sub_64FDC0@0x64FE40 被写为
                // `mov [edx+5AAh], ax`(申请者等级),并在 sub_643350@0x643737 以
                // Hero.ini 键 '等级' 持久化 —— 即雕像的 m_wCelebrityLevel。
                case "getceleblv":
                    result = PasValue.FromInt(
                        NativeCelebrityStatueManager.GetCelebrityLevel(CurrentNpc));
                    return true;
                case "giveconfigprize":
                    if (args.Count != 3 || args[0].ObjVal is not TPlayObject normalPrizePlayer)
                        return false;
                    using (var context = PushItemContext(normalPrizePlayer, CurrentNpc,
                        CurrentInputOk, CurrentInputStr, CurrentItem))
                    {
                        _ = TryNativeGiveConfigPrize(args[1].AsInt(),
                            args[2].AsString(), true);
                    }
                    return true;
                case "reqbecomeceleb":
                    result = PasValue.FromInt(
                        NativeCelebrityStatueManager.TryBecomeCelebrity(
                            CurrentNpc,
                            args.Count >= 1 && args[0].ObjVal is TPlayObject applicant
                                ? applicant
                                : CurrentPlayer));
                    return true;

                // === NPC Shop Setup (战神 FillGoods/AddStdMode/SetRebate) ===
                case "fillgoods":
                    if (CurrentNpc is Merchant fgMerchant && args.Count >= 3)
                    {
                        var itemName = args[0].AsString();
                        var count = args[1].AsInt();
                        var interval = args[2].AsInt();
                        if (!fgMerchant.FillGoods(itemName, count, interval))
                        {
                            M2Share.MainOutMessage($"[PasEngine] FillGoods ignored unknown item '{itemName}' for NPC {CurrentNpc.m_sCharName}");
                        }
                    }
                    return true;

                case "addstdmode":
                    if (CurrentNpc is Merchant smMerchant && args.Count >= 1)
                    {
                        // ✅ 战神字节证据 (Tier-1) — ECON-13: 商人许可表填充点 AddStdMode。
                        // 原生处理函数 sub_6401E8(注册于 sub_738720 商人 PAS 面,名串 @0x739684
                        // "AddStdMode", edx=handler @0x73887E):
                        //   006401e8  xor   ecx,ecx                          ; i = 0
                        //   006401ea  cmp   dword [eax+ecx*4+0x46c],-1       ; 找第一个 == -1 的空槽
                        //   006401f2  jne   0x6401ff
                        //   006401f4  movzx edx,dx                          ; nMode 只取【低 16 位】(Word)
                        //   006401f7  mov   [eax+ecx*4+0x46c],edx           ; 写入该槽
                        //   006401fe  ret
                        //   006401ff  inc   ecx / cmp ecx,0x40 / jne 0x6401ea ; 【固定 64 槽上限】
                        //   00640205  ret                                    ; 满槽 -> 静默忽略
                        // 许可表结构 = 商人对象 +0x46C 处【64 项 int32 数组】;判定 CheckItemType
                        // (sub_64029C, C# Merchant.CheckItemType) 全扫这 64 槽比对 StdMode。构造器
                        // sub_63D888 `lea eax,[esi+0x46c] / ecx=-1 / edx=0x100 / call 0x403B2C`
                        // 把整块填 -1 —— 即【未 AddStdMode 的类型不可交易 = fail-closed】。
                        // 【关键】原生 AddStdMode 仅写数组,【绝无】任何买卖标志副作用:
                        //   旧 C# 的 `m_boBuy=true; m_boSell=true` 是臆造。native 里"卖给商人"的开关
                        //   本就是许可表自身(GetItemPrice @0x63F420 未命中价格表时,靠 CheckItemType
                        //   决定回退价 ROUND(Price*1.1),不许可则价=-1 -> 卖出门 nPrice>0 失败);
                        //   "买/卖窗"由 Click_Buy/Click_Sell(sub_6401B0 等)独立开启,不依赖这两个标志。
                        // C# 用 List 复刻定长数组:LoadMerchantScript 会整表 Clear、且无移除路径,故
                        //   Add(append) 等价于"写第一个空槽";不去重(原生同样不去重);Count<64 复刻满槽忽略;
                        //   &0xFFFF 复刻 movzx dx 的 Word 截断。
                        if (smMerchant.m_ItemTypeList.Count < 64)
                            smMerchant.m_ItemTypeList.Add(args[0].AsInt() & 0xFFFF);
                    }
                    return true;

                case "setrebate":
                    // ✅ 战神字节证据 (Tier-1) — ECON §4.18 二元权威并回。
                    // 原生 PAS `SetRebate(nRebate:Word)` 处理器 sub_647438 写的是【唯一】费率字段
                    // +0x468(= C# m_nPriceRate);买价阶段 sub_640208 @0x640232/@0x640278
                    // `fild dword [ebx+0x468]` 读的正是同一字段。原 C# 误拆出独立 m_nRebate,现并回。
                    // 钳制/复位逐字复刻 sub_647438(入参 edx→ebx,仅看低 16 位 bx):
                    //   00647450  66 85 db        test  bx,bx
                    //   00647453  76 12           jbe   0x647467   ; bx==0     -> 非法
                    //   00647455  66 81 fb ff ff  cmp   bx,0xFFFF
                    //   0064745A  73 0b           jae   0x647467   ; bx>=0xFFFF-> 非法
                    //   0064745C  0f b7 d3        movzx edx,bx
                    //   0064745F  89 90 68 04..   mov   [eax+0x468],edx        ; 合法则写 Word 值
                    //   00647467  c7 80 68 04.. 64 mov  dword [eax+0x468],100   ; 非法复位 100
                    //             + MainOutMessage("[Rebate Err]:"@0x6474CC + IntToStr(bx))
                    if (CurrentNpc is Merchant srMerchant && args.Count >= 1)
                    {
                        var nRebate = args[0].AsInt() & 0xFFFF;
                        if (nRebate > 0 && nRebate < 0xFFFF)
                        {
                            srMerchant.m_nPriceRate = nRebate;
                        }
                        else
                        {
                            srMerchant.m_nPriceRate = 100;
                            M2Share.MainOutMessage("[Rebate Err]:" + nRebate);
                        }
                    }
                    return true;

                // === Castle/Guild ===
                case "opencastledoor":
                    // OpenCastleDoor(bOpen) - 1=open, 0=close
                    if (args.Count >= 1)
                    {
                        var bOpen = args[0].AsInt() != 0;
                        var castle = M2Share.CastleManager.GetCastle(0);
                        if (castle != null)
                            castle.MainDoorControl(!bOpen); // MainDoorControl(close), so invert
                    }
                    return true;

                case "click_repairdoor":
                    if (CurrentPlayer == null) return true;
                    {
                        var castle = M2Share.CastleManager.GetCastle(0);
                        var price = CurrentNpc.m_nPasRepDoorGold > 0
                            ? CurrentNpc.m_nPasRepDoorGold : M2Share.g_Config.nRepairDoorPrice;
                        if (castle == null) { result = PasValue.FromBool(false); return true; }
                        if (CurrentPlayer.m_nGold < price) { CurrentPlayer.SysMsg($"金币不足，修理城门需要{price}金币", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        if (castle.RepairDoor()) { CurrentPlayer.m_nGold -= price; CurrentPlayer.GoldChanged(); CurrentPlayer.SysMsg("城门修理成功", MsgColor.Green, MsgType.Hint); result = PasValue.FromBool(true); }
                        else { CurrentPlayer.SysMsg("城门不需要修理或正在战争中", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); }
                    }
                    return true;

                case "click_repairwall":
                    if (args.Count != 2
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject repairWallPlayer
                        || args[1].Type != PasValueType.Integer)
                        return false;
                    {
                        var wallIdx = args[1].AsInt();
                        var castle = M2Share.CastleManager.GetCastle(0);
                        var price = CurrentNpc.m_nPasRepWallGold > 0
                            ? CurrentNpc.m_nPasRepWallGold : M2Share.g_Config.nRepairWallPrice;
                        if (castle == null) { result = PasValue.FromBool(false); return true; }
                        if (repairWallPlayer.m_nGold < price) { repairWallPlayer.SysMsg($"金币不足，修理城墙需要{price}金币", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        if (castle.RepairWall(wallIdx)) { repairWallPlayer.m_nGold -= price; repairWallPlayer.GoldChanged(); repairWallPlayer.SysMsg("城墙修理成功", MsgColor.Green, MsgType.Hint); result = PasValue.FromBool(true); }
                        else { repairWallPlayer.SysMsg("城墙不需要修理或正在战争中", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); }
                    }
                    return true;

                case "click_hireguard":
                    if (CurrentPlayer == null) return true;
                    {
                        var idx = args.Count >= 1 ? args[0].AsInt() - 1 : 0;
                        var castle = M2Share.CastleManager.GetCastle(0);
                        var price = CurrentNpc.m_nPasHireGuardGold > 0
                            ? CurrentNpc.m_nPasHireGuardGold : M2Share.g_Config.nHireGuardPrice;
                        if (castle == null || castle.m_MapCastle == null || idx < 0 || idx >= castle.m_Guard.Length) { result = PasValue.FromBool(false); return true; }
                        if (CurrentPlayer.m_nGold < price) { CurrentPlayer.SysMsg($"金币不足，雇佣守卫需要{price}金币", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        if (castle.m_boUnderWar) { CurrentPlayer.SysMsg("现在无法雇佣，城堡正在战争中", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        var guard = castle.m_Guard[idx];
                        if (guard.BaseObject != null) { CurrentPlayer.SysMsg("该位置已有守卫", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        guard.BaseObject = M2Share.UserEngine.RegenMonsterByName(castle.m_MapCastle, guard.nX, guard.nY, guard.sName);
                        if (guard.BaseObject != null) { CurrentPlayer.m_nGold -= price; CurrentPlayer.GoldChanged(); guard.BaseObject.m_Castle = castle; ((GuardUnit)guard.BaseObject).m_nX550 = guard.nX; ((GuardUnit)guard.BaseObject).m_nY554 = guard.nY; ((GuardUnit)guard.BaseObject).m_nDirection = 3; CurrentPlayer.SysMsg("雇佣守卫成功", MsgColor.Green, MsgType.Hint); result = PasValue.FromBool(true); }
                        else { result = PasValue.FromBool(false); }
                    }
                    return true;

                case "click_hirearcher":
                    if (args.Count != 2
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject hireArcherPlayer
                        || args[1].Type != PasValueType.Integer)
                        return false;
                    {
                        var idx = args[1].AsInt() - 1;
                        var castle = M2Share.CastleManager.GetCastle(0);
                        var price = CurrentNpc.m_nPasHireArcherGold > 0
                            ? CurrentNpc.m_nPasHireArcherGold : M2Share.g_Config.nHireArcherPrice;
                        if (castle == null || castle.m_MapCastle == null || idx < 0 || idx >= castle.m_Archer.Length) { result = PasValue.FromBool(false); return true; }
                        if (hireArcherPlayer.m_nGold < price) { hireArcherPlayer.SysMsg($"金币不足，雇佣弓箭手需要{price}金币", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        if (castle.m_boUnderWar) { hireArcherPlayer.SysMsg("现在无法雇佣，城堡正在战争中", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        var archer = castle.m_Archer[idx];
                        if (archer.BaseObject != null) { hireArcherPlayer.SysMsg("该位置已有弓箭手", MsgColor.Red, MsgType.Hint); result = PasValue.FromBool(false); return true; }
                        archer.BaseObject = M2Share.UserEngine.RegenMonsterByName(castle.m_MapCastle, archer.nX, archer.nY, archer.sName);
                        if (archer.BaseObject != null) { hireArcherPlayer.m_nGold -= price; hireArcherPlayer.GoldChanged(); archer.BaseObject.m_Castle = castle; ((GuardUnit)archer.BaseObject).m_nX550 = archer.nX; ((GuardUnit)archer.BaseObject).m_nY554 = archer.nY; ((GuardUnit)archer.BaseObject).m_nDirection = 3; hireArcherPlayer.SysMsg("雇佣弓箭手成功", MsgColor.Green, MsgType.Hint); result = PasValue.FromBool(true); }
                        else { result = PasValue.FromBool(false); }
                    }
                    return true;

                case "reqcastlewar":
                    // ReqCastleWar — native sub_6CA6AC @0x006CA6AC
                    if (CurrentPlayer != null)
                    {
                        var castle = M2Share.CastleManager.GetCastle(0);
                        if (castle != null)
                            Services.NativeReqCastleWar.TryApply(CurrentPlayer, castle);
                    }
                    return true;

                // BuildGuild 0x72B1DD 与 RequestGuildWar 0x72B321 原生都注册在 TPlayer 上，
                // 已搬到 CallPlayerFunc。

                // === File operations ===
                case "chkstrinfile":
                    // ChkStrInFile: delegate to standalone implementation
                    if (args.Count >= 2)
                    {
                        if (CallStandaloneFunction("chkstrinfile", args, out var csfResult))
                        { result = csfResult; return true; }
                    }
                                        return true;

                case "addstrtofile":
                    // AddStrToFile(fileName, content): append line to file in Envir directory
                    if (args.Count >= 2)
                    {
                        try
                        {
                            var fileName = args[0].AsString();
                            var content = args[1].AsString();
                            var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                            var filePath = Path.Combine(envirDir, fileName);
                            var dir = Path.GetDirectoryName(filePath);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.AppendAllText(filePath, content + Environment.NewLine, Encoding.GetEncoding("GBK"));
                        }
                        catch (Exception ex) { Debug.WriteLine("addstrtofile failed: " + ex.Message); }
                    }
                    return true;

                case "delstrfromfile":
                    // DelStrFromFile(fileName, content): remove line from file
                    if (args.Count >= 2)
                    {
                        try
                        {
                            var fileName = args[0].AsString();
                            var content = args[1].AsString();
                            var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                            var filePath = Path.Combine(envirDir, fileName);
                            if (File.Exists(filePath))
                            {
                                var lines = File.ReadAllLines(filePath, Encoding.GetEncoding("GBK")).ToList();
                                lines.RemoveAll(l => l.Trim() == content.Trim());
                                File.WriteAllLines(filePath, lines, Encoding.GetEncoding("GBK"));
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine("delstrfromfile failed: " + ex.Message); }
                    }
                    return true;

                // === Map/Movement ===
                case "moveallhum":
                    // MoveAllHumInMap: teleport all players from source map to dest map
                    // moveallhum(srcMap, desMap, x, y)
                    if (args.Count >= 4)
                    {
                        var srcMap = M2Share.MapManager.FindMap(args[0].AsString());
                        var desMapName = args[1].AsString();
                        var desX = (short)args[2].AsInt();
                        var desY = (short)args[3].AsInt();
                        if (srcMap != null)
                        {
                            var humans = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(srcMap, 0, 0, 1000, humans);
                            foreach (var obj in humans)
                            {
                                if (obj is TPlayObject player)
                                    player.SpaceMove(desMapName, desX, desY, 0);
                            }
                        }
                    }
                    return true;

                case "moveallhuminmapbylevel":
                    // MoveAllHumInMapByLevel: teleport players by level range
                    // moveallhuminmapbylevel(srcMap, desMap, x, y, minLevel, maxLevel)
                    if (args.Count >= 6)
                    {
                        var srcMap = M2Share.MapManager.FindMap(args[0].AsString());
                        var desMapName = args[1].AsString();
                        var desX = (short)args[2].AsInt();
                        var desY = (short)args[3].AsInt();
                        var minLevel = args[4].AsInt();
                        var maxLevel = args[5].AsInt();
                        if (srcMap != null)
                        {
                            var humans = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(srcMap, 0, 0, 1000, humans);
                            foreach (var obj in humans)
                            {
                                if (obj is TPlayObject player &&
                                    player.m_Abil.Level >= minLevel &&
                                    player.m_Abil.Level <= maxLevel)
                                    player.SpaceMove(desMapName, desX, desY, 0);
                            }
                        }
                    }
                    return true;

                // === Camp Monster/Animal ===
                case "createcampanimal":
                    // Native CreateCampAnimal is a player-receiver proc (This_player.CreateCampAnimal,
                    // sub_6EB7D8). In the NPC dispatch surface spawn on the interacting player's map
                    // via the same shared core as the player-method form + LIVE sibling createcampmon.
                    // See CallPlayerMethod "createcampanimal" for the full contract + caveats.
                    if (CurrentPlayer?.m_PEnvir != null && args.Count >= 6)
                    {
                        SpawnNativeCampAnimals(CurrentPlayer.m_PEnvir,
                            args[0].AsString(), args[2].AsInt(), args[3].AsInt(),
                            args[4].AsInt(), args[5].AsInt(),
                            args.Count > 6 ? args[6].AsInt() : -1,
                            args.Count > 7 ? args[7].AsInt() : -1);
                        return true;
                    }
                    return RejectUnsupportedNativeApi(out result);

                case "createcampmon":
                    // CreateCampMon(mapName, x, y, range, monName, count, campType)
                    if (args.Count >= 6)
                    {
                        var mapName = args[0].AsString();
                        if (string.IsNullOrEmpty(mapName)) mapName = CurrentNpc.m_sMapName;
                        var x = (short)args[1].AsInt(); var y = (short)args[2].AsInt();
                        if (x <= 0) x = CurrentNpc.m_nCurrX; if (y <= 0) y = CurrentNpc.m_nCurrY;
                        var range = args[3].AsInt();
                        var monName = args[4].AsString();
                        var num = Math.Min(args[5].AsInt(), 200);
                        for (int i = 0; i < num; i++)
                        {
                            var sx = x; var sy = y;
                            if (range > 0)
                            {
                                sx = (short)(x - range + M2Share.RandomNumber.Random(range * 2 + 1));
                                sy = (short)(y - range + M2Share.RandomNumber.Random(range * 2 + 1));
                            }
                            M2Share.UserEngine.RegenMonsterByName(mapName, sx, sy, monName);
                        }
                    }
                    return true;

                case "setmontargetxy":
                    // SetMonTargetXY(x: Integer, y: Integer) - set current NPC/monster target position
                    if (args.Count >= 2)
                    {
                        CurrentNpc.m_nTargetX = (short)args[0].AsInt();
                        CurrentNpc.m_nTargetY = (short)args[1].AsInt();
                    }
                    return true;

                case "uphelmet":
                    // Native signature: procedure UpHelmet(Hum: TPlayer).
                    if (args.Count != 1 ||
                        args[0].ObjVal is not TPlayObject helmetPlayer)
                        return false;
                    helmetPlayer.UpgradeNativeHelmet(CurrentNpc);
                    return true;

                // === CallOut/CallOutEx (Delphi timer system) ===
                case "callout":
                    return ScheduleCallOut(args, false);

                case "calloutex":
                    return ScheduleCallOut(args, true);

                // === HelperDialog (白猪NPC帮助对话框) ===
                case "helperdialog":
                    // 原生 HelperDialog=sub_6F376C 经 merchant-say 下发带标记的对话(见另一处同名 case 注释)。
                    if (args.Count >= 1 && CurrentPlayer != null && !string.IsNullOrEmpty(args[0].AsString()))
                    {
                        CurrentPlayer.SendDefMessage(Grobal2.SM_MERCHANTSAY,
                            CurrentNpc?.ObjectId ?? 0, 0, 0, 0,
                            "NPC/" + args[0].AsString());
                    }
                    return true;

                // ChgEquipmentBreakLevel 原生只注册在 TPlayer 0x72B741 上，已搬到 CallPlayerFunc。

                case "upgradeselfmagicshield":
                    return ExecuteNativeMagicShieldUpgrade(args, false);

                case "upgradeheromagicshield":
                    return ExecuteNativeMagicShieldUpgrade(args, true);

                // === Task/Quest System API ===
                case "questinfo":
                    return ApplyQuestInfo(args);

                case "addtasktouilist":
                    if (args.Count != 2 || CurrentPlayer == null) return false;
                    CurrentPlayer.AddTaskToUIList(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "updatetaskprogress":
                    if (args.Count != 2 || CurrentPlayer == null) return false;
                    CurrentPlayer.UpdateTaskProgress(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "updatetaskdetail":
                    if (args.Count != 2 || CurrentPlayer == null) return false;
                    CurrentPlayer.UpdateTaskDetail(args[0].AsInt(), args[1].AsInt());
                    return true;

                case "deletetaskfromuilist":
                    if (args.Count != 2 || CurrentPlayer == null) return false;
                    CurrentPlayer.DeleteTaskFromUIList(args[0].AsInt(), args[1].AsInt());
                    return true;

                // === NPC INTERACTION / CLICK UI METHODS ===
                // sub_64001C / sub_640058 / sub_640094 / sub_6400D0 / sub_64010C send a
                // bare SM ident on the Clicker's [vtbl+0x250] with Recog = Integer(Self:
                // TPsNpc) and Param/Tag/Series/sMsg all zero; no RM_MERCHANTSAY and no
                // "@page" string is involved. See TPlayObject.NativeScriptUiOpen.
                case "click_open_mosaic_hole":
                    CurrentPlayer?.SendNativeScriptUiOpen(
                        TPlayObject.SM_CLICK_OPEN_MOSAIC_HOLE, CurrentNpc.ObjectId);
                    return true;

                case "click_open_duihuan_contri":
                    CurrentPlayer?.SendNativeScriptUiOpen(
                        TPlayObject.SM_CLICK_OPEN_DUIHUAN_CONTRI, CurrentNpc.ObjectId);
                    return true;

                case "click_open_myoffirankui":
                    CurrentPlayer?.SendNativeScriptUiOpen(
                        TPlayObject.SM_CLICK_OPEN_MYOFFIRANKUI, CurrentNpc.ObjectId);
                    return true;

                case "click_open_attachabilui":
                    CurrentPlayer?.SendNativeScriptUiOpen(
                        TPlayObject.SM_CLICK_OPEN_ATTACHABILUI, CurrentNpc.ObjectId);
                    return true;

                case "click_open_mirtiantiorder":
                    CurrentPlayer?.SendNativeScriptUiOpen(
                        TPlayObject.SM_CLICK_OPEN_MIRTIANTIORDER, CurrentNpc.ObjectId);
                    return true;

                case "click_repair_ex": // compatibility spelling
                    if (args.Count != 2 ||
                        args[0].Type != PasValueType.Object ||
                        args[0].ObjVal is not TPlayObject repairExPlayer ||
                        args[1].Type != PasValueType.Integer)
                        return false;
                    repairExPlayer.SendNativeScriptRepair(CurrentNpc,
                        unchecked((byte)(ushort)args[1].AsInt()));
                    return true;

                case "click_storage":
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.m_nStoragePage = 0;
                        CurrentPlayer.SendSaveItemList(CurrentNpc.ObjectId);
                    }
                    return true;

                case "click_acc_storage":
                    QueueNativeAccountStorageClick(false);
                    return true;

                case "click_acc_getback":
                    QueueNativeAccountStorageClick(true);
                    return true;

                case "click_eqp_operate":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId,
                            args.Count >= 1 ? args[0].AsInt() : 0, 0, $"{CurrentNpc.m_sCharName}/@eqpoperate");
                    return true;

                case "click_vote":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId,
                            args.Count >= 1 ? args[0].AsInt() : 0, 0, $"{CurrentNpc.m_sCharName}/@vote");
                    return true;

                case "click_takeoutgold":
                    return TryHandleNativeCastleGoldClick(args, true);

                case "click_savegold":
                    return TryHandleNativeCastleGoldClick(args, false);

                case "clickupgradejewels":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, 0, 0,
                            $"{CurrentNpc.m_sCharName}/@upgradejewels/{(args.Count >= 1 ? args[0].AsInt() : 0)}");
                    return true;

                case "clientaskybduanzao":
                    return RejectUnsupportedNativeApi(out result);

                case "clientquestgetdiam":
                    return RejectUnsupportedNativeApi(out result);

                case "refreshcredit":
                    return RejectUnsupportedNativeApi(out result);

                case "clientreqgetbacklostitem":
                    // Native lost-item database transaction is absent.
                    return RejectUnsupportedNativeApi(out result);

                case "reqitembygoldact":
                    // Delphi declares this as a procedure with exactly one Player argument.
                    // Keep the function-shaped registration closed: the interpreter probes
                    // functions before methods and would otherwise swallow the native call.
                    if (args.Count != 1 || args[0].ObjVal is not TPlayObject goldActPlayer)
                        return false;
                    goldActPlayer.ReqItemByGoldAct(CurrentNpc);
                    return true;

                case "reqitembygoldid":
                    // The reward codec/state machine is retained for later
                    // authority integration, but no native GoldID account
                    // authority is present in the current deployment.
                    return RejectUnsupportedNativeApi(out result);

                case "reqitembyplatina":
                    // sub_6C8284 @0x006C8284 — platinum role segment claim.
                    if (args.Count != 1 ||
                        args[0].ObjVal is not TPlayObject platinaPlayer)
                        return false;
                    platinaPlayer.ReqItemByPlatina(CurrentNpc);
                    return true;

                case "clientquerylastdealmsg":
                    if (args.Count >= 1 &&
                        args[0].ObjVal is TPlayObject dealLogPlayer)
                    {
                        dealLogPlayer.ShowNativeYbLastDealLog(CurrentNpc, 0xAF,
                            string.Empty, string.Empty);
                        return true;
                    }
                    return false;
                case "clientqueryybdealitem":
                    // Native YB consignment query services are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "clientaskopenyb":
                    return RejectUnsupportedNativeApi(out result);

                case "reqgetfirstusedgift":
                    // sub_7520E0 @0x007520E0 — config\新手礼包.ini first gift.
                    if (args.Count != 1 ||
                        args[0].ObjVal is not TPlayObject giftPlayer)
                        return false;
                    _ = giftPlayer.TryClaimNativeNewbieGift(CurrentNpc);
                    return true;

                case "clientybbuylf":
                    // Native signature: procedure ClientYBbuyLF(Player; const Num: integer).
                    if (args.Count != 2 ||
                        args[0].ObjVal is not TPlayObject ybBuyLfPlayer)
                        return false;
                    ybBuyLfPlayer.ClientYBbuyLF(CurrentNpc, args[1].AsInt());
                    return true;

                case "buywinefromnpc":
                    return RejectUnsupportedNativeApi(out result);

                case "givewine":
                    if (args.Count >= 1 && CurrentNpc != null)
                        CurrentNpc.GotoLable_GiveItem(CurrentPlayer, args[0].AsString(), args.Count >= 2 ? args[1].AsInt() : 1);
                    return true;

                case "usenick":
                    return TryUseNativeNick(args, out result);

                case "clickcomposedress":
                    // Native callback is nullsub_60.
                    return true;

                case "foundrylist":
                    // Native signature: procedure FoundryList(Clicker: TPlayer).
                    if (args.Count != 1 ||
                        args[0].ObjVal is not TPlayObject foundryPlayer)
                        return false;
                    foundryPlayer.ShowNativeDiamondFoundryList(CurrentNpc,
                        Volatile.Read(ref M2Share.DiamondFoundry));
                    return true;

                case "makeitemusediam":
                    // Native signature: procedure MakeItemUseDiam(Hum: TPlayer). Verified byte/behavior-
                    // faithful vs sub_64DF3C (staging/diamond_forge_sub64DF3C_verify_20260802.md -> GO):
                    // consume-before-produce, per-slot atomic (no whole-recipe rollback), exactly one
                    // output item never duplicated; the +0x711 equipment-unlock escrow gate is absent in
                    // this server, so the native confirm-lock is faithfully always-open (no dupe window).
                    if (args.Count != 1 ||
                        args[0].ObjVal is not TPlayObject forgePlayer)
                        return false;
                    forgePlayer.ExecuteNativeDiamondForge(CurrentNpc,
                        Volatile.Read(ref M2Share.DiamondFoundry));
                    return true;

                case "openneedkeybox":
                    if (args.Count != 1 ||
                        args[0].Type != PasValueType.Object ||
                        args[0].ObjVal is not TPlayObject needKeyPlayer)
                        return false;
                    needKeyPlayer.TryOpenNativeNeedKeyBox(true, out _);
                    return true;

                case "openneedkeybox2":
                    if (args.Count != 1 ||
                        args[0].Type != PasValueType.Object ||
                        args[0].ObjVal is not TPlayObject needKeyYuanbaoPlayer)
                        return false;
                    needKeyYuanbaoPlayer.TrySubmitNativeNeedKeyBoxYuanbao(
                        CurrentNpc);
                    return true;

                case "openluckbox":
                case "openluckbox2":
                    // Native luck-box reward state and atomic delivery are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "rndgetmedal":
                    // Native medal selection and grant transaction is not mapped.
                    return RejectUnsupportedNativeApi(out result);

                // === ITEM / GIVE (NPC-side variants) ===
                // GiveItemsToOther 原生只注册在 TPlayer 0x72B7E9 上，已搬到 CallPlayerFunc。

                case "giveconfigprizetemp":
                    if (args.Count != 3 || args[0].ObjVal is not TPlayObject tempPrizePlayer)
                        return false;
                    using (var context = PushItemContext(tempPrizePlayer, CurrentNpc,
                        CurrentInputOk, CurrentInputStr, CurrentItem))
                    {
                        _ = TryNativeGiveConfigPrize(args[1].AsInt(),
                            args[2].AsString(), false);
                    }
                    return true;

                // === Map Event ===
                case "createmapevent":
                case "removemapevent":
                    // Native timed map-event manager is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === NPC Animation Actions ===
                case "dohitaction":
                    // DoHitAction - play NPC hit animation
                    if (CurrentNpc != null)
                        CurrentNpc.SendRefMsg(Grobal2.RM_STRUCK, CurrentNpc.m_btDirection, CurrentNpc.m_nCurrX, CurrentNpc.m_nCurrY, 0, "");
                    return true;

                case "dohideaction":
                    // DoHideAction - hide NPC from all players
                    if (CurrentNpc != null)
                    {
                        CurrentNpc.m_boGhost = true;
                        CurrentNpc.SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                    }
                    return true;

                case "doshowaction":
                    // DoShowAction - show NPC to all players
                    if (CurrentNpc != null)
                    {
                        CurrentNpc.m_boGhost = false;
                        CurrentNpc.SendRefMsg(Grobal2.RM_ALIVE, CurrentNpc.m_btDirection, CurrentNpc.m_nCurrX, CurrentNpc.m_nCurrY, 0, "");
                    }
                    return true;

                case "doflyaction":
                    // DoFlyAction(desMapName, var desX, desY) - fly NPC to target map
                    if (args.Count >= 3 && CurrentNpc != null)
                    {
                        var mapName = args[0].AsString();
                        var desX = (short)args[1].AsInt();
                        var desY = (short)args[2].AsInt();
                        var targetMap = M2Share.MapManager.FindMap(mapName);
                        if (targetMap != null)
                        {
                            CurrentNpc.SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                            CurrentNpc.m_sMapName = mapName;
                            CurrentNpc.m_nCurrX = desX;
                            CurrentNpc.m_nCurrY = desY;
                            CurrentNpc.m_PEnvir = targetMap;
                            CurrentNpc.SendRefMsg(Grobal2.RM_ALIVE, CurrentNpc.m_btDirection, desX, desY, 0, "");
                        }
                    }
                    return true;

                // === Hide/Show NPC on Specific Map ===
                case "dohidenpcex":
                    // DoHideNpcEx(sMapName, sNpcName) - hide NPC by name on specific map
                    if (args.Count >= 2)
                    {
                        var mapName = args[0].AsString();
                        var npcName = args[1].AsString();
                        var map = M2Share.MapManager.FindMap(mapName);
                        if (map != null)
                        {
                            var humans = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(map, 0, 0, 1000, humans);
                            foreach (var obj in humans)
                            {
                                if (obj is TPlayObject player && obj.m_sCharName != null
                                    && obj.m_sCharName.IndexOf(npcName, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    player.SendRefMsg(Grobal2.RM_DISAPPEAR, 0, 0, 0, 0, "");
                                }
                            }
                        }
                    }
                    return true;

                case "doshow npcex":
                    // DoShowNpcEx(sMapName, sNpcName) - show NPC by name on specific map
                    if (args.Count >= 2)
                    {
                        var mapName = args[0].AsString();
                        var npcName = args[1].AsString();
                        var map = M2Share.MapManager.FindMap(mapName);
                        if (map != null)
                        {
                            var humans = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(map, 0, 0, 1000, humans);
                            foreach (var obj in humans)
                            {
                                if (obj is TPlayObject player && obj.m_sCharName != null
                                    && obj.m_sCharName.IndexOf(npcName, StringComparison.OrdinalIgnoreCase) >= 0)
                                {
                                    player.SendRefMsg(Grobal2.RM_ALIVE, obj.m_btDirection, obj.m_nCurrX, obj.m_nCurrY, 0, "");
                                }
                            }
                        }
                    }
                    return true;

                // === Roll Messages ===
                case "sendrollmsgincurrentmap":
                    // SendRollMsgInCurrentMap(sMsgStr, iType) - rolling banner message on current map
                    if (args.Count >= 1 && CurrentNpc?.m_PEnvir != null)
                    {
                        var msg = args[0].AsString();
                        var iType = args.Count >= 2 ? args[1].AsInt() : 1;
                        var humans = new List<TBaseObject>();
                        M2Share.UserEngine.GetMapRageHuman(CurrentNpc.m_PEnvir, 0, 0, 1000, humans);
                        foreach (var obj in humans)
                        {
                            if (obj is TPlayObject player)
                                player.SysMsg(msg, MsgColor.Red, MsgType.Castle);
                        }
                    }
                    return true;

                case "rollmsg":
                    // RollMsg(MsgStr) - global rolling message
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.System);
                    return true;

                case "rollmsgex":
                    // RollMsgEx(MsgStr, iType) - global rolling message with type
                    if (args.Count >= 1)
                    {
                        var msgType = args.Count >= 2 ? (MsgType)args[1].AsInt() : MsgType.Castle;
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), msgType);
                    }
                    return true;

                // === Map Param ===
                case "setmapparam":
                    // SetMapParam(param) - set map parameter flag
                    if (CurrentNpc?.m_PEnvir != null)
                    {
                        var param = args.Count >= 1 ? args[0].AsInt() : 0;
                        SetGlobalVar(200, 1, PasValue.FromInt(param));
                    }
                    return true;

                // === Repair Sword Effect ===
                case "playrepairswordeffect":
                    // Native timed repair-effect message is not mapped.
                    return RejectUnsupportedNativeApi(out result);

                // === Player Statuary ===
                case "loadplayerstatuary":
                case "appearancetransform":
                case "updatefamenpc":
                case "getfameplayernamebyrank":
                    // Native statue, transform, and fame-ranking managers are absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Recycle / Free Retrieve ===
                // sub_647F08 @0x00647F08 is the same six-instruction gate + bare send as the
                // Click_Open_* family (0x00647F30 mov dx,0x10FF / 0x00647F38 call [ebx+0x250]);
                // it reads no recycle configuration, so the send stands on its own.
                case "clickopenfreeretrieve":
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.SendNativeScriptUiOpen(
                            TPlayObject.SM_CLICK_OPEN_FREERETRIEVE, CurrentNpc.ObjectId);
                        result = PasValue.FromBool(true);
                    }
                    return true;

                case "getrecycleitemfeename":
                case "getfreeretrievelist":
                    // Native recycle/free-retrieve configuration and persistence are absent.
                    return RejectUnsupportedNativeApi(out result);

                // === PaoDian ===
                // StartPaodian 原生只注册在 TPlayer 0x72BAC0 上，已搬到 CallPlayerFunc。
                case "setpaodianprizeandperiod":
                    // Native paodian reward scheduler and lifecycle are absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Random Item ===
                case "getranditem":
                    // GetRandItem(idx) - get a random item by config index
                    {
                        var idx = args.Count >= 1 ? args[0].AsInt() : 0;
                        var itemIdx = GetPlayerVarOrZero('V', 50, idx);
                        result = itemIdx > 0
                            ? PasValue.FromString(M2Share.UserEngine.GetStdItemName(itemIdx))
                            : PasValue.FromString("");
                    }
                    return true;

                case "getranditemex":
                    // GetRandItemEx(curTimeTag, idx)
                    {
                        var curTimeTag = args.Count >= 1 ? args[0].AsInt() : 0;
                        var idx = args.Count >= 2 ? args[1].AsInt() : 0;
                        var itemIdx = GetPlayerVarOrZero('V', 50, idx + (curTimeTag % 100));
                        result = itemIdx > 0
                            ? PasValue.FromString(M2Share.UserEngine.GetStdItemName(itemIdx))
                            : PasValue.FromString("");
                    }
                    return true;

                // === Quest / Task ===
                case "getcurrentnewyearorder":
                    // GetCurrentNewYearOrder(Hum) - get current new year order for player
                    {
                        result = PasValue.FromInt(GetPlayerVarOrZero('V', 18, 3));
                    }
                    return true;

                case "getlastnewyearorder":
                    // GetLastNewYearOrder(Hum) - get last new year order
                    {
                        result = PasValue.FromInt(GetPlayerVarOrZero('V', 18, 4));
                    }
                    return true;

                case "newyearorderpresent":
                    // NewYearOrderPresent - give new year order present to player
                    if (CurrentPlayer != null && CurrentNpc != null)
                    {
                        CurrentNpc.GotoLable_GiveItem(CurrentPlayer, "新年礼物", 1);
                    }
                    return true;

                case "newyearorderaccept":
                    // NewYearOrderAccept - accept new year order
                    if (CurrentPlayer != null)
                    {
                        SetPlayerVar('V', 18, 3, PasValue.FromInt(1));
                    }
                    return true;

                case "currentnewyearorderpresent":
                    // CurrentNewYearOrderPresent - give current new year order present
                    if (CurrentPlayer != null && CurrentNpc != null)
                    {
                        CurrentNpc.GotoLable_GiveItem(CurrentPlayer, "新年礼物", 1);
                    }
                    return true;

                case "currentnewyearorderaccept":
                    // CurrentNewYearOrderAccept - accept current new year order
                    if (CurrentPlayer != null)
                    {
                        SetPlayerVar('V', 18, 3, PasValue.FromInt(1));
                    }
                    return true;

                case "newyeargetmygift":
                    // NewYearGetMyGift - 1:1 from Delphi FUN_00601e2c
                    // Three-tier prize: FIND_IN_SET("acTop",PrizeType) / ("acRandom",PrizeType) / ("pcTop10",PrizeType)
                    // Checks AND combination with GiftType to prevent double-claiming
                    if (CurrentPlayer == null) return true;
                    {
                        var name = CurrentPlayer.m_sCharName.Replace("'", "''");
                        // Query NewYearPresentAuto table for player record
                        var rows = ExecuteSqlQuery(
                            $"SELECT PrizeType, GiftType FROM NewYearPresentAuto WHERE ChrName='{name}'");
                        if (rows.Count > 0)
                        {
                            var prizeType = rows[0].GetValueOrDefault("PrizeType", "0");
                            var giftType = rows[0].GetValueOrDefault("GiftType", "0");
                            // Check three-tier prize using FIND_IN_SET equivalent
                            bool hasAcTop = prizeType.Contains("acTop");
                            bool hasAcRandom = prizeType.Contains("acRandom");
                            bool hasPcTop10 = prizeType.Contains("pcTop10");
                            // Prevent double-claiming — check GiftType AND
                            if (hasAcTop && giftType.Contains("acTop"))
                            {
                                                                return true;
                            }
                            if (hasAcRandom && giftType.Contains("acRandom"))
                            {
                                                                return true;
                            }
                            if (hasPcTop10 && giftType.Contains("pcTop10"))
                            {
                                                                return true;
                            }
                            // Award highest available prize: acTop > acRandom > pcTop10
                            if (hasAcTop && !giftType.Contains("acTop"))
                            {
                                CurrentNpc.GotoLable_GiveItem(CurrentPlayer, "春节红包", 3);
                                ExecuteSqlNonQuery(
                                    $"UPDATE NewYearPresentAuto SET GiftType=CONCAT_WS(',',GiftType,'acTop') WHERE ChrName='{name}'");
                                                                return true;
                            }
                            if (hasAcRandom && !giftType.Contains("acRandom"))
                            {
                                CurrentNpc.GotoLable_GiveItem(CurrentPlayer, "春节红包", 2);
                                ExecuteSqlNonQuery(
                                    $"UPDATE NewYearPresentAuto SET GiftType=CONCAT_WS(',',GiftType,'acRandom') WHERE ChrName='{name}'");
                                                                return true;
                            }
                            if (hasPcTop10 && !giftType.Contains("pcTop10"))
                            {
                                CurrentNpc.GotoLable_GiveItem(CurrentPlayer, "春节红包", 1);
                                ExecuteSqlNonQuery(
                                    $"UPDATE NewYearPresentAuto SET GiftType=CONCAT_WS(',',GiftType,'pcTop10') WHERE ChrName='{name}'");
                                                                return true;
                            }
                        }
                                            }
                    return true;

                // ReqPieceUpNewYearPicture 原生只注册在 TPlayer 0x72B65D 上，已搬到 CallPlayerFunc。
                case "composeitem":
                    // Native item-compose transaction is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === YB Shop ===
                case "sendybshopconfig":
                    // Native M2 registers the GBK PAS configuration in the global shop manager.
                    if (args.Count >= 1)
                    {
                        result = PasValue.FromBool(
                            global::GameSvr.Mall.MallManager.Instance.RefreshCache());
                    }
                    else
                    {
                        result = PasValue.FromBool(false);
                    }
                    return true;

                case "setybshoprefreshtime":
                    if (args.Count >= 1)
                    {
                        result = PasValue.FromBool(
                            global::GameSvr.Mall.MallManager.Instance.ConfigureRefreshTime(
                                args[0].AsString()));
                    }
                    else
                    {
                        result = PasValue.FromBool(false);
                    }
                    return true;

                case "ybdealdialogshowmode":
                    // Native YB consignment uses its own 1251..1265 transaction flow.
                    return RejectUnsupportedNativeApi(out result);

                case "notifyclientopenupdateclothes":
                    // NotifyClientOpenUpdateClothes(APlayer) - notify client to open clothes update UI
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, 0, 0,
                            $"{CurrentNpc.m_sCharName}/@updateclothes");
                    }
                    return true;

                // === Normal Castle ===
                case "getnormalcastleflagowner":
                case "getnormalcastlescorerslt":
                case "getnormalcastlematchtakeinfo":
                case "getnormalcastletakeinfo":
                    // Native normal-castle match and score manager is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Recall Players ===
                case "npcrecallplayers":
                    // NpcRecallPlayers(sSrcMap, num) - recall players from source map to NPC position
                    if (args.Count >= 2 && CurrentNpc != null)
                    {
                        var sSrcMap = args[0].AsString();
                        var num = args[1].AsInt();
                        var srcMap = M2Share.MapManager.FindMap(sSrcMap);
                        if (srcMap != null)
                        {
                            var humans = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(srcMap, 0, 0, 1000, humans);
                            int moved = 0;
                            foreach (var obj in humans)
                            {
                                if (moved >= num) break;
                                if (obj is TPlayObject player)
                                {
                                    player.SpaceMove(CurrentNpc.m_sMapName, CurrentNpc.m_nCurrX, CurrentNpc.m_nCurrY, 0);
                                    moved++;
                                }
                            }
                        }
                    }
                    return true;

                // === Dialog Windows ===
                case "opendialogswindows":
                    // OpenDialogsWindows(Player, WindowsID, SubID) - open dialog window
                    if (args.Count >= 2 && CurrentPlayer != null)
                    {
                        var windowsId = args[1].AsInt();
                        var subId = args.Count >= 3 ? args[2].AsInt() : 0;
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, windowsId, subId, "");
                    }
                    return true;

                // PlayerCry 0x729A4B 与 PlayerGive 0x729A5C 原生都是 AddFunction 注册的
                // 全局函数，不是 TPsNpc 方法，已搬到 CallStandaloneFunction。

                // === Route / Gate ===
                case "enterroutewaybylf":
                    if (args.Count != 2
                        || args[0].ObjVal is not TPlayObject routePlayer
                        || args[1].Type != PasValueType.Boolean)
                        return false;
                    routePlayer.EnterNativeMagicTowerRoute(CurrentNpc,
                        M2Share.MagicTowerRouteSequencer);
                    return true;

                case "enterroutewaybylfex":
                    if (args.Count != 3
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject routeExPlayer
                        || args[1].Type != PasValueType.Boolean
                        || args[2].Type != PasValueType.Boolean
                        || M2Share.MagicTowerRouteSequencer == null)
                        return false;
                    routeExPlayer.EnterNativeMagicTowerRouteEx(CurrentNpc,
                        M2Share.MagicTowerRouteSequencer, args[2].BoolVal);
                    return true;

                case "enternewguan":
                    if (args.Count != 1
                        || args[0].ObjVal is not TPlayObject newGuanPlayer
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    newGuanPlayer.EnterNativeMagicTowerNewGuan(CurrentNpc,
                        M2Share.DynamicRoomService);
                    return true;

                case "enterguan":
                    if (args.Count != 1
                        || args[0].ObjVal is not TPlayObject guanPlayer
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    guanPlayer.EnterNativeMagicTowerGuan(CurrentNpc,
                        M2Share.DynamicRoomService);
                    return true;

                case "entermystery":
                    if (args.Count != 1
                        || args[0].ObjVal is not TPlayObject mysteryPlayer
                        || M2Share.DynamicRoomService?.IsInitialized != true
                        || M2Share.MagicTowerRouteSequencer == null)
                        return false;
                    mysteryPlayer.EnterNativeMagicTowerNext2(CurrentNpc,
                        M2Share.DynamicRoomService,
                        M2Share.MagicTowerRouteSequencer);
                    return true;

                case "enternext":
                    if (args.Count != 2
                        || args[0].ObjVal is not TPlayObject nextPlayer
                        || args[1].Type != PasValueType.Boolean
                        || M2Share.DynamicRoomService?.IsInitialized != true
                        || M2Share.MagicTowerRouteSequencer == null)
                        return false;
                    nextPlayer.EnterNativeMagicTowerNext(CurrentNpc,
                        args[1].AsBool(), M2Share.DynamicRoomService,
                        M2Share.MagicTowerRouteSequencer);
                    return true;

                case "enternext2":
                    if (args.Count != 1
                        || args[0].ObjVal is not TPlayObject next2Player
                        || M2Share.DynamicRoomService?.IsInitialized != true
                        || M2Share.MagicTowerRouteSequencer == null)
                        return false;
                    next2Player.EnterNativeMagicTowerNext2(CurrentNpc,
                        M2Share.DynamicRoomService,
                        M2Share.MagicTowerRouteSequencer);
                    return true;

                // === Monster / Combat ===
                case "wantwarmon":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject warMonsterPlayer)
                        return false;
                    warMonsterPlayer.WantNativeMagicTowerWarMon(CurrentNpc);
                    return true;

                case "getskyprize":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject skyPrizePlayer)
                        return false;
                    _ = skyPrizePlayer.GetNativeMagicTowerSkyPrize(CurrentNpc);
                    return true;

                case "clientgetprize":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject clientPrizePlayer)
                        return false;
                    _ = clientPrizePlayer.ClientGetNativeMagicTowerPrize(CurrentNpc);
                    return true;

                case "openactionbox":
                    // OpenActionBox - open action/boss reward box
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, 0, 0,
                            $"{CurrentNpc.m_sCharName}/@actionbox");
                    }
                    return true;

                case "showbook":
                    // ShowBook 不在原生 PAS 注册表内(只有 SubmitLegendBook)——属于原版没有的 API，
                    // 且此前发的是英文 "[Book] xxx"。按"原版没有的移除"改为静默 no-op。
                    return true;

                // === Fee / Time Buy ===
                case "upfeeuseract":
                    // Native fee-user activity service is absent.
                    return RejectUnsupportedNativeApi(out result);

                case "requsetimebuylf":
                    // Native ReqUseTimeBuyLF is an asynchronous account-service request.
                    return RejectUnsupportedNativeApi(out result);

                // === Medal Abilities ===
                case "spegetmedalbyry":
                    if (args.Count != 2 ||
                        args[0].ObjVal is not TPlayObject medalRyPlayer ||
                        args[1].Type != PasValueType.String)
                        return false;
                    medalRyPlayer.ExchangeNativeMedalByRy(CurrentNpc,
                        args[1].StrVal);
                    return true;

                case "spegetmedalbysw":
                    if (args.Count != 2 ||
                        args[0].ObjVal is not TPlayObject medalSwPlayer ||
                        args[1].Type != PasValueType.String)
                        return false;
                    medalSwPlayer.ExchangeNativeMedalBySw(CurrentNpc,
                        args[1].StrVal);
                    return true;

                case "upmedalabil":
                    // UpMedalAbil - recipe-based medal upgrade. Bag needs 5 materials, medal must be worn in U_BUJUK slot.
                    // 50% success: DuraMax-5, DC/MC/SC+1. 50% fail: medal destroyed.
                    // Cannot upgrade if DuraMax <= 5.
                    if (CurrentPlayer == null) return true;
                    {
                        // Verify all 5 recipe materials are in bag
                        var materials = new[] { "矿石", "紫宝石矿", "勋章之心", "绿宝石矿", "金水" };
                        foreach (var mat in materials)
                        {
                            if (CurrentPlayer.CheckItems(mat) == null)
                            {
                                CurrentPlayer.SysMsg("缺少配方原料", MsgColor.Red, MsgType.Hint);
                                return true;
                            }
                        }
                        // Medal must be equipped in BUJUK slot (index 9)
                        var medal = CurrentPlayer.m_UseItems[Grobal2.U_BUJUK];
                        if (medal == null || medal.wIndex <= 0)
                        {
                            CurrentPlayer.SysMsg("请先佩戴勋章", MsgColor.Red, MsgType.Hint);
                            return true;
                        }
                        if (medal.DuraMax <= 5)
                        {
                            CurrentPlayer.SysMsg("勋章持久过低，无法升级", MsgColor.Red, MsgType.Hint);
                            return true;
                        }
                        // Consume all 5 materials
                        foreach (var mat in materials)
                            TakeItems(mat, 1);
                        // 50% success rate
                        if (M2Share.RandomNumber.Random(100) < 50)
                        {
                            // Success: reduce max durability by 5 (min 1), improve DC/MC/SC by 1
                            medal.DuraMax = (ushort)Math.Max(1, medal.DuraMax - 5);
                            if (medal.Dura > medal.DuraMax)
                                medal.Dura = medal.DuraMax;
                            if (medal.btValue != null && medal.btValue.Length > 4)
                            {
                                medal.btValue[0] = (byte)Math.Min(255, medal.btValue[0] + 1);
                                medal.btValue[1] = (byte)Math.Min(255, medal.btValue[1] + 1);
                                medal.btValue[2] = (byte)Math.Min(255, medal.btValue[2] + 1);
                            }
                            CurrentPlayer.SendUpdateItem(medal);
                            CurrentPlayer.SysMsg("勋章升级成功！", MsgColor.Green, MsgType.Hint);
                        }
                        else
                        {
                            // Failure: medal breaks (destroyed)
                            var brokenMedal = CurrentPlayer.m_UseItems[Grobal2.U_BUJUK];
                            CurrentPlayer.m_UseItems[Grobal2.U_BUJUK] = null;
                            CurrentPlayer.SendDelItems(brokenMedal);
                            CurrentPlayer.SysMsg("勋章升级失败，勋章碎裂", MsgColor.Red, MsgType.Hint);
                        }
                    }
                    return true;

                // === LF Bag ===
                case "buylfbag":
                    return RejectUnsupportedNativeApi(out result);

                // === NPC Drop Item ===
                case "npcdropitem":
                    // NPCDropItem(ItemName, nRanger, ItemNum) - NPC drops items on ground
                    if (args.Count >= 3 && CurrentNpc != null)
                    {
                        var itemName = args[0].AsString();
                        var nRanger = args[1].AsInt();
                        var itemNum = Math.Min(args[2].AsInt(), 50);
                        for (int i = 0; i < itemNum; i++)
                        {
                            var userItem = new TUserItem();
                            if (M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem))
                            {
                                int dx = 0, dy = 0;
                                if (nRanger > 0)
                                {
                                    dx = M2Share.RandomNumber.Random(nRanger * 2 + 1) - nRanger;
                                    dy = M2Share.RandomNumber.Random(nRanger * 2 + 1) - nRanger;
                                }
                                CurrentNpc.DropItemDown(userItem, 2, false, null, CurrentNpc);
                            }
                        }
                    }
                    return true;

                case "npcdropitemtoallmaprnd":
                    // Native all-map drop selector and ownership rules are absent.
                    return RejectUnsupportedNativeApi(out result);

                // === GetComingVersion ===
                case "getcomingversion":
                    // GetComingVersion(idx) - get version string by index
                    {
                        result = PasValue.FromString(M2Share.g_sVersionDate ?? "");
                    }
                    return true;

                // === String Utility ===
                case "getvalidstr":
                    // GetValidStr(Str, var Dest, divider) - extract substring before divider (Delphi-style)
                    if (args.Count >= 3)
                    {
                        var str = args[0].AsString();
                        var divider = args[2].AsString();
                        var idx = str.IndexOf(divider, StringComparison.Ordinal);
                        if (idx >= 0)
                        {
                            args[1] = PasValue.FromString(str.Substring(0, idx));
                            args[0] = PasValue.FromString(str.Substring(idx + divider.Length));
                        }
                        else
                        {
                            args[1] = PasValue.FromString(str);
                            args[0] = PasValue.FromString("");
                        }
                    }
                    return true;

                // === Debug Output ===
                case "debugout":
                    // DebugOut(s) - output debug string to main log
                    if (args.Count >= 1)
                        M2Share.MainOutMessage(args[0].AsString());
                    return true;

                // === Hero ===
                case "resetheroexpfealty":
                    // ResetHeroExpFealty(player) - reset hero exp and fealty
                    if (CurrentPlayer != null)
                    {
                        SetPlayerVar('V', 15, 2, PasValue.FromInt(0));
                        SetPlayerVar('V', 15, 4, PasValue.FromInt(0));
                    }
                    return true;

                // === Body Luck ===
                case "addplayerbodyluck":
                    // 原版 AddPlayerBodyLuck (PAS sub_648598 -> sub_7698BC): 身体幸运 [+0x164] += luck 后
                    // clamp[-10,+5]。[+0x164] == m_nBodyLuckLevel(与武器升级 Merchant.cs:1045 / 防御幸运
                    // NativeMagicDamage.cs:246 / 本文件暴击判定同源)。原 C# 误写 weapon.btValue[3](武器幸运)
                    // 且从 args[1] 取值——与原生字段及本派发器约定(args[0]=首个脚本参、玩家=CurrentPlayer，
                    // 见 npcdropitem/decactivepoint)均不符，已改为对 CurrentPlayer 身体幸运做整级 clamp 加法，
                    // 并去除原武器/RecalcAbilitys/RM_ABILITY 伪副作用(原生仅做 add+clamp)。
                    // (证据: staging/pas_baddr_ladders_20260801.md sub_648598 + luck_hide_out.txt sub_7698BC;
                    //  fidelity_divergences_20260731.md #4。sub_648598 参数个数未经 idat 逐字核实——见交付说明。)
                    if (args.Count >= 1 && CurrentPlayer != null)
                    {
                        var addPoint = args[0].AsInt();
                        var newLuck = CurrentPlayer.m_nBodyLuckLevel + addPoint;
                        if (newLuck > 5) newLuck = 5;
                        else if (newLuck < -10) newLuck = -10;
                        CurrentPlayer.m_nBodyLuckLevel = newLuck;
                    }
                    return true;

                // === Student Management (武馆教头) ===
                case "sendkaichulist":
                    {
                        var listMaster = args.Count >= 1
                            && args[0].ObjVal is TPlayObject requestedMaster
                                ? requestedMaster
                                : CurrentPlayer;
                        if (listMaster == null) return true;
                        if (listMaster.m_nGold < 100000)
                        {
                            SendShiMenNpcDialog(listMaster,
                                "你携带的金币数量不够！");
                            return true;
                        }
                        if (listMaster.m_nStudentCount == 0)
                        {
                            SendShiMenNpcDialog(listMaster, "你没有徒弟！");
                            return true;
                        }
                        SendShiMenNpcDialog(listMaster,
                            BuildKaiChuDialog(listMaster));
                        return true;
                    }

                case "npckickoutstu":
                    {
                        var kickMaster = CurrentPlayer;
                        var indexArg = 0;
                        if (args.Count >= 2
                            && args[0].ObjVal is TPlayObject requestedMaster)
                        {
                            kickMaster = requestedMaster;
                            indexArg = 1;
                        }
                        if (kickMaster == null || args.Count <= indexArg)
                            return true;

                        if (kickMaster.m_nGold < 100000
                            || kickMaster.m_nStudentCount == 0)
                            return true;

                        kickMaster.m_nGold -= 100000;
                        kickMaster.GoldChanged();

                        var stuIdx = args[indexArg].AsInt();
                        EnsureStudentSlots(kickMaster);
                        if ((uint)stuIdx >= StudentSlotCount
                            || string.IsNullOrEmpty(
                                kickMaster.m_sStudentNames[stuIdx]))
                        {
                            SendShiMenNpcDialog(kickMaster,
                                "手续费我收下了！但你使用的手段...嘿嘿，我帮不了你");
                            return true;
                        }

                        var stuName = kickMaster.m_sStudentNames[stuIdx];
                        var stu = M2Share.UserEngine?.GetPlayObject(stuName);
                        if (stu != null)
                        {
                            if (string.Equals(stu.m_sMasterName,
                                    kickMaster.m_sCharName,
                                    StringComparison.Ordinal))
                            {
                                ClearStudentRelation(stu);
                                stu.SendMsg(stu,
                                    Grobal2.RM_MASTERRELATION, 0, 9,
                                    0, 0, kickMaster.m_sCharName);
                                SaveShiMenPlayer(stu);
                                stu.SysMsg("你的师傅已将你逐出师门",
                                    MsgColor.Green, MsgType.Hint);
                            }
                        }
                        else
                        {
                            TryClearOfflineStudentRelation(kickMaster, stuName);
                        }
                        ClearStudentSlot(kickMaster, stuIdx);
                        SendShiMenNpcDialog(kickMaster, "操作成功！");
                        return true;
                    }

                case "chgcelebcolor":
                    if (args.Count < 1)
                        return false;
                    result = PasValue.FromBool(
                        NativeCelebrityStatueManager.SetCelebrityColor(
                            CurrentNpc, args[0].AsInt() == 1));
                    return result.AsBool();

                case "addplayerhonorvalue":
                    if (args.Count < 1 || M2Share.HonorValueManager == null)
                        return false;
                    result = PasValue.FromBool(
                        M2Share.HonorValueManager.TryAdd(
                            NativeCelebrityStatueManager.GetCelebrityName(CurrentNpc),
                            args[0].AsInt(), out _));
                    return result.AsBool();

                case "subplayerhonorvalue":
                    if (args.Count < 1 || M2Share.HonorValueManager == null)
                        return false;
                    result = PasValue.FromBool(
                        M2Share.HonorValueManager.TrySubtract(
                            NativeCelebrityStatueManager.GetCelebrityName(CurrentNpc),
                            args[0].AsInt(), out _));
                    return result.AsBool();

                case "engagearcher":
                    if (args.Count != 2
                        || args[0].ObjVal is not TPlayObject engagePlayer
                        || args[1].Type != PasValueType.Integer)
                        return false;
                    engagePlayer.EngageNativeMagicTowerArcher(CurrentNpc,
                        args[1].AsInt());
                    return true;

                case "chkmonanditem":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject checkPlayer)
                        return false;
                    checkPlayer.CheckNativeMagicTowerMonAndItem(CurrentNpc);
                    return true;

                case "reqpopgift":
                case "psaddcrethp":
                case "southwildstartconvoy":
                case "southwildstartmonattack":
                    // These APIs depend on native challenge, ownership, social, or storage state.
                    return RejectUnsupportedNativeApi(out result);

                // =====================================================================
                // 沉默回退族(NPC 面)——原生已注册但 C# 无 case 的 PAS 过程。
                // 这 5 个注册在 TPsNpc PAS 面(registrar sub_738720,
                // class 全局 0x0063CFA8 = TPsNpc),接收者是 NPC 对象而非玩家。
                // 此前语句形式会抛 PasRuntimeException "函数找不到" 中断脚本。
                // =====================================================================

                // Click_RepairEx sub_64016C: hidden Self is the NPC, EDX is
                // Clicker and ECX is RepairMode:Word; only CL is persisted.
                case "click_repairex":
                    if (args.Count != 2 ||
                        args[0].Type != PasValueType.Object ||
                        args[0].ObjVal is not TPlayObject nativeRepairExPlayer ||
                        args[1].Type != PasValueType.Integer)
                        return false;
                    nativeRepairExPlayer.SendNativeScriptRepair(CurrentNpc,
                        unchecked((byte)(ushort)args[1].AsInt()));
                    return true;

                // TryEnterSuperSky sub_6485B4:sub_6DF1B4(npc, edx=0x0C, ecx=0x0D) == 0x378(888)
                // 才 sub_6D3694(npc, dx=0x7D, ecx=0x2738(10040), 0, 0, 0x186A0(100000))。
                case "tryentersupersky":
                    return RejectUnsupportedNativeApi(out result);

                // RegDelayProc sub_648D40:三字段闩锁 —— [npc+0x5DC]=ecx(延时/参数),
                // [npc+0x5D8]=edx(过程名字符串),[npc+0x5E0]=sub_408340()(GetTickCount)。
                // 触发在别处;C# 无该延时回调调度器。
                case "regdelayproc":
                    return RejectUnsupportedNativeApi(out result);

                // DoShowNpcEx sub_64F51C:活动描述符字典 [0x7D660C] 查名(sub_696228),
                // 命中则调该描述符 vtable +0x24。
                case "doshownpcex":
                    return RejectUnsupportedNativeApi(out result);

                default:
                    return false;
            }
        }

        public bool CallNpcFunc(string method, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            if (CurrentNpc == null) return false;

            switch (method.ToLowerInvariant())
            {
                // ===== 原生注册在 TPsNpc 上的一批，此前只挂在 Player/Standalone 表里 =====
                // 解释器不跨接收者回落（PasInterpreter.TryInvokeNpcMethod 只试
                // CallNpcFunc/CallNpcMethod），所以按原生写法 This_Npc.Xxx 调用会抛
                // 「函数找不到」并中断整个标签。注册站点见
                // docs/m_npcscript_native_registry_20260813.txt。

                case "changegpswitch":              // TPsNpc 0x734ADC
                    // 原生声明是 `function ChangeGPSwitch(Player: TPlayer): Integer` —— 唯一
                    // 实参是玩家对象、返回 Integer。原先挂在 CallPlayerMethod 上的那份把
                    // args[0] 当整数写进 V(25,11)，与声明矛盾（既不吃玩家对象也不返回值），
                    // 所以不搬那份实现，按 fail-closed 处理。真正的公会点开关状态在哪一个
                    // 原生字段上未反出来，属 B4。
                    return RejectUnsupportedNativeApi(out result);

                case "getaroundmonnum":             // TPsNpc 0x7349EC（TPlayer 0x72B7AD 也注册）
                    // TPsNpc 版声明 `GetAroundMonNum(const sMonName: string): Integer`，
                    // TPlayer 版 `GetAroundMonNum(const sMonName: string; x,y,Rang: Integer)`
                    // —— 两个重载的第一个实参都是**怪物名字符串**。CallPlayerMethod 里那份把
                    // args[0] 当范围整数读，且只打日志不返回计数，与两个声明都对不上，所以
                    // 不转发。按 fail-closed 处理，避免把错误的实参解读扩散到第二个接收者。
                    return RejectUnsupportedNativeApi(out result);

                case "getcurrenteaperiod":          // TPsNpc 0x734DF4
                    if (args.Count >= 2)
                        result = PasValue.FromInt(GetPlayerVarOrZero('V', 20, (args[0].AsInt() * 10 + args[1].AsInt()) % 50));
                    else result = PasValue.FromInt(0);
                    return true;

                case "getcurrenteaidxbyname":       // TPsNpc 0x734E00
                case "getcurrenteanamebyidx":       // TPsNpc 0x734E0C（global 0x7299F6 也注册）
                case "getcurrenteascorebyidx":      // TPsNpc 0x734E18
                case "getlasteaidxbyname":          // TPsNpc 0x734E24
                case "getlasteanamebyidx":          // TPsNpc 0x734E30
                case "getlasteascorebyidx":         // TPsNpc 0x734E3C
                case "geteaorderinfo":              // TPsNpc 0x734E48（global 0x729A18 也注册）
                case "updateeverydayactorder":      // TPsNpc 0x734DE8（TPlayer/global 也注册）
                    // Native everyday-activity ranking records and pagination are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "eaorderisstart":              // TPsNpc 0x734E54（global 0x729A3A 也注册）
                    result = PasValue.FromBool(args.Count >= 2
                        && GetPlayerVarOrZero('V', 20, 49) != 0);
                    return true;

                case "getguildwargold":             // TPsNpc 0x734D34
                    // 原生不是变量，是个常量桩：注册运 0x739043 把 handler 0x6468C0 与名字串
                    // 0x73A200 "GetGuildWarGold" 配对，而 0x6468C0 全函数只有六个字节
                    // B8 30 75 00 00 C3 —— mov eax,0x7530 / ret，即恒返回 30000。
                    result = PasValue.FromInt(30000);
                    return true;

                case "useguildpoint":               // TPsNpc 0x734AC4
                case "getsomeguildpoint":           // TPsNpc 0x734AD0
                case "setwinetreat":                // TPsNpc 0x734E6C
                case "gettreatwine":                // TPsNpc 0x734E78
                    // These native TPsNpc methods own dedicated player/subsystem state.
                    return RejectUnsupportedNativeApi(out result);

                case "herorename":                  // TPsNpc 0x734E90
                {
                    var renameOwner = args.Count >= 1 && args[0].ObjVal is TPlayObject renamePlayer
                        ? renamePlayer
                        : CurrentPlayer;
                    result = PasValue.FromInt(args.Count >= 3
                        && HeroDataService.RequestRename(renameOwner,
                            args[1].AsString(), args[2].AsString(), CurrentNpc)
                        ? 0 : 1);
                    return true;
                }

                case "moveallhuminmap":             // TPsNpc 0x734ECC
                    // MoveAllHumInMap(desMap, x, y): move all players on current map to dest
                    if (args.Count >= 3 && CurrentNpc.m_PEnvir != null)
                    {
                        var moveAllMap = args[0].AsString();
                        var moveAllX = (short)args[1].AsInt();
                        var moveAllY = (short)args[2].AsInt();
                        var moveAllList = new List<TBaseObject>();
                        M2Share.UserEngine.GetMapRageHuman(CurrentNpc.m_PEnvir, 0, 0, 1000, moveAllList);
                        foreach (var moveAllObj in moveAllList)
                        {
                            if (moveAllObj is TPlayObject moveAllPlayer)
                                moveAllPlayer.SpaceMove(moveAllMap, moveAllX, moveAllY, 0);
                        }
                    }
                    return true;

                case "newfullmailex":               // TPsNpc 0x735070（8 参：带收件人名）
                    if (args.Count != 8) return false;
                    _ = new global::GameSvr.Services.MailService().NewFullMailEx(
                        args[0].AsString(), args[1].AsString(), args[2].AsString(),
                        args[3].AsInt(), args[4].AsInt(), args[5].AsInt(),
                        args[6].AsString(), args[7].AsString());
                    return true;

                case "submitballquest":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject ballQuestPlayer)
                        return false;
                    result = PasValue.FromInt(
                        ballQuestPlayer.SubmitNativeBallQuest());
                    return true;

                case "maxstrengthenequiplv":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject strengthenPlayer
                        || !TryGetNativeMaxStrengthenEquipLevel(
                            strengthenPlayer, out var strengthenLevel))
                        return false;
                    result = PasValue.FromInt(strengthenLevel);
                    return true;

                case "checkmarry":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject marriedPlayer)
                        return false;
                    var marryState = -1;
                    if (marriedPlayer.m_boMarried
                        && !string.IsNullOrEmpty(marriedPlayer.m_sDearName))
                    {
                        var spouse = M2Share.UserEngine?.GetPlayObject(
                            marriedPlayer.m_sDearName);
                        if (spouse != null
                            && marriedPlayer.m_btGender != spouse.m_btGender
                            && spouse.m_boMarried)
                        {
                            marryState = ReferenceEquals(
                                marriedPlayer.m_PEnvir, spouse.m_PEnvir)
                                ? 1 : -2;
                        }
                        else
                        {
                            marryState = -3;
                        }
                    }
                    result = PasValue.FromInt(marryState);
                    return true;

                case "givepositivevvalue":
                    // Native ABI is an NPC function returning Integer. The
                    // persistent vitality account is not mapped yet.
                    return RejectUnsupportedNativeApi(out result);

                case "storageallbagitems":
                    if (args.Count != 1
                        || args[0].Type != PasValueType.Object
                        || args[0].ObjVal is not TPlayObject storagePlayer
                        || CurrentNpc is not Merchant storageMerchant)
                        return false;
                    result = PasValue.FromString(
                        storageMerchant.StorageAllBagItems(storagePlayer));
                    return true;

                case "clickupweaponnow":
                    result = PasValue.FromInt(CurrentNpc is Merchant upMerchant
                        ? upMerchant.ClickUpWeaponNow(CurrentPlayer)
                        : 0);
                    return true;

                case "clickupweaponnobreak":
                    result = PasValue.FromInt(CurrentNpc is Merchant noBreakMerchant
                        ? noBreakMerchant.ClickUpWeaponNoBreak(CurrentPlayer)
                        : 0);
                    return true;

                case "clickgetbackupweapon":
                    result = PasValue.FromInt(CurrentNpc is Merchant getBackMerchant
                        ? getBackMerchant.ClickGetBackUpWeapon(CurrentPlayer)
                        : 0);
                    return true;

                // === Dialogue ===
                case "npcdialog":
                    if (args.Count >= 2 && CurrentPlayer != null && CurrentNpc != null)
                    {
                        var dlg = args[1].AsString();
                        var body = HUtil32.GetBytes(CurrentNpc.m_sCharName + "/" + dlg);
                        var defMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_MERCHANTSAY, CurrentNpc.ObjectId, 0, 0, 1);
                        CurrentPlayer.SendSocket(defMsg, body);
                    }
                    return true;

                case "closedialog":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTDLGCLOSE, 0, CurrentNpc.ObjectId, 0, 0, "");
                    return true;

                case "npcsay":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                // === Monster ===
                case "createmon":
                    if (args.Count >= 6)
                    {
                        var mapName = args[0].AsString();
                        var environment = string.IsNullOrEmpty(mapName)
                            ? CurrentNpc.m_PEnvir
                            : M2Share.MapManager.FindMap(mapName);
                        var x = (short)args[1].AsInt(); var y = (short)args[2].AsInt();
                        if (x <= 0) x = CurrentNpc.m_nCurrX; if (y <= 0) y = CurrentNpc.m_nCurrY;
                        var range = args[3].AsInt();
                        var monName = args[4].AsString();
                        var num = Math.Min(args[5].AsInt(), 200);
                        for (int i = 0; i < num; i++)
                        {
                            var sx = x; var sy = y;
                            if (range > 0)
                            {
                                sx = (short)(x - range + M2Share.RandomNumber.Random(range * 2 + 1));
                                sy = (short)(y - range + M2Share.RandomNumber.Random(range * 2 + 1));
                            }
                            M2Share.UserEngine.RegenMonsterByName(environment, sx, sy,
                                monName);
                        }
                    }
                                        return true;

                case "checkmapmonbyname":
                    if (args.Count >= 2)
                    {
                        if (TryNpcCreatMonsTunnel(args[0].AsString(), args[1].AsString(), out var spawned))
                        {
                            result = PasValue.FromInt(spawned);
                            return true;
                        }
                        var map = M2Share.MapManager.FindMap(args[0].AsString());
                        if (map != null)
                        {
                            var monName = args[1].AsString();
                            int count = 0;
                            var monsterList = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapMonster(map, monsterList);
                            for (int i = 0; i < monsterList.Count; i++)
                            {
                                var mon = monsterList[i];
                                if (mon != null && mon.m_sCharName != null &&
                                    mon.m_sCharName.IndexOf(monName, StringComparison.OrdinalIgnoreCase) >= 0)
                                    count++;
                            }
                            result = PasValue.FromInt(count);
                        }
                    }
                    return true;

                case "checkcurrmapmon":
                    if (CurrentNpc.m_PEnvir != null)
                    {
                        var monsterList = new List<TBaseObject>();
                        M2Share.UserEngine.GetMapMonster(CurrentNpc.m_PEnvir, monsterList);
                        result = PasValue.FromInt(monsterList.Count);
                    }
                    return true;

                case "checkcurrmaphum":
                    if (CurrentNpc.m_PEnvir != null)
                        result = PasValue.FromInt(CurrentNpc.m_PEnvir.HumCount);
                    return true;

                case "checkcastlewardate":
                    result = PasValue.FromString("");
                    return true;

                // === Merchants ===
                case "click_sell":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    result = PasValue.FromBool(true); return true;
                case "click_buy":
                    if (CurrentPlayer != null)
                    {
                        if (CurrentNpc is Merchant merchant)
                            merchant.UserSelect_BuyItem(CurrentPlayer, 0);
                        else
                            CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDGOODSLIST, 0, CurrentNpc.ObjectId, 0, 0, "");
                    }
                    result = PasValue.FromBool(true); return true;
                case "click_getback":
                    if (CurrentPlayer != null)
                    {
                        CurrentPlayer.m_nStoragePage = 0;
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_USERGETBACKITEM, 0, CurrentNpc.ObjectId, 0, 0, "");
                    }
                    result = PasValue.FromBool(true); return true;
                case "click_goldchgbar":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    result = PasValue.FromBool(true); return true;
                case "click_bartogold":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    result = PasValue.FromBool(true); return true;
                case "click_bricktobar":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    result = PasValue.FromBool(true); return true;
                case "click_bartobrick":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_SENDUSERSELL, 0, CurrentNpc.ObjectId, 0, 0, "");
                    result = PasValue.FromBool(true); return true;
                case "click_makedrug":
                    if (CurrentPlayer != null)
                        CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_USERMAKEDRUGITEMLIST, 0, CurrentNpc.ObjectId, 0, 0, "");
                    result = PasValue.FromBool(true); return true;
                case "inputdialog":
                    // InputDialog(Hum, MsgStr, DlgType, InputType) — show input dialog to player
                    if (args.Count >= 3 && CurrentPlayer != null && CurrentNpc != null)
                    {
                        var msg = args[1].AsString();
                        var dlgType = args[2].AsInt();
                        var inputType = args.Count >= 4 ? args[3].AsInt() : 0;
                        CurrentPlayer.SendDefMessage(Grobal2.SM_MERCHANT_QUERY, CurrentNpc.ObjectId, inputType, dlgType, 0, msg);
                    }
                                        return true;
                case "createdynroommon":
                    return RejectUnsupportedNativeApi(out result);
                case "diapeif":
                    // Native DiaPeif is a procedure and is not exposed as a function.
                    return RejectUnsupportedNativeApi(out result);
                case "getcelebname":
                    result = PasValue.FromString(
                        NativeCelebrityStatueManager.GetCelebrityName(CurrentNpc));
                    return true;
                // GetCelebLv sub_64FF5C —— 见 CallNpcMethod 中同名 case 的取证注释。
                case "getceleblv":
                    result = PasValue.FromInt(
                        NativeCelebrityStatueManager.GetCelebrityLevel(CurrentNpc));
                    return true;
                case "giveconfigprize":
                    // Native prize-table lookup and atomic reward transaction are absent.
                    return RejectUnsupportedNativeApi(out result);
                case "reqbecomeceleb":
                    result = PasValue.FromInt(
                        NativeCelebrityStatueManager.TryBecomeCelebrity(
                            CurrentNpc,
                            args.Count >= 1 && args[0].ObjVal is TPlayObject applicant
                                ? applicant
                                : CurrentPlayer));
                    return true;
                case "chkstrinfile":
                    if (args.Count >= 2)
                    {
                        if (CallStandaloneFunction("chkstrinfile", args, out var csfResult))
                        { result = csfResult; return true; }
                    }
                    result = PasValue.FromBool(false); return true;
                case "addstrtofile":
                    if (args.Count >= 2)
                    {
                        try
                        {
                            var fileName = args[0].AsString();
                            var content = args[1].AsString();
                            var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                            var filePath = Path.Combine(envirDir, fileName);
                            var dir = Path.GetDirectoryName(filePath);
                            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                            File.AppendAllText(filePath, content + Environment.NewLine, Encoding.GetEncoding("GBK"));
                                                    }
                        catch { result = PasValue.FromBool(false); }
                    }
                    else return true;
                    return true;
                case "delstrfromfile":
                    if (args.Count >= 2)
                    {
                        try
                        {
                            var fileName = args[0].AsString();
                            var content = args[1].AsString();
                            var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                            var filePath = Path.Combine(envirDir, fileName);
                            if (File.Exists(filePath))
                            {
                                var lines = File.ReadAllLines(filePath, Encoding.GetEncoding("GBK")).ToList();
                                lines.RemoveAll(l => l.Trim() == content.Trim());
                                File.WriteAllLines(filePath, lines, Encoding.GetEncoding("GBK"));
                            }
                                                    }
                        catch { result = PasValue.FromBool(false); }
                    }
                    else return true;
                    return true;

                // === NPC INTERACTION / CLICK UI (return value) ===
                case "click_open_mosaic_hole":
                    if (CurrentPlayer != null) { CurrentPlayer.SendNativeScriptUiOpen(TPlayObject.SM_CLICK_OPEN_MOSAIC_HOLE, CurrentNpc.ObjectId); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_open_duihuan_contri":
                    if (CurrentPlayer != null) { CurrentPlayer.SendNativeScriptUiOpen(TPlayObject.SM_CLICK_OPEN_DUIHUAN_CONTRI, CurrentNpc.ObjectId); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_open_myoffirankui":
                    if (CurrentPlayer != null) { CurrentPlayer.SendNativeScriptUiOpen(TPlayObject.SM_CLICK_OPEN_MYOFFIRANKUI, CurrentNpc.ObjectId); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_open_attachabilui":
                    if (CurrentPlayer != null) { CurrentPlayer.SendNativeScriptUiOpen(TPlayObject.SM_CLICK_OPEN_ATTACHABILUI, CurrentNpc.ObjectId); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_open_mirtiantiorder":
                    if (CurrentPlayer != null) { CurrentPlayer.SendNativeScriptUiOpen(TPlayObject.SM_CLICK_OPEN_MIRTIANTIORDER, CurrentNpc.ObjectId); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_storage":
                    if (CurrentPlayer != null) { CurrentPlayer.m_nStoragePage = 0; CurrentPlayer.SendSaveItemList(CurrentNpc.ObjectId); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_acc_storage":
                    QueueNativeAccountStorageClick(false);
                    result = PasValue.FromBool(true);
                    return true;

                case "click_acc_getback":
                    QueueNativeAccountStorageClick(true);
                    result = PasValue.FromBool(true);
                    return true;

                case "click_eqp_operate":
                    if (CurrentPlayer != null) { CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, args.Count >= 1 ? args[0].AsInt() : 0, 0, $"{CurrentNpc.m_sCharName}/@eqpoperate"); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "click_vote":
                    if (CurrentPlayer != null) { CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, args.Count >= 1 ? args[0].AsInt() : 0, 0, $"{CurrentNpc.m_sCharName}/@vote"); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "clickupgradejewels":
                    if (CurrentPlayer != null) { CurrentPlayer.SendMsg(CurrentNpc, Grobal2.RM_MERCHANTSAY, 0, CurrentNpc.ObjectId, 0, 0, $"{CurrentNpc.m_sCharName}/@upgradejewels/{(args.Count >= 1 ? args[0].AsInt() : 0)}"); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "clientaskybduanzao":
                    return RejectUnsupportedNativeApi(out result);

                case "clientquestgetdiam":
                    return RejectUnsupportedNativeApi(out result);

                case "refreshcredit":
                    return RejectUnsupportedNativeApi(out result);

                case "clientreqgetbacklostitem":
                    // Native lost-item database transaction is absent.
                    return RejectUnsupportedNativeApi(out result);

                case "reqitembygoldact":
                    // Native export is a procedure, never a Boolean-returning function.
                    return RejectUnsupportedNativeApi(out result);

                case "reqitembygoldid":
                    return RejectUnsupportedNativeApi(out result);

                case "reqitembyplatina":
                case "clientquerylastdealmsg":
                    if (args.Count >= 1 &&
                        args[0].ObjVal is TPlayObject dealLogPlayer)
                    {
                        dealLogPlayer.ShowNativeYbLastDealLog(CurrentNpc, 0xAF,
                            string.Empty, string.Empty);
                        result = PasValue.FromBool(true);
                        return true;
                    }
                    return false;
                case "clientqueryybdealitem":
                    // Native platinum/YB consignment transaction services are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "clientaskopenyb":
                    return RejectUnsupportedNativeApi(out result);

                case "reqgetfirstusedgift":
                    return RejectUnsupportedNativeApi(out result);

                case "clientybbuylf":
                    // Native export is a procedure, never a Boolean-returning function.
                    return RejectUnsupportedNativeApi(out result);

                case "buywinefromnpc":
                    return RejectUnsupportedNativeApi(out result);

                case "givewine":
                    if (args.Count >= 1 && CurrentNpc != null) { CurrentNpc.GotoLable_GiveItem(CurrentPlayer, args[0].AsString(), args.Count >= 2 ? args[1].AsInt() : 1); result = PasValue.FromBool(true); }
                    else                     return true;
                    return true;

                case "usenick":
                    return TryUseNativeNick(args, out result);

                case "clickcomposedress":
                    // Native callback is nullsub_60.
                    return true;

                case "foundrylist":
                    // Native FoundryList is a procedure and is not exposed as a function.
                    return RejectUnsupportedNativeApi(out result);

                case "makeitemusediam":
                    // Native MakeItemUseDiam is a procedure and is not exposed as a function
                    // (mirrors foundrylist above). The live forge dispatches through CallNpcMethod.
                    return RejectUnsupportedNativeApi(out result);

                case "openneedkeybox":
                case "openneedkeybox2":
                    return RejectUnsupportedNativeApi(out result);

                case "openluckbox":
                case "openluckbox2":
                    // Native luck-box reward state and atomic delivery are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "rndgetmedal":
                    return RejectUnsupportedNativeApi(out result);

                case "spegetmedalbyry":
                    return RejectUnsupportedNativeApi(out result);

                case "spegetmedalbysw":
                    return RejectUnsupportedNativeApi(out result);

                case "upmedalabil":
                    if (CurrentPlayer == null) return true;
                    {
                        var materials = new[] { "矿石", "紫宝石矿", "勋章之心", "绿宝石矿", "金水" };
                        foreach (var mat in materials)
                        {
                            if (CurrentPlayer.CheckItems(mat) == null) { CurrentPlayer.SysMsg("缺少配方原料", MsgColor.Red, MsgType.Hint); return true; }
                        }
                        var medal = CurrentPlayer.m_UseItems[Grobal2.U_BUJUK];
                        if (medal == null || medal.wIndex <= 0) { CurrentPlayer.SysMsg("请先佩戴勋章", MsgColor.Red, MsgType.Hint); return true; }
                        if (medal.DuraMax <= 5) { CurrentPlayer.SysMsg("勋章持久过低，无法升级", MsgColor.Red, MsgType.Hint); return true; }
                        foreach (var mat in materials) TakeItems(mat, 1);
                        if (M2Share.RandomNumber.Random(100) < 50)
                        {
                            medal.DuraMax = (ushort)Math.Max(1, medal.DuraMax - 5);
                            if (medal.Dura > medal.DuraMax) medal.Dura = medal.DuraMax;
                            if (medal.btValue != null && medal.btValue.Length > 4)
                            {
                                medal.btValue[0] = (byte)Math.Min(255, medal.btValue[0] + 1);
                                medal.btValue[1] = (byte)Math.Min(255, medal.btValue[1] + 1);
                                medal.btValue[2] = (byte)Math.Min(255, medal.btValue[2] + 1);
                            }
                            CurrentPlayer.SendUpdateItem(medal);
                            CurrentPlayer.SysMsg("勋章升级成功！", MsgColor.Green, MsgType.Hint);
                        }
                        else
                        {
                            var brokenMedal = CurrentPlayer.m_UseItems[Grobal2.U_BUJUK];
                            CurrentPlayer.m_UseItems[Grobal2.U_BUJUK] = null;
                            CurrentPlayer.SendDelItems(brokenMedal);
                            CurrentPlayer.SysMsg("勋章升级失败，勋章碎裂", MsgColor.Red, MsgType.Hint);
                        }
                    }
                    return true;

                // === ITEM / GIVE (NPC-side, return value) ===
                // GiveItemsToOther 原生只注册在 TPlayer 0x72B7E9 上，已搬到 CallPlayerFunc。

                case "giveconfigprizetemp":
                    // Native temporary-transfer prize transaction is absent.
                    return RejectUnsupportedNativeApi(out result);

                // === Variable System Pass-Throughs (delegate to standalone) ===
                case "getv":
                    result = args.Count >= 2 ? GetPlayerVar('V', args[0].AsInt(), args[1].AsInt()) : PasValue.FromInt(NativeScriptVarMiss);
                    return true;

                case "gets":
                    result = args.Count >= 2 ? GetPlayerVar('S', args[0].AsInt(), args[1].AsInt()) : PasValue.FromInt(NativeScriptVarMiss);
                    return true;

                case "getplayerhonorvalue":
                    result = PasValue.FromInt(M2Share.HonorValueManager?.Get(
                        NativeCelebrityStatueManager.GetCelebrityName(CurrentNpc)) ?? -1);
                    return true;

                case "getmovechance":
                    if (args.Count != 2
                        || args[0].ObjVal is not TPlayObject movePlayer
                        || args[1].Type != PasValueType.Integer)
                        return false;
                    result = PasValue.FromBool(movePlayer
                        .GetNativeMagicTowerMoveChance(CurrentNpc,
                            args[1].AsInt()));
                    return true;

                case "getengagechance":
                    if (args.Count != 1
                        || args[0].ObjVal is not TPlayObject engageChancePlayer)
                        return false;
                    result = PasValue.FromBool(engageChancePlayer
                        .GetNativeMagicTowerEngageChance(CurrentNpc));
                    return true;

                case "isexistarcher":
                    if (args.Count != 2
                        || args[0].ObjVal is not TPlayObject archerPlayer)
                        return false;
                    result = PasValue.FromBool(CurrentNpc
                        .HasNativeMagicTowerArcher(archerPlayer,
                            args[1].AsInt()));
                    return true;

                case "reqcastleownernpc":
                    if (args.Count != 1
                        || args[0].ObjVal is not TPlayObject castleOwner)
                        return false;
                    result = PasValue.FromInt(
                        NativeCelebrityStatueManager.TrySetCastleOwner(castleOwner));
                    return true;

                case "getaidledynroomindex":
                    if (args.Count != 1
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromInt(M2Share.DynamicRoomService
                        .TryReserveActivatedRoom(args[0].AsString(), null,
                            out var idleRoomIndex)
                        ? idleRoomIndex
                        : -1);
                    return true;

                case "getaidledynroomindexex":
                    if (args.Count != 2
                        || args[1].ObjVal is not TPlayObject roomOwner
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromInt(M2Share.DynamicRoomService
                        .TryReserveActivatedRoom(args[0].AsString(), roomOwner,
                            out var ownedRoomIndex)
                        ? ownedRoomIndex
                        : -1);
                    return true;

                default:
                    return false;
            }
        }

        // =====================================================================
        // STANDALONE FUNCTIONS (ServerSay, GetG, SetG, etc.)
        // =====================================================================

        public bool CallStandaloneFunction(string name, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            switch (name.ToLowerInvariant())
            {
                // ===== 原生用 AddFunction (0x513A7C) 注册为全局函数的一批，此前只挂在
                // Player/Npc 表里。PasInterpreter:778 先试 CallStandaloneFunction，
                // 裸调用 Xxx(...) 走的就是这张表。

                case "getscorebyname":              // global 0x729A29
                    // Native everyday-activity ranking records are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "eaorderisstart":              // global 0x729A3A（TPsNpc 0x734E54 也注册）
                    result = PasValue.FromBool(args.Count >= 2
                        && GetPlayerVarOrZero('V', 20, 49) != 0);
                    return true;

                case "getcurrenteanamebyidx":       // global 0x7299F6（TPsNpc 0x734E0C 也注册）
                case "geteaorderinfo":              // global 0x729A18（TPsNpc 0x734E48 也注册）
                    // Native everyday-activity ranking records and pagination are absent.
                    return RejectUnsupportedNativeApi(out result);

                case "kickallhumtomap":             // global 0x7299E5
                    // KickAllHumToMap(srcMap, desMap, x, y)
                    if (args.Count >= 4)
                    {
                        var kickSrcMap = M2Share.MapManager.FindMap(args[0].AsString());
                        if (kickSrcMap != null)
                        {
                            var kickMap = args[1].AsString();
                            var kickX = (short)args[2].AsInt();
                            var kickY = (short)args[3].AsInt();
                            var kickList = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapRageHuman(kickSrcMap, 0, 0, 1000, kickList);
                            foreach (var kickObj in kickList)
                            {
                                if (kickObj is TPlayObject kickPlayer)
                                    kickPlayer.SpaceMove(kickMap, kickX, kickY, 0);
                            }
                        }
                    }
                    return true;

                case "playercry":                   // global 0x729A4B
                    // PlayerCry(chrName, iType, strMsg)
                    if (args.Count >= 1)
                    {
                        var cryName = args[0].AsString();
                        var cryType = args.Count >= 2 ? args[1].AsInt() : 0;
                        var cryMsg = args.Count >= 3 ? args[2].AsString() : "";
                        var cryTarget = M2Share.UserEngine.GetPlayObject(cryName);
                        if (cryTarget != null)
                        {
                            cryTarget.SysMsg(cryMsg, MsgColor.Green, MsgType.Hint);
                            cryTarget.SendRefMsg(Grobal2.RM_CRY, cryType, 0, 0, 0, cryMsg);
                        }
                    }
                    return true;

                case "playergive":                  // global 0x729A5C
                    // PlayerGive(chrName, ItemName, ItemCount)
                    if (args.Count >= 3 && CurrentNpc != null)
                    {
                        var giveTarget = M2Share.UserEngine.GetPlayObject(args[0].AsString());
                        if (giveTarget != null)
                        {
                            var giveName = args[1].AsString();
                            var giveCount = args[2].AsInt();
                            for (var giveIdx = 0; giveIdx < giveCount; giveIdx++)
                                CurrentNpc.GotoLable_GiveItem(giveTarget, giveName, 1);
                        }
                    }
                    return true;

                case "getdynroomhumnum":
                    if (args.Count != 2
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromInt(M2Share.DynamicRoomService
                        .GetDynamicRoomPlayerCount(args[0].AsString(),
                            args[1].AsInt()));
                    return true;

                case "getdynroomcnt":
                    if (args.Count != 1
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromInt(M2Share.DynamicRoomService
                        .GetDynamicRoomCount(args[0].AsString()));
                    return true;

                case "pshavefreedynroom":
                    if (args.Count != 1
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromBool(M2Share.DynamicRoomService
                        .HasFreeDynamicRoom(args[0].AsString()));
                    return true;

                case "psisdynroomvalid":
                    if (args.Count != 2
                        || M2Share.DynamicRoomService?.IsInitialized != true)
                        return false;
                    result = PasValue.FromBool(M2Share.DynamicRoomService
                        .IsDynamicRoomValid(args[0].AsString(),
                            args[1].AsInt()));
                    return true;

                // UseGuildPoint 0x734AC4 / GetSomeGuildPoint 0x734AD0 / SetWineTreat 0x734E6C /
                // GetTreatWine 0x734E78 / HeroRename 0x734E90 原生全部注册在 TPsNpc 上，
                // 不是全局函数，已搬到 CallNpcFunc。
                case "convertvexp":
                    // This native global owns dedicated player/subsystem state.
                    return RejectUnsupportedNativeApi(out result);

                case "delbagitemofall":
                    return CallPlayerMethod("delbagitemofall", args);

                case "setybshoprefreshtime":
                    result = PasValue.FromBool(args.Count >= 1 &&
                        global::GameSvr.Mall.MallManager.Instance.ConfigureRefreshTime(
                            args[0].AsString()));
                    return true;

                case "updateeverydayactorder":
                    // Native everyday-activity order manager is absent.
                    return RejectUnsupportedNativeApi(out result);

                case "chgmonitempercent":
                case "npc_creatmons":
                case "lmsetysid":
                case "lmcheckmapmon":
                case "lmgetitemid":
                case "lmgetysid":
                case "serveraay":
                    // External plugin entry points are absent from the native M2 registry.
                    return RejectUnsupportedNativeApi(out result);

                case "serversay":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.Notice);
                    return true;

                case "debugout":
                    if (args.Count >= 1)
                        M2Share.MainOutMessage(args[0].AsString());
                    return true;

                case "rollmsg":
                    if (args.Count >= 1)
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), MsgType.System);
                    return true;

                case "rollmsgex":
                    if (args.Count >= 1)
                    {
                        var messageType = args.Count >= 2 ? (MsgType)args[1].AsInt() : MsgType.System;
                        M2Share.UserEngine.SendBroadCastMsg(args[0].AsString(), messageType);
                    }
                    return true;

                case "sendybshopconfig":
                    result = PasValue.FromBool(args.Count >= 1 &&
                        global::GameSvr.Mall.MallManager.Instance.RefreshCache());
                    return true;

                case "getg":
                    result = args.Count >= 2
                        ? GetGlobalVar(args[0].AsInt(), args[1].AsInt())
                        : PasValue.FromInt(NativeGlobalVarMiss);
                    return true;

                case "setg":
                    if (args.Count >= 3) SetGlobalVar(args[0].AsInt(), args[1].AsInt(), args[2]);
                    result = PasValue.FromBool(args.Count >= 3);
                    return true;

                case "getv":
                    result = args.Count >= 2 ? GetPlayerVar('V', args[0].AsInt(), args[1].AsInt()) : PasValue.FromInt(NativeScriptVarMiss);
                    return true;

                case "setv":
                    if (args.Count >= 3) SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]);
                    result = PasValue.FromBool(args.Count >= 3);
                    return true;

                case "ys_cmptime_min":
                    result = PasValue.FromBool(false);
                    if (CurrentPlayer == null || args.Count < 3 ||
                        !TryExecuteTunnelCommand("!!!!hq取sj戳", "ys_CmpTime_min", out var currentMillisecondTick))
                        return true;
                    var previousMillisecondTick = GetPlayerVar('V',
                        args[0].AsInt(), args[1].AsInt()).AsInt();
                    var elapsed = Math.Abs((long)unchecked(
                        currentMillisecondTick - previousMillisecondTick));
                    result = PasValue.FromBool(previousMillisecondTick is 0 or -1 ||
                        elapsed > args[2].AsInt());
                    return true;

                case "ys_setcd_min":
                    result = PasValue.FromBool(false);
                    if (CurrentPlayer == null || args.Count < 2 ||
                        !TryExecuteTunnelCommand("!!!!hq取sj戳", "ys_SetCD_min", out var millisecondTick))
                        return true;
                    SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(),
                        PasValue.FromInt(millisecondTick));
                    result = PasValue.FromBool(true);
                    return true;

                // ── 2.08 秒级 CD 族（ys_SetCD/ys_CmpTime/ys_GetHowTime）──
                // 与上面的毫秒同胞相同机制：玩家 V 变量存/取时间戳；仅分辨率为秒。
                // v_x,v_y = GetV/SetV 的两个坐标参数；秒 = 时间戳隧道(毫秒)/1000。
                // 注：无状态的 api.CDCmpTime/CDGetDiff 不读 V 变量，语义不符，故不复用。
                case "ys_setcd": // procedure ys_SetCD(v_x,v_y) — 写入当前时间秒数
                    result = PasValue.FromBool(false);
                    if (CurrentPlayer == null || args.Count < 2 ||
                        !TryExecuteTunnelCommand("!!!!hq取sj戳", "ys_SetCD", out var secondSetTick))
                        return true;
                    SetPlayerVar('V', args[0].AsInt(), args[1].AsInt(),
                        PasValue.FromInt(secondSetTick / 1000));
                    result = PasValue.FromBool(true);
                    return true;

                case "ys_cmptime": // function ys_CmpTime(v_x,v_y,chazhi):Boolean — 已过 chazhi 秒?
                    result = PasValue.FromBool(false);
                    if (CurrentPlayer == null || args.Count < 3 ||
                        !TryExecuteTunnelCommand("!!!!hq取sj戳", "ys_CmpTime", out var secondCmpTick))
                        return true;
                    var storedSecond = GetPlayerVar('V',
                        args[0].AsInt(), args[1].AsInt()).AsInt();
                    var elapsedSecond = Math.Abs((long)unchecked(
                        (secondCmpTick / 1000) - storedSecond));
                    result = PasValue.FromBool(storedSecond is 0 or -1 ||
                        elapsedSecond > args[2].AsInt());
                    return true;

                case "ys_gethowtime": // function ys_GetHowTime(v_x,v_y,chazhi):Integer — CD 已过多少秒
                    result = PasValue.FromInt(0);
                    if (CurrentPlayer == null || args.Count < 2 ||
                        !TryExecuteTunnelCommand("!!!!hq取sj戳", "ys_GetHowTime", out var secondHowTick))
                        return true;
                    var storedHowSecond = GetPlayerVar('V',
                        args[0].AsInt(), args[1].AsInt()).AsInt();
                    result = PasValue.FromInt((int)Math.Abs((long)unchecked(
                        (secondHowTick / 1000) - storedHowSecond)));
                    return true;

                case "gets":
                    result = args.Count >= 2 ? GetPlayerVar('S', args[0].AsInt(), args[1].AsInt()) : PasValue.FromInt(NativeScriptVarMiss);
                    return true;

                case "sets":
                    if (args.Count >= 3) SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]);
                    result = PasValue.FromBool(args.Count >= 3);
                    return true;

                case "groupsetv":
                    result = PasValue.FromBool(args.Count >= 3 && CurrentPlayer != null
                        && SetGroupPlayerVar('V', args[0].AsInt(), args[1].AsInt(), args[2]));
                    return true;

                case "groupsets":
                    if (args.Count >= 3 && CurrentPlayer != null)
                        SetPlayerVar('S', args[0].AsInt(), args[1].AsInt(), args[2]);
                    result = PasValue.FromBool(args.Count >= 3 && CurrentPlayer != null);
                    return true;

                case "readinisectionstr":
                    result = args.Count >= 3
                        ? PasValue.FromString(ReadIniSectionStr(args[0].AsString(), args[1].AsString(), args[2].AsString()))
                        : PasValue.FromString("");
                    return true;

                case "writeinisectionstr":
                    if (args.Count >= 4) WriteIniSectionStr(args[0].AsString(), args[1].AsString(), args[2].AsString(), args[3].AsString());
                    return true;

                case "executescript":
                case "executequery":
                case "psfirst":
                case "psnext":
                case "psbof":
                case "pseof":
                case "psfieldname":
                case "psfieldbyname":
                case "psfieldbypos":
                case "psrecordcount":
                case "psfieldcount":
                    return CallDbMethod(name, args, out result);

                // === Utility ===
                case "comparetext":
                    result = args.Count >= 2
                        ? PasValue.FromInt(string.Compare(args[0].AsString(), args[1].AsString(), StringComparison.OrdinalIgnoreCase))
                        : PasValue.FromInt(0);
                    return true;

                case "sametext":
                    result = args.Count >= 2
                        ? PasValue.FromInt(string.Compare(args[0].AsString(), args[1].AsString(), StringComparison.OrdinalIgnoreCase) == 0 ? 1 : 0)
                        : PasValue.FromInt(0);
                    return true;

                case "strtointdef":
                    if (args.Count >= 2)
                    {
                        if (int.TryParse(args[0].AsString(), out var sVal))
                            result = PasValue.FromInt(sVal);
                        else
                            result = PasValue.FromInt(args[1].AsInt());
                    }
                    else result = PasValue.FromInt(0);
                    return true;

                case "obtainparambyindex":
                    if (args.Count >= 3)
                    {
                        var s = args[0].AsString() ?? "";
                        var delim = args[1].AsString();
                        if (!string.IsNullOrEmpty(delim))
                        {
                            var parts = s.Split(delim[0]);
                            int idx = args[2].AsInt();
                            result = idx >= 0 && idx < parts.Length ? PasValue.FromString(parts[idx]) : PasValue.FromString("");
                        }
                        else result = PasValue.FromString(s);
                    }
                    else result = PasValue.FromString("");
                    return true;

                case "chkstrinfile":
                    if (args.Count >= 2)
                    {
                        var fileName = args[0].AsString();
                        var searchStr = args[1].AsString();
                        try
                        {
                            var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                            var filePath = Path.Combine(envirDir, fileName);
                            if (File.Exists(filePath))
                            {
                                var content = File.ReadAllText(filePath, Encoding.GetEncoding("GBK"));
                                result = PasValue.FromBool(content.IndexOf(searchStr, StringComparison.OrdinalIgnoreCase) >= 0);
                                return true;
                            }
                        }
                        catch (Exception ex) { Debug.WriteLine("SearchFileContent failed: " + ex.Message); }
                    }
                                        return true;

                case "getopen gametime":
                case "getopengametime":
                    // Return server uptime in seconds since start
                    if (M2Share.g_dwStartTick > 0)
                        result = PasValue.FromInt((HUtil32.GetTickCount() - M2Share.g_dwStartTick) / 1000);
                    else
                        result = PasValue.FromInt(0);
                    return true;

                case "getnow":
                    result = PasValue.FromDouble(DateTime.Now.Ticks / (double)TimeSpan.TicksPerDay);
                    return true;

                case "getdate num":
                case "getdatenum":
                    result = PasValue.FromInt((int)(DateTime.Now.Ticks / TimeSpan.TicksPerDay));
                    return true;

                case "checkothermaphum":
                    if (args.Count >= 1)
                    {
                        var map = M2Share.MapManager.FindMap(args[0].AsString());
                        result = map != null ? PasValue.FromInt(map.HumCount) : PasValue.FromInt(0);
                    }
                    return true;

                case "checkothermapmon":
                    if (args.Count >= 1)
                    {
                        var map = M2Share.MapManager.FindMap(args[0].AsString());
                        if (map != null)
                        {
                            var monsterList = new List<TBaseObject>();
                            M2Share.UserEngine.GetMapMonster(map, monsterList);
                            result = PasValue.FromInt(monsterList.Count);
                        }
                        else
                        {
                            result = PasValue.FromInt(0);
                        }
                    }
                    return true;

                case "chkmonanditem":
                    // Native Magic Tower/challenge state is not a generic map scan.
                    return RejectUnsupportedNativeApi(out result);

                // NewFullMailEx 原生只有两处注册，都是类方法：TPlayer 0x72BBF8（7 参）与
                // TPsNpc 0x735070（8 参，多一个收件人名）。没有全局函数重载，8 参那份
                // 已搬到 CallNpcFunc，7 参那份留在 CallPlayerMethod。

                case "startsiege":
                case "startcastlewar":
                    if (CurrentPlayer != null)
                    {
                        var castle = M2Share.CastleManager.GetCastle(0);
                        if (castle != null)
                            Services.NativeReqCastleWar.TryApply(CurrentPlayer, castle);
                    }
                    return true;

                case "endsiege":
                case "endcastlewar":
                                        return true;

                // === Math ===
                case "pspower":
                    result = args.Count >= 2
                        ? PasValue.FromDouble(Math.Pow(args[0].AsDouble(), args[1].AsDouble()))
                        : PasValue.FromInt(0);
                    return true;

                case "psceil":
                    result = args.Count >= 1
                        ? PasValue.FromInt((int)Math.Ceiling(args[0].AsDouble()))
                        : PasValue.FromInt(0);
                    return true;

                // === Map ===
                case "getmapcanwalkxy":
                    // GetMapCanWalkXY(MapName, var X, var Y, BoOver, BoForce) — find a walkable tile on the map.
                    // Tries to find a nearby walkable position. Returns true if found.
                    if (args.Count >= 5)
                    {
                        var mapName = args[0].AsString();
                        var x = args[1].AsInt();
                        var y = args[2].AsInt();
                        var boOver = args[3].AsInt() != 0;
                        var boForce = args[4].AsInt() != 0;
                        var map = M2Share.MapManager.FindMap(mapName);
                        if (map != null)
                        {
                            // Try exact position first
                            if (map.CanWalk(x, y, boOver))
                            {
                                args[1] = PasValue.FromInt(x);
                                args[2] = PasValue.FromInt(y);
                                                            }
                            else
                            {
                                // Spiral search: expand radius up to 10
                                bool found = false;
                                for (int r = 1; r <= 10 && !found; r++)
                                {
                                    for (int dx = -r; dx <= r && !found; dx++)
                                    {
                                        for (int dy = -r; dy <= r && !found; dy++)
                                        {
                                            if (Math.Abs(dx) == r || Math.Abs(dy) == r)
                                            {
                                                int nx = x + dx, ny = y + dy;
                                                if (nx >= 0 && ny >= 0 && nx < map.wWidth && ny < map.wHeight
                                                    && map.CanWalk(nx, ny, boOver))
                                                {
                                                    args[1] = PasValue.FromInt(nx);
                                                    args[2] = PasValue.FromInt(ny);
                                                    found = true;
                                                }
                                            }
                                        }
                                    }
                                }
                                                            }
                        }
                        else { return true; } }
                    return true;

                // === Version ===
                case "g_version":
                    // Return Grobal2.VERSION_NUMBER as primary, fall back to g_sVersionDate
                    result = PasValue.FromString(Grobal2.VERSION_NUMBER.ToString());
                    if (string.IsNullOrEmpty(result.AsString()))
                        result = PasValue.FromString(M2Share.g_sVersionDate ?? "0");
                    M2Share.MainOutMessage($"[PasBridge] G_Version = {result.AsString()}");
                    return true;

                // === Misc ===
                case "makecattlecrazy":
                    NativeFireKingEventState.ForceLocally();
                    M2Share.UserEngine?.SendServerGroupMsg(
                        Grobal2.ISM_MAKE_CATTLE_CRAZY, 3, string.Empty);
                    result = PasValue.FromBool(true);
                    return true;

                case "getadvplayernum":
                    // Count advanced players (level >= 35, rounded by job)
                    {
                        int advCount = 0;
                        var allPlayers = M2Share.UserEngine.GetPlayerList();
                        foreach (var pl in allPlayers)
                        {
                            if (pl != null && pl.m_Abil.Level >= 35)
                                advCount++;
                        }
                        result = PasValue.FromInt(advCount);
                    }
                    return true;

                // === Task Dispatch ===
                case "inittaskdispatchinfo":
                    // InitTaskDispatchInfo(cmpCost, acceptCnt, dispatchCnt, bronzeCost, silverCost, goldCost)
                    // Store dispatch config in global V vars for later query
                    if (args.Count >= 6)
                    {
                        SetGlobalVar(100, 1, PasValue.FromInt(args[0].AsInt())); // cmpCost
                        SetGlobalVar(100, 2, PasValue.FromInt(args[1].AsInt())); // acceptCnt
                        SetGlobalVar(100, 3, PasValue.FromInt(args[2].AsInt())); // dispatchCnt
                        SetGlobalVar(100, 4, PasValue.FromInt(args[3].AsInt())); // bronzeCost
                        SetGlobalVar(100, 5, PasValue.FromInt(args[4].AsInt())); // silverCost
                        SetGlobalVar(100, 6, PasValue.FromInt(args[5].AsInt())); // goldCost
                    }
                    return true;

                // QueryTaskDispatch 原生只注册在 TPlayer 0x72B9B8 上，已搬到 CallPlayerFunc。

                // === Date/Time utilities ===
                case "psdecodedate":
                    // PsDecodeDate(dt: Double, var Year, var Month, var Day)
                    if (args.Count >= 4)
                    {
                        var dt = args[0].AsDouble();
                        var dateTime = DateTime.MinValue.AddDays(dt - 1); // Delphi TDateTime epoch is 1899-12-30
                        // Also handle negative/small values
                        if (dt > 0)
                        {
                            try { dateTime = DateTime.FromOADate(dt); }
                            catch { dateTime = DateTime.MinValue; }
                        }
                        args[1] = PasValue.FromInt(dateTime.Year);
                        args[2] = PasValue.FromInt(dateTime.Month);
                        args[3] = PasValue.FromInt(dateTime.Day);
                    }
                    return true;

                case "psdecodetime":
                    // PsDecodeTime(dt: Double, var Hour, var Min, var Sec, var MSec)
                    if (args.Count >= 5)
                    {
                        var dt = args[0].AsDouble();
                        DateTime dateTime = DateTime.MinValue;
                        try { dateTime = DateTime.FromOADate(dt); }
                        catch { dateTime = DateTime.MinValue; }
                        args[1] = PasValue.FromInt(dateTime.Hour);
                        args[2] = PasValue.FromInt(dateTime.Minute);
                        args[3] = PasValue.FromInt(dateTime.Second);
                        args[4] = PasValue.FromInt(dateTime.Millisecond);
                    }
                    return true;

                case "minusdatatime":
                case "minusdatetime":
                    // Native sub_728F0C multiplies the Delphi Double delta by 86400 and truncates.
                    if (args.Count >= 2)
                    {
                        var seconds = (args[0].AsDouble() - args[1].AsDouble()) * 86400.0;
                        result = PasValue.FromInt(unchecked((int)seconds));
                    }
                    return true;

                case "secondsbetween":
                    if (args.Count >= 2)
                    {
                        var seconds = Math.Abs(args[0].AsDouble() - args[1].AsDouble()) * 86400.0;
                        var nativeValue = unchecked((int)seconds);
                        result = PasValue.FromInt(nativeValue < 0 ? int.MaxValue : nativeValue);
                    }
                    return true;

                case "adddatetimewithsec":
                    // AddDateTimeWithSec(dtNow, ASec) — add seconds to datetime
                    if (args.Count >= 2)
                    {
                        DateTime dtNow = DateTime.MinValue;
                        try { dtNow = DateTime.FromOADate(args[0].AsDouble()); } catch { }
                        dtNow = dtNow.AddSeconds(args[1].AsInt());
                        try { result = PasValue.FromDouble(dtNow.ToOADate()); }
                        catch { result = PasValue.FromDouble(0); }
                    }
                    return true;

                case "convertdatetimetodb":
                    // ConvertDateTimeToDB(dt) — convert Delphi TDateTime to DB integer (seconds since epoch)
                    if (args.Count >= 1)
                    {
                        DateTime dt = DateTime.MinValue;
                        try { dt = DateTime.FromOADate(args[0].AsDouble()); } catch { }
                        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        result = PasValue.FromInt((int)(dt.ToUniversalTime() - epoch).TotalSeconds);
                    }
                    return true;

                case "convertdbtodatetime":
                    // ConvertDBToDateTime(dt) — convert DB integer (Unix timestamp) to Delphi TDateTime
                    if (args.Count >= 1)
                    {
                        var ts = args[0].AsInt();
                        var epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                        var dt = epoch.AddSeconds(ts).ToLocalTime();
                        try { result = PasValue.FromDouble(dt.ToOADate()); }
                        catch { result = PasValue.FromDouble(0); }
                    }
                    return true;

                case "mirstrtodatetime":
                    // MirStrToDateTime(str) — convert string "YYYY-MM-DD HH:mm:ss" to Delphi TDateTime
                    if (args.Count >= 1)
                    {
                        var str = args[0].AsString();
                        if (DateTime.TryParse(str, out var dt))
                        {
                            try { result = PasValue.FromDouble(dt.ToOADate()); }
                            catch { result = PasValue.FromDouble(0); }
                        }
                        else result = PasValue.FromDouble(0);
                    }
                    return true;

                case "mirdatetimetostr":
                    // MirDateTimeToStr(formatStr, date) — convert Delphi TDateTime to formatted string
                    if (args.Count >= 2)
                    {
                        var format = args[0].AsString();
                        var dtVal = args[1].AsDouble();
                        DateTime dt = DateTime.MinValue;
                        try { dt = DateTime.FromOADate(dtVal); } catch { }
                        result = PasValue.FromString(dt.ToString(format));
                    }
                    return true;

                default:
                    return false;
            }
        }

        public bool TryCallThisPlayerFunc(string name, List<PasValue> args, out PasValue result)
        {
            result = PasValue.Nil;
            return TryCallYanshenFunc(name, args, out result);
        }

        private int ExpandStorageSpace(int addedCount)
        {
            // Native sub_6F30A0 reads the raw 32-bit capacity, performs a
            // signed unchecked add, and caps only the upper bound at 0xC0.
            // It does not clamp the starting value or impose a lower bound.
            var current = CurrentPlayer.m_nStorageSpaceCount;
            var target = unchecked(current + addedCount);
            if (target > TPlayObject.MAX_STORAGE_ITEM_COUNT)
                target = TPlayObject.MAX_STORAGE_ITEM_COUNT;
            var actualAdded = unchecked(target - current);
            if (actualAdded > 0)
                CurrentPlayer.m_nStorageSpaceCount = unchecked(current + actualAdded);

            // Native puts the new count in Tag, not Series:
            //   006F30D1  6A 00              push 0                 ; Param  = 0
            //   006F30D3  8B 86 D0 06 00 00  mov eax,[esi+0x6D0]
            //   006F30D9  66 8B 40 08        mov ax,[eax+8] / push  ; Tag    = new count
            //   006F30DE  6A 00              push 0                 ; Series = 0
            //   006F30E0  6A 00              push 0                 ; sMsg   = nil
            //   006F30E2  33 C9              xor ecx,ecx            ; Recog  = 0
            //   006F30E4  66 BA CE 02        mov dx,0x2CE
            //   006F30EC  FF 96 50 02 00 00  call [esi+0x250]
            CurrentPlayer.SendDefMessage(Grobal2.SM_STORAGE_SPACE,
                0, 0, CurrentPlayer.m_nStorageSpaceCount, 0, string.Empty);
            return actualAdded;
        }

        // =====================================================================
        // GLOBAL VARIABLE SYSTEM (G variables)
        // =====================================================================

        /// <summary>
        /// `GetG` 的未命中值是 **-2**，不是 0。
        /// sub_699198 一进门就 `0x6991BF BE FE FF FF FF mov esi,0xFFFFFFFE` 种下 -2，
        /// 只有缓存命中或数据库查到才覆盖它，收尾 `0x6992B2 8B C6 mov eax,esi` 原样返回。
        /// 缓存查找 sub_69B01C 自己的 miss 分支也是 -2（`0x69B040 B8 FE FF FF FF`）。
        /// 脚本里的 `if GetG(a,b) = 0 then` 依赖这个区分——返回 0 会把判断整个反过来。
        /// </summary>
        private const int NativeGlobalVarMiss = -2;

        /// <summary>
        /// `GetG`/`SetG` 共有的 index 窗口 1..50，两侧都是先判范围再碰存储：
        ///   GetG sub_699198  0x6991C4 `83 FB 01 cmp ebx,1`     / 0x6991C7 `0F 8C` jl  -> 0x699290
        ///                    0x6991CD `83 FB 32 cmp ebx,0x32`  / 0x6991D0 `0F 8F` jg  -> 0x699290
        ///   SetG sub_699310  0x6993FD `83 FE 01 cmp esi,1`     / 0x699400 `0F 8C` jl  -> 0x69949D
        ///                    0x699406 `83 FE 32 cmp esi,0x32`  / 0x699409 `0F 8F` jg  -> 0x69949D
        /// 50 就是底层 MySQL 表 `MirParams` 的列数 g1..g50，列名由
        /// 0x6992C4 `'g'` + IntToStr(index) 拼出，行键是 ParamNo。
        /// </summary>
        private const int NativeGlobalVarMinIndex = 1;
        private const int NativeGlobalVarMaxIndex = 50;

        // 扁平键 = ParamNo * 100 + index：
        //   0x6991DF  6B 55 FC 64  imul edx,[ebp-4],0x64   ; ParamNo * 100
        //   0x6991E3  03 D3        add  edx,ebx            ; + index
        // 注意 GameSvrConfig.cs 上 GlobalVal 那句 "nTaskNo*1000+nFieldNo" 的注释与这里的
        // 字节矛盾，乘数是 100 不是 1000。
        public PasValue GetGlobalVar(int group, int index)
        {
            if (index < NativeGlobalVarMinIndex || index > NativeGlobalVarMaxIndex)
                return PasValue.FromInt(NativeGlobalVarMiss);
            if (M2Share.g_Config == null) return PasValue.FromInt(NativeGlobalVarMiss);
            int flat = group * 100 + index;
            // 越出数组 = 原生那张表里没有这个 ParamNo 行，查询返回 0 行，esi 保持 -2。
            if (flat < 0 || flat >= M2Share.g_Config.GlobalVal.Length)
                return PasValue.FromInt(NativeGlobalVarMiss);
            return PasValue.FromInt(M2Share.g_Config.GlobalVal[flat]);
        }

        public bool SetGlobalVar(int group, int index, PasValue value)
        {
            if (index < NativeGlobalVarMinIndex || index > NativeGlobalVarMaxIndex)
                return false;
            if (M2Share.g_Config == null) return false;
            int flat = group * 100 + index;
            if (flat >= 0 && flat < M2Share.g_Config.GlobalVal.Length)
            {
                M2Share.g_Config.GlobalVal[flat] = value.AsInt();
                return true;
            }
            return false;
        }

        // =====================================================================
        // PLAYER VARIABLE SYSTEM (V/S variables)
        // =====================================================================
        //
        // Native registrations in sub_731350: `GetV`->sub_6DF1E4 @0x731530,
        // `SetV`->sub_6DF288 @0x731541, `GetS`->sub_6DF1B4 @0x731552,
        // `SetS`->sub_6DF240 @0x731563.
        //
        // FLAT KEY = arg1*1000 + arg2 (sub_6E42CC: `imul eax,edx,0x3E8 / add eax,ecx`,
        // with the Delphi register convention eax=Self, edx=arg1, ecx=arg2). The GetV
        // prologue confirms the roles: 0x6DF1EB `mov edi,ecx` (=arg2) / 0x6DF1ED
        // `mov esi,edx` (=arg1), and the group-0 fast path tests esi. So C#'s
        // `group * 1000 + index` operand order is CORRECT — do not "fix" it.
        //
        // MISS SENTINEL = -1, NOT 0. Every getter seeds its result with -1 and only a
        // successful lookup overwrites it:
        //   GetS sub_6DF1B4 : 0x6DF1BB `or esi,0xFFFFFFFF`      (overwritten at 0x6DF1DC)
        //   GetV sub_6DF1E4 : 0x6DF1F1 `mov [ebp-4],0xFFFFFFFF` (overwritten at 0x6DF216/0x6DF232)
        //   keyed core sub_6E4270 : 0x6E427A `mov [ebp-4],0xFFFFFFFF`, and the binary
        //     search over the sorted 8-byte key/value pair array (0x6E42A2
        //     `cmp edi,[esi+eax*8]`, value at +4) leaves it at -1 on a miss
        //     (0x6E4288 `test eax,eax / je` on an empty array, 0x6E42BF loop exit).
        // A script guard written `if GetV(a,b) = 0` therefore means "explicitly zero",
        // never "unset" — returning 0 on a miss inverted every such quest guard.
        //
        // arg <= 0 REJECTS (no read, no write):
        //   GetS 0x6DF1BE `test ecx,ecx / jle 0x6DF1DE` + 0x6DF1C2 `test edx,edx / jle`
        //        -> falls to the epilogue with esi still -1.
        //   SetS 0x6DF251 `test edi,edi / jle 0x6DF27D` + 0x6DF255 `test esi,esi / jle`
        //        -> returns al=0 (xor eax,eax @0x6DF24F), sub_6E4140 never called.
        //   keyed GetV 0x6DF21B/0x6DF21F, keyed SetV 0x6DF2B3/0x6DF2B7 — identical pairs.
        // Group 0 is NOT rejected: it takes the fast path whose only gate is the unsigned
        // bound 0x6DF209 `dec edx` / 0x6DF20A `sub edx,0x64` / 0x6DF20D `jae` (SetV mirror
        // 0x6DF29F..0x6DF2A3) = 1 <= index <= 100. Out of that range it falls through to
        // the keyed path, where `group == 0` then trips the `jle` and yields -1 / no write.
        // NativeScriptVarArgsAccepted restores that 1..100 window. Storage itself is
        // TPlayObject.TryGetScriptVar / SetScriptVar (inline group-0 V + keyed bank).

        /// <summary>
        /// Native miss/reject result for `GetV`/`GetS` (0x6DF1BB, 0x6DF1F1, 0x6E427A).
        /// </summary>
        private const int NativeScriptVarMiss = -1;

        /// <summary>
        /// `GetVEx`'s own unresolved-player sentinel, -100 (sub_6E9358 @0x6E937C
        /// `mov esi,0xFFFFFF9C`). Distinct from `GetV`'s -1 lookup miss.
        /// </summary>
        private const int NativeScriptVarExMiss = -100;

        /// <summary>
        /// The shared entry gate of GetV/SetV/GetS/SetS. False = native rejects the call
        /// outright (getter returns -1, setter writes nothing).
        /// </summary>
        private static bool NativeScriptVarArgsAccepted(char type, int group, int index)
        {
            // Group 0 reaches the inline slots, and only V has them. GetV/SetV test the
            // group first and branch to the inline region when it is zero:
            //   GetV 0x6DF203  85 F6        test esi, esi   ; group
            //        0x6DF205  75 14        jne 0x6DF21B    ; != 0 -> keyed path
            //        0x6DF209  4A           dec edx         ; index - 1
            //        0x6DF20A  83 EA 64     sub edx, 0x64
            //        0x6DF20D  73 0C        jae 0x6DF21B    ; unsigned >= 100 -> keyed
            // so the accepted inline window is index 1..100.
            //
            // GetS/SetS have no such branch at all - they open by rejecting either
            // argument being non-positive, which excludes group 0 before anything else:
            //   GetS 0x6DF1BE  85 C9  test ecx,ecx / 0x6DF1C0  7E 1C  jle -> return -1
            //        0x6DF1C2  85 D2  test edx,edx / 0x6DF1C4  7E 18  jle -> return -1
            //   SetS 0x6DF251  85 FF  test edi,edi / 0x6DF253  7E 28  jle -> return false
            //        0x6DF255  85 F6  test esi,esi / 0x6DF257  7E 24  jle -> return false
            // A group-0 S access used to be accepted here and served out of the keyed
            // dictionary, which native never does.
            if (group == 0 && char.ToUpperInvariant(type) == 'V')
                return index >= 1 && index <= 100;
            // Keyed path (0x6DF21B/0x6DF21F, 0x6DF2B3/0x6DF2B7, 0x6DF1BE/0x6DF1C2,
            // 0x6DF251/0x6DF255): both arguments must be strictly positive. Group 0
            // falls in here for S, and for a V index outside 1..100, and is rejected.
            return group > 0 && index > 0;
        }

        /// <summary>
        /// `GetV`/`GetS` = sub_6DF1E4 / sub_6DF1B4. Returns -1 on a rejected argument or a
        /// lookup miss, matching the native seed at 0x6DF1F1 / 0x6DF1BB.
        /// </summary>
        public PasValue GetPlayerVar(char type, int group, int index)
        {
            return GetPlayerVar(CurrentPlayer, type, group, index);
        }

        /// <summary>
        /// Same read for a player that is not the script's <see cref="CurrentPlayer"/>.
        /// 眼神插件走的就是这条路：它的 GetV 跳板 <c>0x10065F00</c> 把玩家指针放进 eax
        /// 后直接 <c>call 0x6DF1E4</c>（<c>0x10065F16 mov [ebp-0x10],0x6DF1E4</c> /
        /// <c>0x10065F27 call [ebp-0x10]</c>），与脚本引擎共用同一个取值函数。
        /// </summary>
        internal static PasValue GetPlayerVar(TPlayObject player, char type, int group, int index)
        {
            if (player == null) return PasValue.FromInt(NativeScriptVarMiss);
            if (!NativeScriptVarArgsAccepted(type, group, index))
                return PasValue.FromInt(NativeScriptVarMiss);
            // Storage lives in two places (inline group-0 V at player+0x808, keyed
            // bank elsewhere). TPlayObject.TryGetScriptVar is the only resolver —
            // do not recompute group*1000+index here. Group-0 V still yields the
            // inline slot (untouched = 0, 0x6DF20F mov eax,[ebx+eax*4+0x808] over
            // the -1 seed at 0x6DF1F1); a keyed miss still maps to -1.
            return player.TryGetScriptVar(type, group, index, out var value)
                ? PasValue.FromInt(value)
                : PasValue.FromInt(NativeScriptVarMiss);
        }

        /// <summary>
        /// Zero-defaulting V/S read used ONLY by C#-side shadow implementations of other
        /// native APIs (internal-skill levels, mark-stone points, daily counters, activity
        /// order flags). Those shadows are not the native `GetV` API — natively they read
        /// their own fields/managers, not this bank — so they must keep their existing
        /// 0-based arithmetic instead of inheriting `GetV`'s -1 miss sentinel
        /// (0x6DF1F1). Never route a script-visible `GetV`/`GetS` through this.
        /// </summary>
        private int GetPlayerVarOrZero(char type, int group, int index)
        {
            var value = GetPlayerVar(type, group, index).AsInt();
            return value == NativeScriptVarMiss ? 0 : value;
        }

        public bool SetPlayerVar(char type, int group, int index, PasValue value)
        {
            return SetPlayerVar(CurrentPlayer, type, group, index, value);
        }

        /// <summary>
        /// `GroupSetV` is registered on the TPlayer PAS face at 0x7318AF
        /// (<c>mov edx,0x6E0830 / mov ecx,0x732A98</c>, the name blob "GroupSetV"),
        /// so the handler is sub_6E0830 and it forwards to TGroup's sub_727754:
        /// <code>
        ///   6E0835  33 DB                 xor ebx,ebx          ; result := False
        ///   6E0837  8B B0 80 0A 00 00     mov esi,[eax+0xA80]  ; the caller's TGroup
        ///   6E083D  85 F6 / 6E083F 74 0D  test esi,esi / je    ; no group -> keep False
        ///   6E0847  E8 08 6F 04 00        call 0x727754
        ///   727765  C6 45 F3 01           mov byte [ebp-0xD],1 ; result preset True
        ///   72776C  8B 58 44              mov ebx,[eax+0x44]   ; bound = member count
        ///   72777E  8B 40 10              mov eax,[eax+0x10]   ; slot -> player
        ///   72778C  85 C0 / 72778E 74 15  test eax,eax / je    ; empty slot -> skip
        ///   727790  80 78 73 00 / 75 0F   cmp [eax+0x73],0/jne ; GHOST -> skip
        ///   7277A0  E8 E3 7A FB FF        call 0x6DF288        ; per-member SetV
        /// </code>
        /// Three contracts follow. An ungrouped caller writes NOTHING and answers
        /// False - 0x6E083F jumps straight to the epilogue, there is no fall back to
        /// a plain SetV on self. Ghost members are skipped. And the result byte
        /// preset at 0x727765 is never cleared, so the answer is True whenever a
        /// group exists, however many members the ghost gate skipped.
        /// </summary>
        internal bool SetGroupPlayerVar(char type, int group, int index, PasValue value)
        {
            var members = CurrentPlayer?.m_GroupOwner?.m_GroupMembers;
            if (members == null)
            {
                return false;
            }

            for (var i = 0; i < members.Count; i++)
            {
                var player = members[i];
                if (player == null || player.m_boGhost)
                    continue;
                SetPlayerVar(player, type, group, index, value);
            }
            return true;
        }

        /// <summary>
        /// `SetV`/`SetS` = sub_6DF288 / sub_6DF240. A rejected argument pair writes
        /// nothing at all (0x6DF2B3/0x6DF2B7, 0x6DF251/0x6DF255).
        /// </summary>
        private static bool SetPlayerVar(TPlayObject player, char type, int group,
            int index, PasValue value)
        {
            if (player == null) return false;
            if (!NativeScriptVarArgsAccepted(type, group, index)) return false;
            // Native SetV group-0 lands in the inline table (0x6DF2A8
            // mov [ebx+esi*4+0x808],eax / 0x6DF2AF mov al,1). Keyed upsert
            // sub_6E4140 writes zero as zero (0x6E4187/0x6E41C2/0x6E4231/0x6E4260).
            // Both paths are TPlayObject.SetScriptVar — do not recompute
            // group*1000+index here (a flat key < 1000 is group 0, which is
            // not in the dictionary).
            player.SetScriptVar(type, group, index, value.AsInt());
            return true;
        }

        // =====================================================================
        // INI FILE OPERATIONS
        // =====================================================================

        public string ReadIniSectionStr(string fileName, string section, string key)
        {
            try
            {
                var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                var filePath = Path.Combine(envirDir, fileName.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(filePath)) return "";

                var lines = File.ReadAllLines(filePath, Encoding.GetEncoding("GBK"));
                bool inSection = false;
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                    {
                        inSection = string.Equals(trimmed.Trim('[', ']'), section, StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (inSection && trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        var eqPos = trimmed.IndexOf('=');
                        return eqPos >= 0 ? trimmed.Substring(eqPos + 1).Trim() : "";
                    }
                }
            }
            catch (Exception ex) { Debug.WriteLine("ReadIniSectionStr failed: " + ex.Message); }
            return "";
        }

        public void WriteIniSectionStr(string fileName, string section, string key, string value)
        {
            try
            {
                var envirDir = Path.Combine(M2Share.sConfigPath, M2Share.g_Config.sEnvirDir);
                var filePath = Path.Combine(envirDir, fileName.Replace('/', Path.DirectorySeparatorChar));
                var dir = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                List<string> lines;
                bool found = false;
                bool inSection = false;

                if (File.Exists(filePath))
                    lines = File.ReadAllLines(filePath, Encoding.GetEncoding("GBK")).ToList();
                else
                    lines = new List<string>();

                for (int i = 0; i < lines.Count; i++)
                {
                    var trimmed = lines[i].Trim();
                    if (trimmed.StartsWith("["))
                    {
                        inSection = string.Equals(trimmed.Trim('[', ']'), section, StringComparison.OrdinalIgnoreCase);
                        continue;
                    }
                    if (inSection && trimmed.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                    {
                        lines[i] = key + "=" + value;
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    if (!inSection)
                        lines.Add("[" + section + "]");
                    lines.Add(key + "=" + value);
                }

                File.WriteAllLines(filePath, lines, Encoding.GetEncoding("GBK"));
            }
            catch (Exception ex) { Debug.WriteLine("WriteIniSectionStr failed: " + ex.Message); }
        }

        public void ServerSay(string msg, int color)
        {
            YanshenPangu2Patches.BroadcastServerSay(msg, color);
        }

        // =====================================================================
        // HELPER METHODS
        // =====================================================================

        /// <summary>
        /// Perform weapon upgrade operation. Handles DC/MC/SC upgrade with break chance.
        /// upType: 0=DC, 1=MC, 2=SC; bSureSuc: guaranteed success; bNoBreak: no break on fail
        /// </summary>
        private void PerformWeaponUpgrade(int upType, bool bSureSuc, bool bNoBreak)
        {
            if (CurrentPlayer == null) return;
            var weapon = CurrentPlayer.m_UseItems[Grobal2.U_WEAPON];
            if (weapon == null || weapon.wIndex <= 0 || weapon.btValue == null)
                return;

            // Check upgrade limit
            var totalUp = (int)weapon.btValue[0] + (int)weapon.btValue[1] + (int)weapon.btValue[2];
            if (totalUp >= M2Share.g_Config.nUpgradeWeaponMaxPoint)
            {
                CurrentPlayer.SysMsg(M2Share.g_sTheWeaponBroke, MsgColor.Red, MsgType.Hint);
                return;
            }

            if (bSureSuc)
            {
                // Guaranteed success: increment upgrade value and upgrade count
                if (upType >= 0 && upType <= 2 && upType < weapon.btValue.Length)
                    weapon.btValue[upType] = (byte)Math.Min(255, weapon.btValue[upType] + 1);
                if (weapon.btValue.Length > 4)
                    weapon.btValue[4] = (byte)Math.Min(255, weapon.btValue[4] + 1); // upgrade count
                CurrentPlayer.SysMsg(M2Share.sTheWeaponRefineSuccessfull, MsgColor.Green, MsgType.Hint);
            }
            else
            {
                // Random success/fail based on config rates
                var rate = upType switch
                {
                    0 => M2Share.g_Config.nUpgradeWeaponDCRate,
                    1 => M2Share.g_Config.nUpgradeWeaponMCRate,
                    _ => M2Share.g_Config.nUpgradeWeaponSCRate
                };
                var roll = M2Share.RandomNumber.Random(1000);
                if (roll < rate)
                {
                    if (upType >= 0 && upType <= 2 && upType < weapon.btValue.Length)
                        weapon.btValue[upType] = (byte)Math.Min(255, weapon.btValue[upType] + 1);
                    if (weapon.btValue.Length > 4)
                        weapon.btValue[4] = (byte)Math.Min(255, weapon.btValue[4] + 1);
                    CurrentPlayer.SysMsg(M2Share.sTheWeaponRefineSuccessfull, MsgColor.Green, MsgType.Hint);
                }
                else if (!bNoBreak)
                {
                    // Weapon breaks on failure
                    CurrentPlayer.SysMsg(M2Share.g_sTheWeaponBroke, MsgColor.Red, MsgType.Hint);
                    var oldWeapon = new TUserItem(weapon);
                    CurrentPlayer.SendDelItems(oldWeapon);
                    weapon.wIndex = 0;
                    CurrentPlayer.SendRefMsg(Grobal2.RM_BREAKWEAPON, 0, 0, 0, 0, "");
                }
                else
                {
                    // No break on failure - just lose upgrade materials
                    CurrentPlayer.SysMsg("你的武器升级失败", MsgColor.Red, MsgType.Hint);
                }
            }

            CurrentPlayer.RecalcAbilitys();
            CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
        }

        // === PAS Take / TakeExpand / GetBagItemCount — byte-faithful port of 战神 ===
        //
        // Registrations (sub_731350, the TPsNpc procedure table):
        //   `GetBagItemCount` -> sub_7447C0 @0x73140E (a `xor ecx,ecx` + tail-call wrapper
        //                        over the count core sub_7447CC @0x7447C5)
        //   `TakeExpand`      -> sub_6DF7D4 @0x731463 (pushes its caller-supplied 3rd arg)
        //   `Take`            -> sub_6DFA40 @0x731474 (hardcodes `push 0` @0x6DFA43)
        //   `DelAllThisItem`  -> sub_7409D4 @0x731584 (its OWN loop, not a Take re-entry)
        // Both the count core and the consume body live on the bag list only —
        // `[player+0x508]` = m_ItemList (`0x7447F8`, `0x6DF874`). Neither ever reads or
        // writes `m_UseItems`, so NO equipment is ever touched on this path.
        //
        // THREE native invariants this port restores (all three were item-loss bugs):
        //
        //  (1) ALL-OR-NOTHING. sub_6DF7E8 PRE-COUNTS before it mutates anything:
        //        0x6DF854 call sub_7447CC        ; count with the same filter
        //        0x6DF859 mov [ebp-0x14],eax
        //        0x6DF85F cmp eax,[ebp-8]        ; [ebp-8] = requested count
        //        0x6DF862 jl  0x6DF9F3           ; -> epilogue, [ebp-9] still 0, ZERO mutations
        //      Only once the count passes does 0x6DF86D set the result byte to 1 and the
        //      removal loop start @0x6DF871. A short `take` removes NOTHING natively.
        //
        //  (2) STACKS COUNT AND CONSUME BY Dura, NEVER DELETE-WHOLE UNLESS DRAINED.
        //      The runtime kind byte `[item+0x14] == 7` marks a pile/stack object. That is
        //      the RUNTIME CLASS tag, not StdMode: the base item ctor sub_783788 writes
        //      `mov byte [ebx+0x14],0` @0x7837AE, and ONLY the pile ctor sub_7880F0 writes
        //      `mov byte [esi+0x14],7` @0x788118 (it also seeds `word[esi+0x26]`=Dura=1
        //      @0x788112). An exhaustive image scan for `mov byte [r+0x14],7` finds it in
        //      exactly two item ctors (0x788118 pile, plus 0x788C01/0x788C84/0x78B27C/
        //      0x78B2D8/0x78B328/0x78B544 which all chain through sub_7880F0), so the
        //      predicate is "was constructed by the pile ctor" == NativeItemFactory.IsPileItem
        //      (CLASS ANCESTRY: the factory-reachable classes whose VMT parent chain ends at
        //      TBasePileItem/0x781C24 — NOT a StdMode range, and NOT `StdMode == 7`).
        //      StdMode >= 150 is only the DEFAULT arm 0x74D67E `3C 96 cmp al,0x96` /
        //      0x74D680 `72 13 jb`; the explicitly-cased StdMode 3 / Shape 4 arm reaches the
        //      same pile ctor at 0x74CCEC, so TLuckOil is a pile with StdMode 3.
        //      (StdMode 7 is TCharm/TMarkStoneCharm, a normal single item; see the audited
        //      note at TPlayObject.Operate.cs:771 for the same finding on the item-use tail).
        //      count  : 0x744852 cmp byte [ebx+0x14],7 / 0x744858 movzx eax,word [ebx+0x26]
        //               / 0x74485C add [ebp-0xc],eax  (add the stack's Dura)
        //               vs 0x744861 inc [ebp-0xc]     (non-stack: +1 per slot)
        //      consume: 0x6DF8E0 cmp byte [ebx+0x14],7; when the stack holds MORE than the
        //               remaining need (0x6DF93B cmp edi,eax / 0x6DF93D jl 0x6DF96A):
        //                 0x6DF96A add [ebp-0x14],edi        ; credit the partial take
        //                 0x6DF96D sub word [ebx+0x26],di    ; DECREMENT Dura, item STAYS
        //                 0x6DF984 call [vtbl+0x260]         ; SM_BAGITEMDURACHG refresh
        //               otherwise it drains the whole stack: 0x6DF93F add [ebp-0x14],eax /
        //               0x6DF94D sub_424B30 (TList.Delete) / 0x6DF95B [vtbl+0x268] (SendDelItems)
        //               / 0x6DF963 sub_404690 (free).
        //
        //  (3) THE 3rd ARG IS A TRI-STATE LOCK FILTER, NOT "include equipment".
        //      Identical ladder in BOTH the count core and the consume body, gating on
        //      sub_784710 = `mov ax, word [eax+0x34] / ret` — the bind/lock word, which this
        //      repo already models as btValue[10..11] (NativeStrengthenEquipExec
        //      .ItemLockWordOffset == 0x34; TPlayObject.NativeStall.cs:179; HeroObject.cs:2325).
        //        filter == 0 : 0x6DF8AE cmp [ebp+8],0 / 0x6DF8B2 je body      -> accept ALL
        //        filter == 1 : 0x6DF8B4 cmp [ebp+8],1 / 0x6DF8B8 jne next
        //                      0x6DF8BC call sub_784710 / 0x6DF8C1 test ax,ax
        //                      0x6DF8C4 ja  body                              -> accept only lockWord > 0
        //        filter == 2 : 0x6DF8C6 cmp [ebp+8],2 / 0x6DF8CA jne skip
        //                      0x6DF8D2 call sub_784710 / 0x6DF8D7 test ax,ax
        //                      0x6DF8DA jne skip                              -> accept only lockWord == 0
        //        any other   : falls through 0x6DF8CA `jne 0x6DF9D9` -> slot SKIPPED entirely.
        //      (Count core mirror: 0x744828 / 0x74482E / 0x744836 `ja 0x744852` /
        //       0x744840 / 0x744848 `jne 0x744864`.) Note `ja` is UNSIGNED on a word — with a
        //      u16 lock word, `ja 0` is exactly `!= 0`. So filter 1 = LOCKED-ONLY and
        //      filter 2 = UNLOCKED-ONLY; they are opposite-sense filters. `Take` always
        //      passes 0 (0x6DFA43), so plain `take` is unfiltered.
        //
        // Name resolution: 0x6DF831 resolves the name ONCE via sub_74C1E0 on the global
        // std-item table [0x7D5D6C] into [ebp-0x10] = the StdItem INDEX, then compares per
        // slot with 0x6DF8A1 movzx eax,word [ebx+0x24] / 0x6DF8A5 cmp eax,[ebp-0x10] — an
        // integer index compare on TUserItem.wIndex. 0x6DF842 `cmp [ebp-0x10],0 / jle` makes
        // an unknown name a clean no-op with no scan. Kept as an index compare (matching
        // GetStdItemIdx, whose -1/0 miss the `<= 0` gate already rejects).

        /// <summary>
        /// Native filter values for the 3rd argument of `TakeExpand` / `GetBagItemCount`
        /// (sub_6DF7E8 @0x6DF8AE..0x6DF8DA, sub_7447CC @0x744828..0x744850).
        /// </summary>
        private const int NativeTakeFilterAll = 0;          // 0x6DF8B2 je -> accept every slot
        private const int NativeTakeFilterLockedOnly = 1;   // 0x6DF8C4 ja -> word[item+0x34] != 0
        private const int NativeTakeFilterUnlockedOnly = 2; // 0x6DF8DA jne skip -> word[item+0x34] == 0

        /// <summary>
        /// sub_784710 (`mov ax, word [eax+0x34] / ret`) — the item bind/lock word,
        /// stored in this repo as the little-endian pair btValue[10..11].
        /// </summary>
        private static int NativeItemLockWord(TUserItem item)
        {
            if (item?.btValue == null || item.btValue.Length < 12) return 0;
            return item.btValue[10] | (item.btValue[11] << 8);
        }

        /// <summary>
        /// The per-slot acceptance ladder shared by sub_6DF7E8 (@0x6DF8AE) and
        /// sub_7447CC (@0x744828). An unrecognised filter skips every slot, exactly like
        /// native's fall-through at 0x6DF8CA / 0x744844.
        /// </summary>
        private static bool NativeTakeSlotAccepted(TUserItem item, int filter)
        {
            switch (filter)
            {
                case NativeTakeFilterAll:
                    return true;
                case NativeTakeFilterLockedOnly:
                    return NativeItemLockWord(item) != 0;   // `ja` on a u16 == "!= 0"
                case NativeTakeFilterUnlockedOnly:
                    return NativeItemLockWord(item) == 0;
                default:
                    return false;                            // native 0x6DF8CA jne -> skip
            }
        }

        /// <summary>
        /// `[item+0x14] == 7` — the runtime pile/stack class tag written only by the pile
        /// ctor sub_7880F0 (@0x788118); the base ctor sub_783788 writes 0 (@0x7837AE).
        /// </summary>
        private static bool NativeTakeIsStack(TUserItem item)
        {
            return NativeItemFactory.IsPileItem(M2Share.UserEngine?.GetStdItem(item.wIndex));
        }

        /// <summary>
        /// sub_7447CC — count matching bag items, adding a stack's Dura (`word[item+0x26]`)
        /// rather than 1. sub_7447C0 (= the registered `GetBagItemCount`) is this with
        /// filter 0.
        /// </summary>
        private int CountBagItem(string itemName, int filter = NativeTakeFilterAll)
        {
            if (CurrentPlayer?.m_ItemList == null) return 0;
            // 0x7447E7 sub_74C1E0 name->index; 0x7447EF cmp [ebp-0x10],0 / jle -> 0
            var stdIndex = M2Share.UserEngine?.GetStdItemIdx(itemName) ?? -1;
            if (stdIndex <= 0) return 0;

            var count = 0;                                   // [ebp-0xc]
            for (var i = 0; i < CurrentPlayer.m_ItemList.Count; i++)
            {
                var item = CurrentPlayer.m_ItemList[i];
                if (item == null) continue;                   // 0x74481B test ebx,ebx / je
                if (item.wIndex != stdIndex) continue;        // 0x74481F/0x744823 index compare
                if (!NativeTakeSlotAccepted(item, filter)) continue;
                count += NativeTakeIsStack(item)
                    ? item.Dura                               // 0x744858/0x74485C add Dura
                    : 1;                                      // 0x744861 inc
            }
            return count;
        }

        /// <summary>
        /// sub_6DF7E8 — the shared body behind `Take` (sub_6DFA40, filter 0) and
        /// `TakeExpand` (sub_6DF7D4, caller-supplied filter). All-or-nothing; stacks
        /// decrement; equipment is never touched.
        /// </summary>
        private bool TakeItemsCore(string itemName, int count,
            int filter = NativeTakeFilterAll)
        {
            if (CurrentPlayer?.m_ItemList == null) return false;
            // 0x6DF815 cmp [ebp-8],0 / jne — a non-positive count returns TRUE with no work.
            if (count <= 0) return true;                      // 0x6DF81B mov byte [ebp-9],1
            // 0x6DF831 name->index once; 0x6DF842 unknown name -> False, no scan.
            var stdIndex = M2Share.UserEngine?.GetStdItemIdx(itemName) ?? -1;
            if (stdIndex <= 0) return false;

            // (1) PRE-COUNT GATE @0x6DF854..0x6DF862 — bail with ZERO mutations when short.
            if (CountBagItem(itemName, filter) < count) return false;

            var taken = 0;                                    // [ebp-0x14]
            // 0x6DF87A..0x6DF9E5: descending index over m_ItemList.
            for (var index = CurrentPlayer.m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = CurrentPlayer.m_ItemList[index];
                if (item == null) continue;                   // 0x6DF899 test ebx,ebx / je
                if (item.wIndex != stdIndex) continue;        // 0x6DF8A1/0x6DF8A5
                if (!NativeTakeSlotAccepted(item, filter)) continue;

                if (NativeTakeIsStack(item))                  // 0x6DF8E0 cmp byte[+0x14],7
                {
                    var need = count - taken;                 // 0x6DF8EA/0x6DF8ED edi
                    if (need < item.Dura)                     // 0x6DF93B/0x6DF93D jl
                    {
                        // 0x6DF96A/0x6DF96D: partial DECREMENT, the item stays in the bag.
                        taken += need;
                        item.Dura = (ushort)(item.Dura - need);
                        // 0x6DF984 call [vtbl+0x260] — durability refresh, no delete.
                        CurrentPlayer.SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                            CurrentPlayer.EnsureClientItemId(item), item.Dura,
                            item.DuraMax, 0, "");
                    }
                    else
                    {
                        // 0x6DF93F..0x6DF963: stack fully drained -> delete the whole entry.
                        taken += item.Dura;
                        CurrentPlayer.m_ItemList.RemoveAt(index);   // sub_424B30
                        CurrentPlayer.SendDelItems(item);           // [vtbl+0x268]
                        CurrentPlayer.Dispose(item);                // sub_404690
                    }
                }
                else
                {
                    // 0x6DF98C..0x6DF9D4: non-stack -> +1 and remove the slot.
                    taken++;
                    CurrentPlayer.m_ItemList.RemoveAt(index);
                    CurrentPlayer.SendDelItems(item);
                    CurrentPlayer.Dispose(item);
                }

                // 0x6DF9D9 cmp [ebp-0x14],[ebp-8] / jge -> stop once satisfied.
                if (taken >= count) break;
            }

            // 0x6DF9EB call sub_73CEE4 = recompute bag weight (writes [self+0x2C4] and
            // sets the [self+0x458] "weight changed" flag) — reached on the mutating path only.
            CurrentPlayer.WeightChanged();
            return true;                                      // [ebp-9] was set at 0x6DF86D
        }

        /// <summary>`Take` = sub_6DFA40: sub_6DF7E8 with filter 0 (`push 0` @0x6DFA43).</summary>
        private bool TakeItems(string itemName, int count)
        {
            return TakeItemsCore(itemName, count, NativeTakeFilterAll);
        }

        /// <summary>
        /// `TakeExpand` = sub_6DF7D4: sub_6DF7E8 with the script-supplied tri-state lock
        /// filter. Native never touches equipment here (the loop walks `[player+0x508]`
        /// only), so neither does this.
        /// </summary>
        private bool TakeItemsEx(string itemName, int count, int filter)
        {
            return TakeItemsCore(itemName, count, filter);
        }

        /// <summary>
        /// `DelAllThisItem` = sub_7409D4 -> sub_740A00. A single descending pass over
        /// `[player+0x508]` (m_ItemList) removing EVERY slot whose std-item index matches;
        /// returns the number of entries removed ([ebp-0xc], surfaced at 0x740AF9).
        /// Gates: 0x7409E4 sub_74C1E0 name->index once, `0x7409E9 test eax,eax / jl` so a
        /// miss returns 0; 0x740A59 `cmp [ebx+0x1c],0 / je` skips a slot with no std-item
        /// back-pointer; 0x740A62 `movzx eax, word [[ebx+0x1c]]` / 0x740A65 `cmp eax,[ebp-8]`
        /// is the index compare. No count, no lock filter, no Dura arithmetic — a stack is
        /// deleted whole like any other entry. NOT a `Take` loop, so no all-or-nothing gate.
        /// </summary>
        private int DelAllThisItem(string itemName)
        {
            if (CurrentPlayer?.m_ItemList == null) return 0;
            var stdIndex = M2Share.UserEngine?.GetStdItemIdx(itemName) ?? -1;
            if (stdIndex <= 0) return 0;                      // 0x7409E9 test/jl -> 0

            var removed = 0;                                  // [ebp-0xc]
            for (var index = CurrentPlayer.m_ItemList.Count - 1; index >= 0; index--)
            {
                var item = CurrentPlayer.m_ItemList[index];
                if (item == null) continue;                   // 0x740A55 test ebx,ebx / je
                if (item.wIndex != stdIndex) continue;        // 0x740A62/0x740A65
                CurrentPlayer.m_ItemList.RemoveAt(index);     // 0x740A75 sub_424B30
                CurrentPlayer.SendDelItems(item);             // 0x740A83 [vtbl+0x268]
                CurrentPlayer.Dispose(item);                  // 0x740AC7 sub_404690
                removed++;                                    // 0x740ACC inc [ebp-0xc]
            }
            if (removed > 0) CurrentPlayer.WeightChanged();
            return removed;
        }

        private bool ExecuteNativeTakeEx(IReadOnlyList<PasValue> args)
        {
            // The Delphi implementation mutates inventory but never sets its
            // Boolean result. Preserve that observable behavior.
            if (CurrentPlayer == null || args.Count < 4) return false;

            var itemName = args[0].AsString();
            var stdItem = M2Share.UserEngine.GetStdItem(itemName);
            if (stdItem == null) return false;

            var remaining = unchecked((ushort)args[1].AsInt());
            var reason = args[2].AsString();
            var includeEquipment = args[3].AsBool();
            var equipmentChanged = false;

            if (includeEquipment)
            {
                for (var slot = 0; slot < CurrentPlayer.m_UseItems.Length; slot++)
                {
                    var item = CurrentPlayer.m_UseItems[slot];
                    if (!NativeItemNameEquals(item, itemName)) continue;

                    AddNativeTakeExLog(itemName, item.MakeIndex, reason + " 收取");
                    CurrentPlayer.m_UseItems[slot] = null;
                    equipmentChanged = true;
                }
            }

            for (var index = CurrentPlayer.m_ItemList.Count - 1;
                 index >= 0 && remaining > 0;
                 index--)
            {
                var item = CurrentPlayer.m_ItemList[index];
                if (!NativeItemNameEquals(item, itemName)) continue;

                var itemDefinition = M2Share.UserEngine.GetStdItem(item.wIndex);
                if (itemDefinition == null) continue;

                // 原生 TakeEx = sub_74089C，两处堆叠闸读的都是【物品实例】+0x14：
                //   0x740B8F  80 7B 14 07  cmp byte [ebx+0x14],7   ; 预清点循环
                //   0x740C01  80 7B 14 07  cmp byte [ebx+0x14],7   ; 实际扣除循环
                // ebx 是 0x740BE5 `call 0x424D4C`(TList.Get) 取出的背包物品实例，
                // 不是模板；紧接着 0x740C57 `0F B7 43 26` 取的也是实例 +0x26(Dura)。
                // 和两条 give 核心（0x6C89C6 / 0x6C85C5）是同一个谓词，
                // 所以这里必须用类祖先判定：StdMode==7 是护身符族（实例 +0x14 恒为 0），
                // 而真堆叠（TLuckOil 走 StdMode 3/Shape 4）会被漏判成整件删除。
                if (NativeItemFactory.IsPileItem(itemDefinition))
                {
                    var before = remaining;
                    AddNativeTakeExLog(itemName, item.MakeIndex,
                        reason + " 收取" + before + "个");
                    if (remaining < item.Dura)
                    {
                        item.Dura = (ushort)(item.Dura - remaining);
                        remaining = 0;
                        CurrentPlayer.SendDefMessage(Grobal2.SM_BAGITEMDURACHG,
                            CurrentPlayer.EnsureClientItemId(item), item.Dura,
                            item.DuraMax, 0, "");
                    }
                    else
                    {
                        remaining = (ushort)(remaining - item.Dura);
                        CurrentPlayer.SendDelItems(item);
                        CurrentPlayer.m_ItemList.RemoveAt(index);
                    }
                }
                else
                {
                    AddNativeTakeExLog(itemName, item.MakeIndex, reason + " 收取");
                    CurrentPlayer.SendDelItems(item);
                    CurrentPlayer.m_ItemList.RemoveAt(index);
                    remaining--;
                }
            }

            if (equipmentChanged)
            {
                CurrentPlayer.RecalcAbilitys();
                CurrentPlayer.SendMsg(CurrentPlayer, Grobal2.RM_ABILITY, 0, 0, 0, 0, "");
                CurrentPlayer.m_DefMsg = Grobal2.MakeDefaultMsg(Grobal2.SM_TAKEOFF_OK,
                    CurrentPlayer.GetFeatureToLong(), CurrentPlayer.GetFeatureEx(), 0, 0);
                CurrentPlayer.SendSocket(CurrentPlayer.m_DefMsg, CurrentPlayer.GetMobileFeature());
                CurrentPlayer.FeatureChanged();
            }

            return false;
        }

        private bool NativeItemNameEquals(TUserItem item, string itemName)
        {
            if (item == null || item.wIndex <= 0) return false;
            return string.Equals(M2Share.UserEngine.GetStdItemName(item.wIndex), itemName,
                StringComparison.OrdinalIgnoreCase);
        }

        private void AddNativeTakeExLog(string itemName, int makeIndex, string description)
        {
            M2Share.AddGameDataLog(string.Join('\t', 10, CurrentPlayer.m_sMapName,
                CurrentPlayer.m_nCurrX, CurrentPlayer.m_nCurrY, CurrentPlayer.m_sCharName,
                itemName, makeIndex, 1, description));
        }

        // Native GiveItemWithDura (sub_6E15E0, 声明见注册表 0x72B56D
        // `function GiveItemWithDura(const itemName:string; ItemNum:Integer;
        //  ItemDura:Word): Boolean`): 造 count 件, 每件 Dura = min(要求耐久, DuraMax),
        // 入包并写日志。原生是**应用**这个耐久而不是丢弃它, 所以这是个真 API。
        // 它全程不看堆叠标记 —— 对堆叠物这就是直接指定堆内件数。
        //
        // 全或无背包门（这条以前整条缺失）:
        //   0x6E15F7  E8 DC 2B 06 00  call 0x7441D8   ; 空格数
        //   0x6E15FC  3B 45 F8        cmp eax,[ebp-8] ; 空格数 vs count
        //   0x6E15FF  0F 8C 96 00..   jl  0x6E169B    ; 装不下 -> 一件都不发
        //   0x6E169B  66 B9 DB FF     mov cx,0xFFDB   ; 红字 '您包裹位不足' (0x6E16D8)
        // 其中 0x7441D8 是 `mov eax,[eax+0x508] / mov edx,0x30 / sub edx,[eax+8]`,
        // 即 48-背包件数; 本工程的容量权威是 BagCapacity(REPLICATION_RULES §4.18)。
        // 预检不过直接返回 False, 不能像原来那样先塞满背包再停。
        //
        // 耐久取的是 **word**: 0x6E1631 `66 8B 40 28 mov ax,word [eax+0x28]`,
        // 0x6E1635 `66 3B 45 08 cmp ax,word [ebp+8]`, 0x6E1639 `72 0D jb`,
        // 0x6E163E `66 8B 55 08 mov dx,word [ebp+8]` —— 入参只按低 16 位取用,
        // 所以脚本传 70000 时原生用的是 4464。旧写法 Math.Min(int, DuraMax) 在
        // DuraMax > 4464 时会发出比原生更多的数量, 对堆叠物 Dura 就是件数, 那是刷物方向。
        private void TryGiveItemWithDura(List<PasValue> args, out bool gaveAny)
        {
            gaveAny = false;
            if (CurrentPlayer == null || args.Count < 3) return;
            var itemName = args[0].AsString();
            var count = args[1].AsInt();
            var requestedDura = (ushort)args[2].AsInt();

            if (BagCapacity.Of(CurrentPlayer) - CurrentPlayer.m_ItemList.Count < count)
            {
                CurrentPlayer.SysMsg("您包裹位不足", MsgColor.Red, MsgType.Hint);
                return;
            }

            for (var c = 0; c < count; c++)
            {
                var userItem = new TUserItem();
                if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem)) break;
                if (M2Share.UserEngine.GetStdItem(userItem.wIndex) == null) break;
                userItem.Dura = Math.Min(requestedDura, userItem.DuraMax);
                // 0x6E1686 FreeAndNil / 0x6E168E `C6 45 F7 00` 把结果字节清回 0
                // 再 0x6E1692 dec ebx 继续下一轮, 所以塞不进去的那一件不算成功,
                // 但循环不中断。
                if (!CurrentPlayer.AddItemToBag(userItem))
                {
                    CurrentPlayer.Dispose(userItem);
                    gaveAny = false;
                    continue;
                }
                CurrentPlayer.SendAddItem(userItem);
                gaveAny = true;
            }
        }

        /// <summary>
        /// 原生 <c>SysGiveGift</c> = <c>sub_6C8548</c>（声明见注册表 0x72BCC4
        /// <c>function SysGiveGift(const ItemStr: string;Num: integer;BoBind: Boolean)</c>）。
        /// 它不是 <c>Give</c> 的共享核心 <c>sub_6C87B4</c>，所以**不做冒号拆分**：
        /// 0x6C85AD <c>8B 55 FC mov edx,[ebp-4]</c> 把入参名原样交给 0x6C85B0
        /// <c>E8 9F 58 08 00 call 0x74DE54</c>。
        ///
        /// 堆叠判据和 Give 核心是同一条，读的是**物品实例**的运行时类标记：
        ///   0x6C85C2  8B 45 EC     mov eax,[ebp-0x14]   ; 0x74DE54 造出来的实例
        ///   0x6C85C5  80 78 14 07  cmp byte [eax+0x14],7
        /// 实例 +0x14 由构造器写死：根构造器 <c>sub_783788</c> @0x7837AE
        /// <c>C6 43 14 00</c> 写 0，堆叠构造器 <c>sub_7880F0</c> @0x788118
        /// <c>C6 46 14 07</c> 写 7。它**不是**模板的 StdMode ——
        /// 模板 StdMode 只决定工厂 <c>sub_74C338</c> 选哪个构造器，而堆叠构造器
        /// 并不只挂在 StdMode&gt;=150 的默认臂（0x74D67E <c>3C 96 cmp al,0x96</c> /
        /// 0x74D680 <c>72 13 jb</c> / 0x74D68C <c>call 0x7880F0</c>）上：StdMode 3
        /// 的 Shape 4 也直落堆叠构造器 ——
        ///   0074CCE5  A1 AC 1C 78 00  mov  eax,[0x781CAC]  ; TLuckOil
        ///   0074CCEC  E8 FF B3 03 00  call 0x7880F0        ; TBasePileItem.Create
        /// 所以谓词是类祖先，即 <see cref="NativeItemFactory.IsPileItem"/>。
        /// 写 <c>StdMode == 7</c> 是双向错：StdMode 7 是护身符族（0x74CE9E 按 Shape
        /// 二级派发到 TCryCharm/THPCharm/...），实例 +0x14 恒为 0，会被误当堆叠塞进
        /// 一格；而真堆叠（金条、幸运油）反而按件占满背包。
        ///
        /// 数量：0x6C85D5 <c>3B 55 F8 cmp edx,[ebp-8]</c>（DuraMax vs 剩余）/
        /// 0x6C85D8 <c>7C 11 jl</c>。DuraMax &gt;= 剩余就 <c>Dura := 剩余</c>
        /// （0x6C85E1）并置 0x6C85E5 的结束标记；否则 <c>Dura := DuraMax</c>
        /// （0x6C85EE）且 0x6C85F9 <c>29 45 F8 sub [ebp-8],eax</c> 扣掉一整堆。
        /// 非堆叠臂不动剩余量，靠 0x6C8713 的计数器跑满 count 次。
        /// </summary>
        private bool TrySysGiveGift(IReadOnlyList<PasValue> args)
        {
            if (CurrentPlayer == null || args.Count < 2) return false;

            var itemName = args[0].AsString();
            var requestedCount = args[1].AsInt();
            // 0x6C858C cmp [ebp-8],0 / 0x6C8590 jg / 0x6C8592 mov [ebp-8],1
            if (requestedCount <= 0) requestedCount = 1;
            var bind = args.Count >= 3 && args[2].AsBool();
            if (TryExecuteTunnelGive(itemName, requestedCount, bind)) return true;

            var remaining = requestedCount;
            var gaveAny = false;
            for (var i = 0; i < requestedCount && remaining > 0; i++)
            {
                var userItem = new TUserItem();
                if (!M2Share.UserEngine.CopyToUserItemFromName(itemName, ref userItem)) break;

                var stdItem = M2Share.UserEngine.GetStdItem(userItem.wIndex);
                if (stdItem == null) break;
                if (NativeItemFactory.IsPileItem(stdItem))
                {
                    // 原生没有这道闸：DuraMax==0 时 0x6C85D5 的比较走 jl 臂，
                    // Dura:=0 且 0x6C85F9 扣 0，靠计数器跑满 count 次发出 count 份
                    // 空堆。C# 用的是「剩余量」循环，扣 0 会原地打转，所以这里必须
                    // 收手；少发不是刷物方向。
                    if (userItem.DuraMax == 0) break;
                    var quantity = Math.Min(remaining, userItem.DuraMax);
                    userItem.Dura = (ushort)quantity;
                    remaining -= quantity;
                }
                else
                {
                    remaining--;
                }

                if (bind) userItem.Bind = 1;
                // 0x6C86CF lea eax,[ebp-0x14] / 0x6C86D2 call 0x414C24 (FreeAndNil)
                // / 0x6C86D7 jmp 0x6C871A —— 装不下就丢弃这一件并整体退出，
                // 已发出的那些保留，结果字节 [ebp-9] 不回滚。
                if (!CurrentPlayer.AddItemToBag(userItem))
                {
                    CurrentPlayer.Dispose(userItem);
                    break;
                }
                CurrentPlayer.SendAddItem(userItem);
                gaveAny = true;
            }
            return gaveAny;
        }

        /// <summary>Execute a SQL non-query (INSERT/UPDATE/DELETE).</summary>
        private void ExecuteSqlNonQuery(string query)
        {
            try
            {
                var connStr = M2Share.g_Config?.sConnctionString;
                if (string.IsNullOrEmpty(connStr)) return;
                using (var conn = new MySql.Data.MySqlClient.MySqlConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(query, conn))
                    {
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecuteSqlNonQuery failed: " + ex.Message);
            }
        }

        /// <summary>Execute a SQL query and return result set.</summary>
        private List<Dictionary<string, string>> ExecuteSqlQuery(string query)
        {
            var result = new List<Dictionary<string, string>>();
            try
            {
                var connStr = M2Share.g_Config?.sConnctionString;
                if (string.IsNullOrEmpty(connStr)) return result;
                using (var conn = new MySql.Data.MySqlClient.MySqlConnection(connStr))
                {
                    conn.Open();
                    using (var cmd = new MySql.Data.MySqlClient.MySqlCommand(query, conn))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var row = new Dictionary<string, string>();
                            for (int i = 0; i < reader.FieldCount; i++)
                            {
                                var colName = reader.GetName(i);
                                var colValue = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                                row[colName] = colValue;
                            }
                            result.Add(row);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("ExecuteSqlQuery failed: " + ex.Message);
            }
            return result;
        }
    }
}
