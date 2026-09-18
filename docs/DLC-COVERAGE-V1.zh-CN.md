# CSM-Forge 官方 DLC Authority 覆盖矩阵 V1

## 1. 定义

本表回答的是“官方 DLC 的共享持久状态由哪条 Forge Authority 路径负责”，不是“两台真实机器已经逐项玩通”的验收表。

代码覆盖使用三类：

- **CORE**：DLC 的玩家持久操作和共享世界写入复用 Forge 已有 Building / Net / Transport / District / Economy / Tax / Budget / Water / Weather / Clock 等核心域；
- **DEDICATED**：DLC 引入独立持久 Manager / Container，Forge 提供专用 absolute-state adapter，并使用 `EntityIdentityV2` 而不是 CS1 native slot；
- **CONTENT**：没有新的共享模拟状态，只需要 DLC ownership / asset manifest 兼容检查。

固定规则仍然是：

`Intent -> Host authoritative execution -> AuthorityBatch -> Client absolute projection`

本矩阵没有 `UNCLASSIFIED` 项。代码覆盖完成不等于 E4/RC 真机验证完成；`BUILD_INFO.json` 仍必须保留 `gameplay_validation=NOT RUN BY CI`，直到真实多机测试提供证据。

## 2. 官方玩法 DLC

| DLC | 代码覆盖 | Authority 路径 | 关键说明 |
|---|---|---|---|
| After Dark | CORE | Building + Transport + Economy + Clock + District | 休闲/旅游建筑仍是 Building Stable ID；昼夜时钟由 Host authority |
| Snowfall | CORE | Weather + Net + Water + Transport + Budget + Building | 天气、供热/网络、公交/电车与预算走既有核心域 |
| Natural Disasters | DEDICATED | `builtin.disasters` + Building + Net + Event | Disaster 使用独立 `EntityIdentityV2`；Client 不自行创建/释放灾害 |
| Mass Transit | CORE | Net + Transport + Building + Budget | 新道路/轨道与线路仍使用 Net/Transport Stable ID |
| Green Cities | CORE | Building + District + Policy + Tax + Budget | 专门建筑与政策不建立第二套网络身份 |
| Parklife | DEDICATED | `builtin.districtpark` + `builtin.parkgrid` + `builtin.districtpark-deep` + Building + Policy | Park slot 本地化；512×512 park grid 分 64 个 absolute shard |
| Industries | DEDICATED | DistrictPark adapters + Building + Economy + Transport | Industry Area 的安全值结构进入 deep projection；任何 identity-like 字段硬排除 |
| Campus | DEDICATED | DistrictPark adapters + `builtin.districtpark-campus` + `builtin.events` | coach hire timestamps / grant / academic-year gameplay state Host-owned |
| Sunset Harbor | CORE | Building + Transport + Water + Economy + Budget + Net | 捕鱼、交通与污水等持久操作复用核心域 |
| Airports | DEDICATED | DistrictPark adapters + Building + Transport + Economy | Airport Area/等级/安全深值状态使用 DistrictPark Stable ID |
| Plazas & Promenades | DEDICATED | DistrictPark adapters + Building + Net + District + Policy | Pedestrian Area 复用 park grid + deep state，不发送本地 ParkId |
| Financial Districts | CORE | Building + Economy + Tax + Budget + District | 金融建筑与资金层走核心 Economy/Building；未知独立组件仍由 manifest fail-closed |
| Hotels & Retreats | CORE | Building + Economy + District + Tax + Budget | 酒店实体使用 Building Stable ID；持久资金/区域操作由核心域裁决 |
| Match Day | DEDICATED | `builtin.events` + Building + Transport | Event lifecycle/result/color/ticket/security 使用 Event Stable ID |
| Concerts | DEDICATED | `builtin.events` + Building + Economy | Event 创建/释放/结果为 Host absolute state；Festival 的 event slot 不上 wire |

## 3. 其他官方内容

- **Pearls from the East**：CONTENT + Building core authority；新增 landmark/asset 不产生新的网络身份体系。
- **Content Creator Packs / Modder Packs**：CONTENT。所有 owned modder-pack bit 自动进入 `dlc:modderpack:*` manifest；实际启用资产再通过 `asset:*` fingerprint 校验。
- **Radio Stations**：CONTENT。音频是本地表现，不进入共享模拟 root。
- **Client 额外拥有 DLC/内容包**：允许；Host 使用的 DLC 与资产仍然是 Client 的必需兼容条件。

## 4. 专用 Adapter 的完成边界

### DistrictPark 家族

`Parklife / Industries / Campus / Airports / Plazas & Promenades` 共用：

- `builtin.districtpark`：实体生命周期、类型、等级、关键持久标量；
- `builtin.parkgrid`：64 个 8 行 shard，格子只携带 Stable-ID 字典码 + alpha；
- `builtin.districtpark-controls`：Campus/park policy 等交互 intent；
- `builtin.districtpark-campus`：coach hire timestamps、grant、动态 varsity attractiveness；
- `builtin.districtpark-deep`：递归同步安全值类型标量/结构；字段路径出现 `id/index/building/vehicle/citizen/node/segment/path/line/event/gate/target/info/prefab/...` 时硬排除，防止 native identity 泄漏。

### Event

`builtin.events` Schema V2：

- Event 自身使用 `EntityIdentityV2`；
- 关联 Building 使用 Building domain 的 Stable ID；
- Replica 缺事件时使用 `PrefabCollection<EventInfo>.FindLoaded + EventManager.CreateEvent` 在自己的空 slot 物化；
- Host 删除后 Client 使用 `ReleaseEvent` 收敛；
- flags / Success / Failure / color / ticket / security / frame / reward 等最终状态 absolute projection；
- Client 非投影路径不能直接 Create/Release Event。

### Natural Disasters

`builtin.disasters`：

- Disaster 使用 `EntityIdentityV2`；
- Replica 按 prefab 在本地 `CreateDisaster`，不要求 ushort slot 对齐；
- flags / intensity / seed / target / frames / casualties / fire/collapse counters absolute projection；
- Client 的直接 Create/Release/StartRandom 与 DisasterAI 强制生命周期入口 fail-closed；Host 是唯一权威模拟源。

## 5. 仍然独立于“DLC 覆盖”的基础游戏边界

Tree / Prop 已进入 Stable-ID + sharded absolute-state Authority，旧 Alpha safety patch 不再拦截它们；真实双机/多机仍未验证。Terrain 仍按安全策略 fail-closed；Citizen / Vehicle / Path 的完整自然模拟 authority 与生产级公网认证传输也不是本 DLC 矩阵的完成条件。它们属于完整 1.0 的基础游戏/网络层工作，不应和“某个 DLC 未分类”混为一谈。

## 6. 验收

DLC Authority 代码覆盖的自动门禁至少要求：

1. 上表所有官方玩法 DLC 都存在，且没有 `UNCLASSIFIED`；
2. Event V2 同时出现 Stable Event ID、Stable Building ID、`CreateEvent`、`ReleaseEvent`；
3. Natural Disasters adapter 已注册并出现 `CreateDisaster` / `ReleaseDisaster`；
4. Campus deep state 包含 `m_coachHireTimes`，Client 学年结算被阻断；
5. DistrictPark deep adapter 是 sharded absolute state，并存在 identity 风险字段硬排除；
6. docs-contract、Windows、Linux、Mono、真实 CS1 Runtime package 全绿。
