分析完成。以下为该子系统的完整逆向分析报告。

---

# DM.DmProvider 8.3.1.47463 —— 过滤器 / 日志 / 高可用层分析报告

## 1. 核心类清单

| 类 | 文件 | 职责 |
|---|---|---|
| `IFilter` | `Dm.filter/IFilter.cs` | 拦截器接口，约 200 个方法，覆盖 Connection/Command/Transaction/DataReader/Parameter( Collection)/DataAdapter/CommandBuilder/ConnectionStringBuilder 全部公开操作 |
| `IFilterInfo` | `Dm.filter/IFilterInfo.cs` | 挂载点接口（`ID` + `filterHead` + `LogInfo/RWInfo/RecoverInfo`），由 DmConnection、DmCommand、DmTransaction、DmDataReader、DmParameter、DmParameterCollection、DmDataAdapter 等实现 |
| `BaseFilter` | `Dm.filter/BaseFilter.cs` | 责任链基类：默认实现为"有 next 则委托 next，否则调目标对象的 `do_Xxx()`"；`CreateFilterChain()` 按配置组装链 |
| `LogFilter` | `Dm.filter.log/LogFilter.cs` | 全方法埋点：构造 `LogRecord`（含 SQL、参数、返回值、耗时、executeId），异常改写为 `DmException` 副本 |
| `ConnPoolFilter` | `Dm.filter/ConnPoolFilter.cs` | 连接池过滤器，拦截 `Open/Close/ForceClose`，委托 `ConnPoolCache` |
| `ReconnectFilter` | `Dm.filter.reconnect/ReconnectFilter.cs` | 自动重连 + DSC 节点恢复（EP_RECOVER）；注意它是过滤器中唯一 `public` 的类 |
| `RWFilter2` | `Dm.filter.rw/RWFilter2.cs` | 读写分离过滤器：Open 时强制主库登录并建立备库连接；属性 setter 双向同步到备库 command |
| `RWUtil2` | `Dm.filter.rw/RWUtil2.cs` | RW 核心逻辑：选备库、只读判定、路由分发、语句镜像回放、故障摘除 |
| `RWCounter` | `Dm.filter.rw/RWCounter.cs` | 基于"令牌桶/配额递减"的主备按比例分配计数器（进程级静态缓存） |
| `RWSite` | `Dm.filter.rw/RWSite.cs` | 枚举 `PRIMARY/STANDBY/ANY` |
| `RWInfo` / `LogInfo` / `RecoverInfo` | `Dm.filter/*.cs` | 每连接/每命令的 RW、日志、恢复上下文状态袋 |
| `ILogger`/`Logger`/`LogFactory` | `Dm.filter.log/` | 日志门面：3 级（ERROR/SQL/INFO），格式化 `FormatSource/FormatTrace` |
| `LogRecord` | `Dm.filter.log/LogRecord.cs` | 单次调用日志记录（Stopwatch 计时、SQL、返回值、异常） |
| `LogWriter` | `Dm.filter.log/LogWriter.cs` | 单例异步落盘：后台线程 `DmProvider-LogFlusher` 批量写文件、按大小滚动 |
| `DmConnProperty` | `Dm/DmConnProperty.cs` | 连接属性袋（`Dictionary<string,object>`），dm_svc.conf 叠加、地址/用户重映射 |
| `DBAliveCheckThread` | `Dm/DBAliveCheckThread.cs` | 全局后台线程 `DB-ALIVE-CHECK-THREAD`，周期性 TCP 探活并掐死失联实例 |
| `EP`/`EPGroup`/`EPSelector`(+`WellDistributeSelector`/`HeadFirstSelector`) | `Dm/EP*.cs` | 服务节点、节点组连接重试、两种选主策略 |
| `DmSvcConfig`/`DmOption`/`DmOptionHelper` | `Dm.Config/` | dm_svc.conf 解析与全部连接串键的默认值/校验 |

## 2. 重点问题

### 2.1 IFilter 拦截器机制

**组装点**：`BaseFilter.CreateFilterChain(IFilterInfo, DmConnProperty)`（`Dm.filter/BaseFilter.cs:18-64`）。链的顺序固定为：

```
LogFilter → ConnPoolFilter → ReconnectFilter → RWFilter2 → (null ⇒ 调 do_Xxx 真实实现)
```

