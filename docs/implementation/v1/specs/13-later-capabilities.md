# S13 — 后续能力及进入实现的门槛

状态：research-gated。依赖：稳定R2/R3；不阻塞核心驱动。任务：T24。

## 1. 原生服务端取消

需要证据：取消请求是同连接还是控制连接、目标session/statement身份、请求/响应格式、认证权限、服务器仍执行时的同步恢复机制、取消后事务状态。必须用可控长查询、即将结束查询、已结束查询和旧statementid重用测试。没有这些证据，S08的AbortPhysicalSession就是正式可用实现，不把它写成原生取消。

禁止仅依据CMD_CANCLE常量构造64字节消息。原生取消若最终不能确认恢复，仍退为关闭物理会话而非自动重连。

## 2. 快速装载/FLDR

上游DmBulkCopy、DmBulkCopy2+FldrStatement、DmFldrExport有不同路径，不能统称同一个bulk能力。[R13]

进入条件：普通数组绑定/事务/结果错误可用；FLDR消息格式与版本明确；默认无敏感行明文日志；native资源路径可测。单独修setMaxRows恒抛、indexOption恒值、ByteArrayBuffer跨节点边界、失败free/成功泄漏；若这些代码在核心路径可达，边界正确性必须提前处理，不借“后续FLDR”拖延。

验证：批次大小/页边界、宽行、LOB/Unicode、约束/索引、错误行位置、取消、失败后部分持久化、是否绕过普通事务及重建索引/约束责任。没有证明不能承诺原子bulk或把它默认替代EF SaveChanges。

## 3. Prepare 与语句缓存

Prepare正确性可在S05支持；缓存是另一个优化。每个statement属于固定session/generation、SQL、参数metadata与影响语义的schema/设置。reset/DDL/参数类型变化、错误与连接断开均可能使缓存失效，先保守失效。

缓存容量有界、归还/丢弃恰好一次；reader持有期间不能把statement放回缓存。Lob/复杂类型内部借句柄也遵守S03。并发/异常路径无句柄增长后才打开默认缓存。

## 4. 高可用、自动重连、读写分离

先只支持显式Open中的未认证地址候选尝试。运行中故障恢复需要新连接；不能把旧事务/会话身份移植到新socket。

读写路由不能只看SQL首词：WITH、CALL、带副作用函数、SELECT FOR UPDATE等都需要保守处理。事务、保存点、临时表、会话变量与identity回读必须session affinity。未知语句走主库/拒绝，不能“可能是读”就送备库。

Failover的RPO、事务结果未知、读一致性、拓扑改变/凭据隔离都需要实际多节点环境；没有环境不能承诺HA。

## 5. 单资源ambient与分布式事务

后续单资源TransactionScope支持必须覆盖异步flow、Complete/Dispose、超时、连接close延迟归还和并发。事务身份不以GetHashCode作为唯一键，使用稳定事务标识+完整相等/对象引用语义。

多资源升级/XA/DTC是独立项目，要求协调器、持久化决策日志、prepare/commit/rollback/recovery、进程崩溃和in-doubt恢复、OS限制说明。两个本地事务依次Commit不是分布式事务。未完成前Promote/Enlist明确失败，不伪装成功。

## 6. 复杂类型、特殊加密、跨平台和AOT

ARRAY/Class/RefCursor/XDEC/Geometry需要独立参数与Reader/资源测试，递归深度/总字节上限和类型metadata生命周期。不要把数据编码能roundtrip自测当完整服务器互操作。

native/GMSSL/第三方cipher需要合法部署的精确RID资产与真实server安全模式测试。新架构预留接口不代表已经纯托管跨平台。

NativeAOT/trim是单独门槛：处理反射配置、资源和native加载、序列化、动态调用；先保证普通JIT .NET10。不要为了标记IsAotCompatible消除告警却不运行产物。

## 7. 每个研究任务的统一交付

提交 `docs/investigations/<feature>/`：原始问题、协议/公开文档来源、probe代码、环境manifest、实际结果、失败/未知、能否进入src以及推荐后续测试。不支持是合法研究结果，伪造已支持不是。
