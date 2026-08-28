using SystemModule;

namespace GameSvr
{
    /// <summary>
    /// Native <c>THeroAct</c> locomotion — the port of <c>sub_68B838</c> and its
    /// helpers, i.e. what the hero actually calls to close on a point.
    ///
    /// ── Why this file exists
    /// The port reached hero movement through <c>AnimalObject.SetTargetXY</c> +
    /// <c>GotoTargetXY</c>, which is <c>sub_71DDD0</c> — a NON-VIRTUAL TAnimal
    /// method that appears in no VMT, so native THeroAct cannot reach it at all.
    /// Native passes the destination as arguments instead of storing it in fields,
    /// which is why no humanoid ever needed <c>m_nTargetX</c>/<c>m_nTargetY</c>.
    /// Reaching the native shape here is what lets <c>THumanKind</c> stop deriving
    /// from <c>AnimalObject</c>, removing 10 of the 11 invented-layer violations in
    /// the tree.
    ///
    /// ── The chain, found by walking forward from the walk gate
    /// <code>
    ///   [vmt+0x0BC] = 0x6908A4   THeroAct walk-tick predicate
    ///     -> Chebyshev to the anchor [+0x65C]/[+0x660] must exceed 2
    ///     -> 0x68B838(X, Y)      this file
    ///          -> 0x68BD28  run two cells   (0x76756C, the CommitRunMove primitive)
    ///          -> 0x68BD70  walk one cell   (call [vmt+0x030] = 0x741224, the
    ///                                        humanoid mover THumanKind overrides)
    /// </code>
    /// </summary>
    public partial class HeroObject
    {
        /// <summary>
        /// <c>[Self+0x630]</c>, created per hero by ctor <c>sub_6864C4</c> at
        /// <c>0x686573</c>. The ring of cells already tried this walk.
        /// </summary>
        private readonly TARoundList _nativeVisitedCells = new TARoundList();

        /// <summary>
        /// <c>[Self+0x634]</c>, created per hero at <c>0x686585</c>. Walkable
        /// candidates collected while probing directions; when no direction is
        /// usable the mover picks one of these at random.
        /// </summary>
        private readonly TRoundCell _nativeCandidateCells = new TRoundCell();

        /// <summary>
        /// <c>[Self+0x638]</c> — successful steps since the last reset. Once it
        /// passes 100 the mover gives up for this pass (see
        /// <see cref="ResetNativeMoveProgress"/>).
        /// </summary>
        private int _nativeMoveStepCount;

        /// <summary>Direction deltas, native table <c>*(0x7D6BE0)</c> = 0x007D4ADC,
        /// stride 8 with dX at +0 and dY at +4. The numbering matches
        /// <c>Grobal2.DR_*</c> value for value, so no translation is needed, and odd
        /// indices are the diagonals — which is what <c>0x68B965 and eax,0x80000001</c>
        /// selects for the two-cell look-ahead.</summary>
        private static readonly int[] NativeDirDX = { 0, +1, +1, +1, 0, -1, -1, -1 };

        /// <summary>Y half of the table above.</summary>
        private static readonly int[] NativeDirDY = { -1, -1, 0, +1, +1, +1, 0, -1 };

