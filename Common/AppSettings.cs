using System;

namespace PinToDesk.Common
{
    /// <summary>
    /// PinToDesk 统一配置模型类
    /// </summary>
    public class AppSettings
    {
        // ── TodoList 待办设置 ──────────────────────────
        public bool TodoIsPinned { get; set; } = false;
        public bool TodoIsPassThrough { get; set; } = false;

        /// <summary>待办事项展开显示（true: 完全展开自动换行; false: 默认3行折叠）</summary>
        public bool TodoExpandedDisplay { get; set; } = false;

        /// <summary>双击托盘图标唤出/隐藏 TodoList</summary>
        public bool TrayDoubleClickTodoEnabled { get; set; } = true;

        /// <summary>待办历史记录保留天数，默认 60 天，超过自动清理</summary>
        public int RecycleBinRetentionDays { get; set; } = 60;

        /// <summary>记录的 Todo 窗口 X 坐标（WPF 逻辑像素，null 表示未记录或使用默认值）</summary>
        public double? TodoWindowLeft { get; set; } = null;

        /// <summary>记录的 Todo 窗口 Y 坐标（WPF 逻辑像素，null 表示未记录或使用默认值）</summary>
        public double? TodoWindowTop { get; set; } = null;

        /// <summary>记录的 Todo 窗口宽度（WPF 逻辑像素，null 表示未记录或使用默认值）</summary>
        public double? TodoWindowWidth { get; set; } = null;

        /// <summary>记录的 Todo 窗口高度（WPF 逻辑像素，null 表示未记录或使用默认值）</summary>
        public double? TodoWindowHeight { get; set; } = null;

        // ── 常规与外观设置 ──────────────────────────────
        /// <summary>颜色主题模式：System(跟随系统，默认), Light(浅色), Dark(深色)</summary>
        public string ThemeMode { get; set; } = "System";

        /// <summary>窗口不透明度（40 ~ 100）</summary>
        public int WindowOpacity { get; set; } = 100;

        // ── 自定义快捷键设置 ────────────────────────────
        /// <summary>唤出 / 隐藏待办清单窗口（默认 Ctrl+D）</summary>
        public string HotkeyTodoTopToggle { get; set; } = "Ctrl+D";
    }
}
