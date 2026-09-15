# 资料来源、代码事实与证据边界

本轮核对日期：2026-09-15。文档中的产品目标、协议、租约/屏障算法和预算属于 Forge 的工程设计；来源仅支撑所注明的事实，不等于这些资料已经证明 Forge 在 CS1 中可行。

## S-BASE 当前 Forge 代码

来源：[HostSession.cs 固定基线](https://github.com/jiangkaiqi2005/CSM-Forge/blob/225fd25151fb6ec0a8dae3e95ca2945fb4270462/src/Forge.Core/HostSession.cs)、[ReplicaSession.cs](https://github.com/jiangkaiqi2005/CSM-Forge/blob/225fd25151fb6ec0a8dae3e95ca2945fb4270462/src/Forge.Core/ReplicaSession.cs)、[旧路线](https://github.com/jiangkaiqi2005/CSM-Forge/blob/225fd25151fb6ec0a8dae3e95ca2945fb4270462/docs/ROADMAP.zh-CN.md)。

已确认 CompleteJoin 要求客户端确认 Revision 等于主机当前 Revision，旧路线的首个游戏切片是暂停城市双人编辑。前者在实时变化世界中可能形成移动目标，是本次固定历史屏障方案要修改的具体入口。后者被本次产品路线修订为内部关卡。这里是代码/文档事实加工程推论，未声称已实机复现特定错误。

## S-CSM 原版与旧项目研究

来源：[CSM 基线](https://github.com/CitiesSkylinesMultiplayer/CSM/tree/45c4ea3a402422e00e09c7f0840ae59eac2ed58c)、[本仓库研究报告](../research/architecture-review.zh-CN.md)。本会话已读取关键接收、批次、ID 回放、帧控制和运行时文件；CQU 使用 `c713d931f0ed87725ef320a37d83f115803c535e` 的 dev 作为只读参考。

这些源码可帮助识别 API 与旧方法依赖，但不能证明目标 CS1 版本的全部线程和持久状态闭包。本轮不复制其代码、不修改旧仓库、不重新宣称旧实机测试已执行。

## S-GNS Valve GameNetworkingSockets

来源：[官方 README](https://github.com/ValveSoftware/GameNetworkingSockets/blob/master/README.md)，本轮读取 blob `29425edb8af83629f51529a47139002d906c7ad5`。

支持事实：提供可靠/不可靠消息、加密、分片重传、lane、网络故障模拟、自定义信令和 C 接口；不负责游戏实体序列化和压缩。独立开源库和 Steam 平台身份/信令/SDR 不是同一个可用权限集合。Forge 的 net35 ABI、证书信任和部署打包仍需独立验证。

## S-LANES Valve 通道调度

来源：[ISteamNetworkingSockets，ConfigureConnectionLanes](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets#ConfigureConnectionLanes)。

支持事实：同 lane 的可靠消息才有强顺序保证；不同 lane 的消息可能乱序。优先级优先于权重，权重主要在同优先级 lane 间分配带宽。由此得出的 Forge 设计是把激活 marker 放在状态流中，并在房间级增加公平性和配额，而不是假设低优先级快照总会获得带宽。

## S-STEAM Valve 回调与连接生命周期

来源：[ISteamNetworkingSockets，AcceptConnection/SetConnectionUserData](https://partner.steamgames.com/doc/api/ISteamNetworkingSockets)。

支持事实：加载期间长期不处理连接回调会导致连接超时；回调中的 userdata 可能反映入队时的状态，存在晚到上下文风险。Forge 因此要求持续 IO 轮询以及连接/加入/加载代次验证，但不推论这些回调能代替游戏线程推进。

## S-SDR Valve 中继服务

来源：[Steam Datagram Relay](https://partner.steamgames.com/doc/features/multiplayer/steamdatagramrelay)、[独立开源库说明](https://github.com/ValveSoftware/GameNetworkingSockets/blob/master/README.md)。

支持事实：Steam 平台网络和授权/接入条件与开源库本身不同。不能因为游戏在 Steam 上发行，就假定一个第三方 Mod 可以无条件使用该游戏身份服务或 SDR。Forge 的房间、信令和中继部署是实际工程工作，不是 README 中一句“自动穿透”。

## S-HARMONY 运行时补丁

来源：[Harmony 2.x Finalizer 文档](https://harmony.pardeike.net/articles/patching-finalizer.html)、[Execution Flow](https://harmony.pardeike.net/articles/execution.html)、[CitiesHarmony 官方 README](https://github.com/boformer/CitiesHarmony/blob/master/README.md)。

支持事实：Finalizer 用于异常观察与清理；常规 Postfix 不是异常路径清理的完整替代。CitiesHarmony 对 CS1 Mono 有专门适配，IUserMod 与 HarmonyLib 依赖加载需分离。本规范不根据其他进程上的补丁测试推断游戏内动态 patch 已成功，也不把清理成功称为游戏事务回滚。

## S-FACTORIO Wube 的异步保存经验

来源：Wube Software，2024-04-26，[Friday Facts #408 — Statistics improvements, Linux adventures](https://www.factorio.com/blog/post/fff-408)，Asynchronous saving 部分。

支持事实：文中功能使用 fork，在 macOS/Linux 上让子进程保存，且讨论内存与平台限制。它不是 Windows CS1 的现成方案。本文仅借其说明“异步保存必须核实具体机制”，不复制其实现或宣称 Forge 与 Factorio 使用相同联机架构。

## S-LOCKSTEP 确定性条件

来源：Glenn Fiedler，[Deterministic Lockstep](https://gafferongames.com/post/deterministic_lockstep/)。

支持事实：lockstep 要求相同初态和输入产生精确一致的模拟结果。Forge 因此不把未证明的整个 CS1 确定性当基础；这并不是证明 CS1 永远不可能确定化，而是本项目选择不同风险结构。

## 本轮没有取得的证据

没有真实 CS1 双机/四机测试，没有原生 GNS 的 Forge 绑定运行，没有游戏内模拟隔离证明，没有 `.crp` 一致切面或断电恢复验证，没有性能预算实测，没有已经支持特定大型 Mod 的结论。v2 Bootstrap 的精确 codec 与独立 GNS 认证 API 必须在工作包实施时冻结并验证。

本轮文档检查和旧内核 CI 只能说明文档引用/依赖关系与既有代码回归的状态，不能升级这些未测项。