        /// <summary>
        /// <c>sub_68B838(Self, edx=X, ecx=Y)</c> — move one step toward (X, Y).
        ///
        /// <code>
        ///   68B869  cmp [Self+0x638],0x64 / jle    ; > 100 -> 0x68AAA4 and give up
        ///   68B893  [Self+0x634].Clear             ; candidates are per pass
        ///   68B8A6  dir = 0x7682D8(X, Y)
        ///   68B8AF  sub eax,8 / jae 0x68BA06       ; unsigned >= 8 -> no direction
        ///   68B8C0  bl = (0x76B4A4(X, Y) > 1)      ; Chebyshev; run when farther
        ///   68B8D6  if 0x68BEC0(dir) then          ; candidate refused
        ///   68B8EA      dir = 0x68BF3C(dir) ; bl = 0   ; re-pick, and force a walk
        ///   68B8FA  if dir is in range:
        ///   68B92A      [Self+0x630].Add(currX+dX, currY+dY, W)
        ///           else if [Self+0x634].Count >= 1:
        ///   68B956      dir = [Self+0x634][Random(Count)].A ; bl = 0
        ///   68B961  if bl and dir is diagonal and a diagonal step would land on the
        ///           destination row or column, bl = 0     ; 68B965..68B9BC
        ///   68B9C6  if bl and not [vmt+0x0C0]() then bl = 0
        ///   68B9DB  if bl then run = 0x68BD28(dir)
        ///   68B9F0  if not run then walk = 0x68BD70(dir)
        ///   68B9F9  if run or walk then [Self+0x638]++
        /// </code>
        ///
        /// The <c>[ebp-0x14]</c> state variable (1..5) and the two Delphi frames at
        /// <c>0x68BA10</c>/<c>0x68BA7C</c> are diagnostics: on an exception the
        /// handler formats that number into a log line
        /// (<c>0x40C89C</c> int→string, <c>0x405890</c> concat, <c>0x79DF74</c> log).
        /// Nothing there is observable, so none of it is reproduced.
        /// </summary>
        internal void NativeMoveTowardXY(int destX, int destY)
        {
            // 0x68B869: the stuck escape. Native compares > 100 (jle skips), clears
            // the visited ring and zeroes the counter, and does NOT move this pass.
            if (_nativeMoveStepCount > 100)
            {
                ResetNativeMoveProgress();
                return;
            }

            if (m_PEnvir == null)
            {
                return;
            }

            // 0x68B893: candidates are rebuilt every pass.
            _nativeCandidateCells.Clear();

            // 0x68B8A6 then 0x68B8AF `sub eax,8 / jae`: an unsigned compare, so any
            // direction outside 0..7 aborts the whole move.
            var dir = NativeHeroDirectionTo(destX, destY);
            if ((uint)dir >= 8u)
            {
                return;
            }

            // 0x68B8C0/0x68B8C8: `seta` on Chebyshev distance > 1 — the run/walk
            // selector. Every later refusal clears it, so a hero that had to divert
            // always walks rather than runs.
            var far = NativeGridDistance(m_nCurrX, m_nCurrY, destX, destY) > 1;

            // 0x68B8D6: true means "this direction is unusable".
            if (NativeHeroCandidateRefused(dir))
            {
                dir = NativeHeroRepickDirection(dir);
                far = false;
            }

            if ((uint)dir < 8u)
            {
                // 0x68B901..0x68B92A: remember the cell this direction leads to, so
                // the walk does not come back to it. The W word native passes here is
                // the literal 0 pushed at 0x68B901.
                var markX = (ushort)(m_nCurrX + NativeDirDX[dir]);
                var markY = (ushort)(m_nCurrY + NativeDirDY[dir]);
                _nativeVisitedCells.Add(markX, markY, 0);
            }
            else if (_nativeCandidateCells.Count >= 1)
            {
                // 0x68B931..0x68B95F: no direction survived, so fall back to one of
                // the walkable candidates collected while probing — chosen at random
                // (0x68B946 Random(count)) — and walk rather than run.
                var pick = M2Share.RandomNumber.Random(_nativeCandidateCells.Count);
                _nativeCandidateCells.GetByIndex(pick, out _, out _, out var pickDir, out _);
                dir = pickDir;
                far = false;
            }

            // 0x68B961..0x68B9BC: only for a diagonal (`and eax,0x80000001` isolates
            // dir mod 2 == 1). Native doubles both sides of the comparison, which is
            // pure codegen; the test is whether a single diagonal step already lands
            // on the destination's column or row, and if so the two-cell run is
            // dropped down to a walk.
            if (far && (uint)dir < 8u && (dir & 1) == 1)
            {
                var oneX = m_nCurrX + NativeDirDX[dir];
                var oneY = m_nCurrY + NativeDirDY[dir];
                if (oneX == destX || oneY == destY)
                {
                    far = false;
                }
            }

            // 0x68B9C6: `call [vmt+0x0C0]` = 0x774348, which is not a step at all but
            // a predicate — `!HasState(0x43) && !HasState(0x0D)`. Failing it only
            // demotes the run to a walk.
            if (far && !NativeCanRunGate())
            {
                far = false;
            }

            var moved = false;
            if (far)
            {
                // 0x68B9E1 -> 0x68BD28: two cells.
                moved = NativeHeroRunTwoCells(dir);
            }
            if (!moved)
            {
                // 0x68B9F0 -> 0x68BD70: one cell. Native always tries this when the
                // run did not happen, including when `far` was false to begin with.
                moved = NativeHeroWalkOneCell(dir);
            }

            if (moved)
            {
                // 0x68B9F9 `inc [Self+0x638]`.
                _nativeMoveStepCount++;
            }
        }

