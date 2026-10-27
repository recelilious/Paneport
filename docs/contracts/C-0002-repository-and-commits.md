# C-0002 仓库与提交

| 字段 | 值 |
|---|---|
| 状态 | 草案 |
| 版本 | 0.1 |
| 日期 | 2026-10-28 |
| 负责人 | recelilious |

## 1. 名称与位置

| 项目 | 约定 |
|---|---|
| 项目名 | Paneport |
| GitHub 仓库 | `recelilious/Paneport`，由 recelilious 手动创建 |
| 远端地址 | `git@github.com:recelilious/Paneport.git` |
| 本地路径 | `E:\Dev\Paneport` |
| 默认分支 | `main` |

创建 GitHub 仓库时保持为空仓库，不勾选 README、.gitignore 和 license。本地已经有这些文件，空仓库才能直接推送。仓库是否公开由 recelilious 决定。

## 2. 分支

- 初始化提交直接进入 `main`。
- 之后 Claude 的改动默认放在短期分支上，命名为 `<类型>/<简述>`，例如 `feat/jit-request`。
- 分支推送后由 recelilious 合并，可以用 Pull Request，也可以 fast-forward。
- recelilious 可以要求某次改动直接提交到 `main`。

## 3. 推送节奏

- 每次开发会话结束前，把本次的提交推送到 GitHub。
- 推送失败时，Claude 在会话结尾说明原因和本地状态。

## 4. 作者与署名

- **作者：** 使用 recelilious 本机 git 配置中的身份。
- **共同作者：** 凡是 Claude 参与的提交，提交信息最后加一行 trailer：

  ```
  Co-Authored-By: Claude <模型名> <noreply@anthropic.com>
  ```

  模型名写实际完成这次工作的 Claude 模型，例如 `Claude Opus 5.5`。
- recelilious 独立完成的提交不加这一行。

## 5. 提交信息格式

采用 Conventional Commits，用英文，祈使语气。

- 标题格式为 `<type>(<scope>): <subject>`，scope 可以省略，标题不超过 72 个字符。
- 正文说明为什么这样改，每行不超过 72 个字符。
- trailer 放在最后，与正文之间空一行。

| type | 用途 |
|---|---|
| feat | 新功能 |
| fix | 修复缺陷 |
| perf | 性能优化 |
| refactor | 不改变行为的重构 |
| docs | 文档，契约除外 |
| contract | 修改 `docs/contracts/` 下的文件，必须单独提交 |
| build | 构建系统与依赖 |
| ci | 持续集成 |
| test | 测试 |
| chore | 其他杂项 |

示例：

```
contract: add licensing policy (C-0003)

Our own code is MIT. Upstream-derived code keeps its upstream
licence and lives in a separate fork repository.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>
```

仓库根目录的 `.gitmessage` 是提交模板，本地已用 `git config commit.template .gitmessage` 启用。

## 6. 禁止提交的内容

| 类别 | 例子 |
|---|---|
| 签名材料 | `.p12`、`.cer`、`.mobileprovision` |
| 设备配对文件 | `.mobiledevicepairing`，它相当于设备的钥匙 |
| 凭据 | Apple ID、Steam 账号、密码、登录令牌、API 密钥 |
| 第三方二进制 | Microsoft、Valve 或游戏的任何文件 |
| 构建产物 | `.ipa`、`bin/`、`obj/`、`DerivedData/` |

大型二进制文件通过 GitHub Releases 发布，不进 git 历史。

## 7. 文本格式

- 仓库内统一使用 LF 行尾，由 `.gitattributes` 强制。`.bat` 和 `.cmd` 文件例外，使用 CRLF。
- 文本统一使用 UTF-8 编码。
- 文档用中文，代码标识符和提交信息用英文。

## 变更记录

| 版本 | 日期 | 说明 |
|---|---|---|
| 0.1 | 2026-10-28 | 初稿 |
