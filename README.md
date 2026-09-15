# CSM-Forge

从零重写的 **Cities: Skylines 1 多人共同经营系统**。CSM / CSM-CQU 仅作只读研究，不复用旧架构或协议。

**当前代码是 M0 参考域复制内核、v1 协议、68 项内核回归与未实机验证的 ICities 探针，不是可以开房游玩的完整 Mod。** 本轮已补齐 v2 技术规范、并行加入/游玩期恢复路线和可检查的需求索引；新增 v2 游戏能力尚未实现。

## 产品目标

Host 权威模拟、Client 提交意图并应用结果；主机自身操作与自然模拟也进入同一提交序列。正式目标包含实时共同经营、游戏进行中多玩家并行进入，以及单个客户端失败时不拖停其他玩家。

首发目标为 Windows x64 四人房间（含 Host）、至少两名玩家并行加入、已验证的游戏/DLC/内容组合。暂停城市双人编辑只是内部验证关卡，不是最终产品范围。实际支持规模和性能必须经过真实游戏验收。

## 技术文档入口

| 内容 | 文档 |
| --- | --- |
| 完整技术方案、需求、模块、接口、当前代码差距 | [技术总纲](docs/TECHNICAL-SPEC.zh-CN.md) |
| 独立 JoinContext、共享快照、历史屏障和取消/重连 | [并行加入](docs/spec/PARALLEL-JOIN.zh-CN.md) |
| 游戏线程、模拟隔离、自然结果、实体和领域闭包 | [运行时复制](docs/spec/RUNTIME-REPLICATION.zh-CN.md) |
| 认证、消息、通道、版本和字节边界 | [协议 v2 设计](docs/spec/PROTOCOL-V2.zh-CN.md) |
| 游玩期故障、独立恢复、存档、资源与诊断 | [鲁棒性规范](docs/spec/ROBUSTNESS.zh-CN.md) |
| 12 个工作包、依赖和可并行开发路径 | [完整实施路线](docs/ROADMAP.zh-CN.md) |
| 20 组验收、弱网络和 24 小时长跑要求 | [验收规范](docs/spec/ACCEPTANCE.zh-CN.md) |
| 来源与尚未取得的证据 | [来源索引](docs/spec/SOURCES.zh-CN.md) |

机器可检查的 [需求/工作包/验收索引](docs/spec/spec-index.json) 与 [提议预算](docs/spec/budgets.json) 配套维护。数值预算是未实测目标，不是已经达到的性能数据。

旧资料保留：[原架构研究](docs/research/architecture-review.zh-CN.md)、[当前代码的 v1 协议](docs/PROTOCOL.md)、[M0 历史设计与验证](docs/history/README.md)。新路线的关键决策见 [ADR-0002](docs/adr/0002-parallel-hot-join.md)。

## 已实现的 M0 范围

主机排序、权限/阶段检查、有限请求回执、日志保留、参考世界绝对结果应用、前后状态校验、缺口和快照恢复状态机、主机未记录变更防护；有界二进制帧及 Intent/Commit codec；磁盘分片组装与完整性校验；兼容策略、单调时钟和有界诊断。

`ParameterWorld` 只是 16 个整数槽，不是已经接入游戏的税率、道路或城市模拟。测试中的多副本丢包/重复投递不是实际互联网联机。`CompleteJoin` 的当前头比较等入口还需要按 v2 改造。

真实传输/身份服务、并行加入协调器、Mod 实际采集、CS1 模拟隔离、自然模拟结果适配、原生检查点和玩家 UI 尚未完成。游戏探针只观察加载环境，不开启网络或修改城市。

## 验证

```sh
python scripts/check_docs.py
python -m unittest discover -s tests/docs -p "test_*.py"
dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release --nologo
dotnet run --project tests/Forge.Tests/Forge.Tests.csproj -c Release -f net8.0 --no-build
```

有 Mono 时可运行 `mono tests/Forge.Tests/bin/Release/net35/CSM.Forge.Tests.exe`。现代 .NET/标准 Mono 通过不等于游戏自带 Mono、Harmony 或实机多人通过。文档检查也不证明 v2 功能已实现。详见 [测试入口](docs/TESTING.zh-CN.md)。

代码进入 develop，核对该提交 CI 后快进 main；两者都是开发基线，不是玩家发行版。构建不自动安装，不下载/提交游戏程序集、用户城市或密钥。本轮不改动旧仓库，也不设置 GitHub 服务端分支保护。
