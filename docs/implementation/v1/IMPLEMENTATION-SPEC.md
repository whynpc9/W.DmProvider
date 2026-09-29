# W.DmProvider 实现规范 v1.0 · 单文件阅读版

2026-09-28 | 13份特性规范 · 25项任务 · 98条验收要求

**此文件是待实施spec，不是已完成驱动代码或实测报告。拆分版与JSON任务清单见同名ZIP包。**

---

<!-- source: README.md -->

# W.DmProvider：面向 coding agent 的实现规范 v1.0

日期：2026-09-28。状态：**待实施的设计规范，不是已实现或已验证的能力声明**。

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

---

<!-- source: AGENT-START.md -->

# Coding agent 启动指令

把本目录放入 `W.DmProvider/docs/implementation/v1/`；它是文档，不是已实现代码。

```text
你正在实现 W.DmProvider，一个基于 DM.DmProvider 8.3.1.47463 的受控衍生 ADO.NET 驱动。
先读 docs/implementation/v1/README.md、SOURCES.md、tasks.json，再读本次任务引用的 spec。
调查基线是驱动 commit 860988296cb95deeb600328cb7bcd2160237a94f；默认部署资产是 net9.0，目标新包是 net10.0。
若当前仓库比基线新，先读取 git status、最近变更和现有实现，保留用户未提交改动，不重置或覆盖它们。
只执行本次指定任务；没有指定时，选择 tasks.json 中依赖全部满足且 activation_gate 已满足的最早一个未完成任务；初始为 T01。

本次目标：完成 <TASK_ID>。
1. 查明现有入口和所有调用方；写出本次会改哪些文件及哪些行为不变。
2. 先补测试（或构建恢复记录），再做最小实现。来源快照只读，产品代码只在 src/ 修改。
3. 每条新网络路径均服从 S03；不要绕开执行所有权，不用 Task.Run 包同步 I/O，不猜 opcode。
4. 不修改 EF 仓库来绕过驱动失败；所需下游改动写入 handoff，不隐式推送另一仓库。
5. 不使用生产数据库。只读 DAMENG_TEST_CONNECTION_STRING 和显式传入的测试实例配置；不打印连接串。
6. 单元/协议测试必须实际运行；有数据库配置时再运行对应真实库测试。没有数据库环境时报告 integration_pending，绝不伪造通过。
7. 每完成一项填写 contracts/task-report-template.md：改动、命令与退出码、通过/失败/跳过、证据、行为差异、剩余阻塞。
8. 提交粒度按任务，编译恢复、重命名、行为修复不得混成一笔；禁止把所有 spec 一次性做完。
9. 如果一个协议能力没有证据，只实现明确的 Unsupported 路径和 probe。不要以 mock 成功替代真实能力。
10. 任务结束不得直接发布 NuGet、创建 release 或修改远程配置，除非用户另有明确授权。
```

## Agent 的停止条件

- 开始需要更改未授权的公开 API/下游 SQL：先补 ADR 或 handoff，不跨任务修改。
- 需要猜测认证、取消、批处理、复位报文：将该路径保持禁用，产出能执行的验证工具及缺失证据。
- 发现测试基线本身断言了上游缺陷：归为 OfficialCharacterization，不删除记录，也不强迫新驱动复刻。
- 没有真实库不意味着不能完成离线代码，但意味着不能通过真实库或发布门槛。

## 每次交付的格式

任务编号与状态；实现文件；变更前后行为；执行过的测试命令与结果；未执行部分及原因；新增证据文件；下游影响；是否触及安全/事务/池化边界。不要只输出“全部完成”。

---

<!-- source: contracts/task-plan.md -->

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

---

<!-- source: specs/01-baseline-and-build.md -->

# S01 — 来源基线与可编译恢复

状态：实施规范。优先级：P0。依赖：无。关联任务：T01–T03。

## 1. 目标

将调查仓库恢复成可审阅、可重现构建的产品工程，同时保留官方证据。R0 不修复官方行为；W 才是产品。现有 `decompiled/net9.0` 与 `docs/reverse/net9-verification.md` 是起点，不重新做一轮全面逆向。[R01]

## 2. 目录与构建

```text
upstream/DM.DmProvider/8.3.1.47463/
  manifest.json
  decompiled/net9.0/              # 只读来源快照
  public-api.txt
  source-map.json
src/W.DmProvider/
  W.DmProvider.csproj
  PublicApi/
  Internal/{Configuration,Sessions,Transport,Protocol,Execution,TypeHandling,Pooling,Transactions,Diagnostics,Legacy}/
  Native/
  Resources/
tests/{W.DmProvider.UnitTests,W.DmProvider.ProtocolTests,W.DmProvider.IntegrationTests}/
tools/{DmProbe,UpstreamSnapshot}/
eng/{build,test,verify}/
```

最初允许 W 内大量代码保留在 Legacy；不要为了目录整齐进行无意义大规模重写。R 基线可以使用固定 Git tag/worktree 构建，不要求长期复制第二套可修改源码到 main。三个被测实现必须独立进程运行。

`AssemblyName=PackageId=W.DmProvider`，新公开 namespace `W.Dm`，生产目标 `net10.0`。使用锁定稳定 SDK，禁止继承反编译 csproj 的任意 LangVersion 或继续伪装官方程序集身份。资源的 LogicalName 是明确映射，不以全局字符串替换误改资源查找名。

## 3. 必须产生的清单

manifest 记录包 id/version、原 nupkg SHA-256、net9.0 DLL SHA-256/MVID/程序集版本、卫星资源列表、实际 TFM、反编译工具版本/参数、源码 commit。缺失字段为 null+原因，禁止写猜测值。所有哈希由工具计算，不把 Git blob SHA 当 SHA-256。

source-map 每个恢复过的类/方法记录原 assembly/type/signature、原路径、恢复路径、改名及原因。机械恢复导致的语义可疑点必须回读 IL 并标记需要差分测试。

public-api 清单包含可见类型与成员；W 只承诺 S02 的 surface，额外上游 public 成员逐一登记为 preserved/tested、obsolete、internalized 或 unsupported。对已存在但未覆盖的危险入口（HA、FLDR、自动重连、XA）在 W 中加入明确守卫，不能因为 fork 复制了它们就被默认启用。

许可证与原生依赖按精确资产保留来源、归属及修改声明；包声明的 Apache-2.0 不能替代单独原生库的分发审核。[R09] 本 spec 不作未取得资产的分发授权结论。

## 4. O/R/W 差分运行器

`DmProbe` 以场景 ID 驱动官方 O、恢复 R 和工作 W。它输出 JSON，不输出连接字符串。

```text
scenarioId, implementation, driverAssetHash, sourceCommit,
runtime, serverIdentity, configurationFingerprint,
observedResult, exceptionKind, finalDatabaseState,
evidenceLevel, passed, skippedReason
```

实现适配器只将同一场景编译到各独立宿主，不在同一进程加载三套驱动。配置指纹使用不暴露凭据的不可逆、进程/运行隔离标识；不得提交含密码连接串的散列供离线猜测。

场景最少：Open/Close、参数 SELECT、Unicode CRUD、BEGIN/Commit/Rollback、DML+影响行数、DML+生成键、失败后下一条 SELECT、LOB、已知官方缺陷。对正确场景 O≈R；对于 O 错误行为，R 可保留作为研究基线，W 按 approved-differences.json 改善。

不能宣称反编译 C# 相同即运行行为绝对相同；R 恢复是新编译资产，仍需差分。

## 5. 验收

BAS-01：干净 checkout 在锁定 SDK 下 restore/build/test/pack 可执行。
BAS-02：官方包与产品依赖树隔离，最终 W nupkg 不依赖/不嵌入 DM.DmProvider.dll。
BAS-03：资源 en/zh-CN 等已保留范围能正常读取，不混入未授权原生库。
BAS-04：O/R 差异清单无未解释的功能变化；没有 DM 环境时只标 offline_verified。
BAS-05：根目录/产物不含测试连接串、数据库口令、证书私钥或原始认证报文。

## 6. 不做

不逐项修所有上游缺陷；不全库启用 warnings-as-errors 迫使噪音性重写；允许 Legacy 的有理由告警豁免，但新文件 nullable 与分析规则严格。不要同时扩展 netstandard/net48、多版本 EF 或 NativeAOT。

---

<!-- source: specs/02-api-and-configuration.md -->

# S02 — 公共契约、配置和能力声明

状态：实施规范。优先级：P0。依赖：S01。任务：T04。

## 1. 第一批公共 API

`W.Dm.DmConnection : DbConnection`、`DmCommand : DbCommand`、`DmDataReader : DbDataReader`、`DmParameter : DbParameter`、参数集合、`DmTransaction : DbTransaction`、`DmConnectionStringBuilder`、`DmClientFactory : DbProviderFactory`、`DmException : DbException`、`DmDbType`。

保留常用类名及 `DmParameter.DmSqlType` 命名，降低 EF 接入工作；类型实际属于新程序集，不承诺二进制替换。公开 API 均生成快照测试。`DmDataSource : DbDataSource` 在 S09 引入，但不可先依赖一个空壳池。[F03]

可保留恢复后的 `DbDataAdapter` 能力用于兼容，但不进入第一批支持承诺；未测试高级入口必须明确禁用。DbBatch 工厂能力在 S11 完成前返回 false/NotSupportedException，禁止提供只能 throw 的 batch 后宣称支持。

## 2. 配置模型

Builder 可变；连接或 DataSource 构造/设置完成后生成内部不可变 `DmConnectionSettings`，只允许 Closed 状态替换连接配置。Open 中修改配置抛 InvalidOperationException。已打开物理会话永远绑定一个 settings snapshot。

不再使用全局可变 loglevel/logdir、ConnPoolFilter.Instance setter 或共享 filter.next。需要管线时每条会话持有自己的不可变拦截列表；第一版仅保留必要诊断，不保留约 150 方法的复制式责任链。

## 3. 默认值（本产品决策，不是官方事实）

| 设置 | 新默认 | 单位 / 规则 |
|---|---|---|
| Host/Server、User、Password | 无管理员账号默认值 | 认证信息不完整在发 LOGIN 前明确拒绝 |
| Port | 5236 | 1–65535；显式服务端配置可覆盖 |
| ConnectTimeout | 5 秒 | 内部 TimeSpan，0 表示无限，负值拒绝 |
| PoolAcquireTimeout | 5 秒 | 和 ConnectTimeout 分开计时 |
| CommandTimeout | 30 秒 | DbCommand 标准属性以秒计，0 无限 |
| ReadIdleTimeout | 0 | 默认关闭；不能覆盖 CommandTimeout |
| CleanupTimeout | 5 秒 | 必须有限；不使用已取消的用户 token |
| Pooling | false | S09 完成前 true 明确拒绝 |
| MaxPoolSize | 100 | 只在显式开启池后生效 |
| MaxMessageSize | 64 MiB 总 wire frame（含头） | 可配置；不能突破已验证协议硬限制 |
| MaxMaterializedLobSize | 64 MiB | 对 string/byte[] 全量读 API 的分配保护，S10详述 |
| LobChunkSize | 32 KiB 请求目标 | 实际按协商、协议和配置约束取最小值，不当作协议常量 |
| TransportSecurity | RequireTls | 需要完整链路 TLS 且校验证书；不默默降级 |
| AutoReconnect/ReadWriteSplit/Enlist | false | 暂不实现，显式 true 抛 NotSupportedException |
| StatementCache | disabled | 复位/语句所有权验证前不开放 |

