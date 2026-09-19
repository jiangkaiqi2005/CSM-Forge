# 施工点二：门禁体系治理（证据驱动裁剪）

> 状态：`proposal — design_target_not_measured`。父文档：[总施工计划书](CONSTRUCTION-PLAN.zh-CN.md)。
> 方法论前置结论：**不按直觉砍 80%**。按异常类型拆账后，真正"确定可删"的是少数；
> 大头是"合并样板（保护不减、行数减半）"和"失败响应分级（降低每个门禁的破坏半径）"。
> 裁剪一律走 D4 的打点裁决流程，用证据说话。

## 0. 现状量化（基线）

27,394 行源码，1,546 个 throw，按类型分布：

| 异常类型 | 数量 | 语义类别 | 处置方向 |
| --- | --- | --- | --- |
| InvalidOperationException | 529 | 状态机门禁 + 不变量复验 | 保留为主；响应分级（D3） |
| InvalidDataException | 387 | **远端数据解码校验**（协议帧/快照分块/桥接 payload） | **保留（红线）** |
| ArgumentNullException | 224 | 构造期校验 | 合并（D2） |
| ArgumentException | 216 | 构造期校验 | 合并（D2） |
| ArgumentOutOfRangeException | 84 | 构造期边界 | 合并（D2） |
| MissingMethod/MissingMember/TypeLoad | 55 | mod 桥反射面校验 | 合并（D2） |
| EndOfStreamException 等 | 33 | 流处理 | 保留 |

其他门禁原语：`AssertCurrent` 线程归属断言 25 处（保留）、
状态机/角色/围栏判断 117 处（保留，语义即协议）、显式 Validate/Require/Ensure 调用 209 处。

构造期校验集中度（ArgumentNullException top，合并工作量按此排序）：
`NetDomainModelV2.cs`(9)、`DistrictPolicyStateV2.cs`(7)、`ProtocolV2Messages.cs`(6)、
`JoinMessagesV2.cs`(6)、`TransportLineDomainModelV2.cs`(6)、`DistrictDomainModelV2.cs`(6)、
`BuildingDomainModelV2.cs`(6)、`AuthoritySessionV2.cs`(6)。

## D1. 真删除名单（确定项 + 候选项）

### D1-1 确定可删（有明确证据，可直接施工）

| # | 位置 | 冗余内容 | 处置 |
| --- | --- | --- | --- |
| 1 | `CompatibilityPolicyV2.cs:82-83` | `Evaluate` 每次调用都对 `host.Entries` 和 `remote.Entries` 重新执行 `CompatibilityManifest.Collect`（重复查重/上限校验），且 `Entries` getter（`Compatibility.cs:50-58`）每次调用都重新排序并分配新数组 | 构造函数中缓存 host 的字典；`CompatibilityManifest` 增加内部只读访问器暴露 `components`，remote 侧只读一次 |
| 2 | `CompatibilityPolicyV2.cs:64-69` 构造期已算好的 `hostErrors` | `Evaluate` 每次调用都把 `hostErrors` 逐条 `Add` 并再次 `errors.Sort` | `hostErrors` 直接并入返回数组后一次排序即可（微优化，顺手） |
| 3 | `KnownModBridgeRegistry.cs:52,68` | `RegisterAvailable` 与 `InstallOptionalPatches` 各自调用一次 `EnabledPluginCatalog.Capture()` | 一次捕获作为参数传递（低收益，顺手项） |
| 4 | `Forge.Core/HostSession.cs`、`ReplicaSession.cs`、`ParameterWorld`（v1 内核） | 运行时（`Forge.Runtime.Cities1`）已全部使用 V2 协调器，v1 会话类在 runtime 无真实引用（grep 唯一命中是 `HandleHostSessionFrame` 方法名，非类型引用） | **不直接删**：先确认测试引用意图（SessionTests 覆盖它们），移入 `docs/history` 对应的 reference 目录或标记 `[Obsolete]`，随分支演进归档 |