- `DmSvcConfig.logLevel != OFF` 加 LogFilter（注意是**全局静态** `DmSvcConfig.logLevel`，不是连接级属性）；
- `ConnPooling=true` 加 ConnPoolFilter；`DoSwitch != OFF` 加 ReconnectFilter；`RwSeparate > 0` 加 RWFilter2。

**挂载时机**：
- 连接级：给 `DmConnection.ConnectionString` 赋值时（`Dm/DmConnection.cs:130`）；
- 命令级：`DmCommand.DbConnection` setter 里对 command 和参数集合各建一次链（`Dm/DmCommand.cs:176-177`）。

**调用入口模式**：每个公开 API 都是同一个模板（例 `DmConnection.cs:664-682`）：

```csharp
public override void Open() {
    ...
    if (filterHead == null) { Connect(); } else { filterHead.Open(this); }
}
```

即：**可拦截点 = IFilter 中列出的全部方法**（Connection 的 Open/Close/BeginTransaction/GetSchema/fldrStatement、Command 的 Execute\*/Prepare/Cancel 及全部属性 setter、Transaction 的 Commit/Rollback/Save/Release、DataReader 的全部 Get\*/Read/NextResult、ParameterCollection 全部增删改查、DataAdapter 的 Fill/Update/Batch、CommandBuilder、ConnectionStringBuilder）。链路末端 `BaseFilter` 调对象的 `do_Xxx()` 内部实现（命名约定统一，极易做 IL patch 对照）。

**注意**：`IFilter` 接口没有包含 `Save/Rollback/Release(savepoint)`、`CreateStruct/CreateArray/CreateIndexTable` 以及 `ResultSetMetaData_*` 系列，但 `BaseFilter` 上有对应 `virtual` 方法（`BaseFilter.cs:549-583, 1950-2137`）——接口与实现已不同步，属于扩展接口时的陷阱。

### 2.2 读写分离（RW）实现

启用条件：`rw_separate > 0`（连接串键见 §3）。启用后 `DmConnProperty.AdjustProperty()` 强制 `LoginMode=onlyprimary` 且**关闭连接池**（`DmConnProperty.cs:1203-1210`）。

**站点列表获取**（`Dm.filter.rw/RWUtil2.cs:75-129`）：主库上查询 `v$arch_status` ⨝ `V$DM_MAL_INI` ⨝ `V$MAL_LINK_STATUS`，取 `arch_type IN ('TIMELY','REALTIME') AND arch_status='VALID'` 的备机；列名有新旧两版（`mal_inst_name` vs `inst_name`），先查新语法，捕获 `DbException` 回退旧语法（`SQL_SELECT_STANDBY2`/`SQL_SELECT_STANDBY` 常量，:15-17）。从结果中**随机**选一台（`rwCounter.Random(num)`，:105-110）。

**备库连接建立**（`connectStandby`，:45-73）：克隆主连接属性字典 → 改写 Server/Port 为选中备机 → 强制 `RWStandby=true, LoginMode=onlystandby, SwitchTimes=0, EPGroup=null` → 直接 `Connect()`（绕过过滤器链）→ 校验 `SvrMode==2(STANDBY) && SvrStat==4(OPEN)`，不符即丢弃。**每条主连接只挂一个备库连接**（`RWInfo.connStandby`）。

**负载均衡**（`RWCounter.cs`）：按 `server_port_rwPercent` 做 key 的进程级静态缓存。算法是"配额递减"：`increments[0] = rwPercent * standbyCount`（主库配额），`increments[i] = 100 - rwPercent`（每个备库），再除以最大公约数（`divis()`, :243）。每次分发扣减对应 `flag[]`，全部扣完则回补（`adjustNtrx()`, :131-161）。决策在 `count()`（:99-129）：`primaryPercent==1.0` 或主库剩余配额大于备库时走主库，否则走备库。默认 `rwPercent=25`，即 1 主 : 3×(75/n) 备的配比。

**路由规则**（`RWUtil2.distribute()`, :283-299）：
1. 备库不活 → `toPrimary()`；
2. `checkReadonly()` 判定为写 → 主库；
3. 已有事务粘连（当前站点事务仍 Valid）→ 保持原站点；
4. 主连接事务隔离级为 `Serializable` → 强制主库；
5. 否则 `toAny()` 按 RWCounter 比例分配。

