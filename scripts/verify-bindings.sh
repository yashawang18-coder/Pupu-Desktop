#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
XAML="$ROOT/Pupu.Desktop/MainWindow.xaml $ROOT/Pupu.Desktop/ControlWindow.xaml"
read_only=(
  PetFrame BubbleText IsBubbleVisible EffectivePersonality Fullness Happiness
  Cleanliness Energy Trust LitterLevel CurrentBehaviorLabel ChatMessages Display
  MemoryStatus LocalMemoryPath ModelApiStatus PetDisplaySize PetScaleLabel
  CoinFlipScaleX InteractionScale
  NaturalRuleStatus NaturalPolicySummary NaturalRules HiddenActionRules
  RegularActionGalleryGroups MagicActionGallery SeasonalActionGallery AssetPackStatus
  PersonalityMemoryMatchSummary EditableMemoryStatus
  PetProfileSaveStatus
  CodexIterationStatus
  RuntimeStateSummary RelationshipStateSummary LearnedPreferenceItems
  BehaviorScoreItems PetProfilePortrait AutomaticPersonalitySummary
  RelationshipStageDisplay ProfilePresentationStatus AlbumRootPath
  AlbumCards AlbumPhotos AlbumSearchResults AlbumStatus SelectedPhotoPreview
  ExperienceResultOptions ExperienceImageOptions ExperienceIndexStatus
  ExperienceDebugStatus RecentExperienceMatches
  ProductDesignCards CodeImplementationCards
  ArbitrationItems LastArbitrationResult CurrentIntent CurrentPersonaSummary
  LastProposalResult BehaviorProposalItems CurrentPromptPreview
  CurrentPromptTokenEstimate LlmFallbackReason AssetCompatibilityStatus
  AssetGenerationRequirements AssetActionGroups CoinUpdateStates
  SelectedAssetActionGroup
)

for name in "${read_only[@]}"; do
  matches="$(rg -o "\\{Binding ${name}[^}]*\\}" $XAML || true)"
  if [[ -n "$matches" ]] && echo "$matches" | rg -qv "Mode=OneWay"; then
    echo "Read-only binding $name is missing explicit Mode=OneWay." >&2
    exit 2
  fi
done

for header in 主人 功能设置 素材库 开发者 状态与互动 档案 相册 性格设定 长期记忆 动作规则 "大模型与对话联调" 动作预览 技术与存储 诊断 技术说明 "Codex 迭代"; do
  rg -q "TabItem[^>]*Header=\"$header\"" "$ROOT/Pupu.Desktop/ControlWindow.xaml" || {
    echo "Control panel section $header is missing." >&2
    exit 4
  }
done

if rg -q 'TabItem Header="产品设计说明"|TabItem Header="代码实现说明"' "$ROOT/Pupu.Desktop/ControlWindow.xaml"; then
  echo "Legacy design/implementation tab names remain." >&2
  exit 5
fi

if rg -q 'Content="操作"|Content="⋯"|MouseRightButtonUp="PetImage_' $XAML; then
  echo "Legacy visible action button or right-click interaction handler remains." >&2
  exit 3
fi

echo "Verified explicit OneWay bindings and the left-interact/right-menu input contract."
