# S07 — 本地事务、隔离级别、保存点和未知结果

状态：实施规范。优先级：P0。依赖：S03、S05。任务：T10、T11。

## 1. 必须改变的行为

官方恢复代码在特定 Oracle 兼容设置下对仍有效事务 Dispose 进入 Commit；W 不保留该行为，所有兼容设置遵守同一个“未显式提交则尝试回滚”的公开契约。[R05]

注意“尝试回滚”不等于网络失败时已确认回滚。Dispose 失败应使连接不可复用并保留诊断；不能将 socket.Close 记录成服务器 Rollback acknowledged。

## 2. 开始与绑定

只支持一个本地事务；嵌套 BeginTransaction 明确拒绝。Begin 在成功得到服务端确认后才返回 Active；缓存 isolation/autocommit 只能在确认后更新。无法确认 Begin 的部分状态时销毁会话，不能放回池。

命令有显式 Transaction 时必须属于同一逻辑连接、同一 session/generation 且 Active；旧事务不能用于重新 Open 的连接。为避免无意脱离事务，Active 本地事务中未给 command 指定 Transaction 的操作首版拒绝（驱动内部控制操作使用专有入口）；该行为在 EF 使用显式事务时验证。

事务不因连接字符串改变兼容设置而改变 dispose 语义。不把服务器事务状态存在进程静态字段。

## 3. 隔离级别

R1 默认承诺 ReadCommitted；Unspecified 明确解析为本产品默认 ReadCommitted。ReadUncommitted/Serializable 根据真实服务器、SQL形态、事务结果验证后开放对应保存路径。RepeatableRead、Snapshot、Chaos 未证明语义之前抛 NotSupportedException，不能把 RepeatableRead 静默降到 ReadCommitted。

隔离支持有两道门：服务器语义（并发读写 litmus tests）和驱动执行链（特别是 DML+SELECT/保存点）。仅 Begin 成功不代表隔离语义和 SaveChanges 已支持。

**T11 最小复现**：同一服务器/账号/配置，对比 ReadCommitted、ReadUncommitted、Serializable；分别运行单 UPDATE ExecuteNonQuery、UPDATE+SQL%ROWCOUNT ExecuteReader、INSERT+键回读、保存点前后组合、完整 EF SaveChanges。捕捉异常栈、脱敏 SQL 形态及 CommandText 生命周期。现有 EF 已记录某两级 SaveChanges 报 CommandText has no value，并有 Serializable ExecuteUpdate 成功测试，但根因不能由此推定。[R08]

## 4. Commit/Rollback 状态与异常

| 情况 | 状态与返回 |
|---|---|
| 参数错误/预取消，尚未开始发 Commit | 保持 Active；返回参数异常/取消 |
| 收到且验证 Commit 成功响应 | Committed；旧 transaction 不再可用 |
| Commit 发送尝试后连接/响应不确定 | OutcomeUnknown；抛 DmCommitOutcomeUnknownException，连接 Broken |
| 收到确定的服务器 Commit 错误 | 按已验证服务器状态处理；不能默认当作回滚 |
| Rollback 确认成功 | RolledBack |
| Rollback 响应不确定 | OutcomeUnknown；销毁连接，不报已回滚 |

`DmCommitOutcomeUnknownException` 继承 DmException，IsTransient=false；可携带取消/超时作为原因。即使触发原因是 token，已经发送 Commit 且结果不明时优先暴露未知提交结果，不把它装成“只取消了一个未执行请求”。此特殊规则必须文档化并测试。

发送“尝试”开始就保守标记可能送达，因为 Socket/SSL 写异常通常不能证明服务器收到多少字节。收到成功后迟到取消不改变已确认结果。绝不在驱动内自动重连重放 Commit 或整个事务。[F06]

## 5. Dispose 与连接 Close

Active 事务 Dispose：若会话同步，使用独立 CleanupTimeout 尝试 Rollback；若有活动 reader，先按 S05 安全关闭它再回滚；无从安全同步则销毁。清理失败不得悄悄归还池。

同步 Dispose/DisposeAsync 默认不以清理异常覆盖调用方正在传播的业务异常；错误通过 FailureInfo/诊断保留，并销毁会话。显式 Rollback 则传播失败。无论策略如何，不能产出“已回滚”的虚假成功证据。

已经 Committed/RolledBack 的 Dispose 幂等无网络操作；OutcomeUnknown 的 Dispose 不再发送 Commit/Rollback 去猜结果。Clear 引用时先保存最终 outcome，不能 finally 中无条件改为已完成成功。

## 6. 保存点

实现标准 `SupportsSavepoints`、Save/SaveAsync、Rollback(name)/RollbackAsync(name)、Release/ReleaseAsync。[F07]

名称建议映射成驱动生成的短 ASCII 服务端标识符，维护事务内 userName→serverName+sequence；不把用户名称裸拼 SQL。重复名称替换、回滚后后续保存点失效、长度和 NUL 检查有测试。若为兼容必须使用用户名称，严格引用与转义且遵循已验证标识符长度。

服务端 Save/Rollback 必须真实执行。若服务器没有验证可用的 RELEASE，允许公开说明 Release 仅逻辑释放、服务端资源到事务终结回收；不得伪称发送了不存在的 RELEASE opcode/SQL。.NET10 DbTransaction 的默认 Release 本身是 no-op，引用该事实不等于证明 DM 的 RELEASE 语法。[F07] 限制同事务创建保存点数，避免逻辑释放导致无限服务端资源增长。

初始服务器 profile 未证实时 SupportsSavepoints=false，明确拒绝；证明 save/rollback 后才能 true。EF 当前可能走自己的保存点 SQL，因此原有 SQL 路径和新增 ADO API 都要测，不能只测一个。

## 7. DDL 与 ambient 事务

DM DDL 的隐式提交边界不能由驱动伪装成原子迁移。读服务端响应中的已验证事务状态；看到事务已经终结就标 CompletedExternally、使旧事务失效。没有可靠状态信号时明确限制/记录未知，而不在每条 DML 与键回读之间插入状态查询。

本地事务内执行 DDL/动态 DDL 的实际副作用必须在隔离测试中记录；本产品不宣称能回滚已隐式提交的 DDL。[R11]

R1–R3 不实现分布式事务提升。`Enlist=true` 或非空 EnlistTransaction 调用明确不支持；默认 Enlist=false 不参与 Transaction.Current，这一点必须文档化，不能表面接受 scope 却假装参与。后续单资源 PSPE 与多资源提升是两个独立特性，见 S13。

## 8. 验收

TX-01：所有已声明兼容设置，未 Commit Dispose 后独立连接查不到写入；真实事务回滚正确。
TX-02：Commit/Rollback/重复 Dispose、旧 transaction 与重新 Open 的 session 错配被拒绝。
TX-03：Commit 响应丢失与 token 竞争，outcome unknown、没有重放、没有回池。
TX-04：两条语句第二条失败后显式回滚；独立连接验证最终状态，而不只检查异常。
TX-05：保存点正反路径、重复/Unicode/恶意名称、回滚后后继失效；无注入。
TX-06：隔离级别 litmus tests 与 SaveChanges 最小复现单独记录。
TX-07：DDL 隐式提交不被写成回滚成功；状态和文档一致。
TX-08：ambient 配置拒绝/不参与语义准确，不存在伪事务。
