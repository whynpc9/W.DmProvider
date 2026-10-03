# T15 exact-package reset/discard probe

此 consumer 只引用独立的、唯一精确版本 `W.DmProvider` NuGet 包，禁止 ProjectReference/官方驱动依赖。它不修改产品源码，也不凭一次健康检查声称完整会话复位。

独立 Low 验证负责人通过 `eng/t15.sh offline|shared|tls <exact-version> <frozen-local-feed>` 编译并执行。每轮使用隔离的 bin/obj/NuGet cache/CLI home，不改历史证据、不清全局缓存。所有实库连接先检查配置用户，再验证服务端 USER、当前 Schema、STARTUP mode 和新 socket；共享 profile 显式 PlaintextAllowed，独立本机 TLS profile 使用 RequireTls 和实际证书验证。NoCheck 仅针对本机无 CRL 的临时 CA，产品默认不变。

JSON 报告包含实际 package SHA、加载 DLL SHA/MVID、runtime/RID、数字服务器版本、13 项 capability ledger、命令及退出码和 fresh cleanup 状态。异常只输出类型、固定分类和数字服务端错误码，禁止原始消息、SQL、连接串、认证报文及环境变量。命令参数只含固定控制值和本地 artifact 路径。`LogLevel=OFF`，runtime stderr 不保存。

当前源码调查覆盖 S09 和本仓库 codec：`Internal/Legacy/A/A.Async.cs:ResetAsync` 调用 `B.Async.cs:AAsync(A)`，传入 statement handle；它没有提供完整 session reset 的状态范围、权限或版本证明。没有调用该方法当复位，也没有猜 opcode 或调用高权限过程。该调查仅表示本候选没有已验证的全量复位，不能推断服务器不存在其他 reset 能力。

实库必需验证包括正常 autocommit 的 fresh 行可见性、ReadCommitted 事务、未提交写入在独立观察连接上不可见、物理 close 销毁一个 socket、fresh 行为确认未提交写入消失和主键事务锁释放。旧 reader/prepared 有成功正控制；物理 close 后必须拒绝旧对象，且没有新增发送/socket。每次新连接独立验证身份与 Schema。

临时表按[官方管理表 §9.2.5](https://eco.dameng.com/document/dm/zh-cn/pm/management-table.html)使用唯一名字和 `ON COMMIT PRESERVE ROWS`：旧会话存在的行在新物理会话为零；结构会保留，最后精确 DROP。创建语句如被当前 profile 拒绝，保留数字错误并标 `not_proven`，不猜“权限不足”，也不计为完整临时状态测试通过。

schema/isolation/autocommit 条目表示已观察的新会话基线，未证明在同一会话内 reset。role/授权、语言、会话变量、session 级命名锁及 package/procedure state 保持 `not_proven`。主键事务锁释放不冒充 session 锁复位。[官方定义语句 §3.14](https://eco.dameng.com/document/dm/zh-cn/pm/definition-statement.html)提供仅本会话有效的时区/NLS 设置，[官方函数](https://eco.dameng.com/document/dm/zh-cn/pm/function.html)给出 `SELECT SESSIONTIMEZONE FROM DUAL`：探针记录时区 baseline、设置 +9:00、确定效果后 close，并独立 fresh 回读 baseline；NLS 日期格式采用相同流程，但语言仍未探测，整个 NLS/语言条目保持 `not_proven` 并另附 date_format subset。无变化或受服务器 SQL 拒绝则保留未证明。只有 FailureInfo 确认为 Server/ServerReported、有数字 ServerErrorNumber、ConnectionReusable=true 且连接 Open 才允许可选能力拒绝；通信、超时或 Broken 一律失败。

决策固定为 `session_reset_verified=false`、`reuse_policy=discard`、`measured_reused_sessions=0`。即使只执行 SELECT，也不宣称可安全复用或减少握手。offline 只验证 exact package/public API，所有能力未证明且 `integration_pending`；real 缺连接返回 3，必需 case/清理/包冻结一致性失败返回非零。

probe.json 每次阶段切换持久化安全 checkpoint；状态仍为 started、accepted=false，直到所有必需门通过。外部 launcher 900 秒截止后终止独立子进程组，留下 checkpoint 中唯一 owned object 名和 `requires_checkpoint_recovery`，超时绝不视为验收。恢复仍由独立验证者以 TEST fresh identity、精确对象名进行，不查询或删除其他 Schema。普通失败 finally fresh 清理：创建前确认随机名字不存在，尝试 DDL 后即使 ACK 丢失，也只查并清理本次该精确名字；work error 保留非零。最后另一新连接确认两个随机对象都不存在。

源码尚未经本轮独立 Low 编译/执行，本文描述验证契约，不是验收结果。
