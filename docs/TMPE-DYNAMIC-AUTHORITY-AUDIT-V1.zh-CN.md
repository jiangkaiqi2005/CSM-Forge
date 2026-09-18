# TM:PE 11.9.4.1 Dynamic Authority 审计

审计基线与 P6-A 相同：官方 tag `11.9.4.1` / commit `7d1360d39047a7fcee59743aabdc705b8fe29914`，实际程序集 `TrafficManager, Version=11.9.4.25100`，SHA-256 `2cd504e24b4d89a7361e037610bd5a6bffc8c2184bf62094f9d3f32ce86a52df`。

源码确认：

- `TrafficManager.Custom.PathFinding.CustomPathManager.CustomCreatePath` 直接分配/排队 PathUnit；
- `CustomPathManager.CustomReleasePath` 直接释放 PathUnit chain；
- `CustomPathManager.SimulationStepImpl(int)` 覆盖 base manager；
- `TrafficManager.Lifecycle.ThreadingExtension` 在 tick/frame 中推进 Geometry、Routing、TrafficLight simulation 与 transfer queue；
- TM:PE 通过 Car/Human/Train 等 AI patch 参与 vehicle lane selection 和 path choice。

Forge exact-version dynamic bridge 对 Client 阻断上述 CustomPathManager create/release/simulation 和三个 ThreadingExtension simulation callback。base `VehicleManager`、`CitizenManager`、`PathManager` simulation 也被 Client barrier 阻断。Host 继续执行 TM:PE，结果由 `builtin.path-results`、`builtin.vehicle-presentation`、`builtin.citizeninstance-presentation` 与 P6-A persistent-rule adapter 发布。

Path payload 只包含 Forge Stable Path identity；route 的网络引用只包含 Stable Segment identity + lane index/offset。Vehicle/CitizenInstance payload 只包含 Forge stable entity/reference identity，不包含 native VehicleId、CitizenInstanceId、PathUnitId、BuildingId、NetSegmentId。

这仍是代码/metadata/CI 证据。TM:PE UI 写入口和真实双机、多机、hot join/rejoin、长时间运行尚未 gameplay validated，所以兼容表暂时继续 `blocked-mod`。
