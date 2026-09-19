# 施工点一：验证成本模型改造（借鉴原版 CSM 的流畅性）

> 状态：`proposal — design_target_not_measured`。父文档：[总施工计划书](CONSTRUCTION-PLAN.zh-CN.md)。
> 本文所有垃圾量/耗时数字为代码推算或日志摘录，标注"估算"者未经逐项实测。

## 0. 对照结论：原版 CSM 为什么不卡

对 `CitiesSkylinesMultiplayer/CSM`（2026-03 仍活跃）的源码核对结论：

- 每 tick 联机开销约等于零：`OnAfterSimulationTick` 只在**每 2 秒**发送一次经济/资源
  同步与丢帧统计（`csm/Extensions/ThreadingExtension.cs:48-61`）；
- 结构性变更是**事件级命令转发**（`basegame/Commands/Data/` 下 300+ 命令类），
  天然 O(变化量)；
- 世界同步直接复用游戏自带存档（`SaveHelpers.cs` 的 `CSM_SyncSave`）；
- 代价：三方各自跑模拟、无发散检测、无增量恢复，出问题只有
  "restart the multiplayer session"（其自身日志提示）。

**借鉴的是成本模型（热路径 O(变化)），不是"不验证"。** Forge 相对原版的差异化资产
（发散检测 + 已验证快照恢复）全部保留，只把账单从"每 tick 全款"改成"低频 + 增量"。

## 1. 当前每 tick 的四笔账（问题定位）

| # | 位置 | 行为 | 成本量级 |
| --- | --- | --- | --- |
| A | `DistrictDomain.cs:182-224`（ReconcileAll） | 每 tick 全量遍历 262,144 个区划网格单元，逐格 `new DistrictCellStateV2`（class） | 单次捕获约 15-25MB 垃圾（估算：262,144 × 60-90B + 字典比较） |
| B | `NetDomain.cs:172`（CurrentRoot → CaptureWorld） | 每次访问 StateRoot 重建全路网字典（32k 节点 + 49k 段） | 数 MB 垃圾/次；`CitiesMultiplayerSessionV3.cs:320` 每 tick 访问一次 |
| C | `AuthoritySessionV2.cs` AggregateRoot | 每 tick 对 17+ 域根聚合 | 随域数量线性，非大头但叠加 |
| D | `ForgeRoomPreflight` → `CitiesCompatibilityCollector.Collect()` | UI 线程对每个启用 mod 程序集做文件 SHA-256 + 枚举全部 CustomAssetMetaData | mod 多时秒级卡顿；面板每次打开、patch revision 每次变化都重跑 |

日志基线（`output_log.txt:4748`，联机首窗口）：`frame.avgMs=93.094ms`、
`gc0=51`/10s、`managedMiB=1514.6`；第二个窗口回落到 20.7ms 说明负载来自联机层而非游戏本体。

## 2. 工作包

### WP-1.1 低频安全点验证（收益最大，独立可做）—— 已实现，待真机验收

- **诊断修正（实现时逐行复核）**：原评估认为 `AfterSimulationTick` 每 tick 读取的
  `authority.CurrentRoot` 是开销点——复核后确认它是内核缓存属性
  （`AuthoritySessionV2.cs` 仅在 PublishBatch 时更新），读取便宜。真正的每 tick
  O(城市) 成本在**观测轮询路径**：
  - `PollObservedHostDistricts` → `PublishObservedHostDistrict` → `ObserveHostWorld() →
    ReconcileAll()`：无条件全量遍历 262,144 个区划网格单元（`DistrictPolicyPolling.cs`、
    `DistrictDomain.cs:268`）；
  - `PollObservedHostZones` → `ReconcileWorld() → CaptureSparse`：无条件全量遍历
    32,768 个区格（`NetSessionBridge.cs`、`ZoneDomain.cs:137-139`）。
  另注意：`NetDomainBase.CurrentRoot`（`NetDomain.cs:172`）每次读取都全量
  `CaptureWorld()`，作用于意图提交/观测发布路径（每次修路约 3-5 次全路网遍历）——
  属于 WP-1.4 增量根的范围，本工作包不动它。
- **实现**（分支 `feat/wp-1.1-lazy-root-verification`）：
  - `Forge.Core/VerificationCadence`：单调时钟节拍器，`ShouldVerify`（窗口消费语义）+
    `Force()`（安全点/权威提交后立即触发）；`CadenceTests` 7 项确定性回归测试。
  - 区划/区格轮询接入 5 秒窗口（`GridVerifyIntervalMilliseconds`）；brush/策略补丁
    驱动的发布保持即时；`BroadcastBatch` 在 Net/Zone/District 批次后 Force 两个节拍器，
    "修路 → 新区块 → 区格同步"仍在一 tick 内收敛（与旧行为一致）。
  - `AfterSimulationTick` 保持不变（读取确认为缓存语义）。