**只读判定**（`checkReadonly()`, :272-281）极其粗糙：取 SQL 第一个空格前的单词，=SELECT 为只读；=INSERT/UPDATE/DELETE/CREATE/TRUNCATE/DROP/ALTER 为写；**其余一律视为只读**——`MERGE`、`CALL`（写存储过程）、`GRANT`、`SET`、`/*注释*/SELECT`、CTE 中的写等全部会被错误路由到备库。这是最重要的可 patch 点之一。

**语句镜像与故障回退**（`RWUtil2.execute<T>()`, :163-270，泛型 + 三个委托 `execute/closeDmDataReader/executeByOther`，定义在 3 个单行文件中）：
- 执行后按 `RetCmdType` 决定是否把同一语句**回放到另一站点**（静默吞异常）：`147 COMMIT / 148 ROLLBACK / 151 SAVEPNT / 153 SET CURSCH / 165 SET TIME_ZONE / 166 SET SESS_TRAN` 全部镜像；`162 DML_CALL` 仅当过程名为 `SP_SET_PARA_VALUE`/`SP_SET_SESSION_READONLY` 时镜像（:216-236）；
- `160 DML_SELECT` 且 `rwHA=true`：若语句在备库执行且**结果集为空**（`CurResultSetCache.datas.Length == 0`），认为可能是归档延迟，切回主库重跑（:237-243, 254-263）；
- 备库上抛 `DbException` → `afterExceptionOnStandby`：若是 6001 通信错误则摘除备库，切主重跑；主库异常则直接上抛（:245-253, 131-137）。

**备库恢复**：每次执行前 `recoverStandby()`（:31-43）——备库不活且距上次尝试超过 `rwStandbyRecoverTime`（默认 60000ms）才重连，避免惊群。

**属性双向同步**：`RWFilter2` 对 Command 的所有 setter（CommandText/Timeout/Type/DbConnection/DbTransaction…）都先同步到 `cmdStandby` 再走主链（`RWFilter2.cs:83-201`）。但注意 `transStandby` 字段**从未被赋过非 null 值**（全库仅 `RWInfo.init()` 置 null 和 `RWFilter2.cs:179` 读取）——备库侧事务镜像是**未完成的半成品**；savepoint 的同步改由 `RWFilter2.Save/Rollback/Release` 直接操作备库 `m_ConnInst`（:266-315）。

**RW 重连**：`RWUtil2.reconnect()`（:19-29）= 摘备库 → 主库 `Reconnect()` → 重建 RWCounter → 重选备库。

### 2.3 自动重连机制

`ReconnectFilter`（`Dm.filter.reconnect/ReconnectFilter.cs`）对所有 override 方法包了同一个模板：try 调 `base.Xxx()`，catch 任意异常进 `autoReconnect()`。

**触发条件**（:21-39）：仅当异常是 `DmException` 且 Number ∈ {**6001** ECNET_COMMUNITION_ERROR, **6027** ECNET_NO_SOCKET_DATA}，或是 `SocketException` 时重连；其他异常原样上抛。

**重连语义**（`reconnect()`, :41-59）：
- 开启 RW → `RWUtil2.reconnect()`，否则 `DmConnection.Reconnect()`（`DmConnection.cs:396-403`：`do_Close()` + `lock(this) Connect()`，走 EPGroup 重新选节点）；
- **无论成败都抛异常**：失败抛 6094 ECNET_CONNECTION_SWITCH_FAILED（且吞掉原始异常，无 inner），成功抛 6093 ECNET_CONNECTION_SWITCHED——即应用侧第一条撞上故障的语句必然失败，由应用决定是否重试；getter 类方法随后返回兜底值（`getState` 重连后返回 `Open`，:231-242，会掩盖真实状态）。