PlaintextAllowed 是显式连接策略，用于允许经批准的非 TLS 环境；并不关闭协议认证要求，也不跳过已协商 TLS 的证书校验。测试明文 DM 实例必须显式配置它。不要为保持旧连接串“无感”而自动接受不可信证书。TLS 细节见 S04。

原 `command_timeout` 默认无限改为新默认 30s、取消语义、安全策略与类型变化，全部进入 breaking-changes 文档。用户明确传入 0 时尊重无限；不能因新默认覆盖用户值。

## 4. 原连接串别名与单位

`server/host`、`user/user id/uid`、`password/pwd`、`schema`、`connect_timeout`、`conn_pool_timeout`、`command_timeout`、`socketTimeout`、`conn_pooling` 等常见别名由表驱动解析。保留原下划线 timeout 键的已确认输入单位（connect/pool/socket 为毫秒，command 为秒），转换为 TimeSpan；不要把同一旧键静默改单位。[R02]

`DbConnection.ConnectionTimeout` 的公开 int 返回值使用秒；旧连接串毫秒输入先转换内部 TimeSpan，再按文档约定向上取整显示非零秒值（0仍为无限），不得直接把毫秒字段透传为标准属性。

每个别名的状态为 supported / unsupported / deprecated。未知键默认报错，unsupported 非默认值报错；禁止接受后不生效。单位不明的旧别名不能凭猜测映射，先做 probe。对重复或别名冲突、大小写、空值、分号引号转义、IPv6、Unicode 密码均有确定性测试；同一语义重复赋值若值冲突应报错，避免“最后一个值胜出”隐患。

`InitialCatalog` 不映射到 User/Password。DM 不支持的物理 database/catalog 切换明确拒绝；`Schema` 是独立选项，不能假装同一语义。

Builder 的密码读取符合其配置用途，但默认 ToString/日志不作为脱敏 API 使用。专用 `ToRedactedString()` 与诊断 DTO 禁止输出秘密；`DmConnection.ConnectionString` 打开后遵循 PersistSecurityInfo=false 的脱敏契约，内部保留创建新物理会话所需秘密而不从公开脱敏字符串恢复配置。

## 5. 能力声明

内部 `DmServerCapabilities` 来自已验证握手/版本和明确配置，不能仅 `serverVersion >= X` 猜新能力，也不能为探测而偷偷执行 DDL。能力分开命名：ResultSets、Savepoints、NativeCancel、ArrayBind、NativeBatch、SessionReset、LobStreaming 等。

性能能力与 API 可用性独立。支持 ExecuteReaderAsync API 不代表 R2 真异步通过；支持多语句不代表 DbBatch；单次发送不代表原子事务。公开支持矩阵按版本明确列出，内部 flag 不作为隐藏许诺。

## 6. 验收

CFG-01：两个连接设置不同 schema、语言、池大小、日志选项，互不影响。
CFG-02：旧毫秒键/新 TimeSpan 经不同写法得到一致内部期限；非法/溢出拒绝。
CFG-03：InitialCatalog 不能更改凭据；没有 SYSDBA 默认口令。
CFG-04：未知/哑选项不悄悄通过。
CFG-05：W 类型可和官方类型在不同测试进程共存；应用 API 不伪装官方 assembly。
CFG-06：R1 未完成 Pooling/DbBatch/Enlist/HA 时明确拒绝开启。
CFG-07：所有配置输出路径、异常插值、诊断无原始秘密。

---

<!-- source: specs/03-session-and-ownership.md -->

# S03 — 物理会话、执行所有权与生命周期

状态：实施规范。优先级：P0。依赖：S02。任务：T05。此规范先于所有网络现代化。

## 1. 现有入口及改造方法

起点：`DmConnInstance`、`A/B.cs` 的旧 SQL 收发与新 `MSG<T>`、`A/D.cs`、`DmCommand`、`DmDataReader`、`BaseFilter`。[R03]

先建立唯一会话协调器，把既有协议实现作为内部 codec/adapter 使用。先改生命周期与入口，不要求一口气重写编解码。所有调用 socket/SSL 的位置建立 inventory；事务、LOB、Prepare、Schema 查询、pool validation/reset 也在 inventory 内。

## 2. 三个独立状态模型

**逻辑 DmConnection**：Closed → Opening → Open → Broken/Closed → Disposed。Broken 的物理会话不可复活；用户必须 Close 后显式 Open 获取新会话，不自动延续事务。Open 失败对外回到 Closed，内部损坏 transport 已销毁。StateChange 只在外部状态变化时触发且不在状态锁内调用用户代码。

**物理 DmSession**：New → Connecting → Authenticating → Ready → Busy → Ready；Ready → Resetting → Ready；任意存活状态 → Broken → Closed。Closed/Broken 不得重新 Ready。Busy 的 purpose 是 Query/Reader/TransactionControl/Metadata/Lob；事务是否活跃是独立状态，不能简单等同于 Busy。

**本地事务**：None、Starting、Active、Committing、Committed、RollingBack、RolledBack、CompletedExternally、OutcomeUnknown。未知结果和已完成不被 Dispose 状态遮蔽；详情见 S07。

## 3. 所有权对象

```csharp
// 设计草图，不是已实现 API。
internal readonly record struct OperationIdentity(
    long SessionId, long LeaseGeneration, long ExecutionId, long InvocationId);

internal sealed class DmSession { /* 唯一 transport、状态、执行 gate */ }
internal sealed class DmExecutionLease : IDisposable, IAsyncDisposable
{ /* command/reader 持有；幂等释放，不能复制 */ }
internal sealed class DmInvocation : IDisposable
{ /* 一次公开方法/内部清理的期限、取消注册和发送阶段 */ }
```

SessionId 标识物理连接且永不复用；LeaseGeneration 每次 pool checkout 增长；ExecutionId 每个逻辑执行增长；InvocationId 用于 ExecuteReader 与后续 Read/NextResult 等不同调用。计数器溢出必须拒绝/销毁，不循环回旧身份。

### 获得与转移

- `Execute*` 以原子检查获得执行 lease；如果已有 reader/command/事务控制占用，**立即抛 InvalidOperationException**（可有自定义子类），不等待另一条用户命令。
- `ExecuteReader` 成功时把 lease 转交 reader；方法返回不释放。`Read()==false` 或最后 `NextResult()==false` 也不允许另一命令抢先执行，直到 reader 关闭。
- Reader 的 Fetch/LOB 操作复用该 lease，只产生 child invocation；绝不递归调用需要重新获取根 lease 的 public command 方法。
- Commit/Rollback/Savepoint 与 reader 并发使用属于调用错误，立即拒绝；连接/事务 Dispose 为清理可走独立关闭路径，不以普通操作竞争造成死锁。
- 多个 child invocation 同时操作同一 reader/LOB，立即拒绝。父 lease 不意味着任意数量子请求可交织。
- `ExecuteScalar`/`ExecuteNonQuery` 负责完成结果处理或安全终止后释放 lease。

### 同步原语

使用短临界区维护状态、身份和 transport 引用；临界区内不做 I/O、不 await、不调用日志回调/用户回调。等待池容量属于 S09，和同一连接并发命令不是一回事。

不要只对每次 socket.Write 加锁：一次请求的 encode/send/read/decode/state-update 是同一个拥有者的操作；请求 A 的 response 不能被 B 读走。

## 4. 故障与取消的线性化

R1 中 `DmCommand.Cancel()` 也不得沿用上游 Reconnect：可提前使用物理会话销毁，或明确报告该取消路径尚不支持；不能等到 R2 才消除“取消会重连”的危险行为。

取消/关闭需在同一状态临界区中执行“校验身份 → 标 Broken/不可复用 → 分离目标 transport”。在锁外仅关闭捕获的 transport。不能先检查 generation 再晚些时候读取 `connection.CurrentSocket`，否则中间归还/重新租借仍可能误伤新请求。

归还池前必须满足：所有 invocation 完成或中止、取消注册已注销且其回调无权再影响会话、reader/LOB 失效、事务终结可知、复位验证成功。`CancellationTokenRegistration.Dispose` 可能等待回调，不能在回调所需的状态锁内调用。

若 abort 与成功 response 竞争，状态转换在同一个协调器判定。已经观察并原子完成的成功不被随后 token 取消改成 Unknown；反过来已标 broken 的执行不能由迟到响应恢复成 Ready。

## 5. 关闭与资源

`Close` 幂等；标记正在关闭，使旧 reader/LOB 无效；执行有界清理。明确无可靠协议恢复时关闭物理 transport。清理使用独立有限期限，不使用已取消用户 token。

`DisposeAsync` 真正等待需要的异步清理；R2 后不能用同步 Dispose 包 Task。同步 Dispose 可以阻塞在有界清理边界，但不能开启无限后台工作。

托管 command/reader 不使用 finalizer 做网络操作；SafeHandle 只处理其原生句柄。发生无法完成的最终清理，记录脱敏诊断并销毁，不能承诺回滚已确认。

## 6. 验收

SES-01：一个 reader 持有 lease，第二 command/Commit/同 reader 并行 Read 均快速失败，不写任何字节。
SES-02：reader 读取分块 LOB 不递归获取根 lease，无死锁。
SES-03：旧 SQL 与 MSG<T> 两条路径无法交织请求/响应。
SES-04：Cancel/Close 与返回池、再次租用精确交错，旧回调不能关闭新请求。
SES-05：Open/Close/Dispose 多次调用不重复释放、不重复发状态事件。
SES-06：异常与取消后的状态只能是证明 Ready 或已断开；不存在“socket断了但对象继续可用”。
SES-07：独立连接可并行，不因为全局锁被串行化。
SES-08：fault injection 检查每一条 wire 入口都需要有效 lease 或专用 handshake lease。

---

<!-- source: specs/04-transport-protocol-and-security.md -->

# S04 — 传输、协议边界、TLS 与原生库

状态：实施规范。优先级：P0/P1。依赖：S03。任务：T06、T07；异步接通见 S08。

## 1. 目标与切分

现有 `A/D.cs` 是 socket/SSL，`A/B.cs` 汇聚旧消息与 MSG<T>，`A/C.cs` 编解码，`Dm/MSG.cs` 是另一消息体系。报告记录 64-byte header、小端、STARTUP/LOGIN 以及版本相关 LOB/Execute 差异。[R03]

将其切成 `DmTransport`（字节收发）、`DmFrameReader/Writer`（帧边界与校验）、`DmProtocolSession`（请求/响应生命周期）、按消息的 codec。先共用受控入口，保留已验证 codec，后续按消息迁移，不按文件体积重写。

第一版不强制 System.IO.Pipelines。Socket+NetworkStream/SslStream 加受控 Memory<byte> 缓冲已足够；只有 benchmark 证明需要才追加 Pipelines。

## 2. 连接与握手

DNS → 单次 TCP 建连 → STARTUP 协商 → 按协议升级 TLS/建立认证 cipher → LOGIN → 必需初始化，全部共享一个 ConnectTimeout deadline；每一步失败释放前面全部资源。DNS 返回多个地址可在同一预算内逐一尝试，不能每次尝试重置总预算。

不得复刻成功 BeginConnect 后关掉 socket 再同步 Connect 的行为；一个成功 Open 只有一个最终认证 TCP 会话。配置目标主机用于证书验证，不能改为解析后的任意字符串或硬编码 DmProvider。

