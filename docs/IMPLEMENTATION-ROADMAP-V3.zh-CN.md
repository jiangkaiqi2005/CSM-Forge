# CSM-Forge V3 实施路线与验收门

状态：`v3-integration-proposal`。
目的：把 Forge v2 的正确架构、CSM-CQU 已完成的稳定化机制和真实 CS1 Runtime 接入合成一条可执行路线。

关联设计：[V3 融合总架构](ARCHITECTURE-V3.zh-CN.md)、[CQU 迁移审计](CSM-CQU-MIGRATION.zh-CN.md)、[CS1 Runtime](CS1-RUNTIME-INTEGRATION.zh-CN.md)、[Authority 迁移](AUTHORITY-REPLICATION.zh-CN.md)。

本路线意图在评审通过后取代“先大范围实现再实机”的施工顺序，但不降低 `docs/spec/` 的安全要求和 AT-01..AT-20 验收标准。当前所有 Gate 均为 `planned`。

## 1. 总原则

1. **Forge 是实现主干，CQU 是只读供体。**
2. **先真实接入，再扩领域。** 每个 Core 设计都要尽早碰真实 CS1，而不是长期停留在 ParameterWorld。
3. **先小闭包，再道路，再自然模拟。**
4. **每个 Gate 都能失败。** 失败时修适配器或缩小支持范围，不用旧 Command relay 绕过去。
5. **文档完成不是功能完成。**
6. **自动化、真实双进程、弱网/长跑证据分开记录。**

## 2. Gate 总览

| Gate | 交付 | 关键退出条件 |
| --- | --- | --- |
| G0 文档与基线 | V3 五文档、固定 Forge/CQU/官方 CSM 基线 | 交叉审查无架构冲突；仍标 planned |
| G1 真实 Mod 生命周期 | `IUserMod` + Loading/Threading + build probe | 真实 CS1 记录 build/Mono/Unity/线程，加载卸载可靠 |
| G2 Runtime 安全骨架 | LoadGeneration、Scheduler、PatchCoordinator、异常 scope | 旧回调失效；Patch 部分失败关闭；无状态泄漏 |
| G3 只读世界与存档元数据 | Economy/Demand read model + SerializableData | 真实 `.crp` 写/读 WorldId/Epoch/Revision；不修改玩法 |
| G4 真实兼容采集 | CQU ManifestCollector 思路进入 Forge | game/DLC/mod/asset 身份可重现；未知默认不放行 |
| G5 最小 v2 Session/Transport | Bootstrap、身份绑定、有界 codec、真实双进程消息链 | 不用 CQU Command 协议；连接身份与 payload 身份分离 |
| G6 首个 Authority slice | 单参数 Intent→Host→Batch→Projection→AppliedAck | 两个真实进程最终根一致；Client 不本地预提交 |
| G7 Checkpoint/传输 | `.crp` + metadata + SnapshotId/TransferId + 背压 | 大文件流式、取消、hash、坏块、超时、无无界内存 |
| G8 并行 Join | per-JoinContext、共享快照、独立下载/加载 | C/D 重叠加入，一慢/取消不阻断另一人和老玩家 |
| G9 Barrier/Activation | H/A 固定水位、BarrierAck、Grant、Activated | 不再以“下载/追赶完成”单方面标 Live |
| G10 Live Recovery | AppliedRevision、GapRequest、CommitStore、Resync | 单 Client 缺口可补；失败只隔离该 Client |
| G11 Building slice | 创建/修改/删除的实体闭包 | ID/generation、费用、引用、保存重载通过 |
| G12 Net/Road slice | 基础道路 Authority/Projection | Node/Segment/Zone/费用闭包真实验证 |
| G13 必要自然模拟闭包 | 经济、生长、居民/车辆/运输等首发必需域 | Client 不自主产生权威事实；AT-02/04 通过 |
| G14 多人 Alpha | 2–4 人、运行中加入、恢复、UI | AT-01..19 受限支持配置通过 |
| G15 RC | 24h、弱网、兼容目录、发行包 | AT-20 + 全部阻断测试通过 |

## 3. G0 — 文档与基线

交付：

- `ARCHITECTURE-V3.zh-CN.md`
- `CSM-CQU-MIGRATION.zh-CN.md`
- `CS1-RUNTIME-INTEGRATION.zh-CN.md`
- `AUTHORITY-REPLICATION.zh-CN.md`
- 本路线

审查要求：

- 不把 CQU Command Replay 重新写成 Forge 正式机制；
- 不降低 v2 Host Authority/Barrier/Recovery 不变量；
- 明确当前 probe 未实机验证；
- 固定研究 commit，避免“CQU 当前代码”漂移；
- 说明哪些是外部文档事实、哪些仍需本地程序集探针。

