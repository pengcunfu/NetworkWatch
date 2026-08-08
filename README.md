# NetworkWatch

Windows 桌面网络监控工具：按进程展示 TCP/UDP 连接与上传、下载速率，并提供端口管理（查看连接、终止占用进程、查看进程详情）。

## 功能

- 列出每个进程的活跃网络连接（TCP / UDP，IPv4 / IPv6）
- 实时显示各进程与连接的下载、上传速率
- 连接详情：本地/远程地址、端口、协议、TCP 状态
- 按进程名、PID、可执行路径搜索过滤
- 一键测速：测量网络下载与上传速度，并显示延迟、抖动与所用节点
- 端口管理：以扁平列表查看本机全部连接，按端口/地址/PID 筛选，右键查看进程详情或终止占用端口的进程
- 浅色 / 深色主题切换，默认浅色并记住偏好

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
- 程序默认以**普通权限**运行，无需管理员权限即可查看连接与 TCP 流量统计。
- 如需启用完整流量统计（ETW 增强），点击界面上的「管理员模式」按钮，UAC 会临时请求提权并重启；关闭该实例后即恢复普通模式。
- 部分系统进程可能无法统计流量（状态栏会显示「流量采样: 成功数/可统计数」）。
- 测速数据来自公共测速节点：下载优先使用 Cloudflare，失败时自动切换备用节点；上传使用 Cloudflare 官方测速端点。
- 端口管理：列出本机所有 TCP/UDP 连接（含归属 PID 与进程名），支持按端口、地址或 PID 筛选；双击或右键「查看详情」可查看进程的 CPU、内存、线程、句柄、命令行等信息；右键「终止进程」直接结束占用端口的进程（多选生效）。终止需要更高权限的进程时，请先在主窗口切换到「管理员模式」。
