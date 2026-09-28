# DM.DmProvider 8.3.1.47463 逆向分析：ADO.NET 公共 API 层

分析基线：`decompiled/net8.0/`，ilspycmd 反编译。引用格式为 `文件:行号`。

---

## 1. 核心类清单

| 类 | 职责 |
|---|---|
| `DmConnection`（`Dm/DmConnection.cs:15`，sealed） | 连接门面。所有 public 方法统一走 `filterHead` 过滤器链，否则走 `do_*` 内部实现 |
| `DmConnectionStringBuilder`（`Dm/DmConnectionStringBuilder.cs:12`） | 连接字符串解析；静态 `options` 列表（`1187-1280`）登记全部合法键、类型、默认值、取值范围 |
| `DmConnProperty`（`Dm/DmConnProperty.cs:10`） | 连接属性的内部强类型视图；`ConnectionString` setter（`142-226`）完成解析、`PropertyHashCode` 计算、dm_svc.conf 叠加、AddressRemap/UserRemap |
| `DmConnInstance`（`Dm/DmConnInstance.cs:14`） | 物理连接实例（一条 TCP 会话），持有 CSI（协议层 `A.B`）、语句池、当前事务；连接池池化的是它而非 DmConnection |
| `DmConnInstancePool`（`Dm/DmConnInstancePool.cs`） | **死代码**：`GetConnInstance` 直接返回 null，`DestroyAll` 空实现，21 行占位类 |
| `Dm.util.ConnPoolCache`（`Dm.util/ConnPoolCache.cs:10`） | 连接池实现：`Dictionary<string, ConnPool>` + 后台清理线程 |
| `Dm.filter.ConnPoolFilter`（`Dm.filter/ConnPoolFilter.cs:7`） | 池逻辑实际挂载点，作为过滤器插入 `filterHead` 链 |
| `Dm.filter.BaseFilter`（`Dm.filter/BaseFilter.cs:14`） | 责任链基类，~150 个 virtual 方法覆盖 Connection/Command/Reader/Transaction/Parameter/Builder 全部 API，**本层最大扩展点** |
| `DmCommand`（`Dm/DmCommand.cs:14`） | 命令执行、参数绑定、批量、REF CURSOR；`do_Execute*` 为 `internal virtual`（SkyWalking 埋点类覆写它） |
| `DmDataReader`（`Dm/DmDataReader.cs:13`） | 结果集读取，数据实际来自 `DmResultSetCache`（本地行缓存） |
| `DmParameter` / `DmParameterCollection` | 参数模型；占位符为 `:p{n}`/`?`，名字前缀支持 `@`、`:`，统一归一为 `:`（`DmParameter.cs:794-801`） |
| `DmTransaction`（`Dm/DmTransaction.cs:13`，sealed） | 本地事务，包装 `DmConnInstance.Commit/Rollback` |
| `DmPromotableTransaction`（`Dm/DmPromotableTransaction.cs:10`） | System.Transactions PSPE 单阶段 enlistment 实现 |
| `DBAliveCheckThread`（`Dm/DBAliveCheckThread.cs:11`） | 进程级单例后台线程，按 `dbAliveCheckFreq` 周期对存活的 DmConnInstance 做 TCP 探活 |
| `DmDataAdapter` / `DmCommandBuilder` | 标准 DbDataAdapter/DbCommandBuilder 适配，`UpdateBatchSize` 默认 1 |
| `EPGroup`（`Dm/EPGroup.cs:61`） | 多 EP（服务名对应多节点）连接选择与故障切换（SwitchTimes/SwitchInterval） |
| `A.A` / `A.B`（`A/A.cs`、`A/B.cs`） | 反编译折叠的语句对象（Statement）与连接会话接口（CSI），是 API 层下到网络协议层的边界 |

---

## 2. 重点问题

### 2.1 DmConnection 生命周期与状态机

状态字段是私有 `connectionState`（`DmConnection.cs:38`），setter 触发 `OnStateChange`（`140-146`）。只用到了 `Open/Closed/Connecting/Broken` 四个值。

