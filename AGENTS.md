# 本仓库 agent 规则

## .NET 构建

- 运行 `dotnet` 前设置可写的任务目录 `DOTNET_CLI_HOME`，并设置 `DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`。
- 构建和测试使用单节点 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`；SDK 支持时添加 `--disable-build-servers`（.NET 6 不支持）。
- 如本地 IPC 报 `SocketException (13): Permission denied`，先关闭 build servers，再用上述参数重试。不要例行清空全局 NuGet 缓存。

## 本地达梦开发实例

- 本项目在现有达梦实例中使用两个专用账号及同名 Schema：`WDM_PROVIDER_TEST` 用于自动化集成测试，`WDM_PROVIDER_DEV` 用于手工开发探测。默认只使用 `WDM_PROVIDER_TEST`。
- 本机连接信息只在被 Git 忽略的 `.local/secrets/dameng-test.env`、`dameng-dev.env`、`dameng-admin.env` 中。不要将连接串、口令、原始认证报文、含凭据的异常或环境变量输出到终端、日志、测试制品、Git 或对话。不要把本地 secrets 复制到 CI。
- 测试命令通过 `scripts/with-dameng-test.sh <command> ...` 加载唯一测试变量 `DAMENG_TEST_CONNECTION_STRING`。手工探测才显式加载 dev secret。正常开发、测试和 agent 任务不得加载 SA 管理凭据；新增用户或变更授权另行明确限定操作。
- 只对当前专用 Schema 内本次测试创建的对象进行清理。不得用 SA 运行测试，不得执行跨 Schema 的 DML/DDL、`DROP USER`、`DROP SCHEMA`、`DROP TABLESPACE` 或实例级设置变更。自动化测试使用唯一对象名并在失败时清理自己的对象；不要并行运行会互相删除固定对象名的测试。
- 每次真实库测试先确认连接身份为 `WDM_PROVIDER_TEST`，并记录测试命令、退出码、服务器侧最终状态。无显式测试连接时只跑离线测试，并报告 `integration_pending`。官方驱动、可编译恢复版和新驱动的测试结果分开记录。
- 新驱动的 TLS/明文模式必须显式配置并验证；不能通过关闭证书校验让测试通过。当前连接凭据的使用方式见 [本地达梦环境](docs/implementation/local-dameng.md)。

## 实施分工与任务门槛

- 主 agent 负责设计、范围控制和 code review；Sol High 负责指定任务编码；Sol Low（当前工具中对应用户的 light 档）负责独立测试验证；Luna Max 负责生成合成测试数据。
- 每次只派发有明确文件范围和验收条件的任务。共享工作区时禁止交叉修改对方负责的文件，保留其他未提交工作。修复由编码 agent 完成，再由验证 agent 复核，主 agent 审阅后才关闭任务。
- [v1 规范](docs/implementation/v1/README.md)及其任务 DAG 是原始设计；实际进度以 [progress.json](docs/implementation/progress.json) 和对应任务报告为准。v1 中的初始 `not_started` 不表示已验收任务需要重新执行。
- 默认选择依赖已完成且 activation gate 已满足的最早未完成任务，不把来源固化、恢复编译和行为改进混作一次交付。