G0 通过后再修改生产代码。

## 4. G1 — 真实生命周期

生产最小变更：

- 扩展 `Forge.Runtime.Cities1` 的 project references；
- `IUserMod`；
- `LoadingExtensionBase`；
- `ThreadingExtensionBase`；
- runtime evidence logger。

不得：

- 开 socket；
- patch 城市模拟；
- 修改用户存档。

证据：

- Windows x64 目标游戏真实启动；
- 新游戏/读档/回主菜单/换图各一次；
- Main/Simulation callback thread id；
- build、Unity、Mono、DLC 基线。

## 5. G2 — 安全 Runtime

实现：

- `LoadGeneration`；
- `CitiesGameThreadScheduler`；
- `CitiesPatchCoordinator`；
- 从 CQU 迁入的 scope cleanup / CaptureState 模式；
- 有界 runtime event log。

故障注入：

- Prefix 抛异常；
- Original 抛异常；
- Postfix/Finalizer 清理异常；
- 加载后旧任务晚到；
- Patch 找不到目标方法；
- Unload 与后台任务竞争。

退出：任何故障都不能让旧世界任务在新世界执行，不能遗留 Forge 写权限。

## 6. G3 — Read-only + Save Metadata

只读一个小领域：

- Economy 或 Demand 的少量确定字段；
- simulation tick/pause/speed 作为上下文；
- 规范化编码与 hash。

存档：

- 使用 `SerializableDataExtensionBase` 保存 Forge metadata；
- 新测试存档另存，不覆盖用户原档；
- 读回后 WorldId/Epoch/Schema 正确；
- 元数据错误时进入明确不兼容/新世界路径。

## 7. G4 — Compatibility Collector

迁入 CQU 已验证的真实采集经验：

- PluginManager；
- PackageManager；
- Workshop id；
- Assembly/Type/Version；
- Asset checksum；
- enabled；
- capability provider。

Forge Policy 负责判定。测试：

- ClientOnly 差异；
- SharedLogic 缺失；
- 同名不同 assembly；
- hash/version 差异；
- unknown strict/relaxed（relaxed 只能用于实验房间）；
- collection failure。

## 8. G5 — 最小 v2 Session / Transport

先实现足够支撑真实双进程 Authority slice 的 v2 链路，而不是把 CQU 网络协议搬过来。

必须具备：

- 固定 Bootstrap/协议版本协商；
- 实际连接与 `ConnectionBinding` 绑定；
- 有界 frame/消息解码；
- 至少 CONTROL 与 STATE 的可靠有序语义；
- 每 Peer 字节/消息预算；
- 断开后新 binding，旧回调拒绝；
- 虚假 MemberId/ConnectionBinding/过长 payload 回归。

Transport 可先选择虚拟多进程实现，或隔离的 LiteNetLib adapter；若使用 LiteNetLib，只能复用交付/NAT 工程能力，不能复用 CQU Command wire schema。最终 GNS 决策仍按现有 v2 传输规范和真实平台证据推进。

退出条件：两个真实进程可以通过 Forge v2 DTO 交换受身份约束的无游戏副作用消息，并在断线/重连、畸形消息和背压下保持资源有界。

## 9. G6 — First Authority Slice

只选一个参数。

实现链：

```text
Client Capture
→ Forge Intent
→ authenticated session
→ Host simulation-thread adapter
→ AuthorityBatch R
→ Client validate
→ ApplyScope projection
→ real game readback
→ AppliedAck
```

必须有：

- duplicate intent；
- stale permission；
- Host local player 同一路径；
- Client projection failure；
- disconnect after Host commit before local display；
- save/reload 后不把 Client 当 authority。

这一 Gate 不依赖道路。

## 10. G7 — Checkpoint / BULK

结合 Forge storage 设计与 CQU `WorldTransferSender` 教训：

- 快照用不可变文件/句柄，不用每个发送者整份 byte[]；
- per-Transfer 流游标；
- 32 KiB 级分块可作为起始值，但以预算文件为准；
- hash/offset/总长度；
- 可靠队列背压；
- 单 Transfer 取消；
- 超时；
- 磁盘空间；
- 坏块/缺块；
- 同一快照多个读取者。

冻结窗口只覆盖取得一致切面，不等待下载完成。

## 11. G8/G9 — Parallel Join + Activation

G8 先把真实 CS1 load 接 JoinContext；G9 再完成固定 Barrier H 与 Activation A。

场景：

