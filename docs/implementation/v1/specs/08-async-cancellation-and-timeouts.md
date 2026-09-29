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
