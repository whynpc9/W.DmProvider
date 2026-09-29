# S03 — 物理会话、执行所有权与生命周期

状态：实施规范。优先级：P0。依赖：S02。任务：T05。此规范先于所有网络现代化。

## 1. 现有入口及改造方法

起点：`DmConnInstance`、`A/B.cs` 的旧 SQL 收发与新 `MSG<T>`、`A/D.cs`、`DmCommand`、`DmDataReader`、`BaseFilter`。[R03]

先建立唯一会话协调器，把既有协议实现作为内部 codec/adapter 使用。先改生命周期与入口，不要求一口气重写编解码。所有调用 socket/SSL 的位置建立 inventory；事务、LOB、Prepare、Schema 查询、pool validation/reset 也在 inventory 内。

## 2. 三个独立状态模型

**逻辑 DmConnection**：Closed → Opening → Open → Broken/Closed → Disposed。Broken 的物理会话不可复活；用户必须 Close 后显式 Open 获取新会话，不自动延续事务。Open 失败对外回到 Closed，内部损坏 transport 已销毁。StateChange 只在外部状态变化时触发且不在状态锁内调用用户代码。

**物理 DmSession**：New → Connecting → Authenticating → Ready → Busy → Ready；Ready → Resetting → Ready；任意存活状态 → Broken → Closed。Closed/Broken 不得重新 Ready。Busy 的 purpose 是 Query/Reader/TransactionControl/Metadata/Lob；事务是否活跃是独立状态，不能简单等同于 Busy。

**本地事务**：None、Starting、Active、Committing、Committed、RollingBack、RolledBack、CompletedExternally、OutcomeUnknown。未知结果和已完成不被 Dispose 状态遮蔽；详情见 S07。

## 3. 所有权对象

```csharp
// 设计草图，不是已实现 API。
internal readonly record struct OperationIdentity(
    long SessionId, long LeaseGeneration, long ExecutionId, long InvocationId);

internal sealed class DmSession { /* 唯一 transport、状态、执行 gate */ }
internal sealed class DmExecutionLease : IDisposable, IAsyncDisposable
{ /* command/reader 持有；幂等释放，不能复制 */ }
internal sealed class DmInvocation : IDisposable
{ /* 一次公开方法/内部清理的期限、取消注册和发送阶段 */ }
```

SessionId 标识物理连接且永不复用；LeaseGeneration 每次 pool checkout 增长；ExecutionId 每个逻辑执行增长；InvocationId 用于 ExecuteReader 与后续 Read/NextResult 等不同调用。计数器溢出必须拒绝/销毁，不循环回旧身份。

### 获得与转移

- `Execute*` 以原子检查获得执行 lease；如果已有 reader/command/事务控制占用，**立即抛 InvalidOperationException**（可有自定义子类），不等待另一条用户命令。
- `ExecuteReader` 成功时把 lease 转交 reader；方法返回不释放。`Read()==false` 或最后 `NextResult()==false` 也不允许另一命令抢先执行，直到 reader 关闭。
- Reader 的 Fetch/LOB 操作复用该 lease，只产生 child invocation；绝不递归调用需要重新获取根 lease 的 public command 方法。
- Commit/Rollback/Savepoint 与 reader 并发使用属于调用错误，立即拒绝；连接/事务 Dispose 为清理可走独立关闭路径，不以普通操作竞争造成死锁。
- 多个 child invocation 同时操作同一 reader/LOB，立即拒绝。父 lease 不意味着任意数量子请求可交织。
- `ExecuteScalar`/`ExecuteNonQuery` 负责完成结果处理或安全终止后释放 lease。

### 同步原语

使用短临界区维护状态、身份和 transport 引用；临界区内不做 I/O、不 await、不调用日志回调/用户回调。等待池容量属于 S09，和同一连接并发命令不是一回事。

不要只对每次 socket.Write 加锁：一次请求的 encode/send/read/decode/state-update 是同一个拥有者的操作；请求 A 的 response 不能被 B 读走。

## 4. 故障与取消的线性化

R1 中 `DmCommand.Cancel()` 也不得沿用上游 Reconnect：可提前使用物理会话销毁，或明确报告该取消路径尚不支持；不能等到 R2 才消除“取消会重连”的危险行为。

取消/关闭需在同一状态临界区中执行“校验身份 → 标 Broken/不可复用 → 分离目标 transport”。在锁外仅关闭捕获的 transport。不能先检查 generation 再晚些时候读取 `connection.CurrentSocket`，否则中间归还/重新租借仍可能误伤新请求。

归还池前必须满足：所有 invocation 完成或中止、取消注册已注销且其回调无权再影响会话、reader/LOB 失效、事务终结可知、复位验证成功。`CancellationTokenRegistration.Dispose` 可能等待回调，不能在回调所需的状态锁内调用。

若 abort 与成功 response 竞争，状态转换在同一个协调器判定。已经观察并原子完成的成功不被随后 token 取消改成 Unknown；反过来已标 broken 的执行不能由迟到响应恢复成 Ready。

## 5. 关闭与资源

`Close` 幂等；标记正在关闭，使旧 reader/LOB 无效；执行有界清理。明确无可靠协议恢复时关闭物理 transport。清理使用独立有限期限，不使用已取消用户 token。

`DisposeAsync` 真正等待需要的异步清理；R2 后不能用同步 Dispose 包 Task。同步 Dispose 可以阻塞在有界清理边界，但不能开启无限后台工作。

托管 command/reader 不使用 finalizer 做网络操作；SafeHandle 只处理其原生句柄。发生无法完成的最终清理，记录脱敏诊断并销毁，不能承诺回滚已确认。

## 6. 验收

SES-01：一个 reader 持有 lease，第二 command/Commit/同 reader 并行 Read 均快速失败，不写任何字节。
SES-02：reader 读取分块 LOB 不递归获取根 lease，无死锁。
SES-03：旧 SQL 与 MSG<T> 两条路径无法交织请求/响应。
SES-04：Cancel/Close 与返回池、再次租用精确交错，旧回调不能关闭新请求。
SES-05：Open/Close/Dispose 多次调用不重复释放、不重复发状态事件。
SES-06：异常与取消后的状态只能是证明 Ready 或已断开；不存在“socket断了但对象继续可用”。
SES-07：独立连接可并行，不因为全局锁被串行化。
SES-08：fault injection 检查每一条 wire 入口都需要有效 lease 或专用 handshake lease。
