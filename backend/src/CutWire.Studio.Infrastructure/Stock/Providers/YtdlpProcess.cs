using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace CutWire.Studio.Infrastructure.Stock.Providers;

public sealed record ProcessSpec(
    string FileName,
    IReadOnlyList<string> Args,
    TimeSpan Timeout,
    int StdoutLimit,
    int StderrLimit,
    Action<string>? OnStdoutLine = null);

public sealed record ProcessOutput(int ExitCode, byte[] Stdout, string Stderr, bool StdoutTooLarge, bool TimedOut);

/// <summary>Seam between the yt-dlp runner and the operating system, so provider logic can be tested against recorded output.</summary>
public interface IProcessRunner
{
    /// <summary>Kills the whole process tree on timeout, oversized stdout or cancellation. Caller cancellation throws; the other two are reported in the result.</summary>
    Task<ProcessOutput> RunAsync(ProcessSpec spec, CancellationToken ct);
}

public sealed class SystemProcessRunner : IProcessRunner
{
    // yt-dlp can leave a child holding the pipes open after it is killed; bound how long we wait for them to drain.
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(5);

    public async Task<ProcessOutput> RunAsync(ProcessSpec spec, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(spec.FileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in spec.Args) psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();

        using var timeoutCts = new CancellationTokenSource(spec.Timeout);
        using var tooBigCts = new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token, tooBigCts.Token);

        var stdout = new CappedBuffer(spec.StdoutLimit, failClosed: true);
        var stderr = new CappedBuffer(spec.StderrLimit, failClosed: false);
        var readers = Task.WhenAll(
            Pump(process.StandardOutput.BaseStream, stdout, spec.OnStdoutLine, tooBigCts),
            Pump(process.StandardError.BaseStream, stderr, null, null));

        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException)
        {
            Kill(process);
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(KillGrace).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        await readers.WaitAsync(KillGrace).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        ct.ThrowIfCancellationRequested();

        var exited = process.HasExited;
        return new ProcessOutput(
            exited ? process.ExitCode : -1,
            stdout.Snapshot(),
            Encoding.UTF8.GetString(stderr.Snapshot()),
            stdout.Overflowed,
            timeoutCts.IsCancellationRequested && !stdout.Overflowed);
    }

    private static void Kill(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception) { }
    }

    private static async Task Pump(Stream stream, CappedBuffer buffer, Action<string>? onLine, CancellationTokenSource? tooBig)
    {
        var chunk = new byte[16 * 1024];
        var line = onLine is null ? null : new MemoryStream();
        try
        {
            int read;
            while ((read = await stream.ReadAsync(chunk)) > 0)
            {
                if (!buffer.Append(chunk, read))
                {
                    tooBig?.Cancel();
                    return;
                }
                if (line is null) continue;
                var start = 0;
                for (var i = 0; i < read; i++)
                {
                    if (chunk[i] != (byte)'\n') continue;
                    line.Write(chunk, start, i - start);
                    onLine!(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r'));
                    line.SetLength(0);
                    start = i + 1;
                }
                line.Write(chunk, start, read - start);
            }
            if (line is { Length: > 0 }) onLine!(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimEnd('\r'));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException) { }
    }

    /// <summary>Stdout fails closed once it crosses the limit; stderr keeps only its prefix and keeps draining so the process never blocks on a full pipe.</summary>
    private sealed class CappedBuffer(int max, bool failClosed)
    {
        private readonly object _gate = new();
        private readonly MemoryStream _data = new();

        public bool Overflowed { get; private set; }

        public bool Append(byte[] bytes, int count)
        {
            lock (_gate)
            {
                if (Overflowed) return false;
                if (_data.Length + count <= max)
                {
                    _data.Write(bytes, 0, count);
                    return true;
                }
                if (failClosed)
                {
                    Overflowed = true;
                    return false;
                }
                _data.Write(bytes, 0, (int)Math.Max(0, max - _data.Length));
                return true;
            }
        }

        public byte[] Snapshot()
        {
            lock (_gate) return _data.ToArray();
        }
    }
}
