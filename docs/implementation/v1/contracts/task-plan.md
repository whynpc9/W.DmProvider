# 任务依赖与执行表

全部任务当前均为 **not_started**。这是实施计划，不是代码进展表。任务编号不是死板串行号，依赖字段才是准入条件。

| ID | 任务 | 前置任务 | Release | 交付 |
|---|---|---|---|---|
| T01 | 固定资产、来源和O/R/W运行清单 | 无 | R0 | 生成真实哈希/MVID/工具版本；保护只读快照；明确实际net9资产。 |
| T02 | 恢复可编译R基线 | T01 | R0 | 锁定SDK、资源、IL对照和必要机械恢复；记录所有恢复差异。 |
| T03 | 创建独立W包与公开API差异清单 | T02 | R1 | 独立assembly/namespace/package，候选pack与依赖隔离，迁移工作树。 |
| T04 | 实现不可变配置和能力守卫 | T03 | R1 | 别名单位表、默认值、显式安全策略、哑选项拒绝和脱敏。 |
| T05 | 统一会话状态和执行所有权 | T04 | R1 | 唯一session/generation/execution/invocation身份及全部wire入口清单。 |
| T06 | 加固传输与帧边界 | T05 | R1 | 短读短写、长度/CRC/EOF防御、单次TCP建连、统一期限入口。 |
| T07 | 修复TLS和原生资源安全边界 | T06 | R1 | 证书链/主机名校验、拒绝降级、native加载及失败释放。 |
| T08 | 接通命令、Reader和EF保存契约 | T06 | R1 | 保留DML+影响行数/键回读；多结果、早关闭、flags与资源。 |
| T09 | 修复参数与类型精度 | T08 | R1 | 显式类型来源、enum/unsigned/decimal、Guid文本、时间与LOB绑定。 |
| T10 | 本地事务和保存点 | T08 | R1 | Dispose回滚尝试、outcome状态、Save/Rollback/Release标准API及守卫。 |
| T11 | 定位隔离级别SaveChanges失败 | T09, T10 | R1 | 输出ADO最小复现矩阵和实证根因；能确定则修复并加最终状态测试。 |
| T12 | R1下游交接与正确性门槛 | T07, T09, T10, T11 | R1 | 形成真实candidate nupkg与EF handoff；分离官方行为/正确契约/改进。 |
| T13 | 端到端异步调用链 | T12 | R2 | 统一async core，所有声明Async路径贯通，fake transport拒绝同步I/O。 |
| T14 | 取消、期限和未知结果竞态 | T13 | R2 | 代际取消、deadline、异常分类、AbortPhysicalSession、未知提交不重放。 |
| T15 | 验证会话复位能力 | T14 | R3 | 实际探测reset范围/权限/状态残留；输出verified reset或discard决策。 |
| T16 | 实现DataSource与池容量/复用 | T15 | R3 | 独立池+受控registry、取消队列、代际清理、VerifiedResetOrDiscard。 |
| T17 | 流式LOB读写 | T14, T09 | R3 | forward-only流、无Length输入、跨字符分片、父reader生命周期与内存上限。 |
| T18 | 标准诊断与长时故障/资源测试 | T16, T17 | R3 | 低基数可观察性、敏感数据检查、线程/句柄/内存、测试manifest。 |
| T19 | R3候选包下游与发布材料 | T18, T25 | R3 | 最终nupkg、精确兼容矩阵、EF集成证据、SBOM/来源和回退说明。 |
| T20 | 数组绑定和批次协议探测 | T14 | R4 | 实际RT、边界、结果归属、事务/错误矩阵；支持/拒绝/未知清楚。 |
| T21 | 实现受控数组绑定 | T20 | R4.Arrays | 固定DML模板+分块参数组、显式事务、每行已知状态与失败索引。 |
| T22 | 实现经验证的原生DbBatch | T20 | R4.NativeBatch | 工厂和DbBatch API、结果/参数隔离、事务归属、早关闭及取消。 |
| T23 | R4性能与EF批次交接 | T19, T21 | R4 | 同语义RT/吞吐/分配对照；下游单独批次PR交接。 |
| T24 | 后续高级能力分项研究 | T14 | Later | 每次只选择一项：原生取消/FLDR/缓存/HA/ambient/复杂类型/AOT，产出probe与门槛。 |
| T25 | 独立逆向验证与边界审阅 | T18 | R3 | 从调用者视角误用/取消/超时/事务失败/复位状态污染测试与审阅记录。 |

T22是有证据门槛的任务；原生batch未知时保持未启用，不妨碍数组绑定独立发布。T23若包含原生batch声明，必须再满足T22。T24每次只派一个研究主题，不是一次全做。

建议首先只派 T01；T02是编译恢复，T03才是独立产品身份。不要合并为一次无法审阅的全库改写。
