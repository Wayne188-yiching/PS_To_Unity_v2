[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]]$SpecPath,

    [ValidateSet("brief", "qc")]
    [string]$Workflow = "brief",

    [string]$ProjectName,
    [string]$TaskTitle,
    [string]$DeliveryRoot,
    [string[]]$QcEvidencePath = @(),
    [switch]$ApproveApiTransmission,
    [switch]$LocalOnly
)

$ErrorActionPreference = "Stop"

$localSettingsPath = Join-Path $PSScriptRoot "local.settings.psd1"
$localSettings = if (Test-Path -LiteralPath $localSettingsPath) {
    Import-PowerShellDataFile -LiteralPath $localSettingsPath
} else {
    @{}
}
if ([string]::IsNullOrWhiteSpace($ProjectName)) {
    $ProjectName = if ($localSettings.ProjectName) { $localSettings.ProjectName } else { "DemoGame" }
}
if ([string]::IsNullOrWhiteSpace($DeliveryRoot)) {
    $DeliveryRoot = if ($localSettings.DeliveryRoot) {
        $localSettings.DeliveryRoot
    } else {
        Join-Path ([Environment]::GetFolderPath("MyDocuments")) "UI_Outsourcing"
    }
}

if ($ApproveApiTransmission -and $LocalOnly) {
    throw "ApproveApiTransmission 與 LocalOnly 不可同時使用。"
}

function Select-LocalFiles {
    param(
        [string]$Title,
        [string]$Filter
    )

    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.OpenFileDialog
    $dialog.Title = $Title
    $dialog.Filter = $Filter
    $dialog.Multiselect = $true
    if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
        return @()
    }
    return @($dialog.FileNames)
}

function Resolve-LocalFiles {
    param([string[]]$Paths)

    return @($Paths | ForEach-Object {
        (Resolve-Path -LiteralPath $_ -ErrorAction Stop).Path
    })
}

function Copy-CaseFiles {
    param(
        [string[]]$Paths,
        [string]$DestinationFolder
    )

    return @($Paths | ForEach-Object {
        $destination = Join-Path $DestinationFolder ([System.IO.Path]::GetFileName($_))
        if (-not [string]::Equals(
            [System.IO.Path]::GetFullPath($_),
            [System.IO.Path]::GetFullPath($destination),
            [System.StringComparison]::OrdinalIgnoreCase
        )) {
            Copy-Item -LiteralPath $_ -Destination $destination -Force
        }
        $destination
    })
}

$workspaceRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
$agentRoot = Join-Path $workspaceRoot "AgentOrchestrator"

if (-not $SpecPath -or $SpecPath.Count -eq 0) {
    $SpecPath = Select-LocalFiles `
        -Title "選擇要交給 UI 發包製作人的規格書" `
        -Filter "支援的規格書 (*.xlsx;*.md;*.txt;*.csv;*.tsv;*.json;*.html)|*.xlsx;*.md;*.txt;*.csv;*.tsv;*.json;*.html"
}
if (-not $SpecPath -or $SpecPath.Count -eq 0) {
    Write-Host "未選擇規格書，已取消。"
    exit 0
}

$resolvedSpecs = @(Resolve-LocalFiles -Paths $SpecPath)
$resolvedQcEvidence = @(Resolve-LocalFiles -Paths $QcEvidencePath)
if ([string]::IsNullOrWhiteSpace($TaskTitle)) {
    $TaskTitle = [System.IO.Path]::GetFileNameWithoutExtension($resolvedSpecs[0])
}

$approvedForApi = $ApproveApiTransmission.IsPresent
if (-not $ApproveApiTransmission -and -not $LocalOnly) {
    $answer = Read-Host "是否同意將本案件規格與整理後的內部規範傳送至 OpenAI API？輸入 Y 同意；直接按 Enter 只做本機檢查"
    $approvedForApi = $answer.Trim() -match "^(?i:y|yes|同意|是)$"
}

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$safeTitle = [regex]::Replace($TaskTitle, "[^0-9A-Za-z\p{L}_-]+", "_").Trim("_")
if ([string]::IsNullOrWhiteSpace($safeTitle)) {
    $safeTitle = "ui_outsourcing"
}
$caseId = "${safeTitle}_${timestamp}"
$taskFolder = Join-Path ([System.IO.Path]::GetFullPath($DeliveryRoot)) $safeTitle
$outputFolder = Join-Path $taskFolder (Get-Date -Format "yyyy-MM-dd")
$agentWorkFolder = Join-Path $outputFolder "_Agent工作檔"
$requestPath = Join-Path $agentWorkFolder "request.json"
[System.IO.Directory]::CreateDirectory($agentWorkFolder) | Out-Null
$resolvedSpecs = @(Copy-CaseFiles -Paths $resolvedSpecs -DestinationFolder $outputFolder)
$resolvedQcEvidence = @(Copy-CaseFiles -Paths $resolvedQcEvidence -DestinationFolder $outputFolder)

$request = [ordered]@{
    case_id = $caseId
    project_name = $ProjectName
    task_title = $TaskTitle
    workflow = $Workflow
    spec_paths = $resolvedSpecs
    qc_evidence_paths = $resolvedQcEvidence
    user_context = @(
        "請自行讀取規格書（包含後段工作表與後製流程），不要要求使用者重新填寫規格內容。",
        "完整規格會另附給外包；發包交接單使用淺顯易懂的精簡格式。",
        "分支、Unity 版本、專案路徑、Prefab 與共用資源應優先由已登錄專案自動查找。",
        "外包文件與 QC 回覆在使用者確認前一律視為草稿。"
    )
    confirmed_decisions = @()
    api_transmission_approved = $approvedForApi
    user_approved_output = $false
    output_folder = $outputFolder
}

$requestJson = $request | ConvertTo-Json -Depth 8
$utf8WithoutBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($requestPath, $requestJson, $utf8WithoutBom)

$runMode = if ($approvedForApi) { "outsource" } else { "outsource-preflight" }
$python = Join-Path $agentRoot ".venv\Scripts\python.exe"

Push-Location $agentRoot
try {
    if (Test-Path -LiteralPath $python) {
        & $python "main.py" $runMode "--request" $requestPath
    }
    elseif (Get-Command uv -ErrorAction SilentlyContinue) {
        & uv run python "main.py" $runMode "--request" $requestPath
    }
    else {
        throw "找不到 Agent 執行環境，請先在 AgentOrchestrator 執行 uv sync。"
    }
    $agentExitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($agentExitCode -ne 0) {
    throw "UI 發包製作人未完成，請查看 $outputFolder 內的檢查結果。"
}

$generatedPackage = Join-Path $outputFolder "outsourcing_package.md"
$handoffPath = Join-Path $outputFolder "外包交接單.md"
if (Test-Path -LiteralPath $generatedPackage) {
    Move-Item -LiteralPath $generatedPackage -Destination $handoffPath -Force
}
foreach ($internalName in @(
    "outsourcing_agent_result.json",
    "outsourcing_preflight.json",
    "qc_feedback_draft.md"
)) {
    $generatedInternal = Join-Path $outputFolder $internalName
    if (Test-Path -LiteralPath $generatedInternal) {
        Move-Item -LiteralPath $generatedInternal -Destination (Join-Path $agentWorkFolder $internalName) -Force
    }
}

if ($approvedForApi) {
    Write-Host "UI 發包製作人已完成草稿：$handoffPath"
    Write-Host "草稿尚未核准，請先與 Agent 討論後再交給外包。"
}
else {
    Write-Host "已完成本機檢查，資料未傳送至 OpenAI API：$agentWorkFolder\outsourcing_preflight.json"
}
