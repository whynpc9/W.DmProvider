# R3 实施设计：DataSource、保守连接池、流式 LOB 与独立验收

设计状态：`design_ready`，尚无 R3 实现或验收结论。设计依据为当前 `ed0c8a1` 工作树、`progress.json`、v1 S09/S10/S12 及 release-gates；旧报告的 working-tree/未提交说明是历史运行身份，不覆盖当前 Git 状态。用户本轮要求完整实现 R3，并指定 Astra High 设计、Sol High 编码、Sol Low 独立验收；root 最终审阅。本文仅为设计，不替代任务报告。

## 1. 范围、顺序与前置状态

本轮完整范围按 `T15 → T16 → T17 → T18 → T25 → T19` 推进。T17 在原 DAG 中只依赖 T09/T14，但共享产品、构建窗口和故障分类使顺序实施更便于审阅。最早可执行任务是 T15。T01–T14 原候选已验收，不重新执行历史任务；R2 review 的当前候选为 603 离线通过、TLS verified、shared integration_pending，同跨页负载在旧 accepted 包也超时，精确恢复已完成。

R3 可以先在明确的 TLS profile 上完成 T15 和后续功能，shared 当前阻塞必须留在 R3 最终 profile 矩阵中。不能以 TLS 成功、减少负载、放宽原超时、跳过 shared 或静默缩减发布矩阵宣称完整 R3 验收。继续能够独立完成的编码、离线验证、TLS 门和候选材料；最终未满足的真实环境门标 pending。用户本轮未授权远程发行，T19 只生成候选和发布材料。

每阶段开始前 root 保存此前 evidence SHA、accepted source/package 清单；旧证据不改写。Sol High 只写本阶段明确授权文件，不运行 dotnet/实库。root 静态审阅后整体冻结，Sol Low 独占 build/test/DB 窗口。失败先停止并释放窗口，再定点解冻给 High、重新冻结并新建 run；保留失败记录。没有需要合成数据的任务时不用额外 agent；如需大量合成向量，另由 root 按 AGENTS 分工派发。

| 阶段 | Sol High 文件归属建议 | Sol Low 验收与 root 收口 |
| --- | --- | --- |
| T15 | `tools/R3ResetProbe/**`、`eng/t15.sh`、`eng/T15.md` | 基线 nupkg probe、能力台账、reset/discard 决策 |
| T16 | 新 `PublicApi/DmDataSource.cs`、必要 DataSource command wrapper；`Internal/Pooling/**`；Connection/Settings/Builder/Factory/Sessions 必要改动；新 PoolTests 与 R3PoolProbe | POL-01–10；配置隔离、计数、并发与真实 discard |
| T17 | `Internal/Lobs/**`；Reader/Parameter/Types/Legacy LOB 与上传链；新 LobTests 与 R3LobProbe | LOB-01–08；端到端流式、字符边界、真实大值和故障 |
| T18 | `Internal/Diagnostics/**`、公开安全 formatter；核心中最少观测接点；新 DiagnosticsTests/资源 probe、阶段 runner | OBS-01/02、QA-01–03、长时资源与敏感标记检查 |
| T25 | 由 root 给 High 派发独立审核发现所需的精确测试/修复文件；不由实现者自定通过 | root 从调用方进行独立静态审阅；Low 执行独立场景、故障和资源回归 |
| T19 | 新 R3 下游 runner、必要新隔离 archive patch、包元数据/README | final nupkg 精确包身份、固定 EF 全量门、支持矩阵/SBOM/回退 |

表中目录是阶段边界，必须按实际任务再细化，不代表可同时跨阶段修改。`docs/implementation`、兼容矩阵、公共 evidence 和进度由 root 独占；本文由设计 agent 独占至交付。

## 2. T15：复位能力探测与可执行的安全结论

### 2.1 默认决策

S09 明确允许 `VerifiedResetOrDiscard`：只有得到完整状态恢复证明才复用；未知状态销毁。因此 T15 不以找到万能 reset 为前提，也不允许编造 reset opcode、SQL 首词白名单或将 ROLLBACK/SET SCHEMA/SELECT 1 当完整复位。

本轮安全默认是 **每次物理租约归还均 discard**。这包括只执行 SELECT、无业务操作、已明确提交或回滚的租约；不需要危险地猜测调用者 SQL 是否有副作用。将这一默认实现为生产中固定的安全决策，原因码 `reset_not_verified`。只有后续另有对应版本、权限、状态范围和故障证明，经 root 审阅后才可启用具体 reset 路径。T15 的证据可支持此 discard 决策，不应称为“完整 reset 已验证”。

业务会话返回均销毁时，T16 仍提供有界物理并发、可取消等待、DataSource 所有权、配置隔离与生命周期。实际 `reset_success=0`、`reuse=0`，每次重新认证；不得宣传减少握手或执行池化性能提升。生产不放置可开关的弱复位逃生口。

### 2.2 探测台账和独立包工具

