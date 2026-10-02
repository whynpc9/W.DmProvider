# T14 — 取消、期限与未知结果设计

状态：integration_verified；R2（T13–T14）原候选已独立验收，接受快照位于 `.local/verification/r2/t14/final-validation-v1/`。本轮定点 review 修复与新候选验收见 [R2-review.md](reports/R2-review.md)；原输入/证据保持只读。

## 原子终止与请求身份

第一版策略为 AbortPhysicalSession，不发送未经验证的 CMD_CANCLE。取消仅作用于捕获 session/generation/execution/invocation；身份验证、终止原因记录、Broken 和 transport detach 在 session 协调器中原子完成，实际 abort 在锁外。陈旧 callback 不能作用于后续执行或重新打开的连接。

公开调用开始时检查预取消，尚未发送请求则零网络副作用、会话和 Active 事务保留。发送尝试开始后，用户取消、Command.Cancel 或客户端期限终止销毁捕获会话，不重连、不重放。Send 尝试与取消之间的竞态使用同一协调器，不依靠已成功发送字节数推断安全性。

ExecuteReader 的调用 token 只活到返回；后续 Reader 调用绑定各自 token。Command.Cancel 绑定当前 command 的 execution；无活跃执行为 no-op。Reader 存活而无网络 invocation 时 Cancel 仍使该 execution 失效并有界关闭或直接 abort，不能影响别的 command。

## 期限和异常

使用现有单调 DmDeadline / TimeProvider：Connect 覆盖 DNS 到登录和初始化；Command 每次公开执行调用一个预算，Reader 每次公开方法重新开始冻结 command 值的预算；内部 Fetch/chunk/heartbeat 不重置该次预算。ReadIdle 可在字节进展后续订，但总 deadline 始终保留。cleanup 有限且独立于已经取消的用户 token。

终止原因由协调器记录第一个原因，不能在 catch 时通过 token 当前状态反向猜测。普通用户取消保持 OperationCanceledException 和原用户 token；Command.Cancel 保留 execution 自有 token/原因。超时使用明确的 driver Timeout 分类，与用户取消区分，不伪造服务端错误码。

发送后的 Commit 未获确认时，OutcomeUnknown 异常优先于取消/超时；确认 ACK 固化终态。Failure 信息只含稳定分类、请求阶段、NotSent/ServerReported/Unknown、事务结果和是否可复用，不包含连接串、SQL、口令或认证帧。未知写入/提交不得推荐自动重试。

## 证明

CAN-01..05 以受控 barrier 固定发送前、半发送/半响应、旧 token、旧代际、Cancel/Close/成功的交错。TMO-01..02 使用虚拟时钟验证期限、无限/负值、heartbeat 和用户两次 Read 间的处理时间。ERR-01 验证发送后 Unknown 无重放且服务器最终状态独立核实。

真实 TEST 场景使用本轮唯一对象，先身份确认、后 fresh connection 查询最终状态；abort 不宣称服务器立即停止。PER-01 用独立延迟连接观察线程池等待和吞吐/分配，不规定未经测量的倍数目标。失败和修复分别冻结版本，旧证据保持只读。

## 公开故障分类

普通取消/超时保存首原因和原用户token。Sent Commit失确认的顶层固定为 OutcomeUnknown、符号 `WDM_COMMIT_UNKNOWN`、阶段 Commit、事务 OutcomeUnknown、ConnectionReusable=false；CancelSource和InnerException保留原取消/超时原因。完整验证的ACK优先固化Committed，不在 BeforeControlAck 以晚取消伪造Unknown。实际Unknown测试在opcode8送完、尚未读取响应时中断；fresh服务器值如实记录，不预设提交一定失败。

发送前取消还必须保留既有prepared statement及metadata ownership，不能只“不关闭连接”却将server handle本地丢弃。Invocation初始化/注册和实际abort在session gate外，避免同步取消callback和Dispose等待互锁。

## Reader 失效与资源释放

`IsClosed` 在物理会话或代际失效后返回 true，表示 Reader 已不可读取；它不释放 execution lease、command plan 或冻结的命令设置。调用方仍须无条件 Close/Dispose（异步路径使用 CloseAsync/DisposeAsync），包括 Command.Cancel、连接关闭及其他错误之后。建议使用 `using` / `await using` 管理 Reader 生命周期；不以 `if (!reader.IsClosed)` 跳过释放。完成释放后命令才能再次修改设置。该关闭只处理捕获身份，不能影响显式重开后新会话。
