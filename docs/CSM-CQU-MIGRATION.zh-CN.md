# CSM-CQU → CSM-Forge 机制迁移审计

状态：`v3-integration-proposal`。
研究对象：

- 官方 CSM：`CitiesSkylinesMultiplayer/CSM@45c4ea3a402422e00e09c7f0840ae59eac2ed58c`
- CSM-CQU：`fix/nonblocking-parallel-join@8668baeb361ba9cf891dc89796cb26d4b6e26d0d`
- Forge：`main@db3d551ebdf381ed18f7709651e9355655110795`

本文件受 [V3 融合总架构](ARCHITECTURE-V3.zh-CN.md) 约束，并与 [Host Authority 渐进迁移](AUTHORITY-REPLICATION.zh-CN.md) 配套。

本文件回答一个问题：**CSM-CQU 相对官方 CSM 已经改好的哪些机制值得进入 Forge，应该进入哪一层，以什么语义进入。** CSM-CQU 保持只读研究来源，不在本任务继续开发。

## 1. 迁移分类

- **D — 直接吸收**：机制与旧 Command Replication 无关，可在改名/适配后进入 Forge。
- **S — 语义迁移**：保留解决的问题和约束，但改成 Forge 身份/Revision/AuthorityBatch 语义。
- **K — 知识迁移**：不复制旧行为，只把真实 CS1 Hook、API、副作用和故障经验转成 Runtime Adapter。
- **R — 拒绝迁移**：与 Forge 单 Host 权威或结果复制冲突。
- **P — 暂缓**：有价值但不属于第一阶段，等待更高优先级证据。

## 2. 机制矩阵

| CQU 机制 | 相对原版 CSM 的改进 | Forge 处理 | 目标模块 | 备注 |
| --- | --- | --- | --- | --- |
| SessionId / ProtocolVersion | 原版世界报文缺少明确会话与协议上下文 | S | Core / Protocol | 升级为 WorldId/Epoch/Schema/ConnectionBinding |
| SenderEpoch / Sequence | 旧连接、重复包、发送者复用可污染新会话 | S | Sessions / Protocol | 连接序号与逻辑 OperationCounter 分离 |
| SenderId 与实际 `NetPeer` 绑定 | 修复已连接客户端伪造其他玩家 SenderId | D/S | Transport / Sessions | 身份来自可信连接，不来自 payload |
| `CommandSession` 有界历史 | 原版缺少会话级执行记录 | S | Diagnostics | 不把 Command Sequence 当世界 Revision |
| 有界 Transaction | 原版 `_sendStarted` + 无界列表缺批次身份/上限/超时 | S | Replication | 迁为 AuthorityBatch 完整性和分片规则 |
| `ReplayHelper` | 异常时仍执行清理且保留原异常 | D | Runtime.Cities1 | 可直接形成通用 scope cleanup |
| `CaptureState` | Harmony Prefix/Postfix 异常不再遗留 Ignore/Collecting | D/K | Runtime.Cities1 | 改为 IntentCapture/ApplyScope 所有权 |
| Array/ID replay 异常清理 | 避免道路/建筑失败后 ID 应用状态泄漏 | K | Runtime.Cities1 | 不复制“所有副本重演同一 ID”作为架构 |
| Handler `try/finally` 稳定化 | 恢复 zone flags、经济 patch、UI 临时状态 | K | Runtime.Cities1 | 形成适配器级异常契约 |
| WorldTransfer 分片校验 | 原版坏分片/缺首包可能继续拼接 | D/S | Checkpoints / Protocol | Forge 加 SnapshotId/TransferId/hash/offset |
| `WorldTransferSender` | 流式、可靠队列背压、取消、超时、断线清理 | D/S | Checkpoints / Transport | 共享快照必须改文件/不可变句柄，避免大 byte[] |
| 并行加入队列 | 原版只允许一个 joining player | S | Sessions | Forge 用独立 JoinContext，不保留全局玩家槽 |
| 共享 Snapshot + 独立 sender | 多个加入者可共享一次保存并独立下载 | S | Checkpoints / Sessions | 与 Forge SnapshotHandle/TransferId 合并 |
| Join catch-up delta | Host 恢复后记录加入期间变化 | S | Checkpoints / Replication | **payload 从 Command 改为 AuthorityBatch/Commit** |
| Catch-up 进度/心跳 | 加入者独立报告追赶位置 | S | Sessions | 采用 AppliedRevision + Barrier H/A |
| CQU `CatchUpComplete` | 结束追赶 | R/S | Sessions | 不能 Host 单方面即刻 Connected；改 BarrierAck→Grant→Activated |
| `CapabilityManifest` | 不再只比较 Mod 展示名列表 | D/S | Compatibility | 采集逻辑可复用，判断权归 Forge Policy |
| DeploymentKind | 表达 ClientOnly/Shared/HostAuthoritative 等部署语义 | S | Compatibility | 不能相信远端自分类；能力需本地审核证据 |
| `ManifestCollector` | 采集 Workshop/Assembly/Type/Version/Asset checksum | D | Runtime.Cities1 / Compatibility | 真实采集器优先迁移 |
| `ManifestValidator/Policy` | 双向缺失、版本/hash/provider/capability 检查 | S | Compatibility | 与 Forge fingerprint policy 合并 |
| HostParameterSession | 首次显式 Host-only/authoritative 参数能力 | S | Replication / Runtime.Cities1 | 作为首个真实 Authority slice 候选 |
| RuntimeTelemetry | 发送/接收/执行/故障可观测 | D/S | Diagnostics | 状态名升级为 Received/Queued/Committed/Published/Applied |
| Subsystem Fingerprint | 能定位道路/建筑/分区/经济分叉 | S | Replication / Diagnostics | 过渡到 DomainRoot；异步样本只作候选差异 |
| Bug audit 回归组 | 身份、回放清理、分片、GS 并发有可复现测试 | D/K | Tests / Lab | 转成 Forge regression cases，不宣称实机通过 |
| SaveHelpers / WorldFileUtil | 已摸清保存、打开、加载和 CS1 生命周期 | K | Runtime.Cities1 / Checkpoints | 结合 ISerializableData 与原生 `.crp` 证据 |
| Loading/Threading 接入 | 已在真实 CSM 中使用 ICities 生命周期/Simulation Tick | K | Runtime.Cities1 | 用正式 API 重做薄适配层 |
| 中文 UI/Join/Host Panels | 已有真实 Colossal UI 入口 | K/P | UI | 逻辑状态改为 Forge Join/Recovery 状态 |
| LiteNetLib client/server | 已有 UDP、reliable ordered、peer 生命周期 | P | Transport.LiteNetLib | 可做早期 adapter；不冻结最终传输选择 |
| NAT Punch / UPnP / GS | 已解决一定程度的发现与直连 | P | Transport / Discovery | 安全与部署单独审计后决定是否复用 |
| Steam Rich Presence | 可从好友入口加入 | P | UI / Discovery | 不参与身份认证与世界授权 |
| 在线版本检查 | 原版启动时请求 API 版本，CQU 已删除 | R | — | Forge 不恢复这种硬依赖 |
| 旧 Mod 名单 `SequenceEqual` | 简单但表达力不足 | R | — | 已由 Compatibility 体系取代 |
| `CommandReceiver` 远端反射式分派 | 方便但把远端消息直接连到游戏执行 | R | — | Forge 使用固定协议 DTO + 显式路由 |
| Server 原样 Relay Client Command | 旧同步核心 | R | — | 与 Host 结果复制冲突 |
| Client 重跑 Tool/Handler 获得世界结果 | 依赖跨副本游戏确定性 | R | — | 只可作为对照实验，不进入产品路径 |
| 全局 `ClientStatus` 代表全部加入/应用状态 | 简单但混合网络、加载、复制、授权 | R/S | Sessions | 拆 Connection/Join/Load/Replica/Permission |
| 全局 `WorldLoadingPlayer`/共享 joining bool | 无法并行与可靠取消 | R | — | Forge per-JoinContext |
| Catch-up 期间全房 1x 限速 | 避免追不上但会影响正常玩家 | P/R | Sessions QoS | 只作为临时实验策略，不作为最终协议要求 |

