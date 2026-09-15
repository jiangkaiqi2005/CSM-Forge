# CSM-Forge

面向 **Cities: Skylines 1** 的 Host-authoritative 多人共同经营系统。Forge 作为新一代主干，吸收 CSM-CQU 已验证的异常安全、热加入、兼容采集、世界传输和真实 CS1 接入经验，但不继承旧的 Command Relay/Replay 作为最终同步模型。

当前 `fix/forge-cqu-integration-docs` 已从 M0 参考内核推进到 **V3 development runtime**：具备 v2 会话/结果协议、AuthorityBatch、AppliedAck、固定 Barrier/Activation、LiteNetLib 开发直连、真实 CS1 生命周期/线程/存档元数据、Mod/Asset 指纹采集、流式 Snapshot 文件传输、原生 `.crp` 保存/加载桥，以及首个真实 Host-authoritative 领域切片（水服务昼/夜预算）。

**这仍不是已完成 E3/E4 实机验收的玩家发行版。** 仓库无法提交或在公共 CI 中使用游戏 DLL，因此真实 CS1 编译、两机联机、不同城市规模、弱网与长跑必须在安装了合法 CS1 的 Windows 环境继续验证。开发传输使用 LiteNetLib + 房间口令，只适合受控开发/LAN 测试，不等同最终公网认证方案。

## 当前 V3 能力

- Host 单写者、世界 `CommitRevision`、Domain root 与 aggregate root；
- 玩家 Intent 与 Host/Simulation 结果分离，Simulation batch 不伪造玩家 RequestId；
- Replica 只应用连续批次，Gap 请求日志，投影成功后发送 `AppliedAck`；
- 独立 `JoinId/JoinGeneration`，固定 H Barrier 与 A Activation，不追逐 Host 当前头；
- 多加入者共享不可变 Snapshot 文件，但各自拥有独立传输游标、TransferId 和取消生命周期；
- Snapshot 采用 32 KiB 分块、offset/index 校验、chunk SHA-256 与整文件 SHA-256，Client 先写临时文件，完整校验后才交给游戏；
- Host 通过真实 `SavePanel.SaveGame` 生成 `.crp`；Client 采用 CSM-CQU 已验证的内存 `Package` + `LoadingManager.LoadLevel` 线程边界加载；
- Forge 主动换图时保留网络 Session，新 `LoadGeneration` 从存档内 Forge metadata 恢复后再继续 catch-up；
- Compatibility Manifest 采集游戏 build、程序集/插件/资产身份并默认严格匹配；
- 首个真实领域 `WaterBudget`：Client 的 `SetBudget(Water, ...)` 被拦截为 Intent，Host simulation-thread 实际执行，再向 Client 投影绝对昼/夜预算结果；
- LiteNetLib 仅作为开发 Transport，业务身份、权限、Revision、Join 与 Authority 都由 Forge 上层决定；
- Core/Protocol/Transport/Checkpoint 的 net35/net8 回归由 GitHub Actions 在 Linux/Windows 执行。

当前没有宣称已经覆盖建筑、道路、车辆、居民、完整经济、自然模拟闭包或任意第三方模拟 Mod。未覆盖的持久操作不应被当成已支持功能。

## V3 设计文档

| 内容 | 文档 |
| --- | --- |
| Forge × CSM-CQU 融合总架构 | [ARCHITECTURE-V3](docs/ARCHITECTURE-V3.zh-CN.md) |
| CSM-CQU 机制迁移审计 | [CSM-CQU-MIGRATION](docs/CSM-CQU-MIGRATION.zh-CN.md) |
| 真实 CS1 Runtime 接入 | [CS1-RUNTIME-INTEGRATION](docs/CS1-RUNTIME-INTEGRATION.zh-CN.md) |
| Host Authority / 结果复制渐进迁移 | [AUTHORITY-REPLICATION](docs/AUTHORITY-REPLICATION.zh-CN.md) |
| V3 Gate 与施工路线 | [IMPLEMENTATION-ROADMAP-V3](docs/IMPLEMENTATION-ROADMAP-V3.zh-CN.md) |
| 并行加入安全不变量 | [并行加入](docs/spec/PARALLEL-JOIN.zh-CN.md) |
| 协议 v2 | [协议 v2](docs/spec/PROTOCOL-V2.zh-CN.md) |
| 运行时复制 | [运行时复制](docs/spec/RUNTIME-REPLICATION.zh-CN.md) |
| 故障与恢复 | [鲁棒性规范](docs/spec/ROBUSTNESS.zh-CN.md) |
| 实机/长跑验收 | [验收规范](docs/spec/ACCEPTANCE.zh-CN.md) |

