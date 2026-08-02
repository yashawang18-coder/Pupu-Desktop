# pupu 动作素材库

所有正式图集单元格均为 256×256 RGBA，8 列，四边至少 20px 透明安全区。运行时“动作素材库”按自发行为、互动行为、魔法特辑、节日特辑四个页签展示；节日限定只显示文字标签，不允许预览绕过日期门禁。

## 正式图集

| 图集 ID | 文件 | 尺寸 | 行 | 内容 |
| --- | --- | ---: | ---: | --- |
| `core` | `pupu-core-youthful-v18.png` | 2048×1536 | 0–5 | V18 趴卧呼吸、露肚翻滚、追尾转圈、逗猫棒、眨眼哈欠、伸展卧下 |
| `life` | `pupu-life-youthful-v18.png` | 2048×2048 | 0–7 | 投喂、检查猫砂、摸摸与过度 rua、孔雀蓝梳毛、求关注、偷偷捣乱、V18 陪伴睡姿与叼绳求遛 |
| `directions` | `pupu-directions-youthful-v12.png` | 2048×1024 | 0–3 | 左、右、背向、正向移动 |
| `touch` | `pupu-touch-youthful-v13.png` | 2048×1536 | 0–5 | 轻触、呼噜、好奇、需要空间、转身离开、信任亲近 |
| `routines` | `pupu-routines-youthful-v18.png` | 2048×2048 | 0–7 | V18 侧躺慢呼吸、低趴观察、甜脚吃脚、猫粮慢吃、冻干追食、罐头扑食、完整背影、侧背转换 |
| `walkModes` | `pupu-walk-modes-youthful-v12.png` | 2048×2048 | 0–7 | 孔雀蓝背带与无背带的左、右、背向、正向连续步态 |
| `activity` | `pupu-activity-youthful-v18.png` | 2048×2048 | 0–7 | V18 激光扑抓、三种睡姿、趴睡转换、板鸭趴、兼容占位 |
| `lifeEquipment` | `pupu-life-equipment-youthful-v18.png` | 2048×768 | 0–2 | V18 日常舔毛、蓝色长方形小窝睡眠、孔雀蓝牵引绳 |
| `motion` | `pupu-motion-youthful-v18.png` | 2048×2560 | 0–9 | V18 背带／无背带左前右前步态、其余斜向步态、扫帚八方向、侧躺微动 |
| `gazeCoin` | `pupu-gaze-coin-youthful-v17.png` | 2048×768 | 0–2 | 两相贴地视线摆尾、V17 正视亮银边五态银币 |
| `litter` | `pupu-litter-youthful-v18.png` | 2048×1024 | 0–3 | V18 检查进入、低头如厕、概率抬头、爪爪开花埋屎并离开 |
| `specials` | `pupu-specials-youthful-v13.png` | 2048×1280 | 0–4 | 旧视线兼容行与四种等比例紫色星星斗篷魔法 |
| `seasonal` | `pupu-seasonal-youthful-v10.png` | 2048×1024 | 0–3 | 圣诞、万圣节、春节和主人生日限定 |

合计 13 张图集、77 行、616 格；V18 运行清单共引用 20 个 PNG，独立素材合计 110 帧。

## V18 形象统一与交互修复

- 以“好奇询问／开心摸摸”的幼态银灰黑白矮脚猫为身份基线，替换主人点名的旧脸型、旧体型和错误睡姿。
- `laser-chase-8` 与 `snack-chase-8` 不再共用追逐图；两者各自拥有八方向、每方向四个无重影相位。
- 选择冻干或激光落点只进入一次性选点模式，不提前申请行为租约；落点后才生成单个 OwnerAnchor 提案。
- 主人明确点击魔法使用 OwnerForced，可中断普通动作；自主魔法仍保留每日一次和统一仲裁规则。
- 被替换的旧运行 PNG 已从打包目录移除；`AssetSources/v18` 与 `scripts/rebuild-v18-assets.py` 可确定性重建新图集。

## V17 银币组合更新

- `gazeCoin:2`：`normalColor`、`normalFaded`、`unhappyColor`、`unhappyFaded` 四种正面使用主人参考图重制的正视亮银边母版；`back` 为同圆心浮雕猫爪。
- 正面保留 `CAT COIN`、`MAGIC CURRENCY`、月亮、星星和银质环纹，圆外完全透明，不再包含黑色透视侧缘。
- “素材库 → 技术与存储 → 银币组合更新”直接从运行时清单读取五态实图、状态键、坐标和时长，不维护第二套映射。
- `scripts/rebuild-v17-coin.py` 从 `AssetSources/v17` 透明正面母版和 V16 归档背面确定性重建 V17 四态正面与兼容帧。

## V13 增量动作

