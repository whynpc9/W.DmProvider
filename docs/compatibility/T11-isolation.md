# T11 隔离级别与保存点 SQL

本页描述当前候选实现；验收状态、包标识与证据以 [T11 报告](../implementation/reports/T11.md) 为准。

| 级别 | 公开行为 |
| --- | --- |
| Unspecified | 使用 ReadCommitted |
| ReadCommitted | 维持既有默认行为 |
| ReadUncommitted / Serializable | 仅服务器自报版本精确为 `8.1.5.60` 时允许 |
| RepeatableRead / Snapshot / Chaos / 未知值 | 明确抛 NotSupportedException，不静默降级 |

切换隔离级别使用独立控制 statement。事务配置以本次已验证 SET 响应及当前操作身份确认，不要求连接默认隔离缓存随事务设置改变。该 statement 有界关闭，用户命令使用自己的 statement，不继承空 SQL owner。

活动事务内的用户 Command 必须显式绑定对应 Transaction。已验证的 EF 单条 `ROLLBACK TO "name"` 与带 `SAVEPOINT` 关键字形式均可使用；名称支持双引号转义。包含额外语句、缺失名称或完整事务 ROLLBACK 的 SQL 仍拒绝，应使用标准事务 API。SQL 原文不被驱动改写。

此范围不表示所有达梦版本、所有 EF 10.0.x、完整规范套件或真异步均已通过。T11 的固定下游为 EF Core 10.0.12；完整 R1 下游门属于 T12。
