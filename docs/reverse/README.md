# DM.DmProvider 8.3.1.47463 逆向基础报告

> 目的：为达梦数据库 .NET 驱动（DM.DmProvider）的"补强"（扩展/增强/修复）工程建立逆向基线。
> 方法：参考 [dotnet-reverse skill](https://github.com/zhaoxuya520/reverse-skill/tree/main/skills/dotnet-reverse) 六阶段工作流（Identify → Detect → Deobfuscate → Static Analyze → Dynamic → Patch），本报告覆盖前四阶段；动态调试与 Patch 阶段视后续补强需求展开。
> 日期：2026-09-28

## 0. 样本信息

| 项 | 值 |
|---|---|
| 包 | [DM.DmProvider 8.3.1.47463](https://www.nuget.org/packages/DM.DmProvider/8.3.1.47463)（NuGet 上最新版，共 13 个 8.3.1.x 版本） |
| 许可证 | Apache-2.0 |
| 目标框架 | net40 / net45 / netcoreapp2.1 / netcoreapp3.1 / net5.0-net9.0 / netstandard2.0（共 10 个 TFM，各有 en/zh-CN/zh-HK/zh-TW 卫星资源） |
| 分析对象 | `lib/net8.0/DM.DmProvider.dll`（884,736 字节） |
| 托管身份 | 纯托管 PE32 Mono/.NET assembly（ILSpy `file` 确认） |
| 混淆 | **无**。类型/方法名完整可读，无 ConfuserEx/SmartAssembly 等特征，无需 de4dot |
| 反编译 | `ilspycmd -p` → `decompiled/net8.0/`，238 个 .cs 文件，约 7.5 万行 |
| 原生依赖 | 非纯托管：`dmcyt`（加密/DH）、`dmfldr`（快速导出）、`dmcalc.dll`（XDEC，移植残留死代码） |

目录布局：

```
W.DmProvider/
├── packages/extracted/          # NuGet 包解包（全部 TFM）
├── decompiled/net8.0/           # ilspycmd 反编译工程（含 DM.DmProvider.csproj，可直接打开阅读）
└── docs/reverse/
    ├── README.md                # 本文件
    ├── 01-ado-api-layer.md      # DmConnection/Command/Reader/Parameter/事务/连接池/连接串
    ├── 02-network-protocol.md   # 握手登录/加密协商/报文格式/opcode 全表/超时心跳
    ├── 03-type-system.md        # DmDbType↔协议类型码映射/Get/SetValue/LOB/复杂类型/几何
    ├── 04-bulkcopy-fldr.md      # BulkCopy 三路径/FLDR 协议/列式打包格式/原生 dmfldr
    └── 05-filter-log-ha.md      # 过滤器链/读写分离/自动重连/日志框架
```

## 1. 总体架构

```
用户代码
  │  ADO.NET API（DbConnection/DbCommand/...）
  ▼
DmConnection / DmCommand / DmDataReader / DmTransaction   （公开门面，多为 sealed）
  │  每个公开方法二选一：filterHead.Xxx() 或 do_Xxx()
  ▼
过滤器链（BaseFilter 责任链，CreateFilterChain 按配置组装）
  LogFilter → ConnPoolFilter → ReconnectFilter → RWFilter2 → 末端调 do_Xxx()
  ▼
DmConnInstance（物理连接 = 一条 TCP 会话；语句池、当前事务）
  │  m_Csi: A.B（协议访问对象）+ A.A（语句句柄）+ A.D（socket/SSL 传输）+ A.C/c（报文编解码）
  ▼
DM 服务器（默认 localhost:5236，私有二进制协议，64 字节小端消息头，协议版本 21）
```

命名空间折叠说明：反编译目录 `A/`（A.cs/B.cs/C.cs/D.cs）是原始 `dm.jdbc.driver` 包级内部类被 IL 折叠的结果 —— 该驱动整体从 DM JDBC 驱动机械移植（Java 命名习惯遍布全库）。`NetTaste/` 与网络无关，是 Coco/R 生成的 SQL 词法分析器。对照表见 `02-network-protocol.md` §0。

## 2. 补强工作最重要的三个事实

1. **官方预留的扩展缝是过滤器链**。`Dm.filter.BaseFilter` 有约 150 个 `public virtual` 方法，覆盖 Connection/Command/Reader/Transaction/Parameter/DataAdapter 的每一个公开 API；所有 ADO 对象实现 `IFilterInfo`（`filterHead` 可 public set）。LogFilter/ConnPoolFilter/ReconnectFilter/RWFilter2 本身就是范本。注入时必须注意：`ConnectionString` setter、构造函数会重建链，注入要在最后设置之后；且官方过滤器是**共享单例+可变 next 指针**，有跨连接污染竞态（见下）。另外 `DmCommand.do_Execute*` 是 `internal virtual`，官方 SkyWalking 探针就靠反射加载 `DmSkyWalkingAgent` 覆写它 —— 补强程序集可复制同款模式。
2. **sealed 类居多，继承路线基本不可行**（DmConnection/DmTransaction/DmDataAdapter/DmCommandBuilder/DmParameterCollection 全 sealed），非侵入增强 = 过滤器链 + 委托包装；修复内部 bug = Harmony 运行时 patch 或 fork 反编译源码重编译。
3. **驱动并不纯托管**：登录加密的 DH 共享密钥、对称加解密都在原生库 `dmcyt` 里；`ThirdPartCipherDLL` 只 P/Invoke `kernel32.dll`（LoadLibrary），Linux/macOS 下第三方加密必然失败。跨平台补强绕不开这两点。

## 3. 关键能力地图（详见各分报告）

- **连接字符串**：全部键/别名/默认值/取值范围表 → `01` §2.2。注意默认凭据硬编码 `SYSDBA/SYSDBA`、`localhost:5236`；SSL 私钥默认口令 `"changeit"`；`loglevel/logdir/logsize/dbAliveCheck*` 五个连接串键会静默改写**进程级静态全局**配置。
- **连接池**：过滤器形态挂载（`ConnPoolFilter` + `Dm.util.ConnPoolCache`），池 key = `Server:Port/User/PropertyHashCode(string.GetHashCode)`；**每次 checkout 无条件发 `select 1` 验活**，而 `conn_pool_check` 键定义了却从未被消费（哑配置）。
- **协议**：64 字节小端头（stmtId/cmd/bodyLen/sqlCode/svrMode/CRC…），opcode 全表（LOGIN=1, PREPARE=5, EXECUTE2=13, FETCH=7, PUT_DATA2=26, LOB=29-32, FLDR=53-123, XA=60, STARTUP=200, HEART=269…）→ `02` §2.2。登录加密：512 位硬编码 DH + DES/3DES/AES/RC4（默认 AES256_CFB 或 DES_CFB 视协议版本），报文加密为密文+明文 MD5 摘要拼接。压缩协商了字段但**未实现**（半成品）。
- **类型系统**：`DmDbType`（31 值公开枚举）↔ `DmSqlType` 协议类型码（CHAR=0…DATETIME2_TZ=27, ROWID=28, ARRAY=117, CLASS=119, CURSOR=120…）↔ .NET 类型三层巨型 switch；DECIMAL 是 Oracle 风格 base-100 BCD；DATETIME 是位压缩线格式；几何走 PostGIS 风格 GSER↔WKB/EWKB 纯客户端转换 → `03`。
- **批量装载**：三条路径 —— `DmBulkCopy`（数组绑定 INSERT，非 FLDR）、`DmBulkCopy2`+`FldrStatement`（真 FLDR 列式协议，cmd=55/61/111…）、`DmFldrExport`（P/Invoke 原生 dmfldr，仅导出方向）→ `04`。
- **高可用**：EPGroup 多节点故障切换（switchTimes/switchInterval/epSelector）、ReconnectFilter（仅 6001/6027/SocketException 触发，重连无论成败都抛异常）、RWFilter2 读写分离（主库查 `v$arch_status` 选备库、RWCounter 配额递减负载均衡、语句镜像回放）、DBAliveCheckThread 裸 TCP 探活 → `05`。

## 4. 已确认缺陷清单（按补强优先级）

P0 —— 正确性 bug（有明确证据，修复即可交付价值）：

| # | 缺陷 | 位置 |
|---|---|---|
| 1 | `DmGetValue.GetByte`/`GetSByte` 对 DOUBLE 列(case 11)误调 `GetDate` | `Dm/DmGetValue.cs:349, :962` |
| 2 | `DmBLobStream` 三个 setter 自赋值失效（Position/ReadTimeout/WriteTimeout），Seek 无法定位到末尾 | `Dm/DmBLobStream.cs:46,57,69,152` |
| 3 | `FldrStatement.setMaxRows` 设置成功后仍无条件抛异常 | `Dm/FldrStatement.cs:1078-1085` |
| 4 | `InitialCatalog` setter 把值同时塞进 user 和 password（SqlClient 移植残留） | `Dm/DmConnectionStringBuilder.cs:1068-1082` |
| 5 | `CheckCommandBehavior` 位掩码用 `==` 比较，组合标志全失效 | `Dm/DmCommand.cs:797-803`、`Dm/DmDataReader.cs:233` 等 |
| 6 | dm_svc.conf 解析器内层循环用错外层 section 名长度截 key，异常被静默吞 | `Dm.Config/DmSvcConfig.cs:173-267` |
| 7 | `ByteArrayBuffer.setUB3` 跨节点放不下时静默丢数据；`setLong` 边界判断 `>=1` 应为 `>=8` | `Dm.net.buffer/ByteArrayBuffer.cs:765-805` |
| 8 | `setFldrProperties` 的 indexOption 恒为 1（三元两边相同的笔误） | `Dm/FldrStatement.cs:1907` |

P1 —— 线程安全/资源：

| # | 缺陷 | 位置 |
|---|---|---|
| 9 | `MSG<T>` 新式收发路径（COMMIT/LOB/FLDR）**不走连接锁**，与普通 SQL 并发时交织写 socket | `A/B.cs:161`（对照 :249-282 旧路径有锁） |
| 10 | 过滤器单例共享 `next` 指针，跨连接串建链互相污染、无锁竞态 | `Dm.filter/BaseFilter.cs:18-64` |
| 11 | `ConnPoolFilter` 单例 setter 全局覆盖池配置；`ConnPoolCache._connPoolMap` 裸 Dictionary 与清理线程竞态 | `Dm.filter/ConnPoolFilter.cs:35-65` |
| 12 | TCP 连接建立先 `BeginConnect+WaitOne` 成功后又 Close 重连一次（双倍耗时+不可控阻塞） | `A/D.cs:304-335` |
| 13 | `ThirdPartCipherDLL.Dispose` 空实现（句柄永不 FreeLibrary）；`DmFldrExport` 失败路径双重 free / 成功路径泄漏 | `Dm/ThirdPartCipherDLL.cs:102-104` 等 |
| 14 | `DmCommand` 终结器不归还语句池句柄；`convertBytes2BaseData` 借语句无 try/finally | `Dm/DmCommand.cs:737-749`、`Dm/ComplexTypeData.cs:970-972` |

P1 —— 安全：

| # | 缺陷 | 位置 |
|---|---|---|
| 15 | **SSL 证书校验回调恒 `return true`**，targetHost 硬编码 `"DmProvider"`，仅 TLS1.1/1.2 | `A/D.cs:358,372-375` |
| 16 | 512 位固定 DH 参数 + 固定 IV + 明文 MD5 摘要（非 HMAC） | `Dm/MsgSecurity.cs:45-86` |
| 17 | 收包按对端声明 bodyLen 无上限扩容（可达 512MB 分配） | `A/B.cs:1111` |
| 18 | FLDR 错误行整行明文落日志；多处 SQL 字符串拼接（schema/savepoint/系统表查询） | `Dm/FLDR_INSERT.cs:1615` 等 |

P2 —— 能力缺口（增强方向）：

| # | 缺口 | 说明 |
|---|---|---|
| 19 | **全驱动无任何 `*Async` 覆写** | 同步 IO 占满线程池，补强价值最高的点 |
| 20 | 分布式事务 `Promote()` 直接 `NotSupportedException`；事务注册表以 `Transaction.GetHashCode()` 为键（碰撞错配） | `Dm/DmPromotableTransaction.cs:60-63` |
| 21 | 池 checkout 无条件 `select 1` 验活，`conn_pool_check` 哑配置 | `Dm.filter/ConnPoolFilter.cs:88-115` |
| 22 | RW 只读判定只看 SQL 首词（MERGE/CALL/CTE 写全误判到备库） | `Dm.filter.rw/RWUtil2.cs:272-281` |
| 23 | `Cancel()` 语义 = 暴力断线重连 | `Dm/DmCommand.cs:455-461` |
| 24 | LOB 上层 API 全量物化内存，SequentialAccess 名不副实 | `Dm/DmDataReader.cs:326` |
| 25 | 消息压缩协商了但全驱动无压缩实现（半成品） | `A/B.cs:103-106` |
| 26 | 事务 Dispose 在 Oracle 兼容模式下自动 Commit（极易踩坑） | `Dm/DmTransaction.cs:192-202` |
| 27 | `do_GetOrdinal` 无名字→序号字典，宽表高频按名取列是 O(6n) 线性扫描 | `Dm/DmDataReader.cs:625-688` |

各分报告 §4 还有更多条目（类型系统双套转换器不一致、复杂类型嵌套 CLOB 读不出、RW 备库事务镜像半成品等）。

## 5. 补强工程的落地路线建议

1. **非侵入增强**（监控/埋点/限流/自定义路由）：继承 `BaseFilter` 注入过滤器链；注意每连接独立 new 实例以避开单例 next 污染，注入时机在最后设置 ConnectionString/DbConnection 之后。
2. **官方同款 APM 挂钩**：派生 `DmCommand` 覆写 `internal virtual do_Execute*`，程序集命名 `DmSkyWalkingAgent` 或借用 `InternalsVisibleTo` 模式，经 `DmConnection.CreateDmTracingCommand` 反射加载（`Dm/DmConnection.cs:915-926`）。
3. **Bug 修复**：上面 P0 清单均为单行/小范围修复。落地方式二选一：(a) Harmony 运行时 patch（目标方法都是 `internal virtual`/static，签名稳定，前缀 patch 友好）；(b) fork `decompiled/net8.0/` 重编译为自有程序集（项目文件已生成，需补引用与资源）。
4. **大改方向**：真 async（重写 `B`/`D` 收发层为 SocketAsyncEventArgs/Pipelines）、去 dmcyt 依赖（复活托管 `SymmCipher` 死代码 + 修固定 IV）、真流式 LOB、分布式事务 Promote 实现。

## 6. 复现与下一步

复现反编译：

```bash
curl -LO https://nuget.azure.cn/v3-flatcontainer/dm.dmprovider/8.3.1.47463/dm.dmprovider.8.3.1.47463.nupkg
unzip -d packages/extracted dm.dmprovider.8.3.1.47463.nupkg
ilspycmd -p -o decompiled/net8.0 packages/extracted/lib/net8.0/DM.DmProvider.dll
```

下一步候选（按 dotnet-reverse skill 的出口菜单）：

1. 从 P0 清单挑项，用 dnSpyEx/Harmony 做 IL patch 验证（第 6 阶段）；
2. 搭一个最小 .NET 控制台工程引用 nupkg，连真实 DM 库做动态调试基线（第 5 阶段），确认 `select 1` 验活、心跳、重连等运行时行为；
3. 实现 `*Async` 补强原型（价值最高、工作量最大）；
4. 把 decompiled 工程整理为可编译的 fork（补 csproj 引用、资源文件、签名）；
5. 对比 net40 与 net8.0 build 的 IL 差异，确认缺陷是否全 TFM 共有；
6. 导出本报告的英文版或拆分 issue 清单。