连接过程中取消不进入池；失败时外部 connection 回到 Closed。构造函数不做网络 I/O；Open/OpenAsync 才建立会话。

## 3. 帧读取规则

算法要求（具体字段按 opcode+协商版本验证，而不是盲信报告表格）：

1. 读取**精确**的已验证头长；正常 TCP Read 可能只返回部分数据。可使用 ReadExactly/ReadExactlyAsync 或等价循环。[F04]
2. 在分配 body 前解出合法长度；拒绝负值、overflow、超过配置上限的总帧；对 `header+body`、`count*width` 使用 checked。
3. 精确读取 body；中途 EOF 是截断协议，不是正常结果结束。
4. 按协商规则做 checksum/CRC、解密和长度验证，再交给响应解码器。不能把不同版本的校验流程混用。
5. 解密后的长度、结果列数、行计数、字符串长度、LOB locator 长度再次校验；嵌套复杂类型在未支持时拒绝，不无界递归。
6. heartbeat 是独立已识别帧，消费完整帧后继续等待业务响应；不得重置命令总期限，也不得把它当作任意错误帧忽略。

报告某些 header 字段出现重叠偏移；必须根据源代码与消息类型确认它们的条件含义，不能直接生成一个把所有字段当作同时有效的 packed struct。

Stream.WriteAsync 通常表示整个给定缓冲写完；若直接用 Socket.Send/SendAsync，必须循环处理 partial send。零进展不能无限循环。Memory/Span 在 await 边界的所有权清晰，绝不在归还池化内存后继续解码。

默认 64 MiB 是本产品保护值，不是声称服务器协议最大值；配置提高必须受经过验证的协议硬上限和内存预算限制。未实现压缩时协商不宣称支持；服务端强制不支持模式时明确失败。不得先无界解压再检查大小。

## 4. TLS 策略

`TransportSecurity=RequireTls`：要求完整业务流量 TLS，必须验证证书链、有效期、目标主机名；仅登录加密或仅认证阶段 TLS 不满足该策略。服务器只提供 AUTH_ONLY 或不支持 TLS 时，在发送敏感认证材料前明确失败。

`PlaintextAllowed`：明确允许非 TLS DM 会话；如果实际协商 TLS 仍严格验证证书。它不等于 `TrustServerCertificate=true`。

使用 `SslClientAuthenticationOptions`，`TargetHost` 取配置 DNS/IP 名；系统信任链为默认，可显式注入受管理自签 CA；没有恒 true 回调。[F05]

建议使用 OS 协商协议，握手后检查不低于 TLS1.2；不写死只启用旧版本。撤销检查默认 Online，医院离线环境可以显式配置 NoCheck 并写明风险，不能因网络不可达偷偷放行。自定义验证不是关闭主机名验证的捷径。

证书/私钥路径和密码必须显式可控；无 changeit 默认私钥口令。认证失败立即关闭并传播异常，不 catch+Console.WriteLine 后继续登录。

## 5. 旧加密与原生依赖

`dmcyt`/`dmfldr` 的调用在 Native 内集中管理，使用 NativeLibrary/SafeHandle/明确调用约定；不得散落 kernel32 LoadLibrary。产品不嵌入未核实可分发原生二进制。[R03]

不自行把既有 DH/报文 cipher 替换为 AES-GCM 等不同 wire 格式；协议互操作不是单方面改算法。旧消息加密默认不冒充现代安全保障：缺乏已验证实现或原生库时明确说明依赖并失败，不降级为明文。针对服务器要求的已存在模式可以作为显式兼容路径，需独立兼容性与安全记录。

仅加载应用部署的受信绝对路径/已配置原生目录；不能搜索当前工作目录或服务器提供任意路径。每个失败路径验证句柄恰好释放一次；库加载失败不打印认证数据。

未知 native 模式、GMSSL、第三方 cipher 先明确不支持，不能因为上游有 DllImport 就默认开放。

## 6. 验收

NET-01：header 每次仅到达 1 byte，正常拼帧；随机 body 分片同样正确。
NET-02：header/body 截断、负长度、加法溢出、超大长度、错误 CRC 均快速失败且无大分配/复用。
NET-03：连续 heartbeat 不能绕过命令超时。
NET-04：DNS/TCP/TLS/LOGIN 任一步失败或取消无 socket/native handle 泄漏。
NET-05：假 stream 只允许 ReadAsync/WriteAsync，异步路径不能调用同步 I/O（R2验收）。
TLS-01：正确链与正确主机名成功；未知 CA、过期、错主机名失败。
TLS-02：RequireTls 遇明文/AUTH_ONLY 不发送 LOGIN；没有“失败后降级成功”。
TLS-03：PlaintextAllowed 不让无效证书通过；离线撤销策略只按显式配置。
NAT-01：错误 RID、缺库、缺导出符号错误可诊断，释放次数正确，秘密不出现在异常。

真实 TLS/原生 cipher 互操作必须针对实际 DM 环境运行；只通过假 SslStream 不等于已支持达梦加密部署。

---

<!-- source: specs/05-command-reader-and-ef-contract.md -->

# S05 — 命令、Reader、多结果和 EF 保存契约

状态：实施规范。优先级：P0/P1。依赖：S03、S04基础。任务：T08。

## 1. 首要范围

CommandType.Text：参数化 SELECT、INSERT、UPDATE、DELETE；现有 EF 的 DML+SELECT 组合；Prepare 在受支持语句上不改变语义。StoredProcedure/输出参数/RefCursor 若未通过单独测试，明确拒绝，不能靠静默 text 转換假装支持。

第一版必须满足 [R06] 中的生成 SQL，不要求通用 DbBatch。`SingularModificationCommandBatch` 限制修改命令数，不代表只有一条 SQL。

```sql
UPDATE "APP"."Widgets" SET "Name" = :p0
WHERE "Id" = :p1 AND "Version" = :p2;
/*EFCOREROWCOUNT*/SELECT SQL%ROWCOUNT;
```

```sql
INSERT INTO "APP"."Widgets" ("Name") VALUES (:p0);
SELECT "Id", "Stamp" FROM "APP"."Widgets"
WHERE SQL%ROWCOUNT = 1 AND "Id" = SCOPE_IDENTITY();
```

序列键路径相应使用限定序列名 `.CURRVAL`。这些是需要保留的下游契约示例，不作为随意 SQL 拼接模板。

## 2. 执行模型

内部概念 `DmCommandPlan`：固定 SQL、参数 metadata snapshot、事务引用、timeout snapshot；Execute 开始后参数集合和 CommandText 不得并发更改。不能为了支持操作而把用户命令上的 CommandText 临时改成内部 SQL。

`DmResultCursor` 对接已验证 wire 响应，维护：当前 rowset metadata、当前行、是否有后续结果、受影响行计数、输出值状态、底层 statement 所有权。

强制先为现有 reader 对公开方法的行为做 characterization。不要凭报告对 magic comment 的简写重新定义 NextResult：源码检查条件为 `!EFCoreNextResult`，默认开关与注释计数影响必须分场景验证。[R10]

## 3. SQL 处理边界

保留已证明的 SQL 解析/协议执行路径，**不得使用 Split(';')** 实现复合命令。字符串、注释、引用标识符、DMSQL 块中的分号不是独立命令。

参数识别使用 tokenizer，不替换字符串/注释中的 `:p`、`@p` 或 `?`。第一版对选定复合 SQL 有测试不等于承诺任意 DMSQL/任意多语句。若整体执行依赖服务端支持，保留该前提，不用客户端偷偷改事务边界实现。

不允许为了“校验连接活着”在 DML 与 `SQL%ROWCOUNT` 或 identity 回读之间插入 SELECT 1、schema 查询或其他业务 SQL。这些辅助语句可能改变待消费会话值。整个组合共享同一 execution lease。

## 4. Reader 契约

- `FieldCount/GetName/GetFieldType/GetDataTypeName` 描述当前 rowset；`GetValue` 的实际类型必须与所声明 getter 契约一致，DBNull 单独处理。
- ExecuteReader 成功返回时定位到该命令首个可读 rowset。DML 的计数是计数，不能制造有一行空数据的 rowset。
- `Read` 只移动当前结果的行；无行返回 false，不自动跳下个 rowset。
- `NextResult` 按协议顺序移动到下一可读 rowset；空 rowset 也必须保留。只有 update count 的结果如何跳过/累计，要与现有 EF 消费方式一致。
- `RecordsAffected` 非 DML为 -1；未知计数为未知，不伪造 0。实际累计规则与溢出行为必须文档化；总计数不能代替 EF 逐命令并发检查结果。
- 已关闭 reader 的读取与 stream 操作报明确关闭错误。Close/Dispose 幂等。
- `ExecuteScalar` 返回第一可读 rowset 第一行第一列：无行为 null，SQL NULL 为 DBNull.Value；其余结果需安全完成/关闭。
- `ExecuteNonQuery` 不忽略后续服务器错误；只有整个命令完成且状态可知才返回受影响数。

Reader 早关：只消费/终止已经启动的协议执行。不能为了“清空”而执行尚未发送的后续有副作用 SQL。无法有限时间同步协议时销毁连接。不允许仍有悬挂结果就归还池。

输出参数属于后续明确支持范围，若开放必须说明何时可见（通常读完/关闭后），不得留旧执行值。

## 5. CommandBehavior

逐位判断 `(behavior & flag) != 0`，不能用 `behavior == flag` 判组合。Default=0 特别处理，不按 HasFlag(Default) 判业务功能。[R02]

支持范围：Default、CloseConnection、SingleResult、SingleRow、SequentialAccess；SchemaOnly/KeyInfo 要用已验证 describe/metadata 路径，无法保证时在发送前明确拒绝而不执行有副作用 SQL。

`SingleRow/SingleResult` 只约束可见结果，不赋予跳过事务清理的权利；关闭仍遵循会话恢复规则。SequentialAccess 的完整流式语义到 S10 后才声明；R1 即使修位标志，也必须诚实注明尚未实现 LOB 流式，不伪造 memory bound。

`GetSchemaTable` 不递归在持有 reader 的 public connection 上启动第二命令。已有元数据足够则本地生成；缺失的键/基表信息标 unknown/DBNull，不伪造唯一键。无法满足请求的 KeyInfo 前置明确报错；不得偷偷另开连接改变事务视图。

## 6. EF 交接契约（ef-savechanges-v1）

必测：UPDATE/DELETE 影响 0/1 行；INSERT 标识键和序列键；读回多个生成列；组合 SQL 中第二语句失败；空 SELECT；字符串/注释内的分号；重复执行 command；用户事务中的相同路径。

`/*EFCOREROWCOUNT*/` 暂作兼容输入保留，但不建议新增依赖它的设计。未来移除需单独下游变更，首版不能自行删注释或用新的返回约定代替现有 EF 协议。

S07 的隔离问题调查以此执行层为重点之一：单 UPDATE ExecuteNonQuery 对照 UPDATE+SELECT ExecuteReader，再对照 EF 保存点。不能预先宣布根因已定位。

## 7. 验收