R3ResetProbe 使用精确 `PackageReference` 的基线候选，记录源/包/DLL hash 与 MVID；不改当前产品来让 probe 通过。先 TLS wrapper 验证 `USER` 与当前 schema 均为 `WDM_PROVIDER_TEST`，再执行有限权限探测。共享 profile 的每次新 run 同样先验身份。

台账每类记录 `tested_reset`、`tested_discard`、`unsupported_by_profile`、`not_probed` 中的真实状态，附服务器 build、权限事实、观察字段、调用/退出码、独立最终状态和结论。至少覆盖：

| 状态类别 | 可验证的目标与权限边界 |
| --- | --- |
| transaction/autocommit/isolation | 本次唯一表中未提交写入，关闭旧连接，独立 fresh 连接检查未提交数据不可见且后续可更新；记录默认与显式隔离 |
| schema | 仅在当前获授权 schema 内测试设置与新登录基线；无第二授权 schema 时不跨 schema 探测 |
| role/授权会话态 | 只探测 TEST 现有权限可执行的会话角色操作；不 GRANT/REVOKE，不创建用户/角色，不读 SA |
| timezone/NLS/language | 使用有确定来源/服务器支持的会话设置和回读；不猜多个语法后把任一成功称全量覆盖 |
| temporary objects/session variables | 唯一本次对象，区分对象定义可能永久与数据/变量会话性；关闭后 fresh 验证，精确清理定义 |
| session locks | 用本次唯一业务对象和两条 TEST 连接制造可控事务锁等待；关闭持锁连接后另一连接完成；任意命名锁只在接口有来源且已获 TEST 权限时测 |
| cursors/prepared statements | 持有 reader/prepare 后关闭，旧对象必须无法继续；新连接业务成功；能观测服务器句柄则记录，不能将客户端失效等同服务器全计数已验证 |
| package/procedure session state | 目标负载涉及且 TEST 可创建时只在唯一对象探测；无权限/不涉及明确记账，不把缺测当 reset 证明 |

新连接身份不同可借安全 server session 标识证明；若无可用标识，记录客户端物理创建/关闭计数、对象行为和未提交事务独立结果，并明确证据限制。探测未知 server reset 能力时只查已存在授权源码/协议说明/有限目录能力，不盲发网络 opcode；没有来源就直接记录未证明。

