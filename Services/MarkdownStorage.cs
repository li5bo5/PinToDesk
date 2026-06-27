using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using PinToDesk.Models;

namespace PinToDesk.Services
{
    public class MarkdownStorage
    {
        private readonly string _filePath;

        public MarkdownStorage()
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var folder = Path.Combine(appData, "PinToDesk");
            if (!Directory.Exists(folder))
                Directory.CreateDirectory(folder);
            _filePath = Path.Combine(folder, "todos.md");
            // Ensure file exists
            if (!File.Exists(_filePath))
                File.WriteAllText(_filePath, string.Empty, Encoding.UTF8);
        }

        public List<TodoItem> LoadTodos()
        {
            var todos = new List<TodoItem>();
            var lines = File.ReadAllLines(_filePath, Encoding.UTF8);
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                // Simple format: "- Title" (markdown list item)
                var title = trimmed.StartsWith("- ") ? trimmed.Substring(2) : trimmed;
                todos.Add(new TodoItem { Title = title });
            }
            return todos;
        }

        public void SaveTodos(IEnumerable<TodoItem> items)
        {
            var sb = new StringBuilder();
            foreach (var item in items)
            {
                sb.AppendLine($"- {item.Title}");
            }
            File.WriteAllText(_filePath, sb.ToString(), Encoding.UTF8);
        }
    }
}
