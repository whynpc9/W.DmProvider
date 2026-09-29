# S06 — 参数绑定、类型转换与精度

状态：实施规范。优先级：P0/P1。依赖：S02、S05。任务：T09。

## 1. 现有实现复用

起点：`DmSqlType`、`DmGetValue`、`DmSetValue`、`DmdbNumeric`、`DmDateTime`、`DmIntervalDT/YM`。第一阶段保留经测试的 wire codec，把 CLR 转换/推断从巨型 switch 中逐类提取，而不是重新发明 BCD 或日期位编码。[R04]

内部按 codec 分类：Integer、Decimal、Floating、Text、Binary、Guid、DateTime、Interval、LobLocator；每种 codec 明确接受 CLR 类型、wire 类型、null、encode/decode、溢出和精度规则。复杂类型单独 unsupported，不通过 ToString 伪装成文本。

## 2. 参数模型

类型优先级：显式 `DmSqlType` → 显式 `DbType` → 准确的服务端 describe metadata（适用时）→ CLR 值推断。每一层记录设置来源，不能用枚举默认值判断是否显式设置。冲突的显式属性组合报参数错误；`ResetDbType` 清除显式类型来源并恢复推断。

null/DBNull：有显式类型时按该类型发送 NULL；无类型且服务器 prepare 可以准确描述时使用它；两者均无不能盲目发 VARCHAR NULL，给出类型未确定错误。nullable enum 与 DBNull 分开处理。

参数名只移除允许的单个前缀 `:`/`@`；集合匹配采用明确且稳定的规则（本产品默认 OrdinalIgnoreCase），归一后重名拒绝；SQL `:p` 在 tokenizer 中匹配，`?` 按位置。命名/位置混用首版拒绝；字符串和注释里的字符不参与绑定。重复出现同一命名参数必须复用同值或按 wire 需要重复编码，不能要求用户重复添加。

执行时冻结集合/metadata，禁止另一线程改 Value/Size。大 Stream 输入不复制全量内容，但限制其在一次执行期间由驱动独占读取。Prepare 之后值变化合法，类型/Size 变化需失效重准备，而不是沿用错误旧 metadata。

## 3. 精度规则与映射

| CLR/请求 | 新契约 |
|---|---|
| enum / Flags | Convert 到真实底层数值再编码，不用名字排序/索引 |
| ushort / uint | 自动推断到足以容纳范围的有符号宽类型；显式窄类型溢出拒绝 |
| ulong | 使用足以容纳 20 位整数的精确 decimal 编码路径，不降为 Int64 |
| decimal | 直接 coefficient+scale 编码，禁止 double 中转；超出所声明精度/scale 不默默截断 |
| float/double | 明确 finite/NaN/Infinity 支持矩阵；服务器不支持的特殊值早拒绝 |
| 字符串 | UTF-16 长度与编码后的字节长度分别管理；严格 encoder fallback，不替换为问号 |
| Guid | 默认明确字符或 16-byte 二进制存储约定；普通 CHAR/VARCHAR(36) 不自动变成 Guid |
| DateOnly/TimeOnly | 明确 DATE/TIME 对应；非可表示精度拒绝，不丢 subsecond |
| DateTime | 无时区类型返回 Kind=Unspecified；不默默转换本地时区 |
| DateTimeOffset | 保存原始偏移和可表示精度，GetFieldValue<DateTimeOffset> 保持；超 CLR 范围拒绝 |
| TimeSpan | INTERVAL DAY TO SECOND；负值、大天数和有效 scale 准确；不与 TIME 随意互换 |
| byte[] | Binary/VarBinary/Blob 区分；NULL、空数组不是同一值 |

输入 precision/scale 不显式设置时才推断。设为 `(38,20)` 不代表 CLR decimal 能表示所有该列可能值；读取不可表示值必须 OverflowException，不能先舍入再成功。

对需要完整高精度的调用方，增加经 API 审核的小型 `DmDecimal`（BigInteger coefficient + scale、Invariant parse/format）。`GetProviderSpecificValue`/对应 field type 可返回它；普通 `GetValue`/GetDecimal 对超 CLR 范围抛异常。支持 scope 先限制已验证的 DECIMAL wire，不把 XDEC 顺便宣布支持。

`GetByte/GetSByte` 的 DOUBLE 分支必须走数值转换而非日期。整数转换对非整数默认拒绝，不无声截断；具体允许转换矩阵写入 tests，不能到处 Convert.ChangeType 产生不一致舍入。[R04]

## 4. Guid 与文本兼容

普通 36 字符列 `GetFieldType=string`、`GetValue=string`，即使内容像 Guid 也不自动改类型。显式 GetGuid 可以按已声明字符格式或二进制格式解析；格式无效抛 FormatException。

此行为相对官方自动推断可能变化，加入 approved differences。EF Guid 映射需要实际 getter 与参数测试；不得靠恢复官方误推断获得兼容。

## 5. 与下游已有读取方式兼容

EF 的 TimeSpan 映射当前调用 GetString 再 Parse；DateTimeOffset 也有 GetString+Invariant Parse 路径。[R07][R12]

第一版保留这两种字符串读取契约，例：可解析的日期时间附 `+08:00`；INTERVAL 使用下游现有解析能理解的限定字面量形式。同时提供正确 typed getter。不能只改善 GetFieldValue 就破坏旧 GetString；从文本改为 typed getter 应是独立下游 PR。

NCLOB/CLOB、BLOB/VARBINARY、IntervalDayToSecond 必须保留参数类型标记入口；`parameter is DmParameter` 在新 EF 接入分支改为 W 类型，不能静默跳过。空 LOB 和 NULL LOB 同样测。

## 6. 验收

TYP-01：enum {-10, 7, 1000}、Flags、byte/ulong 底层 enum 往返正确。
TYP-02：signed/unsigned min/max、越界、非整数转整数；正确值成功，越界明确失败。
TYP-03：decimal(38,20)、极小值、负值、尾随零；不通过 double；超 CLR 范围有精确 provider-specific 或明确异常。
TYP-04："不是GUID但长度恰好36..." 的真实 36 字符测试保持 string；有效/无效显式 GetGuid 区分。
TYP-05：UTF-8/GB18030 等声明字符集中的中文、emoji、组合字符、半代理项；不静默丢字。
TYP-06：TimeSpan 正负、多天、边界 tick 与非可表示 scale；DateTimeOffset 正负偏移、闰日、7位小数。
TYP-07：NULL/空字符串/空 byte[]、typed null、未确定类型 null。
TYP-08：重复参数、注释/字符串占位符、Prepare 后换值和换类型。
TYP-09：同步 getter 与异步 getter 返回的类型、值、异常一致；EF LOB/INTERVAL/时间戳现有功能通过。

## 7. 不做

不增加 JSON 查询运算符（属于 EF/SQL 方言）；不增加完整 ARRAY/Class/Geometry/XDEC 支持；不承诺所有 .NET 日期时间范围都被 DM 服务端支持。
