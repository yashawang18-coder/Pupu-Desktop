param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [ValidateSet("Full", "Package")]
    [string]$Mode = "Full"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Pupu.Desktop\Pupu.Desktop.csproj"
$installerProject = Join-Path $root "Pupu.Installer\Pupu.Installer.csproj"
$version = (& (Join-Path $PSScriptRoot "get-pupu-version.ps1")).Trim()
$artifactRoot = Join-Path $root "artifacts"
$output = Join-Path $artifactRoot "Pupu-$Runtime-$version"
$architecture = $Runtime.Replace("win-", "")
$zip = Join-Path $artifactRoot "Pupu-Windows-$architecture-$version.zip"
$installerWork = Join-Path $artifactRoot "installer-$Runtime-$version"
$payloadZip = Join-Path $installerWork "Pupu-Payload-$version.zip"
$installerOutput = Join-Path $installerWork "publish"
$setup = Join-Path $artifactRoot "Pupu-Setup-$architecture-$version.exe"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK 未安装。请先访问 https://dotnet.microsoft.com/download/dotnet/8.0"
}

function Assert-NativeSuccess([string]$Step) {
    if ($LASTEXITCODE -ne 0) {
        throw "$Step 失败，退出码：$LASTEXITCODE"
    }
}

Remove-Item $output -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $installerWork -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Remove-Item $setup -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $output | Out-Null
New-Item -ItemType Directory -Force -Path $installerWork | Out-Null

& (Join-Path $PSScriptRoot "verify-release-contract.ps1")
& (Join-Path $PSScriptRoot "verify-asset-manifest.ps1")
dotnet --info
Assert-NativeSuccess "dotnet --info"
dotnet restore (Join-Path $root "Pupu.sln") --runtime $Runtime
Assert-NativeSuccess "dotnet restore"
dotnet build (Join-Path $root "Pupu.sln") --configuration Release --no-restore
Assert-NativeSuccess "dotnet build"
if ($Mode -eq "Full") {
    & (Join-Path $PSScriptRoot "verify-architecture.ps1")
    & (Join-Path $PSScriptRoot "verify-bindings.ps1")
    & (Join-Path $PSScriptRoot "verify-assets.ps1")
    dotnet run --project (Join-Path $root "Pupu.Tests\Pupu.Tests.csproj") --configuration Release --no-build
    Assert-NativeSuccess "dotnet test runner"
}
dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $output `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false
Assert-NativeSuccess "dotnet publish Pupu.Desktop"
& (Join-Path $PSScriptRoot "verify-asset-manifest.ps1") -PublishedRoot $output
$publishedExecutable = Join-Path $output "Pupu.exe"
& (Join-Path $PSScriptRoot "run-windows-smoke.ps1") -Executable $publishedExecutable

Copy-Item (Join-Path $root "README.md") $output -Force
Copy-Item (Join-Path $root "CHANGELOG.md") $output -Force
Copy-Item (Join-Path $root "ASSET-PACK.md") $output -Force
Copy-Item (Join-Path $root "REMEDIATION-1.10.0.md") $output -Force
Copy-Item (Join-Path $root "COIN-UPDATE-1.10.1.md") $output -Force
Copy-Item (Join-Path $root "COIN-UPDATE-1.11.0.md") $output -Force
Copy-Item (Join-Path $root "ARCHITECTURE-1.11.0.md") $output -Force
Copy-Item (Join-Path $root "V19-IMPLEMENTATION-$version.md") $output -Force

$assetManifest = Join-Path $output "Assets\pupu-assets.json"
if (-not (Test-Path $assetManifest)) {
    throw "发布结果缺少外部素材清单：$assetManifest"
}
$publishedManifest = Get-Content $assetManifest -Raw | ConvertFrom-Json
foreach ($atlas in $publishedManifest.atlases.PSObject.Properties.Value) {
    $publishedAtlas = Join-Path $output (Join-Path "Assets" $atlas.file)
    if (-not (Test-Path $publishedAtlas)) {
        throw "发布结果缺少外部动作图集：$publishedAtlas"
    }
}
foreach ($group in $publishedManifest.actionGroups.PSObject.Properties.Value) {
    if ($group.source.type -in @("spriteStrip", "singleFile")) {
        $publishedAction = Join-Path $output (Join-Path "Assets" $group.source.file)
        if (-not (Test-Path $publishedAction)) {
            throw "发布结果缺少独立动作素材：$publishedAction"
        }
    }
}

$manifestFiles = Get-ChildItem $output -Recurse -File |
    Sort-Object FullName |
    ForEach-Object {
        $basePath = [System.IO.Path]::GetFullPath($output).TrimEnd([char[]]"\/")
        $relative = [System.IO.Path]::GetFullPath($_.FullName).Substring($basePath.Length + 1).Replace("\", "/")
        [ordered]@{
            path = $relative
            sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash
        }
    }
$installManifest = [ordered]@{
    version = $version
    architecture = $architecture
    files = @($manifestFiles)
}
$installManifest |
    ConvertTo-Json -Depth 5 |
    Set-Content (Join-Path $output "install-manifest.json") -Encoding utf8

Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip -CompressionLevel Optimal

Compress-Archive `
    -Path (Join-Path $output "*") `
    -DestinationPath $payloadZip `
    -CompressionLevel Optimal
dotnet publish $installerProject `
    --configuration Release `
    --runtime $Runtime `
    --self-contained true `
    --no-restore `
    --output $installerOutput `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:PupuPayloadZip="$payloadZip"
Assert-NativeSuccess "dotnet publish Pupu.Installer"

$builtSetup = Join-Path $installerOutput "Pupu.Installer.exe"
if (-not (Test-Path $builtSetup)) {
    throw "安装程序发布失败：$builtSetup"
}
Copy-Item $builtSetup $setup -Force

Write-Host "pupu 已构建：$output" -ForegroundColor Green
Write-Host "分享包：$zip" -ForegroundColor Green
Write-Host "一键安装程序：$setup" -ForegroundColor Green
