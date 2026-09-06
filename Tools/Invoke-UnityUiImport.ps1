[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$UnityProjectPath,
    [Parameter(Mandatory = $true)][string]$RequestPath,
    [Parameter(Mandatory = $true)][string]$ResultPath,
    [string]$UnityExecutable = 'C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe'
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$testRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'TestProjects')).TrimEnd('\')
$projectFull = [IO.Path]::GetFullPath($UnityProjectPath).TrimEnd('\')
$requestFull = [IO.Path]::GetFullPath($RequestPath)
$resultFull = [IO.Path]::GetFullPath($ResultPath)
if (-not $projectFull.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'UnityProjectPath must stay inside TestProjects for the MVP.'
}
if (-not (Test-Path -LiteralPath $UnityExecutable -PathType Leaf)) {
    throw "Unity 6000.0.67f1 not found: $UnityExecutable"
}
if (-not (Test-Path -LiteralPath $requestFull -PathType Leaf)) {
    throw "Unity request not found: $requestFull"
}

$resultParent = Split-Path -Parent $resultFull
New-Item -ItemType Directory -Path $resultParent -Force | Out-Null
$logPath = Join-Path $resultParent 'unity_batch.log'
$arguments = @(
    '-batchmode',
    '-nographics',
    '-projectPath', "`"$projectFull`"",
    '-executeMethod', 'PhotoshopToUnity.EditorImporter.PhotoshopUiBatchEntryPoint.Run',
    '-psToUnityRequest', "`"$requestFull`"",
    '-psToUnityResult', "`"$resultFull`"",
    '-logFile', "`"$logPath`""
)
$process = Start-Process -FilePath $UnityExecutable -ArgumentList $arguments -Wait -PassThru -WindowStyle Hidden
$exitCode = $process.ExitCode
if (-not (Test-Path -LiteralPath $resultFull -PathType Leaf)) {
    throw "Unity did not produce a result JSON. ExitCode=$exitCode Log=$logPath"
}
Get-Content -LiteralPath $resultFull -Raw
if ($exitCode -ne 0) { exit $exitCode }
