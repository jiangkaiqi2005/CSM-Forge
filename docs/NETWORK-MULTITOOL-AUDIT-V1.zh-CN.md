# Network Multitool 1.3.9 Authority 审计

## 固定源码基线

- 官方仓库：`MacSergey/NetworkMultitool`
- Stable tag：`v1.3.9`
- 源码提交：`2abaf77e665f8b1c2cae65de188f9109f28f0c0b`
- 锁定的 ModsCommon 子模块：`984abb421550369a285498769575fc01c005096b`
- Stable Workshop ID：`2560782729`
- Stable assembly version：`1.3.9.0`

Forge 只支持以上精确表面。安装 Harmony patch 前会同时验证程序集名称/版本、完整方法参数、`Point` 字段与构造器、`SegmentSelection(ushort)`、`Settings.NeedMoney.value` 和 `GetCost(Point[], NetInfo)`。任一漂移会让可选 bridge 整体 fail-closed，不会退回 Client 直接写 `NetManager`。

## 高层操作与最终 mutation

| 模式 | 1.3.9 高层入口 | 源码中的 graph mutation | Forge semantic intent |
|---|---|---|---|
| Add node | `AddNodeMode.InsertNode(ushort, Vector3)` | 删除旧 segment；创建 node 与两个 segment | Stable Segment + position |
| Remove node | `RemoveNodeMode.RemoveNode(ushort)` | 删除两个 segment 和 node；创建替代 segment | Stable Node |
| Union node | `UnionNodeMode.Union(ushort, ushort)` | 每个关联 segment 删除后重建到目标 node；删除源 node | 两个 Stable Node |
| Split node | `SplitNodeMode.Split(ushort, Vector3, IEnumerable<Selection>)` | 创建新 node；选中 segment 删除后重建到新 node | Stable Node + position + 有界 Stable Segment 集合 |
| Intersect | `IntersectSegmentMode.IntersectSegments(ushort, ushort)` | 删除两个 segment；创建交点 node 与四个 segment | 两个 Stable Segment |
| Parallel | `CreateParallelMode.Create(Point[], bool, NetInfo, int)` | 从绝对 Point 几何创建 node/segment | prefab + invert + 有界绝对 Point 几何 |
| Connection / Curve / Loop | `BaseCreateMode.Create(Point[], bool, ushort, ushort, bool, bool, NetInfo, bool, int)` | 连接两个既有 segment 端点并创建中间 node/segment | 两个 Stable Segment + endpoint sides + prefab + flags + 有界绝对 Point 几何 |

这些入口没有要求 Host/Client native slot 相同。Client 只提交 Stable-ID semantic intent；Host 将 Stable ID 解析到自己的 native slot、调用以上 1.3.9 高层入口，再由既有 Net `Reconcile()` 发布 delete/upsert absolute graph mutation。Client 的应用顺序固定为：删除 segment、删除 node、创建 node、创建 segment，因此 endpoint 引用不会依赖 Host slot 或创建顺序。

## Economy 与跨域副作用

- Parallel/Connection 的 construction cost 由 Host 使用精确 1.3.9 `Settings.NeedMoney` 与 `GetCost(Point[], NetInfo)` 重新计算；Client 不提交金额。
- Host 调用位于 `EconomySideEffectCapture` 内，Client 只投影最终 construction/refund side effect，避免双扣。
- Host 调用位于 Net `ApplyScope` 内；道路引发的 Tree/Prop collateral clear 会被允许并标记对应 absolute shard dirty。
- Zone 和其他既有观察域继续按最终状态收敛；不会把 Multitool 命令广播给 Client 重跑。

## 证据边界

当前证据覆盖源码审计、DTO/codec 测试、Windows/Linux kernel、Mono/net35、真实 CS1 reference build 与 runtime package。尚未完成真实 Network Multitool assembly 的游戏内加载，也未完成 Host/Client、hot join、slot reuse、费用和混装双机验证；这些项目在 P9 前均保持“未验证”，不能标为 gameplay validated。
