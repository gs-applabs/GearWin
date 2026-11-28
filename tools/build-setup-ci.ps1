# Builds GearWin-Setup-<version>.exe on a CI runner: self-contained publish + Inno Setup (see ci-install-inno.ps1). Output: installer\Output\.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    # Release builds require the pinned application icon assets; never fetch them in the running app.
    & "$PSScriptRoot/prepare-recommended-icons.ps1"
    dotnet publish wpf/OptimizerWpf/OptimizerWpf.csproj -c Release -r win-x64 --self-contained true -o wpf/OptimizerWpf/publish/win-x64
    if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }
    dotnet publish migration/GearWin.Migration.Cli -c Release -r win-x64 --self-contained true -o wpf/OptimizerWpf/publish/win-x64/migration
    if ($LASTEXITCODE -ne 0) { throw 'Migration CLI publish failed' }
    if (!(Test-Path (Join-Path $root 'wpf/OptimizerWpf/publish/win-x64/Shell/ShellContextHelper.ps1'))) { throw 'Shell context helper missing from publish output' }
    $recoveryRoot = Join-Path $root 'wpf/OptimizerWpf/publish/win-x64/Recovery'
    foreach ($name in @('gearwin_clone.py', 'partclone_adapter.py', 'README.md')) {
        if (-not (Test-Path (Join-Path $recoveryRoot $name))) {
            throw "Recovery kit missing from publish output: $name"
        }
    }
    $migration = Join-Path $root 'wpf/OptimizerWpf/publish/win-x64/migration/GearWin.Migration.Cli.exe'
    foreach ($command in @('inspect', 'inventory')) {
        $result = & $migration $command
        if ($LASTEXITCODE -ne 0) { throw "Migration CLI $command failed" }
        $null = ($result -join "`n") | ConvertFrom-Json -ErrorAction Stop
    }
    Copy-Item migration/example-manifest.json (Split-Path $migration)
    $result = & $migration plan migration/example-manifest.json
    if ($LASTEXITCODE -ne 0) { throw 'Migration CLI plan failed' }
    $null = ($result -join "`n") | ConvertFrom-Json -ErrorAction Stop
    $iscc = @("$env:LOCALAPPDATA\Programs", $env:ProgramFiles, ${env:ProgramFiles(x86)}) | Where-Object { $_ -and (Test-Path $_) } |
        ForEach-Object { Get-ChildItem $_ -Directory -Filter 'Inno Setup*' -ErrorAction SilentlyContinue } | Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName 'ISCC.exe' } | Where-Object { Test-Path $_ } | Select-Object -First 1
    if (-not $iscc) { throw 'ISCC.exe not found' }
    Push-Location installer
    try { & $iscc OptimizerWpf.iss; if ($LASTEXITCODE -ne 0) { throw 'ISCC failed' } } finally { Pop-Location }
    $setup = Get-ChildItem installer/Output -Filter 'GearWin-Setup-*.exe' | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    Copy-Item $setup.FullName (Join-Path $root 'installer/Output/GearWin-Setup.exe') -Force
    (Get-FileHash $setup.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  GearWin-Setup.exe' | Set-Content installer/Output/GearWin-Setup.exe.sha256
    "Built $($setup.Name)"
} finally { Pop-Location }