CMD-01：UPDATE/DELETE 各 0/1 行，返回与数据库最终状态一致。
CMD-02：identity/sequence 与非键生成列准确读回；无额外 probe 污染 SQL%ROWCOUNT。
CMD-03：多 rowset、空 rowset、DML count 混合的 Read/NextResult 顺序正确。
CMD-04：复合 SQL 中后续错误不能丢失；失败后会话处于可证明状态。
CMD-05：复合标志至少验证 SingleRow|CloseConnection、SequentialAccess|CloseConnection、SchemaOnly|KeyInfo（支持则正确，不支持则明确早拒绝）。
CMD-06：早关 reader 不泄漏句柄，不执行额外尚未发送的写入；关闭后下一命令正常或明确需重开。
CMD-07：CommandText 在 Prepare/事务/重复执行后未被内部清理置空。
CMD-08：EF 官方基线的正确功能在 W 候选包上通过；官方缺陷断言另作 characterization。

---

<!-- source: specs/06-parameters-and-types.md -->

# S06 — 参数绑定、类型转换与精度

状态：实施规范。优先级：P0/P1。依赖：S02、S05。任务：T09。

## 1. 现有实现复用

起点：`DmSqlType`、`DmGetValue`、`DmSetValue`、`DmdbNumeric`、`DmDateTime`、`DmIntervalDT/YM`。第一阶段保留经测试的 wire codec，把 CLR 转换/推断从巨型 switch 中逐类提取，而不是重新发明 BCD 或日期位编码。[R04]

内部按 codec 分类：Integer、Decimal、Floating、Text、Binary、Guid、DateTime、Interval、LobLocator；每种 codec 明确接受 CLR 类型、wire 类型、null、encode/decode、溢出和精度规则。复杂类型单独 unsupported，不通过 ToString 伪装成文本。

## 2. 参数模型

类型优先级：显式 `DmSqlType` → 显式 `DbType` → 准确的服务端 describe metadata（适用时）→ CLR 值推断。每一层记录设置来源，不能用枚举默认值判断是否显式设置。冲突的显式属性组合报参数错误；`ResetDbType` 清除显式类型来源并恢复推断。

null/DBNull：有显式类型时按该类型发送 NULL；无类型且服务器 prepare 可以准确描述时使用它；两者均无不能盲目发 VARCHAR NULL，给出类型未确定错误。nullable enum 与 DBNull 分开处理。

参数名只移除允许的单个前缀 `:`/`@`；集合匹配采用明确且稳定的规则（本产品默认 OrdinalIgnoreCase），归一后重名拒绝；SQL `:p` 在 tokenizer 中匹配，`?` 按位置。命名/位置混用首版拒绝；字符串和注释里的字符不参与绑定。重复出现同一命名参数必须复用同值或按 wire 需要重复编码，不能要求用户重复添加。

执行时冻结集合/metadata，禁止另一线程改 Value/Size。大 Stream 输入不复制全量内容，但限制其在一次执行期间由驱动独占读取。Prepare 之后值变化合法，类型/Size 变化需失效重准备，而不是沿用错误旧 metadata。

## 3. 精度规则与映射

| CLR/请求 | 新契约 |
|---|---|
| enum / Flags | Convert 到真实底层数值再编码，不用名字排序/索引 |
| ushort / uint | 自动推断到足以容纳范围的有符号宽类型；显式窄类型溢出拒绝 |
| ulong | 使用足以容纳 20 位整数的精确 decimal 编码路径，不降为 Int64 |
| decimal | 直接 coefficient+scale 编码，禁止 double 中转；超出所声明精度/scale 不默默截断 |
| float/double | 明确 finite/NaN/Infinity 支持矩阵；服务器不支持的特殊值早拒绝 |
| 字符串 | UTF-16 长度与编码后的字节长度分别管理；严格 encoder fallback，不替换为问号 |
| Guid | 默认明确字符或 16-byte 二进制存储约定；普通 CHAR/VARCHAR(36) 不自动变成 Guid |
| DateOnly/TimeOnly | 明确 DATE/TIME 对应；非可表示精度拒绝，不丢 subsecond |
| DateTime | 无时区类型返回 Kind=Unspecified；不默默转换本地时区 |
| DateTimeOffset | 保存原始偏移和可表示精度，GetFieldValue<DateTimeOffset> 保持；超 CLR 范围拒绝 |
| TimeSpan | INTERVAL DAY TO SECOND；负值、大天数和有效 scale 准确；不与 TIME 随意互换 |
| byte[] | Binary/VarBinary/Blob 区分；NULL、空数组不是同一值 |

输入 precision/scale 不显式设置时才推断。设为 `(38,20)` 不代表 CLR decimal 能表示所有该列可能值；读取不可表示值必须 OverflowException，不能先舍入再成功。

对需要完整高精度的调用方，增加经 API 审核的小型 `DmDecimal`（BigInteger coefficient + scale、Invariant parse/format）。`GetProviderSpecificValue`/对应 field type 可返回它；普通 `GetValue`/GetDecimal 对超 CLR 范围抛异常。支持 scope 先限制已验证的 DECIMAL wire，不把 XDEC 顺便宣布支持。

`GetByte/GetSByte` 的 DOUBLE 分支必须走数值转换而非日期。整数转换对非整数默认拒绝，不无声截断；具体允许转换矩阵写入 tests，不能到处 Convert.ChangeType 产生不一致舍入。[R04]

## 4. Guid 与文本兼容

普通 36 字符列 `GetFieldType=string`、`GetValue=string`，即使内容像 Guid 也不自动改类型。显式 GetGuid 可以按已声明字符格式或二进制格式解析；格式无效抛 FormatException。

此行为相对官方自动推断可能变化，加入 approved differences。EF Guid 映射需要实际 getter 与参数测试；不得靠恢复官方误推断获得兼容。

## 5. 与下游已有读取方式兼容

EF 的 TimeSpan 映射当前调用 GetString 再 Parse；DateTimeOffset 也有 GetString+Invariant Parse 路径。[R07][R12]

第一版保留这两种字符串读取契约，例：可解析的日期时间附 `+08:00`；INTERVAL 使用下游现有解析能理解的限定字面量形式。同时提供正确 typed getter。不能只改善 GetFieldValue 就破坏旧 GetString；从文本改为 typed getter 应是独立下游 PR。

NCLOB/CLOB、BLOB/VARBINARY、IntervalDayToSecond 必须保留参数类型标记入口；`parameter is DmParameter` 在新 EF 接入分支改为 W 类型，不能静默跳过。空 LOB 和 NULL LOB 同样测。

## 6. 验收

TYP-01：enum {-10, 7, 1000}、Flags、byte/ulong 底层 enum 往返正确。
TYP-02：signed/unsigned min/max、越界、非整数转整数；正确值成功，越界明确失败。
TYP-03：decimal(38,20)、极小值、负值、尾随零；不通过 double；超 CLR 范围有精确 provider-specific 或明确异常。
TYP-04："不是GUID但长度恰好36..." 的真实 36 字符测试保持 string；有效/无效显式 GetGuid 区分。
TYP-05：UTF-8/GB18030 等声明字符集中的中文、emoji、组合字符、半代理项；不静默丢字。
TYP-06：TimeSpan 正负、多天、边界 tick 与非可表示 scale；DateTimeOffset 正负偏移、闰日、7位小数。
TYP-07：NULL/空字符串/空 byte[]、typed null、未确定类型 null。
TYP-08：重复参数、注释/字符串占位符、Prepare 后换值和换类型。
TYP-09：同步 getter 与异步 getter 返回的类型、值、异常一致；EF LOB/INTERVAL/时间戳现有功能通过。

## 7. 不做

不增加 JSON 查询运算符（属于 EF/SQL 方言）；不增加完整 ARRAY/Class/Geometry/XDEC 支持；不承诺所有 .NET 日期时间范围都被 DM 服务端支持。

---

<!-- source: specs/07-transactions-and-savepoints.md -->

# S07 — 本地事务、隔离级别、保存点和未知结果

状态：实施规范。优先级：P0。依赖：S03、S05。任务：T10、T11。

## 1. 必须改变的行为

官方恢复代码在特定 Oracle 兼容设置下对仍有效事务 Dispose 进入 Commit；W 不保留该行为，所有兼容设置遵守同一个“未显式提交则尝试回滚”的公开契约。[R05]

注意“尝试回滚”不等于网络失败时已确认回滚。Dispose 失败应使连接不可复用并保留诊断；不能将 socket.Close 记录成服务器 Rollback acknowledged。

## 2. 开始与绑定

只支持一个本地事务；嵌套 BeginTransaction 明确拒绝。Begin 在成功得到服务端确认后才返回 Active；缓存 isolation/autocommit 只能在确认后更新。无法确认 Begin 的部分状态时销毁会话，不能放回池。

命令有显式 Transaction 时必须属于同一逻辑连接、同一 session/generation 且 Active；旧事务不能用于重新 Open 的连接。为避免无意脱离事务，Active 本地事务中未给 command 指定 Transaction 的操作首版拒绝（驱动内部控制操作使用专有入口）；该行为在 EF 使用显式事务时验证。

事务不因连接字符串改变兼容设置而改变 dispose 语义。不把服务器事务状态存在进程静态字段。

## 3. 隔离级别

R1 默认承诺 ReadCommitted；Unspecified 明确解析为本产品默认 ReadCommitted。ReadUncommitted/Serializable 根据真实服务器、SQL形态、事务结果验证后开放对应保存路径。RepeatableRead、Snapshot、Chaos 未证明语义之前抛 NotSupportedException，不能把 RepeatableRead 静默降到 ReadCommitted。

隔离支持有两道门：服务器语义（并发读写 litmus tests）和驱动执行链（特别是 DML+SELECT/保存点）。仅 Begin 成功不代表隔离语义和 SaveChanges 已支持。

**T11 最小复现**：同一服务器/账号/配置，对比 ReadCommitted、ReadUncommitted、Serializable；分别运行单 UPDATE ExecuteNonQuery、UPDATE+SQL%ROWCOUNT ExecuteReader、INSERT+键回读、保存点前后组合、完整 EF SaveChanges。捕捉异常栈、脱敏 SQL 形态及 CommandText 生命周期。现有 EF 已记录某两级 SaveChanges 报 CommandText has no value，并有 Serializable ExecuteUpdate 成功测试，但根因不能由此推定。[R08]

## 4. Commit/Rollback 状态与异常

| 情况 | 状态与返回 |
|---|---|
| 参数错误/预取消，尚未开始发 Commit | 保持 Active；返回参数异常/取消 |
| 收到且验证 Commit 成功响应 | Committed；旧 transaction 不再可用 |
| Commit 发送尝试后连接/响应不确定 | OutcomeUnknown；抛 DmCommitOutcomeUnknownException，连接 Broken |
| 收到确定的服务器 Commit 错误 | 按已验证服务器状态处理；不能默认当作回滚 |
| Rollback 确认成功 | RolledBack |
| Rollback 响应不确定 | OutcomeUnknown；销毁连接，不报已回滚 |

`DmCommitOutcomeUnknownException` 继承 DmException，IsTransient=false；可携带取消/超时作为原因。即使触发原因是 token，已经发送 Commit 且结果不明时优先暴露未知提交结果，不把它装成“只取消了一个未执行请求”。此特殊规则必须文档化并测试。

发送“尝试”开始就保守标记可能送达，因为 Socket/SSL 写异常通常不能证明服务器收到多少字节。收到成功后迟到取消不改变已确认结果。绝不在驱动内自动重连重放 Commit 或整个事务。[F06]

## 5. Dispose 与连接 Close

Active 事务 Dispose：若会话同步，使用独立 CleanupTimeout 尝试 Rollback；若有活动 reader，先按 S05 安全关闭它再回滚；无从安全同步则销毁。清理失败不得悄悄归还池。

