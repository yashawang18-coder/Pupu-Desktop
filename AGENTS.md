# pupu Codex 项目约定

这个仓库是 Windows WPF 桌面宠物 pupu。修改前先读 `PUPU-CODEX.md` 与 `ACTION-LIBRARY.md`。

## 不可破坏的产品约束

- pupu 始终是银灰黑白长毛曼基康幼猫：幼态圆脸、黄绿色眼睛、粉黑拼接鼻头（中央有小黑色）、三头身但躯干不可短缩、矮脚和超大毛绒尾巴。
- 原地动作不得移动透明桌面窗口；只有遛猫、自主走动和愤怒逃跑可以触发桌面路径。
- 正式遛猫路线必须在运行时随机生成，不得保存固定路径；孔雀蓝背带与无背带自由模式都必须响应一级“停下”，并安全写入 Interrupted 记录。
- 安静待机以侧躺、低趴和舔脚为主，不使用长时间端坐作为默认姿态。
- 任何只读 WPF 属性都显式使用 `Mode=OneWay`；可编辑属性显式使用 `Mode=TwoWay`。
- `pupu-memory.md` 和 `events.md` 是主人可读、可编辑的记忆入口。升级不得丢失旧 JSON/JSONL 数据，JSON 只作为兼容缓存。
- 天生性格只能由主人明确修改或确认导入；普通互动和主人纠正只影响状态、关系或具体 LearnedPreference，不得回写天生性格。
- 用户离线或多天不互动不得产生状态惩罚、照料欠账、责怪或报复性动作。
- `BehaviorArbitrator` 是唯一行为资格、评分、选择、保护期、可打断性和冷却入口；ViewModel、鼠标、记忆、对话和表现适配器不得绕过或复制这些规则。
- Agent 内核只能输出 `BehaviorPresentationIntent`；具体切帧、骨骼、3D 或程序化表现必须通过可替换表现适配器解析，不得回流影响人格、记忆或仲裁。

## 新增动作

- 每格固定 256×256 RGBA，四边至少 20 px 透明安全区，不得裁尾、串格或跨单元；每个动作行共用同一缩放率和落地点，禁止逐帧拉满导致体型跳变。
- 所有帧必须完整显示头、四肢、躯干和整条尾巴；透明边缘不得出现黑色描边、色键残边或网格线。
- 触摸反应只能出现 pupu 本体，不得出现手、手臂、手指或人体局部。
- 动作组要有符合语义的帧率，循环动作使用往返序列避免末帧跳回首帧。
- 同步更新：序列定义、动作素材库、触发逻辑、性格/记忆匹配、自然语言规则、`ACTION-LIBRARY.md`、README 和 CHANGELOG。
- 图像生成默认使用内置图像生成工具，以纯 `#00FF00` 背景输出，再移除色键并逐格归一化。
- 正式图集文件名与网格登记在 `Pupu.Desktop/Assets/pupu-assets.json`；运行时由 `AssetPackService` 从外部文件读取。替换素材优先修改 PNG/清单，不得重新把文件名硬编码进 ViewModel。

## 验证

```powershell
.\scripts\verify-bindings.ps1
dotnet build .\Pupu.sln -c Release
.\scripts\build-windows.ps1
```

发布前还要检查所有图集尺寸能被 256 整除、每个格子有透明边距、发布 ZIP 可完整解压，并确认 `Pupu.exe` 是 Windows x64 GUI 程序。
