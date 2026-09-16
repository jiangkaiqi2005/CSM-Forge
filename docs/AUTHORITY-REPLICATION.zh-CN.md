# Host Authority 与结果复制的渐进迁移设计

状态：`v3-integration-proposal`。本文件是 [现有运行时复制规范](spec/RUNTIME-REPLICATION.zh-CN.md) 的迁移实施桥梁，不降低其中“Client 不自主修改权威事实、自然模拟进入结果序列”的要求。

本文件说明如何把 CSM-CQU 已有的真实操作覆盖迁到 Forge 的 Host-authoritative 结果复制模型，而不是把旧 Command Replay 原样搬入。

## 1. 目标

最终不变量：

1. 持久世界只有 Host 提交；
2. Client 玩家操作先变成 Intent；
3. Host 真实执行并发布完整结果；
4. Client 只应用连续、完整、当前 Epoch 的 AuthorityBatch；
5. `received != committed != game-published != applied-acked`；
6. 自然模拟最终也进入同一 CommitRevision 序列；
7. 未覆盖的持久写入必须禁用或暴露为实验缺口。

迁移允许按领域逐步完成，但不允许在正式 Forge 房间中把“远端重放工具 Command”当成已完成的 AuthorityBatch。

## 2. 迁移阶段

### R0 — Shadow Observe

目的：在不改变现有单机行为的情况下验证 Hook 和状态闭包。

- 捕获玩家操作，但不联网、不拦截；
- 记录 before/after 规范化小领域状态；
- 对照 CQU Handler 识别副作用；
- 生成候选 result DTO；
- 不改变世界权威。

退出条件：能稳定描述一个操作的输入和最终结果，异常时作用域不泄漏。

### R1 — Final-value Authority Slice

适合税率、预算、需求、规则参数等小闭包。

- Client 捕获 Intent 并阻止本地正式提交；
- Host 校验并调用真实 CS1；
- Host 读取最终绝对值；
- 发布 AuthorityBatch；
- Client 设置最终绝对值，不重跑原玩家工具；
- 两端 DomainRoot 相同后 AppliedAck。

这是第一个真实多人 Gate。

### R2 — Entity Closure Authority

用于 Building/Net 等会创建、删除和引用实体的领域。

批次必须包含：

- entity stable identity / generation；
- create/update/delete；
- prefab stable key；
- 费用与资源副作用；
- 相关引用；
- 需要重建的派生索引/网格；
- before/after root；
- 失败恢复范围。

Client 不能仅调用原来的 `CreateBuilding/NetTool.CreateNode` 然后期望随机/ID/副作用一致。

### R3 — Natural Simulation Authority

覆盖经济、生长、居民、车辆、运输、路径等持续模拟。

Host 捕获安全边界内已经算出的最终事实，OriginKind=`Simulation`。Client 不重新运行这些权威决策，只维护经审核的表现与投影。

只有必要实时领域闭包全部完成后，才能进入实时多人 Alpha。

## 3. CQU Handler 的拆分规则

旧 CQU 往往把一项功能混在“捕获 Command + 远端 Handler 重演”两侧。迁移时必须拆成三个角色。

### 3.1 Capture

回答：**玩家想做什么？**

输入来自 UI/Tool/Harmony。输出是规范化 Intent，不包含“在客户端调用哪个私有方法”。

示例：

```text
SetTaxRateIntent {
  serviceKey,
  requestedRate,
  observedVersion
}
```

### 3.2 Authority

回答：**Host 实际接受后，世界最终变成什么？**

Host：

- 校验身份/权限/对象 generation/读集；
- 调用真实 CS1；
- 捕获所有声明闭包的最终事实；
- 生成 AuthorityBatch。

### 3.3 Projection

回答：**如何把已经决定的最终事实安装到 Client 的真实游戏对象？**

Projection 不是再做一次玩家决策；它是结果安装。若 CS1 没有“纯设置”API，需要专门 suppress 副作用或构造受控安装路径，并验证真实对象。

## 4. AuthorityBatch

逻辑批次至少包含：

```text
WorldId / Epoch
Revision
OriginKind
Origin operation (仅玩家 Intent 必须)
DomainSet
BeforeRoot
AfterRoot
Result payload / parts
SchemaVersion
```

一批跨域副作用若在任何可见中间状态都不合法，必须作为一个原子发布边界。网络分片不等于世界子事务；Client 完整收到并验证所有 part 后才能进入游戏投影。

Host Commit 不等待每个 Client 应用；Client Apply 失败也不能撤销已经发生的 Host 事实。

## 5. 操作身份与并发

