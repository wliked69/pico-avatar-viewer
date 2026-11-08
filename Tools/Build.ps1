param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [switch]$ChecksOnly
)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Unity editor executable not found.' }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'App/Packages/manifest.json'))) { throw 'Run Tools/Setup.ps1 first.' }
if (-not (Test-Path -LiteralPath (Join-Path $repoRoot 'App/Assets/AvatarViewer/Fonts/ViewerFont.ttf'))) { throw 'Add a licensed Chinese TTF as App/Assets/AvatarViewer/Fonts/ViewerFont.ttf.' }
$buildFolder = Join-Path $repoRoot 'Builds'
New-Item -ItemType Directory -Path $buildFolder -Force | Out-Null
$method = if ($ChecksOnly) { 'BuildViewer.Prepare' } else { 'BuildViewer.Build' }
$project = Join-Path $repoRoot 'App'
$log = Join-Path $buildFolder 'build.log'
$buildProcess = Start-Process -FilePath $UnityEditor -ArgumentList @('-batchmode','-quit','-projectPath',('"'+$project+'"'),'-buildTarget','Android','-executeMethod',$method,'-logFile',('"'+$log+'"')) -WindowStyle Hidden -PassThru -Wait
if ($buildProcess.ExitCode -ne 0) { throw "Unity exited with $($buildProcess.ExitCode). See $log" }
Write-Output $(if ($ChecksOnly) { 'Source checks passed.' } else { Join-Path $buildFolder 'AvatarViewer.apk' })
