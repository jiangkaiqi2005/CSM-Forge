# CSM-Forge V3 融合总架构

状态：`v3-integration-proposal`。
Forge 基线：`db3d551ebdf381ed18f7709651e9355655110795`。
CSM-CQU 研究基线：`fix/nonblocking-parallel-join@8668baeb361ba9cf891dc89796cb26d4b6e26d0d`。
官方 CSM 对照基线：`CitiesSkylinesMultiplayer/CSM@45c4ea3a402422e00e09c7f0840ae59eac2ed58c`。

本文件定义 CSM-Forge 下一阶段如何吸收 CSM-CQU 已验证的工程机制并接入真实 Cities: Skylines 1。它不是已实现功能声明。现有 `docs/TECHNICAL-SPEC.zh-CN.md` 与 `docs/spec/` 中关于 Host 单写者、结果复制、固定屏障、恢复边界、资源上限和真实验收的安全不变量继续有效；若本提案最终合入主线，后续必须同步更新总纲、`spec-index.json` 与旧路线文件，避免双重权威。

## 文档关系

- [CSM-CQU 机制迁移审计](CSM-CQU-MIGRATION.zh-CN.md)：决定旧成果如何进入 Forge；
- [CS1 Runtime 真实接入](CS1-RUNTIME-INTEGRATION.zh-CN.md)：定义游戏程序集、线程、Hook 与适配器边界；
- [Host Authority 渐进迁移](AUTHORITY-REPLICATION.zh-CN.md)：定义从真实操作到 Intent/AuthorityBatch/Projection 的迁移方法；
- [V3 实施路线](IMPLEMENTATION-ROADMAP-V3.zh-CN.md)：定义 Gate、依赖和实机退出条件；
- 现有 `docs/spec/*`：继续定义协议、并行加入、鲁棒性和验收的安全不变量。

## 1. 架构决策

未来产品主干为 **CSM-Forge**。CSM-CQU 不再作为未来架构的宿主，但也不被废弃：它是经过真实 CS1 接入和稳定化改造的 **机制供体、运行时知识库、回归对照和可玩参考实现**。

迁移遵守三个原则：

1. **吸收改良机制，不继承旧同步假设。**
   CSM-CQU 的异常安全、会话身份、存档传输、兼容采集、诊断、热加入工程经验可以进入 Forge；“收到远端 Command 后在每个副本重新执行同一游戏逻辑”不能成为 Forge 的最终复制模型。
2. **真实游戏依赖只进入 Runtime 边界。**
   `ICities`、`Assembly-CSharp`、`ColossalManaged`、Unity、Harmony 和游戏私有实现只允许出现在 `Forge.Runtime.Cities1` 及其紧邻适配层。Core、Sessions、Replication、Protocol 不引用游戏程序集。
3. **证据优先于迁移量。**
   CQU 中存在的代码不因“已经能跑”自动获得迁入资格。每个机制必须说明原版缺陷、迁移语义、Forge 所有者、回退策略和验证证据。

## 2. 系统边界

```text
Cities: Skylines 1
        │
        │ ICities / Assembly-CSharp / ColossalManaged / Harmony
        ▼
┌─────────────────────────────────────────────┐
│ Forge.Runtime.Cities1                       │
│ 生命周期、线程、Hook、Authority Adapter、   │
│ Replica Projection、Checkpoint、CS1 采集器  │
└──────────────────┬──────────────────────────┘
                   │ 纯 DTO / 领域契约
                   ▼
┌─────────────────────────────────────────────┐
│ Forge.Replication / Forge.Sessions          │
│ AuthorityBatch、Revision、Applied、Join、    │
│ Barrier、Activation、Recovery               │
└──────────────┬─────────────────┬────────────┘
               │                 │
               ▼                 ▼
      Forge.Checkpoints      Forge.Compatibility
               │                 │
               └────────┬────────┘
                        ▼
                 Forge.Protocol
                        │
                        ▼
                 Forge.Transport.*
```

