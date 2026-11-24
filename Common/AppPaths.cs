using System;
using System.IO;

namespace PinToDesk.Common
{
    /// <summary>
    /// 统一的数据与文件路径管理器（绿色便携：全部存放在程序所在目录下的 Data 文件夹）
    /// </summary>
    public static class AppPaths
    {
        /// <summary>
        /// 应用程序数据目录（应用程序所在目录下的 Data 文件夹，绿色便携）
        /// </summary>
        public static readonly string AppData = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "Data");

        /// <summary>
        /// 待办事项明文保存路径 (todos.jsonl)
        /// </summary>
        public static readonly string Todos = Path.Combine(AppData, "todos.jsonl");

        /// <summary>
        /// 待办回收站明文保存路径 (recycle_bin.jsonl)
        /// </summary>
        public static readonly string RecycleBin = Path.Combine(AppData, "recycle_bin.jsonl");

        /// <summary>
        /// 统一配置文件路径 (settings.jsonl)
        /// </summary>
        public static readonly string Settings = Path.Combine(AppData, "settings.jsonl");

        /// <summary>
        /// 全局异常崩溃日志路径 (crash.log)
        /// </summary>
        public static readonly string CrashLog = Path.Combine(AppData, "crash.log");

        /// <summary>
        /// 旧版 PinToDesk 数据目录（%AppData%\PinToDesk，仅用于首次升级无损迁移）
        /// </summary>
        public static readonly string LegacyAppData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "PinToDesk");

        /// <summary>
        /// 确保数据存放目录存在
        /// </summary>
        public static void EnsureDirectories()
        {
            if (!Directory.Exists(AppData))
            {
                Directory.CreateDirectory(AppData);
            }
        }
    }
}
