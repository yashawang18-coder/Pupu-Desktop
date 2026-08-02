# Pupu Desktop 1.7.0 素材审计

## 更新范围

- 重做 `Accio Broom`、`Apparate`、`Scourgify` 三组偏小魔法素材，并同步重做硬质晶面石化过程；全部魔法斗篷为紫色并带金色星星。
- 按主人提供的彩色／非彩色正脸参考生成五态银币：`normalColor`、`normalFaded`、`unhappyColor`、`unhappyFaded`、`back`。
- 新增 16 帧透明头部视线覆盖层，不重新生成整猫。
- 新增食物／玩具锚点 16 方向靠近形态与孔雀蓝背带遛猫 16 方向形态。
- 重做如厕、激光笔、需要距离、信任亲近、主动求关注、梳理毛发、过度 rua 和叼绳求遛等不一致动作行。
- 删除窗口上沿趴卧／巡视行为链及对应图集行；旧记忆请求安全回退为原地侧躺。

## 正式运行素材

- 13 张图集，共 77 行、616 个 256×256 RGBA 单元格。
- 3 张独立动作条，共 48 帧：
  - `Actions/pupu-gaze-overlays-youthful-v13.png`
  - `Actions/pupu-anchor-chase-16dir-youthful-v13.png`
  - `Actions/pupu-walk-harness-16dir-youthful-v13.png`
- 所有动作组在 `pupu-assets.json` 中登记触发条件、帧数、节奏、循环方式与 fallback。

## 自动审计

`scripts/verify-assets.sh` 与 `scripts/audit-asset-quality.py` 的本轮结果：

| 项目 | 结果 |
| --- | --- |
| 图集单元格 | 616 格，通过 |
| 独立动作帧 | 48 帧，通过 |
| 完整解码与非空 | 通过 |
| 20px 透明安全边距 | 通过 |
| 最小有效主体 | `68×101px` |
| 最低清晰度 | `228.0` |
| 最大移动体型漂移 | `1.238×`，门槛 `1.25×` |
| 最大相邻中心位移 | `8.54px`，门槛 `10px` |
| 五态银币键 | 通过 |
| 动作组触发说明 | 通过 |

局部视线覆盖层按透明局部素材规则验收，不使用整猫主体尺寸门槛。

## 重建与发布边界

- 使用 `scripts/rebuild-v13-assets.sh` 可从 `AssetSources/v13` 重建正式 V13 素材。
- Windows 发布只包含清单引用的正式图集、独立动作文件和运行文件，不包含色键源、旧素材归档、临时图、`bin`、`obj` 或 staged 文件。
- Windows x64 发布前仍须在安装 .NET 8 SDK 的 Windows 环境运行 `scripts/build-windows.ps1`，确认真实 WPF 编译、测试、启动和 ZIP 解压。