Transport 负责可信连接、交付、拥塞和统计；Protocol 负责有界消息；Sessions 负责成员/加入/恢复；Replication 负责世界结果；Runtime 负责真实 CS1。任何一层不得用“网络已收到”代替“游戏世界已应用”。

## 3. 模块职责

| 模块 | 主要职责 | 可吸收的 CSM-CQU 经验 | 禁止事项 |
| --- | --- | --- | --- |
| `Forge.Core` | 纯身份、结果、错误类型、不可变量 | Session/Epoch/序号语义中的约束思想 | 不引用 CQU Command 类型、Unity、Socket |
| `Forge.Protocol` | v2 Bootstrap、消息帧、限额、Schema | CQU 对畸形输入、身份绑定、分片边界的回归案例 | 不做游戏反射分派 |
| `Forge.Sessions` | Membership、JoinContext、Barrier、Activation、恢复 | CQU 并行加入的独立玩家生命周期、取消经验 | 不持有 CS1 对象 |
| `Forge.Replication` | AuthorityBatch、CommitRevision、根、Apply 状态 | CQU Transaction 的批次完整性教训、Fingerprint 诊断经验 | 不回放远端工具 Command 作为权威结果 |
| `Forge.Checkpoints` | 检查点、不可变快照、日志租约、流式传输 | `WorldTransferSender` 的背压/取消/超时、SaveHelpers 经验 | 不把共享快照实现为共享可变 Stream |
| `Forge.Compatibility` | 支持目录、组件指纹、能力规则 | `ManifestCollector/Validator/Policy` 的真实采集经验 | 不相信客户端自报 HostOnly/ClientOnly |
| `Forge.Runtime.Cities1` | 生命周期、线程、Harmony、真实 Manager 适配 | CQU 的 Hook、CaptureState、ReplayHelper、Handler/Injection 知识 | 不拥有房间协议语义 |
| `Forge.UI` | 房间、进度、回执、恢复、诊断 | CQU 现有 Panel 与中文文案经验 | 不先本地提交持久修改再等 Host 修正 |
| `Forge.Transport.*` | 连接、lane、背压、NAT/中继 | CQU LiteNetLib/GS 可作为早期适配证据 | 不分配世界 Revision |

## 4. 从 CQU 吸收机制的四种方式

### 4.1 直接吸收

适用于与旧同步模型无关、且职责边界清楚的机制，例如：

- Harmony 调用级异常清理模式；
- `ReplayHelper` 一类“保留原异常并可靠释放作用域”的结构；
- 有界诊断 Ring/Telemetry 的实现思路；
- Mod/DLC/Asset 的真实身份采集；
- 存档发送中的流式读取、取消、超时和可靠队列背压。

直接吸收仍要重命名到 Forge 语义，并保留必要的 MIT 版权许可信息。

### 4.2 语义改造后吸收

适用于机制正确但数据模型属于旧 CSM 的部分：

- CQU Session/SenderEpoch/Sequence → Forge World/Epoch/Member/Connection/Operation；
- CQU Transaction → Forge AuthorityBatch 完整性；
- CQU Catch-up delta log → Forge CommitStore；
- CQU Fingerprint → Forge DomainRoot/首次分叉诊断；
- CQU HostParameter → Forge 第一批 Host-authoritative 领域适配。

### 4.3 只吸收运行时知识

适用于旧代码能够告诉我们“CS1 怎么做”，但不能复用其网络语义：

- `BuildingHandler/NetHandler/EconomyHandler/TransportHandler` 等 Harmony Hook；
- 各种 Handler 中对 `NetManager`、`BuildingManager`、Tool、Prefab、ID 和副作用的调用方式；
- UI、加载、保存、Steam/GS 生命周期的实际坑。

这些内容应拆成 Intent Capture、Host Authority Adapter 和 Client Projection Adapter，而不是原样注册为远端 Command Handler。

### 4.4 明确丢弃

