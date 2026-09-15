# 会话协议 v2 与传输契约

规范版本：2.0-design。关联需求：F-AUTH-01、F-STATE-01、F-JOIN-04、F-REC-02、F-REC-03、F-NET-01。本规范为新增设计，当前 `Forge.Protocol.FrameCodec` 仍是 [v1](../PROTOCOL.md)。不得修改版本字段却继续发送不同语义的数据。

## 1. 分层与信任

传输层负责经验证的远端身份、连接加密、拥塞/重传、通道和连接统计。会话层负责房间、Membership、JoinGeneration、权限、版本和配额。复制层负责世界结果的顺序、完整性、基线和根。游戏层负责实际引擎状态；传输 ACK、会话接受、镜像更新和世界已应用是不同事件。

保留 GameNetworkingSockets 作为首选可靠/不可靠消息后端，通过独立库名的窄原生 ABI 适配 net35，不替换游戏 steam_api。[S-GNS] 未认证的连接最多获得受限 Bootstrap 配额；不能因 GNS socket connected 就调用 HostSession.RegisterPeer 并赋予游戏权限。

互联网加入：邀请码定位房间与访问策略，经 HTTPS 信令服务取得短期、限用途的 JoinTicket；票据绑定 RoomId、World/Epoch、Member/Generation、角色、客户端密钥身份、主机预期密钥身份、nonce、过期时间和协议/Schema 摘要。Host 验证票据签名/撤销与一次性使用，再把认证结果绑定实际 ConnectionBinding。邀请码不是密钥，不是任意玩家自报的 SenderId。

必须验证所选独立 GNS 版本能把受信证书/密钥身份绑定到实际加密连接，并提供足够的对端证据给会话层；这属于 WP-05 的阻断性 API/安全验证，而不是已经完成的能力。仅在应用报文中附加公钥文本或把一个 bearer token 发在未经验证的连接上，不构成通道绑定。若该前提不能满足，必须修订传输 ADR 并选成熟认证传输，不能自行实现未经审计的密钥交换。

局域网/离线加入也需要明确可信身份，例如受信邀请中的主机密钥指纹和针对成员的独立授权。缺少信任依据时不能静默开启无认证模式。客户端提交的权限、Mod 类别和世界身份都只能作为待核对数据。

房间发现/信令/中继不接触城市规则。提供直连优先和自营/授权中继后备；免费 Steam SDR 不是本项目默认可用依赖。[S-SDR] 中继的身份验证、带宽和连接上限独立于游戏世界权限。网络攻击防护不覆盖已控制本地游戏进程或持有 Host 权限的恶意主机。

## 2. 固定 Bootstrap

Bootstrap 使用独立 CSFB magic 与固定消息表，不能使用尚未协商商定的世界 Schema 或动态 protobuf 子类型注册表。单消息最大 16 KiB，支持清单采用有限分页和最终摘要，限制条目数与总字节；不在一个 Hello 塞入数千资产名称。

协商字段包括：实现版本、支持的协议 major/minor、游戏 build 摘要、规范化 Schema 集合摘要、必需能力、内容/Mod 清单摘要、平台/运行时证据标识和协商上限。协议 major 不同拒绝；minor 仅允许明确能力交集且不删除主机必需能力。未知关键字段或未知必需能力拒绝。

Bootstrap 消息按 `Hello → IdentityProof → ManifestPages → CompatibilityResult → SessionWelcome` 的受限状态机处理。证书/原生连接认证与应用授权相互补充。ManifestPage 的顺序、总数、摘要和请求身份验证后，才可判断清单完整。未经授权不分配快照磁盘配额、不读取城市文件、不接受载入请求。

Bootstrap 的精确字节布局和 golden vectors 在 WP-02 冻结，与认证 API 验证结果联调；该部分没有假定某个尚未核实的 GNS exporter API 存在。这个字段级验证任务是实现前门槛，不是绕过身份认证的许可。

## 3. 会话帧布局（v2 候选冻结格式）

多字节整数采用 little-endian；UUID 为 RFC 4122 字节序。固定头 88 bytes。Payload 限制为 65536 bytes。大小字段先校验，再申请数组；不得依赖远端提供的长度预分配无界缓存。

