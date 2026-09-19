# 施工点三：mod 支持平台化（从桥接密集到声明密集）

> 状态：`proposal — design_target_not_measured`。父文档：[总施工计划书](CONSTRUCTION-PLAN.zh-CN.md)。
> 目标陈述：Forge 只负责联机；对 mod 的支持分层层级化，让"大多数 mod 免手写桥接、
> 少数 mod 填声明、极少数 mod 写全桥"。同时必须承认原理性边界：
> 每条要同步的状态都需要**规范编码**与**联机策略**两类信息，后者是信息不是代码，
> 抽象不能凭空变出"RemoveNoisePollution 会破坏联机"这类事实——平台化降低的是声明成本，不是零声明。

## 0. 现状（桥接密集的证据）

- 5 个已知 mod 桥硬编码在 `KnownModBridgeRegistry.cs:41-48` 的静态数组里，
  每个对着目标 DLL 的**内部类型名**反射（如 `GameAnarchy.ModSettings.ModSetting`、
  `GameAnarchy.Patches.BuildingAIPatch._modSetting`），版本精确锁死（GA 1.3.1.0）；
- client-only 名单硬编码 9 个类型名（`CompatibilityCollector.cs:20-25`），
  加上 `ModCategory` 里的一串字符串特判（`:118-124`）；
- GA 的 28 个不支持选项名单硬编码在 `GameAnarchyBridge.cs:79-89`；
- 未分类 mod 的默认命运：二进制指纹两端完全一致才允许加入，**状态不同步**。

结论：新增一个 mod 的支持 = 改 Forge 源码 + 锁版本 + 发版。这是桥接密集的极致。

## 1. 分层模型

| 层级 | 覆盖对象 | 同步方式 | 每个新 mod 的成本 |
| --- | --- | --- | --- |
| Tier 0 自动兼容 | UI/相机/皮肤类（不碰模拟） | 无需同步，标记为 client-mod | **零**（自动分类） |
| Tier 1 基础域免费 | 通过游戏自身工具表达效果的建造类 mod | 已被基础域（路网/建筑/区划/prop…）的意图与状态同步覆盖 | **零**（现有架构红利，需实测验证覆盖率） |
| Tier 2 声明式桥 | 有设置面/简单状态面的 mod | 通用设置同步适配器 + JSON manifest | **填一份配置文件** |
| Tier 3 全桥 | TM:PE 级深度模拟 mod | 手写适配器 + 专项审计 | 现状不变（例外而非常态） |

## 2. 工作包

### WP-3.1 client-only 启发式自动分类（收益最大，独立可做）—— 已实现，附真机差异报告

- **现状**：`ClientOnlyModTypes`（`CompatibilityCollector.cs:20-25`）+ `ModCategory`
  字符串特判，名单外的一律按需同步/拒绝。
- **已实现**：
  - `Forge.Core/SimulationSurfaceMatcher`：保守的"表现层判定"——类型面出现
    Manager/Tool/Simulation/AI 后缀即视为触碰模拟面（方向刻意单边：误报只会回落到
    精确匹配，安全；漏报才是危险，故不做"看起来像 UI"的放行名单）；
  - `CompatibilityCollector.TouchesSimulationSurface`：收集 mod 程序集的可见类型面
    （自有类型、基类链、接口、成员签名），不可加载的类型/程序集 fail-closed；
  - `ModCategory` 接入顺序：显式声明 > 依赖/阻断名单 > 已审计条目 > 硬编码 client-only
    名单 > **启发式** > 默认精确匹配；
  - 已知 v1 局限（已记录）：泛型类型参数与 Harmony attribute 目标不扫描——
    签名含 List&lt;BuildingManager&gt; 的 UI mod 保持精确匹配（保守方向）。
- **真机差异报告**（`docs/plan/evidence/modscan-report.txt`，工具 `scripts/modscan`，
  MetadataLoadContext 元数据反射，不加载执行 mod 代码）：
  本机 Workshop 32 个 DLL——17 个判 client（免桥接）、7 个不可扫（fail-closed）、
  `UnifiedUILib`/`TMPE.API` 因引用 NetManager/Manager 族正确判 sim、
  `InfiniteGoodsMod` 正确判 sim（BuildingManager/PowerPlantAI）。
- **验收**：单测 `SimulationSurfaceMatcherTests` 4 项（纯 UI 不触发 / Manager·Tool·AI·Simulation
  触发 / Controller 不触发 / 空集 fail-closed）；分类器对本机 mod 集输出与预期一致。
- **验收**：对本机已装 mod 集跑分类器，与手工判定对比输出差异报告；
  分类结果进入兼容清单并可被 S2 的报错文本引用。

### WP-3.2 兼容目录数据化（发版解耦）

