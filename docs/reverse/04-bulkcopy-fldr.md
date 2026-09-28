# DM.DmProvider 8.3.1.47463 批量装载 / FLDR 子系统逆向分析报告

## 1. 核心类清单

| 类 | 职责 |
|---|---|
| `DmBulkCopy`（Dm/DmBulkCopy.cs:11） | 旧版批量拷贝，走**数组绑定的普通 INSERT**（非 FLDR 协议） |
| `DmBulkCopy2`（Dm/DmBulkCopy2.cs:9） | 新版批量拷贝，走 **FLDR 快速装载协议**（FLDR_INSERT/FLDR_BLOB） |
| `FldrStatement`（Dm/FldrStatement.cs:15） | FLDR 协议的核心状态机：取表元数据、setEnvironment、按列打包、批量发送、错误行管理、DPC/MPP 集群分发 |
| `FLDR_INSERT`（Dm/FLDR_INSERT.cs:12） | 协议消息 cmd=55：列式批量数据的类型转换（J2DB）与字节序列化 |
| `FLDR_BLOB`（Dm/FLDR_BLOB.cs:10） | 协议消息 cmd=61：LOB 数据单独分片发送（每片 ≤300MB） |
| `FLDR_SET2`（Dm/FLDR_SET2.cs:7） | 协议消息 cmd=111：装载环境设置（setId、bldr 数、字符集、索引选项等） |
| `FLDR_GET_TABLE_INFO` / `FLDR_CLR` / `FLDR_FIND_INTERVAL` / `FLDR_GET_INDEX_INFO` / `FLDR_GET_MPP_INFO` | cmd=53/56/64/122/112，表元数据、清理提交、水平分区定位、索引信息、MPP 节点信息 |
| `Fldr`（Dm/Fldr.cs:7） | `B`（底层网络访问对象，A/B.cs）的 FLDR 门面；管理 setId↔表名映射（容量 bldrNum=64，满后取模覆盖） |
| `FldrConfig`（Dm/FldrConfig.cs:5） | 装载参数 DTO：maxRows/bldrNum/indexOption/maxErrorNum/logFileName 等 |
| `SetEnvInfo`（Dm/SetEnvInfo.cs:6） | FLDR_SET2 的参数载体，含字符集编码映射表 |
| `ColumnData`（Dm/ColumnData.cs:6） | 一列的批量数据容器：sqlType + null 位图 + 行数据列表 |
| `FldrBuffer`（Dm/FldrBuffer.cs:8） | 已编码好的一个批次（含消息 buffer、LOB 列表、错误行映射、DPC 子表明细） |
| `Bdta`（Dm/Bdta.cs:6） | BDTA 列存包的解码/Decimal 解码（读方向与协议常量） |
| `FldrErrorWriter`（Dm/FldrErrorWriter.cs:11） | 后台线程写错误行日志文件（dm_fldr_*.log） |
| `FldrTask`（Dm.filter.fldr/FldrTask.cs:7） | 列级并行任务的包装（含 CountdownEvent 同步与错误收集） |
| `DmFldrDllCall`（Dm/DmFldrDllCall.cs:6） | 原生 `dmfldr` 库的 P/Invoke 声明 |
| `DmFldrExport`（Dm/DmFldrExport.cs:5） | 基于原生库的**快速导出**（文本抽取）API |
| `TableInfo` / `FldrClusterInfo` / `FldrIndexInfo` | 表结构/自增列/LOB 标记/水平分区树/DPC 节点映射/装载索引状态 |

## 2. 重点问题回答

### 2.1 BulkCopy 的实现路径——实为三条，而非两条

**路径 A：`DmBulkCopy` —— 纯托管、数组绑定 INSERT（非 FLDR）**
- 构造时直接 `conn.Open()`（DmBulkCopy.cs:145，有副作用）。
- 先用 `select top 1 * from <表>` 拿目标列名（DmBulkCopy.cs:222），把源列名按位置/映射对齐后，拼 `insert into t(cols) values (?,?,...)`，每列一个 `DmParameter(":col", object[])` 数组绑定参数，按 BatchSize（默认 100）分批 `ExecuteNonQuery`（DmBulkCopy.cs:290-321, 471-523）。
- 注意：`DmBulkCopyOptions copyOptions` 和构造函数里的 `externalTran` **被收下但从未使用**（DmBulkCopy.cs:19, 156, 162）；`DmBulkCopyOptions` 枚举只有 `Default` 一个值（DmBulkCopyOptions.cs:3-5）。

