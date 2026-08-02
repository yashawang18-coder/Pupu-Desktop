# PersonalityBehaviorV2 运行设计

## 数据与迁移

- `TemperamentBaseline`：五维 0–1 天生性格，只由主人保存滑杆、自然语言明确修改或 Markdown 确认导入。
- `RuntimeState`：唤醒、压力、社交意愿、玩耍意愿、好奇、疲劳、安全感；只在应用运行和真实事件中变化。
- `RelationshipState`：信任、熟悉、触摸接受、主动行为接受；每维单日净变化限制为 ±0.035。
- `LearnedPreference`：以 `behavior_id|interaction_type|context` 为键，带置信度、证据日期、衰减半衰期和习惯标记。

`personality-behavior-v2.json` 的当前 `schemaVersion` 为 3，文件名继续保留以兼容 1.0。首次启动从旧底色迁移 TemperamentBaseline；旧 LearnedDelta 原样进入 LegacyLearningSnapshot。能明确映射的旧权重以 ±0.18 上限和 30 天半衰期迁移，未知键保留在 UnmappedBehaviorWeights。V2 证据和习惯再幂等迁移到四层记忆；`AppliedMigrations` 保证重复启动不叠加。

## 自主行为评分

所有自主候选来自 `BehaviorCatalog.Autonomous`，依次经过：

1. `EligibilityFilter`：硬前置、安全、环境禁止、RequiredSignals 和最短驻留；
2. `UtilityScoring`：只对合格候选计算效用；
3. `SelectionPolicy`：最高分附近 top band 的可复现随机选择；
4. `ActionScheduler`：Entering/Looping/Exiting/Completed/Interrupted。

效用公式：

```text
BaseWeight + TemperamentAffinity + RuntimeStateFit + RelationshipFit
+ LearnedPreference + ContextFit - CooldownPenalty - RepetitionPenalty
- InterruptionCost + SeededJitter
```

自主行为最短驻留硬下限为 75 秒；高干扰候选在深夜、会议、勿扰和全屏时在评分前排除。每次决策的硬过滤原因和评分分量写入 `behavior-decisions.jsonl`，高级调试页显示最近候选。

## 五维 behavior_id 覆盖

| 维度 | 主要 behavior_id |
| --- | --- |
| 活泼 | `play.roll`, `play.tail_chase`, `play.pounce`, `play.accept_toy`, `play.laser.wiggle_chase`, `play.laser.paw`, `explore.short_walk`, `explore.mouse_track` |
| 黏人 | `social.approach`, `rest.near_owner`, `social.purr`, `social.knead`, `social.respond_call`, `social.ask_attention`, `social.ask_play` |
| 敏感 | `vigilance.observe`, `vigilance.guard`, `avoid.quiet_place`, `touch.warning`, `touch.avoid`, `touch.run_away` |
| 独立 | `independent.patrol`, `self.groom`, `rest.far` |
| 淘气 | `mischief.bat_object`, `mischief.hide`, `mischief.detour`, `play.pounce`, `play.tail_chase` |

## 互动与学习

GestureInterpreter 生成 `touch/stroke/hold/lift_intent/drag/rapid_tap/release`，事件包含位置、频率、持续时间、拖动距离、当前 behavior_id、互动区域和近期历史。GestureStateUpdater 先更新 RuntimeState，随后触摸候选统一评分。

长互动使用 `InteractionLifecycle` 和 `ActionScheduler`。效果仅在 Progressed 节点实际提交；Interrupted 保留已经发生的效果并记录 completion_ratio、interrupt_reason 和累计 effects。

普通事件只更新短期状态、极小关系量和证据。记忆分为 ConfirmedProfileFact、RawInteractionEvent、EpisodicMemory、DerivedHabitPreference；行为只读取索引化派生偏好。至少 6 个会话样本、6 次发生机会且跨 3 个日期才形成派生习惯。主人纠正可立即 ±0.12 调整具体偏好，范围 ±0.65，并随时间衰减；不会修改 TemperamentBaseline。
