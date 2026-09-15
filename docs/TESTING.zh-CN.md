# 测试入口与证据边界

当前 v2 目标验收在 [可执行验收规范](spec/ACCEPTANCE.zh-CN.md)。原始 M0 测试记录及其 CI 链接保存在 [历史证据](history/TESTING-M0.zh-CN.md)，不把旧内核测试扩写成新增功能已经通过。

## 现有内核回归

```sh
dotnet build tests/Forge.Tests/Forge.Tests.csproj -c Release --nologo
dotnet run --project tests/Forge.Tests/Forge.Tests.csproj -c Release -f net8.0 --no-build
mono tests/Forge.Tests/bin/Release/net35/CSM.Forge.Tests.exe
```

第三行要求 Mono。历史测试集有 68 项，后续提交应核对该 SHA 的实际执行输出。标准 Mono 的 CLR4 回退不能证明 CS1 自带 Mono 或实际 Harmony 行为。

## 文档契约验证

```sh
python scripts/check_docs.py
python -m unittest discover -s tests/docs -p "test_*.py"
```

检查内容包括文档/相对链接存在、需求到验收和工作包的映射、工作包依赖无环、目标状态仍正确标记、预算上下界关系。它不证明协议的语义正确性或游戏功能已经实现。

## 验收层级

E0 文档与静态契约；E1 生产内核模型；E2 真实网络/磁盘/多进程；E3 真实 CS1；E4 独立多机、弱网络、长期游玩。正式发行必须在声明的支持配置中完成 AT-01..AT-20，包含实时自然模拟和至少两个并行加入者。

每份证据绑定代码 SHA、Schema、环境、城市夹具、故障输入与退出码。未运行层级保持未测，不能用低层全绿替代。新规范的数值预算是设计目标，详见 [预算](spec/budgets.json)。