| 偏移 | 长度 | 字段 |
| --- | ---: | --- |
| 0 | 4 | Magic：ASCII CSF2 |
| 4 | 2 | ProtocolMajor：2 |
| 6 | 2 | ProtocolMinor：0 |
| 8 | 2 | HeaderBytes：88 |
| 10 | 1 | Lane：STATE=0、CONTROL=1、BULK=2、PRESENTATION=3 |
| 11 | 1 | Flags：初始为 0，未知位拒绝 |
| 12 | 2 | MessageKind |
| 14 | 2 | Reserved：0 |
| 16 | 16 | WorldId |
| 32 | 8 | Epoch：非零 |
| 40 | 16 | ConnectionBinding |
| 56 | 8 | LaneMessageSequence |
| 64 | 16 | CorrelationId |
| 80 | 4 | PayloadBytes |
| 84 | 2 | PayloadSchemaVersion |
| 86 | 2 | Reserved：0 |
| 88 | 可变 | Typed payload |
| 88+PayloadBytes | 32 | SHA-256(header+payload)，仅完整性用途 |

总帧长度必须为 120+PayloadBytes，不接受尾部附加字节。帧头的连接绑定还要与可信 transport 上下文相同，不能被当作可信身份的来源。消息 Kind、Lane、方向、阶段、Schema 与协商能力全部检查。没有会话 Stamp 的 Bootstrap 不伪装成 WorldId=0 的普通帧。

此完整性摘要不提供身份认证。实际信道仍必须认证加密。公开状态消息不能包含可用于重新登录的密钥；Token/Grant 的保密和不可伪造由连接认证及主机记录保证，不以随机 GUID 的不可猜测性代替权限校验。

## 4. 传输通道与背压

| Lane | 内容 | 交付与规则 |
| --- | --- | --- |
| STATE | AuthorityBatch、ReplayBarrier、ActivationGrant | 可靠有序；同一 Client 的历史与实时序列在此原子衔接 |
| CONTROL | 意图、回执、取消、BarrierAck、Activated、心跳、权限控制 | 可靠有序且受速率上限约束 |
| BULK | 快照清单与分片 | 可靠传输、有限窗口，按 TransferId/offset 校验；可重传不可重复累计进度 |
| PRESENTATION | 光标、幽灵预览、已审核表现状态 | 不可靠 sequenced；可替换旧值、超时丢弃 |

只保证同一 lane 的可靠消息有序，不能假设跨 lane 或跨连接的接收全序。STATE 内的 marker 仍须在本地游戏应用达到对应边界后生效。[S-LANES]

GNS lane 是每连接调度，不自动解决所有连接共享主机上行带宽的公平性。外层房间调度器采用逐 Peer 公平轮询和字节令牌；紧急控制消息有限优先，STATE/BULK 在容量允许时提供非零剩余服务；表现流优先降级。高优先级持续非空会饿死低优先级，不能只给 bulk 一个极低优先级了事。

队列超过字节或时间预算时，未执行请求返回 Busy；已提交结果不能撤销或丢弃来腾空间，应断开/恢复落后客户端。Send 返回成功只代表进入传输处理，不代表远端已应用。网络工作者不阻塞主机模拟线程等发送完成。

## 5. 核心消息语义表

字段名是 Schema 要求；实际二进制 codec 在 WP-02 中按固定顺序与测试向量冻结。禁止通用反射对象反序列化。

