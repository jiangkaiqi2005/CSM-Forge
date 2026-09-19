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

### WP-1.1 低频安全点验证（收益最大，独立可做）

- **改动**：`CitiesMultiplayerSessionV3.cs:320` `AfterSimulationTick` 不再每 tick 读取
  `authority.CurrentRoot`；根哈希只在下列安全点计算：
  1. 进入暂停 / 恢复暂停；
  2. 存档（快照/检查点）时；
  3. 有客户端完成加入、被踢、断开时；
  4. 周期性定时器（建议先 5 秒，budgets.json 的 `automatic_resync_window=300000` 之内可调）。
- **运行期一致性**：沿用现有意图/命令转发（host 提交 batch → 副本按序应用），
  不新增机制。
- **发散检测延迟**：从"下一 tick"变为"最多 N 秒"。接受理由：检测后的恢复路径
  （已验证快照 + 追赶）成本远低于每 tick 全量验证；原版 CSM 连检测都没有。
- **注意**：快速档（3x）大城市下 N 秒内的批量变化变大，验证须分帧分批，单次预算 ≤ 4ms。
- **验收**：PERF 窗口新增 `verify.count/avgMs/maxMs` 计量点；联机稳态 frame.avgMs 下降；
  既有 root-mismatch 测试用例全绿。

### WP-1.2 P1 分级响应（"卡死感"的另一半）

- `SendServerFrame`（`CitiesMultiplayerSessionV3.cs:506-513`）对 `server.TrySend` 失败抛异常；
  广播路径 `BroadcastBatch`（Host.cs:524-529）、`PublishChat`（V3.cs:399-404）、
  `PublishPresentation`（V3.cs:380-386）、`BroadcastRoster`（Host.cs:385-397）遍历全体 peer，
  任意一个刚断线的 peer 都会抛异常 → `FenceSession` → `WorldFenced` → 全房解散。
- **改动**：发送失败 = 记录诊断 + 移除该 peer，不隔离世界；移除路径自身的
  `BroadcastRoster` 必须容忍发送失败（循环内 try，不重入异常处理器）。
- UI 线程直接调用 `StopImmediately`（`ForgeMultiplayerUi.cs:408/710/830`、
  `ForgeMod.cs` OnLevelUnloading 路径）与模拟线程竞态：改走
  `RuntimeServices.Scheduler.QueueSimulation`（`RequestStop` 已示范该纪律）。
- **验收**：新增确定性故障回归测试（"向已断开 peer 广播不触发围栏"）；
  手动场景：客户端 kill -9 后房主继续运行。

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

### WP-1.6 预检后台化 + 缓存

- `ForgeRoomPreflight.EvaluateHost` 的 `CitiesCompatibilityCollector.Collect()`
  （mod 程序集 SHA-256 + 全资产枚举）挪到后台线程，结果按
  （patch StatusRevision, 启用插件集合哈希）缓存；面板打开即显缓存结果 + "重新检查"按钮强刷。
- 创建房间时的权威收集（模拟线程 `StartHostOnSimulation`）保留不变——那是设计好的权威检查。

### WP-1.7 LiteNetLib 池回收

- `LiteNetServerTransport`/`LiteNetClientTransport` 的 NetManager 设 `AutoRecycle = true`
  （或每条退出路径 `reader.Recycle()`）。一行止血每包 GC 压力。

### WP-1.8 LocalIpv4 修复（S1）

- 现状：`ForgeMultiplayerUi.cs:135-146` `Dns.GetHostAddresses(主机名)` 取首个非回环 IPv4，
  本机实测抓到无网关的虚拟网卡 2.0.0.1，真实内网地址 10.244.185.121 被跳过；
  邀请码（`BuildInviteCode`）随之不可达。
- **改动**：用 `NetworkInterface.GetAllNetworkInterfaces()` 过滤：`OperationalStatus.Up`、
  有 IPv4 默认网关、隧道/虚拟交换机类型排除；取默认路由接口的 IPv4；
  UI 上允许手动改写（地址可见可编辑即已兜底）。
- **验收**：在本机（多网卡 + VPN）返回 10.244.185.121；单网卡机器行为不变。

## 3. 非目标与红线

- 不删除发散检测与 fail-closed；只改变验证的时机与成本结构。
- 不引入车辆/行人每帧状态同步；表现层视觉漂移是接受的设计边界。
- 不做主机迁移；围栏后的恢复路径维持现状（重快照/重基线）。
