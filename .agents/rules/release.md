---
trigger: model_decision
description: 发版、发布新版本、版本号、Git 提交、打标、打包、GitHub Release 时加载
---

# PinToDesk 版本发布与 Git 流程

本文件在“发布新版本”“发版”“发布 X.X”“打包推送”等意图下加载。执行发布前，必须遵守 `core.md` 的讨论与执行边界。

## 一、版本命名规范（SemVer）

1. **版本格式**
   - 正式发布：`主版本号.次版本号`，如 `2.0`。
   - 内部调试：`主版本号.次版本号.修订号`，如 `2.0.1`。
   - Git 标签对外可标识为 `v2.0`。

2. **升级规则**
   - **主版本号 (Major)**：用户定义。
   - **次版本号 (Minor)**：用户定义。
   - **版本修订号 (Patch / 编译自我编号)**：
     - 仅用于本地构建或编译时内部流水编号，精准追踪二进制产物。
     - 每次代码变更触发编译前，修订号自动增加 `0.0.1`，如 `2.0.0 -> 2.0.1 -> 2.0.2`。
     - 最高递增至 `0.0.99`，即末尾修订号最大为 `99`。达到 99 后保持，提醒用户处理。

3. **版本修改位置**
   - 主工程 `PinToDesk.csproj` 中的 `<Version>`（同步 `<AssemblyVersion>` 与 `<FileVersion>`）。

## 二、构建与交付铁律

1. **编译前终结原有程序**
   - 重新编译前，必须首先强制终结正在运行的原程序进程：
     ```powershell
     Stop-Process -Name "PinToDesk" -Force -ErrorAction SilentlyContinue
     ```
   - 彻底释放文件占用与互斥锁，确保新二进制无阻碍覆盖。

2. **拉起与运行交由用户**
   - 构建完成后，交给用户手动拉起程序。
   - 交付验收不需要检测程序运行。
   - 智能体严禁在后台或非交互环境中自行拉起应用。
   - 构建达到 0 警告 0 错误即可完成交付。

3. **构建门禁**
   - 执行 `dotnet build PinToDesk.csproj -c Release`，必须 **0 警告 0 错误**。

4. **打包产物隔离**
   - `Publish/` 目录与 `*.zip` 发布包必须被 `.gitignore` 忽略。

## 三、Git 提交、打标与隐私邮箱规范

1. **提交信息**
   - 遵循 Conventional Commits：`<type>(<scope>): <subject>`（中文描述）。
   - 示例：
     ```powershell
     git commit -m "chore(release): v2.0 - 同步 WinKit 待办架构优化、Fluent 主题与历史回收站"
     ```

2. **打标**
   - 发布版本时必须打附注标签：
     ```powershell
     git tag -a v2.0 -m "Release v2.0"
     ```

3. **隐私邮箱核验**
   - 提交前确认 `git config user.email` 为 GitHub 隐私邮箱（`*@users.noreply.github.com`）。
   - 推送完成后，核验本次提交及标签的 Author / Committer / Tagger 邮箱均为隐私邮箱。

4. **推送顺序**
   - 先推送分支，再推送标签：
     ```powershell
     git push origin main
     git push origin v<Version>
     ```

## 四、自动化编译打包

自动化编译打包流程，共四步：

```text
[步骤 1: 版本确定] -> [步骤 2: 本地构建与验证] -> [步骤 3: 自动化打包] -> [步骤 4: Git 提交与打标]
```

1. **步骤 1：版本确定**
   - 在 `PinToDesk.csproj` 中修改 `<Version>` 为目标版本。
   - 正式发布示例：`<Version>2.0</Version>`；内部调试示例：`<Version>2.0.1</Version>`。

2. **步骤 2：本地构建与验证**
   - 执行：
     ```powershell
     dotnet build PinToDesk.csproj -c Release
     ```
   - 确保 **0 警告 0 错误**。

