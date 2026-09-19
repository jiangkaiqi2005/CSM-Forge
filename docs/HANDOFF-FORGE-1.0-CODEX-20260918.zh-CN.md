# CSM-Forge 1.0 Codex 交接 — 2026-09-18

> 这是一份执行交接，不是设计提案。目标是让 Codex 直接在当前已验证分支上继续完成 1.0 收尾，不重新设计、不回退旧 CSM 同步模型。

## 0. 仓库、分支与已验证基线

- 仓库：`jiangkaiqi2005/CSM-Forge`
- 唯一继续开发分支：`feat/ultimate-dlc-mod-framework`
- Draft PR：#2 `WIP: ultimate DLC and mod compatibility framework`
- 交接时 HEAD：
  `484981402a336c7d91891a708cf540dae9d2bbd2`
- PR base：
  `fix/forge-cqu-integration-docs`
- 不要 merge `main` / `develop`
- 不要切回 `feat/ultimate-dlc-mod-framework-2` 或 `feat/ultimate-dlc-mod-framework-temp-delete-me`

如果 Codex 开始工作时分支已经前进：
1. 先确认当前 HEAD 是 `4849814...` 的 fast-forward 后代；
2. 先读新增提交；
3. 先确认 docs-contract、Windows/Linux kernel、Mono/net35、real CS1 runtime-package-windows 全绿；
4. 再继续，不要 reset 回本交接 SHA。

### 交接时 CI

`484981402a336c7d91891a708cf540dae9d2bbd2` 已确认：

- docs-contract：success
- kernel-ubuntu-22.04：success
- net8 kernel tests：success
- Mono/net35 tests：success
- kernel-windows-2022：success
- CS1 DLC/core simulation metadata probe：success
- real CS1 reference runtime build：success
- runtime-package-windows：success

CI run：`35252864336`

Runtime Artifact：

- Artifact ID：`10511805957`
- Name：`CSM-Forge-runtime-484981402a336c7d91891a708cf540dae9d2bbd2`
- Artifact digest：
  `sha256:0c42ceefad4a3ee5a1d2ec17fa2848929bfe2ef28599392c274018398cc8d9f2`

**注意：CI 只证明编译、测试和包完整性，不证明真实双机稳定。不要把 CI green 写成 gameplay validated。**

---

## 1. 必须先完整阅读

按顺序：

1. `docs/HANDOFF-FORGE-ALPHA-20260916.zh-CN.md`
2. 该交接中列出的五份 V3 核心设计文档
3. `docs/DLC-COVERAGE-V1.zh-CN.md`
4. `docs/MOD-ADAPTER-API-V1.zh-CN.md`
5. `docs/MOD-COMPATIBILITY-TARGET-V1.zh-CN.md`
6. `docs/TESTING.zh-CN.md`
7. 本文件

然后核对当前 HEAD、PR #2、全部 CI。

---

## 2. 绝对不能破坏的架构约束

### 2.1 唯一正确的数据流

```
Player / Mod semantic intent
-> Host authoritative execution
-> capture final absolute state
-> AuthorityBatch
-> Client absolute projection
```

禁止：

- Command Replay
- Client 重跑 Host tool command
- 依赖 Host/Client native slot 一致
- 用 BuildingId / NetId / ParkId / EventId / VehicleId 等原生 index 当网络身份
- 为了“兼容 Mod”绕过 Host Authority

### 2.2 Stable ID

实体跨网络只允许使用：

- `EntityIdentityV2`
- generation
- persisted identity watermark / mapping

CS1 原生 ID 永远 process-local。

### 2.3 Join

Join 固定为：

```
Snapshot(B)
-> journal B+1..H
-> Barrier(H, rootH)
-> BarrierAck
-> journal H+1..A
-> ActivationGrant(A)
-> Activated
-> Live
```

每个 Client 的 JoinId / Generation / snapshot / cursor / recovery 独立。
慢 Client 只能自己 rebaseline，不能冻结 Host 或其他 Client。

### 2.4 Projection Audit

只诊断 drift。

禁止因为 audit fingerprint mismatch 自动：

