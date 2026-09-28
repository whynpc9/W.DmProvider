调查完成。以下为完整分析报告。

---

# DM.DmProvider 8.3.1.47463 网络与协议层逆向分析报告

## 0. 命名空间折叠说明

反编译后 `A\` 目录下 4 个文件是原始包级内部类被 IL 级折叠的结果，对照关系（根据行为推断）:

| 文件：类 | 原始身份 | 职责 |
|---|---|---|
| `A/B.cs:16` 类 `B` | `DmAccess` / "CSI"(DmConnInstance 中字段名 `m_Csi`、getter `GetCsi()`) | 每连接的协议访问对象：收发编解码调度、加解密挂钩、LOB/FLDR 消息收发 |
| `A/B.cs:1045` 类 `b` | `MsgBuffer` | 报文字节缓冲区（默认 32640 字节，小端，可倍增扩容），含全部 head 字段的定偏移读写器 |
| `A/D.cs:14` 类 `D : a` | `DmCommTcpip`（错误信息中仍出现该名，见 B.cs:223) | Socket/SSL 传输层：TCP 连接、SslStream、DH 密钥、收发、CRC |
| `A/A.cs:10` 类 `A` | `DmStatement` | 语句句柄（含 `b A` 请求缓冲 / `b a` 响应缓冲） |
| `A/A.cs:730` 类 `a` | 传输抽象基类 | 仅 2 个 virtual 方法，是传输层唯一正式扩展缝 |
| `A/C.cs:11` 类 `C` | `MsgEncoder`（静态） | 所有请求报文的编码函数 |
| `A/C.cs:610` 类 `c` | `MsgDecoder`（静态） | 所有响应报文的解码函数 |

---

## 1. 核心类清单

| 类（文件） | 职责一句话 |
|---|---|
| `MSG<T>` (Dm/MSG.cs:6) | 新式报文基类：64 字节头模板方法 `encode()/decode()`、CRC 计算、错误检查；子类有 COMMIT、GET_LOB_DATA、SET_LOB_DATA、LOB_TRUNCATE、GET_LOB_LEN、FLDR_* 共 14 个 |
| `B`(A/B.cs) | 连接级收发调度器，持有报文缓冲 `b A` 与传输对象 `D` |
| `b`(A/B.cs:1045) | 小端字节缓冲，offset 0..63 为固定头，自带 head 字段 accessor（如 `K()`=cmd@4、`k()`=bodyLen@6、`L()`=sqlCode@10) |
| `D : a`(A/D.cs) | TCP/SSL socket 封装，含 DH 公钥与两个 `Cipher`(msg/pwd 各一） |
| `Buffer`/`ByteArrayBuffer`(Dm.net.buffer/) | 另一套链表式（Node 双向链表）缓冲抽象，**主要供 LOB/流数据使用，不在主报文路径上** |
| `Dm.net.buffer/Arrays.cs` | CopyOf/Fill 工具（注意 `CopyOf` 只允许缩短，见 §5) |
| `MsgSecurity` (Dm/MsgSecurity.cs:7) | 加密协商中枢：算法/模式位掩码常量、内置 512 位 DH 参数、会话密钥计算、托管 `ICryptoTransform` 工厂 |
| `Cipher` (Dm/Cipher.cs:3) | 加密接口：`Encrypt(byte[],bool genDigest)` / `Decrypt(byte[],bool checkDigest)` |
| `SymmCipher` (Dm/SymmCipher.cs) | 纯托管实现（System.Security.Cryptography)——**全程序无实例化点，死代码** |
| `SymmCipher2` (Dm/SymmCipher2.cs) | 实际使用的实现：P/Invoke 到原生库 **`dmcyt`**(cyt_do_encrypt/decrypt/hash、dm_dh_gen_common_key) |
| `ThirdPartCipher` + `ThirdPartCipherDLL` (Dm/) | 第三方加密 DLL 支持：`cipherPath` 指定 DLL,LoadLibrary + GetProcAddress 绑定 11 个 Cdecl 导出函数；算法 ID ≥ 5000 时启用 |
| `DHGroup`/`DHKey` (Dm/) | BigInteger 实现的 DH 密钥对生成（`ComputeKey` 为死代码，共享密钥实际由 dmcyt 完成） |
| `SymmCipherDesc` (Dm/SymmCipherDesc.cs) | 把算法 ID（高 9 位=算法，低 7 位=工作模式）解析为算法名/密钥长/IV 长/填充 |
| `DmConst` (Dm/DmConst.cs) | 全部协议常量：CMD_*/RET_*/报文头偏移/连接串键名 |
| `Types` (Dm/Types.cs) | JDBC 风格 SQL 类型码（与协议列类型码非同一套，列类型见 DmSqlType) |
| `DBAliveCheckThread` (Dm/DBAliveCheckThread.cs) | 全局单例后台线程，周期性裸 TCP 连接探测服务器活性 |
| `EP`/`EPGroup`/`EPSelector`(Dm/) | 多节点（DSC/RW 集群）连接选择与故障切换 |

---

## 2. 重点问题

### 2.1 连接建立与握手/登录认证完整流程

入口链：`DmConnection.Open()` → `Connect()` → `EPGroup.connect()`(A 的故障切换循环，EPGroup.cs:61)→ `EP.connect()`（改写 ConnProperty.Server/Port,EP.cs:144)→ `DmConnection.do_Open()`(DmConnection.cs:505)→ `new DmConnInstance(this)`(DmConnInstance.cs:119)→ **`new B(m_SendMsg, m_RecvMsg, this)`**，全部握手都发生在 `B` 构造函数（A/B.cs:93-135):

```
B.ctor 顺序（A/B.cs:93）:
 1. A(server)                 // 解析 "host:port" / "[ipv6]:port" (B.cs:299)
 2. new D(server, port, ConnectionTimeout)   // TCP 连接,超时默认 5000ms
 3. A(P_0, P_1)               // STARTUP (cmd=200):  C.A 编码 (C.cs:34) → 收发 → c.a 解码 (C.cs:620)
 4. 若服务器要求 encryptMsg/encryptPwd:
      sessionKey = MsgSecurity.ComputeSessionKey(clientDHKey, serverPubKey)   // B.cs:109, 调 dmcyt 原生 dm_dh_gen_common_key
      encryptPwd → D.A(cipherType, key, cipherPath, hashType, false)  // 建 "密码加密器" D.a
      encryptMsg → D.A(encryptType,    key, cipherPath, hashType, true)   // 建 "报文加密器" D.A
 5. c()                        // SSL 升级 (B.cs:137): 按服务器返回的 Encrypt 字段决定是否包 SslStream
 6. a(P_0, P_1)               // LOGIN (cmd=1):      C.a 编码 (C.cs:61) → 收发 → c.B 解码 (C.cs:687)
