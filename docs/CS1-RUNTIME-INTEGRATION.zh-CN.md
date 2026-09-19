# Cities: Skylines 1 Runtime 真实接入设计

状态：`v3-integration-proposal`。本文件受 [V3 融合总架构](ARCHITECTURE-V3.zh-CN.md) 与现有 `docs/spec/RUNTIME-REPLICATION.zh-CN.md` 约束；它负责“怎样接真实 CS1”，不重新定义世界权威语义。

本文件规定 `Forge.Runtime.Cities1` 如何接入真实 CS1，不代表当前 probe 已升级为可玩 Mod。

## 1. 接入事实

CS1 有正式 Mod API：`ICities.dll`。社区 Modding 文档同时明确，`ICities` 能力有限；高级 Mod 通常还引用游戏安装目录中的 `Assembly-CSharp.dll`、`ColossalManaged.dll`、`UnityEngine.dll` 等程序集。`Assembly-CSharp.dll` 包含道路、居民、建筑等大量玩法类；Harmony 被大量 Mod 用于官方 API 没有暴露的拦截点。

因此 Forge 不采用“全靠反射”或“只用 ICities”两个极端，而是分级接入。

### 1.1 证据等级

| 等级 | 表面 | 用途 | 稳定策略 |
| --- | --- | --- | --- |
| A | `ICities.dll` 正式 API | 生命周期、线程、序列化、部分 Manager | 首选，按接口线程契约使用 |
| B | `Assembly-CSharp` / `ColossalManaged` 可直接访问类型 | 真实世界读写、Prefab、Manager、UI | 锁定游戏 build，运行探针验证 |
| C | Harmony patch | 操作捕获、写屏障、缺失回调、作用域 | 每个 Patch 有签名检查与独立卸载 |
| D | Reflection/private surface | 无 A/B/C 路径的私有入口 | 最后手段；版本绑定、失败关闭 |

不得因为某个方法“能反射到”就把它当稳定 API。

## 2. 构建与依赖

`Forge.Runtime.Cities1` 保持 `net35`。构建时只从用户合法安装的 `Cities_Data/Managed` 引用所需程序集，仓库不提交游戏 DLL、Steam DLL、Harmony 二进制或用户存档。

建议真实 Runtime 至少能按需要引用：

- `ICities.dll`
- `Assembly-CSharp.dll`
- `ColossalManaged.dll`
- `UnityEngine.dll`
- 必要时 `UnityEngine.UI.dll`

引用由 `CitiesManagedPath` 或等价显式路径提供。CI 若没有合法游戏程序集，只运行不依赖这些二进制的 Core/Protocol/Docs 测试；真实游戏构建证据单独记录。

## 3. ICities 正式入口

### 3.1 `IUserMod`

责任：

- Mod 名称、说明、设置入口；
- 启用/禁用时建立最外层 Runtime 生命周期；
- 触发 Harmony 依赖可用性检查。

`IUserMod` 本身不持有世界状态，不直接启动一个可写多人房间。

### 3.2 `LoadingExtensionBase`

已知回调：

- `OnCreated(ILoading)`：Main；
- `OnLevelLoaded(LoadMode)`：Main，地图/存档加载完成后；
- `OnLevelUnloading()`：Main；
- `OnReleased()`：Main。

Forge 用它管理 `LoadGeneration`：

```text
OnCreated
  → 建 Runtime shell

OnLevelLoaded
  → 新 LoadGeneration
  → 验证 patch surface
  → 初始化 world adapters
  → 读取/建立 Forge save metadata
  → 允许后续 HostPreparing / ClientLoading

OnLevelUnloading
  → 先失效 LoadGeneration
  → 取消 Join/Projection/Checkpoint 工作
  → 释放 UI 与世界适配器

OnReleased
  → 撤销 Forge patches / runtime resources
```

旧代次的异步回调必须在“入队时”和“执行时”两次检查 generation。

### 3.3 `ThreadingExtensionBase` / `IThreading`

官方接口提供：

- `QueueMainThread(Action)`；
- `QueueSimulationThread(Action)`；
- `OnUpdate`（Main）；
- `OnBeforeSimulationTick/Frame`（Simulation）；
- `OnAfterSimulationTick/Frame`（Simulation）。

这是 Forge 的正式线程桥。网络线程不得直接写 CS1 世界。

建议 Runtime pump：

```text
OnBeforeSimulationTick
  1. 应用已验证且属于当前 generation 的 Host intents / Replica batches
  2. 处理需要 simulation-thread 的恢复/屏障动作
  3. 建立本 tick 捕获窗口

CS1 simulation work

OnAfterSimulationTick
  1. 收束 Host 产生的持久结果
  2. 验证 Domain closure / state root
  3. 将 AuthorityBatch 交给 Replication
  4. 做作用域泄漏断言
```