**Open 路径**（`DmConnection.cs:664-687`）：
1. `m_AlreadyDisposed` 检查 → `ObjectDisposedException`；
2. 若在 PromotableTransaction 中（`ExistDmPromotableTransaction()`），只把 `StatusInTransactionScope` 置为 Open 就返回——**延迟到 scope 提交才真正开连接**；
3. `filterHead == null` 走 `Connect()` → `ConnProperty.EPGroup.connect(this)`（`689-695`），否则 `filterHead.Open(this)`；
4. 成功后若 `Enlist=true` 且存在 `Transaction.Current`，自动 `do_EnlistTransaction`（`683-686`）。

无池直连时实际建立连接在 `do_Open()`（`505-530`）：硬编码重置 `encryptPwd=false; encryptMsg=false; msgVersion=21`（`515-517`），new `DmConnInstance`（构造里完成 socket+登录并注册 DBAliveCheckThread），置 Open 后执行 `set schema`（`DriverUtil.executeSetSchema`，`DriverUtil.cs:30-45`）。

**Close 路径**（`571-588`）：若在分布式事务中仅标记 `StatusInTransactionScope`；否则 `do_Close()` → `ReleaseUnmanagedResource(pooled:false)`（`743-756`）→ 先 Dispose 未完结事务（非 Oracle 兼容模式即回滚），再 `m_ConnInst.Close(keep_tcp:false)`。`ForceClose` 忽略池化直接关物理连接。

状态机弱点：
- `do_State` setter 无条件触发 `OnStateChange`，即使原值与新值相同（`140-146`）；
- `Reconnect()`（`396-403`）是 `do_Close()` + `lock(this) { Connect(); }`，`DmCommand.Cancel()`（`DmCommand.cs:455-461`）的实现就是**重连**——Cancel 语义等同于暴力断连重建；
- `ChangeDatabase` 直接抛"不支持 catalog"（`364-367`）。

### 2.2 连接字符串完整键表

来源：`Dm/DmConnectionStringBuilder.cs:1187-1280`（注册）+ `Dm/DmConst.cs:9-321`（键名/别名）+ `Dm.Config/DmOptionHelper.cs:28-210`（默认值）。键名大小写不敏感（`Dictionary` 用 `OrdinalIgnoreCase`，`DmConnectionStringBuilder.cs:20`）。

