# Game Anarchy 1.3.1 / Infinite Goods 6.1 Authority 审计

## 1. 供体锁定

Game Anarchy 锁定官方仓库 `Mbyron26/GameAnarchy` 的 `v1.3.1`：

- commit：`b4bed4cf9dd0e5be9d3a09b1ca3a659633368ad3`
- 程序集：`GameAnarchy`
- 程序集版本：`1.3.1.0`

Infinite Goods 锁定官方仓库 `goransh/InfiniteGoodsMod` 的当前 6.1 源码：

- commit：`b5074f9e4e342eb37cb8b17964410357dfff667f`
- 程序集：`InfiniteGoodsMod`
- `ModIdentity.Version`：`6.1`
- `AssemblyVersion` 源声明：`6.0.*`，因此 Forge 只锁 major/minor `6.0`，并另外严格核对产品版本、完整 `SettingId` 枚举和入口签名；不能把通配 build/revision 伪装成固定版本号。

两个 bridge 都只在程序集、版本、关键 type/property/method surface 全部匹配时启用。已知 IUserMod type 加载了不兼容 surface 时会进入 `blocked-mod`，不会退化为未打补丁的共享模拟。

## 2. Game Anarchy 边界

`bridge.gameanarchy` 继续同步所有支持的 shared scalar setting；UI/按键设置保持本地。客户端不能修改 shared setting，也不执行 Game Anarchy 的周期、手工 money mutation。经济与建造/拆除费用结果继续由 Forge 既有 Economy/Building/Net authority 闭环。

真实 1.3.1 源码同时证明下列路径会写当前 P4 尚未完整投影的持久状态：

- `CityServicesManager.OnPostSimulationFrame`：污染、教育覆盖、Building 垃圾/犯罪、Citizen 死亡与实例；
- `OilAndOreResourceExtension.OnAfterResourcesModified`：NaturalResource grid，并消耗 simulation RNG；
- `MilestonesExtension` / `ModUnlockManager`：milestone、prefab unlock 和 service/feature 状态；
- `FireControlManager` / fire patches：fire RNG、Building/Tree fire 状态；
- “扑灭所有着火建筑”按钮：直接遍历本机 Building slot。

这些功能不能仅靠“客户端不执行”闭环。因此 Forge 在 multiplayer 启动 fingerprint/capture 阶段明确拒绝对应配置：unlock/milestone、city-service 清理、oil/ore override 和 fire override。Oil/Ore 必须都设为 `100`，即让该 extension 不恢复被消耗资源。多人角色中 fire probability hook 被还原为继续执行原版路径且不额外消耗 Game Anarchy RNG；“扑灭所有着火建筑”动作被阻断。后续若有专用 absolute adapter，可以再缩小拒绝面。

## 3. Infinite Goods 写集

6.1 的 `TransferMonitor.OnAfterSimulationTick` 直接按本机 `ushort Building` slot 分片扫描。Forge 只让 Host 执行原循环，wire 上只使用 Building `EntityIdentityV2`。

对真实 CS1 1.21.1 `ModifyMaterialBuffer` IL 的审计显示，目标 AI 不只写 `m_customBuffer1/2`：

- Commercial：还可能写 `m_cashBuffer`、`m_outgoingProblemTimer`；
- ProcessingFacility：第二至第四输入复用 `m_youngs/m_teens`、`m_adults/m_seniors`、`m_education1/m_education2`；
- Warehouse、Fishing、PowerPlant、Shelter 与工业路径落在上述字段集合内。

因此 `bridge.infinitegoods-buildingbuffers` schema v2 按 Stable Building identity 分 128 shard，absolute projection 这十个字段，不发送 native Building ID。

ServicePoint 路径还会写 `DistrictPark` 的 material queues、临时 income/outcome 和随机 service-point native Building 引用。当前通用 DistrictPark adapter不会序列化这些 native-ID 队列；所以十个 Pedestrian/Cargo ServicePoint setting 被明确拒绝，而不是宣称已支持。

## 4. 当前证据边界

本批证明的是源码版本/type surface 锁定、已知持久写集的 adapter 或显式 block、payload 有界，以及 native ID 未进入 wire。

以下仍为 **未验证**：

- 真实双机/多机加载两 Mod 后的 Harmony patch ordering；
- Game Anarchy 已允许的经济、退款、搬迁和 unlimited-building 选项的 gameplay convergence；
- Infinite Goods 各 BuildingAI/DLC prefab 在双机上的 material-buffer convergence；
- hot join、掉线恢复和长时间模拟。

CI green 不能写成 gameplay validated。