- **残留风险**：未被补丁覆盖的宿主侧区划/区格写最多延迟一个窗口（5 秒）才发布；
  由 Force 钩子 + 定时窗口两级兜底。副本端 Committed 索引随发布批次推进，语义不变。
- **验收**：单测 190/190（含 7 项新增）；net35 构建 0 警告 0 错误；真机日志对比待
  E3/E4（注意：`[CSM-Forge][PERF]` 计时器存在于安装版构建但不在本分支源码中，
  属构建溯源断链 S 项，需先回移才能出对比数据）。

### WP-1.2 P1 分级响应（"卡死感"的另一半）—— 已实现，待真机验收

- `SendServerFrame`（`CitiesMultiplayerSessionV3.cs:506-513`）对 `server.TrySend` 失败抛异常；
  广播路径 `BroadcastBatch`（Host.cs:524-529）、`PublishChat`（V3.cs:399-404）、
  `PublishPresentation`（V3.cs:380-386）、`BroadcastRoster`（Host.cs:385-397）遍历全体 peer，
  任意一个刚断线的 peer 都会抛异常 → `FenceSession` → `WorldFenced` → 全房解散。
- **已实现**：
  - 新增 `TrySendServerFrame`（非抛出变体）+ `EvictDeadHostPeers`；
    `BroadcastBatch`/`PublishChat`/`PublishPresentation`/`PumpSnapshotTransfers` 改为
    "收集失败 peer → 循环结束后逐个 `RemoveHostPeer`"（避免字典遍历中变更）；
  - `BroadcastRoster` 容错但**不驱逐**——它会在 `RemoveHostPeer` 内部运行，递归驱逐
    会造成重入，死 peer 由自己的失败路径或传输断连事件回收；
  - 单 peer 的 bootstrap/回执发送保持抛出语义：它们在 `DrainServerEvents` 的
    per-peer try 内，异常本就降级为"断开该 peer"。
- UI 线程直接调用 `StopImmediately`（`ForgeMultiplayerUi.cs:408/710/830`、
  `ForgeMod.cs` OnLevelUnloading 路径）与模拟线程竞态：改走
  `RuntimeServices.Scheduler.QueueSimulation`（`RequestStop` 已示范该纪律）。
- **已实现**：三处 UI 调用点改为 `RequestStop()`（失败时回退直呼）；主菜单场景
  （无城市身份）由 `RequestStop` 内部直呼——与旧行为一致且同线程安全。
  `ForgeHostGamePanel.Update` 补充"mode 变为 Offline 时重跑预检"，
  适配异步停止后面板已打开的时序。`ForgeMod` 的卸载钩子保持直呼不变——
  卸载期模拟 tick 停止，队列化可能导致停止动作永不执行；其线程语义另立工作包分析。
- **验收**：net35 构建 0 警告 0 错误、190/190 测试（会话层无游戏无关测试框架，
  新增确定性回归需真机场景：客户端 kill -9 后房主继续运行，聊天/批次不炸房）。

### WP-1.3 区划网格止血（独立小步）—— 已实现，待真机验收

- `DistrictCellStateV2` 由 class 改为 **struct** 并实现 `IEquatable`（字段级相等 +
  GetHash 重写），262k 格 reconcile 不再每格分配一个堆对象（估算省 ~15-25MB/次捕获垃圾）；
  该类型构造后只读，值语义与字典/编解码行为一致（`DistrictCellValueTests` 4 项回归：
  字段级相等、default 空格种子、字典语义、构造校验）。
- 连带清理：`DistrictMutationV2` 的 cell 非空循环、`SeedCell` 的 NotNull、
  `CellEquivalent`/`ApplyCell` 的 null 比较——值类型无 null 可言，校验保留在构造器内。
- **验收**：单测 209/209；net35 构建 0 警告 0 错误；真机 PERF 对比待 E3/E4。

### WP-1.4 脏分片/增量根（治本）—— 第一片已实现，待真机验收

- **已实现（Core）**：`Forge.Core/DistrictShardedCellIndex`——262,144 格按 512 分片 × 512 槽
  组织（TreePropStateAdapters 分片先例），每片缓存规范根、聚合根 = 512 片根的哈希；
  单格变更只重编码所在分片。两类独立的脏概念：hash-dirty（内部，聚合根懒重算）与
  source-dirty（运行时提示，Harmony 钩子置位、re-读该分片的 reconcile 清除）。
