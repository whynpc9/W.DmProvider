# 首轮 agent 交接入口：T01 基线固化

原始规范包已迁入。先读 [`v1/AGENT-START.md`](v1/AGENT-START.md)、[`v1/README.md`](v1/README.md)、[`v1/SOURCES.md`](v1/SOURCES.md)、[`v1/tasks.json`](v1/tasks.json) 和 T01 所引用的 spec。本文件保留此前准备阶段的摘要；正式任务范围、依赖与报告要求以 v1 为准。

本轮只做 T01：固定样本、下游实际资产和验证环境的证据。不要重做已完成的 net9.0 全量反编译，不修改 `packages/` 与 `decompiled/` 快照，不跨任务实现异步、连接池或 `DbBatch`。T02 的可编译恢复和 T03 的自有包另行执行。

报告至少包含：实际改动文件、执行命令及退出码、样本版本与 SHA-256、下游资产选择与实际加载证据、环境限制、尚未验证的假设。无真实达梦环境时写 `integration_pending`，不得称集成验证通过。若本轮运行 `dotnet`，先设置可写的 `DOTNET_CLI_HOME`、`DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1`、`DOTNET_CLI_TELEMETRY_OPTOUT=1`；构建/测试使用单节点 `-m:1 /nodeReuse:false /p:UseSharedCompilation=false`，SDK 支持时加 `--disable-build-servers`。