### D1-2 候选项（疑似冗余，必须先走 D4 打点确认，不允许直接删）

- `ForgeRoomPreflight.EvaluateHost` 与 `StartHostOnSimulation` 的重复清单收集：
  文档定义为"建议性预检 vs 权威检查"，语义有区别，**倾向不删**，只做 01 WP-1.6 的降频缓存。
  若打点证明预检结果从未改变权威结论且成本显著，降级为纯 UI 缓存。
- `CitiesCompatibilityCollector.Collect` 中 adapter 注册重复枚举
  （`ForgeExtensionApi.SnapshotRegistrations` 每次全量遍历）：数据量小，预期保留。

### D1-3 不删示例（重叠但语义不同，防止误伤）

- `GameAnarchyBridge.FindCompatibleAssembly`（`GameAnarchyBridge.cs:261-294`）校验的是
  **属性/方法是否存在**（决定桥可用性）；`ValidateSupportedValues` 校验的是**取值是否合规**
  （决定配置可否联机）。两者重叠在"遍历属性表"上，语义不同，都保留。
- `EnabledPluginCatalog.Capture` 在 `CompatibilityCollector` 与 `KnownModBridgeRegistry`
  两处调用：前者构建清单、后者决定桥注册，时点不同，保留。

## D2. 合并名单（保护不减，行数减半）

| # | 样板 | 现状 | 合并目标 |
| --- | --- | --- | --- |
| 1 | 构造期校验三件套 | 224×`ArgumentNullException` + 216×`ArgumentException` + 84×`ArgumentOutOfRangeException`，每处 3 行 | 扩展 `Model.cs:35-49` 现有 `Check`：`Check.NotNull(x, "name")`、`Check.Condition(cond, msg)`、`Check.Range(v, lo, hi)`；预期净减 800-1,000 行 |
| 2 | canonical 小写 ASCII 字符集校验循环 | `Compatibility.cs:15-17`（ComponentFingerprint）与 `CompatibilityPolicyV2.cs:24-26`（CompatibilityRuleV2）两份相同循环 | 抽 `Check.CanonicalId(value, maxLen, "name")` |
| 3 | 桥反射面校验 | `GameAnarchyBridge.cs`（RequiredMethod/RequiredProperty/RequiredType + FindCompatibleAssembly）与 `InfiniteGoodsBridge.cs`（同构 3 处）各自实现 | 抽公共 `BridgeSurfaceValidator` + 声明式表面描述（类型名/方法签名表） |
| 4 | 桥"可用性解析"模式 | 四个桥四种写法：GA/InfiniteGoods 用 `FindCompatibleAssembly`、`EightyOne2Bridge.cs:73` 用 `ResolveCompatibleAssembly`、`NetworkMultitoolBridge.cs:47` 用 `ResolveType(ModTypeName)` | 统一为一个泛型解析器 + 声明（与 WP-3.2 manifest 数据化衔接） |
| 5 | `Estimate`/编解码成对长度常量 | 协议各编解码器各自维护边界数字 | 集中到 `Limits`/常量，杜绝 S8 类"编码 107 字节差"再发 |

**验收口径**：`grep -c "throw new ArgumentNullException|ArgumentException|ArgumentOutOfRangeException" src` 的
原始三件套数量下降 ≥ 80%，而对应行为由 `Check.*` 承接；单元测试 183 项零回退；
新增一个"Check helper 覆盖率"检查（可选）防止回潮。

## D3. 改响应名单（降低门禁的破坏半径）

原则：**协议非法 → 踢单人；副本根不匹配 → 该客户端重基线；世界确认污染 → 才围栏。**

