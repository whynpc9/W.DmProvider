# T07 TLS 和 native 契约

状态：T07 已通过独立验收；实际测试范围和边界见 [T07 报告](../implementation/reports/T07.md)。

## TLS 配置

| typed Builder 属性 | 连接串键 | 规则 |
| --- | --- | --- |
| TlsCaCertificatePath | tls_ca_certificate_path | 可选绝对 CA 路径；不指定则使用系统信任。自定义 CA 必须有 CA 约束，存在 KeyUsage 时须允许签证书 |
| TlsClientCertificatePath | tls_client_certificate_path | PFX 或 PEM 客户端证书的绝对路径 |
| TlsClientPrivateKeyPath | tls_client_private_key_path | PEM 私钥绝对路径；须与证书成对。省略则证书文件按 PFX 读取 |
| TlsClientCertificatePassword | tls_client_certificate_password | 可选私钥/PFX 口令，无默认值；不得进入脱敏连接串或错误 |
| TlsRevocationMode | tls_revocation_mode | Online 默认；仅显式 NoCheck 关闭吊销检查，不影响链、用途、有效期、主机名检查 |

同名不带下划线属性名也按 Builder 别名规则处理。文件在 Open 时加载，不在配置构造时访问。原有 SslFilesPath/SslKeyPass 未恢复，不按当前目录、用户名或服务端路径搜索证书。

RequireTls 现会读取 STARTUP，再按经过验证的 wire 模式升级同一 TCP 连接；不再一律在 TCP 之前拒绝。未协商 TLS 和 AUTH_ONLY 在 LOGIN 之前拒绝。PlaintextAllowed 允许 wire 0；若服务端选择 TLS，仍严格验证。wire 1/4 支持完整 TLS，wire 2/3/5/6 和未知值拒绝。在已测 DM 8.1.4.6 中，服务器配置 5 返回 wire 4，不能据此推断其他版本的 wire 5 兼容性。

业务 I/O 全程保持在已验证的 TLS channel，至少 TLS 1.2；不重新拨号，不设置恒真证书回调，不在认证失败后回退明文。认证和后续初始化沿用原 ConnectDeadline。

## 证书资源和平台

macOS 的 PFX 私钥不能以 EphemeralKeySet 导入。本实现显式使用 .NET 的临时 keychain 机制，不指定 PersistKeySet 或 MachineKeySet；它在使用期间会写磁盘，最后引用释放时由运行时清理，异常进程退出的清理依赖运行时/OS。其他平台使用 EphemeralKeySet。输入 PFX/PEM 文件本身仍由应用管理。[微软文档](https://learn.microsoft.com/en-us/dotnet/standard/security/cross-platform-cryptography#read-a-pkcs12pfx)。

本机测试 CA 没有 CRL，真实测试显式使用 NoCheck，并单列证据；这不是产品默认值变化，也不代表 Online 吊销网络路径已完成实库验证。链检索期限配置受剩余连接预算限制，平台内部证书检索的取消行为仍依赖 .NET/OS。

## native

dmcyt、dmfldr、dmcalc 及第三方 cipher 的旧直接导入入口保持 NotSupported。唯一预备 loader 使用 NativeLibrary/SafeHandle，只接受受信规范绝对路径并声明必需导出；native callback 执行期间持有 SafeHandle 引用，避免并发 Dispose 提前卸载。库加载/导出错误不包含认证材料或原始路径异常。

loader 的错误及释放测试不等于已支持任何原生 cipher、FLDR 或计算能力，产品不打包这些二进制。