真实调用顺序必须通过探针验证，不能只凭文档推断“所有游戏写入都在两个回调之间”。

### 3.4 `SerializableDataExtensionBase` / `ISerializableData`

用于在 `.crp` 内保存 Forge 元数据：

- WorldId
- Epoch
- CheckpointId
- BaseRevision
- ForgeSchema
- CompatibilityDigest
- StateRoot / Domain roots 的适当摘要
- 必要的 ID mapping schema/version

`OnSaveData` / `OnLoadData` 发生在 Simulation thread。它适合绑定元数据，但不能自动证明 `.crp` 与外部 CommitStore 是同一原子切面；Checkpoint Adapter 仍要记录真实冻结的基线 B。

## 4. Runtime 子模块

建议先按命名空间拆分，证明确有依赖边界后再拆程序集。

### 4.1 Lifecycle

`CitiesLifecycleCoordinator`

- 维护 Role、WorldId、Epoch、LoadGeneration；
- 接 ICities Loading callbacks；
- 管理 patch install/uninstall；
- 将世界不可用状态明确暴露给 Sessions/Replication。

### 4.2 Scheduler

`CitiesGameThreadScheduler`

- 封装 `IThreading.QueueMainThread/QueueSimulationThread`；
- 带 generation、预算、取消和异常返回；
- 禁止任意后台任务捕获可变游戏对象后跨线程长期持有。

### 4.3 Patch Coordinator

`CitiesPatchCoordinator`

- 统一 Harmony ID；
- 每个 patch group 声明目标 build/signature；
- 部分安装失败必须撤销本 Mod 本轮已安装 patch；
- 不替换共享 Harmony DLL。

首选使用社区统一的 CitiesHarmony 提供 Harmony 2.x 运行时；具体版本/依赖策略必须在真实环境验证后冻结。

### 4.4 World Reader

`CitiesWorldReader`

只读、规范化读取被当前支持目录声明的世界事实。第一阶段只覆盖小闭包，不声称代表全城：

- simulation identity/time/pause/speed；
- 选定 Economy/Demand 参数；
- 之后扩展 Building/Net 等。

读取必须发生在可证明一致的线程/安全点；后台线程只处理冻结后的 DTO/bytes。

### 4.5 Intent Capture

`CitiesIntentCapture`

从 CQU 已验证 Hook 中提取“玩家想做什么”，不直接发送旧 Command。要求：

- 捕获调用级作用域；
- Harmony `__state`/Finalizer 或等价机制确保清理；
- Preview 与正式提交分开；
- Client 持久提交被拦截为 Intent，不提前分配正式 ID、扣款或保存。

### 4.6 Authority Adapter

每个领域一个明确适配器，例如：

```text
EconomyAuthorityAdapter
BuildingAuthorityAdapter
NetAuthorityAdapter
```

责任：

- 在 Host simulation owner-thread 校验 intent；
- 调用真实 CS1 API；
- 收集操作完整结果闭包；
- 返回规范化结果或“结果未知/失败”；
- 不直接编码网络帧。

真实世界若已经部分改变而无法证明最终状态，返回 Faulted 并触发 Host fence，不伪造 rollback。

### 4.7 Projection Adapter

Client 不重新执行玩家决策工具，而安装 Host 结果：

- 先在纯镜像/DTO 层验证；
- 在 `ApplyScope(BatchId, DomainSet, LoadGeneration)` 中写游戏对象；
- 禁止 Apply 过程中再次被 IntentCapture 当作玩家操作；
- 发布后核对真实对象/派生索引；
- 失败进入 Client recovery。

### 4.8 Checkpoint Adapter

组合：

- ICities serialization metadata；
- CQU `SaveHelpers`/世界文件经验；
- Forge CheckpointCatalog/Manifest/CommitStore。

首版允许“短暂停顿取得一致 `.crp` + 之后流式传输”，但暂停只覆盖快照安全点，不等待远端下载/加载。

### 4.9 Compatibility Collector

从真实游戏采集：

- game build；
- DLC；
- Plugin/Assembly/Type；
- Workshop id；
- enabled state；
- declared version；
- 可得的 binary/config/content hash；
- Asset/Map 身份；
- Forge capability provider。

采集器不做最终放行判断，判断由 `Forge.Compatibility` 完成。

## 5. Manager 接口地图

下面是首版研究地图，不代表这些类型已稳定支持。每个条目在实现前必须针对目标游戏 build 验证字段、线程和副作用。