| 键（别名） | 类型/默认值 |
|---|---|
| `server`（`data source`） | string，默认 `localhost`；支持 `host:port`、`(h1:p1,h2:p2)` 多 EP、服务名（查 dm_svc.conf） |
| `user`（`userid,user id,username,user name,uid`） | string，默认 **`SYSDBA`** |
| `password`（`pwd`） | string，默认 **`SYSDBA`** |
| `port` | long，默认 **5236**，范围 1-65535 |
| `encoding`（`sessEncode,sess_encode`） | string，默认 `""`（跟随服务器，初始 `gb18030`） |
| `enlist`（`autoenlist`） | bool，默认 **false** |
| `connect_timeout`（`connect timeout,connectiontimeout,timeout,connectTimeout`） | ms，默认 **5000** |
| `command_timeout`（`sessionTimeout,commandtimeout,session_timeout`） | 默认 0（无限） |
| `stmt_pooling`（`stmtpooling`） | bool，默认 **true** |
| `stmt_pool_size`（`poolsize`） | 默认 100 |
| `pstmt_pooling`（`preparepooling`） | bool，默认 false |
| `pstmt_pool_size`（`preparepoolsize`） | 默认 100 |
| `conn_pooling`（`connpooling`） | bool，默认 **false** |
| `conn_pool_size`（`connpoolsize`） | 默认 100 |
| `conn_pool_check`（`connpoolcheck`） | bool，默认 false —— **注意：此键在本 build 中定义但从未被消费**（全库 grep 仅 2 处存取，无读取方） |
| `conn_pool_timeout`（`connpooltimeout`） | 默认 = connect_timeout（5000ms） |
| `conn_pool_idle_expired_time` | ms，默认 0（不过期） |
| `conn_pool_idle_clear_interval` | ms，默认 10000 |
| `escape_process`（`escapeProcess`） | bool，默认 true |
| `time_zone` | long（分钟），范围 -779~840，默认取本地时区 |
| `language` | `cn`/`en`/`cn_hk`/`cn_tw`，默认 cn |
| `primary_key`（`keywords`） | 保留字列表 |
| `loginMode`（`login_mode`） | `primaryfirst/onlyprimary/onlystandby/standbyfirst/normalfirst`，默认 normalfirst |
| `schema` / `schemaSensitive` | `""` / false |
| `app_name`（`appname`）、`host`、`os_name`（`os`） | 空串 |
| `svc_conf_path`（`dm_svc_conf`） | Windows：`%SystemRoot%\Sysnative\dm_svc.conf`；其他：`$DM_SVC_PATH` 或 `/etc/dm_svc.conf` |
| `loglevel`（`log_level`） | OFF/ERROR/SQL/INFO，默认 OFF |
| `logdir`（`log_dir`） | 默认 `Environment.CurrentDirectory` |
| `logsize`（`log_size`） | 默认 104857600（100MB） |
| `rw_separate`（`rwSeparate`） | int，默认 0；**>0 时强制 `LoginMode=onlyprimary` 且 `ConnPooling=false`**（`DmConnProperty.cs:1203-1210`） |
| `rw_percent`（`rwPercent`） | 默认 25，范围 0-100 |
| `compress`（`compress_msg`） | 0/1/2（无/简单/自动），默认 0 |
| `compress_id`（`compressId`） | 0=ZIP，1=Snappy，默认 0 |
| `login_encrypt`（`loginEncrypt, communication_encrypt`） | bool，默认 false |
| `cipher_path`（`cipherPath`） | 默认空 |
| `direct` | bool，默认 true |
| `enRsCache`（`en_rs_cache,enable_rs_cache`） | bool，默认 false（进程级结果集 LRU 缓存） |
| `rsCacheSize`（`rs_cache_size`） | MB，默认 20，范围 1-65535 |
| `rsRefreshFreq`（`rs_refresh_freq`） | ms，默认 10，范围 0-10000 |
| `lobMode`（`lob_mode`） | 默认 1（2=lobFetchAll 全量拉取） |
| `autoCommit`（`auto_commit`） | bool，默认 true |
| `alwaysAllowCommit`（`always_allow_commit`） | bool，默认 true |
| `batchType`（`batch_type`） | 默认 1（2=逐行执行模式） |
| `batchContinueOnError`（3 个别名） | false |
| `batchNotOnCall`（3 个别名） | true |
| `batchAllowMaxErrors`（`BATCH_ALLOW_MAX_ERRORS`） | 0 |
| `bufPrefetch`（`buf_prefetch`） | 0 |
| `clobAsString`、`columnNameUpperCase` | false / false |
| `columnNameCase`（`COLUMN_NAME_CASE`） | OFF/UPPER/LOWER，默认 OFF |
| `databaseProductName`、`compatibleMode`（`COMPATIBLE_MODE`） | 空 / OFF（ORACLE=1, MYSQL=2, SQLSERVER=4，可位或） |
| `ignoreCase`（`IGNORE_CASE`） | true |
| `isBdtaRS`、`maxRows`、`socketTimeout` | false / 0 / 0 |
| `addressRemap` / `userRemap` | 连接地址/用户重映射，格式 `a:p,b:p&...`（`DmConnProperty.cs:183-223`） |
| `epSelector` | WELL_DISTRIBUTE / HEAD_FIRST，默认前者 |
| `switchTimes` / `switchInterval` | 1 / 200ms |
| `loginStatus` / `loginDscCtrl` | OFF / false |
| `rwStandbyRecoverTime` | 60000 |
| `rwHA` / `rwAutoDistribute` / `rwFilterType` | false / true / 2 |
| `doSwitch`（`do_Switch,autoReconnect,auto_Reconnect`） | OFF/CONN_ERROR/EP_RECOVER，默认 OFF |
| `cluster` | NORMAL/RW/DW/DSC/MPP |
| `dbAliveCheckFreq` / `dbAliveCheckTimeout` | 0（关）/ 10000ms |
| `maxLobDataLenPerMsg` | 默认 32000，范围 1024-104857600 |
| `dbtimeToTimeSpan`、`caseSensitive`、`varchar36ToGuid` | false / false / false |
| `useSkyWalking` | false（true 时反射加载 `DmSkyWalkingAgent` 程序集包装命令，`DmConnection.cs:915-926`） |
| `intervalMode` | OFF/YM/DT/ALL |
| `sslFilesPath` / `sslKeyPass` | `""` / **`"changeit"`（硬编码默认口令）** |
| `convertToTz`、`catalog`、`DBAPassword` | false / "" / "" |
| `ShowExtraInfo`、`EFCoreNextResult` | false / **true** |

