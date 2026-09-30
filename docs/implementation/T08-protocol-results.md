# T08 结果序列的协议表征

状态：实施期间的观测记录，不能代替最终候选包验收。真实测试使用既有 TEST Schema；所有对象唯一命名并由新连接确认清理。

## 基线与观测范围

官方 O 是 DM.DmProvider 8.3.1.47463 的 net9.0 资产（SHA-256 `8f6e59680d0a076df53bea50d5a2bdbd288535cd85b2d7ca5064c02adc9c6e6b`）。冻结 W 是 T07 验收程序集（SHA-256 `ae42d4aa9df4af8bf0874dd28056b6b2704f34677a28d34f44d0df200a66a4e5`）。二者的调用行为与候选 W 的内部标量追踪分别记录；不向冻结二进制补入诊断代码。

本地记录：`.local/t08/official-characterization.json`、`oldw-characterization.json`、`oldw-characterization-wirecount.json`、`candidate-trace-initial.json`。内部 hook 只输出 request/response opcode、SQL 状态码、报文长度及解码后的类型/行数/列数标量，不输出 SQL 值、认证信息或报文内容。

## 已观察到的序列

| 场景 | 候选 W 真实响应 | 设计结论 |
| --- | --- | --- |
| 两个 SELECT | 初始请求 5 / 响应 187；44 / 187 返回第二 rowset；44 / 0、SQL code 111 结束 | 空间上独立结果；不能靠 SQL 分号拆分 |
| 空首 SELECT | 初始 SQL code 100，HasResultSet=true、RowCount=0；后续 44 返回下一 rowset | 空结果仍是第一结果，Scalar 返回 null，不跳过 |
| UPDATE 0/1 + 显式 SQL%ROWCOUNT | 初始 5 / 0、ret162 无 rowset；内部 13 / 187、ret162 rowset，回读值分别 0/1 | rowset 的 affected=1 是行数，不能作为 DML 影响数 |
| SELECT; UPDATE; SELECT | 初始和下一 44 都是 ret162 rowset，中间无 count-only 响应 | 混合命令 DML 总数未知，返回 -1 |
| 纯 UPDATE 0/1 | 准备请求 5 后，执行 13 返回 ret159、affected 0/1；0 行 SQL code 100 | 只使用执行阶段证实的 DML 计数 |
| 后续引用不存在的表 | 初始请求 5 即返回 SQL code -2106 并抛出 | 本 profile 中该错误不是延迟到 NextResult；仍须处理后续结果错误路径 |
| SELECT 后带 INSERT，首行后 Close | Close 只发送 opcode 4 并获得响应 0，没有 opcode 44；后续查询成功且 INSERT 已可见 | 整句 SQL 最初已发送，不能把 INSERT 归因于 Close；关闭采用已验证的 statement-close ACK |

终结只在 MORE_RESULT 请求 44 的合法响应上下文判定：响应 opcode 0、SQL code 111、retType 0、列数 0、无 rowset、RowCount -1。`HasResultSet=false`、retType 0 或任意正状态码单独均不能代表终点。负状态码仍按服务器错误传播。

## 与旧行为的差异

官方与冻结 W 在 UPDATE 0/1 + ROWCOUNT 的迭代终点均返回 RecordsAffected=1，纯 SELECT 两结果也返回 1，空首 SELECT 则返回 0。这些值随 rowset 行数变化，不是可靠的 DML 累计数。候选游标只累计可辨认的 DML 执行结果；未提供可归属计数的 compound 保持未知。EF 的显式 ROWCOUNT 结果集单独保留，不能由 aggregate 替代。

这里只记录该服务器/profile 的证据。原生批次、输出参数、RefCursor 及其他协议形态仍须自己的探测和能力门槛。
