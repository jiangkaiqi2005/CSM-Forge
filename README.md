# CSM-Forge

面向 **Cities: Skylines 1** 的 Host-authoritative 多人共同经营系统。Forge 是新的生产主线：吸收 CSM-CQU 已验证的异常安全、热加入、兼容采集、世界传输和真实 CS1 接入经验，但不把旧的 Command Relay/Replay 当作最终同步模型。

当前 `feat/ultimate-dlc-mod-framework` 正推进到 **CSM-Forge 1.0 code-complete candidate**。它还不是玩家正式发行版，但已经具备真实 CS1 Runtime、自动可安装 ZIP、Host/Join/热加入/恢复链，以及持续扩展中的 Host-authoritative 领域。

## 1.0 Candidate 代码覆盖范围

已接入并进入 Forge Authority / Replica 主链：

- Host/Client 会话身份、MemberGeneration、ConnectionBinding、OperationCounter；
- `AuthorityBatch`、连续 `CommitRevision`、Domain root、aggregate root、`AppliedAck`；
- `.crp` Snapshot、32 KiB 流式传输、chunk/整文件 SHA-256；
- 并行 Join、独立 Transfer、固定 H Barrier、固定 A Activation；
- Journal catch-up、Gap recovery、日志淘汰后单 Client Snapshot rebaseline；
- Compatibility Manifest：游戏 build、程序集、插件和资产身份严格核对；
- Building：创建、删除/Bulldoze、建设费/退款、Host 自然创建/删除观察、稳定实体身份；
- Road/Net：节点/路段创建、删除、升级/变化捕获、费用/退款、稳定 Node/Segment identity；
- Zone：最终 zoning overlay；
- District：画区网格、Style、City/District Policy；
- Economy：Water 与通用 Budget、Tax、Cash、Loan/Bailout、Demand；
- Area unlock；
- Simulation Clock：暂停/速度，并随 Snapshot 恢复；
- TransportLine：稳定线路 identity、创建/删除、站点顺序、Add/Remove/Move Stop、颜色、预算、票价、日/夜启停；
- Stable Name：Building / Road Segment / District / TransportLine 自定义名称；
- City Name；
- Weather：Host 权威天气 target，Client 保留本地视觉插值；
- Tree / Prop：Stable ID + sharded absolute state；Client create/move/delete 转 semantic intent，Host 执行后再做 absolute projection；Building/Net 引发的 collateral clear 明确放行并使装饰物 shard 失效重捕获；
- Client projection audit：低频检查真实游戏投影与最后 Host committed root；当前严格 **diagnostic-only**，发现漂移只记录日志，不自动踢人或重同步。

### 当前安全边界

Tree / Prop 已进入 Forge Authority 闭包，不再由旧 Alpha safety patch 拦截。它们使用 Stable ID、分 shard absolute state 与 semantic intent；但在真实双机/多机完成 E3/E4 前，只能称为**代码覆盖完成、真机未验证**。

Terrain 已进入 Host-owned absolute height shard Authority：Host 可执行 brush/undo，Client 只投影最终 raw height rows，并通过原版 terrain update 管线重建 Net/Building 等派生结果。Client 本地 Terrain 工具继续阻断；真实双机/多机尚未验证。

Citizen / Vehicle / Path 已进入 Host-owned simulation/result Authority：Client 不运行对应 manager simulation，Path route 使用 Stable Path 与 Stable Segment identity，Vehicle/CitizenInstance 只投影 lifecycle 与 coarse presentation。DLC 与固定 Mod 的代码覆盖状态见各审查文档；所有这些路径仍需 E3/E4 真机矩阵和 projection audit 长跑，CI green 不代表 gameplay validated。

## 重要限制

- 当前 Transport 是 **LiteNetLib + 临时 room key** 的开发/LAN 适配器，不是最终公网认证方案；
- 真实游戏多机长跑、弱网、复杂 Mod/DLC 组合仍未完成 E4 验收；
- 当前 Candidate 只应用于备份城市；
- CI 能证明 Core/Protocol/Runtime 对参考 CS1 程序集可编译并能生成安装包，不能替代真实两台/多台 CS1 的游戏运行证据。

## V3 设计文档

| 内容 | 文档 |
| --- | --- |
| Forge × CSM-CQU 融合总架构 | [ARCHITECTURE-V3](docs/ARCHITECTURE-V3.zh-CN.md) |
| CSM-CQU 机制迁移审计 | [CSM-CQU-MIGRATION](docs/CSM-CQU-MIGRATION.zh-CN.md) |
| 真实 CS1 Runtime 接入 | [CS1-RUNTIME-INTEGRATION](docs/CS1-RUNTIME-INTEGRATION.zh-CN.md) |
| Host Authority / 结果复制 | [AUTHORITY-REPLICATION](docs/AUTHORITY-REPLICATION.zh-CN.md) |
| V3 Gate 与施工路线 | [IMPLEMENTATION-ROADMAP-V3](docs/IMPLEMENTATION-ROADMAP-V3.zh-CN.md) |
| 并行加入安全不变量 | [并行加入](docs/spec/PARALLEL-JOIN.zh-CN.md) |
| 协议 v2 | [协议 v2](docs/spec/PROTOCOL-V2.zh-CN.md) |
| 运行时复制 | [运行时复制](docs/spec/RUNTIME-REPLICATION.zh-CN.md) |
| 故障与恢复 | [鲁棒性规范](docs/spec/ROBUSTNESS.zh-CN.md) |
| 实机/长跑验收 | [验收规范](docs/spec/ACCEPTANCE.zh-CN.md) |
| Candidate E3/E4 证据记录 | [测试记录模板](docs/E3-E4-TEST-RECORD-TEMPLATE.zh-CN.md) |