**路径 B：`DmBulkCopy2` + `FldrStatement` —— 纯托管 FLDR 协议（真正的快速装载）**
- `WriteToServer` → `InitFldrConfig`（建 `FldrConfig`，调 `m_Conn.fldrStatement(fldrConfig)`，DmBulkCopy2.cs:244）→ 逐行 `fldrStatement.setObject(j+1, value)` → 每满 BatchSize 调 `executeBatch()`，AutoCommit 时每批 `Commit()`（DmBulkCopy2.cs:327-352）。
- `executeBatch` → `noClusterProcess()`（单机）或 `clusterProcess()`（DPC 集群，按子表分区、每 BP 节点建独立连接并行装载，FldrStatement.cs:1214-1278）→ `primaryFldr.insert(...)` → `A/B.cs:972` 发 `FLDR_INSERT` 消息；若含 LOB 先走 `FLDR_BLOB`（A/B.cs:974-977, 1028-1033）。
- 装载结束 `close()` 发 `FLDR_CLR`（commitFlag=1）提交（FldrStatement.cs:967-1033）。

**路径 C：`DmFldrExport` —— P/Invoke 原生 `dmfldr.dll`（仅导出方向）**
- `[DllImport("dmfldr")]` 共 6 个入口：`fldr_alloc/fldr_free/fldr_set_attr/fldr_export/fldr_fetch_data_len/fldr_fetch_data`（DmFldrDllCall.cs:41-103）。
- 用法：`GetFldrHandle` 依次 SetAttr SERVER/UID/PWD/PORT，再硬编码 `LOAD_MODE=2`、`EXPORT_MODE=2`、`TASK_THREAD_NUM=8`、字符集，然后 `fldr_export(fsinst, options + " LOAD DATA INFILE 't1.dta' INTO TABLE T2")`（DmFldrExport.cs:47-61）——**控制串里的文件名 `t1.dta` 和表名 `T2` 是硬编码的**，实际装载对象完全由用户传入的 `options` 控制串决定。
- 整个 net8.0 反编译源码中对 "dmfldr" 的 DllImport 仅此一处；**原生库未被用于 INSERT 方向的 BulkCopy**。

### 2.2 FLDR_INSERT 消息协议（cmd=55）

所有消息共享 64 字节公共头（MSG.cs:142 `MSG_HEAD_SIZE=64`；偏移定义 MSG.cs:146-160：0=stmtId, 4=cmd(short), 6=bodyLen(int), 10=sqlCode, 14=svrMode, 16=expand, 18=compress, 19=CRC8）。

`FLDR_INSERT.doEncode()`（FLDR_INSERT.cs:264-292）的消息体布局：

```
int32  rows            // 本批行数
int16  columns         // 列数
<跳过8字节>            // access.A.A(8, true, true)
byte   compress        // 静态字段，恒为 0（FLDR_INSERT.cs:25，压缩未启用）
byte[] sqlType[]       // 每列 1 个 short 类型码（见第 3 节）
然后按列（列式存储，非行式！）：
  int32 isAllNotNull   // 1=全列非空
  byte[rows] nullArr   // 仅当 isAllNotNull==0 时写入
  byte[] cell × rows   // 每个单元格：变长类型自带长度头（见 2.3），定长类型裸字节
```

`afterEncode()`（FLDR_INSERT.cs:1595-1603）回填头部：偏移 20=setId、70/74=消息长度。BLOB/CLOB 列（sqlType 12/19）在 INSERT 包中只占位（`H(0)` + 全 0 null 数组，FLDR_INSERT.cs:276-280），真实内容走 FLDR_BLOB 消息：`colIndex(short) + rowIndex(short) + len(int) + offset(int) + chunkLen(int) + bytes`，每条消息最多 300MB（FLDR_BLOB.cs:265），上限 510MB（`LOB_MAX_SIZE=534773760`，FLDR_BLOB.cs:30）；头部偏移 52=isOver、56=columnOver。

发送管线在 `A/B.cs:161-184`（`MSG<A>.A(MSG)`）：encode → 可选全报文加密（encryptMsg 时 64 字节头之后的 body 整体加密，B.cs:186-197）→ CRC → 发送 → 收包 → checkCRC → decode。单条消息上限 `MSG_LIMIT_LEN=536870912`（512MB，MSG.cs:48，B.cs:168 检查）。

### 2.3 批量数据打包格式（行数据序列化）

转换入口是 `FLDR_INSERT.batchThreadJ2DB`（FLDR_INSERT.cs:174-262，按列分派），序列化结果按"列式"写入 buffer：

