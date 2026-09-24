# Invoke-ReskinAcceptance.ps1
# Mechanical acceptance for the reskin flow (PsUiSkinApplier) on a throwaway Unity project.
#
#   -Stage build   : sync importer + harness, generate the fixture, keep a pristine copy
#   -Stage before  : run the v2.15.0 legacy applier (and legacy window folder flow) on the pristine fixture
#   -Stage after   : run the current applier (dry-run -> apply -> rollback) on the same pristine fixture
#   -Stage source  : v2.19 read-only source folder + folder to reskin (own fixture, no build needed)
#
# The Unity project must be a disposable test project. Never point this at a production project:
# the script mirrors Assets/Editor/PhotoshopUiImporter from the repo (or from -ImporterRef) into it.
# ASCII only on purpose (Windows PowerShell 5.1 reads BOM-less .ps1 as Big5 on this machine).

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][ValidateSet('build', 'before', 'after', 'source')][string]$Stage,
    [string]$OutDir,
    [string]$ImporterRef,
    [string]$UnityExe = 'C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$project = [IO.Path]::GetFullPath($ProjectPath).TrimEnd('\')
if (-not (Test-Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt'))) { throw "Not a Unity project: $project" }
if (Test-Path (Join-Path $project '.git')) { throw "Refusing to run inside a versioned project ($project). Use a disposable test project." }
if (-not $OutDir) { $OutDir = Join-Path $repo 'TestArtifacts\reskin_acceptance' }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$OutDir = [IO.Path]::GetFullPath($OutDir)

$importerDst = Join-Path $project 'Assets\Editor\PhotoshopUiImporter'
$harnessDst = Join-Path $project 'Assets\Editor\ReskinAcceptance'
$harnessSrc = Join-Path $PSScriptRoot 'Unity'
$pristine = Join-Path $project 'FixturePristine'

# Mirror only *.cs and keep the existing .meta files: deleting and regenerating metas makes Unity lose the
# MonoScript -> class binding, and ScriptableObject assets (the SkinTheme) then load as null.
function Mirror-Scripts([string]$src, [string]$dst) {
    New-Item -ItemType Directory -Force -Path $dst | Out-Null
    robocopy $src $dst '*.cs' /MIR /XF '*.meta' /NJH /NJS /NFL /NDL /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) for $src" }
    # /MIR only purges files matching the *.cs filter; drop metas whose script is gone.
    Get-ChildItem $dst -Filter '*.cs.meta' | Where-Object { -not (Test-Path ($_.FullName -replace '\.meta$', '')) } | Remove-Item -Force
}

function Sync-Importer {
    $src = Join-Path $repo 'Assets\Editor\PhotoshopUiImporter'
    if ($ImporterRef) {
        $src = Join-Path $env:TEMP ('reskin_importer_' + $ImporterRef)
        if (Test-Path $src) { Remove-Item -Recurse -Force $src }
        New-Item -ItemType Directory -Force -Path $src | Out-Null
        $files = git -C $repo ls-tree --name-only "$ImporterRef" 'Assets/Editor/PhotoshopUiImporter/'
        foreach ($f in $files) {
            if (-not $f.EndsWith('.cs')) { continue }
            # cmd redirection writes git's raw bytes; a PowerShell pipeline would re-encode them.
            $dst = Join-Path $src ([IO.Path]::GetFileName($f))
            & cmd /c "git -C `"$repo`" show `"$($ImporterRef):$f`" > `"$dst`""
            if ($LASTEXITCODE -ne 0) { throw "git show failed for $f" }
        }
    }
    Mirror-Scripts $src $importerDst
}

function Sync-Harness([bool]$withAfter, [bool]$withSource = $false) {
    $stage = Join-Path $env:TEMP 'reskin_harness_stage'
    if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($f in @('ReskinAcceptanceCommon.cs', 'ReskinAcceptanceBefore.cs', 'PsUiSkinApplierLegacy.cs')) {
        Copy-Item (Join-Path $harnessSrc $f) $stage
    }
    if ($withAfter) { Copy-Item (Join-Path $harnessSrc 'ReskinAcceptanceAfter.cs') $stage }
    if ($withSource) { Copy-Item (Join-Path $harnessSrc 'ReskinSourceFolderAcceptance.cs') $stage }
    Mirror-Scripts $stage $harnessDst
}

function Remove-LongPath([string]$path) {
    if (Test-Path -LiteralPath $path) { & cmd /c "rd /s /q `"\\?\$path`"" }
}

function Restore-Pristine {
    if (-not (Test-Path $pristine)) { throw "No pristine fixture; run -Stage build first." }
    foreach ($pair in @(@('Assets\ReskinFixture', 'ReskinFixture'), @('FixtureNewArt', 'FixtureNewArt'))) {
        $dst = Join-Path $project $pair[0]
        $src = Join-Path $pristine $pair[1]
        robocopy $src $dst /MIR /NJH /NJS /NFL /NDL /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE) for $src" }
    }
    Copy-Item (Join-Path $pristine 'ReskinFixture.meta') (Join-Path $project 'Assets\ReskinFixture.meta') -Force
    Remove-LongPath (Join-Path $project 'Logs\PsUiReskin')
}

function Invoke-Unity([string]$method, [string]$tag) {
    $log = Join-Path $OutDir "unity_$tag.log"
    $env:RESKIN_ACCEPTANCE_OUT = $OutDir
    $p = Start-Process -FilePath $UnityExe -Wait -PassThru -NoNewWindow -ArgumentList @(
        '-batchmode', '-nographics', '-projectPath', "`"$project`"", '-executeMethod', $method, '-logFile', "`"$log`"")
    $compileErrors = @(Select-String -Path $log -Pattern 'error CS\d{4}' -ErrorAction SilentlyContinue)
    Write-Host ("[{0}] exit={1} compileErrors={2} log={3}" -f $tag, $p.ExitCode, $compileErrors.Count, $log)
    if ($compileErrors.Count -gt 0) { $compileErrors | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line)" } }
    if ($p.ExitCode -ne 0 -or $compileErrors.Count -gt 0) { throw "Unity stage '$tag' failed (see $log)" }
}

switch ($Stage) {
    'build' {
        Sync-Importer
        Sync-Harness $false
        Invoke-Unity 'PsUiReskinAcceptance.ReskinFixture.BuildBatch' 'build'
        if (Test-Path $pristine) { Remove-Item -Recurse -Force $pristine }
        New-Item -ItemType Directory -Force -Path $pristine | Out-Null
        robocopy (Join-Path $project 'Assets\ReskinFixture') (Join-Path $pristine 'ReskinFixture') /MIR /NJH /NJS /NFL /NDL /NP | Out-Null
        robocopy (Join-Path $project 'FixtureNewArt') (Join-Path $pristine 'FixtureNewArt') /MIR /NJH /NJS /NFL /NDL /NP | Out-Null
        Copy-Item (Join-Path $project 'Assets\ReskinFixture.meta') (Join-Path $pristine 'ReskinFixture.meta')
        Write-Host "Pristine fixture saved to $pristine"
    }
    'before' {
        Sync-Importer
        Sync-Harness $false
        Restore-Pristine
        Invoke-Unity 'PsUiReskinAcceptance.ReskinAcceptanceBefore.RunThemeBatch' 'before_theme'
        Restore-Pristine
        Invoke-Unity 'PsUiReskinAcceptance.ReskinAcceptanceBefore.RunFolderBatch' 'before_folder'
        Restore-Pristine
    }
    'after' {
        Sync-Importer
        Sync-Harness $true
        Restore-Pristine
        Invoke-Unity 'PsUiReskinAcceptance.ReskinAcceptanceAfter.RunThemeBatch' 'after_theme'
        Restore-Pristine
        Invoke-Unity 'PsUiReskinAcceptance.ReskinAcceptanceAfter.RunFolderBatch' 'after_folder'
        Restore-Pristine
    }
    'source' {
        Sync-Importer
        Sync-Harness $true $true
        Invoke-Unity 'PsUiReskinAcceptance.ReskinSourceFolderAcceptance.RunBatch' 'source_folder'
    }
}
Write-Host "Outputs in $OutDir"
exit 0