**节点恢复（EP_RECOVER）**（`checkAndRecover()`, :61-103）：仅 `DoSwitch == EP_RECOVER` 且 `Cluster == DSC` 时生效；在无活动事务、当前节点不是 epList[0]、距上次检查超过 `switchInterval`（默认 200ms）时，用 `DriverUtil.loadDscEpSites()`（查 `V$DSC_EP_INFO`/`V$DCR_EP`，`Dm.util/DriverUtil.cs:64-85`）找出状态 OK 且排序在当前节点之前的 EP，存在则主动 `Reconnect()` 回切。挂接点：Execute\*/Prepare 之前、Commit/Rollback/Dispose/Save 之后（:550-710）。

**底层重试**：`EPGroup.connect()`（`Dm/EPGroup.cs:61-98`）按 `switchTimes+1` 轮（默认 2 轮）、每轮间隔 `switchInterval` 遍历排序后的 EP 列表；排序器由 `epSelector` 决定——`WELL_DISTRIBUTE`（默认，随机起点轮转 + 按 sort 值降序，`WellDistributeSelector.cs:19-43`）或 `HEAD_FIRST`（永远从第一个节点开始，`HeadFirstSelector.cs:12-28`）。`EP.calcSort()`（`Dm/EP.cs:65-126`）把 loginMode × serverMode × serverStatus 编码成分值（如 onlyprimary 下非主库直接 -1 判非法）；状态缓存 20 秒（`STATUS_VALID_TIME`, :24）。

**DB 探活线程**（`Dm/DBAliveCheckThread.cs`）：进程级单例后台线程，在 `DmConnInstance` 构造时注册（`DmConnInstance.cs:130`）。每轮以 `server:port` 去重后对该地址做一次裸 TCP 建连（`new D(host, port, dbAliveCheckTimeout)`，`A/D.cs` 即 DmCommTcpip），失败则 `GetCsi().d()` 强掐该地址下所有连接实例的 socket。仅当全局静态 `DmSvcConfig.dbAliveCheckFreq > 0` 时入队（默认 0 = 关闭）。

### 2.4 日志框架

**配置方式**：三层来源，全部最终写入 `DmSvcConfig` 的 **static 字段**（`logLevel/logDir/logSize/dbAliveCheckFreq/dbAliveCheckTimeout`，`Dm.Config/DmSvcConfig.cs:12-24`）：
1. dm_svc.conf（`svc_conf_path` 指定，默认路径见 `DmOptionHelper.dm_svc_confdef`）；
2. 连接串键 `loglevel/logdir/logsize`（`DmConnectionStringBuilder.do_setThis` 中特判写全局静态，:1341-1352）；
3. 代码直接改 `DmSvcConfig`（internal，需反射）。

**级别**：`LogLevel { OFF=0, ERROR=1, SQL=3, INFO=4 }`（`Dm.Config/LogLevel.cs`）——**注意没有 2**，且 SQL(3) > ERROR(1)，即开 SQL 必然带 ERROR；`Logger.ErrorEnabled => logLevel >= ERROR`、`SqlEnabled => >= SQL`、`InfoEnabled => >= INFO`（`Logger.cs:11-15`）。

**输出管道**：`LogFilter`（每个 API 一条 `LogRecord`）→ `Logger`（格式化 `[LEVEL - 时间] tid:x (IsBackground-x) {conn-N (sessId:…)} method(Type…); [RETURN]: … [PARAMS]: … [SQL]: … [USED TIME]: … [EXEC_ID]: …`）→ `LogWriter.Instance`（`LogWriter.cs`）→ 后台线程 `DmProvider-LogFlusher` 从 `BlockingQueue<byte[]>` 每次取 100 条批量写入 `logdir/DmProvider_yyyy_MM_dd_HH_mm_ss.fff.log`，`_curFileLength > logSize` 时滚动换新文件（默认 100MB，无保留个数/清理策略）。

**性能注意**：LogFilter 拦截了 DataReader 的**每一个** `GetXxx()/Read()/GetValue()`（`LogFilter.cs:939-1689`），logLevel=INFO 时逐行逐列记日志，量级是 O(行数×列数)；且每条日志同步做字符串格式化 + UTF8 编码再入队。

## 3. 关键常量 / 枚举值

**枚举**（`Dm.Config/`）：

