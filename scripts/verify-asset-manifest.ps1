param(
    [string]$PublishedRoot = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$assetRoot = Join-Path $root "Pupu.Desktop\Assets"
$manifestPath = Join-Path $assetRoot "pupu-assets.json"
$projectPath = Join-Path $root "Pupu.Desktop\Pupu.Desktop.csproj"

function Get-NormalizedRelativePath([string]$BasePath, [string]$FullPath) {
    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd([char[]]"\/")
    $full = [System.IO.Path]::GetFullPath($FullPath)
    if (-not $full.StartsWith($base + [System.IO.Path]::DirectorySeparatorChar,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "文件不在预期目录内：$FullPath"
    }
    return $full.Substring($base.Length + 1).Replace("\", "/")
}

function Normalize-ManifestPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "素材清单包含空文件路径。"
    }
    $normalized = $Path.Replace("\", "/")
    if ([System.IO.Path]::IsPathRooted($normalized) -or
        $normalized.StartsWith("/") -or
        ($normalized -split '/') -contains "..") {
        throw "素材路径必须位于 Assets 内：$Path"
    }
    if (-not $normalized.EndsWith(".png", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "正式运行素材必须是 PNG：$Path"
    }
    return $normalized
}

if (-not (Test-Path $manifestPath)) { throw "缺少素材清单：$manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$referenced = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

foreach ($atlas in $manifest.atlases.PSObject.Properties.Value) {
    $path = Normalize-ManifestPath $atlas.file
    if (-not $referenced.Add($path)) { throw "素材清单重复引用：$path" }
}
foreach ($group in $manifest.actionGroups.PSObject.Properties.Value) {
    if ($group.source.type -in @("spriteStrip", "singleFile") -and
        -not [string]::IsNullOrWhiteSpace($group.source.file)) {
        $path = Normalize-ManifestPath $group.source.file
        if (-not $referenced.Add($path)) { throw "素材清单重复引用：$path" }
    }
}

foreach ($path in $referenced) {
    $sourcePath = Join-Path $assetRoot $path
    if (-not (Test-Path $sourcePath -PathType Leaf)) {
        throw "素材清单引用的文件不存在：$path"
    }
}

$actual = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)
Get-ChildItem $assetRoot -Recurse -File -Filter "*.png" |
    Where-Object { $_.Name -ne "pupu-icon.png" } |
    ForEach-Object {
        $relative = Get-NormalizedRelativePath $assetRoot $_.FullName
        $null = $actual.Add($relative)
    }

$unregistered = @($actual | Where-Object { -not $referenced.Contains($_) } | Sort-Object)
$missingFromDirectory = @($referenced | Where-Object { -not $actual.Contains($_) } | Sort-Object)
if ($unregistered.Count -gt 0) {
    throw "Assets 中存在未登记的运行 PNG：$($unregistered -join ', ')"
}
if ($missingFromDirectory.Count -gt 0) {
    throw "素材清单文件未进入运行素材目录：$($missingFromDirectory -join ', ')"
}

$project = Get-Content $projectPath -Raw
if ($project -notmatch 'Content Include="Assets\\\*\*\\\*\.png"' -or
    $project -notmatch 'Exclude="Assets\\pupu-icon\.png"') {
    throw "Pupu.Desktop.csproj 必须用受清单门禁保护的 Assets 通配复制规则。"
}
foreach ($path in $referenced) {
    if ($project.Contains($path) -or $project.Contains($path.Replace("/", "\"))) {
        throw "Pupu.Desktop.csproj 仍硬编码正式素材文件名：$path"
    }
}

if (-not [string]::IsNullOrWhiteSpace($PublishedRoot)) {
    $publishedAssets = Join-Path $PublishedRoot "Assets"
    $publishedManifest = Join-Path $publishedAssets "pupu-assets.json"
    if (-not (Test-Path $publishedManifest -PathType Leaf)) {
        throw "发布结果缺少素材清单：$publishedManifest"
    }
    if ((Get-FileHash $publishedManifest -Algorithm SHA256).Hash -ne
        (Get-FileHash $manifestPath -Algorithm SHA256).Hash) {
        throw "发布结果中的素材清单与源码不一致。"
    }

    $published = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    Get-ChildItem $publishedAssets -Recurse -File -Filter "*.png" |
        ForEach-Object {
            $relative = Get-NormalizedRelativePath $publishedAssets $_.FullName
            $null = $published.Add($relative)
        }

    $publishedExtra = @($published | Where-Object { -not $referenced.Contains($_) } | Sort-Object)
    $publishedMissing = @($referenced | Where-Object { -not $published.Contains($_) } | Sort-Object)
    if ($publishedExtra.Count -gt 0) {
        throw "发布目录包含清单外 PNG：$($publishedExtra -join ', ')"
    }
    if ($publishedMissing.Count -gt 0) {
        throw "发布目录缺少清单引用 PNG：$($publishedMissing -join ', ')"
    }
    foreach ($path in $referenced) {
        $sourceHash = (Get-FileHash (Join-Path $assetRoot $path) -Algorithm SHA256).Hash
        $publishedHash = (Get-FileHash (Join-Path $publishedAssets $path) -Algorithm SHA256).Hash
        if ($sourceHash -ne $publishedHash) {
            throw "发布素材与源码字节不一致：$path"
        }
    }
}

Write-Host "素材清单契约通过：$($referenced.Count) 个运行 PNG，目录、工程与发布规则一致。" -ForegroundColor Green
