<div align="center">
  
  <img src="NotiFlow Icon.png" alt="NotiFlow Logo" width="120" height="120">

  <h1>🌊 NotiFlow (弹幕通知)</h1>

  <p>
    <b>在Windows端上实现“弹幕通知”功能</b>
  </p>

  <!-- 徽章区 -->
  <p>
    <a href="https://github.com/Oatizen/NotiFlow/releases"><img src="https://img.shields.io/github/v/release/Oatizen/NotiFlow?color=0078D7&style=for-the-badge" alt="Release"></a>
    <img src="https://img.shields.io/badge/Platform-Windows_10%20%7C%2011-blue?style=for-the-badge&logo=windows" alt="Windows">
    <img src="https://img.shields.io/badge/.NET-8.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET">
    <a href="LICENSE"><img src="https://img.shields.io/badge/License-MIT-success?style=for-the-badge" alt="License"></a>
  </p>
</div>

<br/>

> 打团听到消息提示，不想立即查看但又担心错过重要通知？
> **NotiFlow** 可以拦截 Windows 原生通知，并将其转化为全透明、鼠标穿透的“弹幕”从屏幕上方飘过。**不错过重要信息，也不打断专注心流。**

<div align="center">
  <video src="Presentation.mp4" autoplay loop muted width="800"></video>
</div>

## ✨ 功能介绍

- 👻 **鼠标完全穿透**：弹幕处于置顶状态，但鼠标点击会直接穿透到下层游戏/网页，避免干扰操作。
- 🎨 **丰富的自定义选项**：字体、字号、文字颜色、透明度、弹幕速度乃至弹幕最大长度，都可自定义
- 🥷 **防截图隐身**：采用 `DisplayAffinity` 技术，只需开启设置，使用系统截图或 OBS 录屏时，弹幕会在捕捉画面中自动隐形，保护隐私。
- ⭕ **快捷开关弹幕显示**：可自定义快捷键，随时开关弹幕显示
- ⚙️ **开机自启**：可自定义开机自启，无需每次手动启动

---

## 📥 快速开始

1. 前往 [Releases 页面](https://github.com/Oatizen/NotiFlow/releases) 下载最新版本的 `NotiFlow.zip`。
2. 解压后双击运行 `NotiFlow.exe`。
3. 在系统托盘中找到 NotiFlow，左键单击或是右键菜单进入“设置”界面。

*注：弹幕不支持在全屏模式下运行的程序上方显示*
- ❓为什么不会显示我的微信通知？
由于微信PC版客户端的通知推送不通过Windows通知中心，因此无法被NotiFlow读取和显示。

---

## 🛠️ 从源码构建 (开发者指南)

如果你想自己修改代码或为本项目贡献功能，请参考以下指南：

### 环境依赖
- [Visual Studio 2022](https://visualstudio.microsoft.com/) (包含 .NET 桌面开发工作负载)
- .NET 8.0 SDK

### 快速编译
```bash
# 克隆仓库
git clone https://github.com/Oatizen/NotiFlow.git

# 进入目录
cd NotiFlow

# 运行项目
dotnet run