# 小阳 CAD 轮盘

用于 Windows 上的 AutoCAD 和中望 CAD。把常用原生命令、PGP 别名、自定义命令及已加载的第三方命令绑定到鼠标轮盘。支持自定义格子、图标、触发方式，提供命令批量导入、配置备份和输入法设置。

| 当前版本 | 宿主 | 下载 |
| --- | --- | --- |
| 2.2.0.84 x64 | AutoCAD 2010–2027、中望 CAD 2020–2026 | [64 位安装包](https://github.com/zhao960728-crypto/xy-cad-wheel/releases/tag/v2.2.0.84) |
| 2.2.0.86 Win32 | AutoCAD 2010 32 位 | [32 位独立安装包](https://github.com/zhao960728-crypto/xy-cad-wheel/releases/tag/v2.2.0.86-win32) |

## 安装与使用

保存图纸并退出 CAD 后运行对应的安装 EXE。使用 ZIP 时，先完整解压到长期保留的目录，再运行包内的安装脚本。插件支持按已安装的宿主版本选择安装目标。

在 CAD 中输入 **XYLP** 打开轮盘，**XYLPSZ** 打开设置，**XYLPZJ** 运行自检。默认触发方式为 Shift + 右键长按。批量导入使用安装包内的 **批量导入CAD快捷键模板.xlsx**。

2.2.0.86 调整了 AutoCAD 2010 Win32 的启动、输入法切换及退出清理。该版本已构建并经过离线回归，实际 AutoCAD 2010 32 位中的启动、设置保存和 DWG 保存仍待复测。

## 从源码构建

在 Windows 上安装 .NET 10 SDK，使用 PowerShell 7。在源码根目录运行对应命令。NuGet 用于恢复项目中指定版本的 AutoCAD、ZWCAD API 引用及运行依赖。构建产物写入 **dist/**。

    .\build-combined-installer.ps1
    .\build-autocad2010-win32.ps1

首条命令生成 64 位联合安装器及两套单独 ZIP。单独构建 AutoCAD 可运行 **build.ps1**，单独构建中望 CAD 可运行 **build-zwcad.ps1**。

项目的宿主入口和共享轮盘源码位于 [src](src)，打包脚本位于 [packaging](packaging)，回归工程位于 [tests](tests)。[内置图标库](assets/wheel-icons) 含 1,024 个图标及离线浏览页。部分历史更新回归用例需要对应版本的旧版安装包作为输入。

## 许可证

项目原创源码采用 [MIT](LICENSE)。Theraot.Core 依照其 [许可证](src/XiaoYang.AutoCADPlugin.LegacySupport/Theraot.Core.LICENSE.txt) 分发。AutoCAD 和 ZWCAD 宿主及各自 API 引用适用其自身的许可条款。

SOLIDWORKS 版另见 [XY 轮盘](https://github.com/zhao960728-crypto/xy-sw-wheel)。
