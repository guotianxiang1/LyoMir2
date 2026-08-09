using System.Text;
using System.Threading.Channels;

namespace LoginGate.Core;

internal sealed class LoginGateFileLogWriter : IAsyncDisposable
{
    private readonly string _directory;
    private readonly Channel<LoginGateLogEntry> _channel;
    private readonly Task _writerTask;

    public LoginGateFileLogWriter(string configDirectory)
    {
        _directory = Path.Combine(configDirectory, "logs");
        _channel = Channel.CreateBounded<LoginGateLogEntry>(
            new BoundedChannelOptions(2048)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropOldest
            });
        _writerTask = Task.Run(WriteLoopAsync);
    }

    public bool TryWrite(LoginGateLogEntry entry) => _channel.Writer.TryWrite(entry);

    private async Task WriteLoopAsync()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = Encoding.GetEncoding(936);
        StreamWriter? writer = null;
        try
        {
            await foreach (var entry in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (writer == null)
                {
                    Directory.CreateDirectory(_directory);
                    var path = Path.Combine(_directory, "LoginRecords.txt");
                    var stream = new FileStream(path, FileMode.Append, FileAccess.Write,
                        FileShare.Read, 4096, FileOptions.Asynchronous);
                    writer = new StreamWriter(stream, encoding) { AutoFlush = true };
                }
                await writer.WriteLineAsync(
                    $"{entry.Timestamp:yyyy-MM-dd HH:mm} [{entry.Level}] {entry.Message}")
                    .ConfigureAwait(false);
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        finally
        {
            if (writer != null) await writer.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();
        await _writerTask.ConfigureAwait(false);
    }
}
