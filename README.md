# W.DmProvider

面向达梦数据库的自有 ADO.NET Provider 调查与开发仓库。已基于 **DM.DmProvider 8.3.1.47463** 完成 **R1（T01–T12）** 的限定开发验收：来源、独立包、配置、会话、传输、TLS、命令/Reader、参数类型、本地事务与隔离级别，以及固定 EF Core 10.0.12 下游的实际候选包验证。当前是开发候选；支持范围见 [R1 兼容矩阵](docs/compatibility/R1.md)，真异步、取消、池化等后续任务尚未启动。

## 从哪里开始

- [逆向报告](docs/reverse/README.md)：官方样本的结构、协议、类型、批量装载和已发现限制。
- [net9.0 对照](docs/reverse/net9-verification.md)：与 net8.0 反编译结果的静态差异。
- [EF 适配待做清单](docs/ef-adapter-backlog.md)：与下游 EF Core 10 适配直接相关的驱动限制。
- [实现规范 v1](docs/implementation/v1/README.md)：13 份特性 spec、25 项任务、98 条验收要求及 agent 交接入口。
- [开发准备](docs/implementation/README.md)：规范包迁入记录和当前本地基线。
- [本地达梦环境](docs/implementation/local-dameng.md)：专用测试账号、secret 加载和使用边界。
- [T01 实施报告](docs/implementation/reports/T01.md)：来源固化与官方 O 探测已验收；重跑入口见 [工具命令](eng/T01.md)。
- [T02 实施报告](docs/implementation/reports/T02.md)：内部恢复版 R 已从源码构建并完成九类 O/R 对照；[恢复命令](tools/RestoreBaseline/README.md)、[对照命令](eng/T02.md)。
- [T04 配置和能力变更](docs/compatibility/T04-breaking-changes.md)：安全默认值、脱敏与暂不支持入口。
- [T03 实施报告](docs/implementation/reports/T03.md)：独立 W 源码与候选包、API 清单及实际 nupkg 消费已验收；[包验证命令](eng/T03.md)。
- [T04 实施报告](docs/implementation/reports/T04.md)：不可变配置、默认值和能力守卫；[测试命令](eng/T04.md)、[配置边界](docs/compatibility/T04-options.md)。
- [T05 实施报告](docs/implementation/reports/T05.md)：会话、Reader/LOB 所有权与并发隔离；[测试命令](eng/T05.md)、[wire 入口清单](docs/implementation/T05-wire-inventory.md)、[兼容性边界](docs/compatibility/T05-session-ownership.md)。
- [T06 实施报告](docs/implementation/reports/T06.md)：受控传输、精确帧边界和统一期限；[测试命令](eng/T06.md)、[字段证据](docs/implementation/T06-frame-layout.md)。
- [T07 实施报告](docs/implementation/reports/T07.md)：严格 TLS、原生资源守卫与真实模式矩阵；[配置边界](docs/compatibility/T07-security.md)、[独立本机 TLS 环境](docs/implementation/local-dameng-tls.md)。
- [T08 实施报告](docs/implementation/reports/T08.md)：执行计划、多结果和有限清理；[兼容性变化](docs/compatibility/T08-command-reader.md)、[测试及实际包消费](eng/T08.md)。
- [T09 实施报告](docs/implementation/reports/T09.md)：精确 decimal/unsigned、参数类型来源与无损时间；[兼容性变化](docs/compatibility/T09-types.md)。
- [T10 实施报告](docs/implementation/reports/T10.md)：本地事务、提交结果、保存点和 DDL 边界；[兼容性变化](docs/compatibility/T10-transactions.md)。

- [T11 实施报告](docs/implementation/reports/T11.md)：隔离级别 SaveChanges 根因修复与实际包公开入口验收；[兼容性变化](docs/compatibility/T11-isolation.md)。

- [R1 review 修复](docs/implementation/reports/R1-review.md)：SQL 注释词法、NULL 整数、CLOB 长度探测及命令解绑的独立回归。
- [T12 / R1 实施报告](docs/implementation/reports/T12.md)：298 项驱动离线、307 项 EF 单元、76 项功能、4 项脚本、4 项规范切片和 1 项 CLI 均通过；[离线 CI 与包审计](eng/T12.md)、[下游接入工具](tools/DownstreamAcceptance/README.md)。

`packages/` 和 `decompiled/` 是来源样本及反编译参考，不是产品源码。新驱动应有独立的程序集身份和命名空间。规范是待实施设计，不表示相关能力已经完成。
