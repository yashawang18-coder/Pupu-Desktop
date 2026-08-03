$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$assetDirectory = Join-Path $root "Pupu.Desktop\Assets"
$manifestPath = Join-Path $assetDirectory "pupu-assets.json"

if (-not (Test-Path $manifestPath)) { throw "缺少素材清单：$manifestPath" }
$manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -lt 1 -or $manifest.schemaVersion -gt 2) {
    throw "不支持的素材清单版本：$($manifest.schemaVersion)"
}
if ($manifest.cellSize -ne 256) { throw "素材单格必须为 256×256。" }

Add-Type -AssemblyName PresentationCore

function Read-VerifiedBitmap([string]$Path) {
    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $decoder = [System.Windows.Media.Imaging.BitmapDecoder]::Create(
            $stream,
            [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
            [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
        $frame = $decoder.Frames[0]
        # Reading dimensions alone does not inflate IDAT data and previously
        # allowed truncated PNGs into the installer. CopyPixels forces a full
        # decode and CRC/data-stream validation before packaging.
        $bitsPerPixel = [Math]::Max(32, $frame.Format.BitsPerPixel)
        $stride = [int][Math]::Ceiling($frame.PixelWidth * $bitsPerPixel / 8.0)
        $pixels = [byte[]]::new($stride * $frame.PixelHeight)
        $frame.CopyPixels($pixels, $stride, 0)
        return $frame
    }
    finally { $stream.Dispose() }
}

$requiredRows = @{
    core = 6
    life = 8
    directions = 4
    touch = 6
    routines = 8
    walkModes = 8
    activity = 8
    lifeEquipment = 3
    motion = 10
    gazeCoin = 3
    litter = 4
    specials = 5
    seasonal = 4
}
foreach ($id in $requiredRows.Keys) {
    $atlas = $manifest.atlases.$id
    if ($null -eq $atlas) { throw "素材清单缺少图集：$id" }
    if ($atlas.columns -ne 8 -or $atlas.rows -lt $requiredRows[$id]) {
        throw "图集 $id 网格不足：需要 8×$($requiredRows[$id])。"
    }
    $path = Join-Path $assetDirectory $atlas.file
    if (-not (Test-Path $path)) { throw "找不到图集：$path" }
    $frame = Read-VerifiedBitmap $path
    $expectedWidth = $atlas.columns * $manifest.cellSize
    $expectedHeight = $atlas.rows * $manifest.cellSize
    if ($frame.PixelWidth -ne $expectedWidth -or $frame.PixelHeight -ne $expectedHeight) {
        throw "图集 $id 是 $($frame.PixelWidth)×$($frame.PixelHeight)，期望 $expectedWidth×$expectedHeight。"
    }
}

$requiredCoinStates = @(
    "normalColor",
    "normalFaded",
    "unhappyColor",
    "unhappyFaded",
    "back",
    "normalEdge",
    "backEdge"
)
if ($null -ne $manifest.coinStates) {
    foreach ($state in $requiredCoinStates) {
        $definition = $manifest.coinStates.$state
        if ($null -eq $definition) { throw "coinStates 缺少状态：$state" }
        $atlas = $manifest.atlases.($definition.atlas)
        if ($null -eq $atlas -or $definition.row -lt 0 -or $definition.row -ge $atlas.rows) {
            throw "银币状态 $state 引用了无效图集行。"
        }
    }
}

if ($manifest.schemaVersion -ge 2 -and $null -ne $manifest.actionGroups) {
    foreach ($property in $manifest.actionGroups.PSObject.Properties) {
        $id = $property.Name
        $group = $property.Value
        if ([string]::IsNullOrWhiteSpace($group.behaviorId)) {
            throw "动作组 $id 缺少 behaviorId。"
        }
        if ($group.frameCount -lt 1 -or $group.frameDurationMs -lt 40) {
            throw "动作组 $id 的帧数或帧时长无效。"
        }
        if ($group.loopMode -notin @("once", "loop", "pingPong", "hold")) {
            throw "动作组 $id 的 loopMode 无效。"
        }
        if ($null -eq $group.triggerConditions -or $group.triggerConditions.Count -lt 1) {
            throw "动作组 $id 缺少面板触发条件说明。"
        }
        if ($group.source.type -eq "atlasRow") {
            $atlas = $manifest.atlases.($group.source.atlas)
            if ($null -eq $atlas -or $group.source.row -lt 0 -or $group.source.row -ge $atlas.rows) {
                throw "动作组 $id 引用了无效旧图集行。"
            }
        }
        elseif ([string]::IsNullOrWhiteSpace($group.source.file) -and
                [string]::IsNullOrWhiteSpace($group.fallback)) {
            throw "动作组 $id 缺少独立文件且没有 fallback。"
        }
        elseif (-not [string]::IsNullOrWhiteSpace($group.source.file)) {
            $actionPath = Join-Path $assetDirectory $group.source.file
            if (-not (Test-Path $actionPath)) {
                throw "动作组 $id 找不到独立动作文件：$actionPath"
            }
            $null = Read-VerifiedBitmap $actionPath
        }
    }
}

Write-Host "十三套外部素材图集、五态银币、独立动作文件与触发条件检查通过。" -ForegroundColor Green
