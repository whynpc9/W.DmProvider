# T14 实际包取消 consumer

独立 net10.0 精确 PackageReference consumer，不引用产品 ProjectReference、官方驱动或 mutable DLL。每个候选版本、nupkg/net10 asset/实际加载 DLL SHA、MVID、deps package 类型都核对；runner 前后检查 nupkg 不变。仅指定 Low 执行 `eng/t14.sh shared|tls <exact-version> <absolute-package-feed>`，其它 owner 不运行 dotnet/数据库。

共享 `8.1.5.60` 是主验收，显式 PlaintextAllowed/mode 0；独立 `127.0.0.1:15236` 的 TLS `8.1.4.6` 是相同取消边界验收，显式 RequireTls/mode 1、既有本机 CA/client cert 与无 CRL 的 NoCheck。所有连接先核验 WDM_PROVIDER_TEST 和同名 Schema。TEST wrapper 才加载相应 secret，不读取 DEV/admin/bootstrap，不改容器模式、服务器设置或系统信任。

每轮仅创建 `T14_<随机24位>` 表。owner Transaction 锁住自己行，另一物理连接/Transaction 的 prepared UPDATE 使用成功发送后的数字 `AfterFrameSent` hook（execution opcode 6/13）确认 pending，再分别 user token Cancel、Command.Cancel 或客户端总期限终止。三项都要求 Broken、事务与操作 OutcomeUnknown、不可复用、正确首原因与异常类型、单次 execution/无自动重连，并释放 owner 锁后由 fresh TEST 连接确认未提交值不留。只等待有限最终核实，不宣称服务器立即停止。

行锁和 PER waiter 的 UPDATE 均含真实 Int32 input 参数 `:val`，使实际包进入参数化 prepared execution；仅调用 PrepareAsync 的无参数 UPDATE 会使用 opcode 5，不能满足此发送门。保留严格 6/13 gate，不接纳 opcode 5。safe report 另记录固定 stage/连接数、执行帧基线计数和实际发送的数字 opcodes，失败时也可定位工具 gate；不记录 SQL 或参数值。

其它边界包括预取消 command/Commit 零 send、Active/reusable 与新 token 查询；无活动 Cancel no-op；ExecuteReader 返回后旧 token 取消不影响新 Read token 的缓存行和实际 Fetch；idle Reader Cancel 的稳定 typed 原因、显式重开后旧 Cancel/Dispose 不伤新 session。Commit 的 opcode 8 成功发送后、Read 前取消验证 Unknown 优先且不重放，fresh 状态按实际 0 或 1 记录；validated 成功响应的 BeforeControlAck late cancel 则要求 Committed 不反转且 fresh 1。回读之后的独立 fixture reset 不重试未知事务。

PER-01 只在 shared profile 测量独立 8/16 waiter 连接，各自等待不同的本次 owned row。全部 prepared UPDATE 已发送且 Task pending 的 gate 才开启 250 ms 观测窗口；窗口仅采样，不作为非阻塞证明。记录 ThreadPool.ThreadCount、GetAvailableThreads、托管分配、elapsed、完成吞吐和单次 execution。拒绝每个 waiter 占一个忙 worker及 8→16 对应增加 8 个忙 worker的观测；不设任意吞吐倍数或推广为所有负载的性能结论。结束取消 waiter、释放 owner、fresh 每行 0、精确清理本轮表。

报告只含固定安全分类、CLR 类型、数字 opcode/count、受控指标和 FailureInfo 的枚举/符号码。绝不输出 SQL、message/stack/inner、连接串、认证帧、环境变量或私钥。cleanup 使用新有效 TEST 连接并最终 fresh absence；所有异步等待有公开 command 期限及工具 30 秒等待护栏，整个 probe 进程有 1200 秒安全上限（包含多连接准备/独立回读，单次调用预算不变；旧300秒失败记录保留）。无连接或任何缺失均不作 pass。真实包证明需与独立 barrier/虚拟时钟/first-cause 单元测试及 root review 一起评估。

在 DDL 前先写 safe checkpoint 保存本轮精确表名。若整个进程被安全上限终止，不能声称 finally 已完成；Low 必须依据该 checkpoint 用 fresh TEST 身份确认并清理唯一对象，再记录最终 absence，禁止全 Schema 清理。
