using System;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

namespace PinToDesk.Common
{
    /// <summary>
    /// 统一配置管理器，基于 settings.jsonl 单行明文存储与原子替换（支持旧版 settings.json 无损迁移）
    /// </summary>
    public class SettingsManager
    {
        private readonly string _settingsFile;
        private readonly string _tmpSettingsFile;
        private AppSettings _settings;
        private readonly object _lock = new();
        private bool _faultNotified;

        /// <summary>
        /// 读失败熔断状态：配置文件存在但读取或解析异常时置为 true，拒绝用默认配置覆写原文件
        /// </summary>
        public bool HasReadFault { get; private set; }

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
            WriteIndented = false // 单行 JSON
        };

        /// <summary>
        /// 兼容 PinToDesk v1.0.x 旧版 settings.json 字段映射结构
        /// </summary>
        private sealed class LegacySettingsDto
        {
            public bool? IsPinned { get; set; }
            public bool? IsPassThrough { get; set; }
        }

        public AppSettings Settings => _settings;

        public event EventHandler<AppSettings>? SettingsChanged;

        public SettingsManager()
        {
            AppPaths.EnsureDirectories();
            _settingsFile = AppPaths.Settings;
            _tmpSettingsFile = _settingsFile + ".tmp";
            _settings = LoadSettings();
        }

        /// <summary>
        /// 保存设置并按需触发变更广播（原子写入 settings.jsonl）
        /// </summary>
        /// <param name="newSettings">最新配置对象</param>
        /// <param name="notify">是否触发 SettingsChanged 广播（纯窗口坐标保存可设为 false，避免引发全应用主题重绘）</param>
        public void SaveSettings(AppSettings newSettings, bool notify = true)
        {
            if (newSettings == null) return;
            bool savedOk = false;

            lock (_lock)
            {
                _settings = newSettings;
                if (HasReadFault) return;

                try
                {
                    var json = JsonSerializer.Serialize(_settings, JsonOptions);
                    File.WriteAllText(_tmpSettingsFile, json + Environment.NewLine, Encoding.UTF8);
                    File.Move(_tmpSettingsFile, _settingsFile, overwrite: true);
                    savedOk = true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"SettingsManager: 保存设置失败 ({ex.Message})");
                }
                finally
                {
                    try
                    {
                        if (File.Exists(_tmpSettingsFile))
                        {
                            File.Delete(_tmpSettingsFile);
                        }
                    }
                    catch
                    {
                        // 忽略临时文件清理异常
                    }
                }
            }

            // 在锁外触发广播，防止订阅方跨线程 Dispatcher.Invoke 引起潜在死锁
            if (savedOk && notify)
            {
                SettingsChanged?.Invoke(this, newSettings);
            }
        }

        /// <summary>
        /// 加载设置（支持 Data/settings.json 及旧版 %AppData%\PinToDesk\settings.json 平滑迁移，并在读取失败时启用熔断保护）
        /// </summary>
        private AppSettings LoadSettings()
        {
            lock (_lock)
            {
                // 1. 优先从 Data/settings.jsonl 读取
                if (File.Exists(_settingsFile))
                {
                    try
                    {
                        var lines = File.ReadAllLines(_settingsFile, Encoding.UTF8);
                        bool hasNonEmptyLine = false;
                        foreach (var line in lines)
                        {
                            var trimmed = line.Trim();
                            if (string.IsNullOrWhiteSpace(trimmed)) continue;
                            hasNonEmptyLine = true;

                            var parsed = JsonSerializer.Deserialize<AppSettings>(trimmed, JsonOptions);
                            if (parsed != null) return parsed;
                        }

                        if (hasNonEmptyLine)
                        {
                            // 文件存在且非空但未解析出有效配置：触发熔断保护，严禁覆写原文件
                            EnterReadFault("格式解析异常");
                            return new AppSettings();
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"SettingsManager: 加载 settings.jsonl 失败 ({ex.Message})");
                        EnterReadFault("可能被占用或格式损坏");
                        return new AppSettings();
                    }
                }

                // 2. 兼容检查同目录旧版 Data/settings.json 并平滑迁移
                var oldJsonPath = Path.Combine(AppPaths.AppData, "settings.json");
                if (TryMigrateFromJson(oldJsonPath, out var migratedLocal))
                {
                    SaveSettings(migratedLocal, notify: false);
                    return migratedLocal;
                }

                // 3. 兼容检查 PinToDesk v1.0.x 遗留在 %AppData%\PinToDesk\settings.json 的配置并平滑迁移
                var legacyJsonPath = Path.Combine(AppPaths.LegacyAppData, "settings.json");
                if (TryMigrateFromJson(legacyJsonPath, out var migratedLegacy))
                {
                    SaveSettings(migratedLegacy, notify: false);
                    return migratedLegacy;
                }

                var defaultSettings = new AppSettings();
                SaveSettings(defaultSettings, notify: false);
                return defaultSettings;
            }
        }

        private static bool TryMigrateFromJson(string jsonPath, out AppSettings result)
        {
            result = new AppSettings();
            if (!File.Exists(jsonPath)) return false;

            try
            {
                var oldContent = File.ReadAllText(jsonPath, Encoding.UTF8);
                var parsed = JsonSerializer.Deserialize<AppSettings>(oldContent, JsonOptions);
                var legacy = JsonSerializer.Deserialize<LegacySettingsDto>(oldContent, JsonOptions);

                if (parsed == null && legacy == null) return false;

                result = parsed ?? new AppSettings();
                // 若旧版仅包含 IsPinned / IsPassThrough 字段，则映射至新字段并遵循“穿透必置顶”
                if (legacy != null)
                {
                    if (legacy.IsPinned.HasValue && !result.TodoIsPinned)
                        result.TodoIsPinned = legacy.IsPinned.Value;
                    if (legacy.IsPassThrough.HasValue && !result.TodoIsPassThrough)
                        result.TodoIsPassThrough = legacy.IsPassThrough.Value;
                }
                if (result.TodoIsPassThrough)
                {
                    result.TodoIsPinned = true;
                }
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SettingsManager: 旧配置迁移失败 ({jsonPath}: {ex.Message})");
                return false;
            }
        }

        private void EnterReadFault(string reason)
        {
            HasReadFault = true;
            if (_faultNotified) return;
            _faultNotified = true;
            string fileName = Path.GetFileName(_settingsFile);
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                System.Windows.MessageBox.Show(
                    $"配置文件 ({fileName}) 读取遇到异常（{reason}），已开启保护模式：本次会话使用默认配置且不会覆写磁盘文件，请检查文件并适时重启软件。",
                    "PinToDesk 配置保护",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }));
        }
    }
}
