# CSM-Forge 1.0 固定 Mod 兼容目标 V1

本表冻结用户当前计划使用的 Mod/内容清单。状态描述的是 **Forge 代码接入状态**，不等于双机实测通过。

| 项目 | Workshop / 来源 | Forge 分类 | 当前代码状态 |
|---|---|---|---|
| Demand Controller | 2916710759 | Host-owned bridge + Demand Authority | `bridge.demandcontroller` 同步控制配置；Client 本地 `Refresh()` 被阻断，实际 RCI 值继续由 Demand domain absolute projection |
| Network Multitool 1.3.9 | 2560782729 | Net Authority semantic shim | 以官方 `v1.3.9@2abaf77e` 为[源码基线](NETWORK-MULTITOOL-AUDIT-V1.zh-CN.md)；Add Node / Remove Node / Union / Split / Intersect，以及 Parallel 与 BaseCreate connection family（Create Connection / Curve / Loop 共用执行入口）已按真实高层入口转 Stable Node/Segment + bounded absolute geometry intent。安装 patch 前严格核对 `NetworkMultitool` 程序集版本 `1.3.9.0`、精确方法参数、Point 布局、费用与设置表面；任何漂移整体 fail-closed。Host 重建原 Mod Point[]、按 Host shared setting 计算费用并在既有 Net Authority 内调用原 Mod 逻辑，最终捕获 absolute graph mutation。仍需真实双机逐项验证。 |
| Infinite Goods 6.1 | 725555912 | Host-only simulation + Stable-ID result projection | 锁定 `b5074f9e` 的 6.1 type/enum/入口 surface；Client 原始 tick 被阻断。`bridge.infinitegoods-buildingbuffers` v2 按 Stable Building identity 投影真实目标 AI 会写的十个字段；涉及 DistrictPark native-ID queue 的十个 ServicePoint setting 显式拒绝。真实双机 gameplay 未验证，见 `GAME-ANARCHY-INFINITE-GOODS-AUDIT-V1.zh-CN.md` |
| ACME | algernon-A/ACME | ClientOnly | 已审计为相机/FPS表现层并加入 `client-mod` 白名单 |
| New Place | Map | CONTENT | 地图/资产 fingerprint；无独立 shared-simulation adapter |
| Precision Engineering (Harmony) | `PrecisionEngineering.Mod` | ClientOnly | 已从公开源码确认 IUserMod type；只提供建造测量/吸附辅助，加入 `client-mod` 白名单 |
| CSLModernMap 6.6.2 | 3781187198（已下架） | Exact-binary ClientOnly exporter | 对实际 Workshop DLL 做 IL 审计：程序集 `CSLModernMap` 6.6.2.0，IUserMod type `CSLModernMap.CSLModernMap`，SHA-256 `9fc331…c300a`。世界访问是读取/export；只写本地文件、UI、renderer 安装目录并可启动外部 renderer。仅这个 type + assembly + version + hash 进入 `client-mod`，任何漂移回到默认 exact-match，见 `CSLMODERNMAP-AUDIT-V1.zh-CN.md` |
| Game Anarchy 1.3.1 | 2781804786 | Host-owned settings + unsupported persistent-write blocks | 锁定官方 `v1.3.1@b4bed4cf` 的程序集/type/method surface；共享配置 Host-owned，Client shared setter 与手工/周期 money mutation 被阻断。尚无 authority adapter 的 milestone/resource/city-service/fire 配置在会话启动时显式拒绝，fire hook 多人态恢复原版路径。真实双机 gameplay 未验证，见 `GAME-ANARCHY-INFINITE-GOODS-AUDIT-V1.zh-CN.md` |
| TM:PE 11.9.4.1 | 1637663252 | Dedicated synchronized adapter | P6-A 已按官方 `11.9.4.1@7d1360d3` 实现 `bridge.tmpe-persistent-rules`：全部持久规则与 saved-game options 转 Stable Node/Segment、lane 转 Stable Segment + lane index，原生槽位不进 wire；版本/type/method 漂移 fail-closed。当前继续 `blocked-mod`，在 P6-B/P7 Vehicle/Path 动态 authority 完成前不允许改 Supported，真实双机未验证。见 `TMPE-PERSISTENT-RULES-AUDIT-V1.zh-CN.md` |
| Harmony 2.2.2-0 | dependency | Dependency | Forge 使用 CitiesHarmony；作为运行依赖进入 manifest |
| 81 Tiles 2 1.0.5 | 2862121823 | Area + Host-owned utility result bridge | 精确锁定 `EightyOne2` 1.0.5.0；`bridge.eightyone2` 同步七个共享开关，两个 462-row adapter 投影 expanded Electricity 与 Water/Sewage/Heating absolute result，Client utility step 被阻断；Area 复用 Forge Authority。代码闭包已实现，真实双机 gameplay 仍未验证，见 `81-TILES-2-AUDIT-V1.zh-CN.md` |

## 原则

1. 不因为“目标 Mod”而回退到 Command Replay 或 native slot 对齐。
2. Client-only 必须基于实际源码/类型审计；不认识的 Assembly 仍 exact-match/fail-closed。
3. 修改共享世界或模拟的 Mod 必须 Host-owned，最终状态进入现有核心 domain 或 Forge extension absolute state。
4. TM:PE、81 Tiles 2、Game Anarchy 等大 Mod 可以分阶段接入，但不能把“能加载”写成“联机兼容”。
5. 最终 RC 必须在真实两机/多机环境按此清单逐项验证，并记录 Mod 版本、配置 fingerprint 与测试场景。
