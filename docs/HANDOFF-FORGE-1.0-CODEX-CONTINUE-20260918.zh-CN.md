# CSM-Forge 1.0 Codex 继续交接 — 2026-09-18 15:37

> 本文件是 `docs/HANDOFF-FORGE-1.0-CODEX-20260918.zh-CN.md` 的继续交接。  
> 目标不是重新设计，而是让 Codex 从当前已经全绿的代码继续向 1.0 code-complete candidate 推进。

---

## 0. 仓库 / 分支 / 当前事实

- 仓库：`jiangkaiqi2005/CSM-Forge`
- 唯一继续开发分支：`feat/ultimate-dlc-mod-framework`
- Draft PR：#2 `WIP: ultimate DLC and mod compatibility framework`
- 禁止 merge：`main` / `develop`
- 禁止切回：
  - `feat/ultimate-dlc-mod-framework-2`
  - `feat/ultimate-dlc-mod-framework-temp-delete-me`
- 禁止回退：
  - Command Replay
  - Client 重跑 Host tool command
  - native BuildingId / NetId / ParkId / VehicleId 等网络同步

### 本轮开始时的交接 SHA

- 原交接提交：`b612b6aa6a711e9f72f0c9c59ec5a45fdb610658`
- 原最后代码全绿基线：`484981402a336c7d91891a708cf540dae9d2bbd2`

### 本轮新增并确认的代码提交

1. `4105afc6f90eb5e84de5b58e17b5228108d2d616`  
   `fix(alpha): hand Tree and Prop fully to decoration authority`

2. `8e960081d890c484da59921391749639807a6c7f`  
   `feat(net): add semantic Network Multitool authority shim`

3. `8379f949d0451eb008284a484f207cd751b70a89`  
   `feat(net): cover Multitool parallel and connection creation`

**当前最后一个已确认完整代码全绿基线仍然是：**

`8e960081d890c484da59921391749639807a6c7f`

注意：`8379f949...` 在写本交接时已经存在，但 **CI 是红的**。当前 handoff docs commit 位于它之后，因此 Codex 接手时不能把当前 HEAD 当作 green。必须先修 `8379f949...` 的失败并重新证明 exact HEAD 全绿，不能 reset 回 `8e96008...`，除非用户明确要求回退。

---

## 1. 先读什么

先完整阅读：

1. `docs/HANDOFF-FORGE-1.0-CODEX-20260918.zh-CN.md`
2. 本文件
3. 原文件要求的五份 V3 核心设计文档
4. `docs/DLC-COVERAGE-V1.zh-CN.md`
5. `docs/MOD-ADAPTER-API-V1.zh-CN.md`
6. `docs/MOD-COMPATIBILITY-TARGET-V1.zh-CN.md`
7. `docs/TESTING.zh-CN.md`

不要重新讨论“CSM 应该怎么同步”。现有架构继续固定为：

```
Player / Mod semantic intent
-> Host authoritative execution
-> capture final absolute state
-> AuthorityBatch
-> Client absolute projection
```

---

## 2. 8e96008 的 CI 已经完整确认全绿

Workflow：

- docs-contract run：`35316423526` — success
- kernel-ci run：`35316423518` — success

其中：

- `kernel-ubuntu-22.04` — success
  - net35 + net8 编译
  - net8 production-kernel tests
  - Linux Mono 安装
  - **实际 net35 Mono tests**
- `kernel-windows-2022` — success
  - net35 + net8 编译
  - net8 production-kernel tests
- `runtime-package-windows` — success
  - 隔离 CS1 reference assemblies
  - CS1 DLC/core simulation metadata probe
  - 真实 `Forge.Runtime.Cities1` build
  - 可安装 package
  - provenance / ZIP / artifact

Runtime Artifact：

- Artifact ID：`10535696838`
- Name：`CSM-Forge-runtime-8e960081d890c484da59921391749639807a6c7f`
- Digest：
  `sha256:2b73244d4ab0f474cf6eeb377c5c07d8a6147eb51ee93a9e7ac031e80348a9de`

