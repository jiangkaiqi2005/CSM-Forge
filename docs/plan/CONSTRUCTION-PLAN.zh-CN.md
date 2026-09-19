# CSM-Forge 总施工计划书（提案）

> 状态：`proposal — design_target_not_measured`。本文档是 2026-09 代码评估后的施工提案，
> 尚未经过实现与真机验收，不构成已接受的需求。所有指标均为目标值，不是实测保证。
> 范围：基于 `feat/ultimate-dlc-mod-framework` @ `87ba383` 的实现现状评估，不适用于 main 分支的 M0 内核。
> 详细扩写：[01 验证成本模型改造](01-PERFORMANCE-VERIFICATION.zh-CN.md) · [02 门禁体系治理](02-GATE-RATIONALIZATION.zh-CN.md) · [03 mod 支持平台化](03-MOD-PLATFORM.zh-CN.md)

## 0. 诊断摘要（证据来源）

本轮评估产出的三条结构性结论（证据均为可复查的代码位置与日志行）：

1. **性能：验证的成本模型错了，不是验证本身多余。** 每个模拟 tick 都做 O(整座城) 的工作：
   区划网格 262,144 格全量扫描且逐格分配对象（`DistrictDomain.cs` ReconcileAll）、
   路网每次访问 StateRoot 都全量重建（`NetDomain.cs` CaptureWorld，3.2 万节点 + 4.9 万段）、
   每tick聚合 17+ 域根。日志基线（`output_log.txt:4748`）：`frame.avgMs=93.094ms`、
   `gc0=51`（10 秒窗口）、`managedMiB=1514.6`。原版 CSM 的对照结论：其每 tick 联机
   开销约等于零（经济包每 2 秒一次，`csm/Extensions/ThreadingExtension.cs:48-61`），
   流畅来自"热路径 O(变化量)"，而不是"没有验证"。
2. **门禁：密度高 + 失败响应单一。** 27,394 行源码含 1,546 个 throw
   （InvalidOperationException 529 / InvalidDataException 387 / 构造期校验 524 / 反射门禁 55），
   25 处线程归属断言、117 处状态机门禁。问题不是门禁多，而是：
   少数"全量重验型门禁"放在了每 tick 热路径上；且几乎所有门禁失败最终汇入
   `FenceSession` → `WorldFenced` → 全房解散，门禁密度乘以单一重响应等于自伤。
3. **mod：桥接密集，策略硬编码。** 5 个已知 mod 桥编译在 `KnownModBridgeRegistry` 里，
   版本锁死、选项黑名单硬编码、client-only 名单硬编码，未知 mod 默认拒绝。
   缺少"大多数 mod 免手写桥接"的分层机制。

## 1. 三大施工点

### 第一大点：验证成本模型改造（借鉴原版 CSM 的流畅性）

核心命题：把联机验证从"每 tick 全量重验"改为"运行期靠意图/命令转发 + 低频安全点验证 +
增量脏分片"。目标不是删除验证，而是让验证成本跟随变化量而不是城市总量。
工作包 WP-1.1～WP-1.8，见 [01 文档](01-PERFORMANCE-VERIFICATION.zh-CN.md)。

### 第二大点：门禁体系治理（证据驱动裁剪）

核心命题：真删冗余、合并样板、分级失败响应、打点裁决。按直觉"砍 80%"被评估否决；
按证据裁剪被采纳。工作包 WP-2.1～WP-2.4，含具体处置名单，
见 [02 文档](02-GATE-RATIONALIZATION.zh-CN.md)。

### 第三大点：mod 支持平台化（从桥接密集到声明密集）

核心命题：client-only 自动分类（Tier 0 免桥接）、基础域工具层同步（Tier 1 已有红利）、
声明式目录 + 通用设置适配器（Tier 2，填配置即可）、全桥仅保留给深度模拟 mod（Tier 3 例外）。
工作包 WP-3.1～WP-3.5，见 [03 文档](03-MOD-PLATFORM.zh-CN.md)。

## 2. 附带必修项（S 清单，评估中发现的确定性缺陷）

