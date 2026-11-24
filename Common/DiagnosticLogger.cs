using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PinToDesk.Common
{
    /// <summary>
    /// 异常诊断与崩溃前操作轨迹记录器（仅在定义 PTD_DIAG 时生效；正常运行不落盘日志文件）
    /// </summary>
    public static class DiagnosticLogger
    {
        private const int MaxBreadcrumbs = 50;
        private const int SessionMapCapacityBytes = 16 * 1024; // 16 KB 内存映射区
        private const long MaxCrashLogBytes = 5 * 1024 * 1024; // 5 MB 自动轮转

        private static readonly string SessionDiagPath = Path.Combine(AppPaths.AppData, ".session.diag");
        private static readonly string OldCrashLogPath = Path.Combine(AppPaths.AppData, "crash.old.log");

        private static readonly object _stepLock = new();
        private static readonly object _fileLock = new();
        private static readonly Queue<string> _breadcrumbs = new(MaxBreadcrumbs);
        private static readonly ConcurrentDictionary<string, long> _firstChanceCooldown = new();

        private static MemoryMappedFile? _mmf;
        private static MemoryMappedViewAccessor? _accessor;
        private static volatile bool _initialized;
        private static volatile bool _shuttingDown;
        private static string _sessionHeader = string.Empty;

        [ThreadStatic]
        private static bool _inHandler;

        /// <summary>
        /// 初始化异常诊断服务：恢复上次异常退出记录、创建内存映射轨迹区、挂载异常监听
        /// </summary>
        [Conditional("PTD_DIAG")]
        public static void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            try
            {
                AppPaths.EnsureDirectories();

                // 1. 检查上次会话是否非正常退出
                string? previousSessionContent = TryRecoverPreviousSession();

                // 2. 初始化本次会话的内存映射文件（用于实时保存最近 50 条操作轨迹，正常退出时删除）
                InitSessionMemoryMap();

                RecordStep("App", "DiagnosticLogger 已初始化");

                // 3. 若检测到上次非正常退出，在后台提取系统事件日志并与上次操作轨迹汇总写入 crash.log
                if (!string.IsNullOrEmpty(previousSessionContent))
                {
                    string captured = previousSessionContent;
                    Task.Run(() => WriteAbnormalExitReport(captured));
                }

                // 4. 挂载 FirstChanceException（仅捕获关键内部异常并限流记录）
                AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;

                // 5. 挂载 WinForms 线程异常与进程退出兜底
                System.Windows.Forms.Application.ThreadException += (s, e) =>
                {
                    LogException("WinForms.ThreadException", e.Exception);
                };

                AppDomain.CurrentDomain.ProcessExit += (s, e) =>
                {
                    Shutdown("ProcessExit");
                };
            }
            catch
            {
                // 诊断初始化异常静默兜底，绝不影响主程序启动
            }
        }

        /// <summary>
        /// 记录一条脱敏操作轨迹（仅保存在内存与 16KB 内存映射视图中，正常运行时不写日志文件）
        /// </summary>
        [Conditional("PTD_DIAG")]
        public static void RecordStep(string module, string action)
        {
            if (!_initialized || _shuttingDown) return;

            try
            {
                string line = $"[{DateTime.Now:HH:mm:ss.fff}] [T{Environment.CurrentManagedThreadId}] [{module}] {action}";
                lock (_stepLock)
                {
                    if (_breadcrumbs.Count >= MaxBreadcrumbs)
                    {
                        _breadcrumbs.Dequeue();
                    }
                    _breadcrumbs.Enqueue(line);
                    FlushBreadcrumbsToMemoryMap_NoLock();
                }
            }
            catch
            {
                // 忽略轨迹记录异常
            }
        }

        /// <summary>
        /// 记录未处理异常或关键捕获异常到 Data/crash.log（附带最近 50 条操作轨迹）
        /// </summary>
        [Conditional("PTD_DIAG")]
        public static void LogException(string source, Exception? ex, bool isFirstChance = false)
        {
            if (ex == null) return;

            try
            {
                AppPaths.EnsureDirectories();
                string stepsSnapshot = GetBreadcrumbsSnapshot();

                var sb = new StringBuilder();
                sb.AppendLine($"==================== {(isFirstChance ? "内部捕获异常 (FirstChance)" : "未处理/关键异常")} ====================");
                sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine($"来源: {source}");
                sb.AppendLine($"线程: ManagedId={Environment.CurrentManagedThreadId}, IsThreadPool={Thread.CurrentThread.IsThreadPoolThread}");
                sb.AppendLine($"异常: {FormatExceptionChain(ex)}");
                sb.AppendLine("--- 异常前最近操作轨迹 (最多 50 条) ---");
                sb.Append(string.IsNullOrWhiteSpace(stepsSnapshot) ? "(无记录)\r\n" : stepsSnapshot);
                sb.AppendLine(new string('=', 72));

                AppendToCrashLog(sb.ToString());
            }
            catch
            {
                // 静默兜底
            }
        }

        /// <summary>
        /// 正常退出或系统关机时调用，清理会话内存映射文件
        /// </summary>
        [Conditional("PTD_DIAG")]
        public static void Shutdown(string reason = "NormalExit")
        {
            if (_shuttingDown) return;
            _shuttingDown = true;

            try
            {
                AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
            }
            catch { }

            lock (_stepLock)
            {
                try
                {
                    _accessor?.Dispose();
                    _accessor = null;
                }
                catch { }

                try
                {
                    _mmf?.Dispose();
                    _mmf = null;
                }
                catch { }

                try
                {
                    if (File.Exists(SessionDiagPath))
                    {
                        File.Delete(SessionDiagPath);
                    }
                }
                catch { }
            }
        }

        private static void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (_inHandler || _shuttingDown) return;

            var ex = e.Exception;
            if (ex == null) return;

            if (ex is OperationCanceledException || ex is TaskCanceledException) return;
            if (ex.HResult == unchecked((int)0x800401D3) || ex.HResult == unchecked((int)0x8001010D)) return;

            bool isRelevantType = ex is ExternalException
                               || ex is COMException
                               || ex is InvalidOperationException
                               || ex is IOException
                               || ex is UnauthorizedAccessException
                               || ex is OutOfMemoryException
                               || ex is NullReferenceException
                               || ex is ArgumentException;

            if (!isRelevantType) return;

            string? targetNamespace = ex.TargetSite?.DeclaringType?.FullName;
            string? stack = ex.StackTrace;
            bool inPinToDesk = (targetNamespace != null && targetNamespace.StartsWith("PinToDesk", StringComparison.Ordinal))
                            || (stack != null && stack.Contains("PinToDesk.", StringComparison.Ordinal));

            if (!inPinToDesk && ex is not ExternalException && ex is not COMException)
            {
                return;
            }

            try
            {
                _inHandler = true;

                string key = $"{ex.GetType().FullName}|{ex.TargetSite?.DeclaringType?.Name}.{ex.TargetSite?.Name}|{ex.HResult:X8}";
                long nowMs = Environment.TickCount64;
                if (_firstChanceCooldown.TryGetValue(key, out long lastMs) && (nowMs - lastMs) < 15000)
                {
                    return;
                }
                _firstChanceCooldown[key] = nowMs;

                RecordStep("Exception", $"捕获 FirstChance: {ex.GetType().Name} (0x{ex.HResult:X8}) at {ex.TargetSite?.Name}");

                string snapshot = GetBreadcrumbsSnapshot();
                string exText = FormatExceptionChain(ex);
                string sourceInfo = $"FirstChanceException ({ex.TargetSite?.DeclaringType?.Name}.{ex.TargetSite?.Name})";

                Task.Run(() =>
                {
                    try
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("==================== 内部捕获异常 (FirstChance) ====================");
                        sb.AppendLine($"时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                        sb.AppendLine($"来源: {sourceInfo}");
                        sb.AppendLine($"异常: {exText}");
                        sb.AppendLine("--- 异常前最近操作轨迹 (最多 50 条) ---");
                        sb.Append(string.IsNullOrWhiteSpace(snapshot) ? "(无记录)\r\n" : snapshot);
                        sb.AppendLine(new string('=', 72));
                        AppendToCrashLog(sb.ToString());
                    }
                    catch { }
                });
            }
            catch
            {
                // 静默兜底
            }
            finally
            {
                _inHandler = false;
            }
        }

        private static string? TryRecoverPreviousSession()
        {
            try
            {
                if (!File.Exists(SessionDiagPath)) return null;

                byte[] rawBytes = File.ReadAllBytes(SessionDiagPath);
                try
                {
                    File.Delete(SessionDiagPath);
                }
                catch { }

                int validLen = Array.IndexOf(rawBytes, (byte)0);
                if (validLen < 0) validLen = rawBytes.Length;
                if (validLen == 0) return null;

                string text = Encoding.UTF8.GetString(rawBytes, 0, validLen).Trim();
                return string.IsNullOrEmpty(text) ? null : text;
            }
            catch
            {
                return null;
            }
        }

        private static void InitSessionMemoryMap()
        {
            FileStream? fs = null;
            MemoryMappedFile? mmf = null;
            MemoryMappedViewAccessor? accessor = null;
            try
            {
                var ver = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "unknown";
                _sessionHeader = $"SessionStart: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | PID: {Environment.ProcessId} | Version: {ver}\r\n";

                fs = new FileStream(
                    SessionDiagPath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete);

                mmf = MemoryMappedFile.CreateFromFile(
                    fs,
                    null,
                    SessionMapCapacityBytes,
                    MemoryMappedFileAccess.ReadWrite,
                    HandleInheritability.None,
                    leaveOpen: false);
                fs = null;

                accessor = mmf.CreateViewAccessor(0, SessionMapCapacityBytes, MemoryMappedFileAccess.ReadWrite);
                _mmf = mmf;
                _accessor = accessor;
                mmf = null;
                accessor = null;

                FlushBreadcrumbsToMemoryMap_NoLock();
            }
            catch
            {
                try { accessor?.Dispose(); } catch { }
                try { mmf?.Dispose(); } catch { }
                try { fs?.Dispose(); } catch { }
            }
        }

        private static void FlushBreadcrumbsToMemoryMap_NoLock()
        {
            if (_accessor == null) return;

            var sb = new StringBuilder(2048);
            sb.Append(_sessionHeader);
            sb.AppendLine($"LastActive: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine("--- 最近操作轨迹 ---");
            foreach (var item in _breadcrumbs)
            {
                sb.AppendLine(item);
            }

            byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
            int writeLen = Math.Min(bytes.Length, SessionMapCapacityBytes - 1);
            _accessor.WriteArray(0, bytes, 0, writeLen);
            _accessor.Write(writeLen, (byte)0);
        }

        private static string GetBreadcrumbsSnapshot()
        {
            lock (_stepLock)
            {
                var sb = new StringBuilder();
                foreach (var item in _breadcrumbs)
                {
                    sb.AppendLine(item);
                }
                return sb.ToString();
            }
        }

        private static void WriteAbnormalExitReport(string previousSessionContent)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("==================== 检测到上次会话非正常退出 / 闪退 ====================");
                sb.AppendLine($"检测时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
                sb.AppendLine("--- 上次会话残留状态与最后操作轨迹 ---");
                sb.AppendLine(previousSessionContent);
                sb.AppendLine("--- Windows 系统事件日志关联崩溃记录 (最近 48 小时) ---");
                sb.AppendLine(QueryRecentWindowsCrashEvents());
                sb.AppendLine(new string('=', 72));

                AppendToCrashLog(sb.ToString());
            }
            catch
            {
                // 静默兜底
            }
        }

        private static string QueryRecentWindowsCrashEvents()
        {
            var sb = new StringBuilder();
            try
            {
                long msWindow = 48L * 3600L * 1000L;
                string xpath = $"*[System[(Provider[@Name='.NET Runtime' or @Name='Application Error' or @Name='Windows Error Reporting']) and TimeCreated[timediff(@SystemTime) <= {msWindow}]]]";
                var query = new EventLogQuery("Application", PathType.LogName, xpath)
                {
                    ReverseDirection = true
                };

                using var reader = new EventLogReader(query);
                int matched = 0;
                int scanned = 0;

                for (EventRecord? record = reader.ReadEvent(); record != null && matched < 5 && scanned < 200; record = reader.ReadEvent())
                {
                    using (record)
                    {
                        scanned++;
                        string? desc = null;
                        try
                        {
                            desc = record.FormatDescription();
                        }
                        catch { }

                        var propsText = new StringBuilder();
                        if (record.Properties != null)
                        {
                            foreach (var prop in record.Properties)
                            {
                                if (prop?.Value != null)
                                {
                                    propsText.Append(prop.Value).Append(" | ");
                                }
                            }
                        }

                        string combined = $"{desc}\n{propsText}";
                        if (combined.Contains("PinToDesk", StringComparison.OrdinalIgnoreCase))
                        {
                            matched++;
                            sb.AppendLine($"[事件 #{matched}] 时间={record.TimeCreated:yyyy-MM-dd HH:mm:ss} | ID={record.Id} | 来源={record.ProviderName}");
                            if (!string.IsNullOrWhiteSpace(desc))
                            {
                                sb.AppendLine(desc.Trim());
                            }
                            else
                            {
                                sb.AppendLine(propsText.ToString());
                            }
                            sb.AppendLine(new string('-', 50));
                        }
                    }
                }

                if (matched == 0)
                {
                    sb.AppendLine("(未在系统 Application 事件日志中检索到最近 48 小时的 PinToDesk 崩溃记录)");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"(读取系统事件日志失败: {ex.Message})");
            }
            return sb.ToString();
        }

        private static string FormatExceptionChain(Exception ex)
        {
            var sb = new StringBuilder();
            Exception? cur = ex;
            int depth = 0;
            while (cur != null && depth < 5)
            {
                if (depth > 0) sb.AppendLine($"---> InnerException [{depth}]:");
                sb.AppendLine($"{cur.GetType().FullName} (HResult=0x{cur.HResult:X8}): {cur.Message}");
                if (!string.IsNullOrWhiteSpace(cur.StackTrace))
                {
                    sb.AppendLine(cur.StackTrace);
                }
                cur = cur.InnerException;
                depth++;
            }
            return sb.ToString().TrimEnd();
        }

        private static void AppendToCrashLog(string content)
        {
            lock (_fileLock)
            {
                try
                {
                    AppPaths.EnsureDirectories();
                    if (File.Exists(AppPaths.CrashLog))
                    {
                        var info = new FileInfo(AppPaths.CrashLog);
                        if (info.Length > MaxCrashLogBytes)
                        {
                            File.Move(AppPaths.CrashLog, OldCrashLogPath, overwrite: true);
                        }
                    }
                    File.AppendAllText(AppPaths.CrashLog, content, Encoding.UTF8);
                }
                catch
                {
                    // 静默兜底
                }
            }
        }
    }
}
