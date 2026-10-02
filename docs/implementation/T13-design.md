# T13 — R2 端到端异步设计与验收

用户指令：实现 R2。实施基线 `6b3d1796dfdc207ec5c1949f1998ca0df4e9697c`；T12/R1 已验收，T13 激活。T14 在 T13 独立验收后推进，T15–T25 不在本轮范围。

## 调用链与所有权

公开 Async 入口 → 冻结 command plan / execution lease → 单次 invocation → 完整 protocol exchange → 帧编码与校验 → Socket/SslStream 异步 I/O。网络等待不得通过 Task.Run、线程包装或基类同步 fallback 实现。缓存与纯 CPU 解码允许直接完成。每个 await 使用 ConfigureAwait(false)，短锁内不 await 网络。

按 S08 §2 对既有 R1 经加固同步链的例外，本轮保留其同步入口，避免为异步改造而替换全部历史同步 fake。声明的 Async 路径统一使用异步传输/协议核心；必须逐分支审计并清除残留同步 I/O。不能据此宣称全部同步/异步状态机已经合并为一个算法。

invocation 和 wire exchange 的环境身份从 ThreadStatic 改为按异步执行上下文传递；同步 Begin 方法设置不可变 AsyncLocal 值，Dispose 恢复 prior 值。不得用共享可变 holder 把另一个执行上下文的 owner 清除或替换。session 内仍验证捕获身份和单活动 invocation，AsyncLocal 不是并发互斥替代品。

Reader 接管 execution lease 与冻结 plan。ExecuteReader 的 invocation/token 在成功返回后结束；后续每次 Read/NextResult/getter 使用自己的 invocation。内部 drain/fetch/chunk 复用一次调用的期限，不能每个报文重新给总预算。cleanup 使用有限独立预算；事务 Dispose 的 Reader 关闭和 rollback 共用该次 cleanup 绝对期限。

## 必须贯通的网络点

DNS/TCP、STARTUP、TLS 升级、LOGIN 和 schema 初始化；statement allocation、prepare、参数上传和执行；首次跨页 Fetch、MORE_RESULT 与 count-only drain；本地事务配置、Commit/Rollback、保存点控制；statement/Reader/Command 的异步释放；R1 已支持的 BLOB/CLOB 物化与异步字段读取。

参数编码及结果解码中的嵌套网络 continuation 必须拆出异步步骤。来源快照和协议格式不变，不猜 opcode、不开放原本禁用的复杂类型、refcursor、FLDR、池化或完整流式 API。元数据 Async 包装若尚未贯通，明确 Unsupported，不能沿用同步 fallback。

Connection Close 的现有行为是 detach 后 abort 捕获 transport，不发送 LOGOUT；这条无网络等待的关闭路径可以返回已完成 Task。不能通过清理旧对象关闭重新 Open 后的新 session。

## 验收和冻结

High 编码、Luna 合成逻辑调度数据，root review 后由 Low 独占实际构建和实库窗口。文件归属见 [active-ownership.md](active-ownership.md)。

离线使用同步 I/O 直接抛错的 fake channel 和受控 barrier：调用立即返回 pending Task，释放回复才完成；验证 async-flow 身份、短 I/O、EOF、heartbeat 和清理。覆盖真实后续 Fetch、NextResult、事务和 DisposeAsync，不以首个缓存结果代替。

实际候选 nupkg 使用唯一版本，独立 PackageReference consumer 核对包内与加载 DLL 的 SHA/MVID，分别验证 TEST 明文显式配置和隔离 TLS。每轮先确认 TEST 身份，使用唯一对象并精确清理，以 fresh TEST connection 确认服务器最终状态；无 SA、跨 Schema 或实例设置变更。

历史 R1 archive 与 54 项 evidence 哈希保存于 `.local/verification/r2/historical-baseline/manifest.json`。每次失败、修复、再冻结分别保留新证据，不回写旧候选。源码未通过独立验证前不声明 T13 或 R2 已验收。
