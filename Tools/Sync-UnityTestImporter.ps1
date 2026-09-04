[CmdletBinding()]
param(
    [string]$UnityProjectPath = 'TestProjects\PS_To_Unity_AgentTest'
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$testRoot = [IO.Path]::GetFullPath((Join-Path $workspace 'TestProjects')).TrimEnd('\')
$project = if ([IO.Path]::IsPathRooted($UnityProjectPath)) {
    [IO.Path]::GetFullPath($UnityProjectPath).TrimEnd('\')
} else {
    [IO.Path]::GetFullPath((Join-Path $workspace $UnityProjectPath)).TrimEnd('\')
}
if (-not $project.StartsWith($testRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'UnityProjectPath must stay inside TestProjects.'
}
$source = Join-Path $workspace 'Assets\Editor\PhotoshopUiImporter'
$destination = Join-Path $project 'Assets\Editor\PhotoshopUiImporter'
New-Item -ItemType Directory -Path $destination -Force | Out-Null
Get-ChildItem -LiteralPath $source -Force | Copy-Item -Destination $destination -Recurse -Force
Write-Output "SYNCED_IMPORTER=$destination"