```text
A Host + B Live
C/D 重叠加入
C 先加载
D 慢
A/B 继续
C → Hc → Ack → Ac → Activated
D 可取消，不清 C 的 lease/log
```

禁止退化为：

- 全局 `isJoining`；
- 一个玩家加载完就解除所有加入状态；
- Host 发送 `CatchUpComplete` 后直接认定 Client Live。

## 12. G10 — Live Recovery

每个 Live Client 有实际 AppliedRevision。检测：

- 网络活着但 Apply 停止；
- revision gap；
- hash mismatch；
- projection failure；
- load/recovery 卡住。

处理：

- log 仍覆盖 → 只补缺口；
- 状态不可信/日志淘汰 → 新 checkpoint；
- 一名 Client 恢复不暂停其他成员。

## 13. G11/G12 — Building 与 Net

### Building 先于 Net

Building 至少覆盖：

- create/delete；
- prefab key；
- position/orientation；
- 费用；
- stable id/generation；
- zone/road 关联；
- 关键 flags；
- save/reload。

### Net 后做

Net 至少覆盖：

- Node/Segment create/delete；
- topology references；
- Prefab；
- zone block 影响；
- 费用；
- terrain/relocation 关联；
- ID reuse；
- tool preview 与正式 commit 分离。

若需要重跑整个 CQU Tool 才能“投影”，说明结果闭包仍未设计完成。

## 14. G13 — Natural Simulation

按依赖图逐域开启，而不是一次全城：

1. 世界时间/规则；
2. Economy/资源；
3. Building growth/lifecycle；
4. Citizen/household；
5. Vehicle/path/transport；
6. 已支持 DLC 事件。

每个域要求：

- Client 写屏障；
- Host capture；
- absolute result；
- Projection；
- DomainRoot；
- Checkpoint；
- Recovery；
- 性能预算。

缺任一首发必要域，项目保持内部实验，不标实时 Alpha。

## 15. G14/G15 — 多人 Alpha 与 RC

### G14 多人 Alpha

受限支持配置内完成：

- Host + 3 Client；
- 运行中至少两名玩家重叠加入；
- 一名慢端/取消/重连不阻断其他成员；
- Host/Client 持续编辑与自然模拟；
- 保存、重载、单 Client 完整恢复；
- UI 对 Downloading/Loading/CatchingUp/AwaitingActivation/Live/Recovering 状态诚实显示；
- 未支持工具明确禁用。

退出条件：AT-01..AT-19 在公布的游戏 build、DLC、Mod/资产范围内通过 E3，关键网络场景具备 E4 预演证据。

### G15 RC

在 G14 固定支持目录上执行：

- 四人 24 小时长跑；
- 至少 20 轮并行加入/重连；
- 弱网、短断网、慢下载、客户端故障恢复；
- 资源峰值与 p50/p95/p99 性能记录；
- 发行文件哈希、依赖清单、诊断/回退手册；
- 无未解释持久分叉、无无界内存/磁盘/日志增长。

退出条件：AT-20 及所有阻断项通过 E4。任何未跑组合只能标“未验证”，不能用相邻配置结果代替。

## 16. Transport 决策

最终目标仍可以是 GNS，但 V3 不把“更换网络库”放在真实游戏接入之前。

允许两种实现策略：

- Core/Protocol 先用虚拟 transport；
- 如需尽早双机验证，可实现隔离的 `Forge.Transport.LiteNetLib`，只复用连接/可靠交付/NAT 工程经验。

前提：Protocol v2 消息和身份语义不依赖 LiteNetLib 类型。以后替换 GNS 时不改变 Authority/Join/Recovery。

## 17. 并行开发 Lane

可以并行：

- Runtime：G1–G4，并参与 G6–G13；
- Core/Sessions：Barrier/Activation/Applied 状态机；
- Storage：Checkpoint/CommitStore；
- Protocol：v2 codec；
- Quality：CQU 回归迁移与故障注入。

不可并行写同一权威 Schema 注册表，未冻结 DTO 所有权前不允许多个分支各自发明消息字段。

## 18. 合入门槛

每个 Gate 合入前：

1. 重新基于目标分支；
2. 自动测试；
3. 文档状态真实；
4. 涉及真实游戏时附 E3/E4 记录；
5. 失败路径测试先于修复或与修复同提交；
6. 检查无游戏 DLL、存档、密钥、自动安装脚本；
7. 不用 mocked/标准 Mono 证据冒充真实 CS1。

如果任何实现只能通过恢复旧 CQU Command relay 才工作，该 Gate 失败，必须修 Runtime/Authority 设计。
