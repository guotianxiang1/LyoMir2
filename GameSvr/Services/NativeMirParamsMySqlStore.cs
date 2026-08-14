using MySql.Data.MySqlClient;
using SystemModule;

namespace GameSvr.Services
{
    /// <summary>
    /// MirParams 写侧 — native sub_699310 @0x699310 via sub_724E48 @0x724E48 (cl=1)。
    /// SGRP-30 ident 247 → sub_65805C → sub_699310。
    /// DBSvr 侧无等价 handler（UNKNOWN）；GameSvr 直连 gamedata.MirParams。
    /// </summary>
    public interface INativeMirParamsStore
    {
        bool TryWriteGlobalValue(int paramNo, int index, int value, out string error);
    }

    public sealed class NativeMirParamsMySqlStore : INativeMirParamsStore
    {
        // 0x6994F4 len=1  `g` — column prefix g1..g50
        // 0x699544 `insert into mirparams (ParamNo, ` + gN + `) values (...);`
        // 0x69959C `update MirParams set ` + gN + ` = ... where paramNo = ...;`
        internal const uint ExecuteScriptEa = 0x00724E48;
        internal const uint WriterEa = 0x00699310;

        public bool TryWriteGlobalValue(int paramNo, int index, int value,
            out string error)
        {
            error = string.Empty;
            if (index < NativeMirrorIdent247.NativeIndexMin
                || index > NativeMirrorIdent247.NativeIndexMax)
            {
                error = "MirParams index out of native range 1..50";
                return false;
            }

            var column = "g" + index;
            var connectionString = M2Share.g_Config?.sConnctionString;
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                error = "MirParams store: sConnctionString unavailable";
                return false;
            }

            try
            {
                using var conn = new MySqlConnection(connectionString);
                conn.Open();
                using (var probe = new MySqlCommand(
                        "SELECT paramNo FROM gamedata.MirParams WHERE paramNo=@p LIMIT 1", conn))
                {
                    probe.Parameters.AddWithValue("@p", paramNo);
                    var exists = probe.ExecuteScalar() != null;
                    if (!exists)
                    {
                        using var insert = new MySqlCommand(
                            "INSERT INTO gamedata.MirParams (ParamNo, `" + column
                            + "`) VALUES (@p, @v)", conn);
                        insert.Parameters.AddWithValue("@p", paramNo);
                        insert.Parameters.AddWithValue("@v", value);
                        insert.ExecuteNonQuery();
                        return true;
                    }
                }

                using var update = new MySqlCommand(
                    "UPDATE gamedata.MirParams SET `" + column
                    + "`=@v WHERE paramNo=@p", conn);
                update.Parameters.AddWithValue("@p", paramNo);
                update.Parameters.AddWithValue("@v", value);
                update.ExecuteNonQuery();
                return true;
            }
            catch (MySqlException ex)
            {
                error = ex.Message;
                M2Share.ErrorMessage("[MirParams] SQL failed: " + ex.Message);
                return false;
            }
        }
    }
}
