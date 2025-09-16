using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MGK_Analyzer.Models;
using MGK_Analyzer.Services;
using Syncfusion.UI.Xaml.Charts;

namespace MGK_Analyzer.Controls
{
    public partial class MdiChartWindow : UserControl, INotifyPropertyChanged
    {
        private bool _isDragging = false;
        private bool _isResizing = false;
        private Point _lastPosition;
        private static int _globalZIndex = 1;
        private MemoryOptimizedDataSet _dataSet;
        private string _windowTitle;

        public static readonly DependencyProperty WindowTitleProperty = 
            DependencyProperty.Register("WindowTitle", typeof(string), typeof(MdiChartWindow), 
                new PropertyMetadata("Chart Window"));

        public string WindowTitle
        {
            get => (string)GetValue(WindowTitleProperty);
            set { SetValue(WindowTitleProperty, value); OnPropertyChanged(); }
        }

        public MemoryOptimizedDataSet DataSet
        {
            get => _dataSet;
            set 
            { 
                using var timer = PerformanceLogger.Instance.StartTimer("��Ʈ ������ ������ ����", "Chart_Display");
                
                _dataSet = value; 
                OnPropertyChanged();
                
                PerformanceLogger.Instance.LogInfo($"��Ʈ ������ ���� - ����: {value?.FileName}, �ø���: {value?.SeriesData?.Count}", "Chart_Display");
                InitializeSeriesList();
            }
        }

        public ObservableCollection<ChartSeriesViewModel> SeriesList { get; set; } = new ObservableCollection<ChartSeriesViewModel>();

        public event EventHandler WindowClosed;
        public event EventHandler WindowActivated;

        public MdiChartWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeSeriesList()
        {
            Console.WriteLine("=== InitializeSeriesList ���� ===");
            using var timer = PerformanceLogger.Instance.StartTimer("�ø��� ����Ʈ �ʱ�ȭ", "Chart_Display");
            
            SeriesList.Clear();
            if (DataSet?.SeriesData != null)
            {
                Console.WriteLine($"DataSet.SeriesData.Count: {DataSet.SeriesData.Count}");
                
                foreach (var series in DataSet.SeriesData.Values)
                {
                    Console.WriteLine($"�ø��� �߰�: {series.Name} ({series.Unit}) - DataType: {series.DataType}");
                    
                    var viewModel = new ChartSeriesViewModel { SeriesData = series };
                    viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
                    SeriesList.Add(viewModel);
                }
                
                Console.WriteLine($"�ø��� ����Ʈ �ʱ�ȭ �Ϸ� - {SeriesList.Count}�� �ø���");
                PerformanceLogger.Instance.LogInfo($"�ø��� ����Ʈ �ʱ�ȭ �Ϸ� - {SeriesList.Count}�� �ø���", "Chart_Display");
            }
            else
            {
                Console.WriteLine("ERROR: DataSet �Ǵ� SeriesData�� null�Դϴ�!");
            }
            
            Console.WriteLine("=== InitializeSeriesList �Ϸ� ===");
        }

