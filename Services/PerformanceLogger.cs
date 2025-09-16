using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace MGK_Analyzer.Services
{
    public class PerformanceLogger : INotifyPropertyChanged
    {
        private static PerformanceLogger _instance;
        private static readonly object _lock = new object();
        
        private ObservableCollection<LogEntry> _logEntries;
        private Stopwatch _globalStopwatch;

        public static PerformanceLogger Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        if (_instance == null)
                            _instance = new PerformanceLogger();
                    }
                }
                return _instance;
            }
        }

        private PerformanceLogger()
        {
            _logEntries = new ObservableCollection<LogEntry>();
            _globalStopwatch = Stopwatch.StartNew();
        }

        public ObservableCollection<LogEntry> LogEntries => _logEntries;

        public void LogInfo(string message, string category = "General")
        {
            Log(LogLevel.Info, message, category);
        }

        public void LogWarning(string message, string category = "General")
        {
            Log(LogLevel.Warning, message, category);
        }

        public void LogError(string message, string category = "General")
        {
            Log(LogLevel.Error, message, category);
        }

        public void LogPerformance(string operation, TimeSpan duration, string category = "Performance")
        {
            Log(LogLevel.Performance, $"{operation}: {duration.TotalMilliseconds:F2}ms", category);
        }

        public IDisposable StartTimer(string operation, string category = "Performance")
        {
            return new PerformanceTimer(this, operation, category);
        }

        private void Log(LogLevel level, string message, string category)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                ElapsedTime = _globalStopwatch.Elapsed,
                Level = level,
                Category = category,
                Message = message
            };

            // UI 스레드에서 실행
            if (System.Windows.Application.Current?.Dispatcher != null)
            {
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
                {
                    _logEntries.Insert(0, entry); // 최신 로그가 위에 오도록
                    
                    // 로그가 너무 많아지면 오래된 것 제거 (최대 1000개)
                    while (_logEntries.Count > 1000)
                    {
                        _logEntries.RemoveAt(_logEntries.Count - 1);
                    }
                });
            }

            // 콘솔에도 출력
            Console.WriteLine($"[{entry.Timestamp:HH:mm:ss.fff}] [{entry.ElapsedTime.TotalSeconds:F3}s] [{level}] [{category}] {message}");
        }

        public void Clear()
        {
            _logEntries.Clear();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; }
        public string Message { get; set; }
        
        public string DisplayText => $"[{Timestamp:HH:mm:ss.fff}] [{ElapsedTime.TotalSeconds:F3}s] [{Level}] [{Category}] {Message}";
        
        public System.Windows.Media.Brush TextColor
        {
            get
            {
                return Level switch
                {
                    LogLevel.Error => System.Windows.Media.Brushes.Red,
                    LogLevel.Warning => System.Windows.Media.Brushes.Orange,
                    LogLevel.Performance => System.Windows.Media.Brushes.Blue,
                    LogLevel.Info => System.Windows.Media.Brushes.Black,
                    _ => System.Windows.Media.Brushes.Gray
                };
            }
        }
    }

    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Performance
    }

    public class PerformanceTimer : IDisposable
    {
        private readonly PerformanceLogger _logger;
        private readonly string _operation;
        private readonly string _category;
        private readonly Stopwatch _stopwatch;

        public PerformanceTimer(PerformanceLogger logger, string operation, string category)
        {
            _logger = logger;
            _operation = operation;
            _category = category;
            _stopwatch = Stopwatch.StartNew();
            
            _logger.LogInfo($"시작: {_operation}", _category);
        }

        public void Dispose()
        {
            _stopwatch.Stop();
            _logger.LogPerformance(_operation, _stopwatch.Elapsed, _category);
        }
    }
}