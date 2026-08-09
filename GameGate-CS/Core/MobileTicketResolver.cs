using MySql.Data.MySqlClient;
using SystemModule;

namespace GameGate.Core;

public static class MobileTicketResolver
{
    public static string ConnStr = "server=127.0.0.1;uid=root;pwd=;database=account;charset=gbk;";

    public static void Install()
    {
        MobileTicketStore.ExternalResolver = Resolve;
    }

    private static string? Resolve(string ticket)
    {
        if (string.IsNullOrEmpty(ticket)) return null;
        try
        {
            using var conn = new MySqlConnection(ConnStr);
            conn.Open();
            using var cmd = conn.CreateCommand();
            // AccountHttpSvr stores ticket→pt_id, then normal table has pt_id→uid
            cmd.CommandText = "SELECT n.uid FROM ticket t INNER JOIN normal n ON n.pt_id=t.pt_id WHERE t.ticket=@t AND t.create_time>@exp LIMIT 1";
            cmd.Parameters.AddWithValue("@t", ticket);
            cmd.Parameters.AddWithValue("@exp", DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 300);
            using var dr = cmd.ExecuteReader();
            if (dr.Read()) {
                return dr.IsDBNull(0) ? null : dr.GetString(0);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Mobile ticket resolve failed: " + ex.Message);
        }
        return null;
    }
}
