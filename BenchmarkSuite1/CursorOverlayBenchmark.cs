using BenchmarkDotNet.Attributes;
using MGK_Analyzer.Controls;
using Syncfusion.UI.Xaml.Charts;
using System;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VSDiagnostics;

namespace BenchmarkSuite1
{
    [CPUUsageDiagnoser]
    public class CursorOverlayBenchmark
    {
        private Dispatcher? _dispatcher;
        private Thread? _uiThread;
        private AutoResetEvent? _ready;
        private MdiChartWindow? _window;
        private object? _handle;
        private MethodInfo? _updateOverlayLine;
        [GlobalSetup]
        public void Setup()
        {
            _ready = new AutoResetEvent(false);
            _uiThread = new Thread(() =>
            {
                _dispatcher = Dispatcher.CurrentDispatcher;
                var host = new Window
                {
                    Width = 800,
                    Height = 600,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Visibility = Visibility.Hidden
                };
                _window = new MdiChartWindow();
                host.Content = _window;
                host.Show();
                host.UpdateLayout();
                var handleType = typeof(MdiChartWindow).GetNestedType("ChartCursorHandle", BindingFlags.NonPublic);
                _handle = handleType != null ? Activator.CreateInstance(handleType) : null;
                var annotation = new VerticalLineAnnotation
                {
                    X1 = 0,
                    X2 = 0,
                    Y1 = 0,
                    Y2 = 1,
                    Stroke = Brushes.Crimson
                };
                handleType?.GetProperty("Annotation")?.SetValue(_handle, annotation);
                handleType?.GetProperty("CursorBrush")?.SetValue(_handle, Brushes.Crimson);
                var overlayLine = typeof(MdiChartWindow).GetMethod("CreateCursorOverlayLine", BindingFlags.Instance | BindingFlags.NonPublic)?.Invoke(_window, new object[] { Brushes.Crimson });
                handleType?.GetProperty("OverlayLine")?.SetValue(_handle, overlayLine);
                _updateOverlayLine = typeof(MdiChartWindow).GetMethod("UpdateOverlayLine", BindingFlags.Instance | BindingFlags.NonPublic);
                _ready?.Set();
                Dispatcher.Run();
            });
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.IsBackground = true;
            _uiThread.Start();
            _ready.WaitOne();
        }

        [Benchmark]
        public void UpdateOverlayLine()
        {
            if (_dispatcher == null || _window == null || _handle == null || _updateOverlayLine == null)
            {
                return;
            }

            _dispatcher.Invoke(() => _updateOverlayLine.Invoke(_window, new[] { _handle }));
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            if (_dispatcher != null)
            {
                _dispatcher.InvokeShutdown();
            }

            _uiThread?.Join();
            _ready?.Dispose();
        }
    }
}