解析细节：`do_setThis`（`DmConnectionStringBuilder.cs:1323-1365`）经 `DmOption.ValidateValue` 校验范围；`server` 含 `:` 且非 `(`/`[` 开头时自动拆出端口（`1296-1309`）；设置 `loglevel/logdir/logsize/dbAliveCheckFreq/dbAliveCheckTimeout` 会写入 **静态全局** `DmSvcConfig`（`1341-1360`）——最后设置的连接串覆盖全局，影响进程内所有连接。

**重大怪癖**：`InitialCatalog` 属性只有 setter，且把值同时赋给 `user` 和 `password`（超长截断 48 字符）（`DmConnectionStringBuilder.cs:1068-1082`）。明显是从别的驱动拷贝时的历史遗留 bug。

### 2.3 连接池实现机制

池**不是**在 DmConnection 内部，而是以过滤器形式插入：`BaseFilter.CreateFilterChain`（`Dm.filter/BaseFilter.cs:30-39`）在 `ConnPooling=true` 时把单例 `ConnPoolFilter.Instance` 加进链首。`Open/Close` 由此被拦截：

- **池 key**（`DmConnection.cs:323-326`）：`ServName(Server:Port) + "/" + User + "/" + PropertyHashCode`。`PropertyHashCode` 是"排序后的 k=v 拼接串".`GetHashCode()`（`DmConnProperty.cs:156-163`）。
- **Get**（`ConnPoolCache.cs:37-80`）：先 `TryDequeue`；失败则在 `newConnLock` 下、当 `totalCount < ConnPoolSize` 时新建物理连接（借外层 conn 的 `filter.next.Open` 或直接 `Connect()` 完成登录，再 `CleanDmConnection()` 断开与外层 conn 的关联）；否则 `DequeueTimeout(timeout)` 阻塞等待，超时抛 `ECNET_CONNPOOL_TIMEOUT(6123)`。
- **借出前必做存活校验**：`ConnPoolFilter.Open`（`Dm.filter/ConnPoolFilter.cs:88-115`）对拿到的实例无条件执行 `CheckConnectionSurvival`——即**每次从池取连接都发一次 `select 1`**（`ConnPoolCache.cs:239-266`，临时 new 一个 DmConnection 包裹实例执行）。校验失败则丢弃重建，直到成功（`while(true)` 循环，每 100 次记一条日志）。前面提到的 `connPoolCheck` 键形同虚设。
- **Put**（`ConnPoolCache.cs:82-100`）：若事务未完结（`getTransFinish()` 为假）先尝试无检查回滚，回滚失败直接关物理连接；成功则 `CleanDmConnection()` + 记录 `connPoolPutTime` 后入队。`trxFinish` 由协议回包状态位计算：`status & 0xFFF ∈ {0, 32, 64}` 视为完结（`DmConnInstance.cs:520-531`）。
- **回收策略**：`ConnPoolCache` 构造即启动后台线程 `DmProvider-connPoolClear`（`191-197`），每 `conn_pool_idle_clear_interval` ms 对所有池执行 `Clear()`：抽出全部实例，逐一生存校验 + 空闲超 `conn_pool_idle_expired_time` 的关闭，其余放回（`102-138`）。
- **容量/超时参数的全局污染**：`ConnPoolFilter` 是**单例**，`ConnPoolSize` 等 setter 直接改写共享 `PoolCache.Limit/Timeout/...`（`ConnPoolFilter.cs:35-65`）。不同连接串的池配置互相覆盖，最后的 writer 生效。
- **DBAliveCheckThread**（`Dm/DBAliveCheckThread.cs`）：进程级单例，线程名 `DB-ALIVE-CHECK-THREAD`。每个 `DmConnInstance` 构造时若 `DmSvcConfig.dbAliveCheckFreq > 0` 则入队（`DmConnInstance.cs:130`）；每轮把队列全部抽出，按 `server:port` 去重，对每个端点做一次**裸 TCP 连接**探活（`checkDbAlive`，`108-132`），失败则 `GetCsi().d()` 断掉该实例的会话，存活的重新入队，然后 `Sleep(dbAliveCheckFreq)`。注意 `dbAliveCheckFreq` 是静态全局，且连接关闭只是 `AliveCheck=false`（下一轮剔除），**关闭的连接对象会在队列里滞留一轮**。

