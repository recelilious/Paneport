# Paneport

> Run your Windows PC games, including your Steam library, on iPhone 17 Pro and iPhone 18 Pro.

在 iPhone 17 Pro / 18 Pro 上本地运行 Windows 程序，并通过 Steam 启动你自己游戏库里的 Windows 游戏。

**状态：** 契约阶段，还没有可运行的代码。

## 名字的由来

Paneport 由 pane 和 port 组成。

- **pane**：窗格，指代 Windows。
- **port**：移植，也有便携和港口的意思。

英文里 pane 和 pain 同音，顺带自嘲一下移植之痛。

## 这是什么

一个侧载安装的 iOS 应用，计划由四层组成：

- **Windows 兼容层**：基于上游 Wine 的 ARM64 版本，整个运行时放在一个 iOS 进程里。
- **CPU 翻译**：把 x86 / x86-64 机器码动态翻译成 ARM64，需要 JIT。
- **图形**：把 Direct3D 调用转换到 Metal。
- **Steam 集成**：登录用户自己的账号，管理游戏库，并让游戏的 Steam 功能和 DRM 正常工作。

外壳界面计划使用 Avalonia（.NET）。具体技术选型以路线报告和后续契约为准。

## 使用前提

- 开启开发者模式，用自己的 Apple ID 签名侧载安装。
- 通过调试器附加获得 JIT。
- 不会上架 App Store。

## 契约先行

本项目先写契约，再写实现。所有契约都在 [docs/contracts](docs/contracts/README.md)。

| 编号 | 标题 |
|---|---|
| [C-0001](docs/contracts/C-0001-project-charter.md) | 项目章程 |
| [C-0002](docs/contracts/C-0002-repository-and-commits.md) | 仓库与提交 |
| [C-0003](docs/contracts/C-0003-licensing.md) | 开源许可证 |

## 许可证

本仓库的原创代码采用 [MIT](LICENSE)。Wine、DXMT 等上游组件保留各自的许可证，规则见 [C-0003](docs/contracts/C-0003-licensing.md)。

## 声明

本项目与 Microsoft、Valve、Apple 均无关联。Windows、Steam、iPhone 分别是其所有者的商标。

本项目不分发任何游戏，也不分发 Microsoft、Valve 或游戏厂商的二进制文件，不绕过任何 DRM。
