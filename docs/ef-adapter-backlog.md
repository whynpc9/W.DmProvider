# EF 适配待做清单（驱动层限制登记）

> 来源：EF Core 适配达梦数据库时确认的驱动层限制。每条均已对照 DM.DmProvider 8.3.1.47463 反编译源码核实（引用格式 `文件:行号`，基线见 `docs/reverse/`）。
> 日期：2026-09-28

## 登记的限制（已核实）

### 1. 无 DbBatch，不承诺多命令批次

**状态：已核实，属实。**

- 全程序集无 `DbBatch`/`CanCreateBatch`/`CreateBatch` 任何实现（全库 grep 无匹配）；`DmClientFactory`（`Dm/DmClientFactory.cs`）未覆写批量工厂方法。
- 驱动内唯一的批量设施是 internal 的 `DmCommandSet`（`Dm/DmCommandSet.cs`）：把**连续且 CommandText 完全相同**的命令合并为数组绑定参数，再逐条 `ExecuteNonQuery`——本质是单命令数组绑定，不是多命令报文批处理。
- EF 侧策略：阶段 3 只评估**单条命令内的多行 VALUES**；多命令拼接（`;` 分隔）是否被服务器/驱动接受**未验证**，不要依赖。

待做：
- [ ] 动态验证 DM 服务器是否接受单 CommandText 内多语句（`;` 分隔）——决定 EF 批处理回退形态
- [ ] 多行 VALUES 的参数数量上限实测（协议单参数行内上限 65535 字节、`MSG_PARAM_MAX_LEN=64MB`，见 `docs/reverse/02` §3）
- [ ] 跟踪上游：若后续版本驱动提供 DbBatch，再开放多命令批次承诺

### 2. 异步回退同步，不承诺非阻塞 I/O 与及时取消

**状态：已核实，属实。**

- 全驱动无任何 `*Async` 覆写，基类 `DbCommand.ExecuteReaderAsync` 等回落为同步包装（`docs/reverse/01` §2.4、缺陷 #19）。
- `DmCommand.Cancel()` 的实现是**断线重连**（`Dm/DmCommand.cs:455-461` 调 `Reconnect()`），不是服务器端取消（协议里 `CMD_CANCLE=11` 定义了但客户端未实现，`docs/reverse/02` §2.2）。

待做：
- [ ] EF 适配文档明示：Async API 可用但为同步语义；取消 = 重建连接，代价高
- [ ] （补强 backlog）实现真异步收发是驱动层最大单项增强，独立立项

### 3. RepeatableRead 与 Snapshot 在开始事务时被拒绝

**状态：已核实，属实。**

`Dm/DmConnInstance.cs:278-320` `SetTransactionIsolation` 的实际行为：

| IsolationLevel | 驱动行为 |
|---|---|
| Unspecified / ReadCommitted | → 1（READ COMMITTED） |
| ReadUncommitted | → 0（READ UNCOMMITTED） |
| Serializable | → 3（SERIALIZABLE） |
| RepeatableRead | **仅 `compatibleMode=MYSQL` 时映射为 1（READ COMMITTED，静默降级！）**，否则抛 `ECNET_INVALID_TRAN_ISOLATION` |
| Snapshot / Chaos / 其他 | 直接抛 `ECNET_INVALID_TRAN_ISOLATION` |

注意：隔离级别通过字面量 SQL `SET TRANSACTION ISOLATION LEVEL ...` 下发（:310-316），且仅当 `level != m_ConnPro.IsolationLevel` 时才执行——**快照隔离根本不存在于协议映射里**，RepeatableRead 在 MySQL 兼容模式下是静默降级而非拒绝。

待做：
- [ ] EF 侧在 BeginTransaction 拦截这两个级别，给出明确异常信息（而非透传驱动错误码）
- [ ] 文档标注：MySQL 兼容模式下 RepeatableRead 实际执行的是 ReadCommitted

### 4. ReadUncommitted / Serializable 下跟踪式 SaveChanges 会失败，不写成已支持的保存路径

**状态：驱动侧映射属实；失败根因需动态验证。**