- kick
- fence
- snapshot
- resync

真正 apply/root 失败仍可按现有 recovery/fault 规则处理。

### 2.5 CSM-CQU

只允许作为：

- CS1 API surface 供体
- 真实调用方式参考
- 兼容经验参考

禁止吸收它的：

- CommandReceiver
- relay replay
- native ID alignment
- “两端 array slot 必须一样”的假设

---

## 3. 当前已经完成的“大块”

### 3.1 基础多人架构

已完成并不要重写：

- Host authority
- stable entity identity
- snapshot/journal/barrier/activation
- recovery/rebaseline
- independent join state
- compatibility manifest/policy
- exact source provenance
- install verifier
- diagnostic collector
- projection audit
- Extension Authority framework
- sharded extension state
- interactive extension intent
- named extension entity-map save

### 3.2 核心玩法域

已有：

- water / general budget
- demand / RCI
- tax
- economy cash / loan / bailout
- area unlock
- Building
- Road / Net
- Zone
- District brush / style / policy
- pause / speed
- TransportLine
- names / city name
- Weather
- Event
- Disaster
- DistrictPark family

### 3.3 DLC

`docs/DLC-COVERAGE-V1.zh-CN.md` 已将官方玩法 DLC 全部分成：

- CORE
- DEDICATED
- CONTENT

没有 UNCLASSIFIED。

专用层包括：

- `builtin.districtpark`
- `builtin.parkgrid`
- `builtin.districtpark-controls`
- `builtin.districtpark-campus`
- `builtin.districtpark-deep`
- `builtin.events`
- `builtin.disasters`

不要再次逐 DLC 另造协议。

---

## 4. e48c2e4e 之后已经完成的 6 个提交

不要重复做。

1. `97a948b17a4c2fb07cb327097de3b877abd3bfed`
   - `feat(mod): bridge Demand Controller and classify ACME`

2. `6f33d69e6dec2766c45a92e76d3ceffe058a4eff`
   - `fix(mod): make Demand Controller client guard explicit`

3. `9c2b6aea6a6b78f0b7af02e5de7f85489a384de8`
   - `feat(mod): host-own Game Anarchy Infinite Goods and 81 Tiles settings`

4. `a12d1984d14c54d08b00e8d7cdf9dd7364069491`
   - `chore(runtime): fingerprint known mod settings and probe core simulation`

5. `8c376b44bf194591ae0b53ef987d705e42999fb1`
   - `feat(mod): project Infinite Goods building buffers by stable ID`

6. `484981402a336c7d91891a708cf540dae9d2bbd2`
   - `feat(core): add stable Tree and Prop authority`

---

## 5. 用户固定 1.0 Mod 清单与当前真实状态

以后不要继续把目标无限扩张成“兼容所有 Workshop Mod”。
1.0 一级兼容目标就是下面这些。

| 项目 | 当前状态 | Codex 后续动作 |
|---|---|---|
| Demand Controller | 已有 `bridge.demandcontroller`；Client `Refresh` 阻断；RCI 最终值走 Demand Authority | 真机验证，补版本/type 漂移保护即可 |
| Network Multitool 1.3.9 | 已确认最终调用 `NetManager.CreateNode/CreateSegment/Release*` | **未完成**：Client 复杂操作目前会撞 NetManager client barrier；需做语义 shim/intent 或明确 Host-only UX |
| Infinite Goods | Host config bridge 已有；`m_customBuffer1/2` 已按 Building Stable ID 分 shard absolute projection | 核对 Mod 实际修改的所有 buffer，不足则扩 adapter；真机验证 |
| ACME | ClientOnly | 真机加载验证 |
| New Place | Map / CONTENT | map + asset manifest 验证，不写 adapter |
| Precision Engineering (Harmony) | ClientOnly | 真机验证；保持只读辅助 |
| CSLModernMap | 只读候选，但实际 IUserMod type 尚未安全确认 | 找真实 assembly/type，确认只读后加入精确白名单；不要猜 type |
| Game Anarchy 1.3.1 | `bridge.gameanarchy` Host-owned shared settings；Client money/shared setters 被阻断 | 审计所有会改变 shared simulation 的 patch；未覆盖的要 Host-own / block |
| TM:PE 11.9.4.1 | **仍然 blocked-mod** | 需要 Dedicated Stable Net Rule Adapter；动态 Vehicle/Path 闭包后再解除 block |
| Harmony 2.2.2-0 | Dependency | 不需要 adapter |
| 81 Tiles 2 1.0.5 | `bridge.eightyone2` 同步共享开关；Area 复用 Forge | **未完成**：expanded Water/Electricity 等运行态 authority closure + 真机验证 |