- `specials:1–4`：等比例紫色星星斗篷魔法；石化使用硬质晶面与石缝。
- `gazeCoin:2`：五态银币，四种正面不再共享帧；1.11.0 已由上述 V17 正视亮银边母版替换，运行时状态键保持兼容。
- `Actions/pupu-gaze-overlays-youthful-v13.png`：16 个局部头部覆盖层，只在兼容姿态叠加。
- `Actions/pupu-anchor-chase-16dir-youthful-v13.png`：食物／玩具锚点共用的 16 方向靠近形态。
- `Actions/pupu-walk-harness-16dir-youthful-v13.png`：背带遛猫按路线向量选择最近的 16 方向视角。
- 窗口上沿趴卧／巡视的行为链和素材行均已删除；旧记忆中的 `rest.window` 安全回退为原地侧躺。

当前形象规范以主人最新上传的四张高清实拍及已验收的“眨眼哈欠”“鼠标视线跟随”“向下跑”为身份和清晰度参考。朴朴始终是银灰黑白长毛拿破仑矮脚幼猫：宽圆幼态脸、短口鼻、黄绿色圆眼、额头白色中轴、粉黑鼻头中央小黑斑、自然偏长躯干、明显短腿和完整大尾巴。道具、魔法服装与设备不得改变本体比例。

## V12 日常动作

- `idle.prone_observe`：`routines:1`，胸口贴地低趴，从侧面安静观察。
- `self.paw_nibble`：`routines:2`，抱住后脚舔毛并轻轻啃爪；与 `self.groom` 分开进入低频自主候选。
- `self.groom`：`lifeEquipment:0`，舔前爪、胸口、侧腹和大尾巴，单次至少 75 秒、完成后至少冷却 10 分钟。
- `play.roll`：`core:1`，小猫侧躺、露肚并完整翻滚。
- `play.tail_chase`：`core:2`，短腿原地追大尾巴转圈。
- `care.feed_freeze_dried`：`routines:4`，单次“冻干饿猫扑食”起步后只循环后半段进食帧。
- `care.feed_canned`：`routines:5`，单次“罐头饿猫扑食”起步后只循环后半段舔食帧。
- `care.clean_litter`：`life:1`，先靠近、检查和刨砂。
- `care.groom`：`life:3`，使用孔雀蓝梳子梳后背和大尾巴；梳子属于预设动作素材。
- `touch.warning`：`touch:3`，过度 rua 后移开视线、贴地甩尾和退开，不出现手、哈气、露齿或攻击。
- `touch.enjoy` 高信任分支：`touch:5`，走近、慢眨眼、蹭头并安心趴下；运行时同步将形象从 100% 平滑放大到约 106%，结束后恢复。
- `mischief.bat_object`：`life:5`，偷偷拨弄笔筒并装作没发生。

普通原地动作不移动透明窗口。动作组按语义设置帧速率；循环动作使用往返或拆分后的闭合片段，避免末帧跳回首帧。

## 独立如厕动作链

| 行 | 行为 |
| ---: | --- |
| `litter:0` | 检查猫砂、走近并进入 |
| `litter:1` | 低头完成如厕微动 |
| `litter:2` | 角色偏好的概率抬头变体 |
| `litter:3` | 爪爪开花连续埋屎并离开 |

`routine.toilet` 每个本地自然日随机计划 2–3 次，只在 `toilet_due` 到期时进入统一自主评分。错过的离线时隙不追补。如厕后必须衔接埋砂；即使在已提交排泄后中断，也尽量执行短版埋砂收尾。

## 贴地视线与双面银币

- `gazeCoin:0` 和 `gazeCoin:1` 都是八方向低趴视线，身体落点一致，尾巴平贴地面但处于两个轻摆相位。
- 普通鼠标靠近只作为最低优先级注意力信号：`idle.prone_observe` 记录八方向，`idle.side_lie` 记录左／右／上，`idle.sploot` 记录低头／左右，`rest.near_owner` 记录慢眨眼方向。没有姿态局部帧时不硬切 `gazeCoin`。
- 睡眠、舔毛、如厕、魔法、移动、触摸、进食和普通玩耍不会被鼠标注意力抢占；主动玩具锚点是独立的主人玩法。
- `gazeCoin:2` 包含旧银币兼容帧。石化后拖拽只移动窗口，单击刷新彩色，双击才翻到背面；可选 `coinStates` 缺失时继续回退旧图集。

## 统一仲裁、主动锚点与强制状态