        /// <summary>
        /// <c>sub_68AAA4</c> — five effective instructions: clear the visited ring
        /// (<c>0x68AAB0</c> calls <c>sub_6863E0</c>) and zero the step counter
        /// (<c>0x68AAB7</c>). No movement happens on this pass, which is what breaks
        /// a hero out of a dead end.
        /// </summary>
        private void ResetNativeMoveProgress()
        {
            _nativeVisitedCells.Clear();
            _nativeMoveStepCount = 0;
        }

        /// <summary>
        /// <c>sub_7682D8(Self, edx=X, ecx=Y)</c> — the direction toward (X, Y).
        ///
        /// <b>This is deliberately NOT <see cref="M2Share.GetNextDirection"/>.</b>
        /// That method ports <c>sub_764A90</c>, a different function, and the two
        /// disagree on the same-cell case: 764A90 answers <c>DR_UP</c>, while this one
        /// leaves its seed value untouched and answers <c>DR_DOWN</c>
        /// (<c>0x7682DA mov esi,4</c>, and the fall-through at <c>0x768342 jle</c>
        /// never reaches the <c>xor esi,esi</c>). Reusing GetNextDirection here would
        /// have introduced that divergence silently.
        ///
        /// <code>
        ///   esi = 4 (DR_DOWN)                       ; the seed, and the same-cell answer
        ///   if X > currX:   esi = 2 (DR_RIGHT)
        ///                   if Y > currY: 3   elif currY > Y: 1
        ///   elif currX > X: esi = 6 (DR_LEFT)
        ///                   if Y > currY: 5   elif currY > Y: 7
        ///   else:           if Y > currY: 4   elif currY > Y: 0 (DR_UP)
        /// </code>
        /// </summary>
        private int NativeHeroDirectionTo(int destX, int destY)
        {
            if (destX > m_nCurrX)
            {
                if (destY > m_nCurrY) return Grobal2.DR_DOWNRIGHT;
                if (m_nCurrY > destY) return Grobal2.DR_UPRIGHT;
                return Grobal2.DR_RIGHT;
            }

            if (m_nCurrX > destX)
            {
                if (destY > m_nCurrY) return Grobal2.DR_DOWNLEFT;
                if (m_nCurrY > destY) return Grobal2.DR_UPLEFT;
                return Grobal2.DR_LEFT;
            }

            if (m_nCurrY > destY) return Grobal2.DR_UP;
            return Grobal2.DR_DOWN;   // includes the same-cell case
        }

        /// <summary>
        /// <c>sub_68BEC0(Self, edx=dir)</c> — true when this direction must not be
        /// used.
        /// <code>
        ///   68BECC  candidate = (currX + dX[dir], currY + dY[dir])
        ///   68BEEF  through = byte[Self+0x3FE]          ; occupancy-through cache
        ///   68BF00  if not CanWalk(Envir, candidate, through): return TRUE
        ///   68BF19  [Self+0x634].Add(candidate, dir, 0) ; keep it as a fallback
        ///   68BF28  return [Self+0x630].IndexOf(candidate) >= 0   ; already visited
        /// </code>
        /// Note the ordering: a walkable cell is recorded as a candidate <i>before</i>
        /// the visited test, so cells rejected only for having been visited are still
        /// available to the random fallback. That is the whole mechanism by which a
        /// boxed-in hero keeps moving.
        /// </summary>
        private bool NativeHeroCandidateRefused(int dir)
        {
            if ((uint)dir >= 8u)
            {
                return true;
            }

            var cx = m_nCurrX + NativeDirDX[dir];
            var cy = m_nCurrY + NativeDirDY[dir];

            // 0x68BF00 passes byte[Self+0x3FE] as the flag; CanWalk is the port of
            // sub_777EF8 (TEnvironment.CanWalk, self-named at 0x778030).
            if (!m_PEnvir.CanWalk(cx, cy, m_boThroughOccupancyCache))
            {
                return true;
            }

            _nativeCandidateCells.Add((ushort)cx, (ushort)cy, (ushort)dir, 0);
            return _nativeVisitedCells.IndexOf((ushort)cx, (ushort)cy) >= 0;
        }

