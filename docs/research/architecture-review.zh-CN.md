# CSM 与 CSM-CQU 架构研究

## 研究基线和证据边界

本次以 GitHub 实际可读源码为准：原版 CSM 的 `master` 固定在 `45c4ea3a402422e00e09c7f0840ae59eac2ed58c`；CSM-CQU 的 `dev` 固定在 `c713d931f0ed87725ef320a37d83f115803c535e`。CQU 的 `main` 是另一个提交 `d6c11532e728a259a5d5dcc38f268361305107fa`，以下不把 dev 的改进自动归于 main。

核对范围包括命令接收、事务批次、ID 分配回放、模拟帧控制、加载生命周期、项目运行时、CQU 会话一致性规范和相关生产入口。它是关键路径架构审查，不是逐文件安全审计。CQU 文档中的旧测试数字是原项目自己的记录，本次没有重新执行旧项目测试，也没有进行旧模组的实机复现。

CSM 此基线含 2026 年的 RaceDay 支持提交，因此不能根据旧搜索摘要断言项目早已停止维护。问题判断依据是机制，不是仓库年龄。[1]

## 旧项目究竟如何联机

原版总体链路为：Harmony 拦截游戏操作，采集命令及创建过程中的本地对象 ID，序列化传输；接收方按命令类型选择处理器，再在本地调用游戏逻辑重放。主机兼有连接管理、转发和部分领域控制，不是一个完全无权威的纯 P2P 系统；但它也不是统一的“客户端请求、主机裁决、所有副本只应用裁决结果”系统。[2][3][4]

`ArrayHandler` 对若干 Array16/Array32 实例的分配进行采集和重放。命令携带的不是完整实体结果，而包含执行途中产生的分配序列。远端运行相应逻辑时，再依照序列替换分配结果。`TickLoopHandler` 则修改帧调度/丢帧路径，帮助控制各端推进速度。这些机制各有用途，但相同推进速度、相同 ID 或相同工具参数都不能单独证明整个城市状态一致。[4][5]

CQU 已在此基础上投入了实质性改进：连接身份与 SenderEpoch 校验、固定握手子模型、载入阶段流量门槛、有限批次/超时、异常安全守卫、操作观测及兼容清单。其 receiver 明确区分 transport-connected 和 world-connected，避免加载中的旧城市命令进入主机。这些设计值得保留为原则，不应被笼统描述为“什么都没修”。[7][8][9]

## 最关键的问题与根因

### 1. 同步的是调用过程，未必是唯一裁决后的世界结果

一个工具调用可能修改道路节点、道路段、分区块、建筑关联、费用和第三方 Mod 状态。两端执行相同入口，并不意味着全部前置状态、嵌套调用、随机值消耗、分配顺序和副作用一致。漏掉任意持久副作用，就可能在以后操作中放大差异。

这里不能武断地说每次不同步都是浮点或寻路线程造成的：本次没有这样的实机因果证据。能够从源码直接确认的是，旧回放机制依赖跨端调用过程相容；这些条件一旦被游戏更新或其他补丁改变，传输可靠也不能恢复这项假设。[2][4]

**新项目决定：** 客户端只发送意图；主机验证后执行，并发布规范化结果及提交序号。结果应用不是再次运行整个玩家工具。每个适配器必须声明其修改的状态闭包，未覆盖的领域不能开放编辑。

### 2. ID 一致性绑定了不稳定的分配轨迹

原版 `ArrayHandler` 在“需要的 ID 多于采集数量”时会记录警告并允许原始分配路径继续；采集 ID 未完全消费也只是警告。这样的降级会把结构性回放错误变成后续难以定位的状态差异。CQU 的规范和修复已经改善所有权、异常收尾及 ID 耗尽处理；不能把原版这一具体行为无条件归于其最新版本。[4][9]

更深的问题是：即便给每个标志加上 finally，只要对象身份还依赖跨端完全相同的嵌套分配轨迹，游戏更新和工具 Mod 仍会施加持续兼容压力。

**新项目决定：** 使用逻辑实体身份和代次，并由领域适配器映射到本地游戏句柄。ID 生命周期、引用图、删除和重用纳入结果 Schema。实际 CS1 引用闭包能否完整映射，是道路适配器必须验证的门槛，不是已经解决的功能。

### 3. 批次不是游戏世界的原子事务