### KnownMod bridge 入口

读：

- `src/Forge.Runtime.Cities1/KnownModBridgeRegistry.cs`
- `DemandControllerBridge.cs`
- `GameAnarchyBridge.cs`
- `InfiniteGoodsBridge.cs`
- `InfiniteGoodsBuildingBufferAdapter.cs`
- `EightyOne2Bridge.cs`

第三方兼容声明/API：

- `ForgeCompatibilityApi.cs`
- `ForgeExtensionApi.cs`

---

## 6. 当前新增 Tree / Prop Authority

已经存在：

- `TreePropStateAdapters.cs`
- `DecorationAuthorityPatches.cs`

特点：

- Tree：512 shards
- Prop：Stable ID + sharded absolute state
- Create / Move / Delete 都有 semantic intent
- Host 分配 Stable ID
- Replica 本地新 slot 物化
- native tree/prop ID 不上 wire

### **P0 必修问题：旧 Alpha safety patch 仍在**

`AlphaSafetyPatches.cs` 现在仍然 Patch：

- TreeManager.CreateTree
- MoveTree
- ReleaseTree
- PropManager.CreateProp
- MoveProp
- ReleaseProp
- TerrainTool.ApplyBrush

同时 `DecorationAuthorityPatches.cs` 也 Patch Tree/Prop，而且两边都可能在相同 Harmony priority 下执行。

这意味着：

> Tree/Prop Authority 虽已实现，但旧 fail-closed barrier 还没有被正式移除/降级，存在 patch ordering 冲突风险。

**Codex 第一件事就是解决这个冲突。**

要求：

- Tree/Prop 不再走 AlphaUnsupportedWritePolicy fail-closed；
- Terrain 暂时仍 fail-closed，直到 Terrain Authority 完成；
- Building/Net 引发的 Tree/Prop side effects 必须继续允许 Host authoritative operation 正常执行；
- 更新 `tests/docs/test_minimum_playable_alpha.py` 和新 decoration contract；
- 不要只靠 Harmony priority“碰巧赢”。

---

## 7. 剩余 1.0 工作：按这个顺序做

### P0 — 先保持当前全绿基线

开始前：

- HEAD 必须是当前最新
- docs-contract green
- Windows/Linux green
- Mono green
- real CS1 runtime package green

任何一批实现后都重新跑。

### P1 — Tree / Prop 正式收口

完成第 6 节冲突修复。

验收：

- Host Tree/Prop create/move/delete
- Client Tree/Prop create/move/delete -> intent -> Host -> absolute projection
- hot join snapshot 后 Stable IDs 正确
- slot reuse 不串号
- Building/Net collateral clear 不被拦
- Terrain 仍 fail-closed

### P2 — Network Multitool

公开源码已确认它大量直接调用：

- `NetManager.CreateNode`
- `NetManager.CreateSegment`
- `ReleaseNode`
- `ReleaseSegment`

当前普通道路工具有 NetTool semantic intent，但 Client 直接 NetManager mutation 会被 barrier 阻断。

正确路线：

1. 不放开 Client NetManager direct writes；
2. 找 Network Multitool 的高层 Execute/Apply/Mode entry point；
3. Client 拦截成 Forge semantic intent；
4. Host 调原 Mod/游戏 API；
5. 最终仍由现有 Net domain capture absolute result；
6. 不新增 native NetId wire contract。

至少覆盖用户会用到的：

- Add node
- Remove node
- Union node
- Split/intersect segment
- parallel / connection creation

如果某个模式无法安全语义化，明确 Host-only/fail-closed，不静默执行。