### 2.4 DmCommand 执行路径与参数绑定

公共方法全部二分到 `filterHead`（`Dm.filter` 链）或 `do_*`。无池、无日志时路径为：

```
ExecuteNonQuery() → do_ExecuteNonQuery() (DmCommand.cs:463)
  → lock(this) { m_Stmt = connInstance.GetStmtFromPool(this) }   // 语句池复用
  → 无参：m_Stmt.c(text)  (A/A.cs:452) → SQLProcessor.escape/execOpt → csi.A(sendMsg, recvMsg, stmt, sql, ...)  (A/B.cs，协议层)
  → 有参：PrepareInternal() (DmCommand.cs:839) → m_Stmt.D(text)（远端 prepare）
          → BindParameters() (917) → DmSetValue.SetObject(...) 逐参数写值
          → ExecutePreparedUpdate() = m_Stmt.M() (A/A.cs:540)

ExecuteDbDataReader() → do_ExecuteDbDataReader() (DmCommand.cs:543)
  → EnRsCache 命中则直接用缓存的 DmResultSetCache 构造 reader 返回（569-607）
  → SchemaOnly → m_Stmt.D(text) 只取元数据
  → 无参 → m_Stmt.A(text, behavior) (A/A.cs:391)；有参 → Prepare+Bind+ExecutePreparedQuery
```

- **语句池**：两级。`StmtPooling`（默认开）按空闲队列复用语句句柄（`stmt_queue`，上限 `stmt_pool_size`）；`PreparePooling`（默认关）按 SQL 文本做 `LRUCache<string, A>` 命中已 prepare 的语句（`DmConnInstance.cs:96-170`）。
- **参数绑定**：绑定前用 `ArrayUtil.IsAllMatch` 判断能否按名匹配，否则按下标（`DmCommand.cs:934-940`）。参数值为 `Array` 时进入**批量绑定**模式：`BatchType==2` 或 `BatchNotOnCall` 且语句为 CALL 时逐元素执行累加 rowCount；否则把数组每个元素 append 为 `DmParamValue` 一次下发（`957-1015`）。`BatchContinueOnError`/`BatchAllowMaxErrors` 控制容错。
- **REF CURSOR**：ctype=120 的参数会为每个游标分配独立语句句柄挂到 `RefCursorStmtArr`（`1017-1041`），`NextResult()` 逐个切换（`DmDataReader.cs:849-861`）。
- **存储过程**：`CommandType.StoredProcedure` 时把 CommandText 改写为 `procName(?,?,...)`（有 ReturnValue 时前缀 `? =`）再 prepare（`DmCommand.cs:858-883`）。
- 每次执行后 `m_Stmt.p()` 归还/释放句柄并把 `m_Stmt=null`（`510-515`）——句柄生命周期完全由命令执行周期托管。
- 无 `Execute*Async` 覆写，全部回落到基类的同步包装——**异步是明显的补强点**。
- `Cancel()` 实际调用 `Reconnect()`（`455-461`）：关连接再重连。
- EF Core 后门：`NextResult` 检查 SQL 文本中 `/*EFCOREROWCOUNT*/` 魔法注释出现次数决定是否允许翻下一个结果（`DmDataReader.cs:836-848`）。

### 2.5 事务与分布式事务

**本地事务**：`BeginDbTransaction` → `DmConnInstance.BeginTrx`（`DmConnInstance.cs:327-341`）：已有活动事务时，`Enlist=true` 直接返回同一事务，否则抛"不支持并行事务"。隔离级别映射：Unspecified/ReadCommitted→1、Serializable→3、ReadUncommitted→0，RepeatableRead 仅 MySQL 兼容模式允许（映射为 1）（`278-320`），通过 `SET TRANSACTION ISOLATION LEVEL ...` 语句下发。Commit/Rollback 在 AutoCommit 模式下由 `AlwaysAllowCommit` 决定是否报错（`343-379`）。Savepoint 支持 `Save/Rollback/Release`（SQL 拼接实现，`398-431`）。