**这只证明 `8e96008...` 的编译、测试、真实 reference build 和包完整性。没有真实双机 gameplay 证据。**

### 2.1 后续 `8379f949...` 当前失败状态

`8379f949d0451eb008284a484f207cd751b70a89` 试图继续覆盖：

- `CreateParallelMode.Create`
- `BaseCreateMode.Create`
- Parallel
- Create Connection / Curve / Loop 共用 connection family
- bounded absolute geometry `NetMultitoolPointV2[]`
- Host 侧重建原 Mod Point[]
- Host 侧按 shared `NeedMoney` 设置计算 construction cost

但该提交的 CI：

- docs-contract：success
- kernel-ci：**failure**
- runtime-package-windows：skipped

已确认第一处失败是纯测试 API 编译错误：

`tests/Forge.Tests/NetDomainV2Tests.cs:119`

使用了：

`Assert.False(connection.SecondStart);`

而当前自定义 `Assert` 没有 `False` 方法，Windows/Linux、net35/net8 都因此编译失败。

**Codex 第一件事：修这个测试 API（使用仓库现有 Assert 风格），然后重新跑 exact HEAD CI。**

由于 runtime-package 被跳过，修掉这个编译错误后仍必须继续等真实 CS1 reference runtime build；不能预设 `8379f949...` 的 reflection/signature 一定正确。

`BUILD_INFO.gameplay_validation` 必须继续保持：

`NOT RUN BY CI`

---

## 3. P1 Tree / Prop 已完成的代码收口

### 问题

旧 `AlphaSafetyPatches.cs` 和新的 `DecorationAuthorityPatches.cs` 同时 patch：

- TreeManager.CreateTree / MoveTree / ReleaseTree
- PropManager.CreateProp / MoveProp / ReleaseProp

不能依靠 Harmony priority“碰巧”让新 Authority 赢。

### 已做

提交：`4105afc6f90eb5e84de5b58e17b5228108d2d616`

#### 3.1 旧 Alpha barrier 正式移除 Tree / Prop

`AlphaSafetyPatches.cs` 现在只留下：

- `TerrainTool.ApplyBrush` fail-closed

不再 patch Tree / Prop。

#### 3.2 Tree / Prop 正式由 Decoration Authority 独占

继续使用：

- `TreeStateAdapter`
- `PropStateAdapter`
- Stable `EntityIdentityV2`
- sharded absolute state
- Client semantic intent
- Host allocate Stable ID
- Replica local materialization

#### 3.3 Building / Net collateral clear 显式处理

`DecorationAuthorityPatches.cs` 新增明确判断：

- `BuildingToolIntentScope.Active`
- `ToolsModifierControl.toolController.CurrentTool is NetTool`

Host Building / Net 合法清树清 Prop 时：

- 不转成独立 Tree/Prop 玩家 intent
- 放行原生 collateral side effect
- `TreeStateAdapter.MarkDirty()` / `PropStateAdapter.MarkDirty()`
- 后续 absolute shard recapture

因此没有再把旧 safety patch 的“特殊放行”偷偷留在另一层。

### 已更新

- `tests/docs/test_minimum_playable_alpha.py`
- `tests/docs/test_decoration_authority.py`
- `ForgeSettingsPanel.cs`
- `scripts/build-runtime.ps1`
- `README.md`
- `docs/DLC-COVERAGE-V1.zh-CN.md`

### 状态

**P1 代码门禁完成。**

仍未完成：

- Tree Host create/move/delete 真机
- Client intent -> Host -> projection 真机
- hot join 后 identity
- slot reuse
- 多机 collateral clear

这些必须在 P9/E3/E4 标为未验证，不能写成 PASS。

---

## 4. P2 Network Multitool 当前做到哪里

提交：`8e960081d890c484da59921391749639807a6c7f`

公开源码供体：

- `MacSergey/NetworkMultitool`
- Stable csproj 版本：`1.3.9`
- Workshop Stable ID：`2560782729`

已直接审计真实源码中的高层入口：

