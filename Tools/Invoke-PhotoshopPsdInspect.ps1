[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PsdPath,
    [Parameter(Mandatory = $true)][string]$OutputFile,
    [string]$InspectorPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PhotoshopAutomationCommon.ps1')
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
if (Test-Path -LiteralPath $outputFull) {
    Remove-Item -LiteralPath $outputFull -Force
}
$runId = [Guid]::NewGuid().ToString('N')
$sourceFingerprint = Get-PhotoshopFileSha256 $psdFull
$source = [IO.File]::ReadAllText($inspectorFull, [Text.Encoding]::UTF8)
$source = $source -replace '^#target photoshop\s*', ''
$options = @{
    psdPath = $psdFull
    outputFile = $outputFull
    runId = $runId
    sourceFingerprint = $sourceFingerprint
} | ConvertTo-Json -Compress
$javascript = "$.global.PS_TO_UNITY_V2_INSPECT_OPTIONS = $options;`n$source"
$javascript += @'

var __r = $.global.PS_TO_UNITY_V2_INSPECT_RESULT;
var __status = __r ? (__r.error ? "ERROR|" + __r.runId + "|" + __r.error : "PASS|" + __r.runId) : "MISSING";
$.global.PS_TO_UNITY_V2_INSPECT_RESULT = null;
__status;
'@

$photoshop = New-PhotoshopComApplication
try {
    $automationStatus = [string]$photoshop.DoJavaScript($javascript)
} finally {
    if ($null -ne $photoshop) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($photoshop)
    }
}
if ($automationStatus -cne "PASS|$runId") {
    throw "Photoshop inspector failed or returned another run: $automationStatus"
}
if (-not (Test-Path -LiteralPath $outputFull -PathType Leaf)) {
    throw 'Photoshop inspector did not produce its JSON output.'
}

$inspection = [IO.File]::ReadAllText($outputFull, [Text.Encoding]::UTF8) | ConvertFrom-Json
if ($inspection.sourceFingerprint -ne $sourceFingerprint) {
    throw 'Photoshop inspector output fingerprint does not match the source PSD.'
}
$sourceFingerprintAfter = Get-PhotoshopFileSha256 $psdFull
if ($sourceFingerprintAfter -ne $sourceFingerprint) {
    Remove-Item -LiteralPath $outputFull -Force
    throw 'PSD changed during inspection; discarded stale evidence.'
}
[ordered]@{
    status = 'PASS'
    runId = $runId
    sourceFingerprint = $sourceFingerprint
    outputFile = $outputFull
    document = $inspection.document.name
    layerCount = $inspection.summary.layerCount
    groupCount = $inspection.summary.groupCount
    textCount = $inspection.summary.textCount
} | ConvertTo-Json -Compress
