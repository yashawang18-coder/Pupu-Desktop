$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $root "Directory.Build.props"

if (-not (Test-Path $propsPath)) {
    throw "缺少统一版本文件：$propsPath"
}

[xml]$props = Get-Content $propsPath -Raw
$version = [string](@($props.Project.PropertyGroup.PupuVersion) |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1)

if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Directory.Build.props 中的 PupuVersion 无效：$version"
}

Write-Output $version