### P3 — 81 Tiles 2

已经同步配置，剩余重点：

- expanded electricity runtime
- expanded water/sewage/heating runtime
- 25 格之外的 area/build service behavior
- utility toggle hot-join consistency

原则：

- Area unlock 用现有 Area Authority；
- 运行态 utilities 应 Host-owned 或有 deterministic absolute result；
- 不让两端各自跑不同 utility grid 后只同步 UI。

### P4 — Game Anarchy / Infinite Goods

Game Anarchy：

- 列出全部会修改 shared simulation 的 patch；
- UI-only 留本地；
- shared options Host-owned；
- manual / periodic economy mutation 只能 Host；
- milestone/unlock/oil/ore/resource/fire 等如果影响持久世界，必须 core domain / extension adapter / explicit block 三选一。

Infinite Goods：

- 核对目标版本源码/反编译实际修改字段；
- 目前只同步 Building `m_customBuffer1/2`，不要默认这就等于完整；
- 若还写 TransferManager / warehouse / industry / shelter / power 等其他持久 buffer，补 absolute adapter。

### P5 — CSLModernMap

只做源码确认：

- 找真实 IUserMod type / assembly
- 确认只读取游戏状态、导出文件，不写 shared simulation
- 然后加入精确 client-only/read-only 白名单

不允许 wildcard assembly name。

### P6 — TM:PE

这是固定 Mod 清单中最大一块。

**不要直接先做 Vehicle AI。先拆两层。**

#### P6-A 持久交通规则

建立 Dedicated adapter，按 Forge Stable Net identity 同步：

- speed limits
- lane arrows
- lane vehicle restrictions
- junction restrictions
- priority signs
- timed traffic lights / traffic-light configuration
- parking rules
- lane connections
- 其他持久 TM:PE rule/config

所有 Net node/segment/lane 引用必须转换到 Forge Stable Net identity。
不能发送 ushort node/segment/lane index。

#### P6-B 动态交通

之后处理：

- pathfinding decisions
- vehicle lane selection
- TM:PE traffic AI side effects

在这层完成前保持 TM:PE blocked，不能提前把兼容表改成 Supported。

### P7 — Citizen / Vehicle / Path Authority closure

这是 1.0 最大基础风险。

目标不是“把几十万 Citizen/Vehicle 每 tick 全量广播”。

先做 source-grounded classification：

1. 哪些是 Host-only simulation state；
2. 哪些是 Client 可以重建的表现态；
3. 哪些是必须复制的持久/因果状态；
4. 哪些只需 snapshot/hot-join baseline；
5. 哪些高频状态需要 coarse/sharded result replication。

优先：

- Vehicle create/release/lifecycle
- CitizenInstance create/release/lifecycle
- Path unit identity/lifecycle
- path result / route choice 的 Host authority
- Building natural upgrade/abandon/collapse 等 Host-owned simulation side effect

不能回退“双方同时自然模拟，靠希望保持一样”。

### P8 — Terrain

Terrain 是最后一个基础工具 fail-closed 项。

不要同步鼠标 brush replay。

推荐：

- Host 执行 brush；
- 捕获最终 terrain delta / heightmap tile；
- 固定 tile/shard absolute state；
- Client apply tile result；
- hot join 进入 snapshot/root。

同时处理 terrain 变化对 Net/Building 的合法 side effect。

### P9 — 真实多机 E3/E4 / RC

只有这一阶段可以把“代码完成”变成“1.0 可发布”。

固定测试矩阵：

#### Host / Join
- 1 Host + 1 Client
- 1 Host + 2 Clients
- 两个 Client 重叠 Join
- hot join
- Client drop/rejoin
- lagging Client rebaseline，其他玩家继续玩

#### 核心操作
- pause/speed
- budget/tax
- area
- road/create/delete
- building/bulldoze
- zone
- district/policy
- transport
- names
- weather
- Event
- Disaster
- Park/Campus/Industry/Airport/Pedestrian Area
- Tree
- Prop
- Terrain

#### 固定 Mod 清单
逐个单独，再做混装：

