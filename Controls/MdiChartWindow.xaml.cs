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
using System.Windows.Media.Animation;
using MGK_Analyzer.Models;
using MGK_Analyzer.Services;
using Syncfusion.UI.Xaml.Charts;

namespace MGK_Analyzer.Controls
{
    public enum ResizeDirection
    {
        None,
        Top,
        Bottom,
        Left,
        Right,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public partial class MdiChartWindow : UserControl, INotifyPropertyChanged
    {
        private bool _isDragging = false;
        private bool _isResizing = false;
        private Point _lastPosition;
        private static int _globalZIndex = 1;
        private MemoryOptimizedDataSet? _dataSet;
        private bool _isManagementPanelExpanded = true;
        private bool _isManagementPanelPinned = false;
        
        // ������ ũ�� ���� ����
        private double _normalWidth = 800;
        private double _normalHeight = 600;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private bool _isMaximized = false;
        private bool _isMinimized = false;
        
        // �������� ���� ����
        private ResizeDirection _resizeDirection = ResizeDirection.None;
        private Point _resizeStartPosition;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private double _resizeStartLeft;
        private double _resizeStartTop;

        public static readonly DependencyProperty WindowTitleProperty = 
            DependencyProperty.Register("WindowTitle", typeof(string), typeof(MdiChartWindow), 
                new PropertyMetadata("Chart Window"));

        public string WindowTitle
        {
            get => (string)GetValue(WindowTitleProperty);
            set { SetValue(WindowTitleProperty, value); OnPropertyChanged(); }
        }

        public MemoryOptimizedDataSet? DataSet
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

        public event EventHandler? WindowClosed;
        public event EventHandler? WindowActivated;

        public MdiChartWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            // �ʱ� ����: �г� ǥ��
            _isManagementPanelExpanded = true;
            _isManagementPanelPinned = false;
        }

        #region ���� �г� �ڵ� ����/���� ���

