# CSLModernMap 6.6.2 ClientOnly 审计

## 1. 实际供体

本审计直接读取本机 Steam Workshop 安装目录 `255710/3781187198` 中的实际 DLL，而不是猜测 type：

- Workshop ID：`3781187198`
- `mod.info` 版本：`6.6.2`
- 程序集：`CSLModernMap, Version=6.6.2.0`
- IUserMod type：`CSLModernMap.CSLModernMap`
- DLL SHA-256：`9fc331505b43484dc55d38762d5198e7b68aa04dca19af5ff8f59d37e76c300a`

审计时该 Workshop 页面已显示项目被 Steam 下架。Forge 的分类只描述多人权威边界，不为外部 renderer 的来源、安全性或继续分发背书。

## 2. IL 读写边界

二进制没有公开源码链接，因此使用 Mono.Cecil/IL 逐方法检查外部字段写入和 manager 调用。实际导出器读取：

- `SimulationManager` metadata、pause 状态与 frame index；
- Net node/segment/lane、PathUnit；
- Terrain/Water、Building、District/Park、TransportLine、NaturalResource；
- 可选 TM:PE lane/segment traffic statistics；
- 可选 Procedural Objects 数据。

对游戏对象的外部 `stfld/stsfld` 扫描只发现 UI cursor/position value 写入；没有对上述 simulation manager、buffer 或实体结构的字段写入。`PathManager.WaitForAllPaths()` 是导出前的读取同步屏障，不创建、释放或修改 path。`InstanceID.NetSegment` 只写导出函数的局部查询值。

Mod 的可见副作用均为本地表现/导出行为：设置 UI、写 `.mmap.gz`/JSON/配置及 renderer 文件、打开输出目录、安装并启动外部 renderer。它会询问游戏是否暂停，但没有设置 simulation pause。由此可将这个**精确二进制**分类为 ClientOnly exporter，而不能把同名未来版本自动放行。

## 3. 白名单边界

`CitiesCompatibilityCollector` 同时核对：

- IUserMod type `CSLModernMap.CSLModernMap`
- assembly name `CSLModernMap`
- assembly version `6.6.2.0`
- 上述完整 DLL SHA-256

四项全部匹配才返回 `client-mod`。版本、type、程序集或二进制任一漂移都会回到未知 Mod 的默认 exact-match；没有 `CSLModernMap.*` wildcard，也没有仅凭 Workshop 名称放宽。

## 4. 证据边界

本批证明的是已审计 DLL 的静态 IL 世界写边界与精确分类。尚未验证：

- 真实双机同时导出时的性能影响；
- 与 Forge snapshot/hot join 同时运行的线程时序；
- 外部 renderer 进程本身的安全性与行为；
- Workshop 后续重上架或新版本。

CI green 不能写成 gameplay validated，也不能写成外部 renderer 已完成安全认证。
