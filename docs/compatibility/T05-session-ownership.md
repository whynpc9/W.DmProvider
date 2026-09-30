# T05 会话所有权契约

状态：T05 已通过独立验收；最终验证范围见 [T05 报告](../implementation/reports/T05.md)。本文件描述本次有意变更，不能作为完整 R1 发布证明。

## 调用顺序

- 同一连接只能有一个命令、Reader 或事务控制操作。竞争调用立即抛出 `InvalidOperationException`，不会等待或排队。
- `ExecuteReader` 返回的 Reader 持续占有连接，直到 `Close` / `Dispose`。`Read` 或 `NextResult` 返回 false 后仍需要关闭 Reader，再执行另一条命令。
- 同一 Reader/LOB 的公开读取也不能并行；内部 Fetch/LOB 请求沿用 Reader 的执行所有权。
- 不同连接具有独立 gate。当前仍不支持池化、MARS 和自动重连。

## 关闭和故障

Close/Dispose 关闭捕获的物理会话，使旧 Reader/LOB/事务失效。重新 Open 获得新的物理会话；旧对象的延迟清理不能关闭它。需要继续使用连接时，先 Close 再显式 Open。

本阶段在无法证明完整协议同步的错误上保守断开，包括已收到的部分服务器错误；不能依赖报错之后继续复用原物理会话。取消尚未实现，`Cancel()` 明确抛出 `NotSupportedException`。

未完成事务的 Dispose 不隐式提交。当前通过关闭其绑定的物理会话进行清理，不把关闭描述为已收到服务端回滚确认。完整事务错误分类和结果未知处理仍由 S07 后续任务完善。

已 Prepare 但仍由 Command 持有的服务器 statement，在 Dispose 时由同一会话的独占操作释放；如果另一 Reader 正在占用会话、无法安全释放该句柄，本阶段保守关闭该物理会话。通常应按 Reader → Command → Connection 的顺序释放。已经转移给 Reader 的 statement 由 Reader 负责。

## 暂停的遗留入口

`ExecuteXmlReader`、高级复合类型创建（CreateStruct/CreateArray/CreateIndexTable）在完整所有权与结果消费验收前明确拒绝。Reader 的公开 `GetKeyCols` / `GetUniqueCols` 也拒绝返回额外 Reader；GetSchemaTable 仍使用私有 owned metadata 路径并在内部消费和关闭结果。普通 GetSchema 使用独立 metadata 执行所有权。具体接口与真实覆盖以代码、wire inventory 和 T05 最终证据为准。

本地最小权限账号无法使用部分旧 `SYS` 目录 SQL。Tables/Columns 及 Reader 的主键/唯一约束查询改用可见的 `ALL_OBJECTS`、`ALL_TAB_COLUMNS`、`ALL_CONS_COLUMNS`、`ALL_CONSTRAINTS`；不提升账号权限。Tables 的 `FILLFACTOR`、`SPACE_LIMIT`、`ROW_COUNT` 和 Columns 的 `COLUMN_SIZE` 返回 `DBNull`：尚未证明它们与新视图字段具有相同语义，尤其字节长度不能直接冒充列大小。其他 schema 集合仍保留旧查询，未在本轮完成最小权限功能验收；完整元数据语义留给后续命令/类型任务。

TLS、收包限额、短写处理、真异步、取消期限及真正流式 LOB 仍按任务 DAG 后续推进。T05 的 LOB 所有权验证不等于大字段恒定内存或所有 LOB API 已验收。