| 类型码 | 打包格式 |
|---|---|
| CHAR/VARCHAR/VARCHAR2 (0/1/2) | 8 字节头：int32 尾部空格数 + int32 有效长度，随后是 ServerEncoding 编码字节（`processVarchar`，FLDR_INSERT.cs:1108-1127）。超长即 `EC_STR_TRUNC_WARN`（:1073-1075） |
| BINARY/VARBINARY (17/18) | 8 字节头（前 4 字节恒 0，后 4 字节 = 实际长度）+ 原始字节；支持 "0x" 前缀十六进制字符串（`processBinary`，:339-362） |
| DECIMAL (9) | 28 字节类 Oracle 格式：byte0=符号（正 0xC1/负 0x3E/零 0x80），byte1=precision，byte2=scale，byte3-4=weight（short），byte5=长度，之后每字节存两位十进制数字（`charToDigit[d1][d2] = d1*10+d2+1` 的 +1 偏移码表，:43-55），负数按 `102-x` 取补并以 102（`NEGATIVE_END`）结尾（`processDecimal`，:1301-1415）。零有常量 `ZERO_ARR`（:57-62） |
| DATE/TIME/DATETIME 等 (14/15/16/22/23/26/27) | 12 字节：year(short LE) + month,day,hour,min,sec 各 1 字节 + ms 3 字节 + 时区偏移分钟(short)。`FldrUtil.fromDate/fromDate12`（FldrUtil.cs:10-54）与 `processDate`（FLDR_INSERT.cs:764-803）；TZ 类型支持 "… +08:00" 字符串解析（`parseStringDate`，:847-883） |
| INTERVAL YM (20) | 12 字节：year(int) + month(int) + scale(int)（:544-562） |
| INTERVAL DT (21) | 24 字节：day,msec,hour,minute,scale,second 各 int32（:470-500） |
| INT/BIT/TINYINT/SMALLINT (7/3/5/6) | 均 4 字节 LE int（带范围校验 `N2DB.checkInt/checkTinyint/...`） |
| BIGINT (8) / DOUBLE (11) / REAL (10) | 8 字节 LE long / 8 字节 double / 4 字节 float |
| ROWID (28) | `DmRowId.encode(conn)` |
| BLOB/CLOB (12/19) | INSERT 包中仅占位，实体走 FLDR_BLOB 分片 |

错误行处理：转换失败的行号记入 `errRowMap`（int→Exception），达到 `maxError` 即中止并抛 `FLDR_APPROACH_MAX_ERROR`；编码时 `processErrCol`（FLDR_INSERT.cs:1689-1709）会把错误行从 null 位图和数据流中剔除后重写 rows 计数，错误行明细写入 `FldrErrorWriter` 日志（`"[DATA]..."` 行，FLDR_INSERT.cs:1611-1617）。

读方向（服务器→客户端的 BDTA 包）由 `Bdta.decode` 完成：包头含 nrows/nflds/length/orgLen/compress 字段（Bdta.cs:8-18），变长标记 `VAR_DATA_LEN_STR=-2`、`VAR_DATA_LEN_DEC=-3`（:20-22），Decimal 解码 `readDecimal`（:159-182）是上述 28 字节格式的逆过程。

### 2.4 错误处理与性能相关配置

- `FldrConfig`（FldrConfig.cs）：`maxRows=5000`（每批行数，经 `SetEnvInfo.setBdtaSize` 校验 **[100,10000]**，SetEnvInfo.cs:87-94）；`bldrNum=64`（服务端并行装载线程数，校验 [1,1024]，:156-163）；`indexOption=1`；`maxErrorNum=1`（**0 和 <-1 都会被强制改回 1**，FldrStatement.cs:1836——即"忽略所有错误"的 -1 可用，0 不可用）；`logFileName`（错误日志目录）；`defualtColumns`（原文拼写错误）。
- `DmBulkCopy2.BatchSize` 同样限定 [100,10000]（DmBulkCopy2.cs:51-53），并直接作为 `maxRows`。
- `FldrStatement` 隐藏参数：`setBatchQueue(int)`（异步队列容量，默认 60，FldrStatement.cs:216-219）、`setReconnectTimes/setConnInterval`（**死代码**，仅存储从未被消费，:111-113, 158-173）。
- 错误日志：`FldrErrorWriter` 每实例起一个后台线程（"FldrLogFlusher"），队列容量 1000，单文件不滚动（构造时把静态 `switchMode` 改成 0、`maxFileNumber` 改成 1，FldrErrorWriter.cs:53-54），达到 maxErrorNum 行后停写（:106-110）。日志目录默认 `DmSvcConfig.logDir`（dm_svc.conf 的 `LOG_DIR`），缓冲 32KB、默认 100MB 文件上限（BaseFlusher.cs:22-24）。
- 连接级相关键：`DmConst.PROP_KEY_LOG_SIZE/LOG_FLUSH_FREQ/...`（Dm.Config/DmSvcConfig.cs:59 起），无 FLDR 专属连接串键。
- 消息层完整性：CRC8（头前 19 字节异或）或 crcBody 开启时 CRC32（MSG.cs:298-336）。

