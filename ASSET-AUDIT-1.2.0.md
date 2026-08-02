# Pupu Desktop 1.2.0 素材审计

## 审计范围

- `pupu-core-youthful-v6.png`
- `pupu-life-youthful-v6.png`
- `pupu-directions-youthful-v6.png`
- `pupu-touch-youthful-v6.png`
- `pupu-routines-youthful-v6.png`
- `pupu-walk-modes-youthful-v6.png`
- `pupu-activity-youthful-v8.png`

合计 7 张图集、384 个 256×256 RGBA 单元格。

## 身份参考优先级

1. `pupu-face-front-tongue.jpg`：圆脸、眼距、白鼻梁、额头纹、鼻斑；
2. `pupu-face-three-quarter-alert.jpg`：轻侧脸、耳位、鼻口比例、两侧花纹；
3. `pupu-face-upward-open-mouth.jpg`：只用于抬头与张嘴动态，不决定毛发与脸型细节。

V8 生成明确要求脸颊与口鼻部圆润，不使用尖下巴轮廓；板鸭趴必须同时看见两条向后分开的后腿与后爪。

## 自动门槛

- 每格非空；
- 主体四边至少 20px 透明区；
- 主体宽高均不超过 216px；
- 有效主体短边至少 64px、长边至少 96px；
- 主体区域 Laplacian 方差至少 70；
- 图集尺寸与清单行列一致；
- PNG 可重复完整解码。

执行入口：

```bash
./scripts/verify-assets.sh
```

Windows 构建还会执行：

```powershell
.\scripts\verify-assets.ps1
```

低于门槛的素材不允许进入发布包。旧 `pupu-activity-youthful-v7.png` 已从项目引用、正式清单和 1.2.0 发布包排除；历史源只用于回溯，不参与运行。