root 本轮已查得的官方语法来源为[数据定义语句 §3.14](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)的会话 `SET TIME ZONE '+9:00'`、`ALTER SESSION SET NLS_DATE_FORMAT='YYYY-MM-DD'`/`NLS_DATE_LANGUAGE=ENGLISH`，以及[管理表 §9.2.5](https://eco.dameng.com/document/dm/zh-cn/pm/management-table.html)的会话临时表 `ON COMMIT PRESERVE ROWS`。可据此扩充 probe，并仍须当前 profile 实际执行及回读。没有可信的 session id/role/命名锁/package-state SQL 时保持未探测；检索未找到 reset 不能推断服务器不存在 reset，只能说本候选尚无可用复位证明。

退出条件是足以支持 `VerifiedResetOrDiscard=discard all` 的关闭边界证据和完整台账，而非所有服务器能力被证明支持。任何真实库失败保留原始结果与清理未确认状态；独立恢复成功单列，不回写失败为通过。Probe 输出只含安全结构化类别和受控对象标识，不直接序列化含 SQL 字面量的异常。

## 3. T16：物理容量、DataSource 与租约生命周期

### 3.1 API 和配置

增加 `DmDataSource : DbDataSource`，构造时冻结 `DmConnectionSettings`；CreateConnection 返回 Closed，OpenConnection[Async] 返回 Open。显式 DataSource 各自拥有独立池所有者，即使配置完全相同。`ConnectionString` 始终安全展示，内部持有真实配置，禁止将已脱敏串反向解析成连接。

`Pooling` 默认 false；为兼容已有 builder 选择一个规范键并将 `Pooling`/既有 `ConnPooling` 映射到相同不可变值，`MaxPoolSize`/既有 `ConnPoolSize` 同理。保留 `PoolAcquireTimeout` 独立总预算；0=无限、负数拒绝。新增有界等待数量设置或明确固定内部上限，并文档化满队列的 fail-fast 异常；不要把等待者数量等同物理连接数量。默认容量、等待上限和 registry 上限须在实现/测试中是明确常数且报告记录。旧 `StmtPooling/PreparePooling` 仍无完整证明时维持拒绝，不通过启用旧连接池带入 statement cache。

直接 `new DmConnection(connectionString)` 仅当显式 Pooling=true 使用受控 registry。显式 DataSource 的 owner 生命周期始终独立；Pooling=false 仍可用 DataSource 快捷 API，但每连接独立创建/关闭。不能让改变已关闭连接的 ConnectionString 将原 DataSource 所有者静默迁移到别的配置；对 DataSource 创建的连接应拒绝与冻结配置不同的配置写入，或明确解除其所有权，首版选择前者更简单可审阅。

### 3.2 真正的容量管理而非旧池复活

增加独立 `DmConnectionPool`（或同职责类）与一次性 `DmPoolLease`。不启用旧 `DmConnInstancePool` 和 legacy filter。物理连接许可从预留创建开始占用，贯穿 creating、leased、resetting/closing，只有物理 transport 完成关闭/中止之后释放。异常、Open 取消、初始化失败、重复 Close/Dispose、reader CloseConnection、DataSource 快捷命令失败均只能归还一次。

全部归还销毁策略可直接使用容量调度器，无需实现生产中永远无法安全到达的伪 idle 复用。状态快照仍区分 `creating/leased/closing/waiting` 并将 `idle/resetting` 明确为零；接口为日后经证明的复位预留决策点即可，不用测试虚构 reset 成功然后声称生产可复用。POL 的 reset_failure 分支以受控维护决策/关闭故障检验销毁和容量，不伪造实际 server reset。

队列使用有界 FIFO、`TaskCompletionSource` 的异步 continuation 和原子 waiter 状态。锁内只转移 owner/状态，不发网络、运行用户回调或 Dispose cancellation registration。取消与授予竞争通过单次 CAS/锁决议：取消胜出则不交付；交付胜出则受让者负责归还，不能双发。清理超时先使 transport 不再可用，再释放许可；单纯从集合移除不能代表网络已关闭。

获取开始捕获一次 `PoolAcquireTimeout` deadline；唤醒、建连失败和重新排队不重置预算。排队取消是明确 NotSent，不使其他租约 Broken。建连阶段使用 R2 的 ConnectTimeout 和用户 token，并由最早到期预算限定整体获取；明确失败阶段 PoolWait/Connect，保留已有 typed 首因。同步 Open 也可等待同一容量协调器，但不改变单连接命令并发快速拒绝规则。

### 3.3 Registry 与证书身份

`DmPoolKey` 是显式实现相等/哈希的密封不可变类型，不能用默认 record.ToString。比较所有实际生效设置，包括 host/port、大小写敏感用户/密码/schema、language、TLS 策略和 revocation、CA/客户端证书/私钥身份与密码、各预算/池容量和所有新 wire 设置。完整相等决定共池，哈希仅为查找；安全 ToString 不返回秘密或完整 endpoint。

证书路径相同不等于证书相同。推荐在冻结 TLS 身份时按实际读取的内容形成证书/信任材料 fingerprint，实际认证必须使用同一份快照，避免 fingerprint 与随后加载之间 TOCTOU；私钥指纹留内存不输出。若现 transport 无法做到这一原子快照，首版对含显式证书文件的直接连接采用不共享 registry owner 的保守隔离，并公开此行为；显式 DataSource 仍有独立容量域。绝不能路径不变轮换后沿用旧身份。凭据变化自然生成新完整 key，ClearPool 支持显式退休。

registry 池数量严格有界；满时仅移除无正在创建/借出/关闭/等待且无在途 owner 获取引用的过期项，关闭移除项在锁外执行。不能因为 leased=0 就删掉仍有 waiter 的池，否则相同配置可能分裂成两个并发容量域。所有项忙时新 key 明确失败，不偷偷建未计数全局池。用一个受控调度器+TimeProvider 维护 registry，或在无 idle 的策略下用访问驱动的安全过期回收；不得每条连接创建永久 timer/thread。

### 3.4 清除、Dispose、代际与快捷命令

ClearPool/ClearAllPools 递增 owner epoch；当前租约继续执行，归还销毁；不能在 Clear 时给同 key 建一个未计入旧活跃连接的新容量域突破 MaxPoolSize。DataSource Dispose 关闭入场、取消等待者、停止并观察维护任务，返回时不无限等待业务；已有租约按文档可完成并归还销毁。Open 与 Dispose 竞争须确保已经保留的创建连接不能在 Dispose 后作为新租约交付。

保留 `OperationIdentity(SessionId, LeaseGeneration, ExecutionId, InvocationId)` 的完整验证。all-discard 时每次物理 session 都有新 SessionId，不需要把旧对象迁移到新 session 来假装复用。池租约另有 owner epoch/lease id，用于一次归还、Clear 和任务关联；如实现真实 reuse，必须在 checkout 新增 generation、彻底解绑旧 DmConnection/onBroken handler/事务/statement/取消注册，并取得 T15 的独立复位证明。

`DmConnection.CloseExpectedSession` 当前捕获旧 session 后从连接解绑并 AbortTransport，是 all-discard 的安全基础。接入点在 detach 前捕获一次池租约，transport 关闭后才释放其容量；不能在 close 入口提前 release。旧 reader/transaction/LOB 的 Close/Cancel/Dispose 只作用于其捕获身份，不能碰 reopened connection。

DataSource.CreateCommand 需遵守 DbDataSource 快捷语义：非 reader 完成/失败归还内部连接，reader 持有到 Close/Dispose，Prepare 不创建泄漏，取消和重复 Dispose 恰好归还一次，Async 不同步网络 fallback。可用内部 DbCommand 包装器代理 DmCommand；command.Parameters 的用户参数在多次执行间保留，而每次内部连接/plan 生命周期清楚。当前 DmCommand 是未密封 class；可选择独立 wrapper 或继承，但必须证明参数/plan/连接生命周期；若用框架默认实现，必须独立测试实际 .NET 10 行为及失败路径，而非假设框架替本驱动处理所有归还。

### 3.5 验收

POL-01–10 映射到确定性 barrier/TimeProvider 离线测试和包实库 probe。必测 MaxPoolSize=1 排队取消、队列溢出、创建/初始化/关闭失败、Close 重入、Clear+Open+Dispose、证书/凭据轮换、同 key churn、DataSource reader ownership、旧取消 callback 不伤新连接。真实库必须观测每次创建/归还销毁、未提交事务最终状态、零复用率；能力台账未证实的状态保持 discard 的推导边界。没有 idle 生产路径时不把 idle eviction/健康检查伪报为实测复用能力。

## 4. T17：端到端分块 LOB

### 4.1 当前接通点与新增结构

当前 `DmDataReader.do_GetBytes` 先调用 `DmGetValue.GetBytes` 全量物化；CLOB 的空 buffer 长度路径调用 `MaterializeStringUnderOwner`。当前 `DmClob` 分块方法每片独立 `Encoding.GetString`，不能保留编码边界状态。输入参数 `DmParamValue.m_InValue` 是 byte[]，`GetStreamLen/GetBytes` 实际仍围绕已物化数组；仅把 reader 返回 MemoryStream 或为这些数组改名不能满足 T17。

建议新增 `Internal/Lobs/DmLobReadCursor`、只读 `DmLobReadStream`、`DmLobTextReader`、`DmLobInput`。单个 cursor 区分 wire server offset、已解码 CLR char offset 和缓冲剩余；不共用一个数字。它持有父 reader、row/result version、session/execution/generation 身份和 locator snapshot。

具体网络接点：同步 `Internal/Legacy/A/T02_02000091.cs`（partial B）；异步 `A/B.Async.cs` 的 `ReadLobAsync(AbstractLob,...)`/`WriteLobAsync`；消息 `Dm/T02_0200008A.cs` GET_LOB_DATA（已有 opcode32）、`Dm/T02_0200008E.cs` SET_LOB_DATA（已有 opcode30）；参数上传在 `A/RequestCodec.Async.cs`、对应 sync codec、`B.Async.cs` 的参数上传循环与 `DmParamValue/DmParameterInternal/N2DB`。复用已验证协议，不增猜测 opcode。

新读 cursor 必须直接消费返回 `Data.value/Data.len` 且更新 `readOver` 的底层 GET_LOB_DATA 路径，不能沿用 `ReadLobAsync(DmBlob,...)` 这种预分配整个请求长度并在提前 readOver 后仍返回整数组的 helper；否则短读会被补零隐藏，EOF与截断也无法区分。同步等价 helper 同样审计。每次返回实际读取数，再由上层按可信总长度/终止标记判断 EOF 或协议截断。

### 4.2 读取 API 与生命周期

override `GetStream`/`GetTextReader`，创建时只检查 ordinal/type/null、解析本行 locator，不调用 GET_LOB_LEN/GET_LOB_DATA 读完整字段。BLOB stream `CanRead=true/CanSeek=false/CanWrite=false`，Length、Position set、Seek、SetLength、Write 明确 NotSupported；Position get 可报告已消费字节或明确不支持，文档与测试一致。

每 reader 只允许一个活跃字段流；创建第二个显式拒绝直到首流 Dispose，避免隐藏并读。向下一行/结果前，以及 Close/Dispose/事务终结/连接关闭时失效旧流；后续 Read/ReadAsync 必须在任何网络前拒绝。非 SequentialAccess 也遵守 rowVersion，不能因底层字段缓存还在而继续访问过期 locator。SequentialAccess 再加 ordinal 和消费 offset 单调约束，读取另一列需先关闭/失效当前流；向后访问拒绝。

每次 Read[Async] 借用 reader 现有 execution lease 下的 child invocation，不递归新建根 lease。流同步/异步并发用单操作门快速拒绝；取消只绑定当前调用身份，沿用 R2 的未发可恢复/发后 Broken 与 no replay。父 reader 前进与流读取竞争要由同一 lease/invocation 门线性化，不能销毁正在使用的缓冲又让旧读完成。

实际请求块不超过 `min(LobChunkSize, negotiated MaxLobDataLenPerMsg, request/frame limit)`；大调用者 buffer 也多次有界分块。局部 inline LOB 可以已随结果帧到内存，直接提供只读视图/受控复制，不夸称 frame 零内存；out-row 不能完整缓存。GET response 先校验长度/剩余帧/请求允许范围再分配，非法负长度/巨大长度/非 long offset 溢出明确协议失败。已知尚有数据或 readOver=false 时无进展空块不是 EOF；最后完整终止条件才返回0。

`GetBytes` 对 BLOB 使用 byte offsets；`GetChars` 对 CLOB 使用 UTF-16 char offsets。非空 buffer 只读取目标范围，forward skip 分块丢弃。可定位非 SequentialAccess 请求从独立 locator snapshot 开始或安全重新定位；不能沿用已推进的 locator 假装 offset0。buffer=null BLOB 使用验证长度元数据/GET_LOB_LEN；CLOB 用独立 cursor 有界扫描统计 UTF-16 chars，不改变主 cursor，若不能安全独立扫描则明确 NotSupported，不物化或拿字节数冒充。合法零长度、末尾 offset==buffer.Length、溢出、越界先在网络前处理。

### 4.3 字符与 wire 单位

采用 strict Encoder/Decoder（ExceptionFallback），逐片 `Convert`，保留必要尾部字节/高代理项，最终 flush 验证不完整字符。GET `Data.len` 是服务端推进单位，不等于 byte[].Length 或 CLR string.Length；非负已验证 `Data.len` 用于推进服务端 cursor。没有该元数据的 profile 只可依据已证明的协议单位换算，未知单位明确不支持对应 streaming 路径，禁止猜 emoji 长度。

NCLOB 根据真实类型/charset 元数据选 codec；不能因为返回 CLR string 就总用 ServerEncoding。验收逐个切割 UTF-8/GB18030 编码边界，中文、emoji、NUL、组合字符、代理对、非法尾部；离线 synthetic 固定字节预期由独立规格/明确向量给出，实际 server profile 往返确认 wire 单位。只验证一个 UTF-8 profile 不宣称所有 charset 实库通过。

### 4.4 输入流参数

参数 Value=Stream/TextReader 时要求显式现有 Dm 类型属性（以当前公开 `DmParameter` 类型枚举/API 为准，不为 S10 文中 `DmSqlType` 字样新增冲突公开类型）。Binary/Blob 与 Clob/NClob 匹配不明确时在网络前拒绝；禁止访问 Length/Position/Seek，禁止 ToArray/全量 string；未知长度、不可 seek 必须可工作。

扩展冻结绑定模型保存受控 input descriptor，而非转换到 `m_InValue`。参数上传 sync/async 读取调用者的相应 API，async 无 sync fallback；按真实既有参数上传协议发送，不能把 locator SET_LOB_DATA 与参数 off-row 协议混用。采用一块 lookahead 或既有尾空块规范确定首尾标记，覆盖空输入、恰满块、末短块；每次响应更新 locator/已确认长度，不能拿发送长度代替 ACK 长度。首尾语义必须用固定协议测试及真实库确认。

调用者拥有输入，成功/失败均不 Dispose/rewind/replay。重复执行视为当前流位置的新读取，文档要求调用方重建或定位；同一非 seek input 对象出现在多次绑定/重复命名占位符而协议需多次消费时应在读取前明确拒绝，不能静默第二次上传空数据。输入读异常发生在任何可能发送之后按已发失败保守 Broken，未发则保留 R2 NotSent；不自动重试。

全量 API 继续保持 MaxMaterializedLobSize：已知超限提前拒绝，未知长度累计计数受限；流式 API 不受完整值大小上限阻止，只受分块/帧限额。char payload 与实际分配开销单独解释。既有 explicit DmBlob/DmBLobStream 的 seek surface 不作为新 reader stream：如顺手修复已观察到的 Position setter 错误，须独立兼容测试和 root 定点授权，避免扩大变更。

### 4.5 验收

LOB-01–08 全覆盖。离线生成器提供 >1GiB 非 seek 未知长度逻辑数据，无预分配同等数组，度量峰值受控 buffer 与消息计数；第一块返回前读取量明显小于总长。读写截断、取消、输入异常、父失效、向后偏移、并发读和旧 generation 用 barrier 驱动。

真实包 probe 默认至少64MiB BLOB，另设足够大多字节 CLOB/NCLOB（字符集实际支持）；大小作为明示测试输入写入 manifest，不以小值通过冒充大值。先流式上传、再分块 hash/字符计数校验，避免 probe 自己物化；超限全量 API 拒绝与流式成功同字段验证。最终 T19 使用同候选包跑 EF 40KiB 既有回归。

## 5. T18：标准诊断、故障与资源

新增平台无关 ActivitySource/Meter，固定名称与版本。最小观测包括 physical created/closed、pool creating/leased/closing/waiting、wait/connect/execute/fetch duration、timeout/cancel、broken/discard 分类、bytes sent/received、LOB chunks。标签只用有限 operation/reason/error 枚举；不带 SQL、参数、用户、schema、完整 endpoint、pool key、session id 或 exception.Message。默认不提供敏感日志开关。

提供安全 formatter 输出异常类型、FailureInfo 的受控枚举/数值和稳定代码；未知异常文本统一隐藏。不改变异常本身供应用诊断的契约，不自动将原始 server message 写入 Activity event。现 DmTrace legacy facade 继续关闭，不复活文件日志。

System.Diagnostics listener 也可能执行用户代码；每个 Start/Stop/Add/Record 接点在 session/pool 锁外，隔离 listener 抛错，不能修改 Commit ACK/Outcome、掩盖原始异常或使容量漏还。诊断 snapshot 锁内只复制计数，实际通知在锁外。开始/结束、queued/granted/canceled、create-fail/close 必须计数配平；没有 listener 时保持低开销。

资源验收分离可重复逻辑压力与 OS 实测。使用固定种子并混合独立连接、多 key churn、队列取消、握手失败、stream 异常、Clear/Dispose、旧 reader/取消回调；记录请求数、全部状态归零、创建关闭差额、registry 峰值、buffer 峰值。长时实测在 warmed-up 进程取多时间点 RSS/GC heap/thread/handle/FD（OS不支持的指标明确 unavailable），记录持续时间与吞吐；不要用单点 GC 后内存下降证明无泄漏。

建议验收标准：离线压力至少10,000次租约和多轮 registry 上限 churn；真实 TLS 资源 soak 至少10分钟且至少数百次 create/return，测试前预先固定时长/次数，不能失败后缩短。受机器资源限制时报告未完成长时门，继续其他交付，不把短 smoke 改名 long-run。稳定资源以设计上限、最终归零和多轮增长趋势共同判断；JIT/线程池一次性增长与持续线性增长分开记录。

敏感标记由纯合成 SQL/参数/用户名/密码/证书密码和服务器报错构成，监听 Activity/Meter/formatter/probe stdout/stderr 与失败制品；确保输出不存在标记。原始 credential env、auth frame 不进入扫描制品或日志；不能为了证明脱敏先把真实秘密写入文件。T18 还提供无 DB offline runner、缺 DB release 模式失败及精确测试 manifest。

## 6. T25：独立逆向验证

root 从公共 API 和外部调用者视角提出独立测试输入，Sol Low 执行冻结产品；修复仍交 Sol High，重新冻结后完整重跑受影响门。至少覆盖：DataSource Dispose 与 Open 的交错、Clear 中活跃租约容量不翻倍、同路径证书轮换、不同秘密不串 owner、callback 抛错/重入、重复 Close、旧 transaction/reader/stream 不能伤新 session、取消与分配同时发生、输入流抛错前后发送边界、CLOB emoji 切分、未知提交不可复用、原始 SQL 改会话后必 discard。

审核者必须审阅一次生产 call graph：参数到 wire 的 async 输入读取，GetStream 到 GET_LOB_DATA，Close 到 transport shutdown 到 permit release，DbDataSource command 到 reader Close，diagnostics 到用户 listener。不能只依赖实现者写出的 expected 或 happy-path 计数。记录发现、精确位置、修复版本、未覆盖项与结论；无问题也写审阅范围，不能用“测试全绿”替代独立边界审阅。

## 7. T19：最终包、下游与发布材料

T18/T25 完成后冻结最终源，生成唯一 `0.1.0-r3.<timestamp>` candidate nupkg。记录 package SHA、包内/consumer实际加载 DLL SHA/MVID、accepted source manifest、当前 commit 与若有未提交 diff hash、SDK/runtime/OS/RID、依赖树和 SBOM/第三方来源。当前项目关闭 SourceLink，不能伪称有可验证 SourceLink；要么正式接通并验证，要么明确以 source archive/hash 提供来源。

从既有 T12 接入证据选固定 EF commit 的新隔离 archive，在新目录应用明确 W bridge 和当前 schema fixture；原 EF 工作区、历史 archive/evidence 只读。依赖须真为 W.DmProvider 精确版本，最终验收不用 ProjectReference 或旧缓存。覆盖 ef-savechanges-v1 全量合同、Async/事务/取消回归、Pooling=true（诚实 discard）下 CRUD/保存点/生成键、不在 DML+ROWCOUNT 之间额外验活、40KiB LOB；下游需更改生产行为时另列 handoff，不偷偷扩展其他仓库。

准确矩阵列出实际测试 OS/RID、DM完整 build/profile、TLS/明文/CA/native范围、字符集和 LOB 上限测试值、reset/discard 决策与复用率；未知/unsupported/pending 单列。没有 native 需求的纯 managed 路径记录 not_required，不凭 managed 构建宣称 native 插件全支持。shared 未恢复原回归时，R3 release gate 保持 integration_pending；不要把功能开发完成等同生产发行通过。

交付 root 更新 reports/T15/T16/T17/T18/T25/T19、R3报告、progress、兼容性与包 README，附具体命令、退出码和独立服务器最终状态。回退说明为 pin 到明确旧候选/关闭新 Pooling、重新创建连接/数据源；不得建议重试 Unknown 提交。T19 不 push tag、NuGet 或 GitHub Release。

## 8. 最終判定

实现完成要求所有本轮产品契约和离线/指定 TLS 门实作并独立验证，不允许以 all-discard 理由省略容量、registry、DataSource 或流式 LOB。完整 R3 验收要求 T15/T16/T17/T18/T25/T19 的对应证据全部满足最终 profile 矩阵；缺环境或未完成长时/下游门须保持显式 pending，并交付现有源码、冻结候选和剩余门清单。`production_release_accepted=false` 在未获发行验收与授权前保持。

## 2026-10-02 T16 实施定案

证书文件 fingerprint 与实际 TLS 加载不能在当前 transport 中保证同一原子快照。为保持直接连接的共享容量与身份边界，首版对显式证书文件且直接 `DmConnection Pooling=true` 明确 `NotSupportedException`，指导使用独立 `DmDataSource`；不能为每条连接静默创建新 owner 绕过 MaxPoolSize。默认系统 trust、无显式证书文件的 TLS 仍可用 registry。DataSource 的容量 owner 独立且稳定，物理归还全部销毁，每次重新认证；证书文件本身不宣称为不可变字节快照，证书轮换建议重新创建 DataSource。该限制须列入兼容矩阵与公开文档，实测 TLS 池生命周期使用 DataSource，shared 可验证直接 registry。

## T16 实库错误分类定点设计

v1离线685通过，但TLS shortcut重复主键错误(-6602)的既有通用decoder没有严格receipt标记，最终Transport/Unknown。Astra/root选择窄产品改进：严格完整错误body与当前wire/调用身份经同一session gate原子接受，才记录Server/ServerReported和真实号码；“已收到错误”与“允许保留会话”分开。只有原8.1.5.60/-2106/同Query或Reader/ActiveTransaction白名单可保留，普通严格错误仍让wire未完成路径断开，事务保守Unknown，Reusablefalse且不重试。sync/async execute/prepare用同一完整body校验，截断/越界/trailing/stale与首取消/期限已经获胜时不提升ServerReported。receipt先赢后来的timer不改写结果；CommitUnknown不扩范围。旧失败/同包诊断保留为Characterization，不通过放宽探针断言改为通过。新源/新包/全回归/实库证据独立记录。

## 2026-10-03 T17 旧 getter 兼容与流式范围定案

完整旧回归发现：新 Reader 对 CLOB `GetBytes` 的类型收窄，以及既有 NULL/顺序/参数边界异常的改变，是兼容性回归，不能通过改变期望值绕过。Astra High/root 定案恢复 CLOB `GetBytes` 的旧编码字节转换，复用 `DmGetValue.GetBytes` case19 的严格、受限全量物化路径，继续适用 64 MiB 返回 payload 限制。该跨类型兼容 API 明确仍物化；CLOB 的流式读取入口为 `GetTextReader` 和 `GetChars`，不宣称所有 `GetBytes` 都有界。SequentialAccess 已消费后不能在编码字节与 UTF-16 单位之间切换；长度查询不改变消费单位或位置。

BLOB `GetBytes` 的范围/空 buffer 长度，以及 CLOB `GetChars` 的独立 UTF-16 扫描，按 T17 改进支持超过物化限制的字段。已知 BLOB 长度直接采用验证 metadata，未知才查询；inline 长度先验证帧 payload 边界。CLOB opaque server length 不能作为 UTF-16 或编码字节数。原测试把这些范围/扫描 API 归为全量物化的断言须迁移为独立的有界读取和精确计数证明；真正 `GetValue/GetFieldValue/GetString/loadAllData` 的旧 cap 断言保持。旧预载 local CLOB 缓存配空 row bytes 的 synthetic fixture 改为合法 inline locator/payload，不恢复生产缓存旁路来满足夹具。

旧 getter 的 NULL6081、顺序6097、CLOB负字段offset6057、已固定 buffer 边界异常保持；新的 Stream/TextReader 过期、并发、Dispose 生命周期合同保持其单独异常。合法零长度读取仍完成本地 ordinal/type/NULL/顺序检查，且不发网络。原失败轮次及历史接受输入只读，修复经统一冻结、独立旧回归与真实大字段门后才验收。

## 2026-10-03 分阶段依赖解释（共享实库仍 pending）

T17 产品/完整离线843及精确包TLS大值16case、同包T16 offline/TLS经root接受；shared大上传/精确清理未通过。保留原v1 DAG和T17 `completed=false`，依§1继续能够独立完成的部分：`T17 implementation+offline/TLS accepted → T18 implementation/offline/TLS → T25对应范围review → T19候选/来源/可用下游范围`。后续均携带upstream_pending，不将scoped接受误作原完整activation。只有共享大值、精确对象恢复和最终全矩阵资源/下游全部满足，才关闭完整R3。

T18业务路径仅原子状态/不可变完成记录，ActivitySource/Meter/instrument发布均在单一有界dispatcher业务链之外惰性初始化；禁止staticinitializer同步执行ShouldListenTo/InstrumentPublished。Task.Run单一消费者SuppressFlow，不继承调用者AsyncLocal/Activity/baggage；records仅TraceId/SpanId/flags和固定enum/数字，禁TraceState/业务对象/异常/SQL/endpoint。容量4096 nonblockingTryWrite，callback/init失败仅原子统计，不递归meter、不改Outcome，完成span说明后导出。用户主动另线程操作自己连接不在此隔离保证内。

TLS资源门预先固定warmup60s+测量600s、至少500完成cycle、并发上限4、每5s采样至少121点，静默30s再3点，无强制GC；total900s。预算在测量前manifest固定：相对warmup结束RSS增长≤256MiB、GC heap增长≤128MiB、线程增加≤16、FD增加≤16且quiescent相对baseline≤8；RSS/heap各60s窗口后半段相对前半段中位数增长≤64MiB/32MiB，线程/FD后半段线性趋势每100cycle增加≤1/1，最终pool/physical/queue精确归零，registry≤128/queue≤4096。OS不可用指标明确unavailable，其余严格验证；失败后不调高阈值或缩时长。probe构造数据固定40KiB级流/legacy混合，不能替代T17大值峰值门。离线10k租约含多轮registry128churn、barrier取消/故障、敏感合成标记全部监听/制品检查，缺DBrelease failclosed。

T18公开累计数经Astra/root最后审查选择ObservableCounter读取业务原子值；队列仅传递有损的span/histogram。created/closed/network直接读取累计；acquire/result、operation/result、discard/reason、lob/direction/kind/terminal使用固定bucket数组，不从幸存queue事件反推全量计数。process累计不因本诊断queue溢出丢失，不宣称外部exporter端到端无损；observer取每bucket最新累计值而非累加多次采样。observable消费回调异常属于外部调用，不声称Core能统计所有该类错误。

独立类型审查确认28=ROWID binary，不能进入新charset textcursor；真实text集合0/1/2/19/54。旧GetChars28保留本地小值rendered-string兼容，新GetTextReader28明确拒绝；GetBytes0/1/2在基线原已InvalidCast，不新增支持。新增Luna独立8/12bytes非法UTF8 ROWID golden及两charset text0/54 unicode/NUL fixtures，新publicgetter测试不借同一产品converter算expected，不声称actualROWID实库结果。此audit修复由当前Reader owner整合并入下一源冻结。

## T19 最终范围澄清（Astra High 二次设计）

原§5长时资源合同为TLS600秒，规范不强制每个DMprofile重复长时门。最终候选重新执行该TLS门；shared长时资源明确not_verified，不泛化TLS结果、不新增强制预算，但shared大LOB、原R2 2048×1024跨页/取消、固定EF完整有限权限合同及遗留对象fresh最终状态仍必过。

Unknown不重试分层验证：同最终driver包离线真实public Commit/CommitAsync sent后协议故障合同；同最终候选固定EF真实retrydetector与ExecutionStrategy sync/async注入Unknown并断言一次尝试/同异常传播；正常实库EF功能合同。若第二层用反射构造候选Unknown，明确SyntheticDownstreamUnit，不宣称driver到EF端到端或线上最终提交效果，不破坏真实DB制造故障。

T19每lane源/包/实际DLL/MVID及退出码/TRX/case身份、fresh服务器状态均记录，external summary布尔值不能取代各项证据。最终來源archive含本轮untracked输入、标准LICENSE、upstream map与notices；SourceLink=false，HEAD归档不冒充当前源。

## T19 shared functional 外层宿主预算修订

2026-10-03，Astra High/root 在原900秒失败及同环境R1/current、NoDelay两组ABBA诊断后，批准新的固定shared-functional宿主护栏1500秒。仅该外层护栏是此前预算不变承诺的明确例外；每次调用20秒、原76项/1266次seed、SQL/payload/断言/AutoSavepoints、TLS600/900资源合同及其它lane预算均不变。计算、归因限度、原始证据hash与失败收口见 [修订说明](maintenance/T19-shared-host-budget.md)。新工具进入新freeze及唯一候选，重新执行同包门；旧900秒exit124保留，不称通过，不据诊断修改产品NoDelay或删除必要协议步骤。

## 2026-10-03 CLI 验收工具来源分层定案

Astra High/root批准保留已通过实测的同v6不可变包，仅冻结`t19-cli-retention-v1`工具执行overlay，严格八文件allowlist，产品、原测试与构建输入不变。新CLI原合同重跑并留存该次实际加载DLL；其它门按原工具与原证据逐份复核。producer来源、execution来源与最终aggregation分别记录，不把最终验证源码tree说成v6包的重建来源，不补造历史加载资产。CLI原清理、SQL、case及预算保持；旧失败保留。最终窗口1675输入及历史903证据不变后，root才归档报告和加入CI纯Python35项合同；这些是验证后的文档/CI改动。来源与独立验收见[T19](reports/T19.md)。
