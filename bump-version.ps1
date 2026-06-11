# bump-version.ps1
# 從 version.json 同步版本號到所有需要顯示版本的檔案。
#
# 用法：
#   .\bump-version.ps1                  # 套用 version.json 內目前的版本到所有 surfaces
#   .\bump-version.ps1 -NewVersion 2.6.0  # 先把 version.json 改成 2.6.0，再套用
#   .\bump-version.ps1 -Check           # 只檢查、不修改，列出每個 surface 目前的版本
#
# Phase 1 引入，作為單一版本來源，避免之後再次發生版號漂移。

[CmdletBinding()]
param(
    [string]$NewVersion,
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionJsonPath = Join-Path $scriptRoot 'version.json'

if (-not (Test-Path $versionJsonPath)) {
    throw "version.json not found at $versionJsonPath"
}

# UTF-8 without BOM (Windows PowerShell 5.1 default UTF8 encoder adds BOM, which we must NOT do).
$utf8NoBom = New-Object System.Text.UTF8Encoding $false

function Read-Utf8Text {
    param([string]$Path)
    return [System.IO.File]::ReadAllText($Path, [System.Text.Encoding]::UTF8)
}

function Write-Utf8Text {
    param([string]$Path, [string]$Text)
    [System.IO.File]::WriteAllText($Path, $Text, $utf8NoBom)
}

$config = (Read-Utf8Text $versionJsonPath) | ConvertFrom-Json

if ($NewVersion) {
    if ($NewVersion -notmatch '^\d+\.\d+\.\d+$') {
        throw "NewVersion must be in form X.Y.Z (got: $NewVersion)"
    }
    Write-Host "Updating version.json: $($config.version) -> $NewVersion"
    $config.version = $NewVersion
    $config.updated = (Get-Date -Format 'yyyy-MM-dd')
    Write-Utf8Text $versionJsonPath (($config | ConvertTo-Json -Depth 6))
}

$version = $config.version
$majorMinor = ($version -split '\.')[0..1] -join '.'

Write-Host ""
Write-Host "Target version: v$version (major.minor = v$majorMinor)"
Write-Host ""

$problems = @()
$updated = 0

foreach ($s in $config.surfaces) {
    $filePath = Join-Path $scriptRoot $s.file
    $optional = $false
    if ($s.PSObject.Properties.Name -contains 'optional') { $optional = [bool]$s.optional }

    if (-not (Test-Path $filePath)) {
        if ($optional) {
            Write-Host "[skip] $($s.file) (optional, not present)"
        } else {
            $problems += "Missing: $($s.file)"
            Write-Host "[MISS] $($s.file)" -ForegroundColor Red
        }
        continue
    }

    $content = Read-Utf8Text $filePath
    $pattern = $s.pattern
    $replacement = $s.replacement.Replace('{version}', $version).Replace('{majorMinor}', $majorMinor)

    if ($content -notmatch $pattern) {
        if ($optional) {
            Write-Host "[skip] $($s.file) (optional, pattern not present)"
        } else {
            $problems += "Pattern not found in $($s.file): $pattern"
            Write-Host "[FAIL] $($s.file) — pattern not found" -ForegroundColor Red
        }
        continue
    }

    # Multi-match handling: count for reporting.
    $matchCount = ([regex]$pattern).Matches($content).Count

    if ($Check) {
        $sample = ([regex]$pattern).Matches($content)[0].Value
        Write-Host "[chk ] $($s.file) — current: $sample ($matchCount match$(if($matchCount -gt 1){'es'}))"
        continue
    }

    $newContent = [regex]::Replace($content, $pattern, $replacement)
    if ($newContent -ne $content) {
        # UTF-8 no BOM, preserve original line endings (ReadAllText keeps them in the string).
        Write-Utf8Text $filePath $newContent
        Write-Host "[OK  ] $($s.file) — patched ($matchCount match$(if($matchCount -gt 1){'es'}))" -ForegroundColor Green
        $updated++
    } else {
        Write-Host "[same] $($s.file) — already at v$version"
    }
}

Write-Host ""
if ($problems.Count -gt 0) {
    Write-Host "Issues:" -ForegroundColor Yellow
    $problems | ForEach-Object { Write-Host "  - $_" -ForegroundColor Yellow }
    exit 1
}

if ($Check) {
    Write-Host "Check complete. No files were modified."
} else {
    Write-Host "Done. $updated file(s) patched to v$version." -ForegroundColor Green
    Write-Host "Remember to: git diff, then commit."
}
