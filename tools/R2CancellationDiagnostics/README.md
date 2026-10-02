# T14 old/new/old 只读健康诊断

独立 consumer 仅 exact PackageReference，锁定旧 T13 accepted DLL 3eaa6a45... 和当前 T14 DLL fdea777d...。不引用或修改产品源码、原 T14Probe、tests、Low 的 ignored helper 或历史失败。只加载共享 TEST wrapper，PlaintextAllowed/mode 0；每 case 都 fresh TEST connection 并先核验 identity/Schema。Connect20、Command15 不变；30 秒工具等待护栏与 300 秒进程上限仅限制失控执行，不改变产品预算。

Low 串行运行三个独立 lane：

    eng/t14-diagnostics.sh health <old-exact-version> <old-feed> old-before
    eng/t14-diagnostics.sh health <new-exact-version> <new-feed> new
    eng/t14-diagnostics.sh health <old-exact-version> <old-feed> old-after

每 lane 七项最小对照：Reader 首行然后显式 Close 的无参 SELECT1、typed Int32 参数 SELECT、已恢复唯一表 T14_921060586F914E658A7FD736 的字面量 catalog COUNT0、typed VarChar 参数 catalog COUNT0；以及实际 ExecuteScalarAsync 的 SELECT1、字面量 COUNT0、typed VarChar 参数 COUNT0。Reader 不额外消费 EOF，以便区分首行与 Close/Dispose 网络窗口；Scalar 保留原私有 reader 与 Query lease 路径，隐式 Read/Close 对外不可直接分段，依数字 opcodes 与 invocation 快照定位。结果失败、COUNT 未返回或关键连接失败均拒绝并停止该 lane，绝不把失败当 0。

所有操作只读，不执行 DROP/任何 DDL，不创建对象，不读 SA/DEV/DMV，不修改实例。已恢复对象只以 exact name 查询自身 USER_TABLES，不再次清理。health-only 历史输入独立保留；readers 使用新 overlay，不重复健康 21 个 query。

每个下一 await 前写 safe progress；固定 Substage 为 Open、Identity/Query 的 Execute/Read/Fields/Close、Scalar.Execute、Scalar.Command.Close、Connection.Close。首个 failure_substage/failure 保留，Reader/Command/Connection Close 错误单列。数字 hook 使用异步单 writer 写 probe.json.frames.jsonl，观察挂起的 Scalar 内部发送/响应；不在 hook 阻塞等待磁盘或修改产品 cancellation/deadline。报告含 elapsed、数字 request/response/sqlcode/length、SessionId/ExecutionId/InvocationId、可用的 Purpose/Completed/IsDisposed/remainingMs。旧 DLL 没有 AfterFrameSent 明确 sent_opcode_hook_supported=false，只以兼容 AfterSendBeforeReceive 记录身份快照，RequestOpcode=null，不伪造发送 opcode。

Deadline/current/可选字段快照 getter 失败只记 null，观察 hook 不制造 wire failure。可选 FailureInfo 通过反射仅读取枚举与受控符号码，兼容旧包无新类型；不输出 message/stack/inner、SQL、参数值、连接串、环境变量或凭据。结果是定位证据，不替代 T14 验收。

readers 必须在 CLI 最后显式提供 gate JSON。格式为 health_reports 数组，依 old-before/new/old-after 顺序各含 lane、绝对 path、sha256；工具锁定 health-ab-v1 刚通过的三个报告路径与 SHA，再核对七项成功 case、identity/mode、包版本/DLL/MVID、20/15 秒预算。任一不匹配在建立 TEST 连接前拒绝，不以重跑健康替代固定 proof。

    eng/t14-diagnostics.sh readers <exact-version> <feed> old-before <absolute-gate-json>
    eng/t14-diagnostics.sh readers <exact-version> <feed> new <absolute-gate-json>
    eng/t14-diagnostics.sh readers <exact-version> <feed> old-after <absolute-gate-json>

每 lane 两项 fresh 只读 DUAL 2048×1024：baseline 不取消 Execute token；另一项只在 ExecuteReader 已返回后取消旧 token，后续 Read/getter 使用新的 token。保留 Execute 返回、缓存首行、rows read/validated、failure 前进度、实际 Fetch 与最后 sent/response request opcode，Close 错误独立。大结果进度仅在实际 Fetch、首末行和每 100 行 checkpoint；不逐行序列化全报告。网络/每次 Reader 调用的 15 秒预算不变；readers 进程护栏为 900 秒，覆盖完整 cached getter 循环与 checkpoint，不是提高产品预算。当前输入不创建表或执行恢复 DDL。
