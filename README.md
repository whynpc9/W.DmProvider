# W.DmProvider

面向达梦数据库的自有 ADO.NET Provider 调查与开发仓库。已基于 **DM.DmProvider 8.3.1.47463** 完成来源固化、R 基线恢复及 .NET 10 独立 W 开发候选包。当前处于 T03 阶段，安全配置和完整 R1 门槛待后续验收，候选包仅用于本机开发验证。

## 从哪里开始

- [逆向报告](docs/reverse/README.md)：官方样本的结构、协议、类型、批量装载和已发现限制。
- [net9.0 对照](docs/reverse/net9-verification.md)：与 net8.0 反编译结果的静态差异。
- [EF 适配待做清单](docs/ef-adapter-backlog.md)：与下游 EF Core 10 适配直接相关的驱动限制。
- [实现规范 v1](docs/implementation/v1/README.md)：13 份特性 spec、25 项任务、98 条验收要求及 agent 交接入口。
- [开发准备](docs/implementation/README.md)：规范包迁入记录和当前本地基线。
- [本地达梦环境](docs/implementation/local-dameng.md)：专用测试账号、secret 加载和使用边界。
- [T01 实施报告](docs/implementation/reports/T01.md)：来源固化与官方 O 探测已验收；重跑入口见 [工具命令](eng/T01.md)。
- [T02 实施报告](docs/implementation/reports/T02.md)：内部恢复版 R 已从源码构建并完成九类 O/R 对照；[恢复命令](tools/RestoreBaseline/README.md)、[对照命令](eng/T02.md)。
- [T03 实施报告](docs/implementation/reports/T03.md)：独立 W 源码与候选包、API 清单及实际 nupkg 消费已验收；[包验证命令](eng/T03.md)。

`packages/` 和 `decompiled/` 是来源样本及反编译参考，不是产品源码。新驱动应有独立的程序集身份和命名空间。规范是待实施设计，不表示相关能力已经完成。
