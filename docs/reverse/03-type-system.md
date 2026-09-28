# 达梦 DM.DmProvider 8.3.1.47463 逆向分析报告：类型系统与数据编解码子系统

## 1. 核心类清单

| 类 | 文件 | 职责 |
|---|---|---|
| `DmDbType` | Dm/DmDbType.cs | 公开枚举（31 个值），用户侧指定的参数/列类型 |
| `DmSqlType` | Dm/DmSqlType.cs | **核心**：DM 内部协议类型码常量 + 全部类型映射函数（CType↔DmDbType↔DbType↔System.Type） |
| `Types` | Dm/Types.cs | JDBC 风格类型码常量（如 `INTEGER=4`），基本是历史遗留死代码 |
| `DmField` / `DmColumn` | Dm/DmField.cs, Dm/DmColumn.cs | 列元数据（type/prec/scale/mask/typeDescriptor），DmColumn 仅加 `getMaxTupleLen` |
| `DmDataTypeMetaData` | Dm/DmDataTypeMetaData.cs | 为 SchemaTable 提供 ClassName/TypeName 字符串 |
| `DmGetValue` | Dm/DmGetValue.cs | 行数据 `byte[]` → .NET 对象的全部转换逻辑（每列一组 switch） |
| `DmSetValue` | Dm/DmSetValue.cs | .NET 对象 → 参数 `byte[]` 的全部转换逻辑，`SetObject` 为总入口（约 30 个 `is` 分支链） |
| `DmConvertion` | Dm/DmConvertion.cs | 基础类型小端字节序读写（BitConverter / 手工移位） |
| `DmdbNumeric` | Dm/DmdbNumeric.cs | Oracle 风格 base-100 BCD 的 DECIMAL 编解码（最大 40 位、21 字节） |
| `DmXDec` / `Dmxdec` | Dm/Dmxdec.cs | 高精度十进制（XDEC）字符串↔字节转换；`Dmxdec` 内含 `DllImport("dmcalc.dll")` |
| `DmDateTime` | Dm/DmDateTime.cs | DATE/TIME/DATETIME/TZ 系列的**位压缩**线格式编解码 |
| `DmIntervalDT` / `DmIntervalYM` | Dm/DmIntervalDT.cs, Dm/DmIntervalYM.cs | INTERVAL DAY TO SECOND（24 字节）/ YEAR TO MONTH（12 字节）编解码 |
| `AbstractLob` / `DmBlob` / `DmClob` | Dm/AbstractLob.cs 等 | LOB 定位符解析、读写、截断 |
| `DmBLobStream` | Dm/DmBLobStream.cs | BLOB 的 Stream 包装（**多个 setter 是坏的**，见 §4） |
| `GET_LOB_LEN` / `SET_LOB_DATA` / `LOB_TRUNCATE` / `GET_LOB_DATA` | Dm/*.cs | LOB 网络消息，opcode 29/30/31/32 |
| `ComplexTypeData` / `ComplexTypeDesc` / `DmArray` / `DmStruct` | Dm/*.cs | 数组/对象/记录/嵌套表的二进制编解码；desc 通过 `SF_DESCRIBE_TYPE` 从服务器取 |
| `DB2N` / `N2DB` | Dm.util/DB2N.cs, Dm.util/N2DB.cs | 第二套 DB→.NET / .NET→DB 转换器（供复杂类型成员及另一调用路径使用） |
| `DmGeo2Util` / `NtsBinaryParser` / `ByteReader` / `ByteWriter` | Dm.util.geoUtil/ | PostGIS 风格 GSER ↔ WKB/EWKB 几何转换（纯客户端工具，1642 行解析器） |

## 2. 重点问题回答

### 2.1 DmDbType 枚举与 DM 内部类型码 → .NET 类型映射

`DmDbType`（Dm/DmDbType.cs:3-35）共 31 个成员：Blob, Binary, Bit, Byte, Char, Clob, Date, DateTime, Decimal, **XDEC**, Double, Float, Int16/32/64, IntervalDayToSecond, IntervalYearToMonth, SByte, Text, Time, UInt16/32/64, VarBinary, VarChar, Cursor, RefCursor, DateTimeOffset, TimeOffset, **ARRAY, Class**。

**真正的协议类型码在 `DmSqlType`**（Dm/DmSqlType.cs:16-122），与 JDBC 码完全不同：

```
CHAR=0  VARCHAR2=1  VARCHAR=2  BIT=3  TINYINT=5  SMALLINT=6  INT=7  BIGINT=8
DECIMAL=9  REAL=10  DOUBLE=11  BLOB=12  BOOLEAN=13  DATE=14  TIME=15  DATETIME=16
BINARY=17  VARBINARY=18  CLOB=19  INTERVAL_YM=20  INTERVAL_DT=21
TIME_TZ=22  DATETIME_TZ=23  DEC_INT64=24  NULL=25  DATETIME2=26  DATETIME2_TZ=27
ROWID=28  ANY=31  RECORD=40  TYPE=41  TYPE_REF=42  UNKNOWN=54
ARRAY=117  CLASS=119  CURSOR=120  PLTYPE_RECORD=121  SARRAY=122  CURSOR_ORACLE=-10
```

三层映射函数（全部 static、巨型 switch，无表驱动）：
- `DmDbTypeToDmSqlType`（DmSqlType.cs:610）— 注意 UInt16→SMALLINT、UInt32→INT、UInt64→BIGINT 的**有损降级**；
- `DmSqlTypeToDbType`（:680）、`DbTypeToDmSqlType`（:748，DbType.Guid→VarChar）；
- `CTypeToSystemType`（:411/:478 两个重载）— 关键行为：CHAR/VARCHAR2 **prec==36 时直接映射为 Guid**；VARCHAR 需连接属性 `Varchar36ToGuid`；TIME 可由 `DbTimeToTimeSpan` 映射为 TimeSpan；INTERVAL_DT 在 `IntervalMode=DT/ALL` 或 scale==1574 时映射为 TimeSpan，INTERVAL_YM 在 YM/ALL 模式映射为 long（总月数）；DATETIME_TZ 默认仍映射 DateTime（`ConvertToTz` 才给 DateTimeOffset）。
- `GetFieldType` 实际调用点在 DmDataReader.cs:520。

`DmDataTypeMetaData`（GetClassName/GetTypeName）只做字符串名映射，且 case 17/18 返回 `"System.Bytebyte[]"`（**笔误**，DmDataTypeMetaData.cs:35）。

### 2.2 GetValue / SetValue 双向转换机制

**读取链**：`DmDataReader.do_GetValue`（DmDataReader.cs:744）→ `m_RsCache.GetBytes` 取列原始字节 → 复杂类型走 `DB2N.toComplexType`（受 `ComplexTypeToBytes` 开关控制可只返回 BLOB 原始字节，DmDataReader.cs:778）→ 否则 `DmGetValue.GetObject(i, val, CType, prec, scale)`（DmGetValue.cs:1235）。强类型访问器（GetInt32 等）各自调 `DmGetValue` 里对应方法，内部再按 CType 做 switch 转换 + `CheckRangeXxx` 溢出检查（溢出抛 `ECNET_DATA_CONVERTION_ERROR`）。数值↔字符串互转统一 `double.Parse(..., invariantCulture)`。

**写入链**：`DmCommand` 持有 `m_SetValue`（DmCommand.cs:926）→ `DmSetValue.SetObject`（DmSetValue.cs:1555）按 CLR 类型长链分派到 SetInt/SetString/…。核心开关是 `typeFlag`：**typeFlag==1 表示"按服务器描述的参数类型绑定"**，会执行向目标 cType 的转换（含精度检查 `ECNET_OVER_FLOW`）；否则按客户端自然类型直接编码并 `SetSqlType/SetPrec/SetScale`。`DmField.recommendType/useClientBind`（DmField.cs:425-466）决定何时允许客户端类型覆盖服务器类型。

值得注意的转换特例：
- `SetString` 无类型绑定时：字节数 <32767 → VARCHAR(8188)，否则→CLOB(int.MaxValue)（DmSetValue.cs:815-826）；
- Guid 写入：cType 为字符型且 prec 为 36/8188 → 写字符串；BINARY/VARBINARY 且 prec==16 → 写字节（DmSetValue.cs:1716-1733）；
- Enum 按**名字序号**转成 int 写入（DmSetValue.cs:1735-1750），不是底层数值——`[Flags]`/自定义数值的枚举会写错；
- DATETIME2(26)/DATETIME2_TZ(27) 为 9/11 字节纳秒格式（DmDateTime.DmdtEncodeFast2，DmDateTime.cs:1386）。

### 2.3 LOB 读写流式机制

**定位符解析**：`AbstractLob` 构造函数（AbstractLob.cs:63-114）顺序解析：storageType(1B) + lobId(8B) + bytesLength(4B)，随后可选 groupId/fileId/pageNo（行外存储）、tabId/colId、rowId（msgVersion<9 为 8 字节，≥9 为 12 字节），storageType==4(LONG_ROW) 时尾部还有 8 字节 length。头部长度 `getHeadSize()`：旧协议 13（行内）/21（行外），NewLobFlag 下 43/47（AbstractLob.cs:131-146）。

**三种存储**：`STORAGE_IN_ROW=1`（数据直接随行返回，构造时即拷出）、`STORAGE_OUT_ROW=2`（惰性，按需发消息）、`STORAGE_LONG_ROW=4`。

**网络往返**（每条 LOB 操作一次消息，opcode 见 §3）：
- 读：`DmBlob.do_getBytes`（DmBlob.cs:48）→ `B.A(DmBlob,pos,len)`（A/B.cs:499）按 `MaxLobDataLenPerMsg`（连接属性，默认见 DmConnectionStringBuilder.cs:936）**分片循环**发 GET_LOB_DATA，直到 `readOver`。
- 写：`B.A(AbstractLob,pos,bytes,...)`（A/B.cs:612）分片发 SET_LOB_DATA，首/尾片用 firstOrLast 标志位（1|2）；CLOB 写入时用 `DriverUtil.checkCompleteCharLen` 保证不在多字节字符中间切断（A/B.cs:593）。
- 长度/截断：GET_LOB_LEN / LOB_TRUNCATE，结果会回写 `lob.id/storageType/m_length`（SET_LOB_DATA.cs:66-87 解码时若 groupId==-1 会把 storageType 改回 1）。

**但实际上层并不流式**：`DmDataReader.do_GetBytes`（DmDataReader.cs:326）先 `m_GetVal.GetBytes` 把**整个 LOB 物化进内存**再拷贝到用户 buffer（DmGetValue.cs:1371-1395）；`GetValue`/`GetString` 对 CLOB 也是 `getSubString(0, int.MaxValue)` 全量拉取。唯一真正分块的是 `do_GetChars` 的 CLOB 路径（DmDataReader.cs:401-415）和用户显式用 `DmBlob.GetStream()`。`DmBLobStream` 基于 `DmBlob.GetBytes/SetBytes` 按当前位置读写。

### 2.4 复杂类型（数组、结构、几何）编码

`ComplexTypeDesc` 通过匿名块 `BEGIN :p1 = SF_DESCRIBE_TYPE(:p2); END;` 向服务器取回类型描述并 Unpack（ComplexTypeDesc.cs:70）。

**元素编码**（`TypeDataToBytes`，ComplexTypeData.cs:166-267）：统一前缀 `[0][flag]`，null 为 `[0,0]`；基础类型后跟 `short len + data`；嵌套复杂类型/LOB 直接递归。LOB 成员转成 `[int len][bytes]`（convertLobToBytes:269）。

**容器布局**：
- ARRAY(117)：`[totalLen][itemCount][0][objCount][strCount][objStrOffs...][items...]`（arrayToBytes:434，20 字节头）；
- SARRAY(122)：`[totalLen][length][items...]`（8 字节头）；
- RECORD(121)：`[totalLen][fields...]`；CLASS(119)：`[0][len][fields...]`；
- 集合（m_objId==4，CLTN）：`[0][len][cltnType:short][elemDType:short][count][items...]`；索引表（cltnType=3）按 key+value 对编码，key 为 VARCHAR 时带 4 字节长度，否则按 int（KeyToBytes:391）。
- 整体外封：`[magic 78111999][chkLen][classDescChkInfo][payload]`（toBytes:1011-1035；magic 常量在 ComplexTypeDesc.cs:9 `OBJ_BLOB_MAGIC`，解码校验在 objBlobToBytes:897）。

**读取**：`bytesToArray/bytesToSArray/bytesToObj/bytesToRecord` 递归下降，`checkObjExist`+`findObjByPackId` 处理对象引用（共享对象用 packid 引用表）。基础成员解码走 `convertBytes2BaseData`（ComplexTypeData.cs:946）——它**临时从语句池借一个 Statement 构造 DmGetValue** 再 `p()` 归还。

**几何**：`DmGeo2Util` 是纯客户端 GSER↔WKB/EWKB 转换器（对标 PostGIS JDBC 的 BinaryParser/BinaryWriter），支持 1-17 的 WKB 类型码、EWKB 的 Z/M/SRID flag（NtsBinaryParser.cs:76-80：Z=0x80000000, M=0x40000000, SRID=0x20000000），GEOGRAPHY 默认 SRID=4326。几何列在驱动内仍按 BLOB/VARBINARY 传输，转换由用户显式调用。

## 3. 关键常量/枚举值表

**LOB 消息 opcode**（MSG 子类构造传入）：GET_LOB_LEN=29（GET_LOB_LEN.cs:11）、SET_LOB_DATA=30（SET_LOB_DATA.cs:20）、LOB_TRUNCATE=31（LOB_TRUNCATE.cs:12）、GET_LOB_DATA=32（GET_LOB_DATA.cs:18）。

**协议版本**（MSG.cs:8-44）：VERSION=21；关键门槛 `msgVersion>=9`（VERSION_ROWID_B12，rowId 8→12 字节、LOB 头 43→47）。消息上限：MSG_DEFAULT_LEN=32640、MSG_PARAM_MAX_LEN=64MB、MSG_LIMIT_LEN=512MB。

**列 mask / scale 位域**（DmField.cs:31-38、DmSqlType.cs:180-192）：
- mask：1=ORACLE_DATE，2=ORACLE_FLOAT，3=BFILE，4=LOCAL_DATETIME；
- scale：0x1000=本地时区 DATETIME 标记，0x2000=Oracle DATE 标记，129(0x81)=FLOAT 标记；
- INTERVAL 的 scale 打包：`(type<<8)|(leadScale<<4)|secScale`，其中 1574=0x626 表示 DAY(2) TO SECOND(6)（TimeSpan 兼容模式的判定值，DmGetValue.cs:1216）。

**长度/精度**：MAX_STRING_LEN=8188；VARCHAR/VARBINARY_PREC=32767；BLOB/CLOB_PREC=int.MaxValue；INTERVAL_YM 线长 12B、INTERVAL_DT 24B；DECIMAL 线格式 ≤21B/40 位（DmdbNumeric XDEC_MAX_PREC=40、XDEC_SIZE=21）；DEC_INT64 是 8 字节整数+scale 的紧凑十进制。

**LOB 头**：NBLOB_HEAD_SIZE_INROW=13 / OUTROW=21 / EX=43 / EX_ROWID_12B=47；lobFlag：0=BINARY，1=CHAR。

**复杂类型**：OBJ_BLOB_MAGIC=78111999；CLTN_TYPE：1=VARRAY，2=NST_TABLE，3=IND_TABLE；element null 标记 65534/65533（ComplexTypeData.cs:952）。

**版本遗留**：DmSqlType.cs:10-14 `VERSION="8.3.1.3"`、`BUILD_TIME="2011.01.10"`、`DATABASE_PRODUCT_VERSION="7.0.0.0"` —— 明显是多年未更新的硬编码。

## 4. 可扩展 / 可 patch 点（核心目的）

**明确的 Bug（修补优先级高）**
1. `DmGetValue.GetByte` 与 `GetSByte` 的 `case 11`（DOUBLE 列）误调 `GetDate` 而非 `GetDouble`（DmGetValue.cs:349 和 :962）——DOUBLE 列取 byte/sbyte 必抛异常或产生垃圾。
2. `DmBLobStream` 三个 setter 全部自赋值失效：`Position { set { m_CurPos = Position; } }`（DmBLobStream.cs:46）、`ReadTimeout`（:57 `ReadTimeout = m_ReadTimeout`，且是**递归属性自赋值**，运行时是 no-op 而非栈溢出，仅因 backing field 恰好同值）、`WriteTimeout`（:69）。`Read` 在关闭后返回 -1 而非抛 `ObjectDisposedException`（:105）；`Seek` 越界静默钳制（:152），End 定位到 `Length-1`，无法 seek 到末尾追加。
3. `DmDataReader.do_GetGuid` 对 NULL/非法值返回 `new Guid("")`（DmDataReader.cs:546,:563）——直接抛 FormatException，而非 DBNull 语义。
4. `DmDateTime.GetMicroSecond/GetNanoSecond`（DmDateTime.cs:597-623）对 `TimeOfDay.TotalSeconds.ToString()` 做 `IndexOf('.')` 字符串解析——**区域性 bug**：小数点为 "," 的区域（de-DE、fr-FR 等）下微秒/纳秒静默归 0。同理 `DmIntervalDT.SetNano`（DmIntervalDT.cs:513 `Convert.ToDouble("0."+nano)`）。`GetTimestamp()/GetDate()` 走 `Convert.ToDateTime(string)`（:921-931）同样区域敏感且慢。
5. CLOB 分片读取在 `B.A(DmClob,...)`（A/B.cs:527-556）里**每片独立 `getString` 解码**——多字节字符跨片时被截断成乱码（写路径用 checkCompleteCharLen 防了，读路径没有）。
6. `DmGetValue.GetString` case 12（BLOB）：当 `do_length()<int.MaxValue` 时返回的是**含 13/43 字节定位符头的原始 val 的 hex**（DmGetValue.cs:676-683），与 GetBytes 的剥头行为不一致——对行内小 BLOB 取字符串会带上头部垃圾。

**薄弱设计 / 扩展点**
7. **映射层全是静态巨型 switch**（DmSqlType / DmGetValue / DmSetValue / DB2N / N2DB 两套并存），无任何虚方法或接口注入点；补强新类型（如 XML、JSON、向量、DMGEO 原生类型码）只能 patch 这些方法或在 `DmDataReader.do_GetValue`（DmDataReader.cs:744）/`DmSetValue.SetObject`（DmSetValue.cs:1555）两个总入口做挂钩。好在 `DmDataReader` 已有 `BaseFilter` 过滤链（filterHead，DmDataReader.cs:71+）可拦截所有公开 Get*/Read——**这是官方预留的最佳非侵入扩展点**。
8. **双套转换器并存**：`DmGetValue`（DataReader 用）与 `DB2N`（复杂类型成员/另一路径用，DB2N.cs）逻辑重叠但行为不一致（如 DB2N.toBoolean 对字符串走 `Convert.ToBoolean`），修补时必须同步两处。
9. `ComplexTypeData.bytesToClob` 直接 `throw new NotSupportedException`（ComplexTypeData.cs:566）——CLASS/RECORD 内嵌 CLOB 成员**读不出来**（Blob 可以）。`DmGetValue.ToComplexType` 对 RECORD(121) 也是先解析再 throw（:1451-1453），半成品代码。
10. `ComplexTypeData.bytesToObj` 把**未初始化的 null** 先 `addObjToRefArr`（:794），解析完后从不回填——共享/循环引用的 packid 机制实际失效，`findObjByPackId` 可能返回 null。
11. `convertBytes2BaseData`（ComplexTypeData.cs:970-972）从语句池借 Statement 构造 DmGetValue，`GetObject` 一旦抛异常**语句永不归还池**（无 try/finally）——池泄漏。
12. **LOB 全量物化**：`do_GetBytes`/`GetValue`/`GetString` 对大 LOB 一次性拉进内存（§2.3）。要支持真正的 `CommandBehavior.SequentialAccess` 流式读取，patch 点在 DmGetValue.GetBytes case 12/19 与 DmDataReader.do_GetBytes——现有 `MaxLobDataLenPerMsg` 分片原语已具备。
13. `DmSetValue.SetObject` 把 `byte`/`sbyte`/`ushort` 都路由到 `SetInt`（:1577,:1582,:1613），`SetUByte/SetUShort/SetUInt` 三个方法成为死代码（仅 SetULong 被调用）——绑定 TINYINT 时类型码会被抬成 INT。
14. `DmdbNumeric.ROUND_HALF_SWITCH` 是 public static 可变字段（DmdbNumeric.cs:17）——全局开关，多连接并发互相影响；`DmIntervalYM.encode`/`DmIntervalDT.encode` 会**就地修改对象字段**（convertTo 回写，DmIntervalDT.cs:699-707），同一参数对象二次绑定结果可能不同。
15. `AbstractLob` 所有字段 public mutable，`GET_LOB_DATA.doDecode` 在读取途中改 `curFileId/curPageNo/totalOffset`（GET_LOB_DATA.cs:60-78）——LOB 对象不可跨线程共享；`DmDataReader.m_Clobs` 是非泛型 `ArrayList`。
16. `DmDataReader.GetFieldValue<T>` 仅 `Convert.ChangeType`（DmDataReader.cs:1141-1157），无 `GetFieldValueAsync`、无 provider-specific 类型（`GetProviderSpecificValue` 直接调 base，等于没实现）——补强 interval/rowid/geometry 的强类型读取的挂点。
17. `DmSetValue.fixDecString`/`decStringToBcd`（:1785-1871）是无调用方的死代码；`DmSqlTypeInfo` 构造函数丢弃全部参数（DmSqlTypeInfo.cs:10-13，桩）；`Types.cs` 整套 JDBC 常量无引用。