同步 Dispose/DisposeAsync 默认不以清理异常覆盖调用方正在传播的业务异常；错误通过 FailureInfo/诊断保留，并销毁会话。显式 Rollback 则传播失败。无论策略如何，不能产出“已回滚”的虚假成功证据。

已经 Committed/RolledBack 的 Dispose 幂等无网络操作；OutcomeUnknown 的 Dispose 不再发送 Commit/Rollback 去猜结果。Clear 引用时先保存最终 outcome，不能 finally 中无条件改为已完成成功。

## 6. 保存点

实现标准 `SupportsSavepoints`、Save/SaveAsync、Rollback(name)/RollbackAsync(name)、Release/ReleaseAsync。[F07]

名称建议映射成驱动生成的短 ASCII 服务端标识符，维护事务内 userName→serverName+sequence；不把用户名称裸拼 SQL。重复名称替换、回滚后后续保存点失效、长度和 NUL 检查有测试。若为兼容必须使用用户名称，严格引用与转义且遵循已验证标识符长度。

服务端 Save/Rollback 必须真实执行。若服务器没有验证可用的 RELEASE，允许公开说明 Release 仅逻辑释放、服务端资源到事务终结回收；不得伪称发送了不存在的 RELEASE opcode/SQL。.NET10 DbTransaction 的默认 Release 本身是 no-op，引用该事实不等于证明 DM 的 RELEASE 语法。[F07] 限制同事务创建保存点数，避免逻辑释放导致无限服务端资源增长。

初始服务器 profile 未证实时 SupportsSavepoints=false，明确拒绝；证明 save/rollback 后才能 true。EF 当前可能走自己的保存点 SQL，因此原有 SQL 路径和新增 ADO API 都要测，不能只测一个。

## 7. DDL 与 ambient 事务

DM DDL 的隐式提交边界不能由驱动伪装成原子迁移。读服务端响应中的已验证事务状态；看到事务已经终结就标 CompletedExternally、使旧事务失效。没有可靠状态信号时明确限制/记录未知，而不在每条 DML 与键回读之间插入状态查询。

本地事务内执行 DDL/动态 DDL 的实际副作用必须在隔离测试中记录；本产品不宣称能回滚已隐式提交的 DDL。[R11]

R1–R3 不实现分布式事务提升。`Enlist=true` 或非空 EnlistTransaction 调用明确不支持；默认 Enlist=false 不参与 Transaction.Current，这一点必须文档化，不能表面接受 scope 却假装参与。后续单资源 PSPE 与多资源提升是两个独立特性，见 S13。

## 8. 验收

TX-01：所有已声明兼容设置，未 Commit Dispose 后独立连接查不到写入；真实事务回滚正确。
TX-02：Commit/Rollback/重复 Dispose、旧 transaction 与重新 Open 的 session 错配被拒绝。
TX-03：Commit 响应丢失与 token 竞争，outcome unknown、没有重放、没有回池。
TX-04：两条语句第二条失败后显式回滚；独立连接验证最终状态，而不只检查异常。
TX-05：保存点正反路径、重复/Unicode/恶意名称、回滚后后继失效；无注入。
TX-06：隔离级别 litmus tests 与 SaveChanges 最小复现单独记录。
TX-07：DDL 隐式提交不被写成回滚成功；状态和文档一致。
TX-08：ambient 配置拒绝/不参与语义准确，不存在伪事务。

---

<!-- source: specs/08-async-cancellation-and-timeouts.md -->

# S08 — 真异步、取消、超时和错误语义

状态：实施规范。优先级：P1；R2 核心。依赖：S03–S07。任务：T13、T14。

## 1. 必須贯穿的调用链

`DmConnection.OpenAsync/CloseAsync/DisposeAsync/BeginDbTransactionAsync`；Command ExecuteNonQueryAsync、ExecuteScalarAsync、ExecuteDbDataReaderAsync、PrepareAsync、DisposeAsync；Reader ReadAsync、NextResultAsync、CloseAsync/DisposeAsync、会触发网络读取的 GetFieldValueAsync/IsDBNullAsync；Transaction CommitAsync、RollbackAsync、Save/Release/回滚保存点异步；S10 的 Stream/TextReader 读写异步。

还须检查元数据与 DbDataSource 包装方法是否会回落同步。有网络 I/O 的公开 Async API，要么真异步，要么明确未支持；R2 已声明范围不得继承基类同步 fallback。默认 ADO.NET 的 Async 实现不能作为真异步证明。[F01][F02]

## 2. 内部实现决策

统一 asynchronous core，编解码是纯 CPU 函数，所有 I/O 使用底层异步 API并传递有效取消；库内部 await 使用 ConfigureAwait(false)。同步 public facade 可以在**唯一同步边界**阻塞等待该 core；不在异步调用链中 .Result/.Wait/GetAwaiter().GetResult，不需要为 sync 再复制一套状态机。

若已有经加固同步路径在 R1 保留，T13 必须列出并清理异步路径的全部残留同步调用。Task.Run、Task.Factory.StartNew、另起线程执行 Socket.Receive 都不合格。native 认证 crypto 的短 CPU 运算可同步，但其持有网络/文件等待不得伪称端到端异步；慢文件/证书加载尽可能在可控初始化阶段完成。

已缓存元数据和一行数据可以返回已完成 Task/ValueTask，不需要为了“异步”强行 yield。ValueTask 只能按契约消费一次，不缓存可复用未完成 ValueTask。

## 3. 期限模型（明确产品语义）

| 期限 | 开始与结束 | 重置规则 |
|---|---|---|
| PoolAcquireTimeout | 入队到取得池许可 | 不包括随后新建连接的 ConnectTimeout |
| ConnectTimeout | DNS开始到登录/必需初始化完成 | 所有地址/认证阶段共享，不逐步重置 |
| CommandTimeout | 一次 public 执行调用内部的总工作期限 | ExecuteReader 返回后结束；Read/NextResult 每次调用新预算，使用 command 的冻结值 |
| LOB Read/Write timeout | 一次 stream 方法调用 | 继承 owner command 的冻结值；跨分块仍共享该次调用预算 |
| ReadIdleTimeout | 网络读取等待期间的无字节进展 | 可在字节进展后重新开始，但不能重置总期限 |
| CleanupTimeout | 内部关闭/回滚/复位 | 有限且独立于已取消 token |

这样用户在两次 Read 之间处理业务不计入命令超时；一次 Read 内发生多次 Fetch，则不能每次 Fetch 重置 CommandTimeout。0 为无限只用于公开允许的期限；内部清理永不无限。

使用单调时钟（注入 TimeProvider）与剩余预算，不用 DateTime.Now 做差。CancellationToken 不依赖 Socket.ReceiveTimeout；设置 ReceiveTimeout 不能替代异步 timeout。握手返回的 heartbeat timeout 作为独立字段，不覆盖用户配置。

server-side timeout 可以额外下发，但客户端期限独立有效；server 字段单位必须由源码+probe验证，未验证不写魔术单位转换。

## 4. 取消范围

ExecuteReaderAsync 的 token 管该次调用，成功返回后注销；后续 ReadAsync/NextResultAsync/GetFieldValueAsync 使用它们自己 token，不让旧 token 杀掉新调用。`DmCommand.Cancel()` 针对当前 command 执行/它返回的 reader，并绑定 executionId。不能取消另一个 command。

预取消发生在尚未发送请求前：返回取消，不产生网络副作用、不破坏原可用 session；已有 Active 事务保持 Active。没有活跃执行的 Cancel 是 no-op。

### 第一版取消策略：AbortPhysicalSession

在没有独立证据证明原生 cancel opcode、目标身份、响应和同步恢复方式之前，**不发送 CMD_CANCLE=11**。报告中存在常量不是实现依据。[R03]

一旦请求发送开始，用户取消/客户端超时通过 S03 的原子身份检查将 session 标 Broken 并关闭捕获的 transport，唤醒等待。取消不自动重连，不重放，不返回池。后续必须显式 Close/Open；服务器端执行是否已经停止不作强保证。

在 reader 存活但当前没有网络调用的时刻 Cancel：标该 execution 已取消，使后续读取失败；按有界关闭策略关闭游标或直接销毁会话，不能影响无关租约。Cancel 本身不等待无限网络清理。

### 原生取消属于后续优化

即便后续支持，也必须覆盖执行快结束、目标 query 已完成、陈旧 session、事务状态、取消响应和下一条查询验证；没有恢复证明仍销毁，不能仅因拿到 cancel ack 就回池。

## 5. 故障信息与异常

建议稳定的结构化 `DmFailureInfo`：阶段（Pool/Dns/Connect/Auth/Prepare/Send/Receive/Fetch/Commit/Reset）、DriverErrorCode、ServerErrorNumber（存在才填）、SqlState（存在才填）、OperationOutcome（NotSent/ServerReported/Unknown）、TransactionOutcome、ConnectionReusable、取消/超时原因。

新本地错误使用自己的符号和分配表；不要把所有异常包装为 6001 或用假的服务端负数。`DmException.Number` 若为下游兼容保留，必须明确与原始 server code 的区别。InnerException 保留堆栈但脱敏日志不得自动展开可能包含密码的对象。

普通用户 token 取消用 `OperationCanceledException`（可由 DmOperationCanceledException 继承并携带 FailureInfo），保持调用 token。`Command.Cancel()` 没有外部 token，可使用 execution 自有 token并标 CancelSource=Command。

超时用有明确 ErrorKind=Timeout 的 DmException/专用子类，不用伪造 OperationCanceledException 混淆用户取消；两者同时触发按协调器记录的第一个终止原因。**Commit发送后结果未知例外**，按 S07 优先 Unknown 异常。

IsTransient 不是“可安全重放”的证明。Unknown、认证/配置/语法/约束/精度错误不得由驱动推荐自动重试；连接故障只说明可能需要新连接。驱动内部重试仅可用于未认证、未发送业务的地址尝试，不能自行重放查询或写入。[F06]

## 6. 严格测试

ASY-01：假 transport 的同步 Read/Write/Connect 直接抛测试异常；所有 Async API 在未提供回复时返回未完成 task，不能先堵塞调用线程。
ASY-02：受控 barrier 释放响应后 task 完成；不要用固定 Sleep 假装测试非阻塞。
ASY-03：覆盖 ExecuteReader 之后首次真正 Fetch、NextResult、Commit、Rollback、DisposeAsync，避免只测试缓存页。
CAN-01：发送前预取消为零字节且连接仍可用。
CAN-02：半包发送/半包响应时取消，task结束、状态Broken、无回池、无重连。
CAN-03：旧 ExecuteReader token 在返回后取消，不影响使用新 token 的 Read。
CAN-04：旧 lease 取消与新 lease 借用交错，不能关闭新 transport；用 barrier固定竞态。
CAN-05：Cancel→Close、Close→Cancel、Cancel 与成功完成、重复 Cancel 均无死锁/双释放。
TMO-01：虚拟时钟验证每个预算、0无限和负值拒绝；COMMIT/LOB 不误用 ConnectTimeout。
TMO-02：heartbeat/碎片持续到来不重置总 deadline；用户处理两行间隔不耗 Read 调用预算。
ERR-01：Unknown 写入和 Unknown commit 无自动重试；错误码与 InnerException/最终状态准确。
PER-01：独立连接高并发+真实延迟下观测 ThreadPool/吞吐/分配；不设未经测量的“必须10倍”指标。主门槛是没有随等待连接数线性增加的同步阻塞 worker。

