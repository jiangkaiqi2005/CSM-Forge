# CSM-Forge V3 / 最小可玩 Alpha 交接文档

日期：2026-09-16

> 这份文档用于把当前长对话中的实现状态完整交接给一个全新的开发窗口。新窗口应先读本文件，再读 V3 五份设计文档和当前源码，不要重新从“CSM 应该怎么同步”开始讨论。

---

## 1. 当前目标

当前目标已经从“继续无限扩功能”收敛为：

**先做出一个可安装、可 Host/Join、可热加入、核心建城操作可用、未支持功能不会静默写坏世界的最小可玩 Alpha。**

不是当前目标：

- 立即覆盖所有 DLC；
- 立即同步所有 Citizen / Vehicle / Pathfinding 细节；
- 立即实现最终公网认证 Transport；
- 立即做到正式 Release；
- 为了功能数量回退到旧 CSM/CSM-CQU 的 Command Replay。

当前优先级：

1. 保持现有 V3 Authority 架构不被破坏；
2. 保持所有 CI 门禁全绿；
3. 固定一个可安装 Alpha ZIP；
4. 进行真实双机 Host/Join/热加入/基础建城测试；
5. 根据真实日志和 Projection Audit 修 bug；
6. Alpha 稳定后再逐步解除 Tree / Prop / Terrain 等限制。

---

## 2. 仓库、分支与供体

### 2.1 Forge 主仓库

- 仓库：`jiangkaiqi2005/CSM-Forge`
- 当前开发分支：`fix/forge-cqu-integration-docs`
- 不要擅自合并：`develop` / `main`

### 2.2 原始 CSM

- 仓库：`CitiesSkylinesMultiplayer/CSM`
- 角色：原始行为/API/Hook 参考，**只读基线**
- 典型参考 commit：`45c4ea...`

### 2.3 CSM-CQU

- 仓库：`Aetik-yue/CSM-CQU`
- 角色：经过修复的热加入、异常安全、真实 CS1 Hook、打包流程等机制供体，**只读 donor**
- 重点参考分支：`fix/nonblocking-parallel-join`

原则：

**Forge 可以吸收 CSM-CQU 的机制经验，但不能把旧 Command Relay / remote replay 当成最终同步模型。**

---

## 3. 最新已验证基线

### 3.1 当前 HEAD

截至本交接前核对：

- 分支 HEAD：`f8865aa5a219b6d3b339720ee4bddd941e610821`
- commit message：`fix(alpha): allow supported Host tool side effects through safety layer`

### 3.2 这一个 HEAD 的 CI

已经明确核对：

- `docs-contract`：SUCCESS
- `kernel-windows-2022`：SUCCESS
- `kernel-ubuntu-22.04`：SUCCESS
- net35 / Mono tests：SUCCESS
- `runtime-package-windows`：SUCCESS
- CS1 reference assemblies 准备：SUCCESS
- 真实 `Forge.Runtime.Cities1` 编译：SUCCESS
- 可安装 Runtime ZIP 生成：SUCCESS

对应 workflow run：`35085394599`（kernel + runtime package）。

因此：

**`f8865aa...` 是目前最重要的“全绿可打包 Alpha 候选基线”。后续若继续提交，先确认新的 HEAD 是否仍然全绿；不要因为源码又多了几个 commit 就默认它比这个基线更可靠。**

---

## 4. V3 不可推翻的架构决策

### 4.1 Authority 方向

最终模型必须保持：

`Intent -> Host authoritative execution -> AuthorityBatch -> Client absolute projection`

不是：

`Client command -> 所有人重跑同一 Tool/Command`

### 4.2 原生 CS1 ID 不是网络身份

以下 native ID 只能存在于进程内部：

- BuildingId
- NetNodeId
- NetSegmentId
- DistrictId
- TransportLineId
- 后续 Tree/Prop/Citizen/Vehicle native slot

网络身份必须使用 Forge stable identity / stable key。

已经做出的关键设计：

- `EntityIdentityV2(EntityId, Generation)`；
- `EntityIdMapV2`；
- stable identity 随 Snapshot 持久化；
- native slot 重用不能静默变成“同一个网络实体”。

### 4.3 Join 模型

保持固定边界：

