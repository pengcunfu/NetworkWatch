# NetworkWatch

Windows 桌面网络流量监控工具，按进程展示 TCP/UDP 连接、上传与下载速率。

## 功能

- 列出每个进程的活跃网络连接（TCP / UDP，IPv4 / IPv6）
- 实时显示各进程与连接的下载、上传速率
- 连接详情：本地/远程地址、端口、协议、TCP 状态
- 按进程名、PID、可执行路径搜索过滤

## 运行要求

- Windows 10 或更高版本
- [.NET 10 SDK / 桌面运行时](https://dotnet.microsoft.com/download)

## 构建与运行

双击或在项目根目录执行：

```bat
run.bat
```

PowerShell：

```powershell
.\run.ps1
```

也可手动运行：

```bash
cd d:\Projects\DevTools\NetworkWatch
dotnet run --project src/NetworkWatch
```

发布独立可执行文件：

```bash
dotnet publish src/NetworkWatch -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 说明

- 流量统计基于 Windows IP Helper API（`GetPerTcpConnectionEStats`），主要覆盖 **TCP** 连接；UDP 可显示连接，但系统不提供同等粒度的逐连接字节计数。
- **无需管理员权限**：进程级流量通过 ETW（`Microsoft-Windows-TCPIP`）统计；单连接速率在可能时通过 IP Helper 补充。
- 部分系统进程可能无法统计流量（状态栏会显示「流量采样: 成功数/可统计数」）。
