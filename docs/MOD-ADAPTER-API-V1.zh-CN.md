# Forge Mod / DLC Adapter API V1

## 目标

Forge 的扩展层用于把 DLC 专属系统和第三方 Mod 接入同一条 Host Authority 管线。扩展层不是第二套网络协议，也不是旧 CSM 的 Command Replay 兼容层。

固定数据流：

`本地语义操作 -> Forge Extension Intent -> Host adapter 执行 -> 捕获最终绝对状态 -> AuthorityBatch -> Client adapter 绝对投影`

任何 adapter 都不得把 CS1 原生 `BuildingId / NetId / ParkId / EventId / VehicleId ...` 作为网络身份发送。实体型扩展必须通过 `IForgeAdapterContextV1` 使用 `EntityIdentityV2`。

## Adapter 类型

### `IForgeStateAdapterV1`

只用于没有实体身份的全局状态。实现：

- 稳定 `AdapterId`；
- 非零 `SchemaVersion`；
- `CaptureAbsolute()`；
- `ApplyAbsolute(...)`。

### `IForgeStateAdapterV2`

用于包含实体的系统。除绝对状态捕获/投影外，Forge 提供：

- `TryGetIdentity(nativeId)`；
- Host-only `GetOrAllocateIdentity(nativeId)`；
- `TryGetNative(EntityIdentityV2)`；
- Client-side `BindKnownIdentity(...)`；
- `RetireIdentity(...)`；
- `SnapshotMappings()`。

命名实体映射持久化在 `CSM-Forge.V3.ExtensionEntityMaps`，以 `AdapterId` 为 namespace。Adapter 顺序变化不会改变既有实体身份。

### `IForgeInteractiveStateAdapterV1`

需要让 Client 发起修改的扩展实现该接口。Client UI/Harmony hook 必须取消本地持久写入，再调用：

`ForgeExtensionApi.TrySubmitIntent(adapterId, payload)`

Host 收到 intent 后由 adapter 执行；成功后 Forge 捕获并广播最终绝对状态。Client 不执行 Host 命令、不重放工具输入。

## 兼容握手

Adapter 会以 `adapter:<id>:v<schema>` 进入 Compatibility Manifest，并包含 adapter assembly 指纹。Host 与 Client 的 adapter/schema/binary 必须匹配。

当前组件策略：

- `dlc:*`：Host 拥有的 DLC 是 Client 必需项；Client 可额外拥有 DLC；
- `asset:*`：Host 使用的资产必须存在，Client 可额外拥有资产；
- `client-mod:*`：经过审计的纯客户端/UI Mod 可以只存在一端、版本也可不同；
- `mod:*`：未知 Mod 默认严格匹配；
- `blocked-mod:*`：已知会改共享模拟但尚无 Forge adapter 的 Mod，Host 开房前直接拒绝；
- `adapter:*`：由 Forge adapter 明确接管的状态扩展。

未知组件继续 fail-closed，不以“兼容大多数 Mod”为理由放宽未知共享模拟状态。

## 状态与带宽约束

Extension Authority Domain 的聚合 root 对每个 adapter payload 的 SHA-256 做规范聚合。不同 adapter 的总状态可以超过 64 KiB，但单个 live absolute delta 必须适配单个 Forge frame。

大状态系统必须拆分为多个有稳定 ID 的 adapter/shard；禁止绕过 frame 上限或在一个 payload 内塞无限状态。

## Projection Audit

Extension state 参与 Projection Audit。Audit 只记录 drift；它不得因为指纹不一致自动 kick、fence、snapshot 或 resync。

## 内置 DLC 方向

`builtin.districtpark` 是第一批内置 adapter，覆盖 CS1 `DistrictPark` 共享容器的 Stable ID 与持久标量状态，为 Parklife / Industries / Campus / Airports / Pedestrian Areas 等共享系统提供共同底座。面积 `m_parkGrid`、大型事件数据和各 DLC 的专属高频模拟状态继续拆分，不回退到 brush/command replay。