- 驱动确实会把 0（READ UNCOMMITTED）和 3（SERIALIZABLE）下发给服务器（`DmConnInstance.cs:288-293`），即**驱动不拦，失败发生在服务器/会话语义层**。
- 可疑的驱动侧放大因素：
  - `GetKeyCols`/`GetUniqueCols` 为 SchemaTable 实时查 `SYS.SYSINDEXES` 等系统表（`Dm/DmDataReader.cs:1353-1391`），在 Serializable 下可能触发自锁/一致性读问题；
  - 单连接同时只允许一个活动 DataReader（错误码 6092），SaveChanges 流水线上的元数据查询易与主 reader 冲突；
  - `CheckCommandBehavior` 位掩码 `==` 比较 bug（`docs/reverse/README.md` 缺陷 #5）使 `SchemaOnly|KeyInfo` 组合失效，SchemaTable 的键信息可能缺失，进而影响 EF 生成 UPDATE/DELETE 的 WHERE 子句。

待做：
- [ ] 搭最小复现（两个隔离级 × 跟踪式 SaveChanges），抓实际错误码，区分服务器限制 vs 上述驱动 bug 放大
- [ ] EF 文档把支持矩阵写成：ReadCommitted（默认）为唯一承诺的保存路径；其余级别"可连接可查询，不承诺保存"
- [ ] 若复现指向 `KeyInfo` 组合标志 bug，并入 P0 修复清单（`docs/reverse/README.md` #5）

### 5. 不对连接串超时关键字及其单位作断言

**状态：属实——官方文档缺失；但代码层已有确定事实，可作为文档基线。**

反编译得到的事实（优先级高于任何外部文档）：

| 键 | 单位 | 默认 | 代码依据 |
|---|---|---|---|
| `connect_timeout` | 毫秒 | 5000 | `Dm.Config/DmOptionHelper.cs:40` |
| `conn_pool_timeout` | 毫秒 | = connect_timeout | `docs/reverse/01` §2.2 |
| `socketTimeout` | 毫秒 | 0（无限） | 但**会被服务器登录响应的心跳超时覆盖**（`Dm/DmConnProperty.cs:651-661`） |
| `command_timeout` | 秒 | 0（无限） | `Dm/DmConnectionStringBuilder.cs`（别名 sessionTimeout） |
| **坑** | — | — | COMMIT/LOB/FLDR 走 `MSG<T>` 新式路径，收发超时用的是 **connect_timeout 而非 socketTimeout**（`A/B.cs:202-211`），LOB 大传输默认 5 秒即超时 |

待做：
- [ ] EF 适配文档只承诺 `connect_timeout`/`command_timeout` 两个键及上述实测单位，其余标注"行为以驱动版本实测为准"
- [ ] 动态验证 `command_timeout` 单位（秒 vs 毫秒）与 0 值语义——当前仅来自字段流向推断，未经抓包确认
- [ ] LOB 场景在文档中显式提示放大 `connect_timeout`（对应 `docs/reverse/README.md` 缺陷 #9）

## 附：与 EF 适配直接相关的其他驱动事实（来自逆向，备用）

- EF Core 识别魔法注释：驱动靠 SQL 文本中 `/*EFCOREROWCOUNT*/` 出现次数控制 `NextResult`（`Dm/DmDataReader.cs:836-848`），连接串 `EFCoreNextResult=true`（默认开）——EF 适配层生成 SQL 时可利用/需避开此注释。
- 参数名归一：参数名前缀 `@`/`:` 统一归一为 `:`，占位符为 `:p{n}` 或 `?`（`Dm/DmParameter.cs:794-801`）。
- 单连接单活动 Reader（6092）：EF 的 MARS 类用法全部不可行。
- Guid 映射：`CHAR/VARCHAR2 prec==36` 列自动映射为 Guid（`Dm/DmSqlType.cs` `CTypeToSystemType`）；VARCHAR 需 `varchar36ToGuid=true`。
- 分布式事务：`Promote()` 未实现（`Dm/DmPromotableTransaction.cs:60-63`），TransactionScope 跨资源必然失败——EF 侧不要承诺 ambient transaction 多连接场景。
- 枚举参数按**名字序号**而非底层数值写入（`Dm/DmSetValue.cs:1735-1750`），`[Flags]`/自定义数值枚举会写错。
- `DmTransaction.Dispose` 在 Oracle 兼容模式下自动 Commit（`Dm/DmTransaction.cs:192-202`）——EF `using` 事务块的行为在非默认兼容模式下会反转，需文档警示。
