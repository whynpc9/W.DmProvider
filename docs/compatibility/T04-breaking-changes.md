# T04 开发候选包：配置与能力变更

此文记录 T04 源码当前的公开行为。T04 已通过限定范围的独立验收，见 [任务报告](../implementation/reports/T04.md)；不代表 R1 或发布可用。

## 连接与配置

- `DmConnection` 配置在构造、ConnectionString 赋值时解析为独立快照。只有 `Closed` 可替换，`Open()` 进入网络路径前发布 `Connecting`，期间的配置 setter 抛 `InvalidOperationException`。
- 不再提供默认管理员账号或口令。缺 Host、User、Password 时，在联网前以固定错误拒绝。
- 默认 `TransportSecurity=RequireTls`；T07 TLS 验证尚未完成，默认 Open 在 socket 前抛 `NotSupportedException`。本地授权测试实例须显式设置 `PlaintextAllowed`。服务端协商原 TLS/原生加密模式，或要求旧口令/消息加密时均在旧 cipher 初始化前拒绝；旧的接受任意证书路径不再使用。
- `DmConnection.ConnectionTimeout` 返回标准秒，非零毫秒向上取整；旧连接串 `connect_timeout` 仍按毫秒输入。`DmCommand.CommandTimeout` 默认 30 秒，显式 0 仍代表无限。
- 成功 Open 后，默认 `PersistSecurityInfo=false` 的公开 `ConnectionString` 与 `Password` 不显示口令；关闭或后续重新打开失败后继续脱敏，直到显式重新赋值。Clone 和再次 Open 使用私有快照中的原始认证信息；Clone 继承公开脱敏状态。
- `Schema` 作为独立设置，仅 Closed 可设置；非空 Schema 始终作为单个双引号标识符执行 `set schema`，内部双引号翻倍，保留输入的精确大小写；空 Schema 不发送该语句。旧 `schemaSensitive=false` 的不加引号行为不再使用。物理 catalog 切换不支持。连接串别名、单位、冲突、未知键与不支持值由严格解析器处理；Builder 的配置读取仍可读取密码，诊断需使用 `ToRedactedString()`。

## 暂不开放的入口

Pooling、statement cache、自动重连、读写分离、Enlist、HA、压缩、SkyWalking、服务文件和旧日志选项不能通过非默认值启用。`EnlistTransaction`、Reconnect、FLDR、DmFldrExport 和两代 BulkCopy 直接拒绝；底层 `DmFldrDllCall` 从 public 改为 internal。所有旧 `IFilterInfo.filterHead` 只接受 `null`；共享 `BaseFilter` 链不构造。`DmTrace`、`Logger` 和 `LogWriter` 的旧输出路径为 no-op，非 None 的 `DmTrace.Level` 设置拒绝。`DbProviderFactory.CanCreateBatch=false`，`CanCreateDataSourceEnumerator=false`。

这些守卫保留了部分兼容类型和签名，但不承诺这些功能的行为。T05 统一执行所有权、T07 TLS、S08/T13–T14 异步与 S09 池化仍属于后续任务。