## 3. 关键常量 / 枚举表

**协议 opcode（MSG.cs:102-128）**

| 值 | 名称 |
|---|---|
| 53 | CMD_FLDR_GET_TABLE_INFO |
| 55 | CMD_FLDR_INSERT |
| 56 | CMD_FLDR_CLR（提交/清理，commitFlag 在头部偏移 20） |
| 61 | CMD_FLDR_BLOB |
| 64 | CMD_FLDR_FIND_INTERVAL |
| 111 | CMD_FLDR_SET2（环境设置） |
| 112 | CMD_FLDR_GET_TABLE_MPP_INFO |
| 121 | CMD_FLDR_COL_FUN_INFO |
| 122 | CMD_FLDR_GET_INDEX_INFO |
| 123 | CMD_FLDR_REST_INDEX_INFO |

**SQL 类型码（DmSqlType.cs:16-70）**：0=CHAR, 1=VARCHAR2, 2=VARCHAR, 3=BIT, 5=TINYINT, 6=SMALLINT, 7=INT, 8=BIGINT, 9=DECIMAL, 10=REAL, 11=DOUBLE, 12=BLOB, 14=DATE, 15=TIME, 16=DATETIME, 17=BINARY, 18=VARBINARY, 19=CLOB, 20=INTERVAL_YM, 21=INTERVAL_DT, 22=TIME_TZ, 23=DATETIME_TZ, 26=DATETIME2, 27=DATETIME2_TZ, 28=ROWID。

**原生 dmfldr 属性 ID（DmFldrDllCall.cs:10-30）**：1=SERVER, 2=UID, 3=PWD, 4=PORT, 17=BAD_FILE, 19=DATA_CHAR_SET, 30=ERRORS_PERMIT, 31=LOAD_MODE, 37=MPP_LOCAL_FLAG, 42=TASK_THREAD_NUM, 87=EXPORT_MODE。int 型属性走 `fldr_set_attr_1`，字符串走 `fldr_set_attr_2`（:105-127）。

**字符集编码（SetEnvInfo.setCharset，SetEnvInfo.cs:215-265）**：0=其他, 1=UTF-8, 2=GBK, 3=BIG5, 4=ISO-8859-9, 5=EUC_JP, 6=EUC-KR, 7=KOI8_R, 8=ISO-8859-1, 9=US-ASCII, 10=GB18030, 12=UTF-16。`DM_CHARSET` 枚举仅 {UTF8=1, GB18030=10}（DM_CHARSET.cs），与协议值一致。

**FLDR_SET2 头部扩展字段**（FLDR_SET2.cs:83-100）：20=setIdentity, 24=1（generateLog）, 28=sorted, 32=bdtaSize, 36=indexOption, 40=noMpp, 44=charset, 48=lobFromMsg(=1), 52=ignoreConflict, 60=setId(short), 62=bldrNumber(short)。

**消息通用**：`MSG_DEFAULT_LEN=32640`（buffer 初始容量，A/B.cs:1079）、`MSG_LIMIT_LEN=512MB`、`MSG_PARAM_MAX_LEN=64MB`、`MSG_HEAD_SIZE=64`、协议版本 `VERSION=21`（MSG.cs:44，登录协商后 `Math.Min` 于服务端版本，A/C.cs:654）。

## 4. 可扩展 / 可 patch 点（核心目的）

