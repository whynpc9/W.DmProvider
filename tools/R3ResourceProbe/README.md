# T18 diagnostics 与 TLS资源子门

只消费新冻结的精确PackageReference，记录包/DLL SHA/MVID与工具/fixture输入hash。T17shared大值和恢复仍pending；本门只覆盖implementation/offline/TLS资源，不能宣称完整R3或production release通过。

offline运行七个独立进程首次初始化场景：normal、ShouldListenTo、InstrumentPublished、Sample、ActivityStarted/Stopped、measurement抛错。每个进程先确认dispatcher未初始化，再用最小独立scripted allocation/SET TRANSACTION/ACK channel走真实公开Open/BeginTransaction/CommitAsync，断言Committed、单commit请求、原许可归零、callbacks失败被隔离、AsyncLocal秘密不流入。它们是真实公开Activity/Meter出口与业务API的离线协议测试，不是真实数据库。DiagnosticsTests另覆盖public Commit EOF→Unknown、原token首因、慢callback/4096满队列drop、10k租约/多轮128registry、格式器/上下文和旧cachedgetter中性范围。无wire且无明确完成/失败的旧getters可以不导出，不强迫Complete来改变取消语义。

TLS预声明budget固定：warmup60秒，measurement600秒且≥500完成create/use/close循环；并发≤4，5秒采样≥121点，停止业务后静默30秒再3点（间隔5秒）；总进程900秒，无强制GC。每cycle40KiB Blob stream/旧array、公开query/Begin/commit或rollback，含Clear，另真实queue-cancel/Clear/Dispose控制。只TEST/TLSwrapper，隔离本机RequireTls且无CRL测试CA显式NoCheck；本轮唯一T18R_<20hex>表精确cleanup/fresh absence。

每点实际采样RSS、GC heap/allocated、threads/FD/handle、pool/registry/queue、创建关闭/网络bytes/LOBchunks。平台不支持的native指标为null，并由validator报告unavailable，不填0。原始121+3样本独立validator严格核：相对warmup基线RSS≤256MiB、heap≤128MiB、thread≤16、FD≤16且quiescent FD≤baseline+8；60秒窗口后半/前半median增RSS≤64MiB、heap≤32MiB；后半threads/FD每100cycle线性增≤1/1。最后pool、socket、queue归零、registry≤128、queue≤4096，正常listener drop/callbackfailure为0。不调阈值/缩时长/减载；预算失败即非零。

业务诊断只原子数字和不可变完成记录，listener通过真实Activity/Meter统计固定instrument/有限enum标签。不存SQL/参数/用户/Schema/endpoint/locator/异常/TraceState/baggage。公开formatter仅安全枚举/代码/数值，unknown exception不输出message/type。callback/source init失败和慢回调不能影响确认Commit或Unknown；饱和drop明示。每新连接验证TEST/Schema，实际network hook只数字记录async路径sync0；这个hook仅网络证明，不冒充公开diagnostics。

累计count采用ObservableCounter读取process原子值，export queue只span/hist；collector保存每固定tag桶最新累计数，不累加重复poll，quiet桶sum必须等core truth。late-listener/overflow恢复也不能丢created/closed总数；显式RecordObservableInstruments的用户callback异常属于调用方，不能要求Core替外部SDK调用万能catch。固定15个实际instrument由源码集合严格验证，未知动态名称/tag拒绝且不写原值。

命令与退出码、源码/包前后hash、依赖、safe stdout/stderr/checkpoint保留。High准备源码后整体冻结，Low单窗口执行；没有执行前不预填pass。超900秒需按checkpoint的本轮精确名字恢复，旧失败不覆盖。
