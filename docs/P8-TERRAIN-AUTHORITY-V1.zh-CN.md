# P8 Terrain Authority v1

状态：P8 absolute heightmap authority 代码完成，真实双机/多机未验证。

## 真实 CS1 surface 结论

以本机合法安装的 `Assembly-CSharp.dll` 做 metadata/IL 定点审查后确认：

- `TerrainTool.ApplyBrush()` 直接写 `TerrainManager.RawHeights`；raw map 是 1081×1081 的 `UInt16` 高度点阵。
- brush 写入后调用 `TerrainModify.UpdateArea(..., heights: true, surface: false, zones: false)`。
- `TerrainManager.FinalHeights` 不是 brush 的权威输入层；它由 `TerrainModify` 与 `BuildingManager`、`NetManager` 等 `ITerrainManager` 回调共同重算。
- `TerrainTool.ApplyUndo()` 同样恢复 `RawHeights` 后调用 `TerrainModify.UpdateArea`。

因此 wire 上同步的是 brush/undo 已执行后的 raw height absolute result，不是鼠标坐标、brush mode、strength、随机数或 Command Replay。

## Authority 与网络表示

- `builtin.terrain-heights` 固定为 136 shards，每 shard 最多 8 行；最后一个 shard 只有 1 行。
- payload 只含 canonical shard header、absolute `UInt16` heights；shard 0 另含 Host `DirtBuffer` absolute value。
- Terrain heightmap 没有实体引用，payload 不含任何 native Building/Net/Tree/Prop ID，也不创建网络身份。
- Host/SinglePlayer 可以运行 `TerrainTool.ApplyBrush` 与 `ApplyUndo`；ClientLoading、ClientReplicaLive、ClientRecovering、WorldFenced 的本地 terrain tool 写入被阻断。
- Client 在 Forge ApplyScope 内覆盖对应 raw rows，再调用与原版 brush 相同的 `TerrainModify.UpdateArea` 入口重建派生 terrain layer。
- Net/Building 的合法 terrain callbacks 会参与重算；若它们产生可持久结果，既有 Host-owned Net/Building domain 和 Building simulation adapter 仍是权威投影来源。
- 全部 shards 进入 extension aggregate root、snapshot baseline 和后续 absolute delta；没有 mouse brush replay，也没有 Command Replay。

## 已验证与未验证

代码门禁将覆盖：真实 CS1 reference build、metadata probe、Windows/Linux kernel、net8、Mono/net35、docs-contract 与 runtime package。

仍未验证：真实 1 Host + 1/2 Client brush/undo、跨 shard 大 brush、Net/Building/Tree/Prop collateral、hot join、drop/rejoin、lagging-client rebaseline 和长时间 projection drift。CI green 不能写成 gameplay validated，也不能据此宣称 1.0 RC。