- **确定性失败回归（DistrictShardedCellIndexTests，4 项）**：核心一项是
  `BypassedWriteIsInvisibleToCheapReconcileAndCaughtByFullVerify`——绕过 source-dirty
  标记的写对廉价路径不可见（根保持旧值），必然被全量校验捕获并收敛到与诚实直写一致的根；
  另有 source-dirty 驱动廉价路径、根稳定性与插入顺序无关、边界与空格移除。
- **已接入**：`DistrictStateIndexV2` 的 cells 换成分片索引；`Root` 公式升级为
  FGD3（实体 + 分片聚合根）——之前每次 Root 读取都全量编码 + 哈希 262k 格，
  现在只重算 hash-dirty 分片；`ReconcileCellsSourceDirty`/`ReconcileCellsFull`
  供运行时两级校验使用，与 WP-1.1 的 VerificationCadence 窗口衔接
  （窗口触发 = 全量校验；Harmony 钩子置位 = 廉价路径）。
- **剩余（下一片）**：`NetDomain.CaptureWorld` 的同构分片化；根公式升级 FGD3 需要宿主/副本
  同版本（混跑由 schema 检查按设计拒绝）。

### WP-1.4b Harmony 脏钩子 + 廉价路径（第二片）—— 已实现，待真机验收

- **确定性回归先行**（`DistrictShardedCellIndexTests`）：`HookSequenceIsCaughtByCheapPathAndConfirmedByFullWindow`
  钉死运行时契约——钩子置位 → 廉价路径恰好捕获 1 处变更 → 全量窗口确认 0 漂移 →
  未触分片保持原样；`StateIndexCheapPathCarriesCellsAndKeepsRootCoherent` 覆盖
  DistrictStateIndexV2 集成（聚合根 = 全量重算、Root 读取稳定）。
- **钩子**（`DistrictPatches.cs`）：ilspy 查证 `DistrictManager` 的网格写入点为
  `ModifyCell(int x, int z, Cell)`（brush/mods/ReleaseDistrictImplementation 都落到它）——
  postfix 按 `z*512+x` 置 source-dirty；`ReleaseDistrict` postfix 置全部分片 dirty
  （释放可能跨全图清格）。两钩子仅 HostLive 生效、`IsApplying` 跳过。
- **廉价路径**（`DistrictPolicyPolling.PollObservedHostDistricts`）：
  1. 有 source-dirty 分片 → `ObserveHostSourceDirty()` 只重读那些分片
     （每分片 512 次 CaptureCell）→ 变更发布为权威批次；
  2. 无 → WP-1.1 节拍窗口到点才全量 reconcile（兜底）。
  实体捕获仍随廉价路径运行（≤255 个区划，很便宜）——宿主新建区的实体随其首批格子一起发布。
- **接线链**：patch → `CitiesMultiplayerSessionV3.MarkDistrictCellSourceDirty` →
  `DistrictCompositeAuthorityDomain` → `DistrictAuthorityDomain` → `Committed.MarkCellSourceDirty`。
- **验收**：单测 215/215（+2）；net35 构建 0 警告 0 错误；真机验收项：画区/改区后
  一个 tick 内同步（钩子路径）、无操作时区划 reconcile 零成本（节拍窗口）、
  人工绕过（未钩写入口）在窗口内被全量校验纠正。

### WP-1.5 哈希/编码移出主线程 —— 范围重新划定（2026-09-20）

原设想"把 SHA-256 移到工作线程"。WP-1.4a/c 落地后按代码事实复核：
1. 区划根已增量维护——每窗口只重编码 dirty 分片（通常 0-3 片），
   全量编码（262k 格 ≈ 2MB，20-50ms）仅在节拍全量校验时发生；
2. 路网根已缓存（1.4c）——live 捕获只发生在显式 diff 点；
3. 真正的主线程绑定成本是**游戏状态读取**（CaptureCell/CaptureNode 走
   NetManager/DistrictManager 缓冲区，必须留在游戏线程）——移出主线程
   既不安全也不可行；
4. 可行且值得的剩余优化：全量窗口的游戏重读频率调优（依据 E3/E4 数据
   把 5 秒窗口放宽或改为"钩子零标记则跳过"），以及快照字节在 sim 线程
   拷贝、SHA-256 在工作线程的两段式（仅当数据显示仍有必要）。

**结论**：WP-1.5 的编码/哈希线程化暂缓，先由 E3/E4 真机数据决定。
该决定记录为设计事实，不是未完成项。

