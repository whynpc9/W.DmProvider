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
