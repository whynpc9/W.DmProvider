# 来源、基线和证据等级

本包是设计规范；没有在本次编写过程中恢复编译驱动、运行DM实例或执行EF回归。仓库静态报告与测试代码被用于定义入口、待验证问题和下游契约；仓库记录的历史测试结果不是本次重跑结果。

源码引用固定到已审阅commit。外部.NETAPI按.NET10文档及runtime v10.0.0；实现时必须用实际锁定SDK编译确认API形状。

## 驱动仓库

- **R01** net9.0对照报告（静态记录）：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/net9-verification.md
- **R02** EF限制登记（包含静态结论和待动态验证项）：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/ef-adapter-backlog.md
- **R03** 网络协议报告（协议字段需按实际代码/版本复核）：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/02-network-protocol.md
- **R04** 类型与LOB报告：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/03-type-system.md
- **R05** 事务恢复源码：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/decompiled/net9.0/Dm/DmTransaction.cs
- **R09** 精确包元数据：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/packages/extracted/DM.DmProvider.nuspec
- **R10** Reader恢复源码：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/decompiled/net9.0/Dm/DmDataReader.cs
- **R13** Bulk/FLDR调查：https://github.com/whynpc9/W.DmProvider/blob/860988296cb95deeb600328cb7bcd2160237a94f/docs/reverse/04-bulkcopy-fldr.md

## 下游EF契约

- **R06** 更新SQL测试：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/test/W.EntityFrameworkCore.Dameng.Tests/DamengUpdateSqlGeneratorTests.cs
- **R07** TimeSpan绑定与文本读取：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengTimeSpanTypeMapping.cs
- **R08** 隔离级别功能测试（含已知失败断言）：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/test/W.EntityFrameworkCore.Dameng.FunctionalTests/DamengIsolationLevelFunctionalTests.cs
- **R11** 兼容性与历史验证记录：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/docs/compatibility.md
- **R12** DateTimeOffset文本读取：https://github.com/whynpc9/W.EntityFrameworkCore.Dameng/blob/8dab4c0205f21fae4c8ca06fcbc39450eae2fe42/src/W.EntityFrameworkCore.Dameng/Storage/Internal/DamengDateTimeOffsetTypeMapping.cs

## .NET官方契约

- **F01** DbCommand.ExecuteDbDataReaderAsync默认同步fallback：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbcommand.executedbdatareaderasync?view=net-10.0
- **F02** DbDataReader.ReadAsync默认行为：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatareader.readasync?view=net-10.0
- **F03** DbDataSource职责：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbdatasource?view=net-10.0
- **F04** Stream.ReadExactlyAsync语义：https://learn.microsoft.com/en-us/dotnet/api/system.io.stream.readexactlyasync?view=net-10.0
- **F05** SslClientAuthenticationOptions/TargetHost：https://learn.microsoft.com/en-us/dotnet/api/system.net.security.sslclientauthenticationoptions?view=net-10.0
- **F06** EF连接恢复与提交不确定性：https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency
- **F07** .NET10 DbTransaction源码（Save/Release/异步默认实现）：https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Data.Common/src/System/Data/Common/DbTransaction.cs
- **F08** DbBatch定义及provider差异：https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbbatch?view=net-10.0

## 引用如何使用

R类描述“已有源代码/报告/测试所显示内容”，不自动证明所有服务器部署表现相同。F类仅描述框架契约，不证明达梦实现了该能力。S01–S13中的默认值、架构选择、状态机、异常形状、任务顺序、验收指标都是本产品建议的设计决策，不是冒充官方API已提供。
