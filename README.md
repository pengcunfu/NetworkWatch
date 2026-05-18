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

```bat
dev.bat
```

或：

```bash
cd d:\Projects\DevTools\NetworkWatch
dotnet run
```

发布独立可执行文件：

```bash
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## 说明

- 流量统计基于 Windows IP Helper API（`GetPerTcpConnectionEStats`），主要覆盖 **TCP** 连接；UDP 可显示连接，但系统不提供同等粒度的逐连接字节计数。
- 建议 **以管理员身份运行** `dev.bat` 以启用完整流量统计（ETW + TCP 统计）。
- 部分系统进程可能无法统计流量（状态栏会显示「流量采样: 成功数/可统计数」）。