| 编号 | 内容 | 位置 | 备注 |
| --- | --- | --- | --- |
| S1 | `LocalIpv4` 盲取首个非回环 IPv4，抓到无网关虚拟网卡（实测返回 2.0.0.1），邀请码不可达 | `ForgeMultiplayerUi.cs:135-146` | 已在本机 ipconfig 实证；详见 01 WP-1.8 |
| S2 | GA 违规选项聚合报错只存在于安装版构建；仓库 HEAD 抛到第一个就停；preflight catch 只显示异常类型名 | `GameAnarchyBridge.cs:136-150`、`ForgeRoomPreflight.cs:57` | **已完成**（feat/s2-s3-error-and-text-bounds）：聚合报错回移（InfiniteGoodsBridge 同模式一并聚合），preflight 显示完整异常链 |
| S3 | `HelloV2`/`CompatibilityResultV2` 按 UTF-16 字符数校验、按 UTF-8 字节编码，22+ 汉字显示名无法加入 | `ProtocolV2Messages.cs` | **已完成**（同分支）：模型构造同时校验字符数与 UTF-8 字节数，与线缆编码上限一致；`ProtocolTextBoundsTests` 5 项回归 |
| S4 | `ClosePauseMenu` 反射调用 `new PauseMenu()` 临时实例，实际关不掉暂停菜单且泄漏组件 | `ForgeMultiplayerUi.cs:157-168` | |
| S5 | `Kick(index)` 用渲染时快照的索引重读当前列表，可能踢错人 | `ForgePlayersPanel.cs:642-646` | |
| S6 | Harmony 就绪回调不可取消、执行时不复查启用状态；mod 在等待期被禁用后补丁照常注入 | `PatchCoordinator.cs:28-79` | 子代理发现，未独立复核 |
| S7 | 树/prop 分片计数 unchecked `(ushort)` 强转，超 65,535 静默回绕导致加入端围栏 | `TreePropStateAdapters.cs:177,330` | 子代理发现，未独立复核 |
| S8 | `DecodeBatch` 接受上限比 `EncodeBatch` 最大产出小 107 字节，编解码自不一致 | `SessionMessagesV2.cs:72` | 子代理发现，未独立复核 |
| S9 | 快照文件验证后重读不再验哈希（TOCTOU）；`AbortStart` 不清理快照句柄与临时文件 | `SnapshotFiles.cs:132-139`、`CitiesMultiplayerSessionV3.cs:552-569` | |
| S10 | DemandController 桥在 `WorldFenced` 角色下落到"跳过 Refresh"，围栏后其面板永久停摆 | `KnownModBridgeRegistry.cs:132-141` | |

另有一个流程项：**构建溯源断链**。安装包 `BUILD_INFO.json` 指向的 commit `a6f8623`
在远端不可达（分支改写丢弃），且安装版与仓库任何提交存在行为差异。
处置：重新发布与安装包对应的源状态，或重新打包并记录可达 commit。

## 3. 施工顺序与依赖

**集成记录（2026-09-20）**：①-④ 与 ⑤ 第一轮、⑥ 的全部工作已通过 `--no-ff` 合并
`feat/wp-1.4b-district-dirty-hooks` 回 `feat/ultimate-dlc-mod-framework`（合并提交 `fc17958`，
已推送）；`build-runtime.ps1` 已从该提交打包 `dist/runtime/CSM-Forge-runtime.zip`，
BUILD_INFO 溯源恢复（source_commit 可达）。E3/E4 真机记录待执行：安装包后按
docs/TESTING 场景跑 1 Host + 1/2 Client、画区/修路/聊天、客户端 kill -9、
节拍窗口前后 PERF 对比，并把结果写回 E3-E4-TEST-RECORD。

```
① WP-1.1 低频安全点验证      （独立可做，体感质变，一个 PR 量级）✅
② WP-1.2 P1 分级响应          （稳定性，与①无依赖）✅
③ S2/S3 报错与字符边界        （中文用户可感知修复）✅
④ WP-1.6 预检后台化 + S1 LocalIpv4 ✅
⑤ WP-2.x 门禁治理             （第一轮完成；剩余 AE/AOORE 转换、桥反射去重、D4 全接线）部分完成
⑥ WP-3.1 client-only 自动分类 （独立）✅
⑦ WP-1.3→1.4→1.5 脏分片/增量根/线程化 （1.3/1.4a/1.4b 完成；NetDomain 分片化与 1.5 待做）部分完成
⑧ WP-3.2/3.3 目录数据化 + 通用适配器
```

## 4. 验收与日志基线

日志来源（每步改完各留一份前后对比，作为 evidence）：

- 游戏总日志：`Cities_Data/output_log.txt`（`[CSM-Forge][PERF]` 窗口、异常链）
- Forge 会话日志：`%LOCALAPPDATA%/Colossal Order/Cities_Skylines/multiplayer-logs/log-YYYY-MM-DD.txt`
- 单元测试：`dotnet run --project tests/Forge.Tests`（当前 183/183 通过，任何施工点不得回退）

性能验收目标（`design_target_not_measured`，逐项以日志对比为准）：

| 指标 | 当前基线（output_log.txt:4748） | 目标 |
| --- | --- | --- |
| frame.avgMs（联机稳态） | 93.094ms（首窗口） | < 33ms，理想 < 16ms |
| gc0（10 秒窗口） | 51 | < 5 |
| 托管内存 | 1514.6 MiB 且随会话爬升 | 稳态不爬升 |
| 联机逻辑占用/帧 | 未单独计量 | < 1ms（需先补计量点） |

## 5. 红线（不可退让项）

1. fail-closed 语义保留：远端数据解码校验（387 处 InvalidDataException）、
   不变量复验、线程归属断言、协议状态机门禁不得删除（详见 02 保留清单）。
2. 不同步车辆/行人每帧状态；结构性/决策性状态同步 + 表现层视觉漂移的边界不突破。
3. 未知 mod 默认拒绝的策略保留；平台化改变的是"支持的声明成本"，不是"默认信任"。
4. net35 编译要求、不复制游戏 DLL、不 force-push 覆盖并行工作的约定继续生效（AGENTS.md）。
5. 每个工作包合入前必须附日志/测试证据；"CI green"不能替代真机 E3/E4 记录。
