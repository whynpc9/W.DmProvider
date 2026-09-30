# T09 — 参数和类型精度

状态：实施中；T08 已独立验收并保存快照。以 S06/TYP-01–09 为验收范围，测试数据不能代替实测。

## 类型来源与绑定

把显式 DmSqlType、显式 DbType、服务端 describe 和 CLR 推断分开记录；Value 赋值不能设置显式来源。ResetDbType 清除两种显式来源并重新推断。两个显式属性不兼容时在执行前拒绝，不能依赖最后一次赋值覆盖来隐藏冲突。Prepare 的类型/Size/Precision/Scale 变化使旧 metadata 失效；仅换值不沿用过期缓存。

命名参数归一只移除一个 `:` 或 `@`，使用 OrdinalIgnoreCase，重名拒绝。使用 tokenizer 识别参数，字符串/注释不参与；重复命名占位符复用同一个冻结值，位置参数按顺序，混合两种方式拒绝。删除编码失败转逐元素执行的旧路径，不自动重放 DML。

绑定可使用准备阶段已验证的 describe 来确定 NULL 类型；没有显式类型或可靠 describe 时拒绝。NULL、空字符串、空二进制的服务端实际语义分开报告。不可表示值必须在发送执行报文前失败；准备/describe 本身不等于已执行业务。

## 精确值

保留已验证 wire codec，把输入验证和 CLR 转换收敛到内部类型模块。enum 使用真实底层整数，unsigned 选择足够宽的编码，不通过名字索引或 Int64 截断 ulong。整数 getter 对非整数拒绝；浮点特殊值没有真实支持证据时早拒绝。

新增小型不可变公开 `W.Dm.DmDecimal`，以 BigInteger coefficient 和非负 scale 表达 DECIMAL；与旧 `W.Dm.util.DmDecimal` 分开，后者不作为高精度实现。Invariant parse/format、精确 decimal 转换、precision/scale 验证均不能通过 double。普通 GetDecimal/GetValue 无法精确表达时抛 OverflowException；provider-specific getter 保留精确值。旧 codec 任何隐式舍入必须在其之前被验证或替换，不能把舍入成功当精确往返。

源码明确区分 CType 9 的 base100 DECIMAL 和 CType 24 的固定 8 字节整数加 scale；两者不能共用错误的报文布局。旧 TIME/TIMESTAMP 的微秒精度与扩展时间格式分别验证。支持扩展格式必须先取得目标服务器证据，不因源码存在编码函数就开放。读取服务器值超出 CLR tick 精度同样不能静默截断。

普通 CHAR/VARCHAR(36) 的字段类型和值始终为 string；显式 GetGuid 支持已声明字符或 16 字节约定，其他格式拒绝。严格字符编码不得以问号替换不可编码值或半代理项。

日期/时间逐种验证真实 wire 精度：无时区 DateTime 返回 Unspecified；DateTimeOffset 保留偏移；TimeSpan 的正负、多天及有效小数位准确；DateOnly/TimeOnly 不能悄悄丢 ticks。服务器不能表达的精度明确拒绝。保留下游既有 TimeSpan/DateTimeOffset GetString 解析契约；新增 typed getter 与同步/异步缓存 getter 的值和异常一致。

## 实施与验收

编码按参数模型、数值 codec、文本/时间及 Reader 接入划分唯一文件所有者。测试 lane 统一构建/实库窗口，Luna 提供 not_run 合成边界数据，Sol Low 在冻结后独立验收。所有实库用 TEST wrapper、唯一对象、失败 finally 清理及新连接最终状态检查。保留 O/冻结 W 表征与候选 W 改进证据，记录真实不支持的服务端范围，不把所有 .NET 类型范围都宣称可用。