3. **步骤 3：自动化打包**
   - **打包策略区分**：
     - **正式发布版本（如 `v2.0`）**：同时打包框架依赖版与独立免装版，并自动归档压缩为标准 ZIP 发布包；
     - **内部调试版本（如 `v2.0.1`）**：仅打包独立免装版目录（`Publish\SelfContained_V<Version>`），后续不进行 ZIP 压缩包生成，便于本地秒级启动与调试。
   - **命令执行规范**：
     - **正式发布打包（双版本 + ZIP 归档）**：
       ```powershell
       # 1. 检查并清理旧进程与历史产物
       Stop-Process -Name "PinToDesk" -Force -ErrorAction SilentlyContinue
       $publishBase = "Publish"
       $fdOut = "$publishBase\FrameworkDependent_V<Version>"
       $scOut = "$publishBase\SelfContained_V<Version>"
       $fdZip = "$publishBase\PinToDesk-v<Version>-FrameworkDependent.zip"
       $scZip = "$publishBase\PinToDesk-v<Version>-SelfContained.zip"
       Remove-Item $fdOut, $scOut, $fdZip, $scZip -Recurse -Force -ErrorAction SilentlyContinue

       # 2. 编译发布：框架依赖版与独立免装版
       dotnet publish PinToDesk.csproj -c Release -r win-x64 --self-contained false -o $fdOut
       dotnet publish PinToDesk.csproj -c Release -r win-x64 --self-contained true -o $scOut

       # 3. 自动归档压缩为标准 ZIP 发布包
       Compress-Archive -Path "$fdOut\*" -DestinationPath $fdZip -Force
       Compress-Archive -Path "$scOut\*" -DestinationPath $scZip -Force
       ```
     - **内部调试打包（仅独立免装版，不生成 ZIP）**：
       ```powershell
       # 1. 检查并清理旧进程与历史产物
       Stop-Process -Name "PinToDesk" -Force -ErrorAction SilentlyContinue
       $publishBase = "Publish"
       $scOut = "$publishBase\SelfContained_V<Version>"
       Remove-Item $scOut -Recurse -Force -ErrorAction SilentlyContinue

       # 2. 仅发布独立免装版目录（即解即用，不生成 ZIP 压缩包）
       dotnet publish PinToDesk.csproj -c Release -r win-x64 --self-contained true -o $scOut
       ```

4. **步骤 4：Git 提交与打标**
   - 暂存必要文件，禁止包含 `Publish/` 和 `*.zip`。
   - 提交信息遵循 Conventional Commits：
     ```powershell
     git commit -m "chore(release): v<Version> - <变更摘要>"
     ```
   - 创建附注标签：
     ```powershell
     git tag -a v<Version> -m "Release v<Version>"
     ```

## 五、发布

自动化编译打包完成后，执行发布流程，共三步：

```text
[步骤 1: 文档与更新日志同步] -> [步骤 2: 推送远端分支与标签] -> [步骤 3: 发布]
```

1. **步骤 1：文档与更新日志同步**
   - **全量变更汇总与小版本调试记录清理**：
     - 正式发布新版本时，汇总距离上一个正式发布版本到新版本之间的全部功能级变更；
     - 移除内部调试小版本的琐碎记录，在 `开发日志.md` 同步该正式新版本的完整更新日志；
   - **同步更新 `README.md`**：根据正式版本的全量新特性同步刷新功能描述与构建说明。

2. **步骤 2：推送远端分支与标签**
   ```powershell
   git push origin main
   git push origin v<Version>
   ```

3. **步骤 3：发布**
   - 调用 GitHub CLI 发布（精确指定本次版本的两个 ZIP 资产，避免误传历史旧包）：
     ```powershell
     gh release create v<Version> "Publish\PinToDesk-v<Version>-FrameworkDependent.zip" "Publish\PinToDesk-v<Version>-SelfContained.zip" --title "PinToDesk v<Version>" --notes-file "<更新说明路径>"
     ```
   - 发布成功后，向用户汇报：
     - GitHub Release 链接
     - 打包资产名称及文件大小
     - 隐私邮箱核验结果

## 六、智能体自动化触发契约

1. **发版自然语言意图响应**
   - 用户输入 **“打包”**、**“发布新版本”**、**“发版”**、**“发布 X.X”**、**“打包推送”** 等意图时，智能体自动串联执行标准发布流水线。

2. **发布前确认门禁**
   - 执行远端推送与 GitHub Release 前，必须展示：
     - 目标版本号
     - 提交信息
     - 标签名称
     - 目标仓库
     - 打包资产路径
   - 等待用户一次明确确认后，方可执行 `git push` 与 `gh release create`。
   - “代码修改授权”不等于“远端发布授权”。

3. **失败处理**
   - 构建失败：停止发布，保留现场，输出错误日志。
   - 打包失败：清理半成品，不进入第五节。
   - 提交失败：检查 `git status`，不强行覆盖。
   - 推送失败：检查远端权限与网络，不强制推送。
   - Release 创建失败：保留已推送标签，输出手动补救命令。

4. **执行后闭环交付**
   - 智能体直接向用户汇报：
     - GitHub Release 链接
     - 打包资产名称及文件大小
     - 隐私邮箱最终核验结果