        private async void SeriesViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChartSeriesViewModel.IsSelected))
            {
                var viewModel = (ChartSeriesViewModel)sender;
                var seriesName = viewModel.SeriesData.Name;
                
                PerformanceLogger.Instance.LogInfo($"�ø��� ���� ����: {seriesName} -> {viewModel.IsSelected}", "Chart_Display");
                Console.WriteLine($"=== �ø��� ���� ����: {seriesName} -> {viewModel.IsSelected} ===");
                
                if (viewModel.IsSelected)
                {
                    // UI ���伺�� ���� �񵿱�� ó��
                    await Task.Run(() =>
                    {
                        using var timer = PerformanceLogger.Instance.StartTimer($"�ø��� �߰�: {seriesName}", "Chart_Display");
                        Console.WriteLine($"�񵿱� �ø��� �߰� ����: {seriesName}");
                        
                        // UI �����忡�� ��Ʈ ������Ʈ
                        Dispatcher.BeginInvoke(() => AddSeriesToChart(viewModel.SeriesData));
                    });
                }
                else
                {
                    using var timer = PerformanceLogger.Instance.StartTimer($"�ø��� ����: {seriesName}", "Chart_Display");
                    RemoveSeriesFromChart(viewModel.SeriesData);
                }
            }
        }

        private void AddSeriesToChart(SeriesData series)
        {
            Console.WriteLine($"=== AddSeriesToChart ����: {series.Name} ===");
            using var overallTimer = PerformanceLogger.Instance.StartTimer($"��ü ��Ʈ �ø��� �߰�: {series.Name}", "Chart_Display");
            
            // ������ ����
            Console.WriteLine($"DataSet.TotalSamples: {DataSet?.TotalSamples}");
            Console.WriteLine($"Series.Values ����: {series.Values?.Length}");
            Console.WriteLine($"Series.DataType: {series.DataType}");
            
            if (DataSet == null)
            {
                Console.WriteLine("ERROR: DataSet�� null�Դϴ�!");
                return;
            }
            
            if (DataSet.TotalSamples == 0)
            {
                Console.WriteLine("ERROR: TotalSamples�� 0�Դϴ�!");
                return;
            }
            
            // ��뷮 �������� ��� �ٿ���ø� ���� (���� ����)
            var maxPoints = 500; // �� ���� ����Ʈ�� �����Ͽ� ���� ����
            var totalPoints = DataSet.TotalSamples;
            var step = Math.Max(1, totalPoints / maxPoints);
            
            Console.WriteLine($"�ٿ���ø� ���� - ��ü: {totalPoints:N0}, �ܰ�: {step}, ǥ��: {totalPoints/step:N0}�� ����Ʈ");
            PerformanceLogger.Instance.LogInfo($"�ٿ���ø� ���� - ��ü: {totalPoints:N0}, �ܰ�: {step}, ǥ��: {totalPoints/step:N0}�� ����Ʈ", "Chart_Display");
            
            try
            {
                if (series.DataType == typeof(bool))
                {
                    Console.WriteLine("Bool �ø��� ���� ��...");
                    using var seriesTimer = PerformanceLogger.Instance.StartTimer($"StepLine �ø��� ����: {series.Name}", "Chart_Display");
                    
                    var stepSeries = new StepLineSeries
                    {
                        XBindingPath = "Time",
                        YBindingPath = "Value",
                        Label = series.Name,
                        Stroke = series.Color,
                        StrokeThickness = 1
                    };

                    var dataPoints = CreateBoolDataPoints(series, step);
                    Console.WriteLine($"Bool ������ ����Ʈ ������: {dataPoints.Count}��");
                    
                    stepSeries.ItemsSource = dataPoints;
                    ChartControl.Series.Add(stepSeries);
                    Console.WriteLine($"StepLine �ø��� ��Ʈ�� �߰���: {series.Name}");
                    
                    PerformanceLogger.Instance.LogInfo($"Bool �ø��� �߰� �Ϸ�: {series.Name} ({dataPoints.Count:N0}�� ����Ʈ)", "Chart_Display");
                }
                else
                {
                    Console.WriteLine("Double �ø��� ���� ��...");
                    using var seriesTimer = PerformanceLogger.Instance.StartTimer($"Line �ø��� ����: {series.Name}", "Chart_Display");
                    
                    var lineSeries = new LineSeries
                    {
                        XBindingPath = "Time",
                        YBindingPath = "Value",
                        Label = series.Name,
                        Stroke = series.Color,
                        StrokeThickness = 1
                    };

                    var dataPoints = CreateDoubleDataPoints(series, step);
                    Console.WriteLine($"Double ������ ����Ʈ ������: {dataPoints.Count}��");
                    
                    // ������ ���� Ȯ��
                    if (dataPoints.Count > 0)
                    {
                        Console.WriteLine($"ù ��° ������ ����Ʈ: Time={dataPoints[0].Time}, Value={dataPoints[0].Value}");
                        if (dataPoints.Count > 1)
                        {
                            Console.WriteLine($"�� ��° ������ ����Ʈ: Time={dataPoints[1].Time}, Value={dataPoints[1].Value}");
                        }
                    }
                    
                    Console.WriteLine("�ø�� ��Ʈ�� �߰��ϱ� ����...");
                    lineSeries.ItemsSource = dataPoints;
                    ChartControl.Series.Add(lineSeries);
                    Console.WriteLine($"Line �ø��� ��Ʈ�� �߰� �Ϸ�: {series.Name}");
                    
                    PerformanceLogger.Instance.LogInfo($"Double �ø��� �߰� �Ϸ�: {series.Name} ({dataPoints.Count:N0}�� ����Ʈ)", "Chart_Display");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: �ø��� �߰� �� ���� - {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                PerformanceLogger.Instance.LogError($"�ø��� �߰� ����: {series.Name} - {ex.Message}", "Chart_Display");
            }
            
            // ���� ǥ�� Ȱ��ȭ
            try
            {
                if (ChartControl.Legend is ChartLegend legend)
                {
                    legend.Visibility = Visibility.Visible;
                    Console.WriteLine("���� ǥ�� Ȱ��ȭ��");
                }
                else
                {
                    Console.WriteLine("WARNING: Legend�� null�̰ų� ChartLegend Ÿ���� �ƴմϴ�");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: ���� ���� �� ���� - {ex.Message}");
            }
            
            Console.WriteLine($"���� ��Ʈ�� �� �ø��� ��: {ChartControl.Series.Count}");
            PerformanceLogger.Instance.LogInfo($"���� ��Ʈ�� �� �ø��� ��: {ChartControl.Series.Count}", "Chart_Display");
            
            Console.WriteLine($"=== AddSeriesToChart �Ϸ�: {series.Name} ===");
        }

        private ObservableCollection<ChartDataPoint> CreateBoolDataPoints(SeriesData series, int step)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Bool ������ ����Ʈ ����: {series.Name}", "Chart_Display");
            
            var dataPoints = new ObservableCollection<ChartDataPoint>();
            var pointCount = 0;
            
            for (int i = 0; i < DataSet.TotalSamples && pointCount < 10000; i += step)
            {
                bool value = GetBoolValue(series.BitValues, i);
                dataPoints.Add(new ChartDataPoint
                {
                    Time = DataSet.GetTimeAt(i),
                    Value = value ? 1.0 : 0.0,
                    SeriesName = series.Name
                });
                pointCount++;
            }
            
            PerformanceLogger.Instance.LogInfo($"Bool ������ ����Ʈ ���� �Ϸ�: {pointCount:N0}��", "Chart_Display");
            return dataPoints;
        }

        private ObservableCollection<ChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step)
        {
            Console.WriteLine($"=== CreateDoubleDataPoints ����: {series.Name} ===");
            using var timer = PerformanceLogger.Instance.StartTimer($"Double ������ ����Ʈ ����: {series.Name}", "Chart_Display");
            
            var dataPoints = new ObservableCollection<ChartDataPoint>();
            var pointCount = 0;
            var maxPointsToCreate = 500; // ������ ���� ����
            
            Console.WriteLine($"TotalSamples: {DataSet.TotalSamples}, Step: {step}, MaxPoints: {maxPointsToCreate}");
            Console.WriteLine($"BaseTime: {DataSet.BaseTime}, TimeInterval: {DataSet.TimeInterval}");
            
            // ���� ������ �� Ȯ��
            if (series.Values != null && series.Values.Length > 0)
            {
                Console.WriteLine($"Values �迭 ����: {series.Values.Length}");
                var firstFiveValues = series.Values.Take(5).ToArray();
                Console.WriteLine($"ù 5�� ��: [{string.Join(", ", firstFiveValues)}]");
                
                var nonZeroCount = series.Values.Take(Math.Min(100, series.Values.Length)).Count(v => v != 0 && !float.IsNaN(v));
                Console.WriteLine($"ó�� 100�� �� 0�� �ƴ� ���� ����: {nonZeroCount}");
            }
            else
            {
                Console.WriteLine("ERROR: Values �迭�� null�̰ų� ����ֽ��ϴ�!");
                return dataPoints;
            }
            
            // ������ ���������� ó��
            var maxSafeIndex = Math.Min(DataSet.TotalSamples, series.Values.Length);
            Console.WriteLine($"���� ó�� ����: 0 ~ {maxSafeIndex-1}");
            
            for (int i = 0; i < maxSafeIndex && pointCount < maxPointsToCreate; i += step)
            {
                try
                {
                    var time = DataSet.GetTimeAt(i);
                    var value = series.Values[i];
                    
                    // NaN, Infinity �� üũ
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        continue;
                    }
                    
                    dataPoints.Add(new ChartDataPoint
                    {
                        Time = time,
                        Value = value,
                        SeriesName = series.Name
                    });
                    
                    // ó�� 3�� ����Ʈ�� �α� ���
                    if (pointCount < 3)
                    {
                        Console.WriteLine($"����Ʈ {pointCount}: Time={time:HH:mm:ss}, Value={value}");
                    }
                    
                    pointCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: ������ ����Ʈ ���� �� ���� (�ε��� {i}): {ex.Message}");
                    break;
                }
            }
            
            Console.WriteLine($"Double ������ ����Ʈ ���� �Ϸ�: {pointCount:N0}��");
            PerformanceLogger.Instance.LogInfo($"Double ������ ����Ʈ ���� �Ϸ�: {pointCount:N0}��", "Chart_Display");
            
            Console.WriteLine($"=== CreateDoubleDataPoints �Ϸ�: {series.Name} ===");
            return dataPoints;
        }

        private void RemoveSeriesFromChart(SeriesData series)
        {
            var seriesToRemove = ChartControl.Series.FirstOrDefault(s => 
                s.Label?.ToString() == series.Name);
            if (seriesToRemove != null)
            {
                ChartControl.Series.Remove(seriesToRemove);
            }
        }

        private bool GetBoolValue(byte[] bitValues, int index)
        {
            if (bitValues == null) return false;
            int byteIndex = index / 8;
            int bitIndex = index % 8;
            return byteIndex < bitValues.Length && (bitValues[byteIndex] & (1 << bitIndex)) != 0;
        }

        // Ÿ��Ʋ�� �巡��
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            var canvas = (Canvas)this.Parent;
            _lastPosition = e.GetPosition(canvas);
            this.CaptureMouse();
            
            // Ȱ�� ������� ����
            Canvas.SetZIndex(this, ++_globalZIndex);
            WindowActivated?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas)this.Parent;
                if (canvas == null) return;

                var currentPosition = e.GetPosition(canvas);
                
                var deltaX = currentPosition.X - _lastPosition.X;
                var deltaY = currentPosition.Y - _lastPosition.Y;
                
                var newLeft = Canvas.GetLeft(this) + deltaX;
                var newTop = Canvas.GetTop(this) + deltaY;
                
                // ��� üũ
                newLeft = Math.Max(0, Math.Min(newLeft, canvas.ActualWidth - this.ActualWidth));
                newTop = Math.Max(0, Math.Min(newTop, canvas.ActualHeight - this.ActualHeight));
                
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
                
                _lastPosition = currentPosition;
            }
            
            if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas)this.Parent;
                if (canvas == null) return;

                var currentPosition = e.GetPosition(canvas);
                
                var newWidth = Math.Max(400, currentPosition.X - Canvas.GetLeft(this));
                var newHeight = Math.Max(300, currentPosition.Y - Canvas.GetTop(this));
                
                this.Width = Math.Min(newWidth, canvas.ActualWidth - Canvas.GetLeft(this));
                this.Height = Math.Min(newHeight, canvas.ActualHeight - Canvas.GetTop(this));
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                this.ReleaseMouseCapture();
            }
            base.OnMouseLeftButtonUp(e);
        }

        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isResizing = true;
            this.CaptureMouse();
            e.Handled = true;
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.Height = 30; // Ÿ��Ʋ�ٸ� ���̵���
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                this.Width = canvas.ActualWidth - 10;
                this.Height = canvas.ActualHeight - 10;
                Canvas.SetLeft(this, 5);
                Canvas.SetTop(this, 5);
            }
        }

        public void Close_Click(object sender, RoutedEventArgs e)
        {
            WindowClosed?.Invoke(this, EventArgs.Empty);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}