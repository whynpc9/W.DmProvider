# T10 事务契约变化

证据和范围见 [T10 报告](../implementation/reports/T10.md)。

- 本地事务默认 ReadCommitted；Unspecified 也为 ReadCommitted。其他隔离级别待 T11 验证，不静默降级。
- Active 事务的用户命令必须设置 Transaction；旧事务不能用于重新 Open 的连接。
- Commit 发送后无法确认结果会抛 `DmCommitOutcomeUnknownException`，不自动重连重放。`Outcome` 在释放后仍可读取；失败类别不包含凭据。
- 未提交事务 Dispose 尝试有限回滚，清理失败关闭捕获的原会话；关闭 socket 不等于已确认回滚。
- 保存点使用映射名称和数量上限，能力仅对已验证 profile 开放。用户原始保存点 SQL 与 ADO API 是不同路径，完整 EF 验收继续由 T11/T12 覆盖。
- 已证 TABLE DDL 会使事务 CompletedExternally；不能再用旧句柄假报回滚。Active 下不确定 DDL/动态控制形态提前拒绝；DDL 本身不承诺原子回滚。
- Valid 的变更 setter、活动事务 Clear、篡改旧 `_disposeStatus` 均不能改变真实生命周期；相关旧用法需要调整。
- 默认不参与 ambient transaction，非空 enlist 明确不支持；外部 ambient 状态不应隐式影响本地事务。