| 世界领域 | 主要真实类型/入口 | 第一用途 | 预计接入等级 |
| --- | --- | --- | --- |
| 生命周期 | `LoadingExtensionBase`, `LoadingManager` | 加载代次、世界可用性 | A/B |
| 调度 | `ThreadingExtensionBase`, `SimulationManager` | simulation-thread owner | A/B |
| 序列化 | `SerializableDataExtensionBase`, `ISerializableData` | Forge save metadata | A |
| 经济 | `IEconomy` / `EconomyManager` | 首个参数 Authority slice | A/B |
| 需求 | `IDemand` / demand manager | 首个只读/结果同步 | A/B |
| 地形 | `TerrainTool` / `TerrainManager.RawHeights` / `ITerrainManager` | Host brush/undo；136 个 absolute height shards；Client 派生层重算 | A/B；代码门禁通过后仍须真机验证 |
| 道路 | `NetManager`, `NetTool`, `NetInfo` | 后期 Net Authority/Projection | B/C/D |
| 建筑 | `BuildingManager`, `BuildingTool`, `BuildingInfo` | Building slice | B/C/D |
| 分区 | `ZoneManager` | Zone state/root | B/C |
| 行政区 | `DistrictManager` | District/policy | B/C |
| 公交 | `TransportManager`, line/tool classes | Transport slice | B/C/D |
| 居民 | `CitizenManager` | natural simulation closure | B/C |
| 车辆 | `VehicleManager` | natural simulation/presentation | B/C |
| 寻路 | Path/PathManager 相关入口 | 异步结果/引用 | B/C/D |
| 内容 | `PluginManager`, `PackageManager`, PrefabCollection | Compatibility | B |
| UI | `ColossalFramework.UI` | 房间/状态/错误 | B |

## 6. CQU Runtime 供体映射

优先研究的 CQU 目录：

- `src/basegame/Injections/*`：Intent Capture / 写入口 / 真实 API 知识；
- `src/basegame/Commands/Handler/*`：Host 执行与副作用知识；
- `src/csm/Helpers/SaveHelpers.cs`：Checkpoint；
- `src/csm/Mods/*`：Compatibility Collector；
- `src/csm/Extensions/*`：生命周期和 Tick；
- `src/csm/Panels/*`：UI；
- `src/csm/Diagnostics/*`：运行时观测；
- `src/csm/Networking/WorldTransferSender.cs`：BULK 传输工程经验。

禁止将这些目录整体复制进 Forge。每迁一类功能都要写出“Capture / Authority / Projection / Recovery”四个角色中它属于哪一个。

## 7. 第一轮实机 Gate

本节使用 `RT-*` 表示 Runtime 局部证据门，不与全项目 `IMPLEMENTATION-ROADMAP-V3` 的 `G*` 编号混用。

### RT-0 — Mod 加载

验证：

- Forge DLL 被 CS1 识别；
- `IUserMod`、Loading/Threading callbacks 真实触发；
- 记录游戏 build、Mono/Unity、线程 ID；
- 不打开网络、不改城市。

### RT-1 — Thread & Generation

验证：

- `QueueMainThread/QueueSimulationThread`；
- LoadGeneration 换图失效；
- 旧回调不会执行新世界；
- patch install/uninstall 可重复。

### RT-2 — Read-only World

读取一个 Economy/Demand 小闭包并生成规范化摘要。不得改变城市。

### RT-3 — Save Metadata

在测试存档写入并读回 WorldId/Epoch/Revision 元数据，验证不覆盖用户原始存档。

### RT-4 — First Authority Slice

选一个原版参数；两进程 Host/Client：

```text
Client Intent
→ Host real CS1 apply
→ AuthorityBatch
→ Client projection
→ AppliedAck
```

达到这一 Runtime Gate 且全局路线中的最小 v2 Transport/Session 门已经通过后，Forge 才能称为“已接入真实 CS1 的多人 Authority slice”。

## 8. 来源与验证说明

本设计参考：

- CS1 Modding API 文档关于 `IUserMod`、Loading、Threading、SerializableData 与线程标注；
- Cities: Skylines Modding Guide 对 `ICities.dll`、`Assembly-CSharp.dll`、`ColossalManaged.dll` 的程序集说明；
- CitiesHarmony 对 CS1 旧 Mono 与 Harmony 2.x 共存方式的说明；
- 官方 CSM 与 CSM-CQU 的真实源码。

社区文档并非游戏引擎正确性的最终证明。真正实现仍以目标游戏版本的本地合法程序集检查和实机探针为准。
