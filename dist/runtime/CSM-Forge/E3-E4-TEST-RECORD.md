# CSM-Forge 1.0 Candidate E3/E4 真机记录

> 本文件随安装包提供。默认状态全部是 `NOT RUN`。只有指定机器实际完成场景并保存证据后才能改为 `PASS` 或 `FAIL`；CI green 不能替代本记录。

## 1. Artifact 与环境

| 字段 | Host | Client 1 | Client 2 |
| --- | --- | --- | --- |
| 机器代号 | NOT RUN | NOT RUN | NOT RUN |
| `source_commit` | NOT RUN | NOT RUN | NOT RUN |
| `manifest_sha256` | NOT RUN | NOT RUN | NOT RUN |
| CS1 build | NOT RUN | NOT RUN | NOT RUN |
| OS / CPU / RAM | NOT RUN | NOT RUN | NOT RUN |
| DLC 清单 | NOT RUN | NOT RUN | NOT RUN |
| Mod / 版本 / 配置 | NOT RUN | NOT RUN | NOT RUN |
| 资产清单 | NOT RUN | NOT RUN | NOT RUN |
| 诊断 ZIP | NOT RUN | NOT RUN | NOT RUN |

测试城市：`NOT RUN`

网络条件（LAN/公网、延迟、丢包）：`NOT RUN`

开始/结束 UTC：`NOT RUN`

## 2. 会话与恢复矩阵

| 场景 | 期望 | 结果 | 证据/备注 |
| --- | --- | --- | --- |
| 1 Host + 1 Client | 加入后进入 `ClientLive`，两端 root 收敛 | NOT RUN | |
| 1 Host + 2 Clients | 三端 Live，正常玩家不因新加入者停顿 | NOT RUN | |
| 两个 Client 重叠 Join | 独立 Transfer/Barrier，不串代次 | NOT RUN | |
| hot join | 下载、加载、追赶、激活阶段真实可见 | NOT RUN | |
| Client 取消加入 | 只取消该 Client，不影响房间 | NOT RUN | |
| Client drop/rejoin | 旧连接失效，新 MemberGeneration 生效 | NOT RUN | |
| lagging Client rebaseline | 慢端单独恢复，其他玩家继续操作 | NOT RUN | |
| Host 停止房间 | 所有 Client 明确断开，城市不静默继续联机 | NOT RUN | |

## 3. 核心玩法矩阵

每项至少记录操作发起方、操作前后城市版本、三端可见结果和 projection/root 日志。

| 领域 | Host 操作 | Client 操作 | hot-join 后状态 | 结果/证据 |
| --- | --- | --- | --- | --- |
| 暂停 / 速度 | NOT RUN | NOT RUN | NOT RUN | |
| Budget / Tax / Cash / Loan | NOT RUN | NOT RUN | NOT RUN | |
| Area unlock | NOT RUN | NOT RUN | NOT RUN | |
| Road / Net create-delete-upgrade | NOT RUN | NOT RUN | NOT RUN | |
| Building / Bulldoze / natural lifecycle | NOT RUN | NOT RUN | NOT RUN | |
| Zone | NOT RUN | NOT RUN | NOT RUN | |
| District / Policy / Style | NOT RUN | NOT RUN | NOT RUN | |
| Transport line / stops / properties | NOT RUN | NOT RUN | NOT RUN | |
| Names / City name | NOT RUN | NOT RUN | NOT RUN | |
| Demand / Weather | NOT RUN | NOT RUN | NOT RUN | |
| Event / Disaster | NOT RUN | NOT RUN | NOT RUN | |
| Park / Campus / Industry / Airport / Pedestrian Area | NOT RUN | NOT RUN | NOT RUN | |
| Tree / Prop create-move-delete / slot reuse | NOT RUN | NOT RUN | NOT RUN | |
| Host Terrain brush / undo / collateral | NOT RUN | 不适用：Client 工具必须阻断 | NOT RUN | |
| Citizen / Vehicle / Path 长时间自然模拟 | NOT RUN | 不适用：Host-owned | NOT RUN | |

## 4. 固定 Mod 清单

TM:PE 必须保持未启用和 `blocked-mod`；本 Candidate 不宣称支持 TM:PE。

| Mod | 精确版本 | 单独测试 | 混装测试 | 结果/证据 |
| --- | --- | --- | --- | --- |
| Network Multitool | 1.3.9 | NOT RUN | NOT RUN | Add/Remove/Union/Split/Intersect/Parallel/Connection |
| 81 Tiles 2 | 1.0.5 | NOT RUN | NOT RUN | 9×9 Area 与 expanded utilities |
| Game Anarchy | 1.3.1 | NOT RUN | NOT RUN | 仅允许的共享配置；拒绝项必须 fail closed |
| Infinite Goods | 6.1 | NOT RUN | NOT RUN | Building buffers 与拒绝的 ServicePoint 配置 |
| CSLModernMap | 6.6.2 exact binary | NOT RUN | NOT RUN | 只读导出；漂移版本回到 exact-match |
| TM:PE | 11.9.4.1 | BLOCKED | BLOCKED | 不加入当前测试组合 |

## 5. 长跑与退出结论

| 项目 | 结果 | 证据/备注 |
| --- | --- | --- |
| 2 小时基础 soak | NOT RUN | |
| 24 小时 RC soak | NOT RUN | |
| 弱网/延迟/丢包 | NOT RUN | |
| 大城市 Snapshot / Journal 压力 | NOT RUN | |
| projection audit 无未解释 drift | NOT RUN | |
| 崩溃、卸载、返回主菜单、再次加入 | NOT RUN | |

结论只能填写其一：

- `NOT RUN`：没有足够真实机器证据；
- `FAIL`：记录首个失败步骤、实际结果及所有机器诊断 ZIP；
- `E3 PASS / E4 NOT RUN`：真实 CS1 已验证，但独立多机/弱网/长跑未完成；
- `E4 PASS`：完整矩阵、阻断场景与长跑均有绑定当前 Artifact 的证据。

当前结论：`NOT RUN`