---

<!-- source: specs/09-datasource-and-pooling.md -->

# S09 — DbDataSource、连接池和会话复位

状态：实施规范。优先级：P1/P2；R3。依赖：S03、S07、S08。任务：T15、T16。

## 1. DataSource 所有权

`DmDataSource : DbDataSource` 持有不可变配置、诊断与一个池。CreateConnection 返回 Closed 逻辑连接；OpenConnection[Async] 返回 Open 逻辑连接。DataSource.CreateCommand 等快捷 API 必须让内部连接在非 reader 完成/reader关闭时释放，不能每个 Fetch 获取另一连接。[F03]

显式创建的两个 DataSource 默认拥有不同池，不因连接字符串看起来相同就互相接管生命周期。DataSource 的 ConnectionString 为安全可展示版本，内部凭据不从它反向解析。

为了兼容 EF 目前每次 new DmConnection(connectionString)，直接构造且 Pooling=true 的连接使用进程内受控 pool registry。Registry 使用全量不可变配置身份、并发同步与有界容量；不复用旧共享可变过滤器。默认 Pooling=false；无配置不隐式引入全局池。

## 2. 池身份

至少包括：endpoint/port、用户与认证凭据身份、schema、数据库语义模式、字符集/时区/初始化设置、TLS与CA/客户端证书身份、安全兼容选项、影响 wire 格式的配置。不同凭据或证书不得共用。

不能仅 `string.GetHashCode()` 作身份；哈希仅用于查找，完整不可变键负责相等。含秘密的对象不得使用自动 record.ToString 输出内容；秘密仍会为新建认证保留在内存，不声称散列后已无需保存密码。证书/凭据轮换需要新身份或明确池清除，不能永远复用旧认证会话。

规范化允许等价别名归一，但不能把大小写敏感密码/schema 转小写；不允许两个租户通过hash碰撞获得同一会话。

## 3. 池状态与计数

维护 idle、leased、creating、resetting、closing/reserved；物理容量许可涵盖尚未建完和尚未清理完成的连接。每个许可恰好释放一次。等待队列有界，使用不会在锁内执行用户 continuation 的 TCS/等价机制，取消不会漏 permit。

checkout：检查关闭状态 → 取合格 idle 或在容量内预留创建 → 必要的验证 → 分配新 LeaseGeneration → 交付。队列取消/超时与会话分配需原子处理；如果取消获胜，会话归池或销毁，不能既发给取消用户又给另一个用户。

无闲置且满池时异步等待；这里允许等待，不改变 S03 的同连接并发命令快速失败规则。PoolAcquireTimeout 不反复因队列唤醒重置。

return：使外部旧对象无权再使用 → 注销/完成所有取消回调 → 清理 reader/LOB/事务 → 复位验证 → Ready入idle或销毁。所有失败路径统一计数。旧 statement、LOB locator 不能跨 generation 使用。

## 4. 复位是独立能力，不等于 SELECT 1

默认策略 `VerifiedResetOrDiscard`：只有已证明 session 可以恢复成该 pool 的基线，才能复用；不确定就销毁。不能把 ROLLBACK+SET SCHEMA 包装成“完整复位”。

T15 必须验证至少：事务/autocommit/isolation、schema、role/授权相关会话状态、时区/NLS/语言、临时表、会话变量、session级锁、未关闭游标、prepared statement、包/过程会话状态（目标工作负载涉及时）。调用方原始 SQL/存储过程可能改变客户端无法跟踪的状态；仅检测 SQL 首词或“这是EF生成”不构成证明。

优先寻找并实测服务端 reset/reinitialize 能力及权限/版本；不得猜 reset opcode。没有全量 reset 时，可以证明一组受限操作的安全复用，但必须有明确边界和检测方式；对无法分类的原始 SQL 采用 discard，不能默默切换弱复位。特殊 application-managed 复位策略另行 ADR，不能作为默认安全模式。

因此，可能出现“池容量与异步等待已实现，但某些会话每次归还都销毁”的正确状态。报告真实 reset/discard 比例，不宣称这些路径减少了握手。只有常用目标路径确有安全复用证据后，才宣传池化性能。

不在 DML与SQL%ROWCOUNT/键回读之间验活；所有 reset/validation 在执行lease之外的专用维护 lease 内进行。

## 5. 验活与维护

不用每次 checkout 无条件 SELECT1。按显式验证策略和空闲时长执行有界健康检查；成功验活只是连接可通信，不代表状态已复位。失败销毁并在同一获取预算内再尝试，不能无限重试。

idle eviction/lifetime 等使用一个受控调度器+TimeProvider，不每条连接创建专用后台线程。不在全局锁内发网络请求。Registry 有池数量上限；零借出且超期的池可移除，防止每个租户/动态连接串创建永久静态对象。

`ClearPool/ClearAllPools` 增长 pool epoch，关闭 idle；已借出会话标记归还时退休，不暴力中断业务。DataSource Dispose 停止新借用、取消等待者、关闭闲置；在外借出的会话归还时销毁，文档明确 Dispose 不无限等待用户永不释放的借用者。后台维护停止且任务被观察，无未观察异常。

## 6. 验收

POL-01：MaxPoolSize=1，一个占用、第二个等待可取消；所有结束后permit总数正确。
POL-02：建连失败、初始化失败、取消、复位失败、清理异常均不漏容量。
POL-03：不同用户/密码/schema/TLS/证书不串池；相同有效配置别名得到预期身份。
POL-04：归还未完成事务后，下一租约看不到旧事务写入；实际数据库独立连接检查。
POL-05：修改schema/role/时区/临时对象/会话锁后 reset或discard，不泄漏给下一租约。
POL-06：取消callback与回收/checkout竞态不能破坏下一租约。
POL-07：DataSource关闭/ClearPool与活跃借用、等候者、维护线程竞争无死锁。
POL-08：长期多租户配置 churn 后注册表/线程/句柄/statement数量有界。
POL-09：DataSource.CreateCommand 返回reader时连接持有到关闭；执行失败也恰好归还一次。
POL-10：报告复位证明、实际复用率、验证RT；不把discard路径算握手优化。

---

<!-- source: specs/10-streaming-lob.md -->

# S10 — 流式 BLOB/CLOB/NCLOB

状态：实施规范。优先级：P1/P2；R3。依赖：S05、S06、S08。任务：T17。

## 1. 目标与现有基础

原代码的底层 LOB 消息已有按定位符分块读写，但 Reader 的 GetBytes/GetValue 等上层可能先完整物化；因此目标是接通端到端分块，不重写所有 LOB wire。[R04]

起点：AbstractLob、DmBlob/DmClob、DmBLobStream、GET/SET_LOB_DATA、GET_LOB_LEN、DmDataReader.GetBytes/GetChars、A/B.cs 的 LOB 分片。wire offset 单位、基数和 locator 更新遵循已验证源实现，不假设全是字节或全是0基。

## 2. 输出流

`GetStream(ordinal)` 提供只读 forward-only Stream，`CanSeek=false`；`Length/Position setter/Seek/SetLength/Write` 不支持时明确抛出。不要为了好看暴露一个设置无效的 Position。原 explicit DmBlob 可定位读写属于单独兼容surface，保留时需要其自己的 seek测试，不影响基础流模型。

`GetTextReader(ordinal)` 对 CLOB/NCLOB 增量解码。GetStream/GetTextReader 创建时不整字段读取；实际 Read/ReadAsync 才取下一块。每个 reader v1同一时刻最多一个活跃字段流；下一行/下一结果/reader关闭会关闭旧流并使其后续操作失效。读取另一列时按 SequentialAccess 顺序检查，明确拒绝向后访问。

流关联 session+generation+execution+rowVersion；不能把 locator 从一个连接交到另一个连接使用，也不能在父reader关闭后继续网络访问。多线程并读同一流拒绝；独立连接的流可并行。

## 3. GetBytes/GetChars

非空 buffer：只读取请求的区间并复制到 buffer；GetBytes 的 dataOffset 是字节，GetChars 是 CLR char 单元，CLOB协议单位由适配层换算。SequentialAccess 下 dataOffset 小于已消费位置明确失败，向前跳过可分块消费，不建全量中间数组。

buffer=null 的长度查询不能偷偷物化。BLOB 使用已验证长度元数据/GET_LOB_LEN；CLOB 如无法从服务端准确获得 CLR char 长度，允许用独立定位符有界扫描计算、不改变调用方逻辑游标。不能做到时必须明确声明该长度查询未支持，不能返回字节长度冒充字符长度。

合法的0长度读取不发无用消息；offset/length溢出、目标buffer越界在网络前拒绝。EOF返回0，非预期截断抛协议错误，不把缺少尾部当EOF。

## 4. 字符集

使用带状态 Encoder/Decoder，跨网络片段保留最多必要的尾部字节/代理项；UTF-8、GB18030等已声明字符集的边界测试必需。空字符串、NUL字符、中文、emoji、组合字符、代理对分割都覆盖；非法序列按S06拒绝，不替换丢字。

CLOB/NCLOB依照真实字符集/类型元数据处理，不能以“都是string”统一发送同一个错误codec。最后一块必须flush并检测不完整字符。

## 5. 输入流参数

支持参数 Value=Stream（BLOB/二进制大参数）或TextReader（CLOB/NCLOB），显式DmSqlType；长度未知、CanSeek=false也能流式上传。不得访问Stream.Length/Position作为前提，不做CopyTo(MemoryStream)+ToArray。

调用方拥有输入流，驱动默认不Dispose它；成功/失败后位置已经前移，重复执行不能自动回放或rewind，必须由调用方重建/定位并文档说明。

分块大小取协商限制/配置/当前编码完整边界；正确设置首尾标记并处理响应更新后的locator。用户取消、读输入流失败或服务端错误时停止上传，按S08处理会话，未确认的副作用不自动重试。

## 6. 全量API与内存指标

`GetValue/GetString/GetFieldValue<byte[]>` 仍可按其返回类型物化，但遵守MaxMaterializedLobSize。已知超限尽早拒绝；未知长度累计到上限拒绝，不能无限增长。告知使用GetStream/GetTextReader，而不是静默截断。值大小限额与.NET分配开销区别记录。

流式读的额外活跃内存应为 O(受控frame缓冲+chunk缓冲+解码状态)，不随LOB总长度增长。行内LOB可能已经随完整结果帧抵达，不能忽略frame大小把“零分配”作为虚假指标。

离线测试以生成器模拟1GiB+非seek流，不先分配1GiB数组；记录受控缓冲峰值。真实数据库测试建议至少64MiB BLOB与足够大的多字节NCLOB，具体大小可按测试实例资源显式配置，不把数字当服务器上限。

## 7. 验收

LOB-01：大字段GetStream第一块返回前未读取完整字段，峰值缓冲不随总长度线性增长。
LOB-02：不可seek且长度未知Stream/TextReader输入逐块发送，准确往返。
LOB-03：UTF-8/GB18030分片的每个可能边界、代理对、非法尾部。
LOB-04：reader前进/关闭、事务终结、连接重新租用后旧流不能访问新会话。
LOB-05：半途取消、输入流异常、GET/SET报文错误不回池、不重试、资源无泄漏。
LOB-06：GetBytes/GetChars偏移/长度/空buffer/0长度符合契约；没有先全量物化。
LOB-07：大值全量API超限明确抛错，不悄悄截断；流式API仍可读取。
LOB-08：使用新驱动nupkg的EF 40KiB LOB既有回归不退化；EF整实体string仍物化属于消费层，不算驱动流式失败。

