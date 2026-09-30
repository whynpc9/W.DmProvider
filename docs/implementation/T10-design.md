# T10 — 本地事务和保存点

状态：实施中；T08 已独立验收并保存快照。S07 的隔离级别差分与完整 EF SaveChanges 另由 T11 收口。

## 状态和确认

事务对象保存自己的最终 outcome；不能只读当前 session 的可复用字段。Starting/Active/Committing/Committed/RollingBack/RolledBack/CompletedExternally/OutcomeUnknown 的转换必须与捕获的物理会话身份绑定。旧事务不得影响重新 Open 的会话；Clear 引用不能抹除已确认状态。

开始事务先核查原协议的 isolation/autocommit 确认机制。当前 BeginTrx 可能仅更新本地 AutoCommit，不能称为独立 BEGIN ACK。实施前用真实 profile 明确服务器事务启动边界，并使失败后的部分状态不可复用。Active 事务中用户命令必须显式指定同一 Active Transaction；内部控制路径使用明确借用入口。

官方 SQL 手册说明事务由 SQL 执行隐式开始，没有单独的显式 BEGIN 语句；因此不能发明 BEGIN opcode 或用虚构 ACK 满足规范。应实测隔离配置与手动提交模式的确认边界，并明确区分驱动本地事务 Active 和服务端已发生业务操作。手册还给出了 RELEASE SAVEPOINT，并说明它会同时释放后继保存点；具体测试服务器仍须验证该语法和行为，不能仅据在线手册开放旧版本能力。[官方事务 SQL 文档](https://eco.dameng.com/document/dm/zh-cn/pm/consistency-concurrency.html)（查阅于 2026-09-30）。

对 S07“Begin 确认后 Active”的实施解释：Active 表示句柄已绑定当前会话、请求隔离级别与已确认的登录缓存或必要 SET 响应一致、后续执行将使用 manual-commit 标志；不表示存在独立 BEGIN ACK。必要 SET 完整响应还须核对实际隔离值，失败或不确定时关闭会话。Unspecified 在创建句柄前统一解析为 ReadCommitted。另记录服务器是否已有业务发送/确认，SET 自身是否触发事务保留为待实测事实，不能用本地布尔值宣称服务器已经开始或回滚。

Commit/Rollback 在第一次实际发送尝试前记录身份和阶段；deadline/参数检查发生在发送尝试标记之前。发送尝试紧贴 transport write，不能用成功发送字节数判断是否可能送达。完整帧验证及确定成功响应才是 ACK。成功确认后迟到关闭/取消不能改成未知；发送后无确认则 OutcomeUnknown，销毁捕获会话且不重放。

`DmCommitOutcomeUnknownException` 继承 DmException，IsTransient=false。服务器确定错误与断链/畸形响应区分记录；没有服务端状态证据时不能默认已经回滚。注入测试覆盖 pre-send、partial-send、丢响应、无效响应及 ACK/Close 竞争。

## 清理和保存点

Active Dispose 使用有限、独立 CleanupTimeout 尝试 Rollback；存在 Reader 时先有界安全关闭，不能取得同步状态则销毁。清理失败保留诊断但不掩盖传播中的业务异常；显式 Rollback 传播失败。已确认终态及 Unknown 的重复 Dispose 不再发送事务控制。

Session 可以登记与完整执行身份绑定的 Reader 弱引用；事务清理在锁外获取并有界关闭，再尝试回滚。与进行中的 Read 冲突或无法确认安全状态时保守销毁。不得在 Session 状态锁里调用 Reader、网络或用户回调。用户命令事务绑定除计划捕获时检查外，还须在获得执行 lease 后复查，避免预检查与 Commit/Dispose 之间的竞争。

保存点用户名称映射为驱动生成的短 ASCII 标识符，维护顺序及重复名称替换。回滚后后继保存点失效；NUL/长度/数量上限在发送前检查。真实 SAVEPOINT 和 ROLLBACK TO 通过后才开放 SupportsSavepoints。若 RELEASE 未验证，采用明确文档化的逻辑释放，限制整个事务创建总数，避免用户释放后无限创建。

默认仅承诺 ReadCommitted；其他 isolation 经过 T11 两道门后开放。DDL 隐式提交用真实响应或隔离探针确认，不插入影响 ROWCOUNT 的状态查询；缺少可靠状态信号时公开限制，不能记录虚假 rollback。ambient 默认不参与，非空 EnlistTransaction 明确拒绝。

## 验收方式

Sol High 编码，测试 lane 控制真实 TEST 窗口，Sol Low 冻结后独立验证。事务外创建唯一对象，事务内改数据，结束后新连接读回；失败仍清理本次对象。源版本、兼容模式、服务器版本、send-attempt/ACK、事务终态、连接可用性分别记录。远程共享实例配置不更改。
