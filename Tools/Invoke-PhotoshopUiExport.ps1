[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PsdPath,
    [Parameter(Mandatory = $true)][string]$OutputFolder,
    [string]$ExporterPath
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PhotoshopAutomationCommon.ps1')
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$psdFull = [IO.Path]::GetFullPath($PsdPath)
$outputFull = [IO.Path]::GetFullPath($OutputFolder).TrimEnd('\')
if (-not $outputFull.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputFolder must stay inside the PS_To_Unity_v2 workspace.'
}
if (-not (Test-Path -LiteralPath $psdFull -PathType Leaf)) {
    throw "PSD not found: $psdFull"
}
if ([string]::IsNullOrWhiteSpace($ExporterPath)) {
    $ExporterPath = Join-Path $workspace 'PhotoshopExporter\PhotoshopUiPackageExporter.jsx'
}
$exporterFull = [IO.Path]::GetFullPath($ExporterPath)
if (-not (Test-Path -LiteralPath $exporterFull -PathType Leaf)) {
    throw "Exporter not found: $exporterFull"
}

$imageFolder = Join-Path $outputFull 'Images'
$layoutPath = Join-Path $outputFull 'layout.json'
$resultPath = Join-Path $outputFull 'photoshop_result.json'
$runId = [Guid]::NewGuid().ToString('N')
$sourceFingerprintBefore = Get-PhotoshopFileSha256 $psdFull
$options = @{
    imageFolder = $imageFolder
    layoutJsonFile = $layoutPath
    ignoreHiddenLayers = $true
    skipReferenceLayers = $true
    useExportCache = $true
    useFastLayerDuplicate = $true
    useUnityAtlasStructure = $false
    atlasLanguage = 'Base'
    textLayerOutput = 'tmp'
    selectedTextLayersAsImages = $false
    autoRouteNonSourceHanFonts = $false
    automationRunId = $runId
} | ConvertTo-Json -Compress

$script = [IO.File]::ReadAllText($exporterFull, [Text.Encoding]::UTF8)
$script = $script -replace '^#target photoshop\s*', ''
$psdJson = $psdFull | ConvertTo-Json -Compress
$javascript = @"
var __inputFile = new File($psdJson);
for (var __openIndex = 0; __openIndex < app.documents.length; __openIndex++) {
    var __openPath = "";
    try { __openPath = app.documents[__openIndex].fullName.fsName; } catch (__unsavedDocument) {}
    if (String(__openPath).toLowerCase() === String(__inputFile.fsName).toLowerCase()) {
        throw new Error("Close the target PSD before export so unsaved edits remain untouched.");
    }
}
var __toolDoc = app.open(__inputFile);
$.global.PS_TO_UNITY_V2_AUTOMATION_OPTIONS = $options;
try {
$script
} finally {
    try { __toolDoc.close(SaveOptions.DONOTSAVECHANGES); } catch (__closeError) {}
}
"@
$javascript += @'

var __r = $.global.PS_TO_UNITY_V2_AUTOMATION_RESULT;
var __status = __r ? (__r.error ? "ERROR|" + __r.runId + "|" + __r.error : "PASS|" + __r.runId) : "MISSING";
$.global.PS_TO_UNITY_V2_AUTOMATION_RESULT = null;
__status;
'@

New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
foreach ($stalePath in @($layoutPath, $resultPath)) {
    if (Test-Path -LiteralPath $stalePath) {
        Remove-Item -LiteralPath $stalePath -Force
    }
}

$photoshop = New-PhotoshopComApplication
try {
    $automationStatus = [string]$photoshop.DoJavaScript($javascript)
} finally {
    if ($null -ne $photoshop) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($photoshop)
    }
}
if ($automationStatus -cne "PASS|$runId") {
    throw "Photoshop exporter failed or returned another run: $automationStatus"
}
if (-not (Test-Path -LiteralPath $layoutPath -PathType Leaf)) {
    throw 'Photoshop exporter did not produce layout.json.'
}
$sourceFingerprintAfter = Get-PhotoshopFileSha256 $psdFull
if ($sourceFingerprintAfter -ne $sourceFingerprintBefore) {
    throw 'PSD changed while Photoshop export was running; discard this package and retry.'
}

$layout = [IO.File]::ReadAllText($layoutPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$imageHashes = [ordered]@{}
foreach ($imageFile in (Get-ChildItem -LiteralPath $imageFolder -File -Filter '*.png' | Sort-Object Name)) {
    $imageHashes[$imageFile.Name] = Get-PhotoshopFileSha256 $imageFile.FullName
}
$result = [ordered]@{
    status = 'PASS'
    runId = $runId
    psdSha256 = $sourceFingerprintAfter
    layoutSha256 = Get-PhotoshopFileSha256 $layoutPath
    imageSha256 = $imageHashes
    layoutJsonPath = $layoutPath
    imageFolder = $imageFolder
    imageCount = @(Get-ChildItem -LiteralPath $imageFolder -File -Filter '*.png').Count
    schemaVersion = $layout.schemaVersion
    canvasWidth = $layout.canvas.width
    canvasHeight = $layout.canvas.height
}
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8
$result | ConvertTo-Json -Compress
