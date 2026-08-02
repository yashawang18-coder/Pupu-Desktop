# Pupu Desktop 1.6.0 素材审计

## 1. 正式清单

| ID | 文件 | 网格 | 单元格 |
| --- | --- | ---: | ---: |
| `core` | `pupu-core-youthful-v12.png` | 6×8 | 48 |
| `life` | `pupu-life-youthful-v12.png` | 8×8 | 64 |
| `directions` | `pupu-directions-youthful-v12.png` | 4×8 | 32 |
| `touch` | `pupu-touch-youthful-v12.png` | 6×8 | 48 |
| `routines` | `pupu-routines-youthful-v12.png` | 8×8 | 64 |
| `walkModes` | `pupu-walk-modes-youthful-v12.png` | 8×8 | 64 |
| `activity` | `pupu-activity-youthful-v8.png` | 8×8 | 64 |
| `lifeEquipment` | `pupu-life-equipment-youthful-v12.png` | 3×8 | 24 |
| `motion` | `pupu-motion-youthful-v12.png` | 11×8 | 88 |
| `gazeCoin` | `pupu-gaze-coin-youthful-v12.png` | 3×8 | 24 |
| `litter` | `pupu-litter-youthful-v12.png` | 4×8 | 32 |
| `specials` | `pupu-specials-youthful-v11.png` | 5×8 | 40 |
| `seasonal` | `pupu-seasonal-youthful-v10.png` | 4×8 | 32 |

合计 13 张 RGBA 图集、78 行、624 个 256×256 单元格。

## 2. V12 替换范围

| 图集 | V12 变化 |
| --- | --- |
| `core` | 替换侧躺打滚、追尾转圈 |
| `life` | 替换检查猫砂、孔雀蓝梳毛、偷偷捣乱 |
| `directions` | 以 V12 联合移动尺度重新输出四方向 |
| `touch` | 替换过度 rua 与信任亲近 |
| `routines` | 替换低趴观察、舔脚吃脚、冻干／罐头饿猫扑食 |
| `walkModes` | 以 V12 联合移动尺度重新输出背带／无背带四方向 |
| `lifeEquipment` | 保留并重建低频舔毛、蓝色长方垫、孔雀蓝牵引绳；如厕迁移到独立图集 |
| `motion` | 统一斜向移动尺寸，保留扫帚、侧躺微动和窗口趴卧 |
| `gazeCoin` | 新增两相贴地摆尾八方向视线和双面猫爪银币 |
| `litter` | 新增检查进入、低头如厕、概率抬头、爪爪开花埋屎四行 |

`activity v8`、`specials v11` 和 `seasonal v10` 未被 V12 重建，仍是正式运行清单中的已验收图集。

## 3. 身份硬约束

V12 所有猫身必须是同一只拿破仑矮脚幼猫：

- 银灰黑白长毛；
- 宽圆幼态脸、短口鼻；
- 黄绿色圆眼；
- 额头白色中轴；
- 粉黑拼接鼻头中央小黑斑；
- 自然偏长躯干，不能缩成短圆球；
- 四肢明显短于普通猫；
- 完整、蓬松的大尾巴。

本轮四张主人上传实拍负责锁定真实面部与毛色；已验收的“眨眼哈欠”“鼠标视线跟随”“向下跑”负责锁定半写实可爱程度、清晰度、头身比与动作落点。道具与场景不能改变猫本体身份。

## 4. 图像生成摘要

V12 使用内置图像生成能力制作六张纯绿色色键源：

1. **日常动作**：低趴贴地摆尾、舔脚吃脚、小猫侧躺翻滚、追尾转圈；
2. **照料与进食**：舔毛变体、冻干饿猫扑食、罐头饿猫扑食、孔雀蓝梳毛；
3. **社交动作**：检查猫砂／如厕草案、高信任走近、过度 rua 边界；正式构建只使用其中信任和边界行；
4. **自由移动**：自由外出左前、右前、背影和偷偷捣乱；
5. **视线与银币**：八方向低趴尾巴 A 相、尾巴 B 相，以及头像正面／猫爪背面的银币翻转；
6. **独立如厕**：检查进入、低头如厕、概率抬头、爪爪开花埋屎并离开。

共同提示要求：

- 纯 `#00FF00` 背景，无网格、文字和 UI；
- 每格一个主体，猫、尾巴和道具完整；
- 保持拿破仑矮脚比例，腹部贴近动作平面；
- 宽圆幼态脸、短口鼻、黄绿色圆眼和真实花纹；
- 半写实、清晰可爱，不使用夸张大头短身；
- 触摸素材不得出现手、手臂、手指或人体局部；
- 同一行从动作起因到收尾保持语义连续。

