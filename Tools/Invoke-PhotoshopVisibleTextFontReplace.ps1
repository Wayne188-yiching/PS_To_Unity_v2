[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PsdPath,
    [Parameter(Mandatory = $true)][string]$TargetFont,
    [Parameter(Mandatory = $true)][string]$ReportFile
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$psdFull = [IO.Path]::GetFullPath($PsdPath)
$reportFull = [IO.Path]::GetFullPath($ReportFile)
if (-not (Test-Path -LiteralPath $psdFull -PathType Leaf)) {
    throw "PSD not found: $psdFull"
}
if (-not $reportFull.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ReportFile must stay inside the PS_To_Unity_v2 workspace.'
}

$folder = [IO.Path]::GetDirectoryName($psdFull)
$name = [IO.Path]::GetFileNameWithoutExtension($psdFull)
$stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
$backupFull = Join-Path $folder "$name.pre_font_$stamp.psd"
$photoshop = New-Object -ComObject Photoshop.Application

function Write-Report([hashtable]$Report) {
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportFull)) | Out-Null
    [IO.File]::WriteAllText(
        $reportFull,
        ($Report | ConvertTo-Json -Depth 5),
        [Text.UTF8Encoding]::new($false))
}

try {
    for ($index = 1; $index -le $photoshop.Documents.Count; $index++) {
        try {
            $openPath = [IO.Path]::GetFullPath([string]$photoshop.Documents.Item($index).FullName)
            if ($openPath.Equals($psdFull, [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Close the target PSD before font replacement.'
            }
        } catch [System.Management.Automation.RuntimeException] {
            throw
        } catch {}
    }

    Copy-Item -LiteralPath $psdFull -Destination $backupFull
    $psdJson = $psdFull | ConvertTo-Json -Compress
    $fontJson = $TargetFont | ConvertTo-Json -Compress
    $javascript = @"
var __fontTargetFile = new File($psdJson);
var __fontName = $fontJson;
var __fontFound = false;
for (var __fi = 0; __fi < app.fonts.length; __fi++) {
    if (String(app.fonts[__fi].postScriptName) === __fontName) { __fontFound = true; break; }
}
if (!__fontFound) { throw new Error('Photoshop font not found: ' + __fontName); }
function __walkVisibleText(__container, __parentVisible, __apply) {
    var __counts = { visible: 0, changed: 0, already: 0, mismatch: 0 };
    for (var __i = 0; __i < __container.layers.length; __i++) {
        var __layer = __container.layers[__i];
        var __effectiveVisible = __parentVisible && Boolean(__layer.visible);
        if (!__effectiveVisible) { continue; }
        if (__layer.typename === 'LayerSet') {
            var __nested = __walkVisibleText(__layer, true, __apply);
            __counts.visible += __nested.visible;
            __counts.changed += __nested.changed;
            __counts.already += __nested.already;
            __counts.mismatch += __nested.mismatch;
        } else if (__layer.kind === LayerKind.TEXT) {
            __counts.visible++;
            var __before = String(__layer.textItem.font || '');
            if (__apply && __before !== __fontName) {
                __layer.textItem.font = __fontName;
                __counts.changed++;
            } else if (__before === __fontName) {
                __counts.already++;
            }
            if (String(__layer.textItem.font || '') !== __fontName) { __counts.mismatch++; }
        }
    }
    return __counts;
}
var __doc = app.open(__fontTargetFile);
var __applyCounts;
try {
    __applyCounts = __walkVisibleText(__doc, true, true);
    if (__applyCounts.mismatch !== 0) { throw new Error('Font verification failed before save: ' + __applyCounts.mismatch); }
    __doc.save();
} finally {
    try { __doc.close(SaveOptions.DONOTSAVECHANGES); } catch (__closeError) {}
}
var __verifyDoc = app.open(__fontTargetFile);
var __verifyCounts;
try {
    __verifyCounts = __walkVisibleText(__verifyDoc, true, false);
} finally {
    try { __verifyDoc.close(SaveOptions.DONOTSAVECHANGES); } catch (__verifyCloseError) {}
}
if (__verifyCounts.mismatch !== 0) { throw new Error('Font verification failed after reopen: ' + __verifyCounts.mismatch); }
[__applyCounts.visible, __applyCounts.changed, __applyCounts.already, __verifyCounts.mismatch].join('|');
"@

    $raw = [string]$photoshop.DoJavaScript($javascript)
    $parts = $raw -split '\|'
    if ($parts.Count -ne 4) {
        throw "Unexpected Photoshop result: $raw"
    }
    $report = [ordered]@{
        status = 'PASS'
        psdPath = $psdFull
        backupPath = $backupFull
        targetFont = $TargetFont
        visibleTextLayerCount = [int]$parts[0]
        changedTextLayerCount = [int]$parts[1]
        alreadyTargetFontCount = [int]$parts[2]
        mismatchAfterReopen = [int]$parts[3]
        completedAt = (Get-Date).ToString('o')
    }
    Write-Report $report
    $report | ConvertTo-Json -Compress
} catch {
    $failure = [ordered]@{
        status = 'BLOCKED'
        psdPath = $psdFull
        backupPath = if (Test-Path -LiteralPath $backupFull -PathType Leaf) { $backupFull } else { $null }
        targetFont = $TargetFont
        error = $_.Exception.Message
        completedAt = (Get-Date).ToString('o')
    }
    Write-Report $failure
    throw
} finally {
    if ($null -ne $photoshop) {
        [void][Runtime.InteropServices.Marshal]::ReleaseComObject($photoshop)
    }
}
