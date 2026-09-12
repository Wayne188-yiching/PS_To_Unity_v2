function Get-PhotoshopFileSha256([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
    } finally {
        $stream.Dispose()
        $algorithm.Dispose()
    }
}

function New-PhotoshopComApplication {
    [CmdletBinding()]
    param()

    $candidates = @('Photoshop.Application')
    $versioned = @(Get-ChildItem -LiteralPath 'Registry::HKEY_CLASSES_ROOT' -ErrorAction SilentlyContinue |
        Where-Object { $_.PSChildName -match '^Photoshop\.Application\.\d+$' } |
        Sort-Object { [int]($_.PSChildName -replace '^Photoshop\.Application\.', '') } -Descending |
        ForEach-Object {
            $clsidKey = Join-Path $_.PSPath 'CLSID'
            if (Test-Path -LiteralPath $clsidKey) {
                $clsid = (Get-Item -LiteralPath $clsidKey).GetValue('')
                if ($clsid -and $clsid -ne '{00000000-0000-0000-0000-000000000000}') {
                    $_.PSChildName
                }
            }
        })
    $candidates += $versioned

    $failures = @()
    foreach ($progId in ($candidates | Select-Object -Unique)) {
        try {
            return New-Object -ComObject $progId -ErrorAction Stop
        }
        catch {
            $failures += "${progId}: $($_.Exception.Message)"
        }
    }
    throw "Photoshop COM Automation is unavailable. Attempts: $($failures -join ' | ')"
}
