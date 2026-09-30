# T07 — TLS 与原生资源边界

状态：T07 已实施并通过独立验收，实际范围见 [报告](reports/T07.md)。依赖 T06 已独立验收；其 308 个实现输入及附件已保存到 `.local/t06/accepted-source/`。

## 交付和分工

主 agent 设计、审阅和环境范围控制。沿用 Sol High transport lane 实现 TLS 通道及配置，frames lane 实现握手与 native 守卫，tests lane 实现证书测试和专用本机实例；Luna Max 仅生成场景参数，Sol Low 独立验收。T06 冻结后才开始修改生产代码。本次不修改现有共享远程测试实例的配置或读取其 SA secret。

## 配置契约

在严格 Builder / 不可变 settings 中加入：

- `TlsCaCertificatePath`：可选的绝对 CA 文件路径，默认使用系统信任。
- `TlsClientCertificatePath`：可选 PFX 或 PEM 客户端证书。
- `TlsClientPrivateKeyPath`：PEM 私钥路径，与 PEM 证书成对；为空时证书文件按 PFX 处理。
- `TlsClientCertificatePassword`：PFX 或加密 PEM 私钥的可选口令；没有隐含默认口令。沿用连接字符串、Clone、异常与日志的凭据脱敏规则。
- `TlsRevocationMode`：默认 Online，仅显式 NoCheck 可关闭吊销检查；不因网络失败自动降级。

证书加载发生在 Open，不在 Builder 构造中读文件。旧 SslFilesPath/SslKeyPass 不因同名近似而自动恢复；不搜索当前目录、用户名目录或服务器指定位置。加载的证书/私钥对象有明确所有者和释放路径。

PFX 导入采用明确的平台分支：Windows/Linux 使用 EphemeralKeySet；macOS 使用不带 PersistKeySet/MachineKeySet 的 DefaultKeySet，由 .NET 创建临时磁盘 keychain，证书对象仍由 TLS scope 持有和释放。macOS 不支持 EphemeralKeySet，不能把此分支描述成完全不落盘；最后引用释放和进程异常退出后的清理取决于运行时/OS。[微软跨平台加密说明](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography#read-a-pkcs12pfx)。

## 传输与握手

STARTUP 响应完整读取并校验后，解释已核实的 TLS 模式字段，再决定是否升级同一个 TCP socket。TLS 升级使用 `SslClientAuthenticationOptions`，TargetHost 来自配置目标主机，协议协商后至少 TLS 1.2；默认系统链或显式受信 CA，保留主机名和有效期验证，不设置恒真的证书回调。

升级中的 SslStream 登记为待清理资源；使用原 ConnectDeadline 及 Close 令牌。成功后只在仍持有同一旧 channel 时交换，失败和 Close 都只清理捕获的 socket/SSL/证书对象一次，在状态锁外释放。同步 Open 可以同步等待当前握手任务，不能据此声称 T13 的端到端异步已经完成。

RequireTls 遇明文或 AUTH_ONLY 在 LOGIN 之前拒绝。PlaintextAllowed 允许明确的明文模式；如果服务器要求 TLS，仍进行同样的证书验证。AUTH_ONLY、TLCP/GMSSL 和未实现的消息 cipher 保持明确拒绝。完整 TLS 模式的声明须由真实服务器响应和互操作验证决定，不能仅根据枚举或文档推断支持。

## 证据来源和模式边界

恢复源码的 STARTUP response 从头偏移 20 的 4 字节小端字段取得 Encrypt；旧逻辑区分 1/4 的持续 SSL 和 2 的认证阶段 SSL。密码/消息 cipher 的协商字段独立，不能用它们证明业务流量 TLS。

厂商文档将 0 定义为无 TLS，1 为完整 TLS 且双方验证，2 为仅认证，4 为完整加密但不验证对方，5 为客户端验证服务器的完整 TLS，6 为 TLCP。W 对其支持的完整 TLS 模式仍强制客户端证书链和主机名验证；配置值与 wire 字段的对应另行实测。[达梦用户标识与鉴别](https://eco.dameng.com/document/dm/zh-cn/pm/identification-authentication.html)、[通信加密](https://eco.dameng.com/document/dm/zh-cn/pm/communication-encryption.html)。

本机 DM 8.1.4.6 已观测到配置 1→wire 1、2→2、4→4、5→4。产品只开放已实测的 wire 1/4；wire 5 保持 NotSupported，不能把服务器配置 5 的成功解释为 wire 5 已验证。

## 独立本机环境

当前已有 TEST 实例用于明文回归。为完成 TLS 实测，新增隔离的本机 Docker 实例，固定容器名 `wdm-provider-tls-t07`，仅绑定 `127.0.0.1:15236`，文件在 `.local/t07/`，新增测试账号/同名 Schema 和最小权限。bootstrap 凭据与 TLS 测试凭据分开，存入新增 ignored local secret 文件；不复用现有 SA。

官方域名介质：`https://download.dameng.com/eco/dm8/dm8_20241022_x86_rh6_64_single.tar`，806840320 字节，SHA-256 `97cc976b618e0eb75a20c831fb4e258c74ccc574ffa3e59b187c0c9bb90f019c`。已导入 tag `dm8_single:dm8_20241022_rev244896_x86_rh6_64`，linux/amd64；它是另一个明确版本的验证环境，不替代原实例基线。创建和重新配置仅限这个新专用实例，不操作其他容器或共享数据库。

## native 与验收

原生库通过受控 NativeLibrary/SafeHandle 管理，仅允许显式受信绝对路径。缺 RID、缺库、缺导出和初始化失败有脱敏错误及恰好一次释放证据。未验收的 cipher/FLDR/计算库不因 loader 重构而开放；不将原生二进制打入产品包。

单测使用标准 CertificateRequest 在内存生成证书，验证正确链/名称、未知 CA、过期、错误 SAN、mTLS、策略默认与显式撤销设置、升级取消和资源释放。loopback SslStream 只是 TLS 机制证据；最终必须另有真实 DM 完整 TLS 登录/查询、无降级和独立清理证据。所有真实参数与证书模式记录准确，未验证项保持 pending。