## 5. 有趣实现细节

- **原生 DLL 依赖**：`Dmxdec`（Dmxdec.cs:38）`[DllImport("dmcalc.dll")] xdec_from_char`——net8.0 号称托管的程序集里藏着 Windows 命名的 P/Invoke（虽未见调用方，属移植残留）。
- **格里高利历切换硬编码**：`DmDateTime.RevertToGlgl`（DmDateTime.cs:1544-1551）把 1582-10-05~14 强制改成 15 日。
- **日期线格式是位压缩**而非字节对齐：年 15bit+月 4bit+日 5bit（3B DATE），时 5/分 6/秒 6/微秒 20bit（5B TIME），8B DATETIME / 9B DATETIME2（纳秒）/ +2B 时区（分钟偏移，±780 内，1000 为特殊值 DmDateTime.cs:373）；年份 >9999 时用符号位编码负年（MIN_YEAR=-4712）。
- **DECIMAL 是 Oracle 风格 base-100 BCD**：首字节 0x80=零、正数 exponent+193、负数 62-exp，数字字节正数 +1 / 负数 101-n，负数结尾补 102（DmdbNumeric.decode/encode，:160-285）。
- `GetBoolean` 对字符串列只看**第一个字符是否 '0'**（DmGetValue.cs:781）；对 DECIMAL 列用 `(byte)GetBigDecimal(...)==0`（:769）——值 >255 时直接 OverflowException。
- 行内 BLOB 数据前缀有"真实数据"标志位：`IsRealData`（DmGetValue.cs:1412-1429）检测 val[0] 的 bit0（行内数据）/bit1（定位符）。
- 复杂类型里把字符串**伪装成 byte 数组元素**：`makeupObjToArr`（ComplexTypeData.cs:109-146）允许 string 填入 BINARY/VARBINARY/BIT 数组。
- `DmColumn.getMaxTupleLen`（DmColumn.cs:10-28）：只要含 VARCHAR2(1)/CLOB(19)/复杂类型就按整页 maxRowSize 估算行宽——影响结果集缓冲策略。
- `GetKeyCols`/`GetUniqueCols`（DmDataReader.cs:1353-1391）为 GetSchemaTable 的 IsKey/IsUnique 实时向 `SYS.SYSINDEXES` 等系统表发硬编码 SQL——Schema 查询的隐藏性能点，也是 patch 元数据行为的入口。
- 连接属性中与本层相关的键：`Varchar36ToGuid`、`DbTimeToTimeSpan`、`IntervalMode`(YM/DT/ALL)、`ConvertToTz`、`LobMode`(==2 即 fetchAll)、`MaxLobDataLenPerMsg`、`formatNumericChars`（自定义小数点字符，Get/Set 两侧 ReplaceNumPoint）、`ColumnNameCase`、`ComplexTypeToBytes`、`EFCoreNextResult`（靠 SQL 里的 `/*EFCOREROWCOUNT*/` 注释魔法识别 EF Core 场景，DmDataReader.cs:839）。

**给后续补强工作的建议挂点优先级**：① `DmDataReader` 的 `BaseFilter` 过滤链（零侵入）；② `DmGetValue.GetObject` / `DmSetValue.SetObject` 两个总入口（新类型码/dmgeo/向量类型）；③ LOB 的 `B.A(...)` 分片原语 + `DmDataReader.do_GetBytes`（真流式）；④ 修 §4 的 1/2/4/5 四个确凿 bug。