```

要点：

- **STARTUP 请求**(C.cs:34-59):cmd=200，携带 compress、是否 LoginEncrypt（请求 DH 交换）、crcBody、协议版本 `msgVersion`(21)、客户端版本字符串 **硬编码 `"8.3.1.3"`**(C.cs:46，与包版本 8.3.1.47463 不一致）、若 LoginEncrypt 则附带 64 字节客户端 DH 公钥（`D.B()`,D.cs:447)。
- **STARTUP 响应**(C.cs:620-685)：服务器返回 `Encrypt`(0=无，1=SSL,2=SSL_AUTH_ONLY,3=GMSSL,4=WITHOUT_AUTH；见 DmConst.cs:1003-1009)、字符集（0=GB18030/BIG5,1=UTF-8,2=euc-kr)、`msgVersion` 取 min、**加密协商三字段**:genKeypairFlag(b@40)、commEncFlag(b@41，值 2 表示还带 hashType)、服务器 DH 公钥（64B)、encryptType（算法 ID)、msgVersion≥17 时还有 `algorithm` 字段。`encryptType==-1` 表示仅加密密码，否则加密整个报文（C.cs:664-675)。
- **加密算法决策**(B.cs:113)：一行嵌套三元表达式——
  - `15≤msgVersion<17`：默认 2052(**AES256_CFB**);
  - `msgVersion<15`：默认 132(**DES_CFB**);
  - `msgVersion≥17`：由服务器 `algorithm` 字段决定，0→DES_CFB，否则 AES256_CFB。
- **密码加密**：登录报文中 user/pwd 用 `D.a`(SymmCipher2，无 digest）加密（C.cs:82-86)。
- **报文加密**：每条消息的 body(64 字节头之后）经 `D.A.Encrypt(..., genDigest:true)`，即 **密文 + 16 字节 MD5（明文）** 拼接（B.cs:186-197 发，B.cs:235-247 收）。
- **DH 参数**:512 位固定素数 p（硬编码于 MsgSecurity.cs:59-68),g=5；客户端私钥由 `RNGCryptoServiceProvider.GetNonZeroBytes` 生成 64 字节（DHGroup.cs:23-35)，共享密钥由原生库计算，输出 128 字节缓冲（实际有效 64 字节，其余为 0)。
- **SSL**:`Encrypt==2` 时 SSL 仅用于认证（握手后立即 `TcpClient.Client=null`，后续走明文）;`1/4` 时全链路 SslStream;`3`(GMSSL 国密）直接 `ThrowUnsupportedException`(B.cs:155-158)。客户端证书：`$DM_HOME/bin/client_ssl/<user>/client-cert.pem`，默认密码 **`"changeit"`**(D.cs:25)，可用连接串 `sslKeyPass`/`sslFilesPath` 覆盖。
- 构造完成后 `m_A`（读超时字段）= `ConnProperty.SocketTimeout`;`msgVersion<10` 时 `lobOffRowLen` 降为 2048(B.cs:125-128)。

### 2.2 报文格式

**64 字节公共头**(`MSG_HEAD_SIZE=64`,MSG.cs:142；全部**小端**):

| 偏移 | 类型 | 含义 | 读取点 |
|---|---|---|---|
| 0 | int | stmtId（语句句柄号） | `b.N(int)` 写 / `b.j()` 读 |
| 4 | short | cmd（消息类型，见下表） | `b.I()/K()` |
| 6 | int | body 长度（不含头） | `b.J()/k()` |
| 10 | int | sqlCode（响应；<0 为错误） | `b.L()` |
| 14 | short | svrMode(0 NORMAL/1 PRIMARY/2 STANDBY) | |
| 16 | int | expand（响应尾附加长度，decode 后跳过，MSG.cs:232) | |
| 18 | byte | compress 标志 | |
| 19 | byte | CRC:XOR(0..18) 或 crcBody 模式时 body 尾追加 4 字节 CRC32 | MSG.cs:298-336 |
| 20+ | | 各 cmd 私有字段（偏移常量完整列在 DmConst.cs:1147-1419，如 `MSG_REQ_STARTUP_PROTOCOL_VERSION=35`、`MSG_RES_LOGIN_HEART_BEAT_TIMEOUT=53`) | |

**消息类型（opcode) 全表**——MSG.cs:52-140 与 DmConst.cs:511-577 合并（后者多出 4 个客户端未实现的）:

| 值 | 常量 | 用途 |
|---|---|---|
| 1 | CMD_LOGIN | 登录认证 |
| 2 | CMD_LOGOUT | 登出 |
| 3 | CMD_STMT_ALLOCATE | 分配语句句柄（登录后第一条消息，B.cs:325) |
| 4 | CMD_STMT_FREE | 释放句柄 |
| 5 | CMD_PREPARE | SQL 预编译 |
| 6 | CMD_EXECUTE | 执行（VerNum≤117506688 即 ≤8.1.2.128 时用） |
| 7 | CMD_FETCH | 取结果集行 |
| 8 | CMD_COMMIT | 提交 |
| 9 | CMD_ROLLBACK | 回滚 |
| 10 | CMD_GET_SQLSATE | （仅 DmConst，未实现）获取 SQLSTATE |
| 11 | CMD_CANCLE | （仅 DmConst，未实现）取消 |
| 12 | CMD_POSITION | （仅 DmConst，未实现） |
| 13 | CMD_EXECUTE2 | 执行（新服务器版本） |
| 14 | CMD_PUT_DATA | 上传 LOB/大参数数据 |
| 15 | CMD_GET_DATA | 取数据 |
| 16 | CMD_CREATE_BLOB | （仅 DmConst) |
| 17 | CMD_STMT_CLOSE / CMD_CLOSE_STMT | 关闭游标语句 |
| 18 | CMD_TIME_OUT | （仅 DmConst) |
| 19/20 | CMD_CURSOR_PREPARE / CMD_CURSOR_EXECUTE | （仅 DmConst) |
| 21 | CMD_EXPLAIN | 执行计划 |
| 23 | CMD_CHECK_TAB_FAST_INS | 快速插入检查 |
| 24 | CMD_GET_DATA_ARR | 批量取数据 |
| 25 | CMD_PWD_CHG | 改密码 |
| 26 | CMD_PUT_DATA2 | 带 token 的上传（msgVersion≥10,C.cs:501) |
| 27 | CMD_CURSOR_SET_NAME | 游标命名 |
| 29-32 | CMD_GET_LOB_LEN / SET_LOB_DATA / LOB_TRUNCATE / GET_LOB_DATA | LOB 操作（MSG<T> 子类实现） |
| 44 | CMD_MORE_RESULT | 取下一个结果集 |
| 52 | CMD_SESS_ISO | 设置会话隔离级别 |
| 53/55/56/61/64/111/112/121/122/123 | CMD_FLDR_* | 快速装载（FLDR）系列 |
| 60 | CMD_XA | XA 分布式事务 |
| 71 | CMD_TABLE_TS | 表时间戳（结果集缓存校验） |
| 90 | CMD_PRE_EXEC | 预执行（仅发送参数定义） |
| 91 | CMD_EXEC_DIRECT | 免 prepare 直接执行 |
| 130-133 | CMD_SET/CLEAR/GET_SUBSCRIBE_* | 订阅日志（CDC) |
| 200 | CMD_STARTUP | 首次握手 |
| 269 | CMD_HEART_TIME_OUT | **服务器心跳包**（收包循环中跳过，B.cs:220、D.cs:548) |

响应头中的语句类型码（RET_*）见 DmConst.cs:579-801(147=COMMIT、160=SELECT、162=CALL、166=SET_SESS_TRAN、251-256=Oracle 格式设置等），在 `c.A(b,A,DmConnProperty)`(C.cs:1252）的大 switch 中分派处理。

### 2.3 发包/收包序列化机制

存在**两套并行机制**:

1. **新式 `MSG<T>` 模板方法**(MSG.cs:190-241):`encode()` = `beforeEncode`(buffer 复位、预留 64 字节头）→ `doEncode()`（子类写 body)→ `afterEncode`（回填 stmtId@0、cmd@4、bodyLen@6);`decode()` 同理。收发由 `B.A<A>(MSG<A>)`(B.cs:161）驱动：encode → 可选加密 → setCRC → 发 → 收 → checkCRC → 解密 → decode。
2. **旧式静态编解码对**:`C.*`（编码）/ `c.*`（解码）直接操作 `b` 缓冲。所有高频路径（prepare/execute/fetch/commit/rollback/login）都走这套。

物理收发（`D` 类）:
- 发（D.cs:491-518 `A(b,int,bool crc,bool encrypt)`)：可选加密 body → 长度检查 ≤ **536870912(512MB)** → crcBody 则追加 4 字节 CRC32 并回填长度，否则写头 XOR 字节 → `A(byte[],timeout,len)` 发送（SSL 则 SslStream.Write，否则 Socket.Send,D.cs:520)。
- 收（D.cs:535-590)：先读至多 32640 字节；`while (cmd==269)` 丢弃心跳包重读；按头中 bodyLen 判断是否读全，未读全则扩容并 `ReadFully` 补齐（D.cs:601-614);CRC 校验；可选解密。
- `b` 缓冲读写全部是手工小端位操作（A/B.cs:1121-1462)；序列化原语为：定长整数、UB1/2/3/4、`int 长度前缀+bytes`(`B(byte[])`)、`short 长度前缀+bytes`(`a(byte[])`)、NTS 字符串。参数绑定编码在 `C.A(int,b,DmParameterInternal[],A,int)`(C.cs:274)：每参数 `ioType(1B) + type(4B) + prec(4B) + scale(4B)`，数据区 `ushort len + bytes`,len=65534 表 NULL、65529 表走 PUT_DATA2 旁路。

### 2.4 超时、心跳、keepalive

- **连接超时**:`connect_timeout`，默认 **5000ms**(DmOptionHelper.cs:40)。`D` 的 TCP 连接用 `BeginConnect + WaitOne(timeout)`(D.cs:318-322)。
- **读/写超时**：每次收发前现设 `socket.SendTimeout/ReceiveTimeout`(D.cs:522-523、B.cs:210-211)。旧式路径用传入超时——绝大多数调用传 `SocketTimeout`（连接串 `socketTimeout`，默认 0=无限）；但 **`SocketTimeout` 属性会被服务器心跳超时覆盖**:`m_HeartBeatTimeout != 0 ? m_HeartBeatTimeout : socketTimeout`(DmConnProperty.cs:651-661)，而 `HeartBeatTimeout` 来自登录响应（秒×1000,C.cs:719)。即**服务器可单方面决定客户端 socket 读超时**。
- **不一致点（bug 级）**:`MSG<T>` 新式路径（COMMIT/LOB/FLDR）收发都用 `ConnectionTimeout` 而非 `SocketTimeout`(B.cs:202-204 发、B.cs:211 收），意味着 LOB 操作默认 5 秒就会超时，与大对象传输预期冲突。
- **心跳**：客户端**从不主动发心跳**；服务器空闲时下发 cmd=269 的 `CMD_HEART_TIME_OUT` 包，收包循环读到就丢弃重读（B.cs:213-220、D.cs:541-548)——它唯一的副作用是重置 ReadTimeout 倒计时，充当"服务端保活+客户端假死检测"。
- **TCP keepalive**:**全驱动没有设置任何 socket keepalive/TcpNoDelay 选项**(grep 无 KeepAlive/SetSocketOption/NoDelay)。长连接空闲断链只能靠服务器心跳包或 DBAliveCheckThread 发现。
- **DBAliveCheckThread**(DBAliveCheckThread.cs)：全局单例后台线程，仅当 dm_svc.conf 中 `dbAliveCheckFreq>0`（默认 0，关闭）时启动；每周期对每个 (server,port) 新建一条裸 TCP 连接（超时 `dbAliveCheckTimeout`，默认 10000ms)，失败则 `GetCsi().d()` 强制关闭该连接。
- **活性探测**:`D.D()`(D.cs:631)`Poll(100µs)+Receive==0` 判断半开；`DmConnInstance.IsSocketConnected()`(DmConnInstance.cs:543）用 `Poll(0)+Peek`。
- **错误映射**:SocketException ErrorCode==10060(WSAETIMEDOUT)→ `ECNET_COMMAND_TIME_OUT`(B.cs:271-274);IOException → 先 `E()` 关连接再抛通讯错误（B.cs:178-183)。
- **故障切换/重连**：连接级由 `EPGroup.connect` 完成（SwitchTimes+1 轮、轮间隔 SwitchInterval=200ms,EPGroup.cs:61-98)；运行期自动重连由 `ReconnectFilter`(`doSwitch`/`autoReconnect` 开启时挂入 filter 链）在所有操作上包一层 try/catch-reconnect。语句句柄在切换后置脏，再使用时报 `ERROR_MASTER_SLAVE_SWITCHED`(A/A.cs:694-698)。

---

## 3. 关键常量表（节选）

**加密算法位掩码**(MsgSecurity.cs:11-43)：低 7 位=工作模式（ECB=1,CBC=2,CFB=4,OFB=8)，高 9 位=算法（DES=128, DES3=256, AES128=512, AES192=1024, AES256=2048, RC4=4096, MD5=4352)；组合值 DES_CFB=132、AES256_CFB=2052；第三方算法 ID 起点 `MIN_EXTERNAL_CIPHER_ID=5000`。

**SSL/加密模式**(DmConst.cs:1003-1009):ENCRYPT_MODE_SSL=1、SSL_AUTH_ONLY=2、GMSSL=3（客户端不支持）、WITHOUT_AUTH=4。

**长度限制**(MSG.cs:46-50 / DmConst.cs):MSG_DEFAULT_LEN=32640（缓冲初始/单次首读）、MSG_LIMIT_LEN=512MB、MSG_PARAM_MAX_LEN=64MB、单参数行内上限 65535、LOB 每报文 32000(`maxLobDataLenPerMsg` 默认 32000,DmOptionHelper.cs:186)。

**协议版本**(MSG.cs:8-44)：当前 VERSION=21(VERSION_NEW_SERVER_VERSION)；关键里程碑：v10 PUT_DATA2、v11 全报文加密、v15 SSL_UPDATE、v17 DETERMINE_ENCRYPT（服务器决定算法）、v21 新服务器版本格式。`VerNum` 打包规则：主.次.修订.构建 → `major<<24|minor<<16|rev<<8|build`(C.cs:1556-1654),117506688 = 8.1.2.128(EXECUTE2 分界，DmConst.cs:357)。

**网络相关连接串键**(DmConst.cs，括号内为默认值）:`server`(localhost)、`port`(5236)、`user/password`(SYSDBA/SYSDBA)、`connect_timeout`(5000ms)、`socketTimeout`(0，受 HeartBeatTimeout 覆盖）、`compress`/`compress_id`(0/ZIP)、`login_encrypt`/`loginEncrypt`/`communication_encrypt`(false)、`cipher_path`（第三方加密 DLL 路径）、`sslFilesPath`、`sslKeyPass`、`dbAliveCheckFreq`(0)/`dbAliveCheckTimeout`(10000)、`switchTimes`(1)/`switchInterval`(200ms)、`epSelector`(WELL_DISTRIBUTE)、`maxLobDataLenPerMsg`(32000)、`loginMode`、`rwSeparate` 等。

---

## 4. 可扩展 / 可 patch 点（核心目的）

**正式扩展缝（virtual/interface)**:
1. `A.a` 传输基类（A/A.cs:730-739):`internal virtual void A(b,int,bool,bool)` 发送 + `internal virtual b a(b,int,bool,bool)` 接收，`D` 是唯一实现。可在此注入自定义传输（UDS、代理、mock)。
2. `MSG<T>`(MSG.cs)：`beforeEncode/afterEncode/beforeDecode/afterDecode/doEncode/doDecode` 均为 virtual/abstract，新增私有命令（如自定义监控消息）最直接的模板。
3. `Cipher` 接口（Dm/Cipher.cs)+ `ThirdPartCipherDLL`(Dm/ThirdPartCipherDLL.cs)：官方预留的加密插件点——连接串 `cipher_path` 指向实现 11 个 `cipher_*` 导出的 DLL，算法 ID≥5000 即被采纳（D.cs:478-488)。
4. `BaseFilter` 链（Dm.filter/):`BaseFilter.CreateFilterChain(this, ConnProperty)`(DmConnection.cs:130)，日志/重连/RW 分流都以 filter 插入，是合法的行为注入点。

**设计弱点 / 可 patch 的缺陷**:
5. **SSL 证书校验完全关闭**:`D.A(object, X509Certificate, X509Chain, SslPolicyErrors)` 永远 `return true`(D.cs:372-375)，且 `AuthenticateAsClient` 的 targetHost 硬编码 `"DmProvider"`(D.cs:358)。补强点：换成真正校验链与主机名的回调。
6. **SSL 协议版本陈旧**：仅 `Tls11|Tls12`(D.cs:358)，无 TLS1.3。
7. **DH 仅 512 位**、且 g=5、p 硬编码（MsgSecurity.cs:57-75);CBC/CFB/OFB 使用**固定 IV** `DEFAULT_IV`(MsgSecurity.cs:45-51，即 ASCII 32..63);digest 用 MD5。整体加密强度停留在 DM 服务器兼容水平，可在 ThirdPartCipher 层面替换为国密/AES-GCM。
8. **`MSG<T>` 新式路径不走连接锁**:`B.A<A>(MSG<A>)`(B.cs:161）没有 `lock(this.m_A)`，而旧式路径 `B.A(b,b,int)`(B.cs:249-282）有。同连接上 LOB/FLDR/COMMIT 与普通 SQL 并发时会交织写 socket —— **真实线程安全隐患**，patch 时给新式路径补同一锁。
9. **超时不一致**：新式路径收发硬用 `ConnectionTimeout`(B.cs:202-211),LOB 大传输易误超时；且 `SocketTimeout` 被服务器心跳值覆盖（DmConnProperty.cs:655）的语义值得重新审视。
10. **TCP 连接建立的怪异实现**(D.cs:304-335):`BeginConnect+WaitOne(timeout)` 成功后，**又 Close 并重新同步 `Connect()`（无超时）**——双倍耗时、第二步不可控阻塞，典型的历史遗留写法，patch 时应改为单次带超时的连接。
11. **`D` 的 CRC32 表是实例字段**(D.cs:38-272，每连接 16KB)，而 `MSG.CRC32` 是 static(MSG.cs:385)——重复内存浪费，应合并为 static。
12. **收包长度无上限校验**：收包按服务器声明的 bodyLen 直接扩容（`b.B()`,A/B.cs:1111，倍增策略 `len*2+extra`)，恶意/损坏对端可声明 512MB 触发大分配；发送侧才有 `ECNET_MSG_LEN_TOO_LONG` 检查（D.cs:502)。
13. **`ByteArrayBuffer` 的边界 bug**:`setLong` 用 `>=1` 判断后写 8 字节（ByteArrayBuffer.cs:765-774，应为 >=8);`setUB3` 在跨节点放不下时**直接返回 3 但不写任何数据**(ByteArrayBuffer.cs:796-805，静默丢数据）;`Arrays.CopyOf` 用 `Array.Copy(original, array, newLength)` 实现（Arrays.cs:7-12),newLength 大于原长时抛异常（与 Java 语义不符）。
14. **资源/句柄问题**:`ThirdPartCipherDLL.Dispose()` 是空方法（ThirdPartCipherDLL.cs:102-104，句柄永不 FreeLibrary);`ThirdPartCipher.getHashSize` 中 `Marshal.StringToBSTR("")` 泄漏 BSTR(ThirdPartCipher.cs:38);`ThirdPartCipherDLL` 只 P/Invoke `Kernel32.dll`,**Linux/macOS 下第三方加密必然失败**;`D` 靠终结器 `~D()` 关 socket(D.cs:648-651),SslStream/TcpClient 无 Dispose。
15. **死代码**:`SymmCipher`（托管加密）与 `MsgSecurity.newCipher`、`DHGroup.ComputeKey`、`b.A(string[])`(A/B.cs:1082，一个残留的自测 main 式方法）、`MSG.convertToOracleCode`（恒等 stub,MSG.cs:293)。patch 时可删或复活为"无 dmcyt 依赖的纯托管加密回退"。
16. **`MSG.getCmdName()` 会先抛 KeyNotFoundException** 再走到 null 分支（MSG.cs:660，`CMD_NAMES[cmd]` 而非 TryGetValue)，且表内缺很多 cmd（如 269)。
17. **错误信息中的繁简转换 hack**:`decodeErrorInfo`(MSG.cs:243-264）里 `euc-kr→GB18030`、非 UTF-8→BIG5 的硬编码替换，与编码注册（DmConnProperty.cs:1212-1232，只注册了 5 种）耦合，扩展编码支持时需同步改。

---

## 5. 有趣实现细节

- **驱动自报版本与包版本不符**：协议里写死 `"8.3.1.3"`(C.cs:46)。
- **实际加解密全在原生库**:`dmcyt`(dll/so）承担 AES/DES/3DES/RC4、MD5 及 DH 共享密钥计算（SymmCipher2.cs:16-35)；托管实现只是摆设。net8.0 托管驱动仍依赖原生库意味着"跨平台纯托管"并不成立——且 `ComputeSessionKey` 还要求 dmcyt 能加载 OpenSSL(`cyt_load_ssl_lib`,MsgSecurity.cs:80-82)。
- **会话密钥 128 字节缓冲**:`dm_dh_gen_common_key` 输出到 128 字节数组（MsgSecurity.cs:79-86),512 位 DH 实际只填前 64 字节，其余为 0,AES256 取前 32 字节——即有效熵受限于 512 位 DH。
- **3DES 密钥构造**:16 字节密钥 + 前 8 字节复读 = 24 字节 K1K2K1(SymmCipher2.cs:52-57)。
- **digest 追加在密文尾部且是明文的 MD5**(SymmCipher2.cs:66-97)，解密时先比对再解——有 MD5 长度/碰撞上的理论弱点，且 digest 不提供密钥认证（非 HMAC)。
- **`Encrypt==2`(SSL_AUTH_ONLY）的实现极其取巧**：握手成功后把 `TcpClient.Client` 置 null，让 `TcpClient.Connected` 变 false，后续读写自然回落到裸 socket(D.cs:366-369 配合 D.cs:524-532/594-599)。
- **压缩协商了但没实现**:STARTUP 交换 compress/compress_id(ZIP/SNAPPY 常量都在，DmConst.cs:335-347)，响应头 18 偏移也有 compress 标志，但全驱动没有任何压缩/解压代码；`B` 构造里只在 `Compress>0` 时算了个 `isLocalHost` 布尔并存入私有字段再无人读取（B.cs:103-106)——半成品。
- **服务器可下发 Oracle 日期格式与回话级 NLS**(C.cs:759-793),`RET_DDL_ALTSESS_DATEFMT` 等 RET 码（251-256）会在任意语句执行后顺手更新会话格式（C.cs:1419-1441)。
- **登录响应里服务器会推 standby 拓扑**(StandbyIp/Port/Num,C.cs:747-752)、上次登录 IP/时间/失败次数、Guid（结果集缓存键的一部分）、RW 分离标志——会话上下文远比一般驱动丰富。
- **端口默认 5236，账号默认 SYSDBA/SYSDBA**(DmOptionHelper.cs:28-34)。
- **语句句柄号 = 服务器返回，客户端仅校验 <0 时转无符号**(A/A.cs:301-307),`MAX_STMT_NUM=2000`。
- **错误码特例 -7036**(EC_RN_EXCEED_ROWSET_SIZE）在多处被当作"非错误"跳过（MSG.cs:271、C.cs:1499),-7049 为语句超时（DmConst.cs:1081-1083)。
- **`NetTaste/` 目录与网络无关**：是一个 Coco/R 生成的词法分析器框架（Buffer/Scanner/Parser/SymbolTable)，服务于 SQL 解析（escape 处理、参数提取），不是网络缓冲。

---

### 给后续"补强"工作的优先级建议（基于上述发现）

1. **补锁**:`B.A<A>(MSG<A>)` 加与旧路径相同的 `lock(m_A)`（问题 8，最可能踩坑）。
2. **修传输层**:TCP 连接双连（问题 10）、SSL 证书校验（问题 5)、TLS1.3（问题 6)、`MSG<T>` 路径超时来源（问题 9)。
3. **如需摆脱 dmcyt 原生依赖**：复活 `SymmCipher` 托管实现挂到 `D.A(...)`（问题 15)，注意修 `MsgSecurity` 固定 IV 问题（问题 7)。
4. **健壮性**：收包长度上限（问题 12)、`ByteArrayBuffer` 两个边界 bug（问题 13)、`ThirdPartCipherDLL` 的跨平台与释放（问题 14)。