旧 M0/v1 文档仅用于历史和兼容测试，不代表当前 V3 主线。

## CI 门禁

当前 PR/分支会验证：

- Linux / Windows `net35 + net8` Core/Protocol/Transport/Checkpoint build；
- net8 production-kernel tests；
- Linux 上实际 net35/Mono tests；
- 使用隔离 CS1 reference assemblies 编译 `Forge.Runtime.Cities1`；
- 自动生成不含游戏 DLL / Harmony 实现 DLL 的可安装 ZIP；
- Authority Domain ID 唯一；
- Host / Client Domain registry 集合和顺序对称；
- Client rebaseline 与完整 Stop 的 Domain cleanup 完整；
- projection audit 保持 diagnostic-only；
- 尚未进入 Authority 的 unsupported write 必须继续 fail closed。

纯内核本地测试：

```powershell
dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release --nologo
dotnet run --project tests/Forge.Tests/Forge.Tests.csproj -c Release -f net8.0 --no-build
python scripts/check_docs.py
python -m unittest discover -s tests/docs -p "test_*.py"
```

## 构建真实 CS1 Runtime

仓库不会提交 Cities: Skylines、Unity 或 Steam 的游戏程序集。使用本机合法安装的 CS1 `Cities_Data/Managed`：

```powershell
pwsh ./scripts/build-runtime.ps1 `
  -CitiesManagedPath "D:\SteamLibrary\steamapps\common\Cities_Skylines\Cities_Data\Managed"
```

脚本会：

1. 检查 `ICities.dll`、`Assembly-CSharp.dll`、`ColossalManaged.dll`、`UnityEngine.dll`；
2. 编译 net35 `Forge.Runtime.Cities1`；
3. 拒绝把游戏自有 DLL 或 Harmony 实现 DLL 打进包；
4. 打包 Forge DLL、LiteNetLib 与 CitiesHarmony API；
5. 生成 `SHA256SUMS.txt` 与 `CSM-Forge-runtime.zip`。

GitHub Actions 的 `runtime-package-windows` 还会附加 `BUILD_INFO.json`，Artifact 名包含精确 commit SHA。

## 安装

Windows Mod 目录：

```text
%LOCALAPPDATA%\Colossal Order\Cities_Skylines\Addons\Mods\CSM-Forge
```

把 ZIP 中 `CSM-Forge` 文件夹的内容放入上述目录，使 `CSM.Forge.Runtime.Cities1.dll` 直接位于该目录。另行安装并启用 **CitiesHarmony**，然后重启游戏。

## 第一次双机 Candidate 测试

1. Host 和 Client 都备份测试城市；
2. 两台机器安装**同一个 commit SHA** 的 Forge Candidate Artifact，并启用 CitiesHarmony；
3. 两边在游戏启动前运行安装目录中的 `VERIFY-INSTALL.ps1`，确认 `source_commit` 与 `manifest_sha256` 完全一致；
4. Host 进入要共享的城市，按 Esc，点击 **FORGE 多人联机**，通过预检后创建房间；
5. Host 在会话页点击 **复制直连邀请并打开 Steam**。Forge 会复制 LAN 直连邀请并打开好友列表，由房主粘贴发送；当前不发布 Steam `connect` Rich Presence，好友不能直接点击“加入游戏”，也不提供 NAT 穿透或中继；
6. Client 保持在主菜单，点击 **FORGE 联机**、粘贴邀请并加入；Client 不需要预先加载占位城市；
7. 等 Client 状态明确进入 `ClientLive` 后再操作；
8. 按安装包中的 `E3-E4-TEST-RECORD.md` 执行矩阵；
9. 如果出现失败或 `[CSM-Forge] diagnostic-only projection drift`，先在游戏内点击“写入诊断日志”，再在每台机器运行 `COLLECT-DIAGNOSTICS.ps1`。

## 从 code-complete candidate 到正式发行仍需完成

- 至少 2–4 台真实 CS1 的长时间联机；
- 重叠热加入、慢 Client、取消、掉线重连；
- 大城市 Snapshot 与 Journal 压力；
- 弱网与长时间 projection audit；
- Terrain Authority 的真实双机/多机、hot-join 与 collateral 验证；
- Event / Campus / DLC 专用状态；
- 更完整的 Citizen / Vehicle / Path 自然模拟 closure；
- Host-only Mod 能力的真实分类与实测；
- 最终公网认证 Transport。

因此当前状态是 **1.0 code-complete candidate / gameplay unverified**，不是 Release 或 RC。代码与安装体验已经形成候选闭环；下一道硬门禁是把安装包内 E3/E4 矩阵绑定到真实 Host/Client 证据，并据此修复实际游戏问题。
