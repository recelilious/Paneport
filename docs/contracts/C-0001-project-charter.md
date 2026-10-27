# C-0001 项目章程

| 字段 | 值 |
|---|---|
| 状态 | 草案 |
| 版本 | 0.1 |
| 日期 | 2026-10-28 |
| 负责人 | recelilious |

## 1. 目标

1. 在 iPhone 上本地运行 Windows x86 和 x86-64 程序。
2. 兼容用户自己的 Steam 游戏库，包括登录、浏览、安装和更新。
3. 通过 Steam 启动 Windows 游戏，让游戏自带的 Steam API 和 Steam DRM 在 Valve 官方组件的支持下正常工作。

## 2. 目标设备与系统

| 项目 | 范围 |
|---|---|
| 设备 | iPhone 17 Pro、17 Pro Max（A19 Pro）；iPhone 18 Pro、18 Pro Max（A20 Pro） |
| 系统 | iOS 26.x、iOS 27.x |

其他机型和系统版本不做保证，也不刻意阻止。

## 3. 接受的前提

- 用户开启开发者模式。
- 应用由用户用自己的 Apple ID 签名，侧载安装。
- JIT 通过调试器附加获得。
- 应用永远不上架 App Store。
- iOS 不允许应用创建子进程，所有 Windows 进程都要在一个 iOS 进程内运行。

## 4. 技术方向

以下方向用来指导前期调研，最终方案由路线报告和后续契约确定。

| 层 | 方向 |
|---|---|
| 界面外壳 | Avalonia（.NET），由 recelilious 指定 |
| Windows 兼容层 | 上游 Wine 的 ARM64 构建 |
| CPU 翻译 | 上游 FEX-Emu，Box64 作为备选 |
| 图形 | Direct3D 9 到 11 直接转换到 Metal；Direct3D 12 另行评估 |
| Steam | 方案由 C-0007 决定 |

## 5. 非目标

- 上架 App Store。
- 支持依赖反作弊系统的游戏。
- 绕过、移除或模拟任何 DRM。
- 分发游戏文件，或分发 Microsoft、Valve、游戏厂商的二进制文件。

## 6. 原创规则

recelilious 的要求是：不抄现成产品的代码，但可以使用和整合已有技术栈。具体执行如下。

1. **可以集成：** 上游的库和运行时，例如 Wine、FEX-Emu、Box64、DXMT、MoltenVK、SteamKit2、Avalonia、.NET、SDL、FAudio。可以作为依赖使用，也可以 fork 后修改，但要遵守 C-0003。
2. **不能复制：** 现成产品的代码，不论其许可证是什么。例如 Madeira 及其分支（BCD、WiniOS、Playport、iDeck）、GameNative、Winlator、GameHub、LiveContainer、StikDebug、iSH、Juice、UTM、CrossOver。不复制，不逐行翻译，也不改写成另一种语言。
3. **可以学习：** 这些产品公开的文档、README、issue 讨论和测试数据，只用来了解哪些做法可行。实现时依据上游库文档、Apple 文档和我们自己的设计文档。
4. **记录来源：** 每份设计文档都要列出参考过的公开资料。

## 7. 合规红线

1. 不绕过、不移除、不模拟任何 DRM，包括 SteamStub 和 CEG。不使用、不分发 steam_api 模拟器和 DRM 移除工具。
2. 仓库和安装包里不放任何 Microsoft、Valve 或游戏的二进制文件。需要时由用户设备从官方来源下载，或由用户自己提供。
3. 用户的 Steam 凭据只用于直接与 Valve 服务器通信，不发送给任何第三方。登录令牌只保存在设备钥匙串中。
4. 名称、图标和界面不得暗示与 Microsoft、Valve、Apple 有关联。

## 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| 0.1 | 2026-10-28 | 初稿 |