- `BehaviorArbitrator` 位于 `Pupu.Behavior`，是资格、评分、选择、当前行为租约、保护期、可打断性、冷却及笼子／旅游等禁用状态的唯一入口。旧 `BehaviorSelector` 只保留为兼容外壳，内部仍委托同一仲裁器。
- `PetAgentKernel` 只读取 `IAgentMemoryPort` 的性格和只读记忆快照；接受后输出模型无关的 `BehaviorPresentationIntent`。当前由 `sprite-atlas-v17` 适配器解析为 2D 切帧，未来可替换为骨骼或其他表现实现。
- 主人可进入食物或玩具锚点模式；一次性桌面点击生成目标，先经过参与判断和仲裁，再移动并使用现有进食／玩耍动作作为素材回退。拒绝、冷却或路径不可达都显示气泡。
- “关笼子”是主人强制状态：原地锁定、禁止普通移动和大姿态切换，直到显式释放；状态写入兼容的本地 `state.json`。
- 旅游轻量版最长 24 小时：外出期间只保留明显状态入口，禁止普通行为；到期或召回后用规则模板生成一条轻量本地经历，不直接写复杂长期记忆。

## 移动与连续路线

移动素材继续使用八方向，并在切换方向时保留归一化步态相位。V12 重新统一正面、背面、左右和四个斜向的矮脚体型，尤其缩小自由外出背影的表观尺寸漂移。

`DesktopRoutePlanner` 每次运行时用新随机种子产生路段：

- 正式遛猫混合全屏大范围目标和附近目标，单段速度约 190–320 DIP/s；
- 自主溜达使用较温和、但仍为非零距离的连续曲线路段，单段速度约 135–220 DIP/s，并优先沿相近方向连续走 2–3 段；
- 每帧更新 WPF `Left` / `Top`，所有采样点都限制在当前监视器工作区；
- 路线不保存固定航点，失败时选择远端回退点，不允许只播放步态而原地不动。

扫帚飞行按每轮打乱的八方向桶依次覆盖上下左右与四个斜向，段速约 540–720 DIP/s，每段限制在约 500–900ms，使用收窄弧线和极轻 flutter；段与段连续衔接，处在边缘时先平滑回到可继续覆盖全方向的位置。

## 魔法、节日与参与规则

`specials:1–4` 依次为 `Accio Broom`、`Apparate`、`Petrificus Totalus`、`Scourgify`。主人在右键菜单明确发起魔法时必须执行，不调用普通互动的心情拒绝；宠物每天最多一次的自发魔法仍经过 EligibilityFilter、UtilityScoring、SelectionPolicy 和 ActionScheduler。

节日限定只在精确日期由 `DailySpecialRules` 触发：

- `seasonal:0`：圣诞帽，仅 12 月 25 日；
- `seasonal:1`：万圣节斗篷帽，仅 10 月 31 日；
- `seasonal:2`：春节红围巾，仅农历正月初一；
- `seasonal:3`：主人生日祝福，仅档案中的主人生日。

## 素材包与归档

- `Pupu.Desktop/Assets/pupu-assets.json` 是唯一正式文件名和网格清单。
- schema 2 在旧图集之上增加 `actionGroups`；每组登记 behavior/group ID、旧图集行或独立动作文件、帧数、时长、循环、intro/loop/exit、方向、姿态、鼠标视线、食物／玩具能力和 fallback。schema 1 与旧 1.6.0 图集继续兼容。
- 动作组必须按固定身体坐标系生成：统一画布与透明背景，以头部、身体骨架、脚底线和重心为尺度锚点，禁止按逐帧外接框铺满。每组单独输出、逐帧预览并维护节奏。
- V13 已将四组魔法统一到正常主体尺度：紫色斗篷带金色星星，石化使用硬质晶面而非水泥包裹。
- 五态银币已使用独立正面状态和猫爪背面；局部视线使用 16 帧透明头部覆盖层，不重新生成整只猫。不支持的姿态继续只做轻反馈。
- 食物／玩具锚点和孔雀蓝背带遛猫已提供 16 方向全身形态；运行时按实际移动向量选择最近方向。
- PNG 与 JSON 以外部 `Content` 随应用发布；无效自定义包会回退内置清单。
- 被 V12 替换的 `core v9`、`life/routines v6`、`directions/touch/walkModes v9`、`lifeEquipment/motion v11` 和未使用 `specials v10` 已从运行目录移除，保存在 `AssetSources/legacy-formal-v12`。
- `scripts/rebuild-v13-assets.sh` 从 `AssetSources/v13` 色键源、V12 归档基线和未替换正式行重建 V13 图集及三个独立动作条。
- 新动作还必须注册 `behavior_id`、动画来源、素材库卡片、行为触发、LearnedPreference 上下文和测试，不能只把 PNG 放进目录。

本轮审计门槛：616 格完整解码且非空、20px 安全区、最小有效主体 `68×101px`、最低清晰度 `228.0`；银币侧缘帧不参与主体短边统计，但仍检查焦点和边距。移动行表观体型漂移不超过 `1.25×`，实测 `1.238×`；相邻主体中心位移不超过 `10px`，实测 `8.54px`。