`Snapshot(B) -> journal B+1..H -> Barrier(H, rootH) -> BarrierAck -> journal H+1..A -> ActivationGrant(A) -> Activated -> Live`

不要改回“追着 Host 当前 head 跑”。

### 4.4 独立 Join / Recovery

每个 Client 拥有独立：

- JoinId / JoinGeneration
- SnapshotId
- TransferId
- Snapshot cursor
- recovery 生命周期

日志淘汰时，**只对该 Client 重新 Snapshot**，不能冻结整个 Host 房间。

### 4.5 不要做错误的自动动态重同步

曾考虑过“Host/Client 同 simulation frame fingerprint 连续不一致就自动 resync”，后来明确否决。

原因：热加入 Client 会把 B+1..A 的历史 AuthorityBatch 在自己的加载时间轴中集中安装，因此相同 CS1 frame index 不代表相同历史执行时间点，容易合法假阳性。

当前正确做法：

- Projection Audit 只报警；
- 不自动 Fence；
- 不自动 Snapshot；
- 不滥用 `GapRequest` 或 Host->Client 的 `ResyncRequired`。

---

## 5. 已完成的底层联机能力

当前已有真实代码，不是设计稿：

- v2 Session / Frame protocol；
- SessionStamp / ConnectionBinding / MemberIdentity / Generation；
- Player Intent；
- AuthorityBatch；
- AppliedAck；
- Gap detection；
- bounded journal；
- JoinCoordinator；
- Snapshot file streaming；
- 32 KiB chunk；
- chunk SHA-256；
- full-file SHA-256；
- Snapshot temp file；
- real `.crp` Host save；
- Client real world load；
- LoadGeneration；
- WorldId / Epoch metadata；
- Barrier / Activation；
- per-client rebaseline；
- shared immutable snapshot + independent cursor；
- LiteNetLib development transport；
- Compatibility Manifest；
- Runtime lifecycle；
- simulation-thread scheduler；
- Harmony patch coordinator；
- Entity map save/restore；
- Runtime auto package CI。

Transport 当前仍是：

**LiteNetLib + development room key，适合 LAN/受控 Alpha，不等于最终公网认证 Transport。**

---

## 6. 已完成的真实 CS1 Authority 领域

当前 Host/Client 的 Authority/Replica domain 已远超过最初 Water slice。

### 已实现并接入 aggregate root 的主要领域

- Water Budget
- Demand / RCI
- Tax
- 通用 Budget
- Economy Cash
- Loan / Bailout / EconomyControl
- Area Unlock
- Building
- Road / Net Node + Segment
- Zone
- District brush
- District Style
- City / District Policy
- Simulation pause / speed
- TransportLine
- stable custom names
- CityName
- Weather

其中几个关键领域：

### 6.1 Building

已覆盖的重要内容：

- stable Building identity；
- Snapshot 恢复 identity；
- Create；
- Delete / Bulldoze；
- Host 原生 BuildingTool 行为保留；
- Client 玩家操作转 Intent；
- Host construction cost；
- recent-build refund；
- Client absolute projection；
- Host natural create/delete 捕获；
- Client `BuildingManager.CreateBuilding/ReleaseBuilding` 非 ApplyScope 写入阻断。

### 6.2 Road / Net

核心方向：

- Forge Node identity；
- Forge Segment identity；
- native node/segment ID 不上网；
- `NetTool.CreateNode` Host-authoritative；
- Client create/release barrier；
- bulldoze / upgrade 等逐步纳入；
- Zone 通过 stable Segment identity 定位，而不是 native ZoneBlock ID。

### 6.3 District

Forge 没有重跑旧 CSM 的 brush command，而是：

- Host 执行真实 brush；
- 读取 512×512 District grid 最终 cell delta；
- Client 安装绝对 cell delta；
- stable District identity；
- style + policy 与 District 生命周期闭合；
- policy 使用四组真实 mask：Services / Taxation / CityPlanning / Special。

### 6.4 TransportLine

当前已经不是只同步颜色：

- stable line identity；
- Create；
- Release；
- Color；
- Budget；
- TicketPrice；
- Day/Night active；
- Stop sequence；
- Add / Remove / Move stop；
- Complete state；
- Client pending speculative line -> Create Intent -> Host stable identity -> Client bind；
- Host 拒绝时回滚 pending line。

