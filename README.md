# CSM-Forge

面向 **Cities: Skylines 1** 的 Host-authoritative 多人共同经营系统。Forge 是新的生产主线：吸收 CSM-CQU 已验证的异常安全、热加入、兼容采集、世界传输和真实 CS1 接入经验，但不把旧的 Command Relay/Replay 当作最终同步模型。

当前 `fix/forge-cqu-integration-docs` 已推进到 **V3 minimum-playable Alpha 候选**。它不是玩家正式发行版，但已经具备真实 CS1 Runtime、自动可安装 ZIP、Host/Join/热加入/恢复链，以及一组可共同建设城市的 Host-authoritative 领域。

## 当前最小可玩 Alpha 范围

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
- Client projection audit：低频检查真实游戏投影与最后 Host committed root；当前严格 **diagnostic-only**，发现漂移只记录日志，不自动踢人或重同步。

### Alpha 安全边界

Tree / Prop / Terrain 还没有完成 V3 Authority 闭包。为了避免玩家误操作后出现静默不同步，Forge 在多人阶段对以下持久写操作 **fail-closed**：

- Tree Create / Move / Release；
- Prop Create / Move / Release；
- Terrain brush。

单机状态不受这些限制；Forge `ApplyScope` 内由已支持权威操作触发的底层调用仍可通过。

Event、Campus 和其他 DLC 专用系统尚未声明完整支持。Citizen / Vehicle / Pathfinding 仍主要作为本地动态表现层运行；它们不能直接绕过现有 Building / Net / Zone / Economy 配置写屏障。是否存在长期动态漂移，需要 E4 真机长跑和 projection audit 日志继续验证。

## 重要限制

- 当前 Transport 是 **LiteNetLib + 临时 room key** 的开发/LAN 适配器，不是最终公网认证方案；
- 真实游戏多机长跑、弱网、复杂 Mod/DLC 组合仍未完成 E4 验收；
- 当前 Alpha 只应用于备份城市；
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
- minimum-playable Alpha 的 unsupported-write safety barrier 保持存在。

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

## 第一次双机 Alpha 测试

1. Host 和 Client 都备份测试城市；
2. 两台机器安装**同一个 commit SHA** 的 Forge Alpha Artifact，并启用 CitiesHarmony；
3. 两边都先进入一个城市，打开 Mod 设置页，确认 `patches=ready`；
4. Host 设置 UDP 端口与临时 room key，点击 **Host 当前存档**；
5. Client 填 Host IPv4、同一端口与 room key，点击 **Join Host 快照**；
6. 等 Client 状态明确进入 `ClientLive` 后再操作；
7. 依次测试：暂停/速度 → 一条道路 → 一个建筑 → zoning → district brush/policy → tax/budget → area unlock → 一条 transport line；
8. 当前 Alpha 不使用 Tree / Prop / Terrain 工具；这些写操作会被故意阻断；
9. 再让第二名 Client 加入或让第一名 Client 重连，确认 Host 与其他玩家不被阻塞；
10. 如果日志出现 `[CSM-Forge] diagnostic-only projection drift`，保留游戏日志并记录出现前的最后一个玩家操作。

## 达到“稳定替代”前仍需完成

- 至少 2–4 台真实 CS1 的长时间联机；
- 重叠热加入、慢 Client、取消、掉线重连；
- 大城市 Snapshot 与 Journal 压力；
- 弱网与长时间 projection audit；
- Tree / Prop / Terrain Authority；
- Event / Campus / DLC 专用状态；
- 更完整的 Citizen / Vehicle / Path 自然模拟 closure；
- Host-only Mod 能力的真实分类与实测；
- 最终公网认证 Transport。

因此当前目标是 **minimum-playable Alpha**，不是 Release。Alpha 的价值是开始获得真实多人证据，并据此继续关闭剩余模拟漂移和功能缺口。
