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