| 枚举 | 值 |
|---|---|
| `LogLevel` | OFF=0, ERROR=1, SQL=3, INFO=4 |
| `DoSwitch` | OFF=0, CONN_ERROR=1, EP_RECOVER=2（>OFF 即装 ReconnectFilter；EP_RECOVER 仅 DSC 有意义） |
| `LoginModeFlag` | primaryfirst=0, onlyprimary=1, onlystandby=2, standbyfirst=3, normalfirst=4（默认 normalfirst） |
| `CLUSTER` | NORMAL=0, RW=1, DW=2, DSC=3, MPP=4 |
| `EpSelector` | WELL_DISTRIBUTE=0（默认）, HEAD_FIRST=1 |
| `LoginStatus` | OFF=0, MOUNT=3, OPEN=4, SUSPEND=5 |
| `RWSite` | PRIMARY=0, STANDBY=1, ANY=2 |

**本层相关连接串键**（`Dm/DmConst.cs`，键/别名/默认值）：

| 键 | 别名 | 默认 |
|---|---|---|
| `rw_separate` | rwSeparate | 0 |
| `rw_percent` | rwPercent | 25（0-100，主库占比） |
| `rwStandbyRecoverTime` | RW_STANDBY_RECOVER_TIME | 60000ms |
| `rwHA` | RW_HA | false |
| `rwAutoDistribute` / `rwFilterType` | … | true / 2 —— **定义了但全库无消费方，死配置**（也是 RWFilter**2** 命名的由来，v1 已删） |
| `doSwitch` | do_Switch, autoReconnect, auto_Reconnect | OFF |
| `cluster` | — | NORMAL |
| `loginMode` | login_mode | normalfirst |
| `epSelector` | epSelection, EP_SELECTION, EP_SELECTOR | WELL_DISTRIBUTE |
| `switchTimes` / `switchInterval` | SWITCH_TIME(S) / SWITCH_INTERVAL | 1 / 200ms |
| `loglevel`/`logdir`/`logsize` | log_level/log_dir/log_size | OFF / 当前目录 / 104857600 |
| `dbAliveCheckFreq`/`dbAliveCheckTimeout` | DB_ALIVE_CHECK_* | 0（关）/ 10000ms |
| `svc_conf_path` | dm_svc_conf | 平台相关默认路径 |
| `conn_pooling`/`conn_pool_size`/`conn_pool_timeout`/`conn_pool_idle_expired_time`/`conn_pool_idle_clear_interval`/`conn_pool_check` | connpooling 等 | false/100/… |

**RW 镜像回放用语句返回码**（`Dm/DmConst.cs:619-655`）：RET_COMMIT=147, RET_ROLLBACK=148, RET_SAVEPNT=151, RET_SET_CURSCH=153, RET_DML_SELECT=160, RET_DML_CALL=162, RET_SET_TIME_ZONE=165, RET_SET_SESS_TRAN=166（基址 RET_BASE=127）。

**服务器模式/状态**（`DmConst.cs:323-333`）：SERVER_MODE NORMAL=0/PRIMARY=1/STANDBY=2；SERVER_STATUS MOUNT=3/OPEN=4/SUSPEND=5。

**本层用到的错误码**（`Dm/DmErrorDefinition.cs`）：6001 通信错误、6027 socket 无数据、6060 连接已关、6075 连接未打开、6093 连接已切换（重连成功）、6094 切换失败、6115 服务器模式不符、6123 连接池超时。

## 4. 可扩展 / 可 patch 点与薄弱设计（核心）

### 4.1 结构性缺陷（patch 时务必知晓）

1. **过滤器是"共享单例 + 可变 next 指针"，存在跨连接串链污染与竞态**（最严重）。`LogFilter.Instance`/`ConnPoolFilter.Instance`/`ReconnectFilter.Instance`/`RWFilter2.Instance` 全是进程级静态（`BaseFilter.cs:25,33,43,49`），而 `CreateFilterChain` 每次直接改这些单例的 `next` 字段、且**从不清空旧的 next**。后果：
   - 连接 A（全功能）建链后 `LogFilter.Instance.next = ConnPoolFilter.Instance` 永久残留；之后只开日志的连接 B 会经由 stale next 意外走进连接池/重连/RW 过滤器；
   - 两个不同配置的连接并发建链时对 `next` 的写互相覆盖，无锁；
   - `ConnPoolFilter` 的 `ConnPoolSize/Timeout/...` setter 直接改共享 `ConnPoolCache` 的 `_limit/_timeout`（`ConnPoolFilter.cs:35-65`），**最后设置连接串的连接全局生效**。
   - patch 建议：把 `Instance` 换成每链独立 new 的实例（这些类都有私有构造，用反射/Harmony 即可），或重写 `CreateFilterChain`。

