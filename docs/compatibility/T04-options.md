# T04 配置键与兼容边界

日期：2026-09-29。此表描述 T04 配置解析和快照，不代表 S04/T07 TLS、S04/T06 收包限额、S08/T13–T14 超时预算、S09/T15–T16 池或 S10/T17 LOB 限额已经执行。

| 语义键 | 接受的旧键/别名 | 输入与内部值 | 状态 |
|---|---|---|---|
| `server` | `data source`, `host` | 原样 Unicode 主机；`host:port` 和 `[IPv6]:port` 分离，裸 IPv6 不推测末段为端口 | supported；默认空 |
| `port` | 无 | 1–65535；默认 5236 | supported |
| `user` | `userid`, `user id`, `username`, `user name`, `uid` | 原样字符串；默认空 | supported |
| `password` | `pwd` | 原样字符串；默认空 | supported；诊断串省略 |
| `schema` | 无 | 原样字符串，独立于 catalog；null/空串清除。当前 W 产品策略按 .NET `char` 长度限制 128，拒绝 NUL/CR/LF；这不是对 DM 服务端字节上限的声明 | supported |
| `language` | 无 | `cn/en/cn_hk/cn_tw` 或 0–3；默认 0 | supported |
| `connect_timeout` | `connect timeout`, `connectiontimeout`, `timeout`, `connectTimeout` | 字符串和旧 `ConnectionTimeout` typed 属性均为毫秒，内部 `TimeSpan`；默认 5000 ms | supported |
| `command_timeout` | `sessionTimeout`, `commandtimeout`, `session_timeout` | 秒；默认 30，0 表示无限 | supported |
| `conn_pool_timeout` | `connpooltimeout` | 毫秒，内部 `PoolAcquireTimeout`；默认 5000 ms | supported 配置；实际池待 S09/T15–T16 |
| `socketTimeout` | `socket_timeout` | 毫秒，内部 `ReadIdleTimeout`；默认 0 | supported 配置；预算待 S08/T13–T14 |
| `cleanup_timeout` | `cleanuptimeout` | 毫秒，必须大于 0；默认 5000 ms | supported 配置；预算待 S08/T13–T14 |
| `transport_security` | `transportsecurity` | `RequireTls` 默认，或显式 `PlaintextAllowed` | supported 策略；TLS 实现待 S04/T07 |
| `persist_security_info` | `persistsecurityinfo` | bool；默认 false | supported |
| `initial_catalog` | `initial catalog`, `initialcatalog` | 空值清除；非空拒绝，绝不映射到凭据 | unsupported |
| `max_message_size` | `maxmessagesize` | 仅默认 64 MiB | fixed declaration；收包限额待 S04/T06 |
| `max_materialized_lob_size` | `maxmaterializedlobsize` | 仅默认 64 MiB | fixed declaration；物化保护待 S10/T17 |
| `lob_chunk_size` | `lobchunksize` | 仅默认 32 KiB | fixed declaration；分块待 S10/T17 |

新增 typed `ConnectTimeout`、`PoolAcquireTimeout`、`ReadIdleTimeout`、`CleanupTimeout` 为 `TimeSpan`，只接受完整毫秒且必须可用 32 位毫秒表示。旧 `ConnectionTimeout`、`ConnPoolTimeout`、`SocketTimeout` 的 `int` 签名保留毫秒。`DmConnection.ConnectionTimeout` 标准属性按非零毫秒向上取整为秒。

旧 `DmOption` 表中其余已知键为 deprecated/unsupported。仅与当前安全默认值完全相同的显式值可解析；非默认值拒绝，未知键一律拒绝。典型禁用键包括 `conn_pooling`, `stmt_pooling`, `pstmt_pooling`, `enable_rs_cache`, `enlist`, `rwSeparate`, `rwHA`, `compress`, `svc_conf_path`/`dm_svc_conf`, `log_dir`, `log_level`, `addressRemap` 和 `useSkyWalking`。旧日志目录与服务文件默认路径已改为空；不加载环境服务文件。`conn_pool_size=0`、`escape_process=false` 等不同于旧默认的值也必须拒绝，不能先接受再丢弃。

Builder 自身构造和 `new ConnectionString` setter 先在临时对象验证，失败保留原配置。`DbConnectionStringBuilder.ConnectionString` 在 .NET 10 非虚，基类引用赋值依次调用虚索引器。索引器对同义重复仅接受等价值；要改变值，先 `Remove` 再赋值，或使用 typed 属性。直接索引器赋值失败后也进入不可消费状态，需 `Clear` 或一次成功的自身字符串赋值；事后 `Remove` 不解除该状态。基类赋值遇 `ArgumentException`/`ArgumentOutOfRangeException` 时框架实测可回滚原状态；`NotSupportedException` 路径可能留下部分状态，Builder 会拒绝读取或生成快照。不能把基类 setter 称为原子操作。

框架基类解析器还会在到达虚索引器之前忽略 `connect_timeout=` 一类空值；基类引用 setter 的这一语法输入无法由派生类识别。Builder 自身构造、自身 `new ConnectionString` setter，以及 `DmConnectionSettings.Parse` 使用保留空值的有序解析器，因此会拒绝空数值设置。调用方需要严格验证时应使用这些入口。
