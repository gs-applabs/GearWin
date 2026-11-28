# GearWin shell actions. File and directory names are data, never PowerShell source.
# Installed under the protected application directory. No execution-policy bypass.
param(
    [Parameter(Mandatory=$true)][ValidateSet('Open','Own')][string]$Action,
    [Parameter(Mandatory=$true)][string]$Target
)
$ErrorActionPreference = 'Stop'
try {
    if ($Target -notmatch '^[A-Za-z]:\\' -or $Target.Contains('"')) { throw 'Select a local filesystem path.' }
    $item = Get-Item -LiteralPath $Target -Force
    if ($Action -eq 'Open') {
        if (-not $item.PSIsContainer -or $item.PSProvider.Name -ne 'FileSystem') { throw 'Select a directory.' }
        Set-Location -LiteralPath $item.FullName
        return
    }
    # Ownership only, never recursive ACL grants or system-root ownership.
    $full = $item.FullName
    if ($item.PSProvider.Name -ne 'FileSystem' -or $full -eq [IO.Path]::GetPathRoot($full)) {
        throw 'Drive roots are not supported.'
    }
    $drive = [IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if ($drive.DriveType -ne [IO.DriveType]::Fixed) { throw 'Select a file or directory on a fixed local drive.' }
    foreach ($protected in @($env:windir, $env:ProgramFiles, env:ProgramFiles(x86), $env:ProgramData)) {
        if ([string]::IsNullOrWhiteSpace($protected)) { continue }
        $root = [IO.Path]::GetFullPath($protected).TrimEnd('\')
        if ($full.Equals($root, [StringComparison]::OrdinalIgnoreCase) -or
            $full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Protected system and application directories are not supported.'
        }
    }
    for ($node = $item; $null -ne $node; ) {
        if (($node.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Reparse points are not supported.' }
        if ($node.PSIsContainer) { $node = $node.Parent } else { $node = $node.Directory }
    }
    Write-Host "Selected: $full"
    Write-Host 'This changes ownership only. It does not grant permissions or recurse into children.'
    if ((Read-Host 'Type OWN to request administrator approval') -cne 'OWN') { return }
    # Separate executable, no cmd.exe, no icacls, no PowerShell command text.
    $exe = Join-Path ([Environment]::SystemDirectory) 'takeown.exe'
    $p = Start-Process -FilePath $exe -ArgumentList @('/F', ('"' + $full + '"')) -Verb RunAs -Wait -PassThru
    if ($p.ExitCode -ne 0) { throw "takeown returned exit code $($p.ExitCode)." }
    Write-Host 'Ownership command completed. Verify the owner in Windows security properties.'
} catch {
    Write-Host $_.Exception.Message
}
