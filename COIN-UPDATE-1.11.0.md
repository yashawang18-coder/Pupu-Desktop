# Pupu 1.11.0 V17 正视银币更新

## 正面素材

- 以主人提供的参考图作为构图、文字和银质浮雕方向，重新生成正视银币母版。
- 外缘为完整亮银色滚花边，移除了 V16 正面帧中的黑色斜侧边；不使用透视倾斜、黑色描边、投影或圆外反射。
- 正面保留 `CAT COIN`、`MAGIC CURRENCY`、月亮、星星、圆眼长毛猫头像与银质环纹。
- 母版先从纯 `#00FF00` 色键背景转为透明 RGBA，再由脚本统一缩放到 256×256 单元格；四边实测至少 20px 透明安全区。
- `normalColor`、`normalFaded`、`unhappyColor`、`unhappyFaded` 由同一透明正面母版确定性派生，状态键、坐标、时长和翻面逻辑保持兼容；`back` 继续使用同圆心浮雕猫爪。

## 文件与重建

- 正式图集：`Pupu.Desktop/Assets/pupu-gaze-coin-youthful-v17.png`
- 透明母版：`AssetSources/v17/pupu-coin-front-master-v17.png`
- 色键留档：`AssetSources/v17/pupu-coin-front-master-v17-chroma.png`
- 确定性重建：`python scripts/rebuild-v17-coin.py`

V17 只替换银币正面材质与状态派生，不改变行为 ID、清单 schema、石化手势、猫爪背面或旧记忆格式。