**Dispose 语义危险**：`DmTransaction.do_Dispose` 中，**Oracle 兼容模式下 Dispose 未完结事务会 Commit，其他模式回滚**（`DmTransaction.cs:192-202`）。

**System.Transactions**：`Enlist=true` + `Transaction.Current != null` 时走 `do_EnlistTransaction`（`DmConnection.cs:405-458`）：
- 实现 PSPE（`IPromotableSinglePhaseNotification`），`EnlistPromotableSinglePhase`；
- 同一事务内第二根连接串相同的连接复用已 enlist 的物理实例（`FindExistingEnlistedConnInstance`，`460-476`，按 ConnectionString 字符串相等比较，复用前把自己 `Close()` 掉并偷走对方的 `m_ConnInst`）；
- **不支持真正的分布式提升**：`ITransactionPromoter.Promote()` 直接 `throw new NotSupportedException()`（`DmPromotableTransaction.cs:60-63`）。多资源/多连接串场景提升为 MSDTC 事务必然失败——这是事务子系统最大的能力缺口；
- 事务注册表是**静态 `Hashtable`，以 `Transaction.GetHashCode()` 为键**（`DmPromotableTransactionTransactionManager.cs:8-32`）——哈希碰撞会导致事务错配，且只增不减依赖 Commit/Rollback 回调清理；
- `DmTransaction.do_Commit/do_Rollback` 在存在 PromotableTransaction 且非 `InCommitOrRollback` 时静默 return（把提交权交给 scope，`DmTransaction.cs:133-139`）；Commit 时吞掉错误码 6042（`ECNET_COMMIT_IN_AUTOCOMMIT_MODE`，`148`）。

---

## 3. 关键常量/枚举表

**协议/杂项**（`Dm/DmConst.cs`）：
| 常量 | 值 |
|---|---|
| `MSG_COMPRESS_NO/SIMPLE/AUTO` | 0/1/2 |
| `MSG_CPR_FUN_ID_ZIP/SNAPPY/NONE` | 0/1/-1 |
| `MSG_COMPRESS_THRESHOLD` | 8192 |
| 协议版本 `VERSION` | 21（`VERSION_NEW_SERVER_VERSION`；do_Open 中硬编码 `msgVersion=21`） |
| `VERSION_PREPARE_OPTIMIZE=5`、`VERSION_FULL_ENCRYPT=11`、`VERSION_SSL_UPDATE=15`、`VERSION_RETURN_INTO=18` 等 | 特性协商位 |

**类型码**（散见于各层）：`ctype=1` CHAR（定长补空格 0x20，`DmDataReader.cs:758-775`）、`ctype=14/15/16/22/23/26/27` Oracle 兼容日期时间格式族、`ctype=119` Array、`ctype=120` REF CURSOR、`ctype=19` CLOB；语句类型 162 = CALL（`DmCommand.cs:968`）。

**错误码**（`Dm/DmErrorDefinition.cs`）：6001 通信错误，6034 结果集已关，6054 参数未绑定，6060 连接已关，6075 连接未打开，6092 DataReader 已打开（**单连接同时只允许一个活动 reader**），6123 连接池超时，10000 主备切换句柄失效；6042 自动提交下 Commit（在 do_Commit 中被吞）。

**加密**（`Dm/MsgSecurity.cs:13-39`）：工作模式掩码 127（ECB=1/CBC=2/CFB=4/OFB=8），算法掩码 65408：DES=128、DES3=256、AES128=512、AES192=1024、AES256=2048、RC4=4096、MD5=4352；`MIN_EXTERNAL_CIPHER_ID=5000`。