| 消息 | 方向 / 通道 | 必要内容与门槛 |
| --- | --- | --- |
| Intent | Client→Host / CONTROL | GrantId、MemberGeneration、OperationCounter、读集、对象代次、命令类型与参数；必须 Live |
| IntentReceipt | Host→Client / CONTROL | 操作身份与 payloadHash、Rejected/Committed/Expired/Unknown、结果 Revision、错误码；重复请求稳定返回 |
| BatchBegin/Part/End | Host→Client / STATE | BatchId、Revision、OriginKind、Tick、前/后根、域集、分片数/字节、整体摘要；完整才应用 |
| AppliedAck | Client→Host / CONTROL | 实际已发布 Revision、其根、队列/进展摘要；不能用已接收位置替代 |
| SnapshotOffer | Host→Client / CONTROL | JoinIdentity、SnapshotId、TransferId、基线 B、内容清单、根、租约；必须已授权 |
| SnapshotChunk | Host→Client / BULK | SnapshotId、TransferId、index/offset、长度、块摘要与字节 |
| SnapshotProgress | Client→Host / CONTROL | 已验证范围/块位图摘要、有界缺块请求；只影响本 Transfer |
| WorldInstalled | Client→Host / CONTROL | 当前 LoadGeneration、B 与真实投影的根；不是只通知文件下载完成 |
| ReplayBarrier | Host→Client / STATE | BarrierId、固定 H、Root(H)、JoinIdentity、TTL |
| BarrierAck | Client→Host / CONTROL | 对同一固定 H 的根证明与当前加入代次 |
| ActivationGrant | Host→Client / STATE | GrantId、固定 A 与 Root(A)、Member/Join/Connection 绑定、权限版本、TTL |
| Activated | Client→Host / CONTROL | GrantId、A；与之后 Intent 在同一有序控制流 |
| GapRequest | Client→Host / CONTROL | 最后已应用位置和期望区间；限频，不能索取其他 Epoch 文件 |
| ResyncRequired | Host→Client / CONTROL | 原因、作用域、新 JoinGeneration 的启动策略；不直接覆写客户端城市 |
| CancelJoin / Cancelled | 双向 / CONTROL | 明确 JoinIdentity/TransferId，重复取消幂等 |
| Heartbeat / Progress | 双向 / CONTROL | 单调采样标识、最新提交/应用/加载进度；时间只由接收者本地时钟判断 |
| PermissionChanged / SessionClosing | Host→Client / CONTROL | 递增权限版本/原因，立即撤销对应可写资格 |

未知 Kind、错误方向、错误阶段在世界层之前拒绝。客户端不得发送 AuthorityBatch；观众不得发送 Intent；加载中的客户端只能发送与该阶段匹配的进度、确认和取消。

## 6. 逻辑批次与分片

应用层逻辑批次最大 1 MiB，分片数据最大 32 KiB、最多 32 片，均为首版提议预算。网络帧上限仍为 64 KiB，分片元数据也必须计入。必须同时校验 begin 元数据、part 序号/总数/长度、累计字节与整体摘要。

Client 在完整收齐并验证逻辑批次前不得应用游戏状态；只有完整游戏投影发布后才发送 AppliedAck。缺 begin、重复冲突、缺 part、无效 end 或重装连接后的半批次均不能续用。若完整批次准备后游戏执行失败，不能把成功的 part 当作已应用子操作；进入明确的部分应用恢复。

Host 的 Committed Receipt 在其自身权威批次成功封装并提交后产生，**不等待任何 Client 收齐、应用或确认**。通过 CONTROL 到达的 Receipt 可能早于 STATE 结果；此时 UI 只能显示“主机已提交，等待本地显示”，不能据此提高本地 AppliedRevision、解锁未激活玩家或宣布已经保存。Host 不能在自身批次只执行了一部分时发成功回执，但一个 Client 应用失败也不能撤销 Host 已经提交的事实。

大城市状态必须按真正封闭的领域批次捕获或在已证明的原子批次组中发布，不能因超过协议上限截断。批次组引入需要额外版本化协议与完整性测试；v2 首次实现先拒绝超过上限的工具计划，支持城市规模必须覆盖自然模拟输出峰值。

## 7. 操作身份、重连与幂等

逻辑操作键为 `(WorldId, Epoch, MemberId, MemberGeneration, OperationCounter)`，并记录规范化意图摘要。ConnectionBinding 每次重连改变，逻辑操作账本不因换 socket 立即丢失。

Host 按成员保留最高已处理计数和有界回执。相同键相同摘要返回原结果；相同键不同摘要拒绝为 IdentityConflict；早于高水位但详细回执已淘汰的操作返回 Expired，不重新执行。成员代次在席位被撤销/复用时递增，防止新玩家继承旧操作身份。

