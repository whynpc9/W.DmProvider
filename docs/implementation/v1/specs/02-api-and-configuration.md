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
