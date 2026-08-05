# pupu 可替换素材包

运行时素材由 `Pupu.Desktop/Assets/pupu-assets.json` 描述。发布后这些文件位于 `Pupu.exe` 旁的 `Assets` 文件夹，不会嵌入 DLL。

## 主人替换素材

在控制面板“动作素材库”点击“打开并准备可编辑素材目录”，内置素材会复制到：

```text
%LOCALAPPDATA%\PupuDesktop\assets
```

应用只在本地清单版本与内置清单完全一致时读取该目录；版本不一致会安全回退内置 V19。点击“打开可编辑素材目录”升级时会覆盖刷新当前版 PNG，避免历史文件继续覆盖新版。保持既有网格时，可以直接替换清单指向的 PNG：

- `core`：8×6；
- `life`：8×8；
- `directions`：8×4；
- `touch`：8×6；
- `routines`：8×8；
- `walkModes`：8×8；
- `activity`：8×8；
- `lifeEquipment`：8×3；
- `motion`：8×10；
- `gazeCoin`：8×3；
- `litter`：8×4；
- `specials`：8×5；
- `seasonal`：8×4；
- 每格固定 256×256 RGBA，主体最长边不超过 216 像素，四边至少 20px 透明区。
- 移动图集同一行共用同一缩放率和底部锚点；触摸图集按完整姿势逐格适配同一 216px 舞台，避免横躺姿势把整行其他帧缩小；触摸图集不得出现手或任何人体局部。

如果要改文件名，只修改 JSON 的 `file` 字段。应用会校验路径、尺寸与最低行数；自定义包损坏时会回退内置素材，不阻止 pupu 启动。

## schema 1 / schema 2 兼容

- schema 1 只作为读取兼容；正式 V19 运行包使用 schema 2。
- schema 2 可增加 `actionGroups`。每组包含 `groupId`、`behaviorId`、`source`、`frameCount`、`frameDurationMs`／`frameDurationsMs`、`frames`、`loopMode`、`intro`、`loop`、`exit`、`directions`、`compatiblePostures`、`mouseGaze`、`interactions` 和 `fallback`。
- `source.type=atlasRow` 复用旧图集；`spriteStrip` 表示横向或纵向独立动作条带；`singleFile` 预留单帧动作 PNG。
- schema 2 字段不完整时使用本地默认值；来源不可读时先尝试组内 fallback，再回到 C# 原有 `AnimationSequence`。
- 当前正式清单已登记全部实际行为映射；新动作不能只把 PNG 放入目录，必须同时登记行为 ID、触发、表现映射、预览和测试。

## 固定身体坐标系与生成要求

1. 所有帧使用统一画布和透明背景；当前基准为 256×256 RGBA、四边至少 20px 透明安全区。
2. 禁止按每帧整只宠物的外接框自动铺满。先建立固定“宠物身体坐标系”，以头部、身体骨架、脚底线和重心为尺度锚点。
3. 同一宠物跨动作组保持主体比例、身体长度、短腿程度、头身比和落地点一致。每个动作组单独输出、维护、逐帧预览，并登记帧数、帧时长、循环方式和行为标签。
4. `idle.prone_observe` 需要八方向局部视线；`idle.side_lie` 需要左／右／上；`idle.sploot` 需要低头／左／右；`rest.near_owner` 需要慢眨眼。不具备局部眼睛、耳朵或头部素材时，只记录轻反馈，不硬切大动作。
5. 每个动作至少提供 8 个可显示相位。4 个关键姿态可增加轻微呼吸或重心变化，但禁止直接重复相邻帧、整猫平移冒充动作或交叉淡化造成双重曝光。
6. 银币状态为 `normalColor`、`normalFaded`、`unhappyColor`、`unhappyFaded`、`back`。V19 彩色态提高银质高光和色彩，褪色态使用暖棕旧化与轻微锈迹，不允许只做灰度置换。
7. 追逐素材为 8 方向 × 8 相位的 64 帧横向条带；运行时只允许在脚步换帧时推进窗口坐标，不允许静止图案漂移。
8. 魔法和节日素材必须与日常 V19 身份一致；装扮只能覆盖道具和服饰，不改变脸型、头身比、腿长、身体长度或尾巴。
9. 清单的 `intro`、`loop`、`exit` 必须形成可执行片段。姿态不兼容的组切换先退出、再入场；移动方向切换保留相位。

## 开发者重新生成

透明源图位于 `AssetSources`。在安装 ImageMagick 的环境运行：

```bash
python scripts/rebuild-v19-assets.py
python scripts/audit-asset-quality.py
./scripts/verify-assets.sh
```

V19 脚本从 V18 身份母表、V19 魔法／节日母表和稳定银币源重建正式图集，不读取旧运行图作为隐式底图。审计会完整解码 PNG、检查透明边距、绿边、主体尺度、质心、清晰度、相邻帧和循环闭合；Windows 正式构建运行同一套门禁。

面部身份参考位于 `AssetSources/reference/pupu-face-2026-07-23/`。V13 色键源位于 `AssetSources/v13/`，退役正式图只保留在素材源归档中供审计和回退，不进入应用发布目录。正式运行仅读取清单指向文件。
