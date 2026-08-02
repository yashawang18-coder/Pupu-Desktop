$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$mainXaml = Get-Content (Join-Path $root "Pupu.Desktop\MainWindow.xaml") -Raw
$controlXaml = Get-Content (Join-Path $root "Pupu.Desktop\ControlWindow.xaml") -Raw
$xaml = $mainXaml + "`n" + $controlXaml

$readOnlyBindings = @(
    "PetFrame",
    "BubbleText",
    "IsBubbleVisible",
    "PetDisplaySize",
    "CoinFlipScaleX",
    "InteractionScale",
    "CurrentBehaviorLabel",
    "EffectivePersonality",
    "RuntimeStateSummary",
    "RelationshipStateSummary",
    "Fullness",
    "Happiness",
    "Cleanliness",
    "Energy",
    "ChatMessages",
    "MouseInteractionModeLabel",
    "ConfinementStatus",
    "PersonalityMemoryMatchSummary",
    "EditableMemoryStatus",
    "NaturalPolicySummary",
    "NaturalRuleStatus",
    "NaturalRules",
    "HiddenActionRules",
    "ModelApiStatus",
    "AssetPackStatus",
    "AssetCompatibilityStatus",
    "AssetActionGroups",
    "CurrentIntent",
    "LastArbitrationResult",
    "LastProposalResult",
    "CursorAttentionStatus",
    "BehaviorScoreItems",
    "ArbitrationItems",
    "BehaviorProposalItems",
    "AssetGenerationRequirements",
    "CodeImplementationCards",
    "CodexIterationStatus"
)

foreach ($name in $readOnlyBindings) {
    if ($xaml -notmatch "\{Binding\s+$name,\s+Mode=OneWay") {
        throw "只读属性 $name 没有显式使用 Mode=OneWay。"
    }
}

$requiredTopLevelHeaders = @("主人", "功能设置", "素材库", "开发者")
foreach ($header in $requiredTopLevelHeaders) {
    if ($controlXaml -notmatch "<TabItem\s+Header=`"$header`"") {
        throw "控制面板缺少一级栏目：$header"
    }
}

$requiredFunctionalHeaders = @("性格与回复", "长期记忆", "动作规则", "大模型")
foreach ($header in $requiredFunctionalHeaders) {
    if ($controlXaml -notmatch "<TabItem\s+Header=`"$header`"") {
        throw "功能设置缺少区域：$header"
    }
}

if ($controlXaml -match "<TabItem\s+Header=`"(桌面设置|素材包|性格|记忆|行为)`"") {
    throw "旧的独立页签仍存在，信息架构未完成整合。"
}
if ($controlXaml -notmatch "\{Binding\s+OwnerPersonalityPrompt,\s+Mode=TwoWay") {
    throw "主人自定义宠物性格提示词没有可编辑绑定。"
}
$requiredGalleries = @(
    "AutonomousActionGallery",
    "InteractiveActionGallery",
    "MagicActionGallery",
    "SeasonalActionGallery"
)
foreach ($gallery in $requiredGalleries) {
    if ($controlXaml -notmatch "\{Binding\s+$gallery,\s+Mode=OneWay") {
        throw "素材库缺少动作分类绑定：$gallery"
    }
}
if ($controlXaml -notmatch "\{Binding\s+AssetActionGroups,\s+Mode=OneWay") {
    throw "素材库缺少技术动作映射。"
}

Write-Host "绑定与面板信息架构检查通过。" -ForegroundColor Green