        private void ManagementPanelTab_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isManagementPanelExpanded && !_isManagementPanelPinned)
            {
                ExpandManagementPanel(true);
            }
        }

        private void ManagementPanelTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isManagementPanelExpanded)
            {
                ExpandManagementPanel(true);
            }
        }

        private void ManagementPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(ManagementPanel);
            var bounds = new Rect(0, 0, ManagementPanel.ActualWidth, ManagementPanel.ActualHeight);
            
            if (_isManagementPanelExpanded && !_isManagementPanelPinned && !bounds.Contains(position))
            {
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(300);
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    if (!_isManagementPanelPinned && !ManagementPanel.IsMouseOver)
                    {
                        CollapseManagementPanel(true);
                    }
                };
                timer.Start();
            }
        }

        private void PanelPinButton_Checked(object sender, RoutedEventArgs e)
        {
            _isManagementPanelPinned = true;
            
            if (!_isManagementPanelExpanded)
            {
                ExpandManagementPanel(true);
            }
        }

        private void PanelPinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isManagementPanelPinned = false;
        }

        private void ExpandManagementPanel(bool animated)
        {
            _isManagementPanelExpanded = true;
            
            if (animated)
            {
                var storyboard = FindResource("PanelSlideInStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    storyboard.Begin();
                }
            }
            else
            {
                ManagementPanel.Width = 250;
            }
            
            ManagementPanelTab.Visibility = Visibility.Collapsed;
        }

        private void CollapseManagementPanel(bool animated)
        {
            _isManagementPanelExpanded = false;
            
            if (animated)
            {
                var storyboard = FindResource("PanelSlideOutStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    storyboard.Completed += (s, e) => 
                    {
                        ManagementPanelTab.Visibility = Visibility.Visible;
                    };
                    storyboard.Begin();
                }
            }
            else
            {
                ManagementPanel.Width = 0;
                ManagementPanelTab.Visibility = Visibility.Visible;
            }
        }

        #endregion

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
                PerformResize(currentPosition);
            }

            base.OnMouseMove(e);
        }

        private void PerformResize(Point currentPosition)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;

            var deltaX = currentPosition.X - _resizeStartPosition.X;
            var deltaY = currentPosition.Y - _resizeStartPosition.Y;

            var newWidth = _resizeStartWidth;
            var newHeight = _resizeStartHeight;
            var newLeft = _resizeStartLeft;
            var newTop = _resizeStartTop;

            // �ּ� ũ�� ����
            const double minWidth = 300;
            const double minHeight = 200;

            switch (_resizeDirection)
            {
                case ResizeDirection.Right:
                    newWidth = Math.Max(minWidth, _resizeStartWidth + deltaX);
                    break;

                case ResizeDirection.Bottom:
                    newHeight = Math.Max(minHeight, _resizeStartHeight + deltaY);
                    break;

                case ResizeDirection.Left:
                    var newWidthLeft = Math.Max(minWidth, _resizeStartWidth - deltaX);
                    if (newWidthLeft >= minWidth)
                    {
                        newWidth = newWidthLeft;
                        newLeft = _resizeStartLeft + deltaX;
                    }
                    break;

                case ResizeDirection.Top:
                    var newHeightTop = Math.Max(minHeight, _resizeStartHeight - deltaY);
                    if (newHeightTop >= minHeight)
                    {
                        newHeight = newHeightTop;
                        newTop = _resizeStartTop + deltaY;
                    }
                    break;

                case ResizeDirection.TopLeft:
                    var newWidthTL = Math.Max(minWidth, _resizeStartWidth - deltaX);
                    var newHeightTL = Math.Max(minHeight, _resizeStartHeight - deltaY);
                    if (newWidthTL >= minWidth && newHeightTL >= minHeight)
                    {
                        newWidth = newWidthTL;
                        newHeight = newHeightTL;
                        newLeft = _resizeStartLeft + deltaX;
                        newTop = _resizeStartTop + deltaY;
                    }
                    break;

                case ResizeDirection.TopRight:
                    var newWidthTR = Math.Max(minWidth, _resizeStartWidth + deltaX);
                    var newHeightTR = Math.Max(minHeight, _resizeStartHeight - deltaY);
                    if (newWidthTR >= minWidth && newHeightTR >= minHeight)
                    {
                        newWidth = newWidthTR;
                        newHeight = newHeightTR;
                        newTop = _resizeStartTop + deltaY;
                    }
                    break;

                case ResizeDirection.BottomLeft:
                    var newWidthBL = Math.Max(minWidth, _resizeStartWidth - deltaX);
                    var newHeightBL = Math.Max(minHeight, _resizeStartHeight + deltaY);
                    if (newWidthBL >= minWidth && newHeightBL >= minHeight)
                    {
                        newWidth = newWidthBL;
                        newHeight = newHeightBL;
                        newLeft = _resizeStartLeft + deltaX;
                    }
                    break;

                case ResizeDirection.BottomRight:
                    newWidth = Math.Max(minWidth, _resizeStartWidth + deltaX);
                    newHeight = Math.Max(minHeight, _resizeStartHeight + deltaY);
                    break;
            }

            // ĵ���� ��� üũ
            if (newLeft >= 0 && newLeft + newWidth <= canvas.ActualWidth &&
                newTop >= 0 && newTop + newHeight <= canvas.ActualHeight)
            {
                this.Width = newWidth;
                this.Height = newHeight;
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
            }
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                _resizeDirection = ResizeDirection.None;
                this.ReleaseMouseCapture();
            }
            base.OnMouseLeftButtonUp(e);
        }

        private void StartResize(ResizeDirection direction, Point startPosition)
        {
            _isResizing = true;
            _resizeDirection = direction;
            _resizeStartPosition = startPosition;
            _resizeStartWidth = this.ActualWidth;
            _resizeStartHeight = this.ActualHeight;
            _resizeStartLeft = Canvas.GetLeft(this);
            _resizeStartTop = Canvas.GetTop(this);
            this.CaptureMouse();
            
            // Ȱ�� ������� ����
            Canvas.SetZIndex(this, ++_globalZIndex);
            WindowActivated?.Invoke(this, EventArgs.Empty);
        }

        // ��� �������� �ڵ鷯��
        private void ResizeTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Top, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Bottom, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Left, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Right, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.TopLeft, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.TopRight, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.BottomLeft, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.BottomRight, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        // ���� �޼��� ���� (ȣȯ���� ����)
        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ResizeBottomRight_MouseLeftButtonDown(sender, e);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (_isMinimized)
            {
                // �ּ�ȭ ���� - ���� ũ��� ����
                RestoreNormalSize();
            }
            else
            {
                // �ּ�ȭ - Ÿ��Ʋ�ٸ� ���̵���
                SaveCurrentSize();
                this.Height = 30;
                _isMinimized = true;
                _isMaximized = false;
            }
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;
            
            if (_isMaximized)
            {
                // �ִ�ȭ ���� - �⺻ ũ��� ����
                RestoreNormalSize();
            }
            else
            {
                // �ִ�ȭ
                SaveCurrentSize();
                this.Width = canvas.ActualWidth - 10;
                this.Height = canvas.ActualHeight - 10;
                Canvas.SetLeft(this, 5);
                Canvas.SetTop(this, 5);
                _isMaximized = true;
                _isMinimized = false;
            }
        }
        
        private void SaveCurrentSize()
        {
            if (!_isMaximized && !_isMinimized)
            {
                _normalWidth = this.ActualWidth;
                _normalHeight = this.ActualHeight;
                _normalLeft = Canvas.GetLeft(this);
                _normalTop = Canvas.GetTop(this);
            }
        }
        
        private void RestoreNormalSize()
        {
            this.Width = _normalWidth;
            this.Height = _normalHeight;
            Canvas.SetLeft(this, _normalLeft);
            Canvas.SetTop(this, _normalTop);
            _isMaximized = false;
            _isMinimized = false;
        }

        public void Close_Click(object sender, RoutedEventArgs e)
        {
            WindowClosed?.Invoke(this, EventArgs.Empty);
        }

        #region ũ�� ���� �޼����

        private void ApplySize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (double.TryParse(WidthTextBox.Text, out double width) && 
                    double.TryParse(HeightTextBox.Text, out double height))
                {
                    SetWindowSize(width, height);
                }
                else
                {
                    MessageBox.Show("�ùٸ� ���ڸ� �Է����ּ���.", "ũ�� ����", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"ũ�� ���� �� ������ �߻��߽��ϴ�: {ex.Message}", "����", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(800, 600);
        }

        private void SetSmallSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(400, 300);
        }

        private void SetMediumSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(800, 600);
        }

        private void SetLargeSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(1200, 800);
        }

        private void SetHDSize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                // ĵ���� ũ�⸦ ����Ͽ� ������ ����
                var maxWidth = Math.Min(1920, canvas.ActualWidth - 50);
                var maxHeight = Math.Min(1080, canvas.ActualHeight - 50);
                SetWindowSize(maxWidth, maxHeight);
            }
            else
            {
                SetWindowSize(1200, 800); // ĵ������ ������ �⺻ ũ��
            }
        }

        private void SetWindowSize(double width, double height)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;

            // �ּ�/�ִ� ũ�� ����
            const double minWidth = 300;
            const double minHeight = 200;
            
            width = Math.Max(minWidth, Math.Min(width, canvas.ActualWidth - 20));
            height = Math.Max(minHeight, Math.Min(height, canvas.ActualHeight - 20));

            // ���� ��ġ�� �� ũ�⿡�� ĵ������ ����� �ʵ��� ����
            var currentLeft = Canvas.GetLeft(this);
            var currentTop = Canvas.GetTop(this);
            
            if (currentLeft + width > canvas.ActualWidth)
            {
                currentLeft = Math.Max(0, canvas.ActualWidth - width);
                Canvas.SetLeft(this, currentLeft);
            }
            
            if (currentTop + height > canvas.ActualHeight)
            {
                currentTop = Math.Max(0, canvas.ActualHeight - height);
                Canvas.SetTop(this, currentTop);
            }

            this.Width = width;
            this.Height = height;
            
            // �ִ�ȭ ���� ����
            _isMaximized = false;
            _isMinimized = false;
        }

        private void ShowCustomSizeDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomSizeDialog(this.ActualWidth, this.ActualHeight)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                SetWindowSize(dialog.SelectedWidth, dialog.SelectedHeight);
            }
        }

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}