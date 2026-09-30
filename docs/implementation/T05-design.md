# T05 — 会话与执行所有权

状态：T05 已实施并通过独立验收；实际覆盖和限制见 [最终报告](reports/T05.md)。

## 范围和基线

承接 T04 的严格配置与显式明文测试策略，实现 S03 / SES-01–08。保留旧编解码，覆盖普通 SQL 和 `MSG<T>` 两条路径。T06 传输重写、T07 TLS、T08 之后的命令功能、真异步、取消期限、池和流式 LOB 不在本次扩大实现。

T04 尚未提交；其 evidence 文件保存当时源码哈希，后续文件演进不回写历史哈希。本次使用现有 `codex/t01-baseline` 工作区，不自动提交或推送。

## 核心不变量

1. 每次物理 Open 创建独立 `DmSession`，唯一 SessionId，保留未来池 checkout 的 LeaseGeneration；ExecutionId/InvocationId 只增不循环，溢出断开并拒绝。
2. 一条连接只允许一个根执行 lease，竞争立即失败。Reader 持有 lease 直到 Close；Read/NextResult 返回 false 不提前归还。同一 lease 的 child invocation 也互斥。
3. 同步路径可用 ThreadStatic 传递内部 invocation，但每个公开入口仍必须检查所有权。内部 schema 初始化、Scalar 消费 reader、LOB/fetch 使用明确的 owned 入口，不能因存在 ambient scope 就允许公共重入。
4. 完整 encode/send/receive/decode 使用同一 owner；底层 I/O 校验该物理实例的 owner。普通 SQL、MSG、握手、事务、metadata、LOB、资源释放都列入入口清单；禁用路径也注明。
5. abort 在同一短锁内校验完整操作身份、标记不可用并分离 transport；锁外只关闭捕获的实例。旧 invocation 不能影响新执行或重新 Open 的连接。无证明的协议失败保守断开，迟到成功不能恢复 Ready。
6. 状态锁内不调用网络、用户事件或日志。StateChange 在锁外发出；Open 事件期间仍拒绝配置变更和重入 Open，维持 T04 契约。
7. Close/Dispose 失效当前 reader/LOB，并关闭捕获的物理 transport；不调用旧事务 Dispose 的隐式提交链。旧事务绑定创建时的 session。托管 finalizer 不访问网络。
8. Cancel 暂时明确拒绝，不沿用 Reconnect。事务完整语义仍按 S07 后续任务推进；本次保证所有权、旧事务隔离及清理不暗中提交。

## 分工与文件所有权

| 执行者 | 文件范围 |
| --- | --- |
| 主 agent | 设计、交叉接口审阅、验收报告/进度/证据 |
| Sol High session | Internal/Sessions、Connection、Transaction、DmConnInstance |
| Sol High execution | Command、Reader、LOB、明确需要的内部 metadata 调用 |
| Sol High wire | 旧 A.B/A.D 协议和传输、wire inventory |
| Sol High tests | SessionTests、SessionProbe、eng/t05.sh |
| Luna Max | 合成 session ownership fixtures |
| Sol Low | 独立运行、失败复核及证据记录 |

## 验收方法

离线用 barrier/故障注入测试所有权竞争、完整身份过期、溢出、幂等释放、故障后状态和锁外回调。真实测试仅通过 `scripts/with-dameng-test.sh`，确认 TEST 身份、显式 PlaintextAllowed、使用唯一对象并 finally 清理。验证 reader 生命周期、事务竞争、两连接独立并发、LOB 内部路径及关闭后新连接可用；拒绝路径应有发送计数等证据证明未发字节。不能以固定 Sleep 或仅统计通过数代替并发交错证据。

T04 的配置回归必须继续通过。所有真实库和离线失败记录保留，只有独立 verifier 最终成功运行计入验收；未覆盖入口或未满足 SES 条件不得标记完成。

## 实施中确认的修正

- 旧参数编码会在主 SQL 发出前上传离行 BLOB。仅允许响应解码嵌套的第一版守卫阻断了该路径；以 O/R/W 同场景对照确认后，增加明确的内部参数上传 continuation，同一 invocation 保持不变，子请求异常使外层不可复用。
- 旧元数据 SQL 在最小权限账号下失败，旧 Reader 会吞掉错误。当前 Reader 使用公开目录视图并验证真实主键标记；GetSchema 的 Tables/Columns 同样改用可见视图，未证明等价的统计和列大小返回 DBNull。未扩展其他 schema 集合的功能验收。
