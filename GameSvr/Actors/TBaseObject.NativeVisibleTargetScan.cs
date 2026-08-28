namespace GameSvr
{
    public partial class TBaseObject
    {
        /// <summary>
        /// The visible-actor target scan shared by <c>AnimalObject.SearchTarget</c>
        /// (native <c>sub_71DA70</c>) and <c>RobotPlayObject.SearchTarget</c>.
        ///
        /// ── Why it lives here rather than being copied
        /// Cutting <c>THumanKind : AnimalObject</c> — the one edge behind 10 of the 11
        /// invented-layer violations — breaks <c>RobotPlayObject</c>, which derives
        /// from TPlayObject and calls <c>base.SearchTarget()</c>. RobotPlayObject is a
        /// port invention with no native counterpart anywhere in the image, so it is
        /// not itself a 1:1 subject, but duplicating the scan into it would create a
        /// second source of truth for a native-cited algorithm — the same defect this
        /// pass already had to delete twice (the 92 skeleton twins in <c>dd8d306d</c>,
        /// the orphan TMapEvent shell in <c>13d16c44</c>).
        ///
        /// Extracting it is clean because the scan's whole dependency surface is
        /// TCreature-level: <see cref="m_VisibleActors"/>, <see cref="m_boDeath"/>,
        /// <see cref="m_boHideMode"/>, <see cref="m_boCoolEye"/>,
        /// <see cref="m_nViewRange"/>, <see cref="m_nCurrX"/>/<see cref="m_nCurrY"/>,
        /// <see cref="IsProperTarget"/> and <see cref="SetTargetCreat"/>. It touches
        /// <b>no</b> TAnimal field — in particular not <c>m_nTargetX</c>/
        /// <c>m_nTargetY</c>, which belong to the separate 3-line
        /// <c>SetTargetXY</c> and are what RobotPlayObject's other 51 TAnimal-field
        /// references are about.
        ///
        /// The native citations stay attached to the AnimalObject override that owns
        /// them; this method is the body, not the claim. <see cref="IsProperTarget"/>
        /// is virtual, so each caller still selects targets by its own class's rule.
        /// </summary>
        /// <returns>true when a target was found and committed.</returns>
        protected bool ScanVisibleActorsForTarget()
        {
            TBaseObject nearest = null;
            var bestDistance = 999;

            for (var i = 0; i < m_VisibleActors.Count; i++)
            {
                TBaseObject candidate = m_VisibleActors[i].BaseObject;

                // MONAI-13 — the scan arm of sub_71DA70 uses sub_772DA8
                // (`mov al,[eax+0x74]; ret`), i.e. m_boDeath at +0x74, not the +0x73
                // ghost flag.
                if (candidate.m_boDeath)
                {
                    continue;
                }

                if (!IsProperTarget(candidate) ||
                    (candidate.m_boHideMode && !m_boCoolEye))
                {
                    continue;
                }

                // 战神 sub_71DA70 @0x0071DA70: the view-range box gate is a strict
                // greater-than, so actors exactly AT m_nViewRange are included and
                // only those beyond it are dropped. SPAWN-25: the port was missing
                // this range check entirely before it was restored.
                if (System.Math.Abs(m_nCurrX - candidate.m_nCurrX) > m_nViewRange ||
                    System.Math.Abs(m_nCurrY - candidate.m_nCurrY) > m_nViewRange)
                {
                    continue;
                }

                var distance = System.Math.Abs(m_nCurrX - candidate.m_nCurrX)
                               + System.Math.Abs(m_nCurrY - candidate.m_nCurrY);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    nearest = candidate;
                }
            }

            if (nearest == null)
            {
                return false;
            }

            SetTargetCreat(nearest);
            // 0071DC04  C6 45 FB 01  mov byte [ebp-5],1
            // 0071DCA8  8A 45 FB     mov al,[ebp-5]
            return true;
        }
    }
}