以下机制不得进入 Forge 核心：

- `CommandReceiver` 式远端反序列化后按类型直接执行游戏 Handler；
- Server 将客户端游戏 Command 原样 relay 给其他副本；
- 以“各副本运行同一工具逻辑应得到同一世界”为一致性基础；
- 单个全局 `WorldLoadingPlayer`、`isJoining`、共享可变下载游标；
- 用网络 ACK、下载完成或 UI 状态代表世界已应用；
- 因某个 Client 失败而默认暂停/重置全房。

## 5. 真实 CS1 接入策略

接入按证据等级从稳定到侵入排列：

1. **A：ICities 正式 API**：Mod 生命周期、加载、线程、序列化、可用经理接口；
2. **B：游戏程序集公开表面**：`Assembly-CSharp.dll`、`ColossalManaged.dll` 中可直接访问的真实 Manager/Prefab/UI；
3. **C：Harmony Patch**：官方 API 没有的操作捕获、写屏障、结果安装入口；
4. **D：Reflection/private surface**：仅在 A/B/C 无法完成且已锁定目标游戏 build 时使用，必须有签名探针和失败关闭。

优先使用 A/B；Harmony 是可审核的扩展层，不是默认万能入口；Reflection 是最后手段。Runtime 必须记录目标游戏 build、方法签名和对应验收证据。

## 6. 权威路径

玩家操作不再直接改变 Client 的持久世界：

```text
Client Input
   → IntentCapture
   → Intent
   → Host Validation / Authority Adapter
   → AuthorityBatch(Revision R)
   → Host publish
   → Replica validate
   → ProjectionAdapter
   → CS1 world publish
   → AppliedAck(R)
```

Host 本地玩家也走同一逻辑入口。自然模拟产生的持久变化最终也进入 `AuthorityBatch`，但按领域逐步覆盖，不允许用“CQU 以前能重放”作为跳过闭包证明的理由。

## 7. 迁移期间的兼容边界

Forge 不需要与 CSM/CSM-CQU 协议互联。旧仓库只用于：

- 对照原版缺陷和 CQU 修复；
- 提取真实游戏入口；
- 构造回归用例；
- 对同一操作进行 A/B 运行证据比较。

迁移阶段允许同一功能同时存在“CQU 参考实现”和“Forge 新实现”，但不能在同一 Forge 房间内混用两套世界权威。

## 8. 第一批垂直切片

第一批不选道路。按状态闭包从小到大：

1. 生命周期/线程探针；
2. 只读 Economy/Demand 状态；
3. Forge 存档元数据；
4. Manifest/Compatibility 真实采集；
5. 单个原版最终值参数（税率、预算或需求中的一个）；
6. Snapshot + Authority log + Barrier/Activation；
7. Building 简单最终状态；
8. Road/Net；
9. 自然模拟闭包。

道路和车辆/居民放在后期，因为 ID、跨域引用、随机/异步副作用远大于参数类状态。

## 9. 失败与回退

- Runtime Patch 部分安装失败：撤销本 Mod 已安装 Patch，禁止开房；
- Host Authority Adapter 在修改真实世界后结果不确定：Fence 权威写入，从可信检查点恢复；
- Client Projection 失败：只隔离该 Client，撤销编辑资格并完整恢复；
- Compatibility 采集失败：默认拒绝进入可写房间；
- 未覆盖领域发生持久写入：记录覆盖缺口，关闭对应能力，不静默继续；
- 迁移后的新实现无法达到 CQU 既有行为：保留 CQU 作为诊断对照，不把旧 Command relay 临时塞回 Forge。

## 10. 文档与实现状态

本 V3 文件只决定 **融合方向和模块所有权**。现有 v2 Protocol、Parallel Join、Robustness、Acceptance 的安全语义继续约束实现。任何“已迁移”“已支持”“已兼容”必须有对应代码、自动化和真实 CS1 证据；文档完成本身不计为功能完成。
