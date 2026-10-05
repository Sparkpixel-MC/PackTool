<div align="center">

[English](README.md) | **简体中文**

</div>

# Resource-Sync PackTool

> 把 Minecraft 资源包目录打包成 **RSSYNC3 加密 `dist/`** 的 Windows 桌面工具，产物可直接托管到任意静态服务器，配合 [resource-synchronization](#相关项目) 服务端做资源包同步。

RSSYNC3 打包逻辑与 Java 参考实现（`cn.ymjacky.drm.AssetEncryption` / `cn.ymjacky.io.LocalMetadata`）**逐字节一致**，可互相替换使用。`metadata.json` 记录的是**加密后 dist 文件**（即客户端实际下载的文件）的 sha1/size，客户端完整性校验可直接通过；由于每个加密文件内嵌随机 GCM nonce，同一输入重复打包产生的 metadata 每次不同（但均有效）。

<div align="center">

[![.NET](https://img.shields.io/badge/.NET-8.0_WPF-5C2D91?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows_x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)](LICENSE)
[![Releases](https://img.shields.io/badge/Releases-Latest-blue?style=for-the-badge)](../../releases)
[![Issues](https://img.shields.io/badge/Issues-Welcome-green?style=for-the-badge)](../../issues)

</div>

---

## 目录

- [功能](#功能)
- [下载与使用](#下载与使用)
- [从源码构建](#从源码构建)
- [一致性验证](#一致性验证)
- [项目结构](#项目结构)
- [RSSYNC3 文件格式](#rssync3-文件格式)
- [相关项目](#相关项目)
- [许可证](#许可证)

---

## 功能

- **一键打包** —— 选择（或拖入）资源包目录，一键打包为 RSSYNC3 加密 `dist/`
- **口令生成** —— 一键生成 32 字节 Base64 加密口令 **wrapKey**（AES-256-GCM + PBKDF2 密钥包裹）
- **服务器绑定** —— 可选写入 `pack.mcmeta` 的 `zbx_rpu_server_lock` / `zbx_rpu_server_bind` 服务器绑定字段
- **开箱即用配置** —— 打包完成后自动生成游戏服 `resource-synchronization-server.json` 与客户端 config 配置片段，一键复制
- **实时进度** —— 实时进度条 + 逐文件进度显示；完全自绘深色标题栏，无系统样式残留

---

## 下载与使用

前往 [Releases](../../releases) 下载 `PackTool.exe`（自包含单文件，无需安装 .NET 运行时），直接运行：

1. 选择 ① 原始资源包目录（可拖拽文件夹）
2. 选择 ② 输出目录（生成 `dist/` 与 metadata）
3. 填写或一键生成加密口令 wrapKey —— **与服务端 `syncChannel.wrapKey` 必须一致**
4. *（可选）* 填写服务器绑定 lock / 地址
5. 点击 **开始打包**，把输出目录内容上传到任意静态托管，再将配置片段中的 `syncChannel.url` 改为实际地址

---

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)（无需 Visual Studio）：

```bash
# 调试运行
dotnet build PackTool
dotnet run --project PackTool

# 发布自包含单文件 exe（完整框架）
dotnet publish PackTool -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o release/self-contained

# 发布框架依赖版（体积小，需已安装 .NET 8 桌面运行时）
dotnet publish PackTool -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o release/framework-dependent