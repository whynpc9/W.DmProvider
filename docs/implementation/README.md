# W.DmProvider 开发准备

> 状态：准备与迁入记录，2026-09-29。正式实施依据是 [v1 原始规范包](v1/README.md)。本文先于附件迁入，依据仓库 `860988296cb95deeb600328cb7bcd2160237a94f` 和对话《规划Dm Provider开发》整理；与 v1 不一致时，以 v1 的任务、spec 和验收要求为准。

## 规范包入口

- [实际实施进度](progress.json)、[T01 报告](reports/T01.md)、[T02 报告](reports/T02.md)、[T03 报告](reports/T03.md)：任务执行状态和验证证据独立于原始规范包保存。
- [T04 报告](reports/T04.md)：配置、安全默认值、能力守卫及框架边界的独立验收。
- [T05 报告](reports/T05.md)：物理会话、执行所有权、Reader/LOB 并发隔离和故障清理的独立验收。
- [T06 报告](reports/T06.md)：单次建连、短读写、帧校验、解码预算和统一期限的独立验收。
- [T07 报告](reports/T07.md)：证书/主机名校验、真实 TLS 模式矩阵及 native 释放边界的独立验收。
- [T08 报告](reports/T08.md)：命令快照、多结果、有限清理及实际 NuGet 包保存 SQL 契约的独立验收。
- [T09 报告](reports/T09.md)：参数类型来源、精确数值、Unicode 和时间精度的独立验收。
- [T10 报告](reports/T10.md)：本地事务 outcome、保存点、有限回滚和 DDL 边界的独立验收。
- [T11 报告](reports/T11.md)：隔离级别所有权与确认修复、正常公开入口及实际包 EF 保存验收。
- [T12 / R1 报告](reports/T12.md)：实际候选包、固定 EF 接入、CLI 与当前 schema 脚本验收；[R1 兼容矩阵](../compatibility/R1.md)。按用户指令停在 R1，T13–T25 尚未启动。
- [v1 README](v1/README.md)：产品范围、发布切片和全局不变量。
- [任务 DAG](v1/tasks.json)与[任务依赖表](v1/contracts/task-plan.md)：25 项实施任务。
- [分项 spec](v1/specs/01-baseline-and-build.md)：S01–S13，从构建恢复到高级能力门槛。
- [验收清单](v1/contracts/acceptance-cases.json)：98 条验收要求。
- [agent 启动指令](v1/AGENT-START.md)与[报告模板](v1/contracts/task-report-template.md)。
- [单文件汇总](v1/IMPLEMENTATION-SPEC.md)：便于通读；编辑以拆分文件为主。

原 ZIP `W.DmProvider-implementation-specs-v1.zip` 于 2026-09-29 从用户提供的下载文件迁入 `v1/`。ZIP SHA-256：`4fd0d5aa4ce5f60cdc61ed57f550a6fe261b83bfd53627c9cff1715bc1be52de`。迁入前核对了 24 个文件的路径、大小和 [artifact-manifest.json](v1/artifact-manifest.json) 中的逐文件 SHA-256；任务与验收数量分别为 25 和 98。规范包原件内容未改动。

## 产品与仓库边界

`W.DmProvider` 负责 ADO.NET API、参数与结果类型、物理会话、事务、协议、传输、连接池、取消、超时和驱动诊断。`W.EntityFrameworkCore.Dameng` 继续作为独立仓库，负责 EF Core 10 的模型、SQL 翻译、更新与迁移；其现有功能测试是新驱动的首个下游验收入口。驱动生产项目不引用 EF。

任务开始前，仓库只有官方 NuGet 包、解包资产、net8.0/net9.0 反编译树及分析文档。反编译工程文件由工具生成，不能把它存在等同于自有驱动已恢复编译。首先以实际下游使用的 net9.0 资产为对照，保留 net8.0 报告作为参考；[静态对照报告](../reverse/net9-verification.md)不能代替构建、运行或真实达梦实例验证。新增工具与实际结果以进度记录和任务报告为准。

## 已从对话确认的实施方向

1. **T01 基线固化 → T02 可编译恢复 → T03 独立产品包**，依次推进。首轮 agent 只执行 T01。
2. 优先建立会话执行所有权，再推进 EF 保存契约、类型正确性和本地事务。对话明确提及后续重点 T05、T08、T09、T10；完整依赖和其余任务编号以待搬入的原始 `tasks.json` 为准。
3. Reader 从 `ExecuteReader` 返回直到关闭持续持有物理会话执行权。同一连接有活动 Reader 时，第二条命令和事务控制默认快速失败；不在这一阶段实现 MARS。
4. 取消的第一版设计是中止目标物理会话；请求发送后无法证明协议恢复时将会话标记 Broken，不回池。提交响应丢失要报告结果未知，不自动重放；迟到的取消回调不能影响新租约。
5. 真异步需要覆盖打开、执行、Fetch、下一结果、事务及 LOB 的网络调用链，并用会拒绝同步 I/O 的假传输层证明；`Task.Run` 包同步 I/O 不算完成。
6. 先承接下游已有的保存 SQL、影响行数与生成值回读契约。不能用 `Split(';')` 切组合 SQL，也不能在 DML 与 `SQL%ROWCOUNT` 回读之间插入验活语句。
7. 池中会话只有在恢复基线已验证时才复用，否则销毁；LOB 的 `GetStream` / `GetTextReader` 必须按需读取；不同 SQL 的原生 `DbBatch` 需先证明协议、结果归属和失败语义。

以上为**待实现设计**，不是当前驱动能力。对话中提出的产品选择还包括 `W.DmProvider.dll` / `W.Dm`、无默认管理员凭据、拟定默认 `RequireTls`、拟定默认命令超时 30 秒、未完成事务释放不隐式提交。这些改变在编码时必须形成兼容性差异记录，并通过下游接入验证。新配置名不能表述成官方驱动现有选项。

## 证据与交付门槛

每项能力分别记录 `reported`、`static-confirmed`、`reproduced`、`fixed`、`regression-verified`。现有逆向报告主要提供静态证据。离线构建或单元测试不代表真实达梦集成通过；无数据库时明确记为 `integration_pending`。最终发布仍需跨仓库 EF 回归、版本化包消费和真实目标环境验收。

首轮交接见 [v1/AGENT-START.md](v1/AGENT-START.md)，规范来源见 [v1/SOURCES.md](v1/SOURCES.md)，本地文件校验值见 [SOURCES.md](SOURCES.md)。

## 搬运范围

ZIP 内已有 `IMPLEMENTATION-SPEC.md` 单文件汇总，因此无需另复制对话中同内容的独立 Markdown。更早的 `W.DmProvider-repository-and-roadmap.md` 不在本次提供的 ZIP 中；v1 README 已包含仓库边界、发布切片和实施次序，后续若要保留那份历史规划原件，可单独补入并标注其早于 v1 的决策。
