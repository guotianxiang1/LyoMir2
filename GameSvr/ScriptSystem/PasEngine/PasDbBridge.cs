using System.Data;
using System.Text;
using GameSvr;

namespace GameSvr.PasEngine
{
    /// <summary>
    /// Database bridge for Pascal script ExecuteQuery / ExecuteScript operations.
    /// Implements the TMySQLDB script-level API used in PsNpcScript/M2Server.
    ///
    /// SQL operations:
    ///   ExecuteQuery(sql) -> row count
    ///   ExecuteScript(sql) -> success
    ///   PsFirst / PsNext / PsBof / PsEof - result set navigation
    ///   PsFieldName(pos) -> column name
    ///   PsFieldByName(name) -> cell value
    ///
    /// Database tables commonly accessed by scripts:
    ///   gamedata.yb_user_data       - 元宝用户数据
    ///   gamedata.SellItems          - C2C交易
    ///   gamedata.YBDealHis          - 元宝交易历史
    ///   gamedata.Gild               - 行会
    ///   gamedata.Corps              - 军团
    ///   gamedata.mailitem           - 邮件
    ///   gamedata.awardcodes         - 激活码
    ///   gamedata.SignAct            - 签到
    ///   gamedata.SimpleActRank      - 排行榜
    ///   gamedata.GloryPoint         - 荣誉积分
    ///   gamedata.LiPaoObPoint       - 离线挂机点
    ///   gamedata.User_Honor         - 荣誉值
    ///   gamedata.M2_YB_Deal_SetInfo - 元宝交易设置
    ///   gamedata.mirparams          - 全局参数
    /// </summary>
    public sealed class PasDbBridge : IDisposable
    {
        private readonly string _connectionString;
        private IDbConnection _connection;
        private IDataReader _reader;
        private DataTable _cachedTable;
        private int _currentRow;
        private bool _bof;
        private bool _eof;
        private int _recordCount;

        public PasDbBridge(string connectionString)
        {
            _connectionString = connectionString;
        }

        public bool ExecuteScript(string sql)
        {
            try
            {
                if (!EnsureConnection()) return false;
                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception)
            {
                // sub_61EE90 @0x61EF0B MainOutMessage(0x61F0E0 + sql) — 执行sql失败:
                M2Share.MainOutMessage("执行sql失败:" + sql);
                return false;
            }
        }

        public int ExecuteQuery(string sql)
        {
            ResetResultSet();
            try
            {
                if (!EnsureConnection()) return 0;

                using var cmd = _connection.CreateCommand();
                cmd.CommandText = sql;
                _cachedTable = new DataTable();
                using var adapter = new MySql.Data.MySqlClient.MySqlDataAdapter((MySql.Data.MySqlClient.MySqlCommand)cmd);
                adapter.Fill(_cachedTable);

                _recordCount = _cachedTable.Rows.Count;
                _currentRow = 0;
                _bof = _recordCount == 0;
                _eof = _recordCount == 0;
                return _recordCount;
            }
            catch (Exception)
            {
                ResetResultSet();
                M2Share.MainOutMessage("执行sql失败:" + sql);
                return 0;
            }
        }

        public void PsFirst()
        {
            if (_cachedTable == null || _recordCount == 0)
            {
                _currentRow = 0;
                _bof = true;
                _eof = true;
                return;
            }
            _currentRow = 0;
            _bof = false;
            _eof = false;
        }

        public bool PsNext()
        {
            if (_cachedTable == null || _recordCount == 0)
            {
                _bof = true;
                _eof = true;
                return false;
            }
            if (_bof) { _bof = false; _currentRow = 0; return _recordCount > 0; }
            _currentRow++;
            if (_currentRow >= _recordCount) { _eof = true; return false; }
            return true;
        }

        public bool PsBof => _bof;
        public bool PsEof => _eof || _cachedTable == null || _currentRow >= _recordCount;
        public int PsRecordCount => _recordCount;
        public int PsFieldCount => _cachedTable?.Columns.Count ?? 0;

        public string PsFieldName(int pos)
        {
            if (_cachedTable == null || pos < 1 || pos > _cachedTable.Columns.Count) return "";
            return _cachedTable.Columns[pos - 1].ColumnName;
        }

        public string PsFieldByName(string fieldName)
        {
            if (_cachedTable == null || _currentRow < 0 || _currentRow >= _recordCount) return "";
            try { return _cachedTable.Rows[_currentRow][fieldName]?.ToString() ?? ""; }
            catch { return ""; }
        }

        public string PsFieldByPos(int pos)
        {
            if (_cachedTable == null || pos < 0 || pos >= _cachedTable.Columns.Count || _currentRow >= _recordCount) return "";
            try { return _cachedTable.Rows[_currentRow][pos]?.ToString() ?? ""; }
            catch { return ""; }
        }

        private bool EnsureConnection()
        {
            if (_connection != null && _connection.State == ConnectionState.Open) return true;
            try
            {
                var connStr = _connectionString;
                if (string.IsNullOrEmpty(connStr))
                    connStr = M2Share.g_Config?.sConnctionString;
                if (string.IsNullOrEmpty(connStr))
                {
                    M2Share.MainOutMessage("[PasDB] No DB connection string configured");
                    return false;
                }
                _connection?.Dispose();
                _connection = new MySql.Data.MySqlClient.MySqlConnection(connStr);
                _connection.Open();
                return true;
            }
            catch (Exception ex)
            {
                _connection?.Dispose();
                _connection = null;
                // TMySQLDB.Connect @0x72472A MainOutMessage(0x724880 + detail) — [Error]:
                M2Share.MainOutMessage("[Error]: " + ex.Message);
                return false;
            }
        }

        private void ResetResultSet()
        {
            _reader?.Dispose();
            _reader = null;
            _cachedTable?.Dispose();
            _cachedTable = null;
            _currentRow = 0;
            _recordCount = 0;
            _bof = true;
            _eof = true;
        }

        public void Dispose()
        {
            ResetResultSet();
            _connection?.Close();
            _connection?.Dispose();
            _connection = null;
        }
    }
}