- `AddNodeMode.InsertNode(ushort, Vector3)`
- `RemoveNodeMode.RemoveNode(ushort)`
- `UnionNodeMode.Union(ushort, ushort)`
- `SplitNodeMode.Split(ushort, Vector3, IEnumerable<Selection>)`
- `IntersectSegmentMode.IntersectSegments(ushort, ushort)`

这些方法内部最终仍走：

- `NetManager.CreateNode`
- `NetManager.CreateSegment`
- `ReleaseNode`
- `ReleaseSegment`

### 4.1 新增 semantic Net intents

`src/Forge.Core/NetDomainModelV2.cs` 新增：

- `MultitoolAddNode`
- `MultitoolRemoveNode`
- `MultitoolUnionNodes`
- `MultitoolSplitNode`
- `MultitoolIntersectSegments`

wire 只携带：

- `EntityIdentityV2` Node/Segment
- 必要的 position
- Split 的 Stable Segment identity 集合

**没有发送 native ushort NodeId/SegmentId。**

### 4.2 新增 Optional Runtime Shim

文件：

`src/Forge.Runtime.Cities1/NetworkMultitoolBridge.cs`

策略：

1. 只有检测到 `NetworkMultitool.Mod` 才安装；
2. 安装前一次性解析 5 个真实高层方法；
3. 任意必需方法找不到就 fail closed，不做半套 patch；
4. Client / Host-local 操作先转 `NetIntentV2`；
5. Host 在已有 `NetAuthorityDomain.ExecutePlayer` 内调用原 Mod 高层逻辑；
6. 调用时在 `RuntimeScopeGuard.EnterApply(Load, NetAuthorityDomain.Id)` 内；
7. 原 Mod 内层 NetManager 操作因此不会被 Client barrier/再次 capture；
8. 完成后仍由现有 `Reconcile()` 捕获最终绝对 graph mutation；
9. Client 仍由现有 `NetReplicaDomain` 安装 Host 最终结果。

这不是 Command Replay。Mod 的“命令”没有广播给 Client；Client 收到的仍是 Forge Net absolute mutation。

### 4.3 Registry

`KnownModBridgeRegistry.cs` 已增加：

- `NetworkMultitoolBridge.InstallOptionalPatches(harmony)`
- `NetworkMultitoolBridge.ResetPatchState()`

### 4.4 测试

新增：

- `tests/docs/test_network_multitool_bridge.py`

扩展：

- `tests/Forge.Tests/NetDomainV2Tests.cs`

已经验证：

- semantic intent codec
- Stable identity
- Split target 去重/上限
- 高层 method patch contract
- Host 复用已有 Net Authority

### P2 当前状态

**首批五种模式（Add/Remove/Union/Split/Intersect）在 `8e96008...` 已实现且完整 CI green。**

**Parallel / Connection family 已在 `8379f949...` 写入代码，但尚未通过 CI，不能算完成。**

`8379f949...` 的意图包括：

- `MultitoolCreateParallel`
- `MultitoolCreateConnection`
- bounded `NetMultitoolPointV2[]`
- patch `CreateParallelMode.Create`
- patch `BaseCreateMode.Create`
- Host 侧重建 Point[]
- Host 侧 construction cost 计算
- connection family 共用 BaseCreate 执行入口

接手后先修测试编译并让 runtime-package 真正执行，再判断这批代码是否需要签名修正。

仍必须明确：

- 未真实加载 Network Multitool 1.3.9 做双机测试；
- 未验证其第三方依赖/实际 Workshop assembly 在用户环境中的 type 漂移；
- Parallel / Connection 当前是“代码已写、CI 未过”，不是完成；
- 未做任何 Multitool 模式的 Host/Client/hot-join E3/E4。

---

## 5. Codex 接手后的第一件事：先修当前红 CI，再继续 P2

### 5.0 先修 `8379f949...`

第一处已知错误：

`tests/Forge.Tests/NetDomainV2Tests.cs:119`

不要使用不存在的 `Assert.False`，改成仓库测试框架支持的等价断言。

然后必须重新确认：

- docs
- Windows
- Linux
- net8
- Mono/net35
- real CS1 metadata probe
- real CS1 reference runtime build
- runtime-package-windows

