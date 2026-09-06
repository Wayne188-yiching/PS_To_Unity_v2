[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PsdPath,
    [Parameter(Mandatory = $true)][string]$OutputFolder,
    [string]$ExporterPath
)

$ErrorActionPreference = 'Stop'
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
} | ConvertTo-Json -Compress

$script = [IO.File]::ReadAllText($exporterFull, [Text.Encoding]::UTF8)
$script = $script -replace '^#target photoshop\s*', ''
$psdJson = $psdFull | ConvertTo-Json -Compress
$javascript = @"
var __toolDoc = app.open(new File($psdJson));
$.global.PS_TO_UNITY_V2_AUTOMATION_OPTIONS = $options;
try {
$script
} finally {
    try { __toolDoc.close(SaveOptions.DONOTSAVECHANGES); } catch (__closeError) {}
}
"@

$photoshop = New-Object -ComObject Photoshop.Application
$photoshop.DoJavaScript($javascript) | Out-Null
if (-not (Test-Path -LiteralPath $layoutPath -PathType Leaf)) {
    throw 'Photoshop exporter did not produce layout.json.'
}

$layout = [IO.File]::ReadAllText($layoutPath, [Text.Encoding]::UTF8) | ConvertFrom-Json
$result = [ordered]@{
    status = 'PASS'
    layoutJsonPath = $layoutPath
    imageFolder = $imageFolder
    imageCount = @(Get-ChildItem -LiteralPath $imageFolder -File -Filter '*.png').Count
    schemaVersion = $layout.schemaVersion
    canvasWidth = $layout.canvas.width
    canvasHeight = $layout.canvas.height
}
New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
$result | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $resultPath -Encoding utf8
$result | ConvertTo-Json -Compress
