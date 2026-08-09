using System.Text;
using MySql.Data.MySqlClient;

namespace LoginGate.Core;

public interface ILoginTicketAuthenticator
{
    ValueTask<LoginTicketAuthenticationResult> AuthenticateAsync(
        NativeLoginGateAuthRequest request, CancellationToken cancellationToken);
}

public sealed record LoginTicketAuthenticationResult(bool Success, string Account, string Error)
{
    public static LoginTicketAuthenticationResult Accepted(string account) =>
        new(true, account ?? string.Empty, string.Empty);

    public static LoginTicketAuthenticationResult Rejected(string error) =>
        new(false, string.Empty, string.IsNullOrWhiteSpace(error) ? "authentication failed" : error);
}

public sealed class RejectingLoginTicketAuthenticator : ILoginTicketAuthenticator
{
    public ValueTask<LoginTicketAuthenticationResult> AuthenticateAsync(
        NativeLoginGateAuthRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(LoginTicketAuthenticationResult.Rejected(
            "local ticket authenticator is not configured"));
}

/// <summary>
/// Optional offline ticket validation. The connection string is supplied by the
/// process environment and is never persisted by LoginGate.
/// </summary>
public sealed class MySqlLoginTicketAuthenticator : ILoginTicketAuthenticator
{
    private readonly string _connectionString;
    private readonly TimeSpan _maximumAge;

    public MySqlLoginTicketAuthenticator(string connectionString, TimeSpan? maximumAge = null)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException("A MySQL connection string is required.", nameof(connectionString));
        _connectionString = connectionString;
        _maximumAge = maximumAge ?? TimeSpan.FromMinutes(5);
    }

    public async ValueTask<LoginTicketAuthenticationResult> AuthenticateAsync(
        NativeLoginGateAuthRequest request, CancellationToken cancellationToken)
    {
        // 厂商认证屏蔽：仅凭运营方账号票据(szAuthID)鉴权，等价于原生 LoginCenter /verify
        // (SELECT ... FROM account.ticket ...)。密码/客户端IP/MAC(机器码)/安全码/密保/
        // 安全卡/SDOA 等厂商 SDK 字段一律不校验——私服场景下无意义，已屏蔽。
        if (request == null || string.IsNullOrEmpty(request.Ticket))
            return LoginTicketAuthenticationResult.Rejected("ticket is empty");

        await using var connection = new MySqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = @"SELECT t.pt_id
FROM account.ticket t
WHERE BINARY t.ticket = BINARY @ticket AND t.create_time > @expires
LIMIT 1";
        command.Parameters.AddWithValue("@ticket", request.Ticket);
        command.Parameters.AddWithValue("@expires",
            DateTimeOffset.UtcNow.Subtract(_maximumAge).ToUnixTimeSeconds());
        var value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        var account = Convert.ToString(value) ?? string.Empty;
        if (account.Length == 0)
            return LoginTicketAuthenticationResult.Rejected("ticket is invalid or expired");

        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ascii = Encoding.GetEncoding(Encoding.ASCII.CodePage,
            EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        try
        {
            if (ascii.GetByteCount(account) > 20)
                return LoginTicketAuthenticationResult.Rejected("account exceeds native slot");
        }
        catch (EncoderFallbackException)
        {
            return LoginTicketAuthenticationResult.Rejected("account is not ASCII");
        }
        return LoginTicketAuthenticationResult.Accepted(account);
    }
}

public static class LoginTicketAuthenticatorFactory
{
    public const string ConnectionStringEnvironmentVariable = "LOGINGATE_TICKET_DB";

    public static ILoginTicketAuthenticator CreateFromEnvironment()
    {
        var connectionString = Environment.GetEnvironmentVariable(
            ConnectionStringEnvironmentVariable);
        return string.IsNullOrWhiteSpace(connectionString)
            ? new RejectingLoginTicketAuthenticator()
            : new MySqlLoginTicketAuthenticator(connectionString);
    }
}