**官方拦截点（首选）**
- `Dm.filter.BaseFilter.Connection_fldrStatement(DmConnection, FldrConfig)` 是 **virtual**（Dm.filter/BaseFilter.cs:261-268），`DmConnection.fldrStatement()` 总是先走 filter 链（DmConnection.cs:974-981）。LogFilter（Dm.filter.log/LogFilter.cs:356）与 ReconnectFilter（Dm.filter.reconnect/ReconnectFilter.cs:337）已示范如何覆写。补强批量装载（计时、计数、重试、改写 FldrConfig、返回自定义 FldrStatement 子类）最干净的入口就是注册一个自定义 BaseFilter。
- `FldrStatement` 本身是 public 非密封类，所有 public 方法（setObject/addBatch/executeBatch/Commit/close/setBatchQueue…）可直接调用，`DmConnection.fldrStatement(config)` 是公开工厂——可以**完全绕过 DmBulkCopy2**，自己写高性能装载器（例如消除 DataTable 装箱、自管列映射）。
- `MSG<T>` 的 `beforeEncode/afterEncode/beforeDecode` 是 protected virtual（MSG.cs:197-239），新增消息类型有模板可循；`FLDR_INSERT.batchEncode(...)` 是 public static（FLDR_INSERT.cs:1605），可直接复用其打包逻辑产出裸字节流。

**明显的缺陷 / 可 patch 的 bug**
1. **`FldrStatement.setMaxRows` 永远抛异常**（FldrStatement.cs:1078-1085）：设置成功后无条件 `ThrowDmException(EC_INVALID_DB_OBJECT)`，缺 `else/return`。
2. **`setFldrProperties(Dictionary)` 的 indexOption 恒为 1**：`(Convert.ToInt32(text6) == 1) ? 1 : 1`（FldrStatement.cs:1907），三元两边相同，明显笔误。
3. **`DmBulkCopy` 的 `NotifyAfter`/`DmRowsCopied` 是死功能**：事件从未触发，`OnRowCopied`（DmBulkCopy.cs:545）无人调用；`m_CopyOpt`、`externalTran` 参数被丢弃。
4. **SQL 注入面**：`DmBulkCopy.getDestColNames` 与 `insert()` 直接拼接表名/列名（DmBulkCopy.cs:222, 292-311）；`FldrStatement.getPrecAndScale` 拼 `SELECT * FROM "{0}"."{1}" LIMIT 1`（FldrStatement.cs:1767，有 `processName` 双引号转义，:1657-1673，稍好）。
5. **`DmBulkCopy2` 状态不可复用**：`DestinationTableName` 二次赋值直接抛"DestTable不能二次赋值"（DmBulkCopy2.cs:89-91）；`InitFldrConfig` 在 `fldrConfig != null` 时**静默返回**（:224-226），第二次 `WriteToServer` 换一个结构不同的 DataTable 会沿用旧列映射，静默错装。
6. **构造函数内 NRE 隐患**：`FldrStatement` 构造先 `setFldrTableInfo` 后 `setFldrProperties`（FldrStatement.cs:141-142），而 `setFldrTableInfo` 的 DPC 分支 catch 里调 `shutdownExecutor()` → `fldrErrorWriter.isAlive()`，此时 `fldrErrorWriter` 仍为 null → NRE 掩盖真实错误（:1744-1748, 189-197）。
7. **`DmFldrExport` 双重释放**：`FetchData`/`Export` 失败路径里直接 `fldr_free(fsinst)`（DmFldrDllCall.cs:57-60, 69-73），但 `DmFldrExport._fldrHandle` 仍保存该指针，`Dispose()`→`Free()` 会再 free 一次（DmFldrExport.cs:90-97）。P/Invoke 的 `string ctlBuf` 未指定 CharSet（默认 Ansi 封送），密码以托管字符串明文传入原生库。

**线程安全 / 并发问题**
- **大量忙等自旋**：`setBatchData` 的 `while (isBlocked())`（FldrStatement.cs:702-715）、`close()` 的 `while (!asyncStopFlag && ...)`（:973-976）、`asyncColumnDataList` 的 `while (!batchQueue.put(...))`（:1380）均无 sleep/信号量，空转烧 CPU。
- `usedFldr` 是普通 `HashSet<Fldr>`，却在 `clusterProcess` 的并行任务回调里并发 `add`（FldrStatement.cs:67, 1245）——并发写非线程安全集合。
- `TableInfo.msgVersion` 是**静态可变字段**，由 `FLDR_GET_TABLE_INFO` 解码时写入（FLDR_GET_TABLE_INFO.cs:164）——所有连接共享，后被 `FldrStatement.setFldrProperties` 读进 setEnv（FldrStatement.cs:1843），跨连接存在竞态。
- `FLDR_INSERT.compress` 是 static 可变字段（:25）；`FldrUtil.fromDate/fromDate12` 对纯局部计算加全局静态锁（FldrUtil.cs:8-13），无谓的全局串行化点。
- 列级并行用 `Task.Factory.StartNew` 且未加 `LongRunning`（FldrStatement.cs:1963-2081, FLDR_INSERT.cs:1716-1753），宽表（每列一个任务）会瞬间占满 ThreadPool；错误传播依赖 `CountdownEvent` + 首个任务异常（`threadCountDownHelper`，:2082-2102）。
- `FldrErrorWriter.flushQueue` 是 **static**（FldrErrorWriter.cs:13）但每个 FldrStatement 都 `new` 一个实例并各起一线程——多实例并发装载时多线程消费同一静态队列，日志会交叉且线程数随装载次数增长。

