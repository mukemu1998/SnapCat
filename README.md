# SnapCat

<p align="center">
  <img src="src/SnapCat.App/Assets/SnapCat.png" width="120" alt="SnapCat 项目图标" />
</p>

<p align="center">
  <strong>面向 Windows 的自由框选截图、OCR 与翻译工具</strong>
</p>

<p align="center">
  围绕“先框选，再识别，再翻译或固定”的工作流设计，强调清晰截图、快速操作和常驻托盘体验。
</p>

<p align="center">
  <a href="https://github.com/mukemu1998/SnapCat/releases">下载发布版</a>
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-2d7dff" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4" />
  <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-111827" />
  <img alt="Version 0.1.0" src="https://img.shields.io/badge/Version-0.1.0-22c55e" />
</p>

## 项目简介

SnapCat 是一个面向 Windows 的开源截图工具，聚焦自由框选、本地 OCR、接口翻译和截图固定这几条高频链路。它不是以复杂编辑器为中心，而是围绕“截图后马上处理”这个动作来组织交互，尽量把常用操作压缩到更短的路径里。

## 核心能力

| 能力 | 说明 |
| --- | --- |
| 自由框选截图 | 以自由框选为核心入口，适合快速截取局部内容。 |
| 等待式操作流程 | 默认先框选，再决定固定、OCR、翻译、识别二维码或保存。 |
| 截图固定到屏幕 | 截图可直接固定在桌面上方显示，便于对照查看。 |
| 本地 OCR 识别 | 使用本地 OCR 引擎识别截图文字，兼顾隐私和离线可用性。 |
| 接口翻译 | OCR 后可继续调用兼容 OpenAI 的接口进行翻译，适合接入 DeepSeek 等服务。 |
| 托盘与快捷键 | 支持托盘常驻、右键菜单和三组独立快捷键工作流。 |

## 适用场景

- 阅读外语界面、漫画、文档或软件弹窗时快速识别并翻译
- 截取临时信息并固定在屏幕上，对照输入或整理内容
- 需要轻量、本地优先、适合持续迭代的 Windows 截图工具

## 快速开始

### 普通使用

1. 前往 [Releases](https://github.com/mukemu1998/SnapCat/releases) 下载当前版本便携包。
2. 解压后双击 `SnapCat.exe`。
3. 首次使用可在应用设置中填写接口地址、模型、API Key 与本地 OCR 配置。

当前发布包建议这样区分：

| 包类型 | 说明 |
| --- | --- |
| `portable` | 解压后可直接双击 `SnapCat.exe` 使用，适合普通用户。 |
| `runtime-dependent` | 体积更小，但需要系统已安装 `.NET 8 Desktop Runtime`，否则可能无法启动。 |

也可以直接在仓库本地双击以下启动文件：

- [Launch-SnapCat.cmd](Launch-SnapCat.cmd)
- [启动 SnapCat.bat](启动%20SnapCat.bat)

### 本地开发

- 系统环境：Windows 10 / 11
- SDK 要求：.NET 8 SDK

```powershell
dotnet build .\SnapCat.sln
dotnet run --project .\src\SnapCat.App\SnapCat.App.csproj
```

### 发布打包

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\package-release.ps1 -Configuration Release -Runtime win-x64 -SelfContained -Zip
```

打包脚本会生成当前版本的便携目录、压缩包和 `sha256` 校验文件，方便手动上传到 GitHub Releases。

## 项目结构

```text
SnapCat/
  src/
    SnapCat.App/             WPF 桌面应用
    SnapCat.Core/            核心模型与工作流定义
    SnapCat.Infrastructure/  OCR、翻译、存储与系统集成
  tests/
  tools/
```

## 当前版本

当前版本为 `0.1.0`，属于本地基础版，可完成自由框选、等待式操作、截图固定、OCR、翻译和托盘常驻这些核心流程。

## 许可

[MIT](LICENSE)
