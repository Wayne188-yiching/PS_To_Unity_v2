[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PsdPath,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [string]$InspectorPath
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$psdFull = [IO.Path]::GetFullPath($PsdPath)
$outputFull = [IO.Path]::GetFullPath($OutputFile)
if (-not $outputFull.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputFile must stay inside the PS_To_Unity_v2 workspace.'
}
if (-not (Test-Path -LiteralPath $psdFull -PathType Leaf)) {
    throw "PSD not found: $psdFull"
}
if ([string]::IsNullOrWhiteSpace($InspectorPath)) {
    $InspectorPath = Join-Path $workspace 'PhotoshopExporter\PhotoshopPsdInspector.jsx'
}
$inspectorFull = [IO.Path]::GetFullPath($InspectorPath)
if (-not (Test-Path -LiteralPath $inspectorFull -PathType Leaf)) {
    throw "Inspector not found: $inspectorFull"
}

$outputFolder = Split-Path -Parent $outputFull
New-Item -ItemType Directory -Path $outputFolder -Force | Out-Null
$source = [IO.File]::ReadAllText($inspectorFull, [Text.Encoding]::UTF8)
$source = $source -replace '^#target photoshop\s*', ''
$options = @{ psdPath = $psdFull; outputFile = $outputFull } | ConvertTo-Json -Compress
$javascript = "$.global.PS_TO_UNITY_V2_INSPECT_OPTIONS = $options;`n$source"

$photoshop = New-Object -ComObject Photoshop.Application
try {
    $photoshop.DoJavaScript($javascript) | Out-Null
} finally {
    if ($null -ne $photoshop) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($photoshop)
    }
}
if (-not (Test-Path -LiteralPath $outputFull -PathType Leaf)) {
    throw 'Photoshop inspector did not produce its JSON output.'
}

$inspection = [IO.File]::ReadAllText($outputFull, [Text.Encoding]::UTF8) | ConvertFrom-Json
[ordered]@{
    status = 'PASS'
    outputFile = $outputFull
    document = $inspection.document.name
    layerCount = $inspection.summary.layerCount
    groupCount = $inspection.summary.groupCount
    textCount = $inspection.summary.textCount
} | ConvertTo-Json -Compress