### WP-1.4c 路网根缓存语义 + 节拍全量兜底（第三片）—— 已实现，待真机验收

- **语义变更**：`NetDomainBase.CurrentRoot` 由"每次读取全量 `CaptureWorld()`"改为
  **返回缓存 `Committed.Root`**（提交快照的根，Reconcile/Apply 时更新）。
  每次 修路提交/StateRoot 断言/Metadata.Update 从 1-5 次全路网遍历降为 0 次；
  live 捕获只剩显式 diff 点（`ExecutePlayer` 前后、`ObserveHostChanges`）。
- **语义代价（有意接受并记录）**：`StateRoot` 从"live"变"committed"后，
  Submit 时 `AssertDomain` 不再能发现"绕过 patch 的路网写"；该检测移交给
  **节拍驱动全量 Observe**——`PollObservedHostNetFull`（`NetSessionBridge.cs`）在
  WP-1.1 的 5 秒窗口重捕获 live 图并发布 diff（接 `NetPatches`/`NetBulldozePatches`
  未覆盖的写入口），与 WP-1.4b 的两级校验模式一致。
- **可测面回归**（`NetStateIndexCachedRootTests` 5 项）：`NetStateIndexV2.Root` 记忆化 +
  Apply/Upsert/删除全部失效缓存——"缓存失效遗漏"正是缓存语义下最危险的错误，
  插入顺序无关性与种子/Apply 等价性一并钉死。
- **验收**：单测 220/220（+5）；net35 构建 0 警告 0 错误；真机验收项：修路点击延迟下降
  （每次点击全路网遍历 ~5 次 → ~3 次，剩余由后续增量捕获消减）、节拍窗口内
  绕过写被自动发布。

### WP-1.5 哈希移出主线程（依赖 WP-1.4）

- tick 边界主线程只拷贝脏分片字节（双缓冲/代际指针），SHA-256 与编码在工作线程；
- 结果回投 `BoundedInbox`（`Diagnostics.cs` 已有该原语），下个安全点消费。

### WP-1.6 预检后台化 + 缓存（独立小步）—— 已实现，待真机验收

- **诊断修正（实现时复核）**：`PackageManager`/`PluginManager`/`SteamHelper` 是主线程亲和的
  游戏单例，不能挪到任意工作线程；"后台化"的正确形态是**排队到模拟线程评估 + UI 只读缓存**。
- **实现**：`ForgeRoomPreflight` 增加缓存层——`RequestEvaluation(force, patchRevision)`
  排队到模拟线程（`Scheduler.QueueSimulation`，线程亲和纪律不变），UI 每帧只读
  `PeekCached()`；缓存按 patch revision 键控，显式"重新检查"按钮与 revision 变化强制重评。
  `ForgeHostGamePanel` 不再在 Start/Update/CreateRoom 内联执行收集（此前每次面板打开、
  每次 patch revision 变化、每次点击创建都会在 UI 线程全量收集）；`StopImmediately` 清缓存。
- **验收**：单测 195/195；net35 构建 0 警告 0 错误；真机确认"打开开房面板不再卡顿、
  检查期间显示'正在检查'、按钮在报告落地后解锁"。

### WP-1.7 LiteNetLib 池回收（已实现）

- `LiteNetServerTransport`/`LiteNetClientTransport` 的 NetManager 构造已设
  `AutoRecycle = true`（`{ AutoRecycle = true }` 初始化器），每次接收后回收池化包，
  消除逐包 GC 压力。

### WP-1.8 LocalIpv4 修复（S1）—— 已实现，已真机验证

- `ForgeMultiplayerUi.LocalIpv4` 改用 `NetworkInterface.GetAllNetworkInterfaces()`：
  Up 且拥有 IPv4 默认网关、非 Loopback/Tunnel 的接口优先，排除 169.254 链路本地地址；
  无匹配时回退原 DNS 探测。注意 `GatewayIPv4Addresses` 在 net35/net8 不可用，
  使用 `GatewayAddresses` 按族过滤（真机验证时抓出该编译错误）。
- **真机证据**：同一算法的独立控制台程序在本机（多网卡 + Hyper-V/WSL 虚拟交换机）
  返回 `10.244.185.121`（以太网 2，网关 10.244.0.1），正确跳过无网关的
  `2.0.0.1`（以太网 4）与两个 vEthernet 虚拟交换机。

## 3. 非目标与红线

- 不删除发散检测与 fail-closed；只改变验证的时机与成本结构。
- 不引入车辆/行人每帧状态同步；表现层视觉漂移是接受的设计边界。
- 不做主机迁移；围栏后的恢复路径维持现状（重快照/重基线）。
