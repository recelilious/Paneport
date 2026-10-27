# C-0003 开源许可证

| 字段 | 值 |
|---|---|
| 状态 | 草案 |
| 版本 | 0.1 |
| 日期 | 2026-10-28 |
| 负责人 | recelilious |

本契约是工程层面的许可证管理规则，不构成法律意见。正式公开发布前，条件允许时请专业人士复核。

## 1. 结论

| 代码 | 许可证 | 放在哪里 |
|---|---|---|
| Paneport 的原创代码 | MIT | 本仓库 |
| 对 LGPL 上游的修改，例如 Wine、DXMT | 保持 LGPL-2.1-or-later | 各自的 fork 仓库 |
| 对 MIT 上游的修改，例如 FEX-Emu、Box64 | 保持 MIT | 各自的 fork 仓库 |

我们自己写的每一行代码都用 MIT。整个安装包不能整体按 MIT 发布，因为它包含 LGPL 组件。在不违反上游许可证的前提下，这是最接近 MIT 的做法。

## 2. 为什么不能全部用 MIT

Wine 和 DXMT 都采用 LGPL-2.1-or-later。修改它们得到的代码属于库的衍生作品，必须继续以 LGPL 发布。这包括写在 Wine 源码树里的 iOS 显示驱动，以及为单进程运行而对 Wine 做的改造。

DXMT 在 0.80 及更早的版本使用 MIT，之后改为 LGPL-2.1-or-later。我们跟随新版本，按 LGPL 处理。

## 3. 为什么 MIT 代码可以和 LGPL 组件放进同一个应用

LGPL-2.1 第 6 节允许把库和"使用该库的作品"组合起来发布，条件有三个：

1. 组合作品的条款允许用户为自用而修改，并允许为调试这些修改而逆向工程。MIT 满足这一点。
2. 随附库的完整源码。
3. 让用户能用修改过的库重新链接出新的可执行文件。

我们的做法：

- **动态打包：** Wine 等组件的 Unix 侧代码以动态库或 Framework 的形式放进 .app，用户可以替换。
- **公开源码：** SteamKit2（LGPL-2.1）经 .NET AOT 静态编译进可执行文件。本仓库的全部源码和构建脚本都公开，用户可以换掉 SteamKit2 后重新构建，满足重新链接的要求。
- **用户可安装：** 侧载本来就由用户用自己的 Apple ID 签名，用户能安装自己重新构建的版本。
- **随发布附带：** 每个发布版都附带 `THIRD_PARTY_NOTICES.md` 和全部许可证全文。应用内提供"开源许可"页面。发布说明里给出对应源码标签的链接。

## 4. 仓库划分

- 本仓库的 `src/` 只放原创代码，全部 MIT。
- 从上游派生的代码一律放在独立的 fork 仓库，命名为 `Paneport-<上游名>`，例如 `Paneport-wine`，以 git submodule 的形式挂在本仓库的 `external/` 下。
- fork 仓库保留上游的许可证文件和版权声明。
- 原创源文件的头部写 `SPDX-License-Identifier: MIT`。fork 仓库里的新文件使用该上游的许可证标识，例如 `SPDX-License-Identifier: LGPL-2.1-or-later`。
- 各许可证全文放在本仓库的 `LICENSES/` 目录，第一个第三方依赖进来时创建。

## 5. 准入规则

新增任何依赖之前，先在第 6 节的登记表里登记。

| 类别 | 许可证 | 规则 |
|---|---|---|
| 允许 | MIT、BSD-2-Clause、BSD-3-Clause、ISC、Zlib、0BSD、BSL-1.0、Apache-2.0、CC0-1.0、FTL | 保留版权声明。Apache-2.0 还要保留 NOTICE 文件。FTL 要在文档里注明使用了 FreeType。 |
| 有条件 | LGPL-2.1-or-later、LGPL-3.0、MPL-2.0 | 只能作为可单独识别的组件使用。修改要以同一许可证公开。要保持可重新链接。LGPL-3.0 还要提供安装信息。 |
| 禁止 | GPL-2.0、GPL-3.0、AGPL-3.0、SSPL、仅限非商业使用的许可、没有许可证的代码 | 不得进入本仓库、fork 仓库和安装包。 |
| 禁止 | 厂商 SDK 头文件、反编译得到的代码 | 例如 Steamworks SDK 和 Windows SDK 的头文件。需要的接口定义自己编写，并在设计文档里记录依据。 |
| 待审 | 专有组件，例如 Apple Metal Shader Converter | 公开渠道没有找到它的再分发条款。使用前必须阅读其许可协议并记录结论，并且不放进仓库。 |

双许可的组件选择限制较少的一方。例如 FreeType 在 FTL 和 GPL-2.0 之间选 FTL。

## 6. 组件登记表

核对日期为 2026-10-28，许可证取自各仓库的 LICENSE 文件。

