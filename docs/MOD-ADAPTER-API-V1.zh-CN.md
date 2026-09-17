# Forge Mod / DLC Adapter API V1

## 目标

Forge 扩展层把 DLC 专属系统与第三方 Mod 接入同一条 Host Authority 管线。它不是第二套网络协议，也不是旧 CSM Command Replay 兼容层。

固定数据流：

`本地语义操作 -> Forge Extension Intent -> Host adapter 执行 -> 捕获最终绝对状态 -> AuthorityBatch -> Client adapter 绝对投影`

任何 adapter 都不得把 CS1 原生 `BuildingId / NetId / ParkId / EventId / VehicleId / DisasterId ...` 当作网络身份发送。实体型扩展必须通过 `IForgeAdapterContextV1` 使用 `EntityIdentityV2`。

## Adapter 类型

### `IForgeStateAdapterV1`

只用于没有实体身份、且单个绝对状态不超过一个 Forge frame 的全局状态：

- 稳定 `AdapterId`；
- 非零 `SchemaVersion`；
- `CaptureAbsolute()`；
- `ApplyAbsolute(...)`。

### `IForgeStateAdapterV2`

用于包含实体的系统。Forge 提供：

- `TryGetIdentity(nativeId)`；
- Host-only `GetOrAllocateIdentity(nativeId)`；
- `TryGetNative(EntityIdentityV2)`；
- Client-side `BindKnownIdentity(...)`；
- `RetireIdentity(...)`；
- `SnapshotMappings()`。

命名实体映射持久化在 `CSM-Forge.V3.ExtensionEntityMaps`，以 `AdapterId`/专用 namespace 保存。Adapter 注册顺序变化不会改变既有 Stable ID。

### `IForgeShardedStateAdapterV1`

大型状态必须拆成 deterministic shard：

- `ShardCount` 在一个 active session 内固定；
- `CaptureShard(context, shardIndex)`；
- `ApplyShard(context, shardIndex, state)`；
- 每个 shard 独立受单 frame 上限约束；
- Extension domain aggregate root 覆盖所有 `(adapterId, shardKey, payloadRoot)`。

因此几十个 DLC/Mod 的总状态可以超过 64 KiB，而不允许任何单帧绕过协议预算。

### Interactive adapters

`IForgeInteractiveStateAdapterV1` 和 `IForgeInteractiveShardedStateAdapterV1` 用于 Client 可发起修改的扩展。

Client UI/Harmony hook 必须取消本地持久写入，再调用：

`ForgeExtensionApi.TrySubmitIntent(adapterId, payload)`

Host 收到 intent 后才执行真实游戏 API；成功后 Forge 捕获并广播最终绝对状态。Client 不执行 Host 命令、不重放工具输入。

## 第三方 Mod 兼容声明

Mod 可在 multiplayer session 启动前调用：

`ForgeCompatibilityApi.Declare(assembly, kind)`

`kind` 有四种：

- `ExactMatch`：默认。Host/Client 都必须有相同 assembly/config fingerprint；
- `ClientOnly`：纯 UI / 显示 / 本地工具；允许单端存在或版本不同；
- `ForgeSynchronized`：该 assembly 必须同时注册至少一个 Forge state adapter；没有 adapter 时 manifest 收集直接 fail-closed；
- `Blocked`：已知会改变共享模拟但没有安全 authority 方案，Host 开房前拒绝。

声明不是“自我认证无害”。Host 的 compatibility policy 仍是最终裁决；声明在 active multiplayer session 内冻结，不能中途改类别。

## 兼容握手

Adapter 以：

`adapter:<id>:v<schema>`

进入 Compatibility Manifest，并带 adapter assembly binary/config fingerprint。

组件规则：

- `dlc:*`：Host 使用的 DLC 是 Client 必需项；Client 可额外拥有 DLC；
- `asset:*`：Host 使用的资产必须存在且 fingerprint 匹配；Client 可额外拥有资产；
- `client-mod:*`：经过审计/声明的纯客户端 Mod 可以只存在一端；
- `mod:*`：未知 Mod 默认严格匹配；
- `sync-mod:*`：Forge-synchronized Mod，assembly 本身严格匹配，并且对应 `adapter:*` 也必须严格匹配；Client 额外 `sync-mod` 不允许；
- `blocked-mod:*`：Host 创建 room 即失败；
- `dependency-mod:*`：基础运行依赖仍进入 manifest。

未知共享模拟组件继续 fail-closed；“兼容大多数 Mod”不等于放行无法分类的 Harmony/Manager 修改。

## Stable entity 规则

第三方 adapter 如果状态里包含游戏实体：

1. wire 只携带 `EntityIdentityV2`；
2. Host 才能分配新 Stable ID；
3. Client 只能把 Host 已签发 identity 绑定到自己的 native slot；
4. native slot reuse 必须先 retire 旧 identity；
5. snapshot/save 必须保留 identity watermark 和 live mapping；
6. adapter 不得假设两端数组 slot、创建顺序或 manager index 相同。

## 状态预算与安全边界

- 单个 absolute delta/shard 不得超过 `Limits.FramePayloadBytes`；
- Adapter 必须规范排序，保证相同绝对状态得到相同 payload/root；
- 解码必须拒绝 trailing bytes、越界 count、未知 schema；
- Apply 后 Forge 会重新 Capture；如果实际 payload root 不能重现 Host 要求的 payload root，Replica 直接报 projection failure；
- 大状态不得通过“发送 command log”规避 frame 上限。

## Projection Audit

Extension state 参与 Projection Audit。Audit 只记录 drift；它不得因为 fingerprint 不一致自动 kick、fence、snapshot 或 resync。

## 内置参考实现

当前内置 adapter 展示了几种推荐模式：

- `builtin.districtpark`：Stable entity lifecycle；
- `builtin.parkgrid`：512×512 grid 拆成 64 个 absolute shard；
- `builtin.districtpark-controls`：Client semantic intent；
- `builtin.districtpark-campus`：Host-only 时间/学年相关深状态；
- `builtin.districtpark-deep`：保守递归同步安全 value-state，同时硬排除 identity-like field path；
- `builtin.events`：Event Stable ID + Building Stable ID + local slot materialization；
- `builtin.disasters`：Disaster Stable ID + local slot materialization。

这些实现都继续走 `AuthorityBatch`，没有重新引入 native ID 对齐或 Command Replay。
