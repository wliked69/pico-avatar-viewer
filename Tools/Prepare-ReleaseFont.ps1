$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Get-Content -LiteralPath (Join-Path $repoRoot 'ThirdPartyNotices/NotoSansSC-source.json') -Raw | ConvertFrom-Json
$fontPath = Join-Path $repoRoot 'App/Assets/AvatarViewer/Fonts/ViewerFont.ttf'
$downloadPath = $fontPath + '.download'
Invoke-WebRequest -Uri $source.fontUrl -OutFile $downloadPath
if ((Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.fontSha256) {
    Remove-Item -LiteralPath $downloadPath
    throw 'Downloaded font checksum does not match the pinned source.'
}
Move-Item -LiteralPath $downloadPath -Destination $fontPath -Force
$metaPath = $fontPath + '.meta'
if (Test-Path -LiteralPath $metaPath) {
    $meta = Get-Content -LiteralPath $metaPath -Raw
    $meta = [regex]::Replace($meta,'(?m)^  fontNames:\r?\n(?:  - .*\r?\n)*',"  fontNames:`n  - Noto Sans SC`n")
    [IO.File]::WriteAllText($metaPath,$meta,[Text.UTF8Encoding]::new($false))
}
$licensePath = Join-Path $repoRoot 'ThirdPartyNotices/NotoSansSC-OFL.txt'
if ((Get-FileHash -LiteralPath $licensePath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $source.licenseSha256) { throw 'Font license checksum mismatch.' }
$embeddedNotices = Join-Path $repoRoot 'App/Assets/StreamingAssets/ThirdParty'
New-Item -ItemType Directory -Path $embeddedNotices -Force | Out-Null
Copy-Item -LiteralPath $licensePath -Destination (Join-Path $embeddedNotices 'NotoSansSC-OFL.txt') -Force
Write-Output 'Pinned Noto Sans SC font prepared; OFL notice will be included inside the APK.'