- Demand Controller
- Network Multitool
- Infinite Goods
- ACME
- New Place
- Precision Engineering
- CSLModernMap
- Game Anarchy
- TM:PE
- Harmony
- 81 Tiles 2

#### 长时间
- 1h
- 4h
- 8h/overnight soak
- high population save
- dense road network
- weak network / packet delay / reconnect

任何问题先收：

- Host diagnostics ZIP
- 每个 Client diagnostics ZIP
- exact BUILD_INFO
- manifest_sha256
- last player action
- city save
- full Mod/version/config list

修复优先级：

1. startup/load crash
2. root mismatch
3. Join hang
4. Building/Net
5. Transport
6. Tree/Prop/Terrain
7. Mod adapter
8. Projection drift
9. UI

---

## 8. CI / 打包硬门禁

每个候选 HEAD 必须全部满足：

- docs-contract
- Windows kernel
- Linux kernel
- net8 tests
- Mono/net35 tests
- real CS1 metadata probe
- real CS1 reference runtime build
- runtime-package-windows
- packaged verifier PASS

安装包必须：

- `BUILD_INFO.json` 精确指向候选 SHA
- `SHA256SUMS.txt` 全覆盖
- Host/Clients source_commit 相同
- Host/Clients manifest_sha256 相同

禁止泄漏：

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

### 不允许伪造验收

`BUILD_INFO.gameplay_validation` 在没有真实多机证据前保持：

`NOT RUN BY CI`

不要改成 PASS。

---

## 9. Codex 每次提交的工作方式

不要一次改 30 个无关系统。

每批按：

1. 读真实源码/API
2. 写最小实现
3. 写 contract/test
4. 提交
5. 等 exact HEAD CI
6. Runtime green 后继续下一批

若 Runtime CI 报 CS1 API 不存在：

- 先读 metadata probe
- 修真实签名
- 不删功能绕过编译
- 不用反射猜一个不存在的方法

---

## 10. 交接后的第一个执行批次

Codex 请直接按下列顺序开始，不再问架构问题：

### Batch A
1. 修 Tree/Prop vs AlphaSafetyPatches 冲突
2. 更新 package/UI 文案，不再说 Tree/Prop intentionally fail-closed
3. 保留 Terrain fail-closed
4. CI 全绿

### Batch B
1. Network Multitool source audit
2. 写 Client semantic shim
3. 先支持 Add/Remove/Split/Union/Intersect
4. Host 最终走已有 Net absolute result
5. CI 全绿

### Batch C
1. 81 Tiles expanded utility closure
2. Infinite Goods 全字段 audit
3. Game Anarchy shared simulation patch audit
4. CI 全绿

### Batch D
1. TM:PE persistent rule adapter
2. Stable Net identities
3. 保持动态 TM:PE blocked 直到 Vehicle/Path closure
4. CI 全绿

### Batch E
1. Citizen/Vehicle/Path source-grounded authority closure
2. Terrain absolute tile/shard authority
3. CI 全绿

然后进入真实多机测试与 RC。

---

## 11. 完成定义

不能用“代码很多了”作为完成。

### Framework complete
当前已经达到。

### Code-complete 1.0 candidate
要求：

- 固定 Mod 清单没有未解释的 shared-simulation write
- TM:PE 不再靠 blocked 逃避持久规则层
- Citizen/Vehicle/Path 有明确 authority closure
- Tree/Prop/Terrain 都不再是普通操作 fail-closed
- 所有 CI green

### RC
还必须：

- 双机/多机真实运行
- hot join/rejoin
- mixed DLC/Mod
- 4h+ soak
- 无系统性 root mismatch / join hang / world divergence

### 1.0
只有 RC 通过后才能标记。

---

## 12. 最后强调

- 不要重新设计。
- 不要 merge main/develop。
- 不要回退 Command Replay。
- 不要同步 native IDs。
- 不要为了“支持 TM:PE”让 Client 直接写共享模拟。
- 不要把“能加载 Mod”写成“兼容”。
- 不要把“CI green”写成“真机稳定”。
- 优先完成有限的固定目标，不继续扩大需求边界。