| # | 位置 | 现行为 | 目标行为 |
| --- | --- | --- | --- |
| 1 | `SendServerFrame`（`CitiesMultiplayerSessionV3.cs:506-513`）及广播路径 `BroadcastBatch`(Host.cs:524-529)、`PublishChat`(V3.cs:399-404)、`PublishPresentation`(V3.cs:380-386)、`BroadcastRoster`(Host.cs:385-397) | 任一 peer 发送失败抛异常 → `PollSimulation` catch → `FenceSession` → 全房 `WorldFenced` | 发送失败 = 记录诊断 + `RemoveHostPeer`；广播循环内 try，异常不逃逸（与 01 WP-1.2 同一施工项） |
| 2 | UI 线程 `StopImmediately`（`ForgeMultiplayerUi.cs:408/710/830`） | 与模拟线程竞态，可把本应安全的退出升级为围栏 | 统一走 `Scheduler.QueueSimulation`（同 01 WP-1.2） |
| 3 | `FenceSession`（`V3.cs:571-579`） | 只 `Stop()`，不清理 server/client/快照游标，轮询继续空转并反复触发错误 | 围栏 = 终态清理（复用 `StopImmediately` 的资源回收段），且围栏前增加"踢可疑 peer 观察一轮"的中间档 |
| 4 | `DemandControllerRefreshPrefix`（`KnownModBridgeRegistry.cs:132-141`） | `WorldFenced` 落到 `return false`，围栏后 DemandController 永久停摆 | `WorldFenced` 归入"单人语义放行"分支（S10） |
| 5 | `ReplicaCoordinatorV2.Receive` 的 `UnknownDomain`（`AuthoritySessionV2.cs:511-512`） | 返回决定但不改相位，副本留在 Live/CatchingUp 继续吃后续 gap | UnknownDomain 视为兼容性故障：直接 `NeedsSnapshot` 并上报诊断（版本不匹配越早显性化越好） |

## D4. 打点裁决流程（先证据、后动刀）

1. **分类打点**：为门禁引入 `GateCategory { Wire, Invariant, Lifecycle, Thread, Contract }`
   标注（attribute 或 helper 重载），触发时向 `DiagnosticRing` 记
   `(category, site-id)`；Ring 容量从 256 提到 4096（`Diagnostics.cs` 上限本身允许）。
2. **场景矩阵**：跑仓库既有 E0-E4 验收场景 + 以下压力场景：
   正常共建 / 客户端 kill -9 / 网线拔插 / mod 热切换 / 快速档大城市长会话 / 恶意构造包。
3. **判决规则**（逐门禁）：
   - 全场景 0 触发 **且** 静态分析不可达（从 runtime 入口无路径）→ 删除；
   - 高频触发 → 降级响应（改 D3）或修根因；
   - 低频 + 致命 → 保留；
   - 无法判定 → 保留并延长打点。
4. **回归纪律**：每个被删除的门禁先加"确定性失败回归测试"证明其不可达（仓库惯例）；
   183 项测试零回退是每一步的硬门槛。

## 保留红线清单（不可裁剪）

1. 全部 387 处远端数据解码校验（`InvalidDataException`）：协议帧、快照分块、桥接 payload 的
   magic/版本/长度/尾部字节/哈希校验——这是信任边界。
2. 不变量复验：`SubmitCore` 的 before/after 根校验（`HostSession.cs:174-201`）、
   `AuthoritySessionV2` 的 `AssertDomain`/result-root 校验、`ReplicaCoordinatorV2.Receive`
   的 post-apply 校验——这是"可验证联机"的本体。
3. 25 处 `AssertCurrent` 线程归属断言。
4. Bootstrap/会话状态机门禁（相位序列本身就是协议）。
5. 编解码边界常量与"先校验后分配"纪律（AGENTS.md 关键不变量）。

## 验收

- 门禁样板行数：三件套原始 throw 行数下降 ≥ 80%（D2 完成后）；
- `DiagnosticRing` 可按 GateCategory 输出触发统计，E3/E4 报告附触发分布；
- 围栏触发次数：压力场景下从"任意单点故障即围栏"降为"仅世界级污染触发"；
- 测试 183 项零回退，新增回归测试随每个删除项落地。