**资源泄漏风险**
- `DmBulkCopy` 构造函数 `conn.Open()`（DmBulkCopy.cs:145），但 `Close()` 只置标志位（:526-529），**不关闭自己打开的连接**。
- `FldrStatement.close()` 若 `insertFlag==false`（从未 executeBatch）不会发 FLDR_CLR，但 close 的异常路径里 catch 后仅 `Console.WriteLine(ex.StackTrace)` 吞错（:977-981）；DPC 模式为每个 BP 节点 `connection.Clone()` + `Open()` 新建物理连接（:1735-1742），只在 close 的 finally 里关闭（:1025-1031），中途泄漏窗口大。
- 原生句柄：见上 `DmFldrExport` 双重释放；`AllocSinst` 成功但 `Export` 失败的路径会 free（DmFldrDllCall.cs:71），而 `fldr_alloc` 成功、`SetAttr` 中途失败的路径**不 free**（SetAttr 只抛异常，:123-126）——句柄泄漏。

## 5. 有趣实现细节

- **Java 血统明显**：`List.get(i)/set(i)/isEmpty()`、`String.startsWith/charAt/substring`、`CountdownEvent` 命名 `countDownLatch`、`CopyOnWriteArrayList`、`AtomicBoolean/AtomicInteger`——该驱动是从 JDBC 驱动（DmJdbcDriver）机械移植的，`A/B.cs`、`A/C.cs` 的混淆式命名是原始包 `dm.jdbc.driver` 被折叠的结果。
- **Decimal 编码是 Oracle 风格的"两位数字一字节 +1 偏移"码表**（`charToDigit`：digit pair (x,y) → x*10+y+1），负数按 102 取补并以 102 结尾——即 DM 与 Oracle NUMBER 线上格式同源；`Bdta.readDecimal` 是其逆运算，且 `sign==128` 时直接返回 `decimal.MaxValue`（Bdta.cs:169-171，零的歧义处理）。
- **死代码/半成品异步管线**：`FldrStatement.setBatchData`/`asyncColumnDataList`/`batchQueue`/`usedAsyncInsert` 构成一套"预编码 + 队列"的异步装载框架，但 `batchQueue` 只有 put 没有任何消费者，`usedAsyncInsert` 从未被置 true（全库搜索仅 FldrStatement.cs:1801/1809 的 compareAndSet(true→false)）——是未完成或未发布的特性。同理 `reconnectTimes=3`/`connInterval=3000`（:111-113）只有 setter 没有使用者。
- **Fldr.getSetId 的伪 LRU**（Fldr.cs:74-96）：setId 用满 bldrNum 后对 bldrNum 取模覆盖最旧映射，实现里 `setId++` 在被回收分支中永不生效（返回值固定为取模结果），逻辑微妙。
- 服务器错误码 **-9106** 被特判翻译为中文提示"存在使用相同FldrConfig和DmConnection创建的FldrStatement且未关闭"（Fldr.cs:36-39）——说明同一连接同表只允许一个未关闭的 FldrStatement。
- `DmFldrExport` 把密码以 `SetAttr(fsinst, 3, pwd, pwd.Length)` 明文长度传给原生库，且 `fldr_set_attr_2` 用 `string` 封送（DmFldrDllCall.cs:102-103）——`pwd.Length` 是**字符数而非字节数**，含非 ASCII 密码时长度错误。
- `FldrErrorWriter` 写错误行时会把整行数据 `string.Join(", ", arr[row])` 落盘（FLDR_INSERT.cs:1615）——**敏感数据明文落日志**，补强时可加脱敏钩子。
- `MSG.convertToOracleCode` 是空实现直接返回原值（MSG.cs:293-296），Oracle 兼容错误码转换在该版本未启用。
- `FldrConfig.ToString()` 中字段名拼写为 `defualtColumns`（FldrConfig.cs:29），连拼写错误都从 Java 版原样保留。