2. **`checkReadonly` 首词启发式误判**（`RWUtil2.cs:272-281`）：注释前缀、`WITH`、CTE 写、`MERGE`、`CALL`、DDL 其他动词全部按"只读"路由到备库 → 写操作打备库直接报错或（更糟）在只读库静默失败。可在 `RWUtil2.execute` 前置 hook 换成真正的 SQL 解析判定。

3. **重连丢原始异常 & 必抛语义**：`reconnect()` 失败抛 6094 且无 inner exception（`ReconnectFilter.cs:54-57`）；成功也抛 6093。getter 重连后返回假值（`getState` 返回 Open）。要增强可 patch `autoReconnect`/`reconnect` 以保留异常链、增加重试次数/退避。

4. **备库事务镜像是半成品**：`RWInfo.transStandby` 只读不写（全库无赋值点），`RWFilter2.setDbTransaction` 把它赋给备库 command 的永远是 null。若要在备库支持事务内读一致性，这里是天然的扩展挂点。

### 4.2 线程安全问题

5. `RWCounter` 全方法共用**一个全局静态锁** `obj`（`RWCounter.cs:8`），不同服务器组的计数也互斥；同时 `rwMap`（静态 Dictionary）**只增不删** → 按 `server_port_percent` 键无限累积（轻微泄漏）。`Random` 实例非线程安全但在锁内使用，OK。
6. `RWInfo` 各字段（`distribute/cmdStandby/cmdCurrent/readOnly`）无任何同步，同一 DmCommand 跨线程使用（如并行取消）会有竞态；`RWUtil2.execute` 中 `cmdCurrent` 读写非原子。
7. `DBAliveCheckThread.run()` 的 `while(true)` + 每轮 `Dequeue(_queue.Count)`：先读 Count 再 Dequeue 存在 TOCTOU（无碍功能）；真正的风险是**每轮重新入队所有实例**，连接数大时该线程持续占 CPU 一轮接一轮，且 `dictionary` 每轮重建。
8. `DmSvcConfig.logLevel` 等 static 被 `DmConnectionStringBuilder.do_setThis` 在 `lock(this)`（builder 实例锁）内改写（:1336-1360）——**全局可见性无保障**（非 volatile），且任何一条连接串都能改全局日志级别/目录，多应用域（多租户）场景互相干扰。

### 4.3 资源泄漏 / 资源浪费

9. `LogWriter` 用 `logSize` **同时**当滚动阈值和 `BufferedStream` 缓冲区大小（`LogWriter.cs:110-112`）：默认 100MB → 一开日志就常驻一个 100MB 缓冲区。`logFlushFreq`(30)/`logBufferSize`(32768) 对 LogWriter 而言是**死配置**（仅 `FldrErrorWriter` 使用）。日志文件无数量上限/清理。
10. `LogFactory.instances` 静态字典只增不删；`releaseAll()` 是不可达的 virtual 实例方法（`LogFactory.cs:34`），从未被调用。
11. `Logger(string name)` 构造**丢弃 name**（`Logger.cs:17-19`）——日志无分类能力，扩展时可直接利用这个被忽略的参数做按类过滤。
12. `ConnPoolFilter.Open` 的取池循环无上限（`ConnPoolFilter.cs:88-115`），每 100 次往 ERROR 打一条 `"[cnt]:" + num`；`CheckConnectionSurvival` 用 `select 1` 逐条验活（`ConnPoolCache.cs:239-266`），且**无视 `conn_pool_check` 配置**（该键在 ConnPoolFilter 路径上未被检查——始终验活）。
13. `ConnPool` 构造器里有**调试残留**：`if (limit == 3) { Console.WriteLine("limit=3,call stack trace:"); ... }`（`ConnPoolCache.cs:28-32`）——证明该代码未经清理即发布。
14. 备库连接 `connectStandby` 用 `new DmConnection(connection.ConnectionString)` + 直接 `Connect()`（`RWUtil2.cs:54-62`）：绕过过滤器链、不走连接池，每次重建都新建物理连接；`recoverStandby` 的时间戳节流（`tryRecoverTs`）是唯一的防抖。
15. `DBAliveCheckThread`/`LogWriter`/`ConnPoolCache.clearThread` 三个后台线程都是 `IsBackground=true` 的 `while(true)`，无停止机制（进程退出才结束）；`ConnPoolCache.Clear` 线程 `Thread.Sleep(_clearInterval)` ——若连接串把 `conn_pool_idle_clear_interval` 设为 0，该线程变成忙等。

