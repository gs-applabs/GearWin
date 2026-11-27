# Fetch pinned, package-specific PNG assets at build time only. Never fetch at application runtime.
param([string]$ReportDir)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$assets = Join-Path $root 'wpf/OptimizerWpf/Assets'
$catalog = @(Get-Content (Join-Path $assets 'recommended-icons.json') -Raw | ConvertFrom-Json)
$out = Join-Path $assets 'recommended-icons'
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $ReportDir) { $ReportDir = Join-Path $out 'report' }
New-Item -ItemType Directory -Force $ReportDir | Out-Null
Add-Type -AssemblyName System.Drawing.Common
$rows = [System.Collections.Generic.List[object]]::new()
$seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($item in $catalog) {
    if ($item.id -notmatch '^[A-Za-z0-9._+-]{1,120}$' -or -not $seen.Add($item.id)) { throw "Invalid or duplicate ID: $($item.id)" }
    if ($item.url -notmatch '^https://raw\.githubusercontent\.com/[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+/[a-f0-9]{40}/[A-Za-z0-9_./+-]+\.png$' -or $item.url.Contains('/../')) { throw "Unpinned or invalid source: $($item.id)" }
    $filename = $item.id.ToLowerInvariant() + '.png'
    $dest = Join-Path $out $filename
    $tmp = Join-Path $out ([Guid]::NewGuid().ToString('N') + '.download')
    try {
        # The committed source URL and SHA-256 (when published upstream) define identity.
        # Always verify bytes before including them in the binary.
        Invoke-WebRequest -Uri $item.url -OutFile $tmp -MaximumRedirection 0 -TimeoutSec 30
        $length = (Get-Item $tmp).Length
        if ($length -lt 32 -or $length -gt 4MB) { throw "Unexpected asset size: $($item.id)" }
        $hash = (Get-FileHash $tmp -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($item.sha256 -and $hash -ne $item.sha256) { throw "Hash mismatch: $($item.id)" }
        $bytes = [IO.File]::ReadAllBytes($tmp)
        if ([Convert]::ToHexString($bytes[0..7]) -ne '89504E470D0A1A0A') { throw "Not a PNG: $($item.id)" }
        $width = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes,16))
        $height = [System.Net.IPAddress]::NetworkToHostOrder([BitConverter]::ToInt32($bytes,20))
        if ($width -lt 16 -or $height -lt 16 -or $width -gt 4096 -or $height -gt 4096) { throw "Unexpected image dimensions: $($item.id)" }
        $image = [System.Drawing.Image]::FromFile($tmp)
        try {
            if ($image.Width -ne $width -or $image.Height -ne $height) { throw "Image header mismatch: $($item.id)" }
            # Normalize at build time (transparent square, no stretching). The original
            # asset hash is recorded separately from the normalized embedded PNG.
            $bitmap = [System.Drawing.Bitmap]::new(96,96,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
            try {
                $g = [System.Drawing.Graphics]::FromImage($bitmap)
                try {
                    $g.Clear([System.Drawing.Color]::Transparent)
                    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $scale = [Math]::Min(96.0/$width,96.0/$height)
                    $w = [int][Math]::Round($width*$scale)
                    $h = [int][Math]::Round($height*$scale)
                    $g.DrawImage($image,[System.Drawing.Rectangle]::new([int]((96-$w)/2),[int]((96-$h)/2),$w,$h))
                } finally { $g.Dispose() }
                $bitmap.Save($dest,[System.Drawing.Imaging.ImageFormat]::Png)
            } finally { $bitmap.Dispose() }
        } finally { $image.Dispose() }
        $rows.Add([pscustomobject]@{id=$item.id;name=$item.name;source=$item.url;sourceSha256=$hash;publishedHashMatched=[bool]$item.sha256;width=$width;height=$height;embeddedSha256=(Get-FileHash $dest -Algorithm SHA256).Hash.ToLowerInvariant()})
        Write-Host "ICON OK $($item.id) $width x $height $hash"
    } finally { if (Test-Path $tmp) { Remove-Item -LiteralPath $tmp -Force } }
}
$rows | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $ReportDir 'icon-audit.json') -Encoding utf8
# Contact sheet for human review; not a claim of visual identity certification.
$columns = 5
$cellW = 240; $cellH = 116
$sheet = [System.Drawing.Bitmap]::new($columns*$cellW,[int][Math]::Ceiling($rows.Count/$columns)*$cellH)
try {
    $g = [System.Drawing.Graphics]::FromImage($sheet)
    $font = [System.Drawing.Font]::new('Segoe UI',10)
    try {
        $g.Clear([System.Drawing.Color]::White)
        for ($i=0; $i -lt $rows.Count; $i++) {
            $x = ($i % $columns)*$cellW
            $y = [int][Math]::Floor($i/$columns)*$cellH
            $image = [System.Drawing.Image]::FromFile((Join-Path $out ($rows[$i].id.ToLowerInvariant()+'.png')))
            try { $g.DrawImage($image,[System.Drawing.Rectangle]::new($x+90,$y+6,60,60)) } finally { $image.Dispose() }
            $g.DrawString($rows[$i].name,$font,[System.Drawing.Brushes]::Black,[System.Drawing.RectangleF]::new($x+4,$y+70,$cellW-8,43))
        }
        $sheet.Save((Join-Path $ReportDir 'icon-contact-sheet.png'),[System.Drawing.Imaging.ImageFormat]::Png)
    } finally { $font.Dispose(); $g.Dispose() }
} finally { $sheet.Dispose() }
Write-Host "ICON CATALOG: $($rows.Count)/$($catalog.Count) assets validated and normalized."
