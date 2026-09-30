# W.DmProvider → EF 接入交接契约

## 固定基线

上游官方对照：DM.DmProvider 8.3.1.47463 / net9.0。
驱动调查：860988296cb95deeb600328cb7bcd2160237a94f。
已审阅EF：8dab4c0205f21fae4c8ca06fcbc39450eae2fe42。
实施时若分支更新，记录新的准确commit及契约差异，不覆盖用户更改。

## 候选包信息（由实现任务填写，当前没有候选驱动代码）

PackageId: W.DmProvider
PackageVersion: 尚未构建
SHA-256: 尚未计算
DriverCommit: 尚未实施
TargetFramework: net10.0（目标）
EFIntegrationCommit: 尚未接入
Evidence: spec_only

## 下游需要独立变更的点

1. PackageReference DM.DmProvider→W.DmProvider，lockfile和包缓存隔离。
2. DamengRelationalConnection创建W.Dm.DmConnection。
3. 特定DmParameter/DmDbType判断改为W类型，覆盖Clob/Blob/VarBinary/IntervalDayToSecond。
4. 异常分类识别W.DmException和FailureInfo；Unknown不得沿旧通信错误表自动重试。
5. 测试创建/脚本执行/检查结果的连接工厂同时切换，防止半套官方半套W。
6. 日期时间/TimeSpan的GetString兼容路径先保留，不为换驱动顺便重写类型映射。
7. 测试显式配置传输安全；测试库无TLS时明确PlaintextAllowed，不禁用证书验证。
8. 官方缺陷复现保留在Characterization，已修复问题以Improvement成功测试代替旧失败断言。

## ef-savechanges-v1 必测集合

查询/CRUD、null与Unicode、identity/sequence和额外生成列、0/1影响行及并发冲突、DML+SELECT组合、显式事务、保存点、RepeatableRead/Snapshot拒绝边界、可串行化纯ADO最小复现、LOB/日期时间/INTERVAL、迁移DMSQL块执行与已有脚本；后续Async/Pooling/LOB改动再加相应故障回归。

没有真实故障注入证据，不扩大重试范围；没有批次结果映射变更，EF继续原SingularModificationCommandBatch。更换驱动不等于自动获得EF批处理。
