using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// This timeout is for metadata clients only. Never route a backup, restore,
// repair or other mutating command through this class.
internal static class RecoveryMetadataProbe
{
    internal static ProcessStartInfo CatalogStart(string drive)
    {
        var info = Create("wbadmin.exe");
        info.ArgumentList.Add("get");
        info.ArgumentList.Add("versions");
        info.ArgumentList.Add("-backupTarget:" + BackupService.NormalizeDrive(drive));
        return info;
    }

    internal static ProcessStartInfo WinReStart()
    {
        var info = Create("reagentc.exe");
        info.ArgumentList.Add("/info");
        return info;
    }

    private static ProcessStartInfo Create(string executable) => new(Path.Combine(Environment.SystemDirectory, executable))
    {
        UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
    };

    internal static Task<(bool Success, string Output)> CatalogAsync(string drive) =>
        ReadAsync(CatalogStart(drive), TimeSpan.FromSeconds(90), 32768);

    internal static Task<(bool Success, string Output)> WinReAsync() =>
        ReadAsync(WinReStart(), TimeSpan.FromSeconds(25), 4000);

    internal static ProcessStartInfo RestorePointsStart()
    {
        const string script = "$ErrorActionPreference='Stop'; $items = @(Get-ComputerRestorePoint | Sort-Object SequenceNumber -Descending | " +
            "Select-Object SequenceNumber, Description, @{N='CreationTime';E={if ($_.CreationTime -is [datetime]) {$_.CreationTime.ToString('o')} else {$_.ConvertToDateTime($_.CreationTime).ToString('o')}}}); " +
            "ConvertTo-Json -InputObject $items -Compress";
        var info = Create(@"WindowsPowerShell\v1.0\powershell.exe");
        info.Arguments = PowerShellArgs.Command(script);
        info.StandardOutputEncoding = System.Text.Encoding.UTF8;
        return info;
    }

    internal static Task<(bool Success, string Output)> RestorePointsAsync(CancellationToken ct) =>
        ReadAsync(RestorePointsStart(), TimeSpan.FromSeconds(45), 65536, ct, stdoutOnly: true);

    private static async Task<(bool Success, string Output)> ReadAsync(ProcessStartInfo start, TimeSpan deadline, int limit,
        CancellationToken ct = default, bool stdoutOnly = false)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            using var process = Process.Start(start);
            if (process is null) return (false, "Metadata query did not start.");
            using var timer = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timer.CancelAfter(deadline);
            var stdout = HealthCleanupService.ReadBoundedOutputAsync(process.StandardOutput, limit);
            var stderr = HealthCleanupService.ReadBoundedOutputAsync(process.StandardError, limit);
            var streams = Task.WhenAll(stdout, stderr);
            try
            {
                await process.WaitForExitAsync(timer.Token).ConfigureAwait(false);
                var output = await streams.WaitAsync(timer.Token).ConfigureAwait(false);
                if (stdoutOnly)
                    return process.ExitCode == 0 && !output[0].Contains("[Output truncated]", StringComparison.Ordinal)
                        ? (true, output[0].Trim()) : (false, "Restore point inventory failed or was truncated.");
                return (process.ExitCode == 0, (output[0] + "\n" + output[1]).Trim());
            }
            catch (OperationCanceledException)
            {
                // Stop only the query client, never a backup/repair service or
                // process tree. A blocked service may still need manual review.
                try { if (!process.HasExited) process.Kill(); } catch { }
                ct.ThrowIfCancellationRequested();
                return (false, "Metadata query timed out; result unknown. No backup or repair was cancelled.");
            }
            finally
            {
                // Observe a pipe failure even if the deadline already expired.
                _ = streams.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                    TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or InvalidOperationException)
        {
            return (false, ex.Message);
        }
    }
}