## 5. 重建流程

`scripts/rebuild-v12-assets.sh`：

1. 复制归档正式图集作为未替换行的基线；
2. 对六张 V12 色键源移除绿色背景、去绿边和轻量边缘收缩；
3. 按源图真实网格拆分主体；
4. 同一动作行统计最大宽高并共享缩放率；
5. 以 256×256 单格、20px 透明安全区统一落点；
6. 对 Directions、WalkModes 和 Motion 移动相关行联合校准尺寸；
7. 替换指定旧行，并生成独立 GazeCoin / Litter；
8. 完整解码、检查目标尺寸后写入正式文件。

## 6. 行定义

### 6.1 `core`

| 行 | 动作 |
| ---: | --- |
| 0 | 趴卧呼吸 |
| 1 | V12 侧躺露肚翻滚 |
| 2 | V12 追尾转圈 |
| 3 | 逗猫棒 |
| 4 | 困倦与哈欠 |
| 5 | 伸展与卧下 |

### 6.2 `life`

| 行 | 动作 |
| ---: | --- |
| 0 | 投喂 |
| 1 | V12 检查猫砂 |
| 2 | 摸摸与抗议 |
| 3 | V12 孔雀蓝梳毛 |
| 4 | 主动求关注 |
| 5 | V12 偷偷捣乱 |
| 6 | 陪伴与完整睡姿 |
| 7 | 求遛猫 |

### 6.3 `routines`

| 行 | 动作 |
| ---: | --- |
| 0 | 侧躺慢呼吸 |
| 1 | V12 低趴观察 |
| 2 | V12 舔脚吃脚 |
| 3 | 猫粮慢吃 |
| 4 | V12 冻干饿猫扑食 |
| 5 | V12 罐头饿猫扑食 |
| 6 | 完整背影 |
| 7 | 侧面背面转换 |

### 6.4 `touch`

| 行 | 动作 |
| ---: | --- |
| 0 | 轻触回应 |
| 1 | 放松呼噜 |
| 2 | 好奇互动 |
| 3 | V12 过度 rua／需要空间 |
| 4 | 转身离开 |
| 5 | V12 信任走近与安心贴近 |

### 6.5 `lifeEquipment`

| 行 | 动作 |
| ---: | --- |
| 0 | V12 低频日常舔毛 |
| 1 | 蓝色长方形垫子和窝内睡眠 |
| 2 | 孔雀蓝牵引绳互动 |

### 6.6 `motion`

| 行 | 动作 |
| ---: | --- |
| 0–3 | 孔雀蓝背带左前、右前、左后、右后 |
| 4–7 | 无背带左前、右前、左后、右后 |
| 8 | 扫帚八方向飞行 |
| 9 | 矮脚侧躺微动 |
| 10 | 窗口上沿矮脚趴卧 |

### 6.7 `gazeCoin`

| 行 | 动作 |
| ---: | --- |
| 0 | 鼠标八方向视线与贴地摆尾 A |
| 1 | 鼠标八方向视线与贴地摆尾 B |
| 2 | 银币头像正面、侧缘过渡、猫爪背面和回转 |

### 6.8 `litter`

| 行 | 动作 |
| ---: | --- |
| 0 | 检查猫砂并进入 |
| 1 | 自发如厕低头微动 |
| 2 | 如厕概率抬头 |
| 3 | 爪爪开花埋屎并离开 |

## 7. 自动审计规则

正式验收检查：

- PNG 可完整解码；
- 宽高与清单行列完全一致；
- 624 格均非空；
- 四边至少 20px 透明安全区；
- 不裁头、躯干、短腿、尾巴或道具；
- 不存在绿色残边、黑色硬边、网格线或相邻格串入；
- 有效主体尺寸和清晰度达到项目门槛；
- Directions、WalkModes 和 Motion 移动行的面积漂移不超过 `1.25×`；
- 相邻帧及闭环主体中心位移不超过 `10px`。

`gazeCoin:2` 的银币侧缘／最窄翻转帧是语义性薄主体，不参加“有效主体短边”统计，但仍必须通过非空、透明安全区、焦点清晰度和完整解码检查。

## 8. 自动审计结果

