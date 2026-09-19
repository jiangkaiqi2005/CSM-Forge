# P7 Citizen / Vehicle / Path Authority closure v1

状态：P7-B authority closure 代码完成，真实双机/多机未验证。

## 固定分类

| 状态 | Authority 分类 | 网络表示 |
| --- | --- | --- |
| `VehicleManager` / Vehicle AI 推进 | Host-only simulation | Client 不运行 manager simulation；只接收 lifecycle、coarse presentation 和稳定引用结果 |
| `CitizenManager` / CitizenInstance AI 推进 | Host-only simulation | Client 不运行 manager simulation；只接收 instance lifecycle 与 coarse presentation |
| `PathManager` / pathfinding worker | Host-only causal computation | path result/route choice 以 Forge Stable Path identity 和 Stable Net identity 表示；Client 不重新求路 |
| Vehicle / CitizenInstance frame | Client presentation input | 分片 absolute result；允许低频/coarse 更新，不要求逐 tick 广播 |
| Vehicle / CitizenInstance create/release | persistent lifecycle | Forge stable entity identity；不得发送 native VehicleId/CitizenInstanceId |
| PathUnit create/release/route | persistent causal state | Forge stable Path identity；每个 lane position 使用 Stable Segment identity + lane index/offset |
| Building upgrade/abandon/collapse/value state | Host-owned simulation side effect | Building domain `Updated` absolute result + `builtin.building-simulation` 分片标量结果 |
| hot join baseline | snapshot-only baseline | `.crp` 安全点 + Forge stable identity metadata + authority roots |

## P7-A 已实现边界

- Building domain 的 committed root 由已提交 absolute state 计算，不再把两端本地自然模拟当作权威根。
- Host 分批检查已映射 Building 的 prefab/placement/length 变化，并发布 `Updated` result；Client 通过本地 native mapping 投影。
- `builtin.building-simulation` 只同步 Building 的非身份 scalar、flags、problem bits 和 frame scalar，按 Stable Building identity 分 128 shards。
- 下列 native/结构字段明确不进入该 payload：Building/Net/Event/Vehicle/Citizen/Water/Transport 等 index/link、prefab `m_infoIndex`、grid links、position/angle/length/buildIndex。prefab 和 placement 由 Building domain 管理。
- ClientLive 阻断 `BuildingManager.SimulationStepImpl(int)`；ApplyScope 投影仍允许执行。

## P7-B 已实现边界

- `builtin.path-results`：256 shards；Host 分配 Stable Path identity，route 中每个 `PathUnit.Position` 把 native segment 转为 Stable Segment identity，仅保留 lane index/offset。Client 直接分配本地 PathUnit 并投影 absolute route，不运行 pathfinding worker。
- `builtin.vehicle-presentation`：256 shards；同步 Vehicle stable lifecycle、prefab、四帧 coarse presentation、target/segment 值、Stable Building/TransportLine/Path 引用。原生 VehicleId、BuildingId、TransportLineId、PathUnitId 不进 wire。
- `builtin.citizeninstance-presentation`：256 shards；同步 CitizenInstance stable lifecycle、prefab、四帧 coarse presentation、target/color 和 Stable Building/Path 引用。底层 Citizen record 作为 Host-only state，只在 hot-join `.crp` baseline 中提供，不作为双方自然模拟依据。
- ClientLive/ClientLoading/ClientRecovering 阻断 `VehicleManager`、`CitizenManager`、`PathManager` 的 create/release/simulation 入口；ApplyScope 仍可创建本地 projection slot。
- exact TM:PE `11.9.4.25100` dynamic bridge 阻断 Client 的 `CustomPathManager` path create/release/simulation 和 TM:PE threading simulation callbacks；Host TM:PE 的 route/vehicle/citizen result 进入上述 P7 adapter。
- extension aggregate entry bound 从 2048 提升至 4096，以容纳 81 Tiles 462×2 utility shards、Tree/Prop、P7 dynamic shards 和其他既定 adapter；单个 delta 仍受 `Limits.FramePayloadBytes` 限制。

## 尚未验证/仍保持 fail-closed

- TM:PE UI 写入口还没有真实联机行为证据；因此 TM:PE 继续是 `blocked-mod`，不能因为 dynamic bridge 编译成功就提前声明 Supported。
- 真实双机/多机 gameplay、断线恢复和 hot-join 验证。

CI green 只证明编译、协议/契约测试和打包门禁，不等于 gameplay validated。