        /// <summary>
        /// <c>sub_68BF3C(Self, edx=dir)</c> — pick a replacement direction by
        /// spreading outward alternately, returning -1 if nothing works.
        /// <code>
        ///   for offset = 1, 2, 3:
        ///       cand = (dir + offset) mod 8        ; 0x68BF5B and ebx,0x80000007
        ///       if not refused(cand): return cand
        ///       cand = (dir + 8 - offset) mod 8    ; 0x68BF7F
        ///       if not refused(cand): return cand
        ///   cand = (dir + 4) mod 8                 ; 0x68BFAC, straight back
        ///   if not refused(cand): return cand
        ///   return -1                              ; 0x68BF4A seeds it
        /// </code>
        /// The loop stops at <c>esi == 4</c> (<c>0x68BFA4</c>), so offsets 1..3 only;
        /// the opposite direction is tried once, separately, afterwards.
        /// </summary>
        private int NativeHeroRepickDirection(int dir)
        {
            for (var offset = 1; offset < 4; offset++)
            {
                var candidate = (dir + offset) & 7;
                if (!NativeHeroCandidateRefused(candidate))
                {
                    return candidate;
                }

                candidate = (dir + 8 - offset) & 7;
                if (!NativeHeroCandidateRefused(candidate))
                {
                    return candidate;
                }
            }

            var opposite = (dir + 4) & 7;
            if (!NativeHeroCandidateRefused(opposite))
            {
                return opposite;
            }

            return -1;
        }

        /// <summary>
        /// <c>sub_774348</c>, reached through VMT slot <c>+0x0C0</c> — the same
        /// pointer on TCreature, TAnimal, THumanKind, TPlayer, THeroAct and TPsNpc,
        /// i.e. nobody overrides it. Read whole it is 22 bytes and is a predicate,
        /// not a step:
        /// <code>
        ///   77434E  mov dl,0x43 / call 0x772960 / test al,al / jne -> FALSE
        ///   77435B  mov dl,0x0D / call 0x772960 / test al,al / je  -> TRUE
        ///   774368  xor eax,eax / ret        ; either state set
        ///   77436D  mov al,1    / ret
        /// </code>
        ///
        /// <c>sub_772960</c> is the <b>112-bit presence bitset at obj+0x168</b>, not
        /// the timed-ability list:
        /// <code>
        ///   772960  cmp dl,0x6F / ja 0x77296F      ; ids above 0x6F answer false
        ///   772965  and edx,0x7F
        ///   772968  bt dword [eax+0x168], edx
        ///   77296F  setb al / ret
        /// </code>
        /// with <c>bts</c> at <c>0x77299B</c> and <c>btr</c> at <c>0x7729B9</c> as its
        /// write sides. The port already models it exactly as
        /// <see cref="TBaseObject.HasNativeActiveState"/>, whose
        /// <c>NativeActiveStateMax = 111</c> is the same 0x6F ceiling.
        ///
        /// This method first used <c>HasTimedAbility</c>, which was simply the wrong
        /// structure — that is the timed-ability linked list <c>sub_76B4D0</c> unlinks
        /// from, a different mechanism with different contents. The gate was therefore
        /// consulting state that has nothing to do with the two ids native checks.
        /// </summary>
        private bool NativeCanRunGate()
        {
            return !HasNativeActiveState(0x43) && !HasNativeActiveState(0x0D);
        }