原版事务列表收到 finish 后顺序执行，逐项异常会被记录，随后继续并清空。CQU 已加入条数、批次数、超时、索引和转发一致性检查，并在执行失败时停止后续成员。但是，一批操作中前面的成员已改变游戏时，后面的失败不会自动撤销前面的状态。[3][8]

这不是“再补一个 try/catch”能解决的事。连接上完整收到 N 条命令，只证明一个完整批次到达，不证明 N 个世界副作用共同提交。

**新项目决定：** 引擎明确区分“拒绝且未修改”和“执行状态不确定”。引用世界可先构造候选状态再交换；不具备原子发布能力的游戏适配器发生部分失败时必须隔离。主机失败封锁房间写入和新快照；客户端失败禁止继续应用增量，要求可信检查点恢复。不能用清除 Ignore 标志代替恢复城市。

### 4. 帧协调不能替代确定性证明

确定性 lockstep 的前提是相同初态与输入得到精确相同状态。浮点计算、执行顺序及外部输入等因素都需要控制。Fiedler 的原始技术文章明确区分“输入一样”和“逐位相同的模拟结果”。CSM 的帧调度补丁自身并不是这样的证明。[5][10]

在一个包含闭源模拟引擎、异步工作和开放 Mod 生态的现成游戏上，先承诺完整确定性，再逐个修复例外，风险和维护成本都很高。这是工程取舍，不是声称 CS1 确定化在理论上不可能。

**新项目决定：** 不把整个 CS1 确定性作为协议正确性前提。由主机运行权威模拟；复制状态机和自有规范化编解码要求确定性；客户端表现层插值可以近似，但不能反向改变经济、道路或存档。

### 5. 看见差异不等于具有修复策略

CQU 规范已经正确指出，不同 tick 或跨多个变化窗口取得的哈希可能不可比较；首次可疑差异不应自动触发无限重同步。它还明确把低频指纹、候选 Mod 支持和真实双机验收分开。这些是有效的工程改进。[9]

然而，在保留分布式操作回放的前提下，加日志和指纹主要改善发现问题、缩小范围，不能自动建立唯一执行顺序、完整覆盖所有副作用或回滚既有错误。

**新项目决定：** 比较相同 World/Epoch/Revision/Schema 下的规范化状态。缺失提交采用有限日志补齐；根不匹配、部分应用或超出保留窗口使用快照；主机自身污染不能用其当前状态创建“修复快照”。失败分类、恢复状态和写权限是内核行为，而不只是一条日志。

### 6. 兼容性不是名称列表相等

相同显示名称可能对应不同二进制、配置或资产；两端安装同一未知模拟 Mod，也不证明它已被复制系统覆盖。CQU 已引入能力、身份和版本等更细判断，这优于仅比较名称。但任何兼容分类最终都需要操作级证据。[9]

**新项目决定：** 发布维护者控制的支持目录，而不是让对端自报“ClientOnly”。要求精确的游戏/Schema/必要组件指纹；可选本地组件只能由已审核目录放行；未知组件默认拒绝。当前实现的是该策略算法，尚未实现游戏进程中的完整组件采集，也没有宣布 TM:PE、81 Tiles、Move It 或 Network Multitool 已兼容。

## 技术路线比较

| 路线 | 优点 | 主要代价 | 决策 |
| --- | --- | --- | --- |
| 全游戏确定性 lockstep | 带宽随输入量而非实体量增长 | 需要证明并维护整个模拟与 Mod 的确定性；慢端可能影响所有人 | 不作为基础 |
| 各端继续模拟，加输入回放与定期纠正 | 容易沿旧逻辑扩展，视觉更新成本低 | 难证明持久状态不持续漂移；纠正可能掩盖遗漏 | 舍弃为持久世界模型 |
| 每次操作都重发存档 | 状态来源直观 | 保存、传输、载入延迟过大 | 仅用于检查点和恢复 |
| 主机唯一权威，结果复制、快照与表现流分离 | 一致性边界明确，失败可隔离 | 游戏适配工作最多；必须证明客户端模拟隔离和完整状态闭包 | 采用，分阶段验证 |

Fiedler 的 State Synchronization 描述的是双方继续模拟并发送状态的近似方案，不能把它误称为本项目整套设计。本项目借鉴状态/表现分离、带宽优先级和显式基线等思想，不把近似外推用于持久城市事实。[11]