旧 M0/v1 文档仍保留用于历史与兼容测试，不再代表 V3 当前实现主线。

## 构建纯内核与协议

```powershell
dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release --nologo
dotnet run --project tests/Forge.Tests/Forge.Tests.csproj -c Release -f net8.0 --no-build
```

Linux CI 还会执行实际 `net35` 产物的 Mono 回归。现代 .NET/标准 Mono 通过不等于游戏自带 Mono 与真实 CS1 已通过，但可以阻止 Core/Protocol/Transport/Checkpoint 回归。

文档合同：

```powershell
python scripts/check_docs.py
python -m unittest discover -s tests/docs -p "test_*.py"
```

## 构建真实 CS1 Runtime

仓库不会提交 Cities: Skylines、Unity 或 Steam 的游戏程序集。请使用你本机合法安装的 CS1 `Cities_Data/Managed`：

```powershell
pwsh ./scripts/build-runtime.ps1 `
  -CitiesManagedPath "D:\SteamLibrary\steamapps\common\Cities_Skylines\Cities_Data\Managed"
```

脚本会：

1. 检查 `ICities.dll`、`Assembly-CSharp.dll`、`ColossalManaged.dll`、`UnityEngine.dll`；
2. 编译 `Forge.Runtime.Cities1` 的 net35 版本；
3. 拒绝把游戏自有 DLL 打入发行包；
4. 将 Forge DLL 与允许的依赖放到 `dist/runtime/CSM-Forge`；
5. 生成 `SHA256SUMS.txt` 和 `CSM-Forge-runtime.zip`。

CitiesHarmony 需要在游戏环境中可用。首次测试请只使用备份城市。

## 开发直连测试流程

当前开发 UI 位于 Mod 设置页：

1. Host 与 Client 都安装同一 Forge build，并保证 Compatibility Manifest 可通过；
2. Host 进入城市，在设置页填写 UDP 端口与临时房间口令，点击 **Host 当前城市**；
3. Client 进入任意测试城市/存档作为运行时起点，填写 Host IPv4、端口、相同房间口令，点击 **Join Host 城市**；
4. Host 生成 Forge Snapshot，Client 流式下载并校验后加载 Host `.crp`；
5. Client 追赶 Authority journal，完成固定 Barrier/Activation 后进入 `ClientLive`；
6. 当前可验证的持久编辑切片仅是水服务昼/夜预算。其他领域尚未达到 V3 Authority 验收，不应据此认定已支持完整共同建设。

如果日志淘汰、Snapshot/Projection 校验失败或客户端状态根不匹配，设计目标是只隔离/恢复该 Client；Host 世界自身出现未记录持久变化则 Fence 房间，禁止继续发布不可信结果。

## 当前仍需真实游戏验收的阻断项

- `scripts/build-runtime.ps1` 在目标 CS1 build 上的实际编译；
- CitiesHarmony patch 签名与 Water Budget Hook 实机命中；
- Host `.crp` Snapshot 在真实大/小城市中的保存安全点；
- Client 加载期间 LiteNet 网络线程保活与新 `LoadGeneration` 重绑定；
- 两名 Client 同时加入、一人慢/取消不阻断另一人；
- Gap recovery、日志淘汰后的重新 Snapshot；
- 24 小时长跑、弱网、断线/重连、存档/重新加载；
- Building、Road/Net、自然模拟等后续领域闭包；
- 最终公网认证 Transport（当前 room-key LiteNet 仅是开发适配器）。

因此当前分支可以作为 **V3 实机开发候选**，但在上述 E3/E4 证据完成前，不应标记为 Release/玩家正式版。
