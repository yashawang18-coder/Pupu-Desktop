# pupu 可替换素材包

运行时素材由 `Pupu.Desktop/Assets/pupu-assets.json` 描述。发布后这些文件位于 `Pupu.exe` 旁的 `Assets` 文件夹，不会嵌入 DLL。

## 主人替换素材

在控制面板“动作素材库”点击“打开并准备可编辑素材目录”，内置素材会复制到：

```text
%LOCALAPPDATA%\PupuDesktop\assets
```

应用启动时优先读取该目录中的 `pupu-assets.json`。保持既有网格时，可以直接替换清单指向的 PNG：

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

- schema 1 继续只需要 `atlases`；运行时会为面板合成只读旧图集动作组。
- schema 2 可增加 `actionGroups`。每组包含 `groupId`、`behaviorId`、`source`、`frameCount`、`frameDurationMs`／`frameDurationsMs`、`frames`、`loopMode`、`intro`、`loop`、`exit`、`directions`、`compatiblePostures`、`mouseGaze`、`interactions` 和 `fallback`。
- `source.type=atlasRow` 复用旧图集；`spriteStrip` 表示横向或纵向独立动作条带；`singleFile` 预留单帧动作 PNG。
- schema 2 字段不完整时使用本地默认值；来源不可读时先尝试组内 fallback，再回到 C# 原有 `AnimationSequence`，不会让旧素材包失效。
- 当前正式清单已登记侧躺、低趴、板鸭趴、逗猫棒、冻干、窗口休息和八方向视线代表组；其他旧动作仍通过 atlas row 正常播放，后续可逐组迁移。

## 固定身体坐标系与生成要求

1. 所有帧使用统一画布和透明背景；当前基准为 256×256 RGBA、四边至少 20px 透明安全区。
2. 禁止按每帧整只宠物的外接框自动铺满。先建立固定“宠物身体坐标系”，以头部、身体骨架、脚底线和重心为尺度锚点。
3. 同一宠物跨动作组保持主体比例、身体长度、短腿程度、头身比和落地点一致。每个动作组单独输出、维护、逐帧预览，并登记帧数、帧时长、循环方式和行为标签。
4. `idle.prone_observe` 需要八方向局部视线；`idle.side_lie` 需要左／右／上；`idle.sploot` 需要低头／左／右；`rest.near_owner` 需要慢眨眼。不具备局部眼睛、耳朵或头部素材时，只记录轻反馈，不硬切大动作。
5. 银币正式状态为 `normalColor`、`normalFaded`、`unhappyColor`、`unhappyFaded`、`back`。V17 四个正面状态从主人参考图重制的正视亮银边透明母版确定性派生，不含黑色透视侧缘；背面继续使用同圆心浮雕猫爪。
6. V15 色键源必须经过连通域切分、固定主体中线、脚底基线、20px 透明安全边距与边缘去绿；不得把生成图的等宽网格线直接当作宠物边界。
7. 追逐素材为方向优先的 8×4 帧条带；运行时只允许在脚步换帧时推进窗口坐标，不允许静止图案漂移。
6. V13 四组魔法已按固定身体坐标系统一尺度，石化为硬质晶面；素材审计继续检测主体过小、透明边距异常、清晰度不足、移动体型漂移和中心跳动。

## 开发者重新生成

透明源图位于 `AssetSources`。在安装 ImageMagick 的环境运行：

```bash
./scripts/rebuild-v8-activity.sh
./scripts/rebuild-v12-assets.sh
./scripts/rebuild-v13-assets.sh
python scripts/rebuild-v17-coin.py
./scripts/verify-assets.sh
```

规范化脚本会清除色键，并按固定身体坐标系归一化整猫、道具和局部覆盖层。V13 在 V12 基线上重做魔法、硬质石化、五态银币、如厕、激光笔和社交触摸，并增加 16 方向锚点追逐、16 方向背带遛猫及 16 帧局部视线覆盖层，同时删除无法可靠感知环境的窗口上沿素材行。`rebuild-v13-assets.sh` 先写临时 PNG、完整解码后再原子替换正式文件。`verify-assets.sh` 会检查 616 个图集单元格和 48 个独立动作帧；Windows 正式构建还会运行 `scripts/verify-assets.ps1`。

面部身份参考位于 `AssetSources/reference/pupu-face-2026-07-23/`。V13 色键源位于 `AssetSources/v13/`，退役正式图只保留在素材源归档中供审计和回退，不进入应用发布目录。正式运行仅读取清单指向文件。