**枚举**：`CompatibleMode{OFF=0,ORACLE=1,MYSQL=2,SQLSERVER=4}`、`LoginModeFlag{primaryfirst,onlyprimary,onlystandby,standbyfirst,normalfirst}`、`DoSwitch{OFF,CONN_ERROR,EP_RECOVER}`、`LogLevel{OFF=0,ERROR=1,SQL=3,INFO=4}`、`CLUSTER{NORMAL,RW,DW,DSC,MPP}`、`EpSelector{WELL_DISTRIBUTE,HEAD_FIRST}`、`ColumnNameCase{OFF,UPPER,LOWER}`、`LoginStatus{OFF=0,OPEN=4,MOUNT=3,SUSPEND=5}`、`DmMppType{LOGIN_MPP_LOCAL,LOGIN_MPP_GLOBAL}`。

---

## 4. 可扩展 / 可 patch 点（核心目的）

**设计好的扩展点**
1. **`BaseFilter` 过滤器链（首选）**：`Dm.filter/BaseFilter.cs` 有 ~150 个 `public virtual` 方法覆盖 Connection/Command/Reader/Transaction/Parameter/ParameterCollection/DataAdapter/CommandBuilder/ConnectionStringBuilder 的每一个公共 API。所有 ADO 对象实现 `IFilterInfo`（`filterHead` 属性**可 public set**）。注入方式：构造后设置 `conn.filterHead = myFilter`（myFilter.next = 原链）。注意 `DmConnection.ConnectionString` setter、`DmCommand.do_DbConnection` setter、各构造函数会**重建链并覆盖 filterHead**（`DmConnection.cs:130`、`DmCommand.cs:176-177`），注入必须在最后一次设置连接串/连接之后。`LogFilter`、`ConnPoolFilter`、`ReconnectFilter`、`RWFilter2` 本身就是范本。
2. **`DmCommand` 的 `internal virtual do_ExecuteNonQuery/do_ExecuteScalar/do_ExecuteDbDataReader`**（`DmCommand.cs:463/520/543`）：官方为 SkyWalking 埋点预留的覆写点（`DmConnection.CreateDmTracingCommand` 按名字反射加载 `DmSkyWalkingAgent.DmTracingCommand`，`915-926`）。补强程序集可用同样模式（派生类 + `InternalsVisibleTo` 或程序集名借用）挂执行前后钩子。
3. **连接池行为**：`ConnPoolFilter.Close/Open/ForceClose` 即池的全部策略，绕过/替换它即可实现自定义池（如饥饿控制、按连接串分配置）；`ConnPoolCache` 是 internal，反射可达。
4. `DmClientFactory`（`Dm/DmClientFactory.cs:6`）是标准 DbProviderFactory 入口，`Instance` 单例。