重连先重新认证并撤销旧绑定，再查询未确认操作状态。Committed 表示不得重做，Rejected 可让玩家修改后发新操作，Expired/Unknown 显示状态不确定并先恢复世界；禁止自动将不确定的修路/扣款变成新操作重发。Actor 账本以当前 Epoch 为边界；Host 崩溃恢复新 Epoch 后，不提供原进程所有操作永久 exactly-once 的承诺。

当前 M0 的 RequestId 跟随连接，只能提供较窄保证；WP-01 必须显式引入 Member 账本与迁移测试，不能把旧连接 GUID 重用来“实现重连”。

## 8. 期限、时钟与迟到授权

Host 的 leaseDeadline 是仅在 Host 上解释的单调时钟值，不把它作为可跨机器比较的时间戳。Barrier/Grant 在线上携带有限 TTL 时长；Host 保存自己的发放时间和到期点，收到 Ack/Activated/Intent 时独立检查。重复消息不重置原授权期限；延长期限必须创建新的、显式绑定的授权流程。

Client 从首次接收时起，用自己的单调时钟为该 Barrier/Grant 建立本地最长处理期限；这一期限只是本地停止等待的上限，不能证明 Host 的授权此刻仍有效。延迟期间 Host 已撤销或到期时，即使 Client 的本地 TTL 尚未到，Host 也拒绝 Activated/Intent，返回 Expired/RejoinRequired。Client 立即撤销编辑并重新进入受限恢复流程，不将失败操作自动作为新意图发送。

正常高延迟也可能使一个授予过期，因此设置有界重试与追赶预算；不靠伪造时钟同步消除这一情况。客户端短暂显示可编辑不会成为权威写入：它只发送意图，Host 的有效成员/Grant/权限/读集检查仍是最终边界。UI 应尽量等明确的成员状态确认，并在落后或健康信号异常时提前禁用持久工具。

身份票据的证书/UTC 有效期属于认证协议，由验证方的受信本机时间和有限容差检查；它与会话内单调租约不同。不能使用对端自报的墙钟决定票据有效，不能通过重置本地超时来延长票据授权。

## 9. 版本、兼容和存档

区分产品版本、Wire major/minor、消息 Schema、领域 Schema、Checkpoint Schema、游戏 build 与支持目录版本。必要能力不匹配时，在下载前拒绝，给结构化差异报告。minor 升级仅能启用双方明确支持的扩展，不静默放宽权威规则。

运行中不改变成员的领域 Schema；需要变更时停止新加入、保存有效检查点、关闭会话并重新协商。存档迁移只离线操作副本，保留原文件和迁移记录。两个版本必须共享明确测试过的 Schema/协议才能同房，不通过模糊“版本相近”放行。

支持目录清单是内容/配置事实加维护者审核规则，不是客户端自报的安全标签。清单中的路径只用于本地采集，不允许远端指定任意读取路径。日志记录差异 ID 与摘要，不上传原始密钥和私人文件路径。

## 10. 安全边界与验证

解码预算覆盖单报文、单连接、房间总量、解压后总量、字段条目和字符串长度；首版不自动接受未知压缩格式。快照只写应用控制的临时目录，不接受远端文件路径。清单以内容摘要索引，防止路径穿越和任意文件替换。

未认证请求限时清理，认证票据限用途并防重放；禁止客户端修改 Host 权限、请求任意后台管理命令或触发自动下载执行 Mod。原生 IO 回调只投递经过验证的连接上下文，回调队列中的旧 userdata 不可当作当前成员归属。[S-STEAM]

验证包括字段级 golden vectors、独立解码器互测、部分读写、畸形批次、错误角色、错误阶段、过期 token、重连重放、跨 lane 重排、身份服务和中继不可用。增加 Host Receipt 先到但 STATE 尚未应用、Host Grant 已过期但 Client 本地 TTL 尚未到、重复 Grant 不续期的确定性测试。实现前仍须完成认证 API 可行性门槛；[验收规范](ACCEPTANCE.zh-CN.md) 给出对应 AT 编号。来源见 [SOURCES](SOURCES.zh-CN.md)。
