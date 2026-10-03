# T16 独立精确包连接池探测

本工具只通过精确 `PackageReference` 消费冻结 nupkg，不引用产品项目，不使用官方驱动。版本、包/DLL SHA256、实际加载位置、MVID 与 deps 中的 package 来源必须一致。High 只编写源码；root 审阅并冻结后，Low 独占编译及真实库窗口。

入口为 `eng/t16.sh offline|tls|shared <exact-version> <absolute-local-feed>`。runner 保存安全命令、退出码、前后包 manifest、consumer build、checkpoint 和独立 validator 结果；runtime 最长 900 秒，超时必须依 checkpoint 精确恢复本轮对象，不能视为通过。

offline 核查 DataSource 公共 API、Closed 构造、安全连接串、显式证书文件 direct pooling 在 socket 前拒绝。调度器合同由独立 PoolTests 验证，不用伪握手冒充真实数据库。

真实 profile 使用 TEST wrapper，新连接读取 USER 与当前 Schema。`Pooling=true,MaxPoolSize=1,MaxPoolWaiters=4,PoolAcquireTimeout=5000ms` 固定为本工具输入，Connect/Command 仍分别为15秒。TLS 为本机隔离实例与显式证书文件，通过独立 DataSource 容量域；shared 分别验证 DataSource 与 direct registry。

必需场景包括等待取消与截止无 socket、Clear 保持活跃容量域、归还物理关闭后新认证、重复关闭、DataSource Dispose 结束等待而允许业务租约完成、快捷 nonreader 成功/服务器失败/reader 持有、活跃事务关闭后的 fresh 零未提交行、旧 reader/transaction/cancel 无新 I/O、新会话可获得原锁。快捷隐式连接用 SQL 身份条件和结果验证，无插入握手查询。仅创建 `T16_T_<20 hex>` 唯一表；finally 仅清理该精确名字，再用 fresh TEST 连接查 absence。

活跃事务另有同事务写入 `COUNT=1` 和关闭前独立 DataSource observer `COUNT=0` 正控制，关闭后 fresh 仍为0，再验证原锁释放。实际 connect/tls/send/receive 入口通过包内只读观察 hook 统计有限标签，async 实库必须同步入口0，connect/send/receive异步计数大于0，TLS另要求异步tls大于0。缺观察 hook 直接拒绝，不跳过或将Task返回类型冒充异步网络。

输出只包含受控错误类别/枚举/服务器编号、TEST 身份、数字版本、物理/异步入口计数和零状态快照，不含异常文本、SQL、连接串、环境或认证内容。成功要求实际认证次数=创建 socket=关闭 socket、每种模式准确、所有 owner 的 creating/leased/closing/waiting/idle/resetting 归零。全部租约归还销毁，`reuse=0`；没有完整 reset 或握手性能提升结论。shared/TLS 独立报告；offline 仍为 `integration_pending`。