玩家逻辑操作键：

```text
(WorldId, Epoch, MemberId, MemberGeneration, OperationCounter)
```

连接更换不自动产生新操作。相同键+相同摘要可稳定返回旧 Receipt；相同键+不同摘要拒绝。超时只表示结果未知，不等于 Rejected；在没有查到旧 Receipt/OperationLedger 结果前，不得把同一用户动作自动换成新 OperationCounter 重试。

不能要求每个 Intent 的 `ExpectedRevision == HostHead`，否则自然模拟会让正常输入持续过期。使用领域读集/对象 generation/permission version 判断冲突；必要时返回最新对象版本供玩家重新选择。

## 6. Client 写屏障

Client Persistent Write 必须满足以下之一：

1. 当前 `ApplyScope` 正在安装已验证 AuthorityBatch；
2. 当前 LoadGeneration 正在加载可信 Checkpoint；
3. 明确列出的非权威本地表现工作。

玩家正式工具提交、Mod 未审核的模拟写入、旧回调、远端消息反射分派均不具备写权限。

ApplyScope 至少绑定：

```text
BatchId
Revision
DomainSet
LoadGeneration
```

嵌套写入超出 DomainSet 视为领域覆盖缺口。

## 7. Root 与 CQU Fingerprint 的关系

CQU Fingerprint 很适合作为迁移诊断工具，但不能直接宣称为 Forge 世界根。

迁移策略：

1. 保留低频子系统指纹，帮助发现首次分叉；
2. 每个已 Authority 化领域建立规范化编码与 `DomainRoot(R)`；
3. AuthorityBatch 保存 before/after DomainRoot；
4. 完整产品闭包形成组合根；
5. 不同 tick/Revision 的异步扫描只标记不可比较。

Root 是结果复制正确性的证据，不代替真实游戏投影验证。

## 8. Snapshot / Join / Live Recovery

同一 Authority log 服务三种用途：

### 8.1 新加入

```text
Checkpoint B
→ Commit B+1..H
→ Barrier(H, RootH)
→ BarrierAck
→ Commit ..A
→ ActivationGrant(A)
→ Client publish A
→ Activated
→ Live
```

### 8.2 Live Gap

Client 发现缺 `R+1`：

- 若 CommitStore 仍保留：只读 CatchingUp，补连续结果；
- 若已淘汰或本地状态不可信：ResyncRequired，安装新 Checkpoint；
- 其他 Client 不等待它。

### 8.3 Projection Fault

Client 应用部分失败：禁止继续接后续 Commit 冒充健康。保存首次故障证据，撤销 Grant，重新安装完整可信闭包。

## 9. 第一 Authority Slice

建议按“闭包最小 + API 最清楚”原则在税率、预算或需求中选一个，不同时做三个。

示例：税率。

### Intent

```text
SetTaxRate(service, rate, observedPolicyVersion)
```

### Host

1. 验证成员可编辑；
2. 验证 rate 范围和策略版本；
3. simulation-thread 调用 Economy 真实入口；
4. 读取最终 rate 与受影响的声明状态；
5. 生成 Revision R 的 AuthorityBatch。

### Client

1. 检查连续 Revision/BeforeRoot；
2. ApplyScope 内安装最终 rate；
3. 刷新必要 UI/派生缓存；
4. 读取真实值；
5. 验证 AfterRoot；
6. 发布 AppliedAck(R)。

验收需要两个真实游戏进程，不以纯模型测试替代。

## 10. Building / Net 的迁移门槛

Building/Net 只有满足以下条件才进入实现：

- 确认实体 identity/generation 方案；
- 枚举操作的费用、Prefab、引用和派生网格；
- 证明 Client 可以安装结果而不重复 Host 的随机/规则决策；
- 失败时知道最小可信恢复闭包；
- 与 CQU 对应操作做 A/B 状态差异实验；
- 有实际存档重载测试。

道路不能因为 CQU 已有 `NodeCreateHandler` 就提前成为第一切片；旧 Handler 的价值是暴露副作用与真实调用路径。

## 11. 不允许的捷径

- AuthorityBatch 中放一个旧 Command 类型名再反射执行；
- Client 为得到相同 ID 重用 CQU Array replay 就宣称结果复制完成；
- 只同步 UI 显示值，不同步影响后续规则的真实值；
- 每隔几秒强制覆盖资金/需求来掩盖持续漂移；
- projection 失败后清标志继续跑；
- 用全房 pause 等所有 Client 应用每个 Commit；
- 因为 Transport reliable ordered 就跳过 Revision/Root 校验。
