# W.DmProvider：面向 coding agent 的实现规范 v1.0

日期：2026-09-28。状态：**原始设计规范，不作为当前能力声明**。T01–T12 的限定 R1 验收及 PR #1 合并已完成；T13–T14 的限定 R2 async preview 已独立验收；T15–T25 未启动。实际进度以 [progress.json](../progress.json)、任务报告和 [R1 兼容矩阵](../../compatibility/R1.md) 为准；本目录初始任务状态不表示需要重新执行已验收任务。

## 1. 适用范围和来源

目标仓库：`whynpc9/W.DmProvider`。调查基线 commit：`860988296cb95deeb600328cb7bcd2160237a94f`。
下游仓库：`whynpc9/W.EntityFrameworkCore.Dameng`。已审阅的契约基线：`8dab4c0205f21fae4c8ca06fcbc39450eae2fe42`。
官方包基线：`DM.DmProvider 8.3.1.47463`，实际 EF10 资产 `lib/net9.0/DM.DmProvider.dll`。

最新驱动提交新增了 net9.0 反编译树和对照报告；报告记载相对 net8.0 仅有构建与编译器生成差异。该报告可作为静态基线，**不替代重新编译后的动态等价性测试**。[R01]

本规范只建设 ADO.NET 驱动。EF 查询翻译、模型、迁移和完整 EF 测试仍在下游仓库。保留受控 fork，生产包不得依赖官方 DLL；不采用长期 Harmony patch，不从零重写所有协议。

本文中的 MUST/必须为验收要求；SHOULD/建议允许通过 ADR 说明替代方案。类型名称属于建议的内部设计；公开 API 的具体签名必须用锁定的 .NET10 reference assemblies 编译验证。除明示的框架 API 外，本文 API 草图不是现有库的可调用接口。

## 2. 第一批必须补全的特性

| Spec | 内容 | 最早交付 | 关键约束 |
|---|---|---|---|
| S01 | 可编译恢复、来源追踪、独立包 | R0 | O/R/W 三方差异可解释 |
| S02 | 公共 API、不可变配置、类型身份 | R1 | 不接受哑配置，不暴露默认管理员凭据 |
| S03 | 会话状态机与执行所有权 | R1 | 单连接单活动执行；所有收发统一入口 |
| S04 | 网络、报文边界、TLS、原生依赖 | R1/R2 | 不猜协议；认证失败关闭；有长度上限 |
| S05 | 命令、Reader、多结果和 EF 保存契约 | R1 | DML+回读是第一批能力，不等 DbBatch |
| S06 | 参数、类型和精度 | R1 | 显式类型优先；禁止静默数值损失 |
| S07 | 本地事务、隔离级别和保存点 | R1 | Dispose 不提交；未知提交结果不重放 |
| S08 | 真异步、取消、超时和错误分类 | R2 | 网络等待不阻塞；取消后恢复或销毁 |
| S09 | DbDataSource、池化和会话复位 | R3 | 未证明复位安全就销毁，不污染下一租约 |
| S10 | 流式 LOB | R3 | 分块、受限内存、严格生命周期 |
| S11 | 数组绑定、批处理和 DbBatch | R4 | API/网络往返/原子性分开验证 |
| S12 | 诊断、测试、CI、发布与下游 | 全程 | 没运行数据库测试不能算已验证 |
| S13 | 后续特性研究门槛 | 后续 | FLDR、HA、原生取消、复杂类型、分布式事务不自动开放 |

## 3. 发布切片

**R0 — internal restored baseline**：只供隔离比较，不面向业务发行；允许保留上游已知行为用于对照。恢复构建和行为修复分开提交。

**R1 — correctness preview**：独立 `W.DmProvider`，.NET10；单节点；参数化查询和下游现有保存 SQL；常用类型；本地事务；清晰的保存点边界；安全传输策略；未开放的能力明确拒绝。同步语义可作为这一版临时公开边界，必须标注 Async 尚未达到 R2。池默认关闭。

**R2 — async preview**：已声明范围内的 Open、Execute、Read、NextResult、事务和网络清理真正异步；精确定义取消、超时及未知结果；不要求服务器原生取消。

