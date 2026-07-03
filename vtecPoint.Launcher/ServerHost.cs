using System.Diagnostics;
using System.IO;
using System.Net.Sockets;

namespace vtecPoint.Launcher;

public sealed class ServerHost : IDisposable
{
    public const string Url = "http://localhost:5252";
    private const int Port = 5252;

    private Process? _process;
    private bool _startedByLauncher;

    public async Task EnsureRunningAsync(CancellationToken ct = default)
    {
        if (await IsReachableAsync(ct))
            return;

        var serverExe = FindServerExe();
        if (serverExe == null)
            throw new FileNotFoundException("ไม่พบ vtecPoint.Server.exe ในโฟลเดอร์เดียวกับโปรแกรม");

        _process = Process.Start(new ProcessStartInfo
        {
            FileName = serverExe,
            Arguments = $"--urls {Url}",
            WorkingDirectory = Path.GetDirectoryName(serverExe)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("ไม่สามารถเริ่ม vtecPoint.Server.exe ได้");

        _startedByLauncher = true;

        for (var i = 0; i < 60; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (await IsReachableAsync(ct))
                return;
            await Task.Delay(500, ct);
        }

        throw new TimeoutException("รอ server เริ่มทำงานนานเกินไป");
    }

    public void StopIfStarted()
    {
        if (!_startedByLauncher || _process == null || _process.HasExited)
            return;

        try
        {
            _process.Kill(entireProcessTree: true);
            _process.WaitForExit(3000);
        }
        catch
        {
            // ponytail: best-effort shutdown
        }
    }

    private static string? FindServerExe()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "vtecPoint.Server.exe"),
            Path.Combine(baseDir, "vtecPoint.exe"),
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task<bool> IsReachableAsync(CancellationToken ct)
    {
        try
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync("127.0.0.1", Port, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => StopIfStarted();
}
