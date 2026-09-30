# T07 独立本机达梦 TLS 测试实例

此实例只用于 T07 TLS/原生安全边界的真实互操作测试，与现有 `172.16` 测试实例和 `.local/secrets/dameng-test.env` 完全隔离。唯一容器名为 `wdm-provider-tls-t07`，Docker 使用 `linux/amd64`，仅绑定宿主机 `127.0.0.1:15236` 到容器 `5236`。持久文件位于被 Git 忽略的 `.local/t07/`。本流程不访问原管理 secret、其他容器或其他数据库实例。

官方介质 `dm8_20241022_x86_rh6_64_single.tar` 的 SHA-256 为 `97cc976b618e0eb75a20c831fb4e258c74ccc574ffa3e59b187c0c9bb90f019c`；镜像标签为 `dm8_single:dm8_20241022_rev244896_x86_rh6_64`，架构 `linux/amd64`，入口 `/opt/startup.sh`。镜像已由项目协调人加载。镜像内 `/opt/dmdbms/bin/server_ssl` 使用 `ca-cert.pem`、`server-cert.pem`、`server-key.pem`，客户端用户目录使用 `ca-cert.pem`、`client-cert.pem`、`client-key.pem`。

按顺序运行 `python3 tools/LocalTlsEnvironment/provision.py inspect`、`prepare`、`start-plaintext`、`create-user`、`generate-certs`、`enable-tls`。`status` 和 `verify-tls` 可只读核对容器状态、配置及官方 `disql` 的 TLS TEST 登录。每一步都只作用于该唯一容器；已有容器不会被覆盖。启动时写入带精确容器 ID 与镜像 ID 的 `0600` 本地 manifest；后续操作核对 ID、镜像、端口及三个 bind 路径。当前首次容器是在加入 label 前启动的，已使用首次启动返回 ID 执行 `adopt-created`，未删除重建；后续新建会带专用 scope label。脚本为新实例生成随机 SYSDBA bootstrap 口令和 `WDM_PROVIDER_TEST` 口令，只写入 `.local/secrets/dameng-tls-bootstrap.env` 与 `dameng-tls-test.env`（文件 `0600`，父目录 `0700`）。CLI 参数、标准输出和 Git 不包含口令、连接串、私钥或认证报文。Docker 与 disql 原始输出只留在进程内，写入忽略目录的阶段日志前按秘密字面值和凭据行脱敏。

证书由本机 OpenSSL 新建，CA、服务端和客户端私钥只在忽略目录内，文件模式 `0600`。服务端证书 SAN 含 `DNS:localhost` 与 `IP:127.0.0.1`，客户端证书 CN 为 `WDM_PROVIDER_TEST`。先在明文模式创建测试用户，只授予 `CREATE SESSION`、`CREATE TABLE`、`CREATE INDEX`、`CREATE VIEW`、`CREATE SEQUENCE`、`CREATE PROCEDURE`、`CREATE TRIGGER`。之后仅在此实例设置静态 `ENABLE_ENCRYPT=1` 和 `MIN_SSL_VERSION=771`（TLS 1.2），并只重启此容器。官方文档说明 `ENABLE_ENCRYPT=1` 需要双方证书和完整传输加密，配置不完整时服务可能无法启动；[参数说明](https://eco.dameng.com/document/dm/zh-cn/pm/physical-storage)、[双因子与模式说明](https://eco.dameng.com/document/dm/zh-cn/pm/identification-authentication.html)。

新 TLS 探针必须通过 `scripts/with-dameng-tls-test.sh` 加载唯一 TEST 变量，先验证服务端身份与当前 Schema，并只操作本次创建的唯一对象，在 `finally` 清理和独立连接回读。与现有明文 TEST 的回归、官方驱动及 W 新驱动结果分开记录。证书与 loopback `SslStream` 测试只证明 TLS 组件；真实达梦 TLS 互操作以此独立实例的实际连接结果为准。若镜像许可过期、启动失败、证书格式不兼容或 TLS 模式不匹配，应保存脱敏诊断并如实报告，不能关闭验证或更改既有实例来绕过。

本次建立记录（2026-09-29 UTC）：容器仅绑定上述本机端口；纯明文初始化、TEST 用户登录、CA/服务端/客户端证书、`ENABLE_ENCRYPT=1` 和最低 TLS 1.2 均已完成。官方 `disql` 使用显式 `SSL_PATH` 在新 TEST 用户下连接并回读 `USER` 成功。W 随后经独立验收完成 TLS 业务读写、证书拒绝和模式矩阵；模式 5 配置实际返回 wire 4，最终已恢复模式 1，详见 [T07 报告](reports/T07.md)。官方结果与 W 结果分别记录。镜像日志报告默认许可到期日为 **2026-10-13**；到期后复现需有效介质或许可，不绕过期限。

受控模式矩阵只通过 `eng/t07.sh matrix` 操作该已核 ID 的容器。它仅允许临时选择 `ENABLE_ENCRYPT` 1、2、4、5，检查镜像 ID、端口及三个 bind；每次重启等官方 TEST 登录就绪后才运行 W 探针。`finally` 恢复 1 并验证官方与 W 登录。若容器在失败模式下退出，恢复路径在核对同一 ID 和 DATA bind 后只修改该 `dm.ini` 的唯一 `ENABLE_ENCRYPT` 行，并重新启动同一容器；离线模拟测试覆盖此分支。真实矩阵观察到配置 2→报文模式 2（拒绝认证阶段 TLS），配置 4→报文模式 4（W TLS 成功并拒绝未知 CA），配置 5→报文模式 **4**（W TLS 成功）；从未观察到报文模式 5，驱动对它保持明确拒绝。历史失败矩阵保留，不能计入成功。