**R3 — production candidate**：增加经过验证的池化/复位与流式 LOB、实际部署 OS/RID 测试、故障/长时测试、最终 nupkg 的 EF 下游验证。仅支持已验证的服务器/配置组合。1.0 不等于官方全部功能。

**R4 — throughput extensions**：经过独立门槛的数组绑定、批处理、DbBatch。DbBatch 不是 R1–R3 的前置条件。默认不自动把 SQL 拆为分号片段。

## 4. 实施次序

先阅读 `AGENT-START.md`。`tasks.json` 是机器可读任务及依赖表；每个任务一项可审阅变更，不允许 agent 一次完成所有 spec。

主线：S01 → S02 → S03 → S04基础 + S05 + S06 + S07 → R1 → S08 → R2 → S09/S10 → S12发布 → R3。
S11 的协议验证可在 R2 后独立做；S13 不能悄悄混入前面的 PR。

S03 是所有网络入口的共同约束，包含 Legacy SQL、MSG<T>、LOB、事务、握手、验证与复位。不允许主路径新架构、旁路仍访问旧 socket。

R1 阶段先统一状态和边界，可以暂用经加固的同步 transport；S08 替换该单一缝隙，并逐调用链清除 async fallback。不要为做 R1 强迫提前完成整个异步重写。

## 5. 全局不变量

1. 未显式 Commit 的本地事务不得被 Dispose/Close/入池流程提交。
2. 一条物理连接任何时刻只有一个执行拥有者；拥有者可以是 command/reader/事务控制/复位。拥有者内部的多个 wire 操作也不得并发。
3. public reader 获得所有权直到关闭；读完一页或调用 ExecuteReaderAsync 返回，不释放会话所有权。
4. 取消回调只能作用于捕获的 `(sessionId, leaseGeneration, executionId, invocationId)`，不能作用于后来复用该逻辑对象的操作。
5. 发送开始后发生不确定故障，不声称请求未执行。特别是 Commit 响应丢失，不重放 SQL/Commit。
6. 所有收包长度、计数、偏移和算术都经过校验；未知 opcode/不支持安全模式不得当成功跳过。
7. config 不可变且按连接/数据源隔离；静态注册表可以并发安全地管理对象，不能用静态字段承载某个租户的可变配置。
8. 参数显式类型、规模和精度优先；用户值不得经 double 中转而失去十进制精度。
9. 标准能力标志只报告已实现的 API；不能因存在常量或反编译类型就声称支持。
10. SQL、值、凭据、握手材料默认不进入日志、测试制品或公共仓库。
11. 不能以修复驱动为名给服务器增加不存在的事务隔离/DDL 原子性。
12. 下游 EF 既有 SQL 不为方便驱动 MVP 被改写；需要改变必须是独立下游 PR，并记录新契约。

## 6. 证据与已知边界

每项能力使用 `not_started / offline_verified / integration_verified / downstream_verified`。协议不明时先建立 probe；有离线结果但缺 DM 环境可继续其他不依赖任务，但不能将集成门槛标为完成。

三种测试分别保存：`Contract`（应该正确的行为）、`OfficialCharacterization`（官方特定版本的观察，包括缺陷）、`Improvement`（新驱动目标）。官方预期失败不能成为新驱动必须失败的断言。

任意疑似漏洞或错误先以静态问题登记，必须用最小复现区分反编译恢复差异、客户端错误、服务器限制。原始反编译 C# 中重名成员/编译器产物不能仅靠猜名字恢复；必要时回看 IL。

## 7. 文件说明

`specs/`：分特性实现规范，包含作用范围、改动入口、实现要求、验收与禁止事项。
`contracts/`：交接与测试清单。
`tasks.json`：可分配给 agent 的任务 DAG。
`AGENT-START.md`：直接粘贴给 coding agent 的启动指令。
`SOURCES.md`：固定版本来源与框架官方 API。
`IMPLEMENTATION-SPEC.md`：上述 Markdown 的单文件汇总，方便一次阅读；以拆分文件为编辑主本。
