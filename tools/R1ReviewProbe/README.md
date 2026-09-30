# R1 PR review package consumer

该工具只通过 exact `PackageReference` 消费 `W.DmProvider` nupkg，禁止 ProjectReference。Low 使用新的隔离包源与输出目录，传入 `-p:R1ReviewPackageVersion=<exact-version>`；报告核对实际 DLL informational version，并保存 SHA256/MVID。旧包与修复候选使用相同冻结工具源码，输出分别保存。

所有 dotnet 操作由 Low 统一执行。先设置可写 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`；build/restore 使用单节点 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`，支持时添加 `--disable-build-servers`。

运行编译后的 DLL：`scripts/with-dameng-test.sh <dotnet-path> <R1ReviewProbe.dll> baseline|fixed <safe-report.json>`。只读取唯一 TEST wrapper 环境变量。无变量返回 `integration_pending`、退出码 2。连接显式 PlaintextAllowed、PersistSecurityInfo=false、连接与命令超时 20 秒；每条新连接在任何业务操作前验证 USER、当前 Schema 均为 WDM_PROVIDER_TEST，ServerVersion 为 8.1.5.60。

`baseline` 记录 NULL int、CLOB 长度/partial/zero/剩余读取、forward gap、prepared解绑与reader占用行为。基线 contract failure 为缺陷表征，`baseline_characterized` 不代表旧包正确。基线只运行安全 `SELECT 11 /* /* */ + 31 -- */\n FROM DUAL` 表征当前 profile：首 close 返回 42，nested 返回 11，两种模式均是有效 SELECT；绝不将包含 COMMIT 的复合 payload 发送给旧包。

`fixed` 还检查 active 事务的 `ROLLBACK TO sp /* /* */ ; COMMIT -- */` 在客户端拒绝，事务保持 Active、插入行仍在，随后显式 Rollback，独立连接确认新增行数 0。实际 SELECT 返回 11 时停止 guard 验收并报告需要 root 重新审阅 profile 设计。该 SELECT 只证明当前已验证 profile 的注释语义，不概括所有达梦配置。fixed 的所有 required contract 必须通过。

CLOB 数据采用 Luna 逻辑 fixture 的 `A中e\u0301文`（5 UTF-16 units，保持分解组合符）；supplementary 字符仅在离线 local CLOB 检查，不推断 GB18030 emoji 映射。空字符串实库结果单列 characterization，若服务器映射为 SQL NULL，不把它算作 non-NULL empty CLOB 通过；离线 ReaderReviewTests 独立覆盖本地 non-NULL empty CLOB 长度 0。工具保留当前物化路径，不宣称完成 LOB 流式能力；超过 int.MaxValue 的底层 CLOB 长度明确拒绝，不返回前缀长度。

DDL 前确认随机唯一表不存在，并用 CreateNew 写 `<report>.ownership.json` 注册精确 ownership。finally 只按当前 nonce 注册的唯一表名清理，随后另一新连接核实 USER/Schema/profile 和 USER_TABLES 最终数量 0。宿主被中断时该 ownership 文件用于人工限定收尾；无跨 Schema 操作、无 SA、无固定共享对象。报告仅输出结构化状态、异常类型/安全 number，不输出 exception message、SQL异常、连接串、口令或环境内容。
