# P7 Citizen / Vehicle / Path Authority closure v1

状态：P7-A（Building natural lifecycle）代码完成，真实双机/多机未验证。

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

## 尚未完成

- Vehicle / CitizenInstance / PathUnit lifecycle、coarse/sharded result 和 client manager barrier。
- TM:PE dynamic PathManager / traffic AI 闭包；因此 TM:PE 继续是 `blocked-mod`。
- 真实双机/多机 gameplay、断线恢复和 hot-join 验证。

CI green 只证明编译、协议/契约测试和打包门禁，不等于 gameplay validated。
