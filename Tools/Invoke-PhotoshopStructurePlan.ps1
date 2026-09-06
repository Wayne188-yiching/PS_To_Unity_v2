[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PsdPath,
    [Parameter(Mandatory = $true)][string]$PlanFile,
    [Parameter(Mandatory = $true)][string]$InspectionFile,
    [Parameter(Mandatory = $true)][string]$ReportFile,
    [ValidateSet('Validate', 'Apply')][string]$Mode = 'Validate',
    [string]$BackupPath,
    [string]$ApplierPath
)

$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')).TrimEnd('\')
$psdFull = [IO.Path]::GetFullPath($PsdPath)
$planFull = [IO.Path]::GetFullPath($PlanFile)
$inspectionFull = [IO.Path]::GetFullPath($InspectionFile)
$reportFull = [IO.Path]::GetFullPath($ReportFile)
foreach ($required in @($psdFull, $planFull, $inspectionFull)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "Required file not found: $required"
    }
}
if (-not $planFull.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $inspectionFull.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase) -or
    -not $reportFull.StartsWith($workspace + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'PlanFile, InspectionFile, and ReportFile must stay inside the workspace.'
}
if ([string]::IsNullOrWhiteSpace($ApplierPath)) {
    $ApplierPath = Join-Path $workspace 'PhotoshopExporter\PhotoshopStructurePlanApplier.jsx'
}
$applierFull = [IO.Path]::GetFullPath($ApplierPath)
if (-not (Test-Path -LiteralPath $applierFull -PathType Leaf)) {
    throw "Structure applier not found: $applierFull"
}

$python = Join-Path $workspace 'AgentOrchestrator\.venv\Scripts\python.exe'
if (-not (Test-Path -LiteralPath $python -PathType Leaf)) {
    throw "AgentOrchestrator Python not found: $python"
}
$agentRoot = Join-Path $workspace 'AgentOrchestrator'
Push-Location $agentRoot
try {
    $validationText = & $python -m ps_to_unity_agents.psd_structure_plan $planFull $inspectionFull 2>&1
    $validationExit = $LASTEXITCODE
} finally {
    Pop-Location
}
$validation = ($validationText -join "`n") | ConvertFrom-Json
if ($validationExit -ne 0 -or $validation.status -eq 'BLOCKED') {
    throw "Structure plan validation failed: $($validationText -join ' ')"
}
if ($Mode -eq 'Apply' -and -not $validation.readyToApply) {
    throw 'Structure plan is valid but has not been approved.'
}

$planObject = [IO.File]::ReadAllText($planFull, [Text.Encoding]::UTF8) | ConvertFrom-Json
$planJson = $planObject | ConvertTo-Json -Compress -Depth 40
$source = [IO.File]::ReadAllText($applierFull, [Text.Encoding]::ASCII)
$source = $source -replace '^#target photoshop\s*', ''
New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($reportFull)) -Force | Out-Null

function Invoke-PlanInPhotoshop([string]$RunMode) {
    $optionsJson = @{ mode = $RunMode.ToLowerInvariant(); outputFile = $reportFull } | ConvertTo-Json -Compress
    $javascript = "$.global.PS_TO_UNITY_V2_STRUCTURE_PLAN = $planJson;`n$.global.PS_TO_UNITY_V2_STRUCTURE_OPTIONS = $optionsJson;`n$source"
    if (Test-Path -LiteralPath $reportFull) {
        [IO.File]::Delete($reportFull)
    }
    $photoshop = New-Object -ComObject Photoshop.Application
    $opened = $false
    try {
        for ($index = 1; $index -le $photoshop.Documents.Count; $index++) {
            $document = $photoshop.Documents.Item($index)
            try {
                if ([IO.Path]::GetFullPath([string]$document.FullName).Equals($psdFull, [StringComparison]::OrdinalIgnoreCase)) {
                    throw 'Close the target PSD in Photoshop before running the structure tool.'
                }
            } catch [System.Management.Automation.RuntimeException] {
                throw
            } catch {}
        }
        $photoshop.Open($psdFull) | Out-Null
        $opened = $true
        $photoshop.DoJavaScript($javascript) | Out-Null
    } finally {
        if ($opened) {
            try { $photoshop.DoJavaScript('app.activeDocument.close(SaveOptions.DONOTSAVECHANGES);') | Out-Null } catch {}
        }
        if ($null -ne $photoshop) {
            [void][Runtime.InteropServices.Marshal]::ReleaseComObject($photoshop)
        }
    }
    if (-not (Test-Path -LiteralPath $reportFull -PathType Leaf)) {
        throw 'Photoshop structure tool did not produce a report.'
    }
    return [IO.File]::ReadAllText($reportFull, [Text.Encoding]::ASCII) | ConvertFrom-Json
}

$preflight = Invoke-PlanInPhotoshop 'Validate'
if ($preflight.status -ne 'PASS') {
    throw "Photoshop preflight failed: $($preflight.errors -join '; ')"
}

$backupFull = $null
if ($Mode -eq 'Apply') {
    if ([string]::IsNullOrWhiteSpace($BackupPath)) {
        $folder = [IO.Path]::GetDirectoryName($psdFull)
        $name = [IO.Path]::GetFileNameWithoutExtension($psdFull)
        $stamp = Get-Date -Format 'yyyyMMdd_HHmmss'
        $backupFull = Join-Path $folder "$name.pre_structure_$stamp.psd"
    } else {
        $backupFull = [IO.Path]::GetFullPath($BackupPath)
    }
    $sourceFolder = [IO.Path]::GetDirectoryName($psdFull).TrimEnd('\')
    $backupFolder = [IO.Path]::GetDirectoryName($backupFull).TrimEnd('\')
    if (-not $backupFolder.Equals($sourceFolder, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'BackupPath must stay beside the source PSD.'
    }
    if (Test-Path -LiteralPath $backupFull) {
        throw "Backup already exists: $backupFull"
    }
    Copy-Item -LiteralPath $psdFull -Destination $backupFull
    $result = Invoke-PlanInPhotoshop 'Apply'
    if ($result.status -ne 'PASS' -or -not $result.saved) {
        throw "Photoshop apply failed. Original backup: $backupFull. Errors: $($result.errors -join '; ')"
    }
} else {
    $result = $preflight
}

[ordered]@{
    status = $result.status
    mode = $Mode
    beforeLayerCount = $result.beforeLayerCount
    afterLayerCount = $result.afterLayerCount
    createdGroupCount = $result.createdGroupCount
    renamedCount = $result.renamedCount
    movedCount = $result.movedCount
    saved = $result.saved
    backupPath = $backupFull
    reportFile = $reportFull
} | ConvertTo-Json -Compress