---

<!-- source: specs/11-array-binding-and-batch.md -->

# S11 — 数组绑定、多命令与 DbBatch

状态：带协议门槛的实施规范。优先级：P2；R4。依赖：S05–S08；生产发布依赖S12。任务：T20、T21、T22。

## 1. 不混淆的四种能力

A：现有EF单修改命令中的DML+回读（S05，R1必须）。
B：相同SQL、不同参数组的数组绑定。
C：多个不同命令的真实批次传输/结果归属。
D：标准DbBatch API，包括工厂能力、结果、取消、事务、错误。

同样，一次物理Write不是一次逻辑request，也不是一次round-trip。网络合包/粘包不是批处理协议。

官方DbBatch定义以单次往返为设计目标，同时指出不同provider的错误/回滚语义可不同。[F08] 本产品决策：不以普通for+Execute伪装“原生DbBatch”作为第一版交付；可以有内部顺序参考执行器作测试oracle，但不因此把CanCreateBatch改为true。

## 2. T20 先交协议证据

对官方锁定资产、恢复版与真实DM分别验证：同SQL数组、不同SQL、参数数量/每行长度限制、失败位置、输出参数/生成键、每条影响行数、后续结果消费、是否仍可用、物理事务边界。记录实际协商协议与服务器版本。

每个probe产出支持/拒绝/未知，不从DmCommandSet、bulkcopy名字或枚举常量推导能力。对一次主执行的请求/响应计数要分开列出Prepare、Fetch、Commit、握手，不能用总体时间反推一次往返。

若C不能证明，结束本阶段时NativeBatch=false、CanCreateBatch=false；继续B的合法实现，不为了任务完成猜协议或客户端拼semicolon。

## 3. 数组绑定 first slice

建议提供窄的 provider-specific `ExecuteArray[Async]`（公开命名前需API评审）：一个已验证parameterized DML模板，参数metadata固定，多行值；不支持DDL、事务控制、输出参数、生成键回读、每行变化的SQL。

输入按配置行数/编码字节预算分块，不完整装入内存。严格区分：单参数字节限制、单行限制、参数数量限制、帧限制。某一限制的65535或64MB不能替代另一个维度。[R02][R03]

默认要求显式本地事务；不为调用方Commit。失败后停止发送后续块，抛携带rowIndex/blockIndex及可确定信息的异常。早期已写入块仍由调用方Rollback/Commit，文档不能叫“自动原子”；未知提交/传输结果保持Unknown。

结果每行状态可为Succeeded/Failed/NotExecuted/Unknown，RowsAffected可空。服务端只给总数时不能平均分配成每行1。首版不支持ContinueOnError；不提供自动逐行重试。

## 4. DbBatch API（仅C证据通过后）

实现DmBatch、DmBatchCommand、集合、Connection/DataSource/ProviderFactory创建入口及能力标志。通过.NET10 reference assemblies确认真实签名，禁止影子属性new伪装override。

每个命令拥有独立参数集合；批次内相同参数名不能互相覆盖。若wire需全局名，必须用tokenizer及唯一映射，不改字符串/注释/DMSQL内容。

执行期间冻结集合；Connection/Transaction一致性、空batch、重复命令对象、取消与Dispose有测试。一个batch占用一个execution lease，结果reader跨命令共享它。

ExecuteReader按命令顺序呈现rowsets，空rowset保留；每个batch command的RecordsAffected在可确定后更新。ExecuteScalar取第一可读值，但不能让其余命令永久不执行或吞掉错误。必须证明实现的reader早关策略，不靠偷偷提交未完成批次解决。

Timeout遵循S08的调用预算：一次执行/Read/NextResult中的多个子命令共享预算，不每条重置。支持批次取消不代表单独命令取消；首版统一abort物理session。

## 5. 原子性和错误策略

先明确范围再开放，不能任意SQL一律“隐式事务”。

- 有调用方Transaction：使用它，绝不擅自Commit/Rollback整个外部事务。可在已验证保存点能力下为整个batch建立内部保存点，失败回滚至它；无保存点则明确部分更改仍在外部事务内。
- 无调用方Transaction：只在已验证、事务性DML/查询范围内使用驱动拥有的本地事务；成功完整结束后Commit，任一错误Rollback；Commit确认丢失仍Unknown。
- DDL、显式事务控制、可能改变事务边界的过程/动态块第一版不进入上述原子性承诺，明确拒绝或不支持该batch形态。不能仅看首词SELECT就宣布无副作用。
- 服务端不能提供所需错误停止/结果归属/事务效果时不开放该优化，不拿客户端观测伪造rollback确认。

若支持batch reader，隐式事务不得在reader返回时提前Commit；需规定完整消费/关闭的提交规则。第一版建议：完整成功消费至终结才提交；提前Dispose则回滚隐式事务并明确其更改未承诺保存。调用方事务不由reader关闭自动提交。此与普通DbCommand语义差异必须写入API文档并通过测试。

## 6. EF 批次优化另立下游PR

DbBatch存在不会自动让EF现有SingularModificationCommandBatch使用它。下游必须单独实现批次生成、结果映射、生成键顺序、并发错误和回滚行为；继续以旧单命令链路为回退对照，运行时不半途切后端。[R06]

## 7. 验收

BAT-01：数组10/100/1000行，单行/最后一行/中间行失败、总数与每行已知信息准确。
BAT-02：参数数/字节/帧边界拆块；SQL和值不被拼接混淆；未知状态不伪造。
BAT-03：不同SQL+重名参数+空rowset+影响行数混合，结果顺序与归属准确。
BAT-04：外部事务不被擅自提交；内部保存点保护已有更改；隐式事务错误回滚验证最终状态。
BAT-05：reader早关与Commit响应丢失行为符合文档，无重放/回池。
BAT-06：相同业务语义下比较RT/吞吐/p95/分配；记录Prepare/Fetch等开销。
BAT-07：原生批次未证明时能力false；顺序参考执行器不作为native性能证据。

---

<!-- source: specs/12-tests-diagnostics-and-release.md -->

# S12 — 测试、诊断、CI 与跨仓库发布

状态：实施规范。优先级：P0，全程。依赖：分阶段。任务：T12、T18、T19、T23、T25。

## 1. 测试工程

UnitTests：配置、类型、有限状态机、错误分类、缓冲边界。
ProtocolTests：ScriptedTransport/FragmentingStream/FaultInjectingStream、握手/消息 golden fixtures、EOF、畸形帧、取消竞态。默认不连接真实库。
IntegrationTests：只连接显式提供的测试DM，用有限权限专用账号/独立schema；真实认证、查询、参数、事务、LOB、池复位及服务器状态验证。
下游测试：checkout固定EF commit，使用实际候选nupkg运行现有测试和新增改进测试；完整EF测试不复制进驱动仓库。

每项测试标 Category=Contract/OfficialCharacterization/Improvement；Feature/ServerProfile/RequiresNative 等属性使测试选择可审计。发现官方缺陷不要把`Assert.Throws`直接搬入Contract。

## 2. 协议测试缝隙

ScriptedTransport支持：期望请求类别与字节片段、任意分片返回、在指定读写序号注入EOF/异常、在屏障处挂起、取消、捕获网络入口是sync还是async、记录租约身份。控制调度使用barrier/TCS/TimeProvider，不依赖大批随机Sleep。

golden bytes来源必须是授权测试实例的合成数据或独立定义的协议样例。不能用同一份错误encode生成expected再decode，导致自洽但不互操作；至少有独立官方执行证据或审阅的固定样例。脱敏后改变字节/CRC的帧要作为新synthetic fixture，记录生成方式，不假称原始抓包。

畸形输入测试确保错误发生在分配前；测managed buffer峰值、请求计数和最终状态，不只期待异常类型。Fuzz先限定已声明parser入口、长度与运行资源，不发到生产数据库。

## 3. 真实库环境

唯一主机密变量：`DAMENG_TEST_CONNECTION_STRING`；TLS证书、原生库等需要文件时只读显式安全路径，不提交私钥。测试代码不打印完整连接串，不读取用户默认/生产连接配置。

环境清单：服务器自报版本/完整build、页面大小、兼容模式、字符集、隔离配置、runtime/OS/RID、驱动asset hash、测试commit、TLS/认证模式、时间与测试种子。敏感信息以最小必要标识替代。不同服务器版本记录不得拼成一个“最低支持版本”。[R11]

所有对象用可追踪随机前缀；清理只删除本次确切对象，不`DROP SCHEMA CASCADE`模糊匹配用户环境。DDL可能隐式提交，测试不能仅靠事务回滚清理。无测试连接变量时显式skip；在release模式必须FAIL缺环境而不是全部skip后成功。

最终状态用独立连接查询；未知提交测试至少记录两种服务端最终结果均可接受，但驱动对不确定结果的报告必须正确。不能因为某一次断线后恰好回滚，就把全部通信错误归类可重试。

## 4. 诊断 API

使用 ActivitySource/Meter 或等价.NET诊断钩子，不绑定具体遥测平台。建议低基数指标：连接创建/关闭、pool idle/leased/waiting、wait/connect/execute/fetch时长、timeout/cancel计数、broken/discard/reset原因、bytes sent/received、LOB chunks。

metric tag不带SQL、参数、完整endpoint/user/schema等高基数/敏感值；可以带版本、操作kind、已归类错误kind。默认Activity不记录SQL正文或参数；`EnableSensitiveDataLogging`首版不提供，以免不可控泄漏。

异常保留用于程序分类的结构化信息，但默认日志不得自动打印可能含业务字面量的服务器Message/SQL。提供安全formatter，对未知错误文本也保守处理。测试用姓名、证件号、密码等合成敏感标记检查所有日志/trace/异常快照和artifact不得泄漏。

用户日志回调不得在session/pool状态锁内运行；日志失败不能改变事务提交行为。不要在驱动内再次发SQL收集诊断元数据。

## 5. 跨仓库交接

`contracts/downstream-handoff.md` 记录：候选driver包版本、hash、SourceLink/commit，要求的EF分支commit、允许的行为差异、必测场景、新配置与公开类型调整。

EF当前具体绑定点：Connection创建、DmParameter/DmDbType特殊映射、异常分类、测试连接工厂；DateTimeOffset/TimeSpan文本回读；SQL%ROWCOUNT/生成键/保存点。[R06][R07][R08][R12]

更换NuGet源不会把依赖`DM.DmProvider`自动换成`W.DmProvider`；下游必须有明确引用新包与新namespace的接入分支。生产下游不同时引用两个后端做运行时fallback。O/R/W比较继续在隔离进程。

官方旧基线与候选W的比较要分开构建目录、lockfile、package-cache/中间产物；新的候选包ID/版本必须唯一，不能覆盖同名同版本的包造成cache误测。最终至少一轮用nupkg，不只ProjectReference。

## 6. 发布门槛

R1：公开surface/新配置文档；首批安全正确性测试；下游既有正确契约；未完成Async/Pooling等明确标注或拒绝。
R2：所有声明Async路径无sync fallback；取消/超时/Unknown故障测试和实际等待场景通过。
R3：目标OS/RID、目标DMprofile、实际TLS/native范围，池复位或保守discard行为、LOB流式、资源长时测试、最终nupkg下游全量验收。
R4：数组/DbBatch协议门槛、原子性/结果归属、RT证据和下游单独批次验收。

