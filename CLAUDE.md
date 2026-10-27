# Claude 工作约定

本文件给参与 Paneport 开发的 Claude 会话阅读。规则的正式版本在 `docs/contracts/`，两者不一致时以契约为准。

1. **先读契约。** 每次会话开始时，读 `docs/contracts/README.md` 和与任务相关的契约。要改契约，先征得 recelilious 同意，并用 `contract` 类型单独提交。
2. **提交与署名。** 遵守 C-0002。作者使用本机 git 身份。Claude 参与的提交，最后加一行 `Co-Authored-By: Claude <实际模型名> <noreply@anthropic.com>`。
3. **分支与推送。** 改动默认放在短期分支上，由 recelilious 合并。每次开发会话结束前推送到 `origin`。
4. **许可证。** 遵守 C-0003。新增依赖前先登记许可证。GPL 和 AGPL 代码不得进入仓库。
5. **原创。** 遵守 C-0001 第 6 节。不复制、不翻译、不改写现成产品的代码。
6. **合规。** 不实现、不引入任何 DRM 绕过或 steam_api 模拟。不提交第三方二进制、签名材料、配对文件和凭据。
7. **语言。** 文档用中文，代码标识符和提交信息用英文。
