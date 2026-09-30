# T05 wire 入口、所有者与守卫盘点

日期：2026-09-29。范围：T05 物理会话所有权；不改变旧协议编码、帧格式、TLS、异步或连接池。本文按当前产品源码盘点，测试结论以 T05 独立验证记录为准。

## 共同规则

每个活动物理连接有一个 `DmSession`。公开操作或内部握手先取得根 `DmExecutionLease`，调用期限内取得唯一 `DmInvocation`。`B.Wire` 在协议编码前调用 `Session.BeginWireExchange()`，在完整编码、发送、接收、校验及业务解码成功后调用 `Complete()`；异常令 `Dispose()` 将该身份对应的物理 transport 标为 Broken、分离并在状态锁外关闭。即使已收到完整服务器错误，当前实现也保守断开；不能将解析异常仅凭类型判为安全可复用。

`D` 的原始发送、接收、补读、读超时配置与 socket 探测先调用 `RequireActiveWireExchange()`，检查当前线程中的 exchange、invocation、根 lease 和物理 SessionId/LeaseGeneration。无主调用在触及 socket 前失败。`B` 原有协议锁不再控制关闭；关闭先在 session gate 中截取旧 transport 身份，随后直接关闭已截取的 `D`，不会等发送/接收或测试 barrier 释放协议锁，更不会读取新连接的 socket。

握手时 `D` 构造首先通过 `DmSession.RegisterPendingTransport(this)` 登记，之后才建立 TCP socket。`Close` 与该登记在 session gate 中线性化；旧握手晚到的建 socket/认证继续前检查原 invocation，失败后关闭本 `D`。`DmConnInstance` 构造完成并调用 `AttachTransport` 后由 session 持有实例。`D` 的最终器网络关闭已移除。

测试观察点 `DmWireTestHooks` 仅为 internal：`AfterExchangeEntered`、`AfterSendBeforeReceive`、`BeforeDecode` 回调只传 `OperationIdentity` 和 `DmOperationPurpose`，在 session gate 外运行；`SendCount` 计已返回的发送调用，`SentBytes` 计 `Socket.Send` 实际返回的字节或成功 SSL 写入的长度，不采集帧、口令或认证报文。钩子抛错会令 exchange fail closed。测试必须串行设置/清理静态钩子。

## 活动 wire 路径

| 原入口与调用来源 | owner / 完整 exchange 守卫 | 建议验收 |
| --- | --- | --- |
| `B` 构造：`A(b,b)` 协商，`a(b,b)` 登录；`DmConnInstance` 构造触发 | Handshake 根 lease/invocation；待建 `D` 先登记；两次旧协议消息各自 `B.Wire`，`D` 前置 raw guard；完整构造后 AttachTransport | SES-04/08；Close 与 pending connect、握手 barrier 交错，无迟到认证或存活 socket |
| `B.A(A,b,b,ref bool)`、`B.A(b,b,A)`、`B.A(b,b,A,string,bool,int)` 及其 bool 重载、`B.A(A,DmInfo)`、`B.A(int,A,DmParameterInternal[])`、`B.A(A,List<SQLProcessor.Parameter>)`：Prepare/普通 SQL/执行/批参数 | Query/Reader lease + invocation；各外层 `B.Wire` 包住 `C` 编码、旧 `A(b,b,int)` 收发和 `c` 解码。`B.A(A,DmInfo)` 的多条分块请求在同一 exchange 内 | SES-01/03/08；第二 command 快速拒绝且 SentBytes 不变，成功/解析异常分别 Ready/Broken |
| `B.A(A,DmResultSetCache,short,long,long)`、`B.a(DmResultSetCache)`、`B.A(DmResultSetCache)`：Fetch 和行缓存同步 | Reader lease + child invocation；每次外层 `B.Wire`，直到对应 response 被解码；reader 生命周期由调用方持有根 lease | SES-01/02/03；同 reader 并行 Read 拒绝、不同 session 可并行 |
| `B.__t02_method_06000A82`、`B.b(b,b)`、`B.a(b,b,int)`、`B.e()` 的 `COMMIT` MSG：Commit/Rollback/事务控制 | TransactionControl lease + invocation；旧协议三入口各自 `B.Wire`；COMMIT 走 `MSG<T>` 总入口 `B.A<T>`。`ClearTrx` 在完成 response 后执行 | SES-01/03/06；reader 并发事务控制零字节拒绝；未知结果断开 |
| `B.A(A)`、`B.A(A,short)`、`B.A(A,DmInfo,short)`、`B.A(b,b,A,string)`：statement close、cursor/result metadata | 活动 Query/Reader/Metadata lease + invocation；每条旧协议消息的 encode/send/receive/decode 同属一个 `B.Wire` | SES-06/08；关闭期间旧清理不得触及新连接 |
| `B.A(b,b,A,short,byte[],int,int)`、`B.A(b,b,A,int,byte[],int,byte[])`：参数 LOB 分块；`B.A<T>(MSG<T>)` 下的 `GET_LOB_LEN`、`GET_LOB_DATA`、`SET_LOB_DATA`、`LOB_TRUNCATE` | Reader/Query/Lob 根 lease 下的 child invocation；每个旧分块或 MSG 帧单独完整 `B.Wire`。多块循环不递归获取根 lease | SES-02/03/08；LOB 期间并发命令零字节拒绝，异常 Broken |
| `B.A<T>(MSG<T>)`：所有 MSG 派生类型（COMMIT、上述 LOB、FLDR_GET/SET/INSERT/BLOB/CLR/RESET 等） | 单一泛型入口执行 encode、加密/CRC、发送、接收、校验、解密、decode；`B.Wire` 罩住全段；`D` 每次 raw I/O 再查 active exchange | SES-03/08；MSG 与旧 SQL 不能交错，BeforeDecode 注入使 Broken |
| `D.A(byte[],int,int)`、`D.__t02_method_06000A4D`、`D.a(byte[],int,int)`、`D.B(byte[],int,int)`、`D.ConfigureReadTimeout` | 只接受活动 `DmWireExchange`；`D` 绑定唯一 `DmSession`。raw 方法本身不创造 lease 或 exchange | SES-08；无 invocation、错 session、错 exchange 在任何字节前失败 |