不要回到 CQU 39KB TransportTool replay。

### 6.5 Economy

当前：

- cash 是 Host absolute truth；
- Client 自然现金写入的高风险资源类型被抑制；
- Loan/Bailout 有独立最终状态；
- Tax/Budget 玩家操作必须走 Host Authority。

### 6.6 Weather

Weather root 故意只跟踪 target：

- Host 决定 target；
- AuthorityBatch 可携带 current + target；
- Client 可本地插值 current；
- Client 不允许自己改变 Host committed target；
- 避免每 tick 插值导致 domain root 抖动。

---

## 7. Alpha 安全边界

最小可玩 Alpha 的原则是：

**支持的功能开放；未闭合功能不能静默把世界写坏。**

文件：

`src/Forge.Runtime.Cities1/AlphaSafetyPatches.cs`

当前多人阶段明确阻断：

- Tree create/move/release
- Prop create/move/release
- Terrain brush

但有重要例外：

- `RuntimeScopeGuard.IsApplying` 时放行；
- Host 的已支持 BuildingTool / NetTool 引起的树/Prop 副作用允许通过，避免 Alpha safety layer 反过来破坏建楼/修路。

这就是 HEAD `f8865aa...` 最后修的内容。

当前不要为了“功能数更多”马上删除这个安全层。

---

## 8. 当前诊断与 CI 门禁

已经增加的长期门禁：

### 8.1 Domain ID 唯一性

`tests/docs/test_runtime_domain_ids.py`

这个门禁已经实际抓出历史隐藏 bug：

- Zone = 30
- Transport 曾经也 = 30
- 后来尝试 40，又与 District = 40 冲突
- Transport 最终迁到 50
- Weather 使用 90

因此以后不要人工猜 Domain ID；让门禁决定是否冲突。

### 8.2 Domain lifecycle cleanup

`tests/docs/test_runtime_domain_lifecycle.py`

保证：

- rebaseline 清所有 Client domain；
- Stop/Abort 清所有 Host + Client domain；
- committed root watermark 不残留。

### 8.3 Projection Audit

`ProjectionAudit.cs`

语义：

- diagnostic-only；
- 低频轮询；
- 每次只检查一个 domain；
- 比较 Host committed expectation 与 Client 当前真实投影；
- Cash/Loan 特别检查真实游戏字段，避免被“committed root 缓存”遮住；
- mismatch 只日志报警；
- 不自动 rebaseline。

### 8.4 Alpha 最小可玩合同

`tests/docs/test_minimum_playable_alpha.py`

用于保证：

- Tree/Prop/Terrain safety barrier 不被误删；
- 包说明明确哪些功能支持、哪些暂禁。

### 8.5 Host/Client domain 对称

已经增加源码级门禁，用于保证 Host/Client 注册域集合与顺序一致。

目的：防止“编译全绿，但 Host aggregate root 有 17 域、Client 只有 16 域，Join 永远失败”。

---

## 9. 打包状态

### 9.1 自动 Runtime Package 已经真正跑通

`.github/workflows/ci.yml`：

- Windows + Linux Core tests；
- Linux net35 / Mono；
- 下载隔离 CS1 reference assemblies；
- 编译真实 `Forge.Runtime.Cities1`；
- 调用 `scripts/build-runtime.ps1`；
- 检查禁止打包游戏 DLL；
- 生成 ZIP；
- 生成 SHA256；
- 生成 BUILD_INFO；
- 上传 Artifact。

Artifact 名：

`CSM-Forge-runtime-<github.sha>`

所以双机测试必须使用**完全相同 commit SHA 的 Artifact**。

### 9.2 最后已确认全绿的 Alpha 候选

`f8865aa5a219b6d3b339720ee4bddd941e610821`

对应 runtime package 已 success。

如果后续 HEAD 没有完成全绿，不要替换这个已验证基线。

---

## 10. 当前 UI / 使用流程

设置页已有：

- DisplayName
- Host IPv4
- UDP port
- room key
- Host
- Join
- Stop
- status

最近已收紧启动门槛：

