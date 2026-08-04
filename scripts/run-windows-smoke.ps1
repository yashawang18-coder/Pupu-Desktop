param(
    [Parameter(Mandatory = $true)]
    [string]$Executable,
    [int]$TimeoutSeconds = 70
)

$ErrorActionPreference = "Stop"
$executablePath = [System.IO.Path]::GetFullPath($Executable)
if (-not (Test-Path $executablePath -PathType Leaf)) {
    throw "Published Pupu executable is missing: $executablePath"
}
if ($TimeoutSeconds -lt 15 -or $TimeoutSeconds -gt 180) {
    throw "Smoke-test timeout must be between 15 and 180 seconds."
}

$dataRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("PupuDesktop-Smoke-" + [Guid]::NewGuid().ToString("N"))
$resultPath = Join-Path $dataRoot "desktop-smoke-result.json"
New-Item -ItemType Directory -Force -Path $dataRoot | Out-Null

try {
    $arguments = @(
        "--smoke-test",
        "--data-root", ('"' + $dataRoot + '"'),
        "--smoke-result", ('"' + $resultPath + '"')
    )
    $process = Start-Process `
        -FilePath $executablePath `
        -ArgumentList $arguments `
        -PassThru
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
        Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
        throw "Published Pupu smoke test timed out after $TimeoutSeconds seconds."
    }
    $process.WaitForExit()
    if (-not (Test-Path $resultPath -PathType Leaf)) {
        throw "Published Pupu did not write a smoke-test result. Exit code: $($process.ExitCode)"
    }

    $result = Get-Content $resultPath -Raw | ConvertFrom-Json
    Write-Host ("Desktop smoke steps: " + ($result.steps -join " -> "))
    Write-Host ("Mock model requests: " + $result.modelRequestCount)
    if ($process.ExitCode -ne 0 -or -not $result.passed) {
        $errors = @($result.errors) -join " | "
        throw "Published Pupu smoke test failed. Exit code: $($process.ExitCode). $errors"
    }
    if ($result.modelRequestCount -ne 1) {
        throw "Published Pupu smoke test did not complete exactly one mock model request."
    }
    Write-Host "Published Pupu desktop smoke test passed." -ForegroundColor Green
}
finally {
    Remove-Item $dataRoot -Recurse -Force -ErrorAction SilentlyContinue
}
