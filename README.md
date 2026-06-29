# PinToDesk - Windows桌面待办事项工具

一个极致轻量的 Windows 桌面待办事项工具。
半透明磨砂风格，常驻系统托盘，悬浮于桌面之上，始终清晰可见。

---

## ✨ 功能特点

- **零干扰界面**：无边框半透明设计，悬浮于桌面，不遮挡工作
- **快速操作**：双击空白处添加，悬停条目显示编辑/删除按钮，拖拽重新排序
- **无字数限制**：支持任意长度内容，自动按窗口宽度换行显示
- **自由缩放**：拖拽右下角可自由调整窗口宽高，无纵横比锁定
- **边界保护**：拖动窗口或缩放时不会超出屏幕工作区范围，支持多显示器
- **细腻滚动条**：7px 极细滚动条，仅在鼠标悬停或滚轮操作时自动淡入显示
- **空列表提示**：列表为空时显示「暂无代办」占位文字
- **数据持久化**：自动保存至本地 Markdown 文件，可用文本编辑器直接查看
- **开机自启**：可在系统托盘菜单中一键设置，无需管理员权限
- **系统托盘**：最小化到托盘后台运行，支持显示/隐藏、置顶切换、开机启动、退出

---

## 开发备忘
- 运行软件之后，就进入后台进程
- 运行软件之后，默认不穿透，不置顶
- 记录设置选项（置顶、穿透、开机启动），再次启动软件之后，加载上一次的设置

---
## 操作演示

https://github.com/user-attachments/assets/6403c61e-899e-4287-91b0-b3e3110954c9

---
## 🖱️ 操作说明

| 操作 | 方法 |
|------|------|
| **添加待办** | 双击列表空白区域 → 输入内容 → 回车确认 |
| **取消添加** | 按 Esc 键 |
| **编辑待办** | 双击某条文字 → 修改 → 回车确认 |
| **删除待办** | 鼠标悬停在条目上 → 点击右侧 ✕ 按钮 |
| **拖拽排序** | 鼠标按住条目不放 → 拖拽至目标位置 → 松开 |
| **移动窗口** | 鼠标拖拽标题栏 |
| **调整大小** | 鼠标拖拽右下角手柄（宽高独立调整） |
| **置顶 / 取消** | 标题栏 📌 按钮，或托盘右键菜单 |
| **置顶穿透** | 启用置顶后，鼠标点击穿透内容区，仅标题栏保留交互 |
| **显示 / 隐藏** | 双击托盘图标，或托盘右键菜单 |

---

## 🖥️ 系统要求

- **操作系统**：Windows 10 / 11（64 位）

| 版本 | 运行依赖 |
|------|---------|
| **SelfContained**（自包含版） | 无，下载即用 |
| **FrameworkDependent**（精简版） | 需预装 [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) |

---

## 💾 数据存储

待办事项以 Markdown 格式自动保存至：

```
%AppData%\PinToDesk\todos.md
```

格式示例：

```markdown
- 完成周报
- 买牛奶
- 回复邮件
```

可直接用任意文本编辑器查看和手动编辑。

---

## 🚀 开机自启动

在系统托盘图标上右键 → 勾选「**开机启动**」即可。

> 注意：开机启动路径基于当前 `.exe` 文件的实际位置。若移动软件文件，需重新勾选以更新路径。

---

## 🔧 从源码构建

```bash
# 开发调试
dotnet build

# 发布（自包含单文件，无依赖）
dotnet publish PinToDesk.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:PublishReadyToRun=true ^
  -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o "Publish/SelfContained"

# 发布（框架依赖单文件，体积极小）
dotnet publish PinToDesk.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true ^
  -o "Publish/FrameworkDependent"
```

- 技术栈：C# / WPF / .NET 8

---

## 友情链接
- [LINUX DO 社区](https://linux.do/)