- 必须已经进入城市；
- Runtime load identity 有效；
- CitiesHarmony patches ready；
- 当前 session Offline。

UI 也已更新，不再写“只支持 Water”。

README 最近也已经重写为当前 Alpha 能力说明，不应再恢复旧的 M0/Water-only 描述。

---

## 11. 当前最关键的未验证项

这些是“真正最小可玩”最后的风险，不是再补 DTO 能解决的：

### 11.1 真实双机/多机还需要跑

CI 能证明：

- C# 编译；
- CS1 reference API 可编译；
- package 能生成；
- Core/Protocol tests 能过。

CI **不能**证明：

- 两个真实 CS1 进程可以顺利 Host/Join；
- Snapshot 加载时游戏 UI/LoadingManager 不出运行时问题；
- Harmony patch 实机触发顺序正确；
- 长时间 Citizen/Vehicle 动态层不会造成体验漂移；
- 多 Mod 下行为稳定。

### 11.2 Citizen / Vehicle / Pathfinding

当前仍主要是本地动态模拟，不是完整 Authority 结果复制。

但关键权威拓扑写口已经大量封住：

- Client Building create/release 被阻断；
- Client Net create/release 被阻断；
- Client Zone 写入被还原/Authority 化；
- Tax/Budget 写入必须走 Authority；
- Cash 高风险自然资源写入被抑制/绝对校准。

所以当前模型更接近：

**Citizen/Vehicle 暂作为表现/动态层，不允许它们随便改 Forge-owned persistent truth。**

是否还存在长时间反写，由 Projection Audit 真机日志判断。

### 11.3 Tree / Prop / Terrain

当前不是“已同步”，而是 Alpha 多人时明确禁写。

后续正式支持建议：

Tree/Prop 采用“implicit baseline identity + sparse dynamic map”：

- 存档基线的大量 Tree/Prop 不进 EntityMap；
- 基线对象 identity 可由固定高位 + slot 可重建；
- 联机后新增/槽位复用对象才进入动态 map；
- 避免几十万树把 `EntityMapSave` 撑爆。

此方案目前只是设计方向，**没有完成代码实现**。

### 11.4 Terrain

旧 CSM 是 TerrainTool brush replay。

Forge 正确方向应是：

- Host 执行 brush；
- 捕获绝对 terrain / soil delta；
- Client 安装最终结果。

当前 Alpha 先禁用，不做 replay。

### 11.5 Event / Campus / Park / DLC

暂不属于最小可玩阻塞项。

不要直接使用 native EventId / ParkId 做网络身份。

---

## 12. 新窗口下一步应该怎么做

### 第一阶段：不要再加新功能，固定 Alpha

1. 先检查当前 branch HEAD；
2. 如果 HEAD != `f8865aa...`，检查新 HEAD 的：
   - docs-contract
   - Windows kernel
   - Linux kernel
   - Mono net35
   - runtime-package-windows
3. 如果任何一项没绿：先修绿，不加功能；
4. 如果新 HEAD 全绿，下载该 HEAD 的 `CSM-Forge-runtime-<sha>`；
5. 否则直接使用 `f8865aa...` Artifact 做 Alpha。

### 第二阶段：真实双机 Smoke Test

两台机器：

- 同一个 CS1 游戏版本；
- 同一个 Forge commit Artifact；
- 安装并启用 CitiesHarmony；
- 备份测试城市；
- 先尽量减少无关 Mod。

顺序：

1. Host 进入小城市；
2. Client 进入任意测试城市；
3. Host 点击 Host；
4. Client 填 Host IPv4 + 相同 UDP port + room key；
5. Client Join；
6. 验证 Snapshot 下载/加载；
7. 验证 Barrier/Activation；
8. 进入 `ClientLive`；
9. 测试：
   - 暂停 / 速度；
   - 水预算；
   - 其他预算；
   - 税率；
   - 建楼；
   - bulldoze；
   - 修路；
   - 拆路；
   - zoning；
   - district；
   - district policy/style；
   - area unlock；
   - loan/bailout；
   - transport line create/edit/delete；
   - stable naming / city name；
   - weather；
10. 确认 Tree/Prop/Terrain 操作被安全阻止，而不是静默修改；
11. 观察日志里是否出现 `diagnostic-only projection drift`。