| 项目 | 结果 |
| --- | --- |
| 正式图集 | 13 张，通过 |
| 正式单元格 | 624 格，通过 |
| 非空、完整解码、20px 安全区 | 全部通过 |
| 最小有效主体 | `67×100px` |
| 最低清晰度 | `111.3` |
| 最大移动体型漂移 | `1.238×`（门槛 `1.25×`） |
| 最大相邻中心位移 | `8.54px`（门槛 `10px`） |

## 9. 正式文件 SHA-256

| 文件 | SHA-256 |
| --- | --- |
| `pupu-core-youthful-v12.png` | `82d63ecd2ed95d0e1ddcb5ca5a80844c364a2585525b923ee23976549e4ab7ab` |
| `pupu-life-youthful-v12.png` | `76c3479e2a276a1415aadeade4639cd7346d244df00352fa49dd494c3d387a48` |
| `pupu-directions-youthful-v12.png` | `502b1570c765b533be69e445a8af3715e129d1b566431692113832947acae6f0` |
| `pupu-touch-youthful-v12.png` | `ba2c1b9f94983181aa8e52a28b5370b5b985e7dbb77e18933478271c14d91317` |
| `pupu-routines-youthful-v12.png` | `6e9a48faf47fd6fe603cf812dd56daf8e649e403adb81028f54490b7c00f7e63` |
| `pupu-walk-modes-youthful-v12.png` | `e9f10e81ac17d5a92c1702d379feba25b67563cabd32976bdfcaae32c9235700` |
| `pupu-activity-youthful-v8.png` | `d175cd4b39e032a4ae5ea80abe64614a988840267897d344a335648b23e75590` |
| `pupu-life-equipment-youthful-v12.png` | `e1b396f63b4ec65bd2585fbd1c88e08630f40ca3794c8e3695f346e62745e0e5` |
| `pupu-motion-youthful-v12.png` | `9bd7a9900a350be89c3da796a49ace3d910d0385fa9d90dfcdd395230e1bed02` |
| `pupu-gaze-coin-youthful-v12.png` | `d349ce6d556a48a738d94d458228c233244d2ac6949245602a5d0c07799cdcd1` |
| `pupu-litter-youthful-v12.png` | `b3ce4e4c689defa11180edbbecd0759712f084dd46d049845188a9bb96963e22` |
| `pupu-specials-youthful-v11.png` | `16ca2dbbf3fb886bfcfb58b9ba6e7ce37dce64875e0aa0757ffbe2bbcbf0c927` |
| `pupu-seasonal-youthful-v10.png` | `7cdc031096509c3bf75d8ad1d7f70746c5845e0a3b67956628d7926099a1de36` |

## 10. 旧正式素材处理

下列被替换文件已从 `Pupu.Desktop/Assets` 移除，仅保存在 `AssetSources/legacy-formal-v12`：

- `pupu-core-youthful-v9.png`
- `pupu-life-youthful-v6.png`
- `pupu-directions-youthful-v9.png`
- `pupu-touch-youthful-v9.png`
- `pupu-routines-youthful-v6.png`
- `pupu-walk-modes-youthful-v9.png`
- `pupu-life-equipment-youthful-v11.png`
- `pupu-motion-youthful-v11.png`
- `pupu-specials-youthful-v10.png`

它们用于重建可追溯性，不属于 1.6.0 运行白名单。

## 11. 人工视觉复核

- 低趴观察、视线跟随和安静摆尾始终贴地，不突然端坐；
- 舔脚动作能读出抱脚、舔毛和轻啃，不扭曲后肢；
- 侧躺打滚和追尾转圈从起因到收尾连贯；
- 冻干与罐头能区分食物与急切节奏；
- 梳毛只出现孔雀蓝梳子和猫本体；
- 信任亲近没有手或人体，脸和花纹保持一致；
- 过度 rua 只表达温和边界，不出现攻击姿态；
- 自由外出背影与斜向步态的体型不突然放大；
- 如厕低头、概率抬头和埋砂动作语义清楚；
- 银币正面头像、侧缘和猫爪背面能形成完整翻转；
- 所有日常站姿和移动帧保持拿破仑矮脚比例。

## 12. 发布边界

Windows 运行包只允许包含清单登记的 13 张正式图集。不得进入应用包：

- `AssetSources` 色键源与旧素材归档；
- 图像生成原始输出；
- `.asset-work`；
- staged PNG；
- 重建脚本临时目录；
- 被替换的旧正式 PNG。

源码包可保留 `AssetSources/v12`、`legacy-formal-v12` 和可复现脚本，但必须排除 `bin`、`obj`、`dist`、临时工作目录和 staged 文件。