## 网络、运行时和工具选择

现有 CSM 的游戏程序集目标为 .NET Framework 3.5。Forge 因此保留 net35 生产编译边界，使用 net8 开发测试，但不把现代 .NET 可运行等同于游戏自带 Mono 可运行。Microsoft 的参考程序集包可用于编译阶段验证目标 API；标准 Mono 的测试仍不是游戏进程验收。[6][14]

首选正式传输适配器是独立 GameNetworkingSockets，通过窄原生接口连接 net35。其官方说明列出可靠/不可靠消息、加密、拥塞与多 lane 调度、ICE 及自定义信令，适合将控制、批量存档和表现更新分开。它不负责游戏实体序列化和状态一致性，也不意味着一个第三方 Mod 自动拥有 Steam 身份、信令或 SDR 服务权限。原生 ABI、依赖打包和 CS1 进程加载必须先通过验证；当前未实现这一适配器。[12][13]

未来 Harmony 接入按 CitiesHarmony.API 的加载生命周期组织。其官方项目明确提醒不要让 IUserMod 直接依赖 HarmonyLib，并区分 API DLL 与由共享 Harmony Mod 提供的 Harmony DLL。Forge 当前探针没有安装任何补丁，也没有借用不匹配运行时的动态 patch 测试冒充成功。[15]

## 资料索引

1. CitiesSkylinesMultiplayer，CSM 基线提交：[45c4ea3](https://github.com/CitiesSkylinesMultiplayer/CSM/commit/45c4ea3a402422e00e09c7f0840ae59eac2ed58c)。
2. CSM，[CommandReceiver.cs](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/csm/Commands/CommandReceiver.cs)。
3. CSM，[TransactionHandler.cs](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/csm/Commands/TransactionHandler.cs)。
4. CSM，[ArrayHandler.cs](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/basegame/Injections/ArrayHandler.cs)。
5. CSM，[TickLoopHandler.cs](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/csm/Injections/TickLoopHandler.cs)。
6. CSM，[CSM.csproj](https://github.com/CitiesSkylinesMultiplayer/CSM/blob/45c4ea3a402422e00e09c7f0840ae59eac2ed58c/src/csm/CSM.csproj)。
7. CSM-CQU dev，[CommandReceiver.cs](https://github.com/Aetik-yue/CSM-CQU/blob/c713d931f0ed87725ef320a37d83f115803c535e/src/csm/Commands/CommandReceiver.cs)，私有仓库，需权限。
8. CSM-CQU dev，[TransactionHandler.cs](https://github.com/Aetik-yue/CSM-CQU/blob/c713d931f0ed87725ef320a37d83f115803c535e/src/csm/Commands/TransactionHandler.cs)，私有仓库，需权限。
9. CSM-CQU dev，[会话一致性 Spec](https://github.com/Aetik-yue/CSM-CQU/blob/c713d931f0ed87725ef320a37d83f115803c535e/docs/session-consistency/Spec.md)、[捕获覆盖与实机边界](https://github.com/Aetik-yue/CSM-CQU/blob/c713d931f0ed87725ef320a37d83f115803c535e/docs/session-consistency/capture-coverage.md)，私有仓库，需权限。
10. Glenn Fiedler，2014，[Deterministic Lockstep](https://gafferongames.com/post/deterministic_lockstep/)。
11. Glenn Fiedler，2015，[State Synchronization](https://gafferongames.com/post/state_synchronization/)。
12. Valve，[GameNetworkingSockets README](https://github.com/ValveSoftware/GameNetworkingSockets/blob/master/README.md)；本次读取 blob `29425edb8af83629f51529a47139002d906c7ad5`。
13. Valve，[Steam Datagram Relay 文档](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)。
14. Microsoft，[Reference assemblies](https://learn.microsoft.com/en-us/dotnet/framework/migration-guide/reference-assemblies)。
15. boformer，[CitiesHarmony README](https://github.com/boformer/CitiesHarmony/blob/master/README.md)；本次读取 blob `b1f3f969982c59c6fc91b9adb59baf6b2e673337`。

原版 CSM 仍是有效的游戏 API 与工具行为研究对象；CQU 是稳定化与诊断经验的重要参考。本项目舍弃的是跨端重演执行过程作为一致性基础的约束，不是否认旧项目的全部价值。
