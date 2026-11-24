using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PinToDesk.Common;
using PinToDesk.Models;

namespace PinToDesk.Services
{
    /// <summary>
    /// 待办事项业务服务，基于 JsonLinesStorage 明文存储引擎（首次升级自动兼容旧版 todos.md）
    /// </summary>
    public class TodoService : IDisposable
    {
        private readonly JsonLinesStorage<TodoItem> _storage;

        public TodoService()
        {
            _storage = new JsonLinesStorage<TodoItem>(AppPaths.Todos);
            TryMigrateLegacyMarkdownTodos();
        }

        /// <summary>
        /// 若新目录尚未创建 todos.jsonl 且存在旧版 %AppData%\PinToDesk\todos.md，则执行一次无损平滑迁移
        /// </summary>
        private void TryMigrateLegacyMarkdownTodos()
        {
            try
            {
                if (File.Exists(AppPaths.Todos)) return;

                var legacyMdPath = Path.Combine(AppPaths.LegacyAppData, "todos.md");
                if (!File.Exists(legacyMdPath)) return;

                var lines = File.ReadAllLines(legacyMdPath, Encoding.UTF8);
                var migrated = new List<TodoItem>();
                int order = 0;

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrWhiteSpace(trimmed)) continue;

                    var title = trimmed.StartsWith("- ", StringComparison.Ordinal)
                        ? trimmed.Substring(2).Trim()
                        : trimmed;
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    migrated.Add(new TodoItem
                    {
                        Title = title,
                        Order = order++
                    });
                }

                if (migrated.Count > 0)
                {
                    _storage.Save(migrated);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"TodoService: 旧版 todos.md 迁移失败 ({ex.Message})");
            }
        }

        /// <summary>
        /// 从存储文件加载全部待办事项
        /// </summary>
        public List<TodoItem> LoadTodos()
        {
            return _storage.Load().OrderBy(t => t.Order).ToList();
        }

        /// <summary>
        /// 持久化保存待办事项列表（采用 300ms 后台防抖异步写入，避免 UI 线程磁盘阻塞）
        /// </summary>
        public void SaveTodos(IEnumerable<TodoItem> items)
        {
            if (items == null) return;
            int order = 0;
            foreach (var item in items)
            {
                item.Order = order++;
            }
            _storage.QueueSave(items, 300);
        }

        /// <summary>
        /// 强制立即刷盘（退出或关键操作时调用）
        /// </summary>
        public void Flush()
        {
            _storage.Flush();
        }

        public void Dispose()
        {
            _storage.Dispose();
        }
    }
}
