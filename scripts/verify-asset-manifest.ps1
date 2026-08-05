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
        throw "File is outside the expected directory: $FullPath"
    }
    return $full.Substring($base.Length + 1).Replace("\", "/")
}

function Normalize-ManifestPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw "The asset manifest contains an empty file path."
    }
    $normalized = $Path.Replace("\", "/")
    if ([System.IO.Path]::IsPathRooted($normalized) -or
        $normalized.StartsWith("/") -or
        ($normalized -split '/') -contains "..") {
        throw "Asset paths must stay inside the Assets directory: $Path"
    }
    if (-not $normalized.EndsWith(".png", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Runtime assets must be PNG files: $Path"
    }
    return $normalized
}

if (-not (Test-Path $manifestPath)) { throw "Asset manifest is missing: $manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
$referenced = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::OrdinalIgnoreCase)

foreach ($atlas in $manifest.atlases.PSObject.Properties.Value) {
    $path = Normalize-ManifestPath $atlas.file
    if (-not $referenced.Add($path)) { throw "Duplicate asset manifest reference: $path" }
}
foreach ($group in $manifest.actionGroups.PSObject.Properties.Value) {
    if ($group.source.type -in @("spriteStrip", "singleFile") -and
        -not [string]::IsNullOrWhiteSpace($group.source.file)) {
        $path = Normalize-ManifestPath $group.source.file
        if (-not $referenced.Add($path)) { throw "Duplicate asset manifest reference: $path" }
    }
}

foreach ($path in $referenced) {
    $sourcePath = Join-Path $assetRoot $path
    if (-not (Test-Path $sourcePath -PathType Leaf)) {
        throw "Asset manifest references a missing file: $path"
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
    throw "Assets contains unregistered runtime PNG files: $($unregistered -join ', ')"
}
if ($missingFromDirectory.Count -gt 0) {
    throw "Manifest files are missing from the runtime asset directory: $($missingFromDirectory -join ', ')"
}

$project = Get-Content $projectPath -Raw
if ($project -notmatch 'Content Include="Assets\\\*\*\\\*\.png"' -or
    $project -notmatch 'Exclude="Assets\\pupu-icon\.png"') {
    throw "Pupu.Desktop.csproj must use the manifest-guarded Assets wildcard copy rule."
}
foreach ($path in $referenced) {
    if ($project.Contains($path) -or $project.Contains($path.Replace("/", "\"))) {
        throw "Pupu.Desktop.csproj still hard-codes a runtime asset file name: $path"
    }
}

if (-not [string]::IsNullOrWhiteSpace($PublishedRoot)) {
    $publishedAssets = Join-Path $PublishedRoot "Assets"
    $publishedManifest = Join-Path $publishedAssets "pupu-assets.json"
    if (-not (Test-Path $publishedManifest -PathType Leaf)) {
        throw "Published output is missing the asset manifest: $publishedManifest"
    }
    if ((Get-FileHash $publishedManifest -Algorithm SHA256).Hash -ne
        (Get-FileHash $manifestPath -Algorithm SHA256).Hash) {
        throw "Published asset manifest does not match the source manifest."
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
        throw "Published output contains PNG files outside the manifest: $($publishedExtra -join ', ')"
    }
    if ($publishedMissing.Count -gt 0) {
        throw "Published output is missing manifest PNG files: $($publishedMissing -join ', ')"
    }
    foreach ($path in $referenced) {
        $sourceHash = (Get-FileHash (Join-Path $assetRoot $path) -Algorithm SHA256).Hash
        $publishedHash = (Get-FileHash (Join-Path $publishedAssets $path) -Algorithm SHA256).Hash
        if ($sourceHash -ne $publishedHash) {
            throw "Published asset bytes do not match source: $path"
        }
    }
}

Write-Host "Asset manifest contract passed: $($referenced.Count) runtime PNG files match directory, project, and publish rules." -ForegroundColor Green
