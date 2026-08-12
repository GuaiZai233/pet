using System.IO.Pipes;
using System.Text;
using GuaiMiao.Infrastructure;

namespace GuaiMiao.Services;

internal sealed class SingleInstanceService : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cancellation = new();
    private Task? _serverTask;

    private SingleInstanceService(Mutex mutex, string pipeName)
    {
        _mutex = mutex;
        _pipeName = pipeName;
    }

    public event Action<string>? CommandReceived;

    public static SingleInstanceService? TryAcquire(string? mutexName = null, string? pipeName = null)
    {
        mutexName ??= AppInfo.MutexName;
        pipeName ??= AppInfo.PipeName;
        var mutex = new Mutex(true, mutexName, out var createdNew);
        if (createdNew)
            return new SingleInstanceService(mutex, pipeName);
        mutex.Dispose();
        return null;
    }

    public void StartServer() => _serverTask = Task.Run(ServerLoopAsync);

    public static async Task<bool> SendAsync(string command, int timeoutMs = 1200, string? pipeName = null)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", pipeName ?? AppInfo.PipeName, PipeDirection.Out,
                PipeOptions.Asynchronous);
            using var timeout = new CancellationTokenSource(timeoutMs);
            await pipe.ConnectAsync(timeout.Token).ConfigureAwait(false);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: false)
            {
                AutoFlush = true
            };
            await writer.WriteLineAsync(command).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private async Task ServerLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(_cancellation.Token).ConfigureAwait(false);
                using var reader = new StreamReader(server, Encoding.UTF8, leaveOpen: false);
                var command = await reader.ReadLineAsync(_cancellation.Token).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(command))
                    CommandReceived?.Invoke(command);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LocalLog.Warn("pipe-server", ex);
                await Task.Delay(250, _cancellation.Token).ConfigureAwait(false);
            }
        }
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try { _serverTask?.Wait(500); } catch { }
        try { _mutex?.ReleaseMutex(); } catch { }
        _mutex?.Dispose();
        _cancellation.Dispose();
    }
}
