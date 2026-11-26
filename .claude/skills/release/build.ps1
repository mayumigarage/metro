# Builds the Windows player through the open Unity Editor, smoke-tests it and zips it.
# Does not commit, tag or publish anything.
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version,
    [string]$OutRoot = 'Builds'
)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$project = (Resolve-Path "$PSScriptRoot\..\..\..").Path
Set-Location $project

function Invoke-Editor([string]$Command, [string[]]$Params = @()) {
    $raw = unity command --project-path $project --timeout 180 --json $Command @Params | Out-String
    $json = $raw | ConvertFrom-Json
    if (-not $json.success) { throw "Editor command '$Command' failed: $raw" }
    $json.data.result
}

$dirty = git status --porcelain -- Assets Packages ProjectSettings
if ($dirty) { throw "Uncommitted changes would end up in the build:`n$($dirty -join "`n")" }

$outDir = Join-Path ([IO.Path]::GetFullPath($OutRoot, $project)) "v$Version"
if (Test-Path $outDir) { throw "$outDir already exists. Use a new version, or remove that folder first." }

$current = (Select-String -Path ProjectSettings\ProjectSettings.asset -Pattern '^\s*bundleVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
if ($current -ne $Version) { throw "bundleVersion is $current, not $Version. Update and commit it first." }

Invoke-Editor 'script eval' @('--code', 'UnityEditor.AssetDatabase.Refresh(); return "ok";') | Out-Null
do { Start-Sleep 3; $console = Invoke-Editor 'console status' } while ($console.groundTruth.compiling)
if ($console.groundTruth.compilationFailed) { throw 'C# compilation failed. See: unity command --project-path . "console get" --level error' }

# Releases are Direct3D 11 builds: refuse to build with any other graphics API setting.
$apis = Invoke-Editor 'script eval' @('--code', 'var t = UnityEditor.BuildTarget.StandaloneWindows64; return UnityEditor.PlayerSettings.GetUseDefaultGraphicsAPIs(t) + " " + string.Join(",", UnityEditor.PlayerSettings.GetGraphicsAPIs(t));')
if ("$apis" -notmatch 'False Direct3D11(?![,\w])') { throw "Windows graphics APIs must be Direct3D11 only (auto off). Got (auto, APIs): $apis" }

$player = Join-Path $outDir 'win64'
$queued = Invoke-Editor 'build run' @('--target', 'StandaloneWindows64', '--output-path', "$player\metro.exe", '--confirm', 'true')
do {
    Start-Sleep 5
    # The Editor may not answer while it is building.
    try { $build = Invoke-Editor 'build status' } catch { $build = $null }
} until ($build.status -eq 'completed' -and $build.buildId -eq $queued.buildId)

# The build rewrites preloadedAssets and empties the HUD's dynamic font atlas; the tree was clean, so put the files back.
git checkout --quiet -- ProjectSettings/ProjectSettings.asset 'Assets/UI/MetroSign/Fonts'
if ($build.result -ne 'Succeeded') { throw "Build $($build.result): $($build.totalErrors) errors" }

$log = Join-Path $outDir 'smoke.log'
$game = Start-Process "$player\metro.exe" -PassThru -ArgumentList '-screen-fullscreen', '0', '-screen-width', '1280', '-screen-height', '720', '-logFile', "`"$log`""
Start-Sleep 8
if ($game.HasExited) { throw "The game quit on its own. See $log" }
Stop-Process -Id $game.Id -Confirm:$false
$game.WaitForExit()
$errors = Select-String -Path $log -Pattern 'Exception' | Select-Object -First 10
if ($errors) { throw "Exceptions in ${log}:`n$($errors.Line -join "`n")" }
if (-not (Select-String -Path $log -Pattern 'Direct3D 11\.' -Quiet)) { throw "The game did not start on Direct3D 11. See $log" }

$stage = Join-Path $outDir 'metro'
New-Item -ItemType Directory $stage | Out-Null
Get-ChildItem $player | Where-Object Name -notlike '*DoNotShip*' | Copy-Item -Destination $stage -Recurse
$zip = Join-Path $outDir "metro-v$Version-win64.zip"
Compress-Archive -Path $stage -DestinationPath $zip

"OK warnings=$($build.totalWarnings) commit=$(git rev-parse HEAD)"
"zip=$zip ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