每次release依赖准确版本范围，不凭“编译能过”写所有DM8/所有EF10.0.x都支持；驱动独立SemVer，EF依赖独立版本化。CI保存依赖树、SBOM/来源清单、测试结果、包sha256；敏感数据检查作为发布阻断。

安全/事务/协议/池化变更须有人或独立审核agent审阅差异与故障测试，不能让实现者只用自己生成的expected数据自证。

## 7. 验收

QA-01：无数据库CI能运行offline测试；缺数据库的release job明确失败。
QA-02：每个新特性有Contract/Characterization/Improvement归属及状态记录。
QA-03：测试清理只影响本次对象，失败后也不泄漏长期锁/连接。
QA-04：最终nupkg在固定EF接入commit通过，包hash确为本次candidate。
OBS-01：合成敏感标记不出现在日志/trace/失败制品；诊断回调异常不改业务结果。
OBS-02：高并发与故障下计数可平衡，metrics不存在无限标签增长。
REL-01：发行记录精确列出支持与未支持矩阵；skip、未知和观察不被写为通过。

---

<!-- source: specs/13-later-capabilities.md -->

# S13 — 后续能力及进入实现的门槛

状态：research-gated。依赖：稳定R2/R3；不阻塞核心驱动。任务：T24。

## 1. 原生服务端取消

需要证据：取消请求是同连接还是控制连接、目标session/statement身份、请求/响应格式、认证权限、服务器仍执行时的同步恢复机制、取消后事务状态。必须用可控长查询、即将结束查询、已结束查询和旧statementid重用测试。没有这些证据，S08的AbortPhysicalSession就是正式可用实现，不把它写成原生取消。

禁止仅依据CMD_CANCLE常量构造64字节消息。原生取消若最终不能确认恢复，仍退为关闭物理会话而非自动重连。

## 2. 快速装载/FLDR

上游DmBulkCopy、DmBulkCopy2+FldrStatement、DmFldrExport有不同路径，不能统称同一个bulk能力。[R13]

进入条件：普通数组绑定/事务/结果错误可用；FLDR消息格式与版本明确；默认无敏感行明文日志；native资源路径可测。单独修setMaxRows恒抛、indexOption恒值、ByteArrayBuffer跨节点边界、失败free/成功泄漏；若这些代码在核心路径可达，边界正确性必须提前处理，不借“后续FLDR”拖延。

验证：批次大小/页边界、宽行、LOB/Unicode、约束/索引、错误行位置、取消、失败后部分持久化、是否绕过普通事务及重建索引/约束责任。没有证明不能承诺原子bulk或把它默认替代EF SaveChanges。

## 3. Prepare 与语句缓存

Prepare正确性可在S05支持；缓存是另一个优化。每个statement属于固定session/generation、SQL、参数metadata与影响语义的schema/设置。reset/DDL/参数类型变化、错误与连接断开均可能使缓存失效，先保守失效。

缓存容量有界、归还/丢弃恰好一次；reader持有期间不能把statement放回缓存。Lob/复杂类型内部借句柄也遵守S03。并发/异常路径无句柄增长后才打开默认缓存。

## 4. 高可用、自动重连、读写分离

先只支持显式Open中的未认证地址候选尝试。运行中故障恢复需要新连接；不能把旧事务/会话身份移植到新socket。

读写路由不能只看SQL首词：WITH、CALL、带副作用函数、SELECT FOR UPDATE等都需要保守处理。事务、保存点、临时表、会话变量与identity回读必须session affinity。未知语句走主库/拒绝，不能“可能是读”就送备库。

Failover的RPO、事务结果未知、读一致性、拓扑改变/凭据隔离都需要实际多节点环境；没有环境不能承诺HA。

## 5. 单资源ambient与分布式事务

后续单资源TransactionScope支持必须覆盖异步flow、Complete/Dispose、超时、连接close延迟归还和并发。事务身份不以GetHashCode作为唯一键，使用稳定事务标识+完整相等/对象引用语义。

多资源升级/XA/DTC是独立项目，要求协调器、持久化决策日志、prepare/commit/rollback/recovery、进程崩溃和in-doubt恢复、OS限制说明。两个本地事务依次Commit不是分布式事务。未完成前Promote/Enlist明确失败，不伪装成功。

## 6. 复杂类型、特殊加密、跨平台和AOT

ARRAY/Class/RefCursor/XDEC/Geometry需要独立参数与Reader/资源测试，递归深度/总字节上限和类型metadata生命周期。不要把数据编码能roundtrip自测当完整服务器互操作。

native/GMSSL/第三方cipher需要合法部署的精确RID资产与真实server安全模式测试。新架构预留接口不代表已经纯托管跨平台。

NativeAOT/trim是单独门槛：处理反射配置、资源和native加载、序列化、动态调用；先保证普通JIT .NET10。不要为了标记IsAotCompatible消除告警却不运行产物。

## 7. 每个研究任务的统一交付

提交 `docs/investigations/<feature>/`：原始问题、协议/公开文档来源、probe代码、环境manifest、实际结果、失败/未知、能否进入src以及推荐后续测试。不支持是合法研究结果，伪造已支持不是。

---

<!-- source: contracts/downstream-handoff.md -->

# W.DmProvider → EF 接入交接契约

## 固定基线

上游官方对照：DM.DmProvider 8.3.1.47463 / net9.0。
驱动调查：860988296cb95deeb600328cb7bcd2160237a94f。
已审阅EF：8dab4c0205f21fae4c8ca06fcbc39450eae2fe42。
实施时若分支更新，记录新的准确commit及契约差异，不覆盖用户更改。

## 候选包信息（由实现任务填写，当前没有候选驱动代码）

PackageId: W.DmProvider
PackageVersion: 尚未构建
SHA-256: 尚未计算
DriverCommit: 尚未实施
TargetFramework: net10.0（目标）
EFIntegrationCommit: 尚未接入
Evidence: spec_only

## 下游需要独立变更的点

1. PackageReference DM.DmProvider→W.DmProvider，lockfile和包缓存隔离。
2. DamengRelationalConnection创建W.Dm.DmConnection。
3. 特定DmParameter/DmDbType判断改为W类型，覆盖Clob/Blob/VarBinary/IntervalDayToSecond。
4. 异常分类识别W.DmException和FailureInfo；Unknown不得沿旧通信错误表自动重试。
5. 测试创建/脚本执行/检查结果的连接工厂同时切换，防止半套官方半套W。
6. 日期时间/TimeSpan的GetString兼容路径先保留，不为换驱动顺便重写类型映射。
7. 测试显式配置传输安全；测试库无TLS时明确PlaintextAllowed，不禁用证书验证。
8. 官方缺陷复现保留在Characterization，已修复问题以Improvement成功测试代替旧失败断言。

## ef-savechanges-v1 必测集合

查询/CRUD、null与Unicode、identity/sequence和额外生成列、0/1影响行及并发冲突、DML+SELECT组合、显式事务、保存点、RepeatableRead/Snapshot拒绝边界、可串行化纯ADO最小复现、LOB/日期时间/INTERVAL、迁移DMSQL块执行与已有脚本；后续Async/Pooling/LOB改动再加相应故障回归。

没有真实故障注入证据，不扩大重试范围；没有批次结果映射变更，EF继续原SingularModificationCommandBatch。更换驱动不等于自动获得EF批处理。

---

<!-- source: contracts/task-report-template.md -->

# 任务执行报告模板

Task ID：
源代码 commit（实施前/后）：
状态：not_started / in_progress / offline_verified / integration_verified / downstream_verified / blocked
本次范围：
不在本次范围：

## 行为变化

修改的文件与入口：
旧行为：
新契约：
关联规范和验收ID：
新增/变更公开API或配置：
是否修改来源快照（应为否）：

## 实际执行

| 命令 | 退出码 | 通过 | 失败 | 跳过 | 环境manifest | 报告路径 |
|---|---:|---:|---:|---:|---|---|

没有执行的命令及原因：
不得把“预期输出”放入实际执行表。

## 证据

真实driver asset sha256/MVID：
server build / profile：
.NET runtime / OS / RID：
Contract / Characterization / Improvement差异：
最终数据库状态验证：
取消/超时/失败后的connection/transaction状态：
内存/句柄/线程/往返指标（适用时）：
敏感数据检查：

## 审核与交接

已审阅安全/事务/协议/池化边界：
下游EF所需独立变更：
未解决问题与后续任务：
满足的release gate：
不得仅以“代码已生成”填写已验证。

---

<!-- source: SOURCES.md -->

# 来源、基线和证据等级

本包是设计规范；没有在本次编写过程中恢复编译驱动、运行DM实例或执行EF回归。仓库静态报告与测试代码被用于定义入口、待验证问题和下游契约；仓库记录的历史测试结果不是本次重跑结果。

源码引用固定到已审阅commit。外部.NETAPI按.NET10文档及runtime v10.0.0；实现时必须用实际锁定SDK编译确认API形状。

## 驱动仓库

- **R01** net9.0对照报告（静态记录）：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/net9-verification.md
- **R02** EF限制登记（包含静态结论和待动态验证项）：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/ef-adapter-backlog.md
- **R03** 网络协议报告（协议字段需按实际代码/版本复核）：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/02-network-protocol.md
- **R04** 类型与LOB报告：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/03-type-system.md
- **R05** 事务恢复源码：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/decompiled/net9.0/Dm/DmTransaction.cs
- **R09** 精确包元数据：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/packages/extracted/DM.DmProvider.nuspec
- **R10** Reader恢复源码：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/decompiled/net9.0/Dm/DmDataReader.cs
- **R13** Bulk/FLDR调查：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/04-bulkcopy-fldr.md

## 下游EF契约

- **R06** 更新SQL测试：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/test/W.EntityFrameworkCore.Dameng.Tests/DamengUpdateSqlGeneratorTests.cs
- **R07** TimeSpan绑定与文本读取：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTimeSpanTypeMapping.cs
- **R08** 隔离级别功能测试（含已知失败断言）：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengIsolationLevelFunctionalTests.cs
- **R11** 兼容性与历史验证记录：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/docs/compatibility.md
- **R12** DateTimeOffset文本读取：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeOffsetTypeMapping.cs

## .NET官方契约

- **F01** DbCommand.ExecuteDbDataReaderAsync默认同步fallback：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbcommand.executedbdatareaderasync?view=net-10.0
- **F02** DbDataReader.ReadAsync默认行为：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatareader.readasync?view=net-10.0
- **F03** DbDataSource职责：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatasource?view=net-10.0
- **F04** Stream.ReadExactlyAsync语义：https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readexactlyasync?view=net-10.0
- **F05** SslClientAuthenticationOptions/TargetHost：https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions?view=net-10.0
- **F06** EF连接恢复与提交不确定性：https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
- **F07** .NET10 DbTransaction源码（Save/Release/异步默认实现）：https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Data.Common/src/System/Data/Common/DbTransaction.cs
- **F08** DbBatch定义及provider差异：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbbatch?view=net-10.0

## 引用如何使用

R类描述“已有源代码/报告/测试所显示内容”，不自动证明所有服务器部署表现相同。F类仅描述框架契约，不证明达梦实现了该能力。S01–S13中的默认值、架构选择、状态机、异常形状、任务顺序、验收指标都是本产品建议的设计决策，不是冒充官方API已提供。

