# 81 Tiles 2 1.0.5 Authority 审计

## 1. 供体锁定

- 上游仓库：`algernon-A/EightyOne2`
- 审计提交：`04fa96241fab752b5d90a47a311246613e1af66d`
- 项目声明版本：`1.0.5`
- Forge 只接受程序集名 `EightyOne2`、程序集版本 `1.0.5.0` 和预期 type/property/method surface。版本或 type 漂移时不安装 bridge，兼容性协商继续 fail closed。

上游没有对应的 `v1.0.5` tag；因此这里锁的是项目文件声明为 1.0.5 的上述提交，不把 `master` 名称本身当作版本证据。

## 2. Utility authority 边界

真实源码中的 `ElectricityManagerPatches`、`WaterManagerPatches` 分别把原版网格扩展为 `462 × 462`，并把 `SimulationStepImpl` 转到 `ExpandedElectricityManager`、`ExpandedWaterManager`。后者包含电力以及供水、污水、供热的传播队列。

Forge 的边界是：

- Host 继续运行 81 Tiles 2 的原始 expanded utility step；
- `ClientLoading`、`ClientRecovering`、`ClientReplicaLive` 不运行本地 `ElectricityManager.SimulationStepImpl` / `WaterManager.SimulationStepImpl`；
- `bridge.eightyone2.electricity` 与 `bridge.eightyone2.water` 各使用 462 个行 shard，发送 Host 的 absolute result；
- 每个 simulation tick 结束时，Client 从最后已提交的 shadow projection 恢复网格，清除本地 BuildingAI 查询/消费路径造成的临时写入；
- hot join 初始化会捕获全部行 shard；七个 utility/area 开关仍由 `bridge.eightyone2` absolute state 同步。

电力行包含 conductivity、current/extra charge、pulse group 和 current/tmp electrified。水务行包含两类 conductivity、三类 pressure、三类 pulse group、water/sewage/heating current/tmp flags 和 pollution。

`m_closestPipeSegment` / `m_closestPipeSegment2` 是本机 `ushort` Net segment cache，**不进入 wire payload**。它们在 Apply 时从 Client 现有 cell 保留，避免把 Host native Segment ID 当作网络身份。网格行本身不分配实体身份；跨端持久 Net 拓扑仍只使用 Forge `EntityIdentityV2`。

## 3. 25 格之外的 area/build service

81 Tiles 2 的 9×9 area 查询、`PointOutOfArea` / `QuadOutOfArea`、BuildingTool 边界以及 utility 查询常量替换继续在每端由相同的 1.0.5 patch surface 提供。共享的已解锁 area 状态仍由 Forge Area Authority 决定；Building / Net 的玩家持久操作继续走 Forge 既有 Stable-ID authority，而不是新增 81 Tiles native-ID 指令。

因此 25 格之外的服务判断读取的是 Host-projected expanded utility cells，而不是 Client 自行演算的一套 utility grid。81 Tiles 2 还扩展 district、immaterial resource、disaster 等网格；这些不在本批“utility closure”的声明范围内，分别继续受 Forge 已有 District/Park、Disaster 等域或后续动态模拟闭包约束。

## 4. 当前证据边界

本批可以证明：源码供体与 surface 已锁定、payload 有界、native Segment ID 未上 wire、Client utility step 有角色写屏障、Host absolute rows 可通过 Extension Authority/Replica 流程投影。

以下仍为 **未验证**：

- 真实游戏同时加载 81 Tiles 2 1.0.5 与 Forge 后的 Harmony patch ordering；
- 两台/多台机器在 25 格外的供电、供水、污水和供热 gameplay convergence；
- hot join 时 924 个 utility shard 的真实耗时与网络表现；
- no-pipes、no-powerlines、electric-roads 各组合的真实 gameplay；
- 长时间运行和断线恢复。

CI green 只表示代码、契约、真实 CS1 metadata/runtime build 门禁通过，不能写成 gameplay validated。