## 3. 优先迁移批次

### Batch A：零语义争议的稳定性机制

优先级最高：

- `ReplayHelper` 异常保真清理；
- Harmony 调用级 `CaptureState` 所有权；
- 世界传输的流式读取/取消/超时/队列背压；
- Manifest 真实采集；
- RuntimeTelemetry 的有界记录；
- CQU bug audit 中能独立复现的回归用例。

这些工作不要求先完成所有 Authority 领域。

### Batch B：身份、加入和恢复机制

- 将 CQU Session/Epoch 教训映射到 Forge World/Epoch/Connection/Member；
- 将并行加入的“共享快照、独立下载”映射到 SnapshotHandle + JoinContext；
- 将 Catch-up Command log 替换成 CommitStore；
- 增加 Barrier H、Activation A、AppliedAck 和 live Gap Recovery。

### Batch C：Host-authoritative 真实领域

- 以 HostParameter 的经验做一个原版参数切片；
- 再扩展经济/规则的绝对值结果；
- Building；
- Road/Net；
- 车辆/居民/路径等自然模拟域。

## 4. 不允许的“假迁移”

以下做法即使代码能跑也视为迁移失败：

1. 把 CQU `CommandReceiver/CommandInternal` 改名后放进 Forge；
2. `AuthorityBatch.Payload` 仍然只是“请客户端调用某个旧 Handler”；
3. 用 CQU Sequence 直接充当 `CommitRevision`；
4. Client 下载完成后直接标 `Live`；
5. Host 只比较客户端当前 hash，不保存 Barrier H 的历史根；
6. 将 `ManifestEntry.Deployment` 由对端声明并直接信任；
7. Runtime 为方便直接反向引用 Protocol/Transport 的具体网络类型；
8. 因为 CQU 某个测试通过就标记真实 CS1/弱网/多人验收通过。

## 5. 许可证与来源

官方 CSM/CSM-CQU 代码基于 MIT。若直接复制或实质改写代码，Forge 必须保留相应版权和许可声明。迁移记录应在提交或源文件中注明来源路径/基线，便于以后区分：

- 直接继承的实现；
- 根据 CQU 经验重新实现的 Forge 代码；
- 完全新的 Forge 设计。

## 6. 完成定义

本迁移审计本身不代表任何机制已进入 Forge。某一行从“计划”变成“已迁移”至少需要：

- Forge 目标模块中的生产实现；
- 对应自动回归；
- 若涉及游戏对象，真实 CS1 E3/E4 证据；
- 与现有 v2 安全不变量一致；
- 不再依赖旧 CQU Command Replication 才能成立。
