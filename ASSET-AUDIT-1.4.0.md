# Pupu Desktop 1.4.0 素材审计

## 1. 正式清单

| ID | 文件 | 网格 | 单元格 |
| --- | --- | ---: | ---: |
| core | `pupu-core-youthful-v9.png` | 8×6 | 48 |
| life | `pupu-life-youthful-v6.png` | 8×8 | 64 |
| directions | `pupu-directions-youthful-v9.png` | 8×4 | 32 |
| touch | `pupu-touch-youthful-v9.png` | 8×6 | 48 |
| routines | `pupu-routines-youthful-v6.png` | 8×8 | 64 |
| walkModes | `pupu-walk-modes-youthful-v9.png` | 8×8 | 64 |
| activity | `pupu-activity-youthful-v8.png` | 8×8 | 64 |
| specials | `pupu-specials-youthful-v10.png` | 8×5 | 40 |
| seasonal | `pupu-seasonal-youthful-v10.png` | 8×4 | 32 |

合计九张 RGBA 图集、456 个 256×256 单元格。

## 2. V10 生成与形象约束

V10 使用内置图像生成模式，身份参考为：

- `pupu-face-front-tongue.jpg`；
- `pupu-face-three-quarter-alert.jpg`；
- V9 Core 正式图集；
- V8 Activity 正式图集。

生成分为四个提示组：

1. 八方向鼠标视线条带；
2. 四行魔法动作表；
3. 四行节日与生日动作表；
4. 视线条带低趴体型定向修订。

共同约束：

- 银灰黑白长毛曼基康幼猫；
- 宽圆幼态脸、短口鼻、常态圆眼；
- 额头白色中轴、黄绿色眼睛、粉黑鼻头中央小黑斑；
- 长躯干、矮脚、完整大尾巴；
- 柔和半写实 Q 感，不做尖下巴或过度卡通；
- 纯绿色色键背景；
- 完整主体与道具，不裁头、爪、尾巴、扫帚、斗篷、硬币或魔杖。

生成源保存在 `AssetSources/v10`。正式透明图集由 `scripts/rebuild-v10-specials.sh` 重建，不直接把生成器画布作为运行图。

## 3. 非破坏性结论

- V9 Core、Directions、Touch、WalkModes 未修改；
- V8 Activity 未修改；
- V6 Life、Routines 未修改；
- V10 以新增 Specials / Seasonal 图集进入清单；
- 没有把魔法斗篷或节日配饰混入常态核心动作；
- 鼠标视线使用低趴常态体型，避免从安静姿势突然变成站立大头造型。

## 4. 自动检查

`scripts/verify-assets.sh` 和 `scripts/audit-asset-quality.py` 实际结果：

```text
Audited 456 cells: effective subject >= 69x96px;
minimum focus 111.3;
movement size drift <= 1.214x;
centroid step <= 9.01px.
Verified nine HD atlases, 456 non-empty cells,
20px margins, and complete PNG decoding.
```

检查范围：

- PNG 可完整解码；
- 每格非空；
- 四边至少 20px 透明区；
- 主体最长边不超过 216px；
- 有效主体短边至少 64px、长边至少 96px；
- 拉普拉斯清晰度方差至少 70；
- Directions / WalkModes 同一行表观面积漂移不超过 1.25 倍；
- 移动相邻帧和循环首尾主体中心步长不超过 10px。

## 5. 人工视觉复核重点

- 视线行：身体始终低趴，八帧只改变眼睛和小幅头向；圆脸、鼻斑和尾巴一致。
- Accio Broom：斗篷、扫帚与猫身完整，飞行循环可往返。
- Apparate：转圈到闪光消失的阅读顺序明确；粒子仍位于安全区。
- Petrificus Totalus：毛色逐渐转灰，最后两帧为清楚的头像银币。
- Scourgify：魔杖与闪光不裁切，不包含桌面截图或图标。
- Seasonal：圣诞帽、万圣节斗篷帽、红围巾只存在于独立节日图集；生日行为不永久改变体型。

## 6. 发布门槛

只有清单中的九张图集进入应用包。`AssetSources`、色键原图、生成器输出和 `.asset-work` 不进入 Windows 运行包。源码包保留生成源与可复现脚本。