如果 Runtime CI 报 Multitool reflection/signature 不存在：

- 回到真实 1.3.9 源码/assembly surface；
- 修真实签名；
- 不删功能绕过；
- 不放开 Client NetManager barrier。

### 5.1 P2 完成边界

原交接要求 P2 至少覆盖：

- Add node
- Remove node
- Union node
- Split/intersect segment
- parallel / connection creation

前五项已有 green 代码。Parallel / Connection family 已有 `8379f949...` 实现草案，先通过 CI 和真实 reference build 审查这批代码，不要再平行另写第二套。

重点复核真实源码：

- `NetworkMultitool/ToolModes/NodeLineModes/CreateParallel.cs`
- `NetworkMultitool/ToolModes/ConnectionModes/BaseCreate.cs`
- `CreateConnection.cs`
- `CreateCurve.cs`
- `CreateLoop.cs`

### 推荐做法

继续沿用当前模式：

1. 找真正执行 shared mutation 的高层 `Apply` / static helper；
2. Client 阻止原方法直接执行；
3. 转成 Stable-ID semantic `NetIntentV2`；
4. Host resolve Stable ID -> 自己的 native slot；
5. Host 调原 Mod 高层方法；
6. 仍由已有 Net `Reconcile()` 捕获最终 graph；
7. Client absolute projection；
8. 不让 Client 直接通过 NetManager barrier。

如果某个模式需要大量连续自由几何/参数，先建立明确且有界的 semantic DTO；不要发送旧 Command 对象，不要序列化 native selection object。

若无法安全覆盖某个模式：

- 明确 Host-only / fail-closed；
- UI/日志说明；
- 不要静默放开 Client NetManager direct write。

---

## 6. P2 继续时要重点检查的风险

### 6.1 Mod 版本 / type 漂移

当前 shim 是 source-grounded reflection optional patch。

继续增强：

- 检查实际 1.3.9 assembly/type；
- 若 stable 1.3.9 与当前源码 master 存在签名差异，按真实 1.3.9 签名修；
- 不 wildcard 猜 type；
- 不因为一个方法没找到就悄悄降级成 Client direct NetManager。

### 6.2 Net absolute projection

Union/Split/Intersect 会造成 segment/node delete + recreate / relink。

要用真实测试确认：

- Host `Reconcile()` 能正确处理 build-index / slot reuse；
- Stable old identity retire；
- 新 identity allocate；
- Client delete/upsert 顺序合法；
- Zone side effects 最终能由已有 Zone observer/authority 收敛；
- Tree/Prop collateral clear 不被误拦。

若发现 Client projection 对某种 Host mutation 不支持：

**修 Net absolute projection closure，不要改成两端重跑 Multitool。**

### 6.3 Economy

Multitool 的 BaseMode 有：

- `FetchMoney`
- `AddMoney`
- `ChangeMoney`

当前 Host `NetAuthorityDomain.ExecutePlayer` 外层已有 `EconomySideEffectCapture.Begin()`。

继续验证：

- construction cost / refund 能被捕获；
- Client 只投影 Host 最终 economy side effect；
- 不双扣钱。

---

## 7. P3 以后仍按原 handoff，不要改顺序

P2 完整后：

### P3 — 81 Tiles 2

重点：

- expanded electricity runtime
- expanded water/sewage/heating
- 25 格外 service behavior
- utility toggle hot-join consistency

现有：

- `bridge.eightyone2` 只完成共享开关同步
- Area 继续复用 Area Authority

### P4 — Game Anarchy / Infinite Goods

Game Anarchy：

- 继续审计 shared-simulation patch
- UI-only 本地
- shared option Host-owned
- manual/periodic money Host-only
- milestone/resource/fire 等 persistent write 必须 core / adapter / block

Infinite Goods：

- 当前已有 config bridge
- 当前已有 Building `m_customBuffer1/2` Stable-ID sharded projection
- 必须继续核对真实版本是否还改其他持久 buffer

### P5 — CSLModernMap

只做真实源码/type 确认：

- exact IUserMod type / assembly
- 证明 read-only/export
- 再加入精确 client-only whitelist
- 不 wildcard