旧协议低层 `B.A(b,b,int)` 仍保留原编解码适配和协议锁，但不自行启动 exchange；它仅被上述外层入口调用，故不缩短消息的所有权期限。其发送之后、读取之前和读取/校验之后分别设有内部测试钩子。`D` 的 `Socket.Send` 短写、接收帧边界和超时预算属于 S04/T06；T05 不把一次 `Send` 返回当成完整帧证明。

静态调用图还有两个解码时二次请求：旧 `c` 解码游标参数时调用 `P_2.h().A(stmt,1)`，解码结果缓存通知（case 200/201）时调用 `P_1.h().A(P_1, ...)`。真实库离行 BLOB INSERT/UPDATE 对照又确认 `B.A(A,DmInfo)` 在主 SQL 尚未发送时，参数编码 `C.A` 会先调用 `B` 上传参数分块。`B.Wire` 仅在同一线程、同一 session/current invocation 的两种明确 continuation 阶段复用外层 exchange：已读完 response 的 decode，或上述参数编码上传；其他 encode/send/receive 阶段拒绝递归。二次请求失败会立即按当前身份 detach/abort，外层不能把失败完成为 Ready。ref cursor 与缓存通知仍只有静态覆盖，不能算真实库验收。

## 关闭、探测与禁用分支

| 原入口 | 当前处置 | 验证 |
| --- | --- | --- |
| `DmConnection.Close/Dispose`、`DmConnInstance.Close/AbortTransport`、`B.E`、`D.C` | 物理 session 按当前身份 detach，锁外直接关闭捕获的 D；关闭不发未证明安全的协议 cleanup。迟到的旧 invocation 因身份/状态检查无法触及重新打开的 transport | SES-04/05/06；after-send barrier 中 Close 不等待，旧回调解除后不能发送或收包 |
| `B.d` 和 `DBAliveCheckThread` 旧 heartbeat/reset | T04 已移除 `DmConnInstance` 入队/启动路径，当前 ADO 无活动调用；无 session owner 的旧后台工作不得重新启用。单独 `new D(host,port,timeout)` 只做 TCP 连通性探测，不发送协议字节 | 静态调用点核对；若将来启用需独立 handshake/validation lease |
| `DmConnInstance.IsSocketConnected` 原 `Socket.Poll` + `Receive(Peek)` | 明确 `NotSupportedException`，不允许绕过 session owner 的隐藏 socket 读取 | SES-08 负例 |
| `D.__t02_method_06000A63` 原 Poll/Receive 健康探测 | 保留代码但要求 active exchange；当前无活动调用者 | SES-08 无主调用拒绝 |
| 自动 Reconnect、pool validation/reset、旧 filter/native/FLDR 高级入口 | T04 默认禁用；T05 不赋予绕过 session 的 wire 权限。重新开放需单独 owner 与验收 | 配置/直接入口禁用测试；不以旧注释或存在代码推断功能可用 |

## 验证边界和待续风险

- 本文是入口与守卫清单，不能替代离线 SES-01～08、真实库或故障注入记录；测试 agent 在独立报告记录命令、退出码、服务器侧状态。无测试连接时标 `integration_pending`。
- 旧 TCP 建连代码仍含多地址回退、二次同步 `Connect` 和进程共享事件；T05 在每次新建 socket 后检查当前 handshake 身份，并允许 Close 立即关闭已登记的 pending `D`，不声明完整 T06 建连超时/异步行为。
- 旧 `Socket.Send` 可能短写，旧 `B` 收包长度限制及边界尚待 T06。`SentBytes` 仅为测试观测计数。
- 被禁用的 heartbeat、pool、FLDR 或 Reconnect 不能因为 T05 主路径通过而视为已验收。真实服务器错误在当前保守策略下可能断开连接，复用优化需单独证明状态同步。
