# T19 shared functional 宿主预算修订

2026-10-03，Astra High 设计审阅与 root 复核同意，仅将新的 shared functional 测试宿主护栏由 900 秒修订为 1500 秒。策略标识为 `t19-shared-functional-host-v2`。这是整套正确性测试的外层运行预算，不是驱动吞吐 SLA，也不表示原 900 秒门通过或延迟原因已经定位。

v1 S12 与 release-gates 要求实际最终包的完整下游正确性验收，没有规定 shared functional 的 900 秒吞吐目标。此前 T19 工具及执行计划使用 900 秒护栏；本次明确修订该工具合同，保留两次超时、诊断及清理记录，不回写历史结果。TLS 资源门的 600 秒测量和 900 秒总护栏保持原合同。

## 修订依据与限度

- 同一 v5 包的同步／异步诊断，每行中位数约 386／385 ms，命令释放耗时很小。异步原始 INSERT 每行五次交换：statement 分配、参数描述、执行、后续结果终结及 statement 关闭。它们均有契约职责，未删除任何步骤。
- 自有连接的 NoDelay A/B/B/A 对照，四组中位数为 384.85／386.04／384.35／387.14 ms，没有稳定收益，产品未修改该选项。
- 同环境独立进程的 R1／当前版／当前版／R1 对照，中位数为 384.59／388.96／385.41／384.94 ms；每行均五次交换，实际服务器、UTF-8 和 msgVersion 21 相同。两版都在当前环境下较慢，已测 seed 路径未出现稳定的当前包特有成本。此比较使用共同的 ConnPooling=false，只覆盖已打开连接内的负载，不替代最终 Pooling=true 功能验收，也不确定具体网络机制。
- 原 900 秒进度诊断记录 71 个用例开始、70 个返回；三个聚合 fixture 各 422 次 INSERT 和命令释放全部结束。工作持续推进，最后一个 Trim 用例在护栏到期前约 5 秒开始。返回事件不是断言通过，原记录仍为 exit124，未生成完整 TRX。

以上安全归档见 [候选 v5 历史索引](../evidence/T19/candidate-v5-interim/archive-index.json)。原始输入 SHA-256：

| 输入 | SHA-256 |
| --- | --- |
| R1/current ABBA 独立验收汇总 | `9d8f4c63741c1c6ee685dafa1834454dde1f609bd8d3e3c73293a07c89f19eca` |
| ABBA coordinator 汇总 | `f451219e98214b32180670ff35d1deaf68d5a29bd8e5777931b2a740d78d0917` |
| 原 900 秒进度诊断汇总 | `00cfb8186b3ab62bfa0a1da766d906a475bac28adce07554920744dbc6b85f81` |
| NoDelay ABBA 汇总 | `014bf0ddc01c5c3b14d6367dda6393030e18eaf6f93a221f2c4377f3c0946440` |

归档索引分别记录原始输入和安全归档哈希；路径去本机前缀、移除精确对象名称的副本不冒充原始字节。所有诊断均为 diagnostic_only，accepted_final=false。

## 下一轮前固定的计算

```text
已返回 70 项累计                     889.628586747 s
剩余 6 项 × 已观测非聚合用例最大值     6 × 27.1 = 162.6 s
工作量估算                           1052.228586747 s
加 20% 宿主及执行余量                 1262.6743040964 s
向上取 300 秒整数倍                   1500 s
```

剩余六项来自原 76 项身份集合：两个 ConstantCharacterTrim、一个 NclobTrim、StringDateTimeAndGuid、StringCompare 和 SequenceCurrval。27.1 秒仅用于有限预算估算，不新增单用例时长断言，也不以历史耗时证明当前正确性。

## 验收约束

仅 `(shared, functional)` 使用 1500 秒。unit 600 秒、其他 lane 900 秒、audit 90 秒、嵌套 CLI 300 秒及 TLS 资源预算不变；不提供环境变量或任意命令行覆盖。每次执行记录冻结的策略、profile/lane、host_timeout_seconds、timed_out、host_exit_code 和 actual_elapsed_seconds，验证器核对一致性。

产品每次连接／命令 20 秒、原 76 项身份、1266 次聚合 seed、SQL、payload、断言、自动保存点及失败收口均保持。真实单调用超时、协议无进展、结果错误或 fresh 清理失败仍使验收失败。1500 秒再次失败时停止调查，不自动放大预算。

工具和本修订进入新的源码冻结、唯一候选版本及来源清单；最终离线、TLS、shared 和固定 EF 门使用同一新包重新验收。新结果只能写为“修订宿主预算下完整 76 项通过”，不能写为“原 900 秒通过”或“性能问题已修复”。在这些门完成前，完整 R3 仍未结束，production_release_accepted=false。