        /// <summary>
        /// <c>sub_68BD28(Self, edx=dir)</c> — the two-cell run.
        /// <code>
        ///   68BD31  0x76B4D0(Self, 0x17)      ; drop state 0x17 first
        ///   68BD3E  ok = 0x76756C(Self, dir)  ; the run mover
        ///   68BD57  if ok: 0x76BECC(0x3C, 0xA, 0) ; then 0x76BEC8(1)
        /// </code>
        /// </summary>
        private bool NativeHeroRunTwoCells(int dir)
        {
            if ((uint)dir >= 8u)
            {
                return false;
            }

            // 0x68BD31 `mov dl,0x17 / call 0x76B4D0` — sub_76B4D0 is the thin shell
            // over sub_7731C0 that unlinks a timed-state node, i.e. the port's
            // RemoveNativeMovementTimedState. The run mover clears 0x17 again on its
            // own success arm (0x767638), so native really does clear twice.
            RemoveNativeMovementTimedState(0x17);

            // 0x767597 `mov byte [Self+0x154], al` — the facing is written BEFORE any
            // walkability test, so a refused run still turns the hero.
            m_btDirection = (byte)dir;

            // 0x7675A4..0x7675D3: the MID cell is probed first, and a failure aborts
            // the whole run. Skipping this was a real defect — without it the hero
            // could cross a blocked cell in a two-cell run. The player's RunTo already
            // models the same probe as Envirnoment.NativeCanRunOccupancy, and both
            // native probe sites read obj+0x3FE for boIgnoreOccupancy
            // (0x7675BA and 0x767601).
            var midX = m_nCurrX + NativeDirDX[dir];
            var midY = m_nCurrY + NativeDirDY[dir];
            if (!m_PEnvir.NativeCanRunOccupancy(midX, midY, m_boThroughOccupancyCache))
            {
                return false;
            }

            // 0x7675E0: only now is the two-cell destination formed.
            var targetX = m_nCurrX + NativeDirDX[dir] * 2;
            var targetY = m_nCurrY + NativeDirDY[dir] * 2;

            // 0x76761C -> sub_7797CC, the move primitive, = MoveToMovingObjectForRun.
            if (m_PEnvir.MoveToMovingObjectForRun(
                    m_nCurrX, m_nCurrY, this, targetX, targetY,
                    m_boThroughOccupancyCache) <= 0)
            {
                return false;
            }

            // 0x68BD57 `sub_76BECC(edx=0x3C, ecx=0xA, push 0)` then
            // 0x68BD63 `sub_76BEC8(edx=1)`, both on the run's success arm only.
            // These are NOT diagnostics: sub_76BECC decrements the two recovery
            // budgets at obj+0x10 / obj+0x14 and floors the second at zero
            // (0x76BEE1 `sub [eax+0x10],edx` / `sub [eax+0x14],ecx`, then
            // 0x76BEE7 `cmp [eax+0x14],0 / jge` / `mov [eax+0x14],0`), and
            // sub_76BEC8 is the single instruction `sub byte [eax+0x20], dl`.
            // HeroObject.NativeCrossMoon.CompleteNativeWarHeroAction already models
            // this exact pair for sub_76BECC(30,100,0) + sub_76BEC8(2), so this
            // follows that precedent rather than introducing a second shape.
            ApplyNativeMoveRecoveryCost(0x3C, 0x0A);
            DecreaseHealthSpellRecoveryStep(1);
            return true;
        }

        /// <summary>
        /// <c>sub_76BECC(Self, edx=health, ecx=spell, [ebp+8]=reset)</c>, <c>ret 4</c>.
        /// The movers always pass <c>0</c> for the reset flag, so only the decrement
        /// arm is reachable from here:
        /// <code>
        ///   76BECF  cmp byte [ebp+8],0 / je 0x76BEE1      ; reset flag
        ///   76BED5  [eax+0x10]=0 ; [eax+0x14]=0           ; (reset arm, unused here)
        ///   76BEE1  sub [eax+0x10], edx                   ; health budget
        ///   76BEE4  sub [eax+0x14], ecx                   ; spell budget
        ///   76BEE7  cmp [eax+0x14],0 / jge / mov [eax+0x14],0   ; spell floors at 0
        /// </code>
        /// Note the asymmetry: only the spell budget is clamped; the health budget is
        /// allowed to go negative, which
        /// <c>NativeCrossMoon.CompleteNativeWarHeroAction</c> already records.
        /// </summary>
        private void ApplyNativeMoveRecoveryCost(int health, int spell)
        {
            m_nHealthTick = unchecked(m_nHealthTick - health);
            m_nSpellTick = HUtil32._MAX(0, unchecked(m_nSpellTick - spell));
        }

        /// <summary>
        /// <c>sub_68BD70(Self, edx=dir, ecx=0)</c> — the one-cell walk.
        /// <code>
        ///   68BD7C  0x76B4D0(Self, 0x17)
        ///   68BD8D  ok = call [vmt+0x030](dir, 0)   ; 0x741224, the humanoid mover
        ///   68BDA1  if ok: 0x76BECC(0xA, 0, 0)
        /// </code>
        /// The slot it calls is exactly the one whose override moved onto THumanKind,
        /// so the hero's walk and the player's walk are the same primitive — as they
        /// are natively.
        /// </summary>
        private bool NativeHeroWalkOneCell(int dir)
        {
            if ((uint)dir >= 8u)
            {
                return false;
            }

            // 0x68BD7C, the same pre-clear the run path does.
            RemoveNativeMovementTimedState(0x17);
            if (!WalkTo((byte)dir, false))
            {
                return false;
            }

            // 0x68BDA1 `sub_76BECC(edx=0xA, ecx=0, push 0)` on the success arm only.
            // The walk costs health budget alone — no spell component and no
            // sub_76BEC8 call, unlike the run path.
            ApplyNativeMoveRecoveryCost(0x0A, 0);
            return true;
        }
    }
}
