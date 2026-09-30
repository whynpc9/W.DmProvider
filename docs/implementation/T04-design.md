# T04 不可变配置、默认值与能力守卫

日期：2026-09-29。状态：已按本设计验收，见 [T04 报告](reports/T04.md)。前置 T03 已验收。依据 [S02](v1/specs/02-api-and-configuration.md)。

## 配置边界

公开 Builder 可编辑，连接在构造/赋值时取得独立的内部 `DmConnectionSettings`。设置只包含不可变值和只读副本；不得持有可变 Builder、EPGroup 或全局配置引用。属性字典提供给旧协议层时每次复制；协商/会话状态与用户设置分开保存。

`DmConnectionSettings.Parse(string)`、Builder 构造及自身字符串 setter 使用严格、事务性解析。别名统一到语义键，等价重复允许、冲突重复拒绝；保留值的大小写和引用/转义，不把密码和 Schema 转小写。未知键、格式错误和不支持的非默认选项抛固定安全异常，不附原值或原始 inner exception。

.NET 10 的 `DbConnectionStringBuilder.ConnectionString` setter 非虚方法，基类通过虚 `Clear` 和 indexer 逐项写入，无法由派生类统一保证整次赋值原子性。为确保基类赋值也不会绕过冲突验证，indexer 使用严格赋值：同义键重复等价允许，改值需先 `Remove`；typed 属性继续支持替换。自己的字符串 setter 事务性更新。实测基类会回滚 `ArgumentException`（含越界异常）到原有效配置，允许继续消费该原状态；`NotSupportedException` 路径不回滚，必须将 Builder 标为不可消费，直到 Clear 或成功重新赋值。测试分别覆盖两种路径，禁止笼统宣称基类原子。禁止访问基类私有字段或依赖调用栈猜测。

直接 indexer 校验失败也可能进入不可消费状态，不能用后续 Remove 解除；“先 Remove 再改值”指在尝试冲突赋值前操作。typed 属性的预校验失败不破坏旧配置。统一恢复方式为 Clear 或成功的派生字符串整体赋值。

另一个框架限制是基类字符串 setter 会在调用派生索引器前丢弃部分无引号空值，派生类无法判定原文是否包含这些键。自有解析器保留空值并拒绝空数值；连接的字符串入口始终走该严格解析器。文档不得把基类上转型赋值也描述为具有相同的原文保留能力。

## 默认值、单位与暂未实现选项

Host/User/Password 无默认值，认证不完整在任何 LOGIN 前拒绝；缺少 host 也不得偷偷连接 localhost。标准 `DmConnection.ConnectionTimeout` 返回秒，旧 Builder `ConnectionTimeout` 及 connect/pool/socket 字符串键维持毫秒。新 typed TimeSpan 属性按文档精确换算，负值/超出旧层可表示范围/无法保持单位的值拒绝。命令默认 30 秒，显式 0 为无限；无连接的新命令也使用 30。

默认 TransportSecurity=RequireTls。T07 尚未提供完整 TLS 路径，本轮默认策略在 socket 前明确拒绝；调用方必须显式选择 PlaintextAllowed 才能使用已授权测试实例。服务器要求 TLS 时同样拒绝，不能落到旧的恒真证书回调或静默降级；未验证的本机加密库模式不可悄悄激活。这些检查是能力守卫，不是 T07 已完成的声明。

Pooling、statement cache、自动重连、读写分离、ambient/distributed enlistment、HA、压缩、SkyWalking、服务文件与旧日志等未验收功能不得被非默认选项启用。公开高级入口（包括 Bulk/FLDR/native）也要有直接守卫，不能仅检查连接字符串。默认池参数可以保留供配置模型描述，池操作本身仍不支持。其他尚未接入执行层的限额（frame、物化 LOB、chunk 等）记录为固定默认/后续阶段项，非默认值拒绝，不能声称已实现资源限制。收包限额属于 S04/T06，TLS 属于 S04/T07，超时预算与真正异步属于 S08/T13–T14，池化属于 S09/T15–T16，LOB 属于 S10/T17。

## 会话接入与输出

只有 Closed 状态可以替换设置，包括 Schema 等旁路属性。Open 原子选定 snapshot 并标为 Connecting，避免另一个调用在联网期间改配置；失败释放资源并返回明确状态。完整执行租约和 Reader 所有权属于 T05。

成功打开后，PersistSecurityInfo=false 时 ConnectionString 和旧 Password getter 隐去秘密，关闭后仍保持该规则；显式重新赋值按新配置重新计算。重开和 Clone 使用内部 snapshot，不从脱敏串重建认证。Builder 的明确配置读取可取得密码，`ToRedactedString` 和内部设置的字符串表示必须安全；禁止把原始配置用于日志。

停止加载环境服务文件、连接级配置写进程全局字段、对含口令连接串做稳定 hash，以及共享 `filter.next` 链。保留兼容类型时不允许把非空 filterHead 注入当前 ADO 路径；旧日志不再作为活动输出路径。握手与错误处理需保留结构化代码但不输出凭据。

## 验证与分工

- Sol High settings：Builder、内部配置表/解析器/不可变设置、TransportSecurity enum、旧默认值来源。提供选项支持/弃用/拒绝表。
- Sol High integration：Connection/Command 接入、Legacy session adapter、过滤器和高级入口守卫、握手安全守卫。记录破坏性变更和禁用入口。
- Sol High tests：自动化配置测试、真实库 probe、独立 T04 构建/运行脚本与新 API 差异记录，不修改实现或 fixture。
- Luna Max：合成解析、单位、冲突、异常脱敏 canary 和能力开关数据，不连接数据库。
- Sol Low：稳定版后的独立测试/真实库验证，负例与证据；缺陷交编码者修复。
- 主 agent：设计裁决、review、T04 报告/进度。

离线覆盖所有入口、等价/冲突别名、quoted values、IPv6、Unicode、单位上下界、Clear/Remove/失败恢复、默认安全策略、未实现开关及直接入口拒绝、配置不互相影响和脱敏。真实库仅由新 T04 probe 显式采用 PlaintextAllowed；验证基本查询/事务、打开期间禁止设置、关闭重开保留认证、公开配置脱敏与无测试对象残留。不改变 local secret 或历史 O/R/T03 消费者以迁就新默认。

T01/T02 原实现及来源保持只读。T03 的产品文件开始有意演进，其历史 hash 应对照验收时 Git 基线 `8e42481`，不重写历史证据或初次导入清单；T04 新增 API/行为差异单独记录。此阶段不发布包，不默认提交或推送。
