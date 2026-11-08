param([Parameter(Mandatory=$true)][string]$PicoSdkPath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$sdkRoot = (Resolve-Path -LiteralPath $PicoSdkPath).Path
if (-not (Test-Path -LiteralPath (Join-Path $sdkRoot 'package.json'))) {
    $sdkRoot = Join-Path $sdkRoot 'XR'
}
$sdkPackage = Join-Path $sdkRoot 'package.json'
if (-not (Test-Path -LiteralPath $sdkPackage)) { throw 'Provide the PICO Unity SDK XR package directory or its parent.' }
$sdkInfo = Get-Content -LiteralPath $sdkPackage -Raw | ConvertFrom-Json
if ($sdkInfo.name -ne 'com.bytedance.pico.xr') { throw 'This is not the PICO Unity XR SDK package.' }
$template = Get-Content -LiteralPath (Join-Path $repoRoot 'App/Packages/manifest.template.json') -Raw | ConvertFrom-Json
$template.dependencies.'com.bytedance.pico.xr' = 'file:' + $sdkRoot.Replace('\','/')
$manifest = Join-Path $repoRoot 'App/Packages/manifest.json'
[IO.File]::WriteAllText($manifest,($template | ConvertTo-Json -Depth 20),[Text.UTF8Encoding]::new($false))
Write-Output "Configured local PICO XR SDK $($sdkInfo.version). The generated manifest is Git-ignored."
Write-Output 'Add a licensed Chinese TTF as App/Assets/AvatarViewer/Fonts/ViewerFont.ttf before building.'
