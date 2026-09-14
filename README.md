# CSM-Forge

从零重写的 **Cities: Skylines 1 多人联机系统**。不在 CSM / CSM-CQU 上继续开发，不复用其源码或协议。

**当前状态：已实现可执行的主机权威复制内核与测试实验台，另有未实机验证的 ICities 加载探针。尚不是可开房游玩的多人模组，没有可用的游戏联机安装包。**

## 已实现

- 主机唯一写入入口、全局提交顺序、连接身份边界、权限与加入完成门槛。
- 请求去重、有限回执与日志保留；过期请求不重新执行；过期日志明确要求快照。
- 客户端前后状态校验、缺口补日志、部分失败隔离、快照安装与 Ready 屏障。
- 主机绕过提交日志发生的状态变化检测；污染主机不得继续发布快照。
- 固定版本、有大小上限的二进制帧及 Intent/Commit 编解码；SHA-256 完整性校验。
- 可使用磁盘临时流的乱序分片组装、重复/冲突检查、完整文件校验、取消释放。
- 严格游戏/Schema/组件二进制与配置指纹准入；未知额外组件默认拒绝。
- 单调时钟超时判断、有界诊断环与跨线程收件队列。
- 同一份生产代码同时编译 net35 / net8；Windows、Linux 与 Mono 自动化验证。

实验台中的 `ParameterWorld` 是 16 个整数槽的参考领域，**不是已经接入 Cities 的税率、资金或道路**。网络故障测试使用生产编解码和复制内核，但不是实际互联网连接测试。

## 尚未实现 / 不作承诺

真实传输与身份认证、房间服务/中继、游戏 Mod 清单采集、完整会话协调器、CS1 权威模拟隔离、道路/建筑等结果适配器、原生存档检查点、实际游戏重连与 UI 均未完成。不能把接口、策略类或探针当成这些功能已经交付。

## 文档

- [旧架构研究与根因分析](docs/research/architecture-review.zh-CN.md)
- [新架构、模块和一致性边界](docs/ARCHITECTURE.zh-CN.md)
- [协议的已实现范围](docs/PROTOCOL.md)
- [测试与诊断策略](docs/TESTING.zh-CN.md)
- [MVP、验收门槛与后续顺序](docs/ROADMAP.zh-CN.md)
- [ADR：主机权威与运行时边界](docs/adr/0001-authority-and-runtime.md)
- [游戏加载探针及构建边界](src/Forge.Runtime.Cities1/README.md)

## 运行内核验证

安装 .NET 8 SDK 后，在仓库根目录执行：

```sh
dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release
dotnet run --project tests/Forge.Tests/Forge.Tests.csproj -c Release -f net8.0 --no-build
```

安装 Mono 的环境还可执行实际 net35 编译产物：

```sh
mono tests/Forge.Tests/bin/Release/net35/CSM.Forge.Tests.exe
```

标准 Mono 和现代 .NET 的成功 **不等于游戏自带 Mono、Harmony 补丁或双机联机验收成功**。公共 CI 不下载、不提交也不伪造游戏程序集。

开发提交进入 `develop`；可用自动化检查通过后快进 `main`。两者均为开发状态，不代表可发布给玩家的版本。构建不会自动安装或覆盖游戏文件。