### P6 — TM:PE

仍保持 blocked。

先：

- persistent traffic rule adapter
- Stable Net identity

后：

- Vehicle / Path dynamic closure

在 P6-B/P7 完成前不能把 TM:PE 改 Supported。

### P7 — Citizen / Vehicle / Path

按原 handoff 做 source-grounded classification + authority closure。

### P8 — Terrain

最后一个基础 fail-closed 工具。

固定路线：

- Host brush
- capture final terrain tile / heightmap shard
- Client absolute apply
- snapshot/root
- 处理合法 Building/Net side effects

不要 replay mouse brush。

### P9 — 真机 E3/E4 / RC

仍按原固定矩阵。

---

## 8. 现在不要误写的状态

当前不能写：

- “Tree/Prop 已真机验证”
- “Network Multitool 已兼容”
- “1.0 已 code-complete”
- “CI green = gameplay pass”
- “TM:PE supported”
- “81 Tiles runtime utilities complete”
- “Citizen/Vehicle/Path complete”
- “Terrain supported”
- “RC”

当前可以准确写：

- Tree/Prop Authority 代码冲突已正式收口，CI green，真机未验证；
- Network Multitool 首批 Add/Remove/Union/Split/Intersect semantic shim 已实现，CI green，真机未验证；
- 最新代码全绿 Runtime Artifact 已生成。

---

## 9. 建议 Codex 的提交节奏

不要一口气改 P2-P8。

继续：

### Batch B2
- 修 `8379f949...` 当前测试编译错误
- 审查 Parallel + Connection family 已写实现
- 必要时按真实 1.3.9 signature 修正
- contract/tests
- exact HEAD 全 CI green

### Batch C
- 81 Tiles runtime utilities
- Infinite Goods 全字段 audit
- Game Anarchy shared patch audit
- exact HEAD 全 CI green

### Batch D
- TM:PE persistent rules
- exact HEAD 全 CI green

### Batch E
- Citizen / Vehicle / Path closure
- Terrain absolute shards
- exact HEAD 全 CI green

然后真实多机。

---

## 10. 每批硬门禁

每个候选 HEAD 都必须确认：

- docs-contract
- Windows kernel
- Linux kernel
- net8 tests
- Linux Mono/net35
- real CS1 metadata probe
- real CS1 reference runtime build
- runtime-package-windows
- package verifier / provenance

禁止打包：

- ICities.dll
- Assembly-CSharp.dll
- ColossalManaged.dll
- UnityEngine.dll
- UnityEngine.UI.dll
- 0Harmony.dll
- CitiesHarmony.Harmony.dll
- mscorlib.dll
- System.dll
- System.Core.dll

---

## 11. 最短 Codex 启动指令

> 继续开发 `jiangkaiqi2005/CSM-Forge` 的 `feat/ultimate-dlc-mod-framework`。先完整阅读 `docs/HANDOFF-FORGE-1.0-CODEX-20260918.zh-CN.md` 和 `docs/HANDOFF-FORGE-1.0-CODEX-CONTINUE-20260918.zh-CN.md`。最新已确认完整代码全绿基线是 `8e960081d890c484da59921391749639807a6c7f`：Tree/Prop 已从旧 Alpha safety barrier 正式迁出并由 Stable-ID Decoration Authority 独占；Network Multitool 1.3.9 的 Add/Remove/Union/Split/Intersect 已有 Stable-ID semantic Net intent shim并全绿。其后的 `8379f949d0451eb008284a484f207cd751b70a89` 已写 Parallel / Connection family，但当前 kernel CI 因 `NetDomainV2Tests.cs:119` 使用不存在的 `Assert.False` 而失败，runtime-package 被跳过。不要 reset；先修该测试 API并重新证明 exact HEAD docs/Windows/Linux/Mono/real CS1 runtime-package 全绿，再审查 Parallel/Connection 的真实 1.3.9 signature。不要 merge main/develop，不要 Command Replay，不要 native ID wire，不要把 CI 当真机。全绿后再进入 P3；其余 P3→P9 严格按原 handoff 顺序执行。
