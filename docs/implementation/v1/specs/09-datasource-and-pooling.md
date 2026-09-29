# S09 — DbDataSource、连接池和会话复位

状态：实施规范。优先级：P1/P2；R3。依赖：S03、S07、S08。任务：T15、T16。

## 1. DataSource 所有权

`DmDataSource : DbDataSource` 持有不可变配置、诊断与一个池。CreateConnection 返回 Closed 逻辑连接；OpenConnection[Async] 返回 Open 逻辑连接。DataSource.CreateCommand 等快捷 API 必须让内部连接在非 reader 完成/reader关闭时释放，不能每个 Fetch 获取另一连接。[F03]

显式创建的两个 DataSource 默认拥有不同池，不因连接字符串看起来相同就互相接管生命周期。DataSource 的 ConnectionString 为安全可展示版本，内部凭据不从它反向解析。

为了兼容 EF 目前每次 new DmConnection(connectionString)，直接构造且 Pooling=true 的连接使用进程内受控 pool registry。Registry 使用全量不可变配置身份、并发同步与有界容量；不复用旧共享可变过滤器。默认 Pooling=false；无配置不隐式引入全局池。

## 2. 池身份

至少包括：endpoint/port、用户与认证凭据身份、schema、数据库语义模式、字符集/时区/初始化设置、TLS与CA/客户端证书身份、安全兼容选项、影响 wire 格式的配置。不同凭据或证书不得共用。

不能仅 `string.GetHashCode()` 作身份；哈希仅用于查找，完整不可变键负责相等。含秘密的对象不得使用自动 record.ToString 输出内容；秘密仍会为新建认证保留在内存，不声称散列后已无需保存密码。证书/凭据轮换需要新身份或明确池清除，不能永远复用旧认证会话。

规范化允许等价别名归一，但不能把大小写敏感密码/schema 转小写；不允许两个租户通过hash碰撞获得同一会话。

## 3. 池状态与计数

维护 idle、leased、creating、resetting、closing/reserved；物理容量许可涵盖尚未建完和尚未清理完成的连接。每个许可恰好释放一次。等待队列有界，使用不会在锁内执行用户 continuation 的 TCS/等价机制，取消不会漏 permit。

checkout：检查关闭状态 → 取合格 idle 或在容量内预留创建 → 必要的验证 → 分配新 LeaseGeneration → 交付。队列取消/超时与会话分配需原子处理；如果取消获胜，会话归池或销毁，不能既发给取消用户又给另一个用户。

无闲置且满池时异步等待；这里允许等待，不改变 S03 的同连接并发命令快速失败规则。PoolAcquireTimeout 不反复因队列唤醒重置。

return：使外部旧对象无权再使用 → 注销/完成所有取消回调 → 清理 reader/LOB/事务 → 复位验证 → Ready入idle或销毁。所有失败路径统一计数。旧 statement、LOB locator 不能跨 generation 使用。

## 4. 复位是独立能力，不等于 SELECT 1

默认策略 `VerifiedResetOrDiscard`：只有已证明 session 可以恢复成该 pool 的基线，才能复用；不确定就销毁。不能把 ROLLBACK+SET SCHEMA 包装成“完整复位”。

T15 必须验证至少：事务/autocommit/isolation、schema、role/授权相关会话状态、时区/NLS/语言、临时表、会话变量、session级锁、未关闭游标、prepared statement、包/过程会话状态（目标工作负载涉及时）。调用方原始 SQL/存储过程可能改变客户端无法跟踪的状态；仅检测 SQL 首词或“这是EF生成”不构成证明。

优先寻找并实测服务端 reset/reinitialize 能力及权限/版本；不得猜 reset opcode。没有全量 reset 时，可以证明一组受限操作的安全复用，但必须有明确边界和检测方式；对无法分类的原始 SQL 采用 discard，不能默默切换弱复位。特殊 application-managed 复位策略另行 ADR，不能作为默认安全模式。

因此，可能出现“池容量与异步等待已实现，但某些会话每次归还都销毁”的正确状态。报告真实 reset/discard 比例，不宣称这些路径减少了握手。只有常用目标路径确有安全复用证据后，才宣传池化性能。

不在 DML与SQL%ROWCOUNT/键回读之间验活；所有 reset/validation 在执行lease之外的专用维护 lease 内进行。

## 5. 验活与维护

不用每次 checkout 无条件 SELECT1。按显式验证策略和空闲时长执行有界健康检查；成功验活只是连接可通信，不代表状态已复位。失败销毁并在同一获取预算内再尝试，不能无限重试。

idle eviction/lifetime 等使用一个受控调度器+TimeProvider，不每条连接创建专用后台线程。不在全局锁内发网络请求。Registry 有池数量上限；零借出且超期的池可移除，防止每个租户/动态连接串创建永久静态对象。

`ClearPool/ClearAllPools` 增长 pool epoch，关闭 idle；已借出会话标记归还时退休，不暴力中断业务。DataSource Dispose 停止新借用、取消等待者、关闭闲置；在外借出的会话归还时销毁，文档明确 Dispose 不无限等待用户永不释放的借用者。后台维护停止且任务被观察，无未观察异常。

## 6. 验收

POL-01：MaxPoolSize=1，一个占用、第二个等待可取消；所有结束后permit总数正确。
POL-02：建连失败、初始化失败、取消、复位失败、清理异常均不漏容量。
POL-03：不同用户/密码/schema/TLS/证书不串池；相同有效配置别名得到预期身份。
POL-04：归还未完成事务后，下一租约看不到旧事务写入；实际数据库独立连接检查。
POL-05：修改schema/role/时区/临时对象/会话锁后 reset或discard，不泄漏给下一租约。
POL-06：取消callback与回收/checkout竞态不能破坏下一租约。
POL-07：DataSource关闭/ClearPool与活跃借用、等候者、维护线程竞争无死锁。
POL-08：长期多租户配置 churn 后注册表/线程/句柄/statement数量有界。
POL-09：DataSource.CreateCommand 返回reader时连接持有到关闭；执行失败也恰好归还一次。
POL-10：报告复位证明、实际复用率、验证RT；不把discard路径算握手优化。
