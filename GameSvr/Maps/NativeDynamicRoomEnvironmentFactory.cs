namespace GameSvr
{
    public static class NativeDynamicRoomEnvironmentFactory
    {
        public static bool TryCreateDormantEnvironment(
            NativeDynamicRoomDefinition definition,
            string mapDirectory,
            int currentServerIndex,
            out Envirnoment environment,
            out IReadOnlyList<string> errors)
        {
            environment = null;
            var diagnostics = new List<string>();

            if (definition == null)
                diagnostics.Add("dynamic room definition is null");
            else if (definition.RoomType == -1)
                diagnostics.Add($"room {definition.RoomName}: room type -1 is invalid");
            else if (definition.RoomType is 100 or 101 or 110)
                diagnostics.Add($"room {definition.RoomName}: dynamic room type {definition.RoomType} is unsupported");
            if (currentServerIndex < 0)
                diagnostics.Add($"invalid current server index: {currentServerIndex}");
            if (string.IsNullOrWhiteSpace(mapDirectory) || !Directory.Exists(mapDirectory))
                diagnostics.Add($"map directory not found: {mapDirectory}");
            if (diagnostics.Count > 0)
            {
                errors = diagnostics;
                return false;
            }

            var mapPath = Path.Combine(mapDirectory,
                definition.MapFileName + ".map");
            var created = new Envirnoment
            {
                sMapName = definition.RoomName,
                m_sMapFileName = definition.MapFileName,
                sMapDesc = definition.Description,
                nServerIndex = currentServerIndex,
                Flag = NativeDynamicRoomFlagMapper.CreateMapFlag(definition.Flags)
            };

            if (!created.LoadMapData(mapPath))
            {
                errors = new[]
                {
                    $"room {definition.RoomName}: map load failed: {mapPath}"
                };
                return false;
            }

            created.ConfigureDormantDynamicRoom(definition.RoomName);
            // MOVE-17 @0x6BBFDC: dynamic rooms must mirror MapInfo's RUNFLAG→+0xB0
            // write (Maps.cs:421). Without this, TMapFlag.boRUNFLAG default leaked true.
            created.NativeCanRunWhileOverweight = created.Flag.boRUNFLAG;
            environment = created;
            errors = Array.Empty<string>();
            return true;
        }

    }
}
