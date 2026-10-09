# PenBridge v0.3.0

Windows 数位板切屏工具，面向 Photoshop 等多屏绘画工作流。

## 本版变化

- 全新 PenBridge 界面：浅色卡片、圆角、留白与蓝色强调，屏幕布局更容易辨认。
- 可视化点选目标屏幕，配合托盘、全局快捷键与 OTD 映射撤销。
- 独立三屏交互演示：移动模拟笔尖、点选屏幕、自动播放；不调用真实驱动。
- 公开 C# 源码、构建与测试脚本，提供 Windows 自动构建工作流。
- 提供直接运行的 EXE、便携 ZIP、演示素材及 SHA-256 校验文件。
- EXE 内置帮助；演示版独立编译，文件改名后仍保持模拟模式。

## 下载

| 文件 | 用途 |
| --- | --- |
| `PenBridge.exe` | 真实驱动模式主程序，也可用 `--demo` 启动模拟 |
| `PenBridge-Demo.exe` | 无需数位板或驱动的独立交互演示 |
| `PenBridge-0.3.0-Windows.zip` | 主程序、演示程序、使用说明与演示素材 |
| `SHA256SUMS.txt` | 发布文件的 SHA-256 校验值 |

系统要求：Windows 10/11 与 .NET Framework 4.8。日常运行不需要 Python、Anaconda 或安装器。源码可从本仓库获取。

## 兼容性与验证范围

精确指定屏幕需要兼容的 OpenTabletDriver 0.6.x、已运行的 daemon 与 Console，以及受支持的绝对定位输出模式。原厂驱动方式只转发全局循环切屏快捷键，无法指定或回读目标屏幕；目前不能承诺任意品牌免配置通用。

OTD 的 Windows 压感需要 VMulti、WindowsInk 插件与绘画软件的 Windows Ink 配置，参见 [官方说明](https://opentabletdriver.net/Wiki/FAQ/Windows)。

测试套件包含 31 项核心检查和 26 项演示检查。多屏与 OTD 自动测试使用模拟数据；真实多屏数位板切换和 Photoshop 实笔压感尚未完成硬件验证。使用前请悬空切换，确认目标屏幕落点与压感。
