# TM:PE 11.9.4.1 持久交通规则审计

## 1. 精确基线

- Steam Workshop：`1637663252`
- 官方源码：`CitiesSkylinesMods/TMPE` 标签 `11.9.4.1`，commit `7d1360d39047a7fcee59743aabdc705b8fe29914`
- 实际安装程序集：`TrafficManager, Version=11.9.4.25100`
- 实际 `TrafficManager.dll` SHA-256：`2cd504e24b4d89a7361e037610bd5a6bffc8c2184bf62094f9d3f32ce86a52df`
- `IUserMod`：`TrafficManager.Lifecycle.TrafficManagerMod`

Forge 只在程序集名、完整版本、`IUserMod` type、Configuration DTO、manager/facade 和 Load/Save/Clear 方法表面全部吻合时注册 `bridge.tmpe-persistent-rules` schema 1。未知版本或 surface 漂移不会模糊反射继续运行。

## 2. P6-A 覆盖面

对 `SerializableDataExtension.Save/LoadDataState` 与各 manager 的 11.9.4.1 源码逐项核对后，adapter 覆盖：

- priority signs；
- junction restrictions（U-turn、near/far turn-on-red、straight lane change、enter blocked junction、pedestrian crossing）；
- timed traffic lights、node group、steps、segment/vehicle lights；
- lane arrows；
- road/track lane connections；
- lane speed limits与 custom default speed limits；
- lane vehicle restrictions；
- parking restrictions；
- saved-game options opaque payload。

投影前会清理上述旧规则，再按 TM:PE 自身加载顺序重建 absolute state；失败会抛错并进入既有 replica failure/fencing 路径。payload 受单个 Forge frame 上限约束，超限 fail-closed。

## 3. 网络身份边界

TM:PE 的原始 `Configuration` 使用 `ushort nodeId/segmentId` 与 `uint laneId`，这些值不会直接编码到 Forge payload：

- node → Forge Stable Node identity；
- segment → Forge Stable Segment identity；
- lane → Forge Stable Segment identity + 该 segment prefab lane-chain index。

Replica 使用本地 Net Authority map 反解为本机槽位。wire codec 中不存在原生 node、segment 或 lane ID 字段。

## 4. 当前仍然 blocked

P6-A 只闭合持久规则数据面；它没有宣称 TM:PE 动态交通已经安全。`TrafficManager.Lifecycle.TrafficManagerMod` 继续被分类为 `blocked-mod`，原因包括：

- custom pathfinding decisions；
- vehicle lane selection；
- TM:PE traffic AI side effects；
- Citizen / Vehicle / Path 动态 authority 代码闭包已在 P7-B 完成，但真实联机行为尚未验证；
- Client 交通工具 UX/写入入口尚未进入 Host authority intent 边界。

P6-B/P7 已提供重新评估所需的 dynamic code closure，但 UI 写入口与真实联机证据仍不充分，因此当前不解除 block。代码编译、单元/契约测试和 CI green 都不等于真实双机 gameplay validated；TM:PE 11.9.4.1 的双机、多机、hot join/rejoin、长时间运行仍明确**未验证**。
