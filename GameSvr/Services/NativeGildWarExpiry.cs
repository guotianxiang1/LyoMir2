using System;
using System.Collections.Generic;

namespace GameSvr.Services
{
    // GILD-28: Expiry logic for guild wars in the native (MySQL) corps system.
    //
    // Wars have a fixed duration (dwGuildWarTime, default 3 hours). The CreateTime
    // column in the gildrelation table records when the war started. When
    // CreateTime + Duration <= now, the war expires and the relation is deleted.
    //
    // This is the MySQL equivalent of AssociationManager.Run() line ~159, which
    // checks (GetTickCount()-dwWarTick) > dwWarTime on the file-based system.
    internal static class NativeGildWarExpiry
    {
        internal const byte GildHostile = 2; // Relation type for wars

        internal record ExpiredWar(long FirstGildId, long SecondGildId);

        // Scans _gildRelations and returns all wars whose creation time + duration
        // has passed. Relation type 2 (GildHostile) are wars; type 1 (GildUnion)
        // alliances never expire.
        internal static List<ExpiredWar> GetExpired(
            Dictionary<(ulong First, ulong Second), (byte Relation, DateTime CreateTime)> relations,
            DateTime now,
            int durationMs)
        {
            var expired = new List<ExpiredWar>();
            foreach (var pair in relations)
            {
                if (pair.Value.Relation != GildHostile)
                    continue; // Only wars expire, not unions
                var deadline = pair.Value.CreateTime.AddMilliseconds(durationMs);
                if (now >= deadline)
                {
                    expired.Add(new ExpiredWar(
                        unchecked((long)pair.Key.First),
                        unchecked((long)pair.Key.Second)));
                }
            }
            return expired;
        }
    }
}
