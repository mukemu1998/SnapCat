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
  <a href="https://github.com/mukemu1998/SnapCat/releases">下载发布版</a> ·
  <a href="./docs/architecture-guidelines.zh-CN.md">开发规范</a>
</p>

<p align="center">
  <img alt="Windows 10/11" src="https://img.shields.io/badge/Windows-10%20%2F%2011-2d7dff" />
  <img alt=".NET 8" src="https://img.shields.io/badge/.NET-8-512bd4" />
  <img alt="WPF" src="https://img.shields.io/badge/UI-WPF-111827" />
  <img alt="Version 0.3.3-preview" src="https://img.shields.io/badge/Version-0.3.3--preview-22c55e" />
  <img alt="License MIT" src="https://img.shields.io/badge/License-MIT-f2c14e" />
</p>

## 项目简介

SnapCat 是一个面向 Windows 的开源截图工具，聚焦自由框选、本地 OCR、接口翻译和截图固定这几条高频链路。它不是以复杂编辑器为中心，而是围绕“截图后马上处理”这个动作来组织交互，尽量把常用操作压缩到更短的路径里。

## 核心能力

| 能力 | 说明 |
| --- | --- |
| 自由框选截图 | 支持等待操作、固定到屏幕、自动翻译、保存到默认位置和复制到剪贴板。 |
| 智能预框选 | 框选时可预选窗口、屏幕边缘和部分控件区域，单击即可确认。 |
| 截图固定到屏幕 | 贴图支持拖动、缩放、翻转、阵列、隐藏、分组和启动后恢复。 |
| 本地 OCR 识别 | 内置本地 OCR 流程，支持识别增强、结果调试和系统 OCR 回退。 |
| 本地与 API 翻译 | 默认可用本地轻量翻译，也可添加多套兼容 OpenAI 的 API 配置。 |
| 翻译浮窗 | 截图翻译后在框选附近显示小浮窗，支持语言选择、复制和再次框选。 |
| 托盘与快捷键 | 支持托盘常驻、托盘菜单、左键默认动作和多类自定义快捷键。 |
| 主题与图标 | 内置多套深色主题，默认主题图标与 EXE 图标色调保持一致。 |

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
| `runtime-dependent` | 体积更小，但需要系统已安装 `.NET 8 Desktop Runtime` 才能启动。 |

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
powershell -ExecutionPolicy Bypass -File .\tools\package-release.ps1 -Configuration Release -Runtime win-x64 -PackageKind portable -Zip
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

## 开发规范

后续新增功能和重构统一遵循 [SnapCat 架构规范](docs/architecture-guidelines.zh-CN.md)，重点保持窗口层轻量、业务逻辑服务化、用户配置不写入源码。

## 当前版本

当前版本为 `0.3.3-preview`，属于 v0.3 预览阶段体验增强版，重点整理全屏画布与框选标注、真实像素马赛克、托盘悬浮提示、快捷键显示和托盘摘要配置。

## 许可

本项目采用 [MIT License](LICENSE) 开源。
