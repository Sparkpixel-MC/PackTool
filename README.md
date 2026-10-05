<div align="center">

**English** | [简体中文](README.zh-CN.md)

</div>

# Resource-Sync PackTool

> A Windows desktop tool that packs a Minecraft resource pack directory into an **RSSYNC3 encrypted `dist/`**. The output can be hosted on any static server and used together with the [resource-synchronization](#related-projects) server plugin for resource pack syncing.

The RSSYNC3 packing logic is **byte-for-byte identical** to the Java reference implementation (`cn.ymjacky.drm.AssetEncryption` / `cn.ymjacky.io.LocalMetadata`) — the same input directory produces exactly the same `metadata.json` / `metadata.sha1`, so the two PackTool implementations are fully interchangeable.

<div align="center">

[![.NET](https://img.shields.io/badge/.NET-8.0_WPF-5C2D91?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![Platform](https://img.shields.io/badge/Platform-Windows_x64-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/License-MIT-yellow?style=for-the-badge)](LICENSE)
[![Releases](https://img.shields.io/badge/Releases-Latest-blue?style=for-the-badge)](../../releases)
[![Issues](https://img.shields.io/badge/Issues-Welcome-green?style=for-the-badge)](../../issues)

</div>

---

## Table of Contents

- [Features](#features)
- [Download & Usage](#download--usage)
- [Building from Source](#building-from-source)
- [Consistency Validation](#consistency-validation)
- [Project Structure](#project-structure)
- [RSSYNC3 File Format](#rssync3-file-format)
- [Related Projects](#related-projects)
- [License](#license)

---

## Features

- **One-click packing** — pick (or drag in) a resource pack directory and pack it into an RSSYNC3 encrypted `dist/`
- **Wrap key generation** — one click to generate a 32-byte Base64 encryption password (**wrapKey**) using AES-256-GCM + PBKDF2 key wrapping
- **Server binding** — optionally write the `zbx_rpu_server_lock` / `zbx_rpu_server_bind` server-binding fields into `pack.mcmeta`
- **Ready-to-use config** — after packing, the game server's `resource-synchronization-server.json` and client config snippets are generated automatically, ready to copy
- **Live progress** — real-time progress bar with per-file status; fully custom-drawn dark title bar with no system styling artifacts

---

## Download & Usage

Head over to [Releases](../../releases) and download `PackTool.exe` (self-contained single file, no .NET runtime required), then run it directly:

1. Select ① the original resource pack directory (drag-and-drop supported)
2. Select ② the output directory (where `dist/` and metadata will be written)
3. Enter or one-click generate the encryption password **wrapKey** — it **must match the server's `syncChannel.wrapKey`**
4. *(Optional)* Fill in the server binding lock / address
5. Click **Start Packing**, upload the output directory contents to any static host, and change `syncChannel.url` in the generated config snippet to the actual address

---

## Building from Source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (Visual Studio is not required):

```bash
# Debug run
dotnet build PackTool
dotnet run --project PackTool

# Publish self-contained single-file exe (full framework)
dotnet publish PackTool -c Release -r win-x64 --self-contained true \
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:EnableCompressionInSingleFile=true -o release/self-contained

# Publish framework-dependent build (smaller, requires .NET 8 Desktop Runtime)
dotnet publish PackTool -c Release -r win-x64 --self-contained false \
  -p:PublishSingleFile=true -o release/framework-dependent