- **现状**：`KnownModBridgeRegistry` 静态数组、版本锁、选项黑名单、local-only 属性表全部编译进 DLL。
- **设计**：随包分发 `compat/mods/*.json`，schema 草案：

```json
{
  "mod": "gameanarchy",
  "assembly": { "name": "GameAnarchy", "versionRange": "[1.3.1, 1.4.0)" },
  "category": "synchronized",
  "settings": {
    "type": "GameAnarchy.ModSettings.ModSetting",
    "holder": "GameAnarchy.Patches.BuildingAIPatch::_modSetting",
    "holderFallback": "GameAnarchy.Patches.BulldozeToolPatch::_modSetting",
    "localOnly": ["AchievementSystemEnabled", "SkipIntroEnabled", "..."],
    "blockedOptions": ["RemoveNoisePollution", "..."],
    "numericConstraints": { "OilDepletionRate": 100, "OreDepletionRate": 100 }
  }
}
```

- **加载与校验**：启动时解析 + schema 校验；非法 manifest 按该项不存在处理并记录诊断
  （fail-closed 不变：坏数据不产生信任）。
- **效果**：新增/修正一个 mod 的支持 = 随包改一个数据文件，不再需要重新编译 Forge；
  版本锁从"精确相等"放宽为"声明区间"，但区间外仍拒绝。
- **验收**：GA 桥的全部策略（类型、holder、黑名单、数值约束）迁入 manifest 后，
  现有 GA 相关测试全绿；故意写坏 manifest 时行为 = 未声明该 mod（拒绝而非崩溃）。

### WP-3.3 通用设置同步适配器（Tier 2 落地）

- **关键事实**：`GameAnarchyBridge` 的采集部分已经是通用的——
  `SharedProperties`（`:114-127`）反射全部公开 get/set 基础类型属性、
  `ToBits/FromBits`（`:163-182`）统一位编码、`SchemaFingerprint`（`:152-161`）规范指纹。
- **改动**：把"枚举属性 → 序列化 → 校验 → 应用"抽成 `GenericSettingsStateAdapter`，
  行为由 WP-3.2 的 manifest 驱动（localOnly/blockedOptions/numericConstraints）；
  GA 桥退化为"manifest + 特例补丁"的薄壳。
- **边界**：通用适配器只覆盖"可枚举设置面"；有模拟副作用的 mod（GA 的经济/火蔓延 manager
  patch）仍需 Tier 3 手写——那部分是语义不是数据。
- **验收**：用 manifest 声明一个 GA 之外的真实 mod 设置面（如 81 Tiles 部分设置），
  双机验证设置同步一致。

### WP-3.4 第三方 API 与文档化

- **现状**：`ForgeExtensionApi.Register`（适配器注册）与 `ForgeCompatibilityApi.TryGet`
  （mod 自我声明 ForgeSynchronized/ClientOnly/Blocked）已存在，
  但**第三方可用性未验证**：无公开引用程序集/包、无文档、无示例。
- **改动**：
  1. 明确公开面：第三方 mod 作者需要的最小 API（声明类别、注册适配器、提供设置面）；
  2. 提供可引用的 API 程序集 + 一个最小示例 mod；
  3. 文档进入 `docs/`，并在 TECHNICAL-SPEC 增补对应章节。
- **诚实标注**：本工作包先做"可用性验证 spike"，再决定 API 形态。

### WP-3.5 兼容矩阵 UI

- 把分类结果（自动 Tier0 / 声明 Tier2 / 全桥 Tier3 / 拒绝）以玩家可读清单呈现
  （复用 S2 的报错文本与 `ForgeCompatibilityFailureText`），加入房间前即可见
  "哪些 mod 会跟过去、哪些会被拒、为什么"。
- **验收**：GA 聚合报错（S2 回移后）在 UI 完整可读，不再被标签高度截断
  （`ForgeMultiplayerUi.cs:474` 预检标签 86px 高度需随内容伸缩）。

## 3. 非目标与红线

- 不做"自动语义同步"：不知道语义的自动同步 = 静默漂移，违反 fail-closed。
- 未知 mod 默认拒绝不放松；平台化改变声明成本，不改变默认信任。
- 版本锁可放宽为区间，但"区间外拒绝 + 拒绝原因可读"必须保留。
- Tier 3 全桥长期存在；TM:PE 在审计通过前保持 blocked/dormant（`KnownModBridgeRegistry.cs:58-62` 注释约定）。

## 4. 依赖与顺序

WP-3.1（自动分类）独立可先做；WP-3.2（manifest）先行于 WP-3.3（通用适配器消费它）；
WP-3.4 的 spike 可与 WP-3.2 并行（manifest schema 直接决定第三方声明形态）；
WP-3.5 依赖 S2 报错回移。
