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

### WP-1.3 区划网格止血（独立小步）

- `DistrictCellStateV2`（`DistrictDomainModelV2.cs:54`）由 class 改 struct，或池化复用；
- `ReconcileAll` 支持传入"脏单元集合"（先全量接口兼容，脏集合由 WP-1.4 供给）。
- **验收**：单次 ObserveHostWorld 分配量在 DiagnosticRing 中可计量且下降一个数量级。

### WP-1.4 脏分片 + 增量根（治本）

- **写入点脏标记**：Harmony 前缀挂在各 Manager 的变更入口（模式已由
  GameAnarchy setter prefix 验证可行）；标记粒度 = 分片。
- **分片规范**：区划网格按行带分片（参照 `TreePropStateAdapters` 已有 512 分片先例）、
  路网按 node/segment id 段分片；每片独立根，聚合根 = 各片根的规范聚合
  （复用 `AuthorityCoordinatorV2.AggregateRoot` 的编码纪律）。
- **快照/加入路径不变**：绝对增量与全量快照仍按现有通道，只是根的计算变增量。
- **风险**：漏标写入口 → 假阴性发散检测。缓解：保留低频全量校验兜底（WP-1.1 的定时点），
  两级校验互补。
- **验收**：注入"绕过脏标记的写"的回归测试必须能被定时全量校验捕获（确定性失败先行的仓库惯例）。

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

### WP-1.7 LiteNetLib 池回收（待做）

- `LiteNetServerTransport`/`LiteNetClientTransport` 的 NetManager 设 `AutoRecycle = true`
  （或每条退出路径 `reader.Recycle()`）。一行止血每包 GC 压力。

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
