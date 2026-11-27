using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OptimizerWpf.Services;

// Registry uninstall strings are data, never elevated shell commands.
// Only a registered, machine-installed MSI product gets a direct uninstall.
internal static class SafeUninstallService
{
    internal enum Outcome { Removed, RestartRequired, NotVerified, Failed, WindowsSettingsOpened }
    internal readonly record struct Result(Outcome State, int? ExitCode = null);
    private const uint Machine = 4;
    private const uint UnknownProduct = 1605;

    [DllImport("msi.dll", EntryPoint = "MsiGetProductInfoExW", CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern uint GetProductInfo(string product, string? userSid, uint context,
        string property, StringBuilder value, ref uint length);

    internal static string? MatchMachineMsi(string keyName, string uninstall, bool machine, bool windowsInstaller)
    {
        if (!machine || !windowsInstaller || !Guid.TryParseExact(keyName, "B", out var key)) return null;
        // Reject arbitrary switches, scripts, executable paths and trailing commands.
        var match = Regex.Match(uninstall ?? "", @"\A\s*msiexec(?:\.exe)?\s+/[ix]\s*(\{[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\})\s*\z",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success || !Guid.TryParseExact(match.Groups[1].Value, "B", out var product) || product != key) return null;
        return product.ToString("B").ToUpperInvariant();
    }

    private static (uint Status, string Value) Query(string product, string property)
    {
        var buffer = new StringBuilder(1024);
        uint length = (uint)buffer.Capacity;
        var status = GetProductInfo(product, null, Machine, property, buffer, ref length);
        return (status, status == 0 ? buffer.ToString() : "");
    }

    private static bool IsRegisteredProduct(InstalledApp app, string product)
    {
        var (status, name) = Query(product, "InstalledProductName");
        if (status != 0 || !string.Equals(name, app.DisplayName, StringComparison.OrdinalIgnoreCase)) return false;
        var (stateStatus, state) = Query(product, "State");
        return stateStatus == 0 && state == "5";
    }

    internal static bool OpenWindowsSettings()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("ms-settings:appsfeatures") { UseShellExecute = true });
            // The shell can service a URI without returning a new process.
            return true;
        }
        catch { return false; }
    }

    internal static async Task<Result> UninstallAsync(InstalledApp app)
    {
        // No registry-provided executable or argument is ever passed to CreateProcess.
        var product = app.MachineProductCode;
        if (product == null || !Guid.TryParseExact(product, "B", out var guid))
            return new(OpenWindowsSettings() ? Outcome.WindowsSettingsOpened : Outcome.Failed);

        using var lease = await GearWinCore.EnterAsync(CoreWork.SoftwareChange);
        try
        {
            product = guid.ToString("B").ToUpperInvariant();
            if (!IsRegisteredProduct(app, product))
                return new(OpenWindowsSettings() ? Outcome.WindowsSettingsOpened : Outcome.Failed);
            var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
            { UseShellExecute = false };
            psi.ArgumentList.Add("/x");
            psi.ArgumentList.Add(product);
            psi.ArgumentList.Add("/norestart");
            using var process = Process.Start(psi);
            if (process == null) return new(Outcome.Failed);
            // Do not kill an active Windows Installer transaction on a timer.
            await process.WaitForExitAsync();
            var code = process.ExitCode;
            if (code == 3010 || code == 1641) return new(Outcome.RestartRequired, code);
            if (code != 0) return new(Outcome.Failed, code);
            var (status, _) = Query(product, "InstalledProductName");
            return new(status == UnknownProduct ? Outcome.Removed : Outcome.NotVerified, code);
        }
        catch (Exception ex) { System.Diagnostics.Trace.TraceError(ex.Message); return new(Outcome.Failed); }
    }
}
