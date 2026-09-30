# T09 类型契约变化

验收范围和哈希见 [T09 报告](../implementation/reports/T09.md)。

- CLR enum 按真实底层值编码；unsigned 使用足够宽的 wire 类型，同时检查显式声明的逻辑范围，禁止 wrap 或按枚举名称序号编码。
- `W.Dm.DmDecimal` 提供精确 coefficient/scale。超 CLR decimal 范围的 DECIMAL 应用 provider-specific getter 读取；普通 getter 明确溢出。
- 显式类型不会被建议性 metadata 忽略。无类型 NULL 无可靠描述时需要设置 DbType/DmSqlType；ResetDbType 清除显式来源。
- 普通 36 字符列仍为 string。GetGuid 是显式转换；Guid 字符采用 D 格式，二进制采用 CLR 16 字节约定。
- 通用 DbType.Binary/byte[] 使用变量长类型；显式 provider Binary 保留固定长度语义和容量校验。禁止超长截断、隐式文本/二进制混写和空值变 NULL。
- DateOnly/TimeOnly 使用明确 DATE/TIME 编码；无时区 DateTime 返回 Unspecified。已验证时间精度为微秒，无法表达的尾数/范围拒绝。INTERVAL 保留正负号及原 EF 可解析的限定字面量。
- 泛型整数读取对小数或越界失败；typed NULL 不转成空字符串。同步/缓存异步 getter 保持一致，真异步网络尚属后续任务。
- 未开放的 native XDEC、复杂类型、命名/可更新 cursor 等旧入口明确拒绝；不把源代码存在视为能力声明。
