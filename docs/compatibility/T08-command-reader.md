# T08 Command 与 Reader 契约变化

T08 只开放已验证的 CommandType.Text 路径。当前实现的最终独立验收状态以 [progress.json](../implementation/progress.json) 为准；协议表征见 [T08-protocol-results.md](../implementation/T08-protocol-results.md)。

- 执行期间冻结 SQL、参数 metadata/值和超时；返回的 Reader 持有计划直到关闭。修改 Command、Parameter 或参数集合会立即失败。byte[]/char[] 输入复制；未明确支持的可变参数对象在执行前拒绝。Clone 不共享参数父关系。
- 空 rowset 保留。ExecuteReader 定位首个可读结果；NextResult 更新整套列信息、类型读取器和缓存。纯 DML Reader 没有伪造数据行。
- RecordsAffected 只使用已证明归属的 DML 计数。纯 SELECT 及无法获取完整 DML 计数的复合语句返回 -1；EF 显式 SQL%ROWCOUNT 结果单独返回。累计使用 checked Int64，公开 Int32 无法容纳时抛 OverflowException。
- Scalar 对第一可读结果的空集返回 null，SQL NULL 返回 DBNull。NonQuery 完成结果遍历后才返回；后续服务器错误不会作为成功忽略。
- 支持的行为标志逐位处理。SchemaOnly/KeyInfo 尚未开放，在发送前拒绝。SequentialAccess 的标志支持不等于已实现有界内存 LOB 流式，流式契约由 T17 验收。
- GetSchemaTable 只使用当前响应的本地 metadata；未知 IsKey/IsUnique 为 DBNull，不执行辅助目录 SQL。依赖 T05 查询填出键属性的代码需要调整。
- 正常关闭 Reader 使用有限、独立的 CleanupTimeout。提前关闭使用已验证的 statement-close 请求，不通过发送额外 MORE_RESULT 来恢复会话。无法确认同步状态的失败路径关闭捕获的物理会话；需显式重新 Open。不能把断开描述为服务端已确认回滚。
- Dispose Command 后，已有 Reader 可继续消费并关闭；新的 Command 调用被拒绝。没有活动 Reader 的 prepared statement 也使用有限清理期限。
- 参数编码错误不再触发旧的逐元素数组执行补救逻辑；未开放数组绑定、StoredProcedure、输出参数、RefCursor 或复杂类型。ArrayBindCount/ComplexTypeToBytes 非默认值明确拒绝。

相对官方及冻结 T07，RecordsAffected 和键 metadata 的变化是有意调整。实际 NuGet 消费测试验证 ef-savechanges-v1 的选定 SQL 契约；完整 EF 仓库的候选接入由 T12 单独验收。当前源码包含未提交工作，不能用 T03 的 Git HEAD 代表这些实现。
