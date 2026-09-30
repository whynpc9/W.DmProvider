# S04 — 传输、协议边界、TLS 与原生库

状态：实施规范。优先级：P0/P1。依赖：S03。任务：T06、T07；异步接通见 S08。

## 1. 目标与切分

现有 `A/D.cs` 是 socket/SSL，`A/B.cs` 汇聚旧消息与 MSG<T>，`A/C.cs` 编解码，`Dm/MSG.cs` 是另一消息体系。报告记录 64-byte header、小端、STARTUP/LOGIN 以及版本相关 LOB/Execute 差异。[R03]

将其切成 `DmTransport`（字节收发）、`DmFrameReader/Writer`（帧边界与校验）、`DmProtocolSession`（请求/响应生命周期）、按消息的 codec。先共用受控入口，保留已验证 codec，后续按消息迁移，不按文件体积重写。

第一版不强制 System.IO.Pipelines。Socket+NetworkStream/SslStream 加受控 Memory<byte> 缓冲已足够；只有 benchmark 证明需要才追加 Pipelines。

## 2. 连接与握手

DNS → 单次 TCP 建连 → STARTUP 协商 → 按协议升级 TLS/建立认证 cipher → LOGIN → 必需初始化，全部共享一个 ConnectTimeout deadline；每一步失败释放前面全部资源。DNS 返回多个地址可在同一预算内逐一尝试，不能每次尝试重置总预算。

不得复刻成功 BeginConnect 后关掉 socket 再同步 Connect 的行为；一个成功 Open 只有一个最终认证 TCP 会话。配置目标主机用于证书验证，不能改为解析后的任意字符串或硬编码 DmProvider。

连接过程中取消不进入池；失败时外部 connection 回到 Closed。构造函数不做网络 I/O；Open/OpenAsync 才建立会话。

## 3. 帧读取规则

算法要求（具体字段按 opcode+协商版本验证，而不是盲信报告表格）：

1. 读取**精确**的已验证头长；正常 TCP Read 可能只返回部分数据。可使用 ReadExactly/ReadExactlyAsync 或等价循环。[F04]
2. 在分配 body 前解出合法长度；拒绝负值、overflow、超过配置上限的总帧；对 `header+body`、`count*width` 使用 checked。
3. 精确读取 body；中途 EOF 是截断协议，不是正常结果结束。
4. 按协商规则做 checksum/CRC、解密和长度验证，再交给响应解码器。不能把不同版本的校验流程混用。
5. 解密后的长度、结果列数、行计数、字符串长度、LOB locator 长度再次校验；嵌套复杂类型在未支持时拒绝，不无界递归。
6. heartbeat 是独立已识别帧，消费完整帧后继续等待业务响应；不得重置命令总期限，也不得把它当作任意错误帧忽略。

报告某些 header 字段出现重叠偏移；必须根据源代码与消息类型确认它们的条件含义，不能直接生成一个把所有字段当作同时有效的 packed struct。

Stream.WriteAsync 通常表示整个给定缓冲写完；若直接用 Socket.Send/SendAsync，必须循环处理 partial send。零进展不能无限循环。Memory/Span 在 await 边界的所有权清晰，绝不在归还池化内存后继续解码。

默认 64 MiB 是本产品保护值，不是声称服务器协议最大值；配置提高必须受经过验证的协议硬上限和内存预算限制。未实现压缩时协商不宣称支持；服务端强制不支持模式时明确失败。不得先无界解压再检查大小。

## 4. TLS 策略

`TransportSecurity=RequireTls`：要求完整业务流量 TLS，必须验证证书链、有效期、目标主机名；仅登录加密或仅认证阶段 TLS 不满足该策略。服务器只提供 AUTH_ONLY 或不支持 TLS 时，在发送敏感认证材料前明确失败。

`PlaintextAllowed`：明确允许非 TLS DM 会话；如果实际协商 TLS 仍严格验证证书。它不等于 `TrustServerCertificate=true`。

使用 `SslClientAuthenticationOptions`，`TargetHost` 取配置 DNS/IP 名；系统信任链为默认，可显式注入受管理自签 CA；没有恒 true 回调。[F05]

建议使用 OS 协商协议，握手后检查不低于 TLS1.2；不写死只启用旧版本。撤销检查默认 Online，医院离线环境可以显式配置 NoCheck 并写明风险，不能因网络不可达偷偷放行。自定义验证不是关闭主机名验证的捷径。

证书/私钥路径和密码必须显式可控；无 changeit 默认私钥口令。认证失败立即关闭并传播异常，不 catch+Console.WriteLine 后继续登录。

## 5. 旧加密与原生依赖

`dmcyt`/`dmfldr` 的调用在 Native 内集中管理，使用 NativeLibrary/SafeHandle/明确调用约定；不得散落 kernel32 LoadLibrary。产品不嵌入未核实可分发原生二进制。[R03]

不自行把既有 DH/报文 cipher 替换为 AES-GCM 等不同 wire 格式；协议互操作不是单方面改算法。旧消息加密默认不冒充现代安全保障：缺乏已验证实现或原生库时明确说明依赖并失败，不降级为明文。针对服务器要求的已存在模式可以作为显式兼容路径，需独立兼容性与安全记录。

仅加载应用部署的受信绝对路径/已配置原生目录；不能搜索当前工作目录或服务器提供任意路径。每个失败路径验证句柄恰好释放一次；库加载失败不打印认证数据。

未知 native 模式、GMSSL、第三方 cipher 先明确不支持，不能因为上游有 DllImport 就默认开放。

## 6. 验收

NET-01：header 每次仅到达 1 byte，正常拼帧；随机 body 分片同样正确。
NET-02：header/body 截断、负长度、加法溢出、超大长度、错误 CRC 均快速失败且无大分配/复用。
NET-03：连续 heartbeat 不能绕过命令超时。
NET-04：DNS/TCP/TLS/LOGIN 任一步失败或取消无 socket/native handle 泄漏。
NET-05：假 stream 只允许 ReadAsync/WriteAsync，异步路径不能调用同步 I/O（R2验收）。
TLS-01：正确链与正确主机名成功；未知 CA、过期、错主机名失败。
TLS-02：RequireTls 遇明文/AUTH_ONLY 不发送 LOGIN；没有“失败后降级成功”。
TLS-03：PlaintextAllowed 不让无效证书通过；离线撤销策略只按显式配置。
NAT-01：错误 RID、缺库、缺导出符号错误可诊断，释放次数正确，秘密不出现在异常。

真实 TLS/原生 cipher 互操作必须针对实际 DM 环境运行；只通过假 SslStream 不等于已支持达梦加密部署。