### 4.4 顺手的 patch 面（由易到难）

- `BaseFilter` 全部方法 `virtual`，类 `public`；自定义过滤器继承后插入 `next` 链即可（但受缺陷 1 影响，建议同时换掉单例）。
- `ReconnectFilter` 是 `public class`，`reconnect()` 是 `public static`——外部代码/Harmony 可直接 hook。
- `RWUtil2` 是 internal static，全是 static 方法——Harmony 前缀/后缀 patch 友好（`checkReadonly`、`distribute`、`execute<T>`、`chooseValidStandby` 都是单点）。
- `EP.calcSort`/`EPSelector.sortDBList` 是选节点的单点，可实现自定义负载策略。
- `DmSvcConfig` 的 static 字段可用反射直接改（运行时调日志级别无需重启）。

## 5. 有趣实现细节

- **调试残留**：`ConnPoolCache.cs:28-32` 的 `limit==3` 打印堆栈；`DmConnection.do_Close`/`do_EnlistTransaction` 里大量 `StringBuilder` 拼接执行轨迹塞进异常 message（`[extraInfo]:{do_Close}->{...}`），是厂商排障通道，patch 时可利用同款手法。
- **dm_svc.conf 解析器有真 bug**：`DmSvcConfig.ParseConfigFile`（:173-267）内层循环遇到下一个 `[section]` 头时，`_ = array2[0].Length;` 丢弃了新长度，仍用**外层** section 名长度做 `Substring(1, length-2)`（:204-208）——新 section 名长度不同则 key 截错或抛异常（被外层 `catch(Exception){}` 静默吞掉）。且整个文件解析任何异常都静默。
- **`chooseValidStandby` 的 NRE 陷阱**：第一次查询抛 `DbException` 时先 `dmDataReader.do_Close()`（此时 dmDataReader 大概率为 null，`RWUtil2.cs:83-86`），finally 里也无条件 `do_Close()`（:122）——依赖 `DmDataReader.do_Close` 的容错或被吞异常兜底。
- **历史遗留**：`rwFilterType`（默认 2）与 `rwAutoDistribute` 属性定义完整但全库无读取方——RWFilter v1 已被删除，"2"是二代实现；`IFilter` 与 `BaseFilter` 方法集已漂移（§2.1）。
- **连接池 key**：`ServName/User/PropertyHashCode`，其中 `PropertyHashCode = 排序拼接串.GetHashCode()`（`DmConnProperty.cs:163`）——.NET Core 下 string.GetHashCode 每进程随机，无碍池内一致，但**不能跨进程比较**；且不同连接串文本但同语义会进不同池。
- **RW 强制约束**：`rw_separate>0` 时 `AdjustProperty()` 强制关连接池 + onlyprimary（`DmConnProperty.cs:1203-1210`）；但 `ConnPoolFilter.Close` 里仍有对 `RWInfo` 的搬运逻辑（`ConnPoolFilter.cs:124-133`）——说明这两个特性曾经允许叠加，现在是死代码路径。
- **EP_RECOVER 只认 DSC**：`checkAndRecover` 中 `Cluster == CLUSTER.DSC ? loadDscEpSites() : null`，RW/DW 集群配置了 EP_RECOVER 也永远不会回切（`ReconnectFilter.cs:72-75`）。
- **日志"纳秒"是假精度**：`LogRecord.formatUsedTime` 把 `ElapsedMilliseconds * 10^6` 当 ns 显示（`LogRecord.cs:103,111-122`）。
- 日志/探活/重连行为相关的 5 个 `DmSvcConfig` static 字段都能被**任意一条连接串**静默改写（`DmConnectionStringBuilder.cs:1341-1360`）——这既是隐患，也是最廉价的运行时调参后门。