### 第三阶段：热加入与恢复

Smoke 通过后：

- Host 已经玩一会后 Client 再加入；
- 两 Client 重叠 Join；
- 一 Client 慢下载；
- 一 Client 中途取消/掉线；
- Journal 足够时 gap catch-up；
- Journal 淘汰时该 Client 独立 Snapshot rebaseline；
- 其他 Client 与 Host 不应被阻断。

### 第四阶段：根据真实 bug 修，而不是继续扩域

优先级：

1. 启动/加载 crash；
2. Host/Client root mismatch；
3. Join 卡死；
4. Building/Net 真实操作失败；
5. Transport route 投影失败；
6. projection drift；
7. UI/体验问题；
8. 最后才是 Tree/Prop/Terrain 新功能。

---

## 13. 绝对不要做的事情

新窗口请遵守：

- 不要合并 `main/develop`，除非用户明确要求；
- 不要把 CSM 原版当成目标架构；
- 不要恢复 Array16/Array32 强制 native ID 对齐；
- 不要让远端传 native BuildingId/NodeId/SegmentId/DistrictId/LineId；
- 不要让 Client command 在每台机器重跑 Tool 当最终同步；
- 不要把“CI 编译成功”写成“实机联机已经验证”；
- 不要把 diagnostic fingerprint 直接升级为自动重同步；
- 不要删除 Alpha safety barrier 只为了“功能看起来更多”；
- 不要为了 Tree/Prop 全量 stable identity 把几十万对象塞入 `EntityIdMapV2`；
- 不要在没有全绿 CI 时继续叠大块 Runtime Hook。

---

## 14. GitHub/工具层已知怪现象

过去出现过 branch metadata API 短暂显示旧 HEAD，但：

- `fetch_file(ref=fix/forge-cqu-integration-docs)` 已能读到新文件；
- commit CI 能运行；
- 后续 branch metadata 又刷新。

因此判断源码状态优先级：

1. 目标分支 `fetch_file` 实际内容；
2. 明确 commit SHA；
3. 该 commit 的 workflow runs；
4. branch metadata。

不要因为 branch API 短暂缓存就错误回滚源码。

---

## 15. V3 五份核心设计文档

必须保留：

1. `docs/ARCHITECTURE-V3.zh-CN.md`
2. `docs/CSM-CQU-MIGRATION.zh-CN.md`
3. `docs/CS1-RUNTIME-INTEGRATION.zh-CN.md`
4. `docs/AUTHORITY-REPLICATION.zh-CN.md`
5. `docs/IMPLEMENTATION-ROADMAP-V3.zh-CN.md`

新窗口不要重新发明一套架构，先以这五份 + 当前代码为事实依据。

---

## 16. 给新窗口的最短启动指令

可以直接复制下面这段作为新对话第一条任务：

> 继续开发 `jiangkaiqi2005/CSM-Forge` 的 `fix/forge-cqu-integration-docs` 分支。先完整阅读 `docs/HANDOFF-FORGE-ALPHA-20260916.zh-CN.md` 和五份 V3 设计文档，并核对当前 HEAD/CI。不要重做架构，不要合并 main/develop，不要回退 Command Replay。当前目标不是继续堆功能，而是固定最小可玩 Alpha：保持 Domain ID/Host-Client 对称/lifecycle cleanup/Alpha safety/projection-audit 门禁全绿，优先拿全绿 Runtime Artifact 做真实双机 Host/Join/热加入 smoke test。发现 bug 后按启动/加载/root mismatch/Join/Building-Net/Transport/projection drift 的优先级修。`f8865aa5a219b6d3b339720ee4bddd941e610821` 是目前已经确认 Windows/Linux/Mono/真实 CS1 Runtime Package 全绿的 Alpha 候选基线；如果当前 HEAD 更新，必须先证明它同样全绿。

---

## 17. 当前一句话状态

**Forge 已经从“架构原型”推进到“可自动编译打包、核心建城 Authority 域大量完成、未支持写操作 fail-closed 的最小可玩 Alpha 候选”；现在最缺的不是继续写更多领域，而是拿同一 commit 的真实游戏包进行双机/多机实测，并根据真实 root/Join/Projection 日志收稳定性。**