**薄弱设计 / 缺陷**
- **sealed 类**：`DmConnection`、`DmTransaction`、`DmDataAdapter`、`DmCommandBuilder`、`DmParameterCollection` 均 sealed——继承扩展不可行，只能靠 filter 链或运行时 IL patch（Harmony 等）。
- **无 async**：全驱动无任何 `*Async` 覆写，高并发下线程池被同步 IO 占满；补强价值最高的点。
- **池 checkout 必发 `select 1`**（`ConnPoolFilter.cs:100`），`connPoolCheck` 键是哑配置；高 QPS 下多一次 RTT。可 patch 为按 `connPoolCheck` 门控。
- **`ConnPoolFilter` 单例 + 可变池配置**：不同连接串的 `ConnPoolSize/Timeout` 互相覆盖（`ConnPoolFilter.cs:35-65`）；`ConnPoolCache._connPoolMap` 是裸 `Dictionary`，后台 `Clear` 线程迭代 `Values` 时与前台 `Get` 写并发存在竞态（`ConnPoolCache.cs:220-237`）。
- **池 key 用 `string.GetHashCode()`**（`DmConnProperty.cs:163`）：32 位哈希碰撞会把不同配置的连接混进同一池；同义键（`pwd` vs `password`）因写入 `setProperty` 的键不同会产生不同哈希 → 同一服务器出现多个冗余池。
- **`ConnPool` 构造函数残留调试代码**：`limit==3` 时向 Console 打印堆栈（`ConnPoolCache.cs:28-32`）。
- **`DmCommand.do_ExecuteNonQuery` 吞异常**：`GetStmtFromPool` 的非 DmException 异常被 `catch{}` 吞掉且 `m_Stmt` 可能为 null，随后 NRE（`DmCommand.cs:478-490`）。
- **`CheckCommandBehavior` 位掩码比较错误**：`(byte)behavior & 0x3F` 与单值做 `==` 比较（`DmCommand.cs:797-803`、`DmDataReader.cs:233/880/889` 等多处），组合标志（如 `SchemaOnly|KeyInfo`）全部不匹配，行为分支失效。
- **`do_GetOrdinal` 最多 6 次全列线性扫描**（`DmDataReader.cs:625-688`），宽表高频按名取列是性能热点；没有名字→序号字典。
- **`do_Cancel` = 断线重连**（`DmCommand.cs:455-461`），任何超时取消都会导致连接重建，且 `Reconnect` 只在 `lock(this)`（连接对象）上同步，与正在执行的命令无互斥。
- **分布式事务表键碰撞**：`Hashtable[Transaction.GetHashCode()]`（`DmPromotableTransactionTransactionManager.cs:14`）；`Promote()` 抛 `NotSupportedException`（`DmPromotableTransaction.cs:60-63`）。
- **资源清理靠终结器**：`DmConnection` 终结器里 `ForceDispose` 后又 `GC.SuppressFinalize(this)`（`DmConnection.cs:720-736`，矛盾但无害）；`DmCommand` 终结器只把 `m_Stmt` 置 null，**未归还语句池**（`DmCommand.cs:737-749`）——忘记 `using` 的命令会让句柄池泄漏直到连接关闭。
- **`DmTransaction.Dispose` 在 Oracle 兼容模式下自动 Commit**（`DmTransaction.cs:192-202`）——`using` 块里忘 Commit 反而提交，极易踩坑。
- **字符串拼接 SQL**：set schema、savepoint、`GetKeyCols/GetUniqueCols` 的系统表查询（`DmDataReader.cs:1353-1391`）、`ExecuteXmlReader` 的 PL/SQL 包装（`DmCommand.cs:816`）全是拼接，虽有 `StringUtil.processDoubleQuoteOfName` 之类转义，但 schema 名等来自连接串，注入面存在。
- **CheckConnectionSurvival 里 `dmCommand.Close()` 不会真正关底层**（`DmCommand.cs:723-726` 仅置空语句引用），且整个校验每次 checkout 执行。

---

## 5. 有趣的实现细节

- **默认凭据硬编码 `SYSDBA`/`SYSDBA`、`localhost:5236`**（`DmOptionHelper.cs:28-34`）；SSL 私钥默认口令 `"changeit"`（`198`）。
- **登录加密使用 64 字节硬编码 DH 参数 p/g**（`Dm/MsgSecurity.cs:57-74`），算法族仍是 DES/3DES/RC4/MD5 为主、辅以 AES（`SymmCipherDesc.cs:24-57`）——明显沿袭 DPI/JDBC 老协议。
- `InitialCatalog` setter 把值塞给 user 和 password（`DmConnectionStringBuilder.cs:1068-1082`）——从 SqlClient 行为移植时的遗留 bug。
- `RwSeparate>0` 会偷偷把 `LoginMode` 改成 onlyprimary 并**关闭连接池**（`DmConnProperty.cs:1203-1210`）。
- `DmConnInstancePool` 是完整保留的死类（`GetConnInstance` 返回 null）——早期池实现的化石。
- 驱动内建 **SkyWalking APM 探针挂钩**：`UseSkyWalking=true` 时反射加载外部程序集 `DmSkyWalkingAgent`（`DmConnection.cs:915-926`），证明官方自己也是用"派生 DmCommand + 覆写 do_Execute*" 的方式做增强——这正是补强项目可复制的路径。
- EF Core 识别靠 SQL 里的魔法注释 `/*EFCOREROWCOUNT*/`（`DmDataReader.cs:839-847`）。
- `DmConnection.getFormat` 按列 ctype 切换 Oracle 兼容格式串（`DmConnection.cs:928-947`）。
- 全库大量异常路径把内部状态序列化成 `"{do_Close}->{...}"` 形式的 `StringBuilder` 轨迹附进异常消息（如 `DmConnection.cs:369-384`）——生产代码里的调试残留，也便于逆向定位分支。
- `DBAliveCheckThread` 探活是**裸 TCP  connect** 而非协议层 ping（`DBAliveCheckThread.cs:108-132`），对防火墙/DSC 场景语义偏弱。
