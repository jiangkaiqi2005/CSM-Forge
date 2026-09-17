# CSM-Forge 1.0 固定 Mod 兼容目标 V1

本表冻结用户当前计划使用的 Mod/内容清单。状态描述的是 **Forge 代码接入状态**，不等于双机实测通过。

| 项目 | Workshop / 来源 | Forge 分类 | 当前代码状态 |
|---|---|---|---|
| Demand Controller | 2916710759 | Host-owned bridge + Demand Authority | `bridge.demandcontroller` 同步控制配置；Client 本地 `Refresh()` 被阻断，实际 RCI 值继续由 Demand domain absolute projection |
| Network Multitool 1.3.9 | 2560782729 | Net Authority target | 已确认其工具最终调用 `NetManager.CreateNode/CreateSegment/Release*`；Host direct mutations可进入现有 Net observer，Client 复杂工具语义仍需专用 intent/shim |
| Infinite Goods | 725555912 | Host simulation target | 已确认每 tick 按本地 Building slot 调 `BuildingAI.ModifyMaterialBuffer`；不能让 Client 独立运行，后续必须 Host-only + Stable Building state bridge |
| ACME | algernon-A/ACME | ClientOnly | 已审计为相机/FPS表现层并加入 `client-mod` 白名单 |
| New Place | Map | CONTENT | 地图/资产 fingerprint；无独立 shared-simulation adapter |
| Precision Engineering (Harmony) | Workshop | ClientOnly candidate | 仅在确认实际 IUserMod type 后进入白名单；未知 type 不猜、不放宽 |
| CSLModernMap: Map&Metro Export | Workshop | ClientOnly/ReadOnly candidate | 仅在确认实际 IUserMod type 后进入白名单；未知 type 不猜、不放宽 |
| Game Anarchy 1.3.1 | 2781804786 | Host settings/simulation target | 已确认其 `ModSetting` 含经济、解锁、污染/死亡/垃圾/犯罪、资源、退款、火灾等共享模拟开关；需要 Host-owned settings bridge |
| TM:PE 11.9.4.1 | 1637663252 | Dedicated synchronized adapter | 当前继续 `blocked-mod`；在 Stable Net rule adapter + Vehicle/Path authority 完成前不允许静默加入 |
| Harmony 2.2.2-0 | dependency | Dependency | Forge 使用 CitiesHarmony；作为运行依赖进入 manifest |
| 81 Tiles 2 1.0.5 | 2862121823 | Area + utility simulation target | Area 可复用 Forge Area authority；已确认它还替换/扩展 Water/Electricity/District/Disaster/Terrain 等 Manager 行为，需单独配置/模拟闭环 |

## 原则

1. 不因为“目标 Mod”而回退到 Command Replay 或 native slot 对齐。
2. Client-only 必须基于实际源码/类型审计；不认识的 Assembly 仍 exact-match/fail-closed。
3. 修改共享世界或模拟的 Mod 必须 Host-owned，最终状态进入现有核心 domain 或 Forge extension absolute state。
4. TM:PE、81 Tiles 2、Game Anarchy 等大 Mod 可以分阶段接入，但不能把“能加载”写成“联机兼容”。
5. 最终 RC 必须在真实两机/多机环境按此清单逐项验证，并记录 Mod 版本、配置 fingerprint 与测试场景。