| 组件 | 用途 | 许可证 | 类别 |
|---|---|---|---|
| Wine | Windows 兼容层 | LGPL-2.1-or-later | 有条件 |
| FEX-Emu | x86-64 翻译 | MIT | 允许 |
| Box64 | x86 翻译备选 | MIT | 允许 |
| FEX-darwin（dbc-hbin） | FEX 的 Darwin 与 Wine 集成模块 | MIT | 允许 |
| DXMT | Direct3D 10/11 到 Metal | LGPL-2.1-or-later | 有条件 |
| MoltenVK | Vulkan 到 Metal，可选 | Apache-2.0 | 允许 |
| SPIRV-Cross | 着色器转换，MoltenVK 的依赖 | Apache-2.0 | 允许 |
| DXVK | Direct3D 到 Vulkan，在 iOS 上大概率不用 | Zlib | 允许 |
| vkd3d-proton | Direct3D 12 到 Vulkan，在 iOS 上大概率不用 | LGPL-2.1 | 有条件 |
| SteamKit2 | Steam 网络协议 | LGPL-2.1 | 有条件 |
| protobuf-net | SteamKit2 的依赖 | Apache-2.0 | 允许 |
| Avalonia | 界面外壳 | MIT | 允许 |
| SkiaSharp | Avalonia 的渲染库 | MIT | 允许 |
| .NET 运行时 | 托管运行时 | MIT | 允许 |
| .NET for iOS（dotnet/macios） | iOS 绑定 | MIT | 允许 |
| SDL | 输入与音频辅助 | Zlib | 允许 |
| FAudio | XAudio2 实现 | Zlib | 允许 |
| FreeType | 字体渲染，Wine 的依赖 | FTL 或 GPL-2.0 | 允许，选 FTL |
| GStreamer | 媒体解码，Wine 的可选依赖 | LGPL-2.1-or-later | 有条件，不带 GPL 插件 |
| idevice（jkcoxson） | 设备协议，可选 | MIT | 允许 |

媒体解码优先使用 Apple 系统框架，以避开 GStreamer 的 GPL 插件和编解码器专利问题。

## 7. 只能参考、代码不得复用的项目

这些项目要么是现成产品，受 C-0001 第 6 节约束，要么许可证与本项目不兼容。

| 项目 | 许可证 |
|---|---|
| Madeira | GPL-3.0 |
| madeira-dock | GPL-3.0-or-later 加 Madeira Converter Exception |
| BCD、WiniOS、Playport、iDeck（Madeira 的分支） | GPL-3.0 系，据各仓库的声明 |
| GameNative | GPL-3.0 |
| Winlator | LGPL-2.1 |
| DepotDownloader | GPL-2.0 |
| iSH | GPL-3.0 |
| LiveContainer | AGPL-3.0 |
| StikDebug | AGPL-3.0 |
| UTM | Apache-2.0 |
| SteamARM（LukeOkk） | MIT |
| SteamRE/open-steamworks | 没有许可证，已归档 |
| Apple D3DMetal（Game Porting Toolkit） | 专有，仅限非商业使用，只支持 macOS |
| GameHub | 闭源 |

UTM 和 SteamARM 的许可证虽然宽松，但它们是现成产品，同样只作参考。

## 8. 运行时下载、不进仓库和安装包的组件

| 组件 | 来源 |
|---|---|
| Microsoft VC++ 运行库、DirectX 运行库 | 用户设备从 Microsoft 官方下载，或由用户提供 |
| Valve Steam 客户端组件 | 用户设备从 Valve 官方 CDN 下载 |
| Wine Mono、Wine Gecko | 首次需要时从官方地址下载，适用各自的许可证 |
| 游戏文件 | 用户用自己的 Steam 账号下载 |

## 9. 与 AGPL 程序的边界

StikDebug 是独立的 App，采用 AGPL-3.0。Paneport 只通过公开的 JIT 请求约定与它协作，不链接、不内嵌它的任何代码。

如果以后要在应用内自行启用 JIT，就基于 MIT 的 idevice 自己实现。

## 10. 回馈上游

各上游对 AI 辅助贡献的政策不同。

| 项目 | 政策 |
|---|---|
| QEMU | 2025 年起明确拒绝 AI 生成的代码 |
| Gentoo、NetBSD | 禁止或严格限制 |
| GNU Binutils | 要求标注 LLM 生成的内容 |
| GCC | 截至 2026 年 4 月仍在讨论 |
| Wine、FEX-Emu | 截至 2026-10-28 未查到明确政策 |

规则：

1. 向上游提交前，先查对方当前的政策并遵守。
2. 不得为了绕过政策而删除 AI 署名。
3. 因此，Wine、DXMT、FEX-Emu 的改动可能要长期维护在我们自己的 fork 里。

## 11. 备选方案

如果以后需要明确的专利授权，可以把原创代码改为 `MIT OR Apache-2.0` 双许可。默认保持 MIT。

## 12. 依据

- Wine 许可证：<https://github.com/wine-mirror/wine/blob/HEAD/LICENSE>
- DXMT 许可证：<https://github.com/3Shain/dxmt/blob/HEAD/LICENSE>
- FEX-Emu 许可证：<https://github.com/FEX-Emu/FEX/blob/HEAD/LICENSE>
- SteamKit2 许可证：<https://github.com/SteamRE/SteamKit/blob/HEAD/LICENSE>
- protobuf-net 许可证：<https://github.com/protobuf-net/protobuf-net/blob/HEAD/Licence.txt>
- Madeira：<https://github.com/willfaust/Madeira>
- FEX-darwin：<https://github.com/dbc-hbin/FEX-darwin>
- Metal Shader Converter：<https://developer.apple.com/metal/shader-converter/>
- 关于在应用里附带 Metal Shader Converter 的未答复提问：<https://developer.apple.com/forums/thread/829379>
- QEMU 的 AI 代码政策补丁：<https://patchew.org/QEMU/20250616092241.212898-1-armbru@redhat.com>
- GCC 的讨论：<https://gcc.gnu.org/pipermail/gcc/2026-April/247958.html>
- GNU Binutils 的声明：<https://gnu.googlesource.com/binutils-gdb/+/c0316a54b489d68e30de718aee69ab441799aad2%5E%21>
- 开源项目 AI 政策综述：<https://www.ssdnodes.com/learn/lang/pcm/ai-assisted-code-open-source-policies>

## 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| 0.1 | 2026-10-28 | 初稿 |
