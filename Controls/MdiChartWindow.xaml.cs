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
    public partial class MdiChartWindow : UserControl, INotifyPropertyChanged
    {
        private bool _isDragging = false;
        private bool _isResizing = false;
        private Point _lastPosition;
        private static int _globalZIndex = 1; // deprecated local counter; retained to avoid widespread edits
        private MemoryOptimizedDataSet? _dataSet;
        private bool _isManagementPanelExpanded = true;
        private bool _isManagementPanelPinned = false;
        
        // 윈도우 크기 상태 저장
        private double _normalWidth = 800;
        private double _normalHeight = 600;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private bool _isMaximized = false;
        private bool _isMinimized = false;
        
        // 리사이즈 상태 추적
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
                using var timer = PerformanceLogger.Instance.StartTimer("차트 윈도우 데이터 설정", "Chart_Display");
                
                _dataSet = value; 
                OnPropertyChanged();
                
                PerformanceLogger.Instance.LogInfo($"차트 데이터 설정 - 파일: {value?.FileName}, 시리즈: {value?.SeriesData?.Count}", "Chart_Display");
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
            // Bring to front on any mouse down inside the control
            this.PreviewMouseDown += (_, __) => MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
            
            // 초기 상태: 패널 표시
            _isManagementPanelExpanded = true;
            _isManagementPanelPinned = false;
        }

        #region 관리 패널 자동 숨김/고정 기능

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
            Console.WriteLine("=== InitializeSeriesList 시작 ===");
            using var timer = PerformanceLogger.Instance.StartTimer("시리즈 리스트 초기화", "Chart_Display");
            
            SeriesList.Clear();
            if (DataSet?.SeriesData != null)
            {
                Console.WriteLine($"DataSet.SeriesData.Count: {DataSet.SeriesData.Count}");
                
                foreach (var series in DataSet.SeriesData.Values)
                {
                    Console.WriteLine($"시리즈 추가: {series.Name} ({series.Unit}) - DataType: {series.DataType}");
                    
                    var viewModel = new ChartSeriesViewModel { SeriesData = series };
                    viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
                    SeriesList.Add(viewModel);
                }
                
                Console.WriteLine($"시리즈 리스트 초기화 완료 - {SeriesList.Count}개 시리즈");
                PerformanceLogger.Instance.LogInfo($"시리즈 리스트 초기화 완료 - {SeriesList.Count}개 시리즈", "Chart_Display");
            }
            else
            {
                Console.WriteLine("ERROR: DataSet 또는 SeriesData가 null입니다!");
            }
            
            Console.WriteLine("=== InitializeSeriesList 완료 ===");
        }

        private async void SeriesViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChartSeriesViewModel.IsSelected))
            {
                var viewModel = (ChartSeriesViewModel)sender;
                var seriesName = viewModel.SeriesData.Name;
                
                PerformanceLogger.Instance.LogInfo($"시리즈 선택 변경: {seriesName} -> {viewModel.IsSelected}", "Chart_Display");
                Console.WriteLine($"=== 시리즈 선택 변경: {seriesName} -> {viewModel.IsSelected} ===");
                
                if (viewModel.IsSelected)
                {
                    // UI 응답성을 위해 비동기로 처리
                    await Task.Run(() =>
                    {
                        using var timer = PerformanceLogger.Instance.StartTimer($"시리즈 추가: {seriesName}", "Chart_Display");
                        Console.WriteLine($"비동기 시리즈 추가 시작: {seriesName}");
                        
                        // UI 스레드에서 차트 업데이트
                        Dispatcher.BeginInvoke(() => AddSeriesToChart(viewModel.SeriesData));
                    });
                }
                else
                {
                    using var timer = PerformanceLogger.Instance.StartTimer($"시리즈 제거: {seriesName}", "Chart_Display");
                    RemoveSeriesFromChart(viewModel.SeriesData);
                }
            }
        }

        private void AddSeriesToChart(SeriesData series)
        {
            Console.WriteLine($"=== AddSeriesToChart 시작: {series.Name} ===");
            using var overallTimer = PerformanceLogger.Instance.StartTimer($"전체 차트 시리즈 추가: {series.Name}", "Chart_Display");
            
            // 데이터 검증
            Console.WriteLine($"DataSet.TotalSamples: {DataSet?.TotalSamples}");
            Console.WriteLine($"Series.Values 길이: {series.Values?.Length}");
            Console.WriteLine($"Series.DataType: {series.DataType}");
            
            if (DataSet == null)
            {
                Console.WriteLine("ERROR: DataSet이 null입니다!");
                return;
            }
            
            if (DataSet.TotalSamples == 0)
            {
                Console.WriteLine("ERROR: TotalSamples가 0입니다!");
                return;
            }
            
            // 대용량 데이터의 경우 다운샘플링 적용 (성능 개선)
            var maxPoints = 500; // 더 적은 포인트로 제한하여 성능 개선
            var totalPoints = DataSet.TotalSamples;
            var step = Math.Max(1, totalPoints / maxPoints);
            
            Console.WriteLine($"다운샘플링 설정 - 전체: {totalPoints:N0}, 단계: {step}, 표시: {totalPoints/step:N0}개 포인트");
            PerformanceLogger.Instance.LogInfo($"다운샘플링 설정 - 전체: {totalPoints:N0}, 단계: {step}, 표시: {totalPoints/step:N0}개 포인트", "Chart_Display");
            
            try
            {
                if (series.DataType == typeof(bool))
                {
                    Console.WriteLine("Bool 시리즈 생성 중...");
                    using var seriesTimer = PerformanceLogger.Instance.StartTimer($"StepLine 시리즈 생성: {series.Name}", "Chart_Display");
                    
                    var stepSeries = new StepLineSeries
                    {
                        XBindingPath = "Time",
                        YBindingPath = "Value",
                        Label = series.Name,
                        Stroke = series.Color,
                        StrokeThickness = 1
                    };

                    var dataPoints = CreateBoolDataPoints(series, step);
                    Console.WriteLine($"Bool 데이터 포인트 생성됨: {dataPoints.Count}개");
                    
                    stepSeries.ItemsSource = dataPoints;
                    ChartControl.Series.Add(stepSeries);
                    Console.WriteLine($"StepLine 시리즈 차트에 추가됨: {series.Name}");
                    
                    PerformanceLogger.Instance.LogInfo($"Bool 시리즈 추가 완료: {series.Name} ({dataPoints.Count:N0}개 포인트)", "Chart_Display");
                }
                else
                {
                    Console.WriteLine("Double 시리즈 생성 중...");
                    using var seriesTimer = PerformanceLogger.Instance.StartTimer($"Line 시리즈 생성: {series.Name}", "Chart_Display");
                    
                    var lineSeries = new LineSeries
                    {
                        XBindingPath = "Time",
                        YBindingPath = "Value",
                        Label = series.Name,
                        Stroke = series.Color,
                        StrokeThickness = 1
                    };

                    var dataPoints = CreateDoubleDataPoints(series, step);
                    Console.WriteLine($"Double 데이터 포인트 생성됨: {dataPoints.Count}개");
                    
                    // 데이터 샘플 확인
                    if (dataPoints.Count > 0)
                    {
                        Console.WriteLine($"첫 번째 데이터 포인트: Time={dataPoints[0].Time}, Value={dataPoints[0].Value}");
                        if (dataPoints.Count > 1)
                        {
                            Console.WriteLine($"두 번째 데이터 포인트: Time={dataPoints[1].Time}, Value={dataPoints[1].Value}");
                        }
                    }
                    
                    Console.WriteLine("시리즈를 차트에 추가하기 시작...");
                    lineSeries.ItemsSource = dataPoints;
                    ChartControl.Series.Add(lineSeries);
                    Console.WriteLine($"Line 시리즈 차트에 추가 완료: {series.Name}");
                    
                    PerformanceLogger.Instance.LogInfo($"Double 시리즈 추가 완료: {series.Name} ({dataPoints.Count:N0}개 포인트)", "Chart_Display");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: 시리즈 추가 중 오류 - {ex.Message}");
                Console.WriteLine($"StackTrace: {ex.StackTrace}");
                PerformanceLogger.Instance.LogError($"시리즈 추가 오류: {series.Name} - {ex.Message}", "Chart_Display");
            }
            
            // 범례 표시 활성화
            try
            {
                if (ChartControl.Legend is ChartLegend legend)
                {
                    legend.Visibility = Visibility.Visible;
                    Console.WriteLine("범례 표시 활성화됨");
                }
                else
                {
                    Console.WriteLine("WARNING: Legend가 null이거나 ChartLegend 타입이 아닙니다");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: 범례 설정 중 오류 - {ex.Message}");
            }
            
            Console.WriteLine($"현재 차트의 총 시리즈 수: {ChartControl.Series.Count}");
            PerformanceLogger.Instance.LogInfo($"현재 차트의 총 시리즈 수: {ChartControl.Series.Count}", "Chart_Display");
            
            Console.WriteLine($"=== AddSeriesToChart 완료: {series.Name} ===");
        }

        private ObservableCollection<ChartDataPoint> CreateBoolDataPoints(SeriesData series, int step)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Bool 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
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
            
            PerformanceLogger.Instance.LogInfo($"Bool 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
            return dataPoints;
        }

        private ObservableCollection<ChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step)
        {
            Console.WriteLine($"=== CreateDoubleDataPoints 시작: {series.Name} ===");
            using var timer = PerformanceLogger.Instance.StartTimer($"Double 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
            var dataPoints = new ObservableCollection<ChartDataPoint>();
            var pointCount = 0;
            var maxPointsToCreate = 500; // 성능을 위해 제한
            
            Console.WriteLine($"TotalSamples: {DataSet.TotalSamples}, Step: {step}, MaxPoints: {maxPointsToCreate}");
            Console.WriteLine($"BaseTime: {DataSet.BaseTime}, TimeInterval: {DataSet.TimeInterval}");
            
            // 실제 데이터 값 확인
            if (series.Values != null && series.Values.Length > 0)
            {
                Console.WriteLine($"Values 배열 길이: {series.Values.Length}");
                var firstFiveValues = series.Values.Take(5).ToArray();
                Console.WriteLine($"첫 5개 값: [{string.Join(", ", firstFiveValues)}]");
                
                var nonZeroCount = series.Values.Take(Math.Min(100, series.Values.Length)).Count(v => v != 0 && !float.IsNaN(v));
                Console.WriteLine($"처음 100개 중 0이 아닌 값의 개수: {nonZeroCount}");
            }
            else
            {
                Console.WriteLine("ERROR: Values 배열이 null이거나 비어있습니다!");
                return dataPoints;
            }
            
            // 안전한 범위에서만 처리
            var maxSafeIndex = Math.Min(DataSet.TotalSamples, series.Values.Length);
            Console.WriteLine($"안전 처리 범위: 0 ~ {maxSafeIndex-1}");
            
            for (int i = 0; i < maxSafeIndex && pointCount < maxPointsToCreate; i += step)
            {
                try
                {
                    var time = DataSet.GetTimeAt(i);
                    var value = series.Values[i];
                    
                    // NaN, Infinity 값 체크
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
                    
                    // 처음 3개 포인트만 로그 출력
                    if (pointCount < 3)
                    {
                        Console.WriteLine($"포인트 {pointCount}: Time={time:HH:mm:ss}, Value={value}");
                    }
                    
                    pointCount++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"ERROR: 데이터 포인트 생성 중 오류 (인덱스 {i}): {ex.Message}");
                    break;
                }
            }
            
            Console.WriteLine($"Double 데이터 포인트 생성 완료: {pointCount:N0}개");
            PerformanceLogger.Instance.LogInfo($"Double 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
            
            Console.WriteLine($"=== CreateDoubleDataPoints 완료: {series.Name} ===");
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

        // 타이틀바 드래그
        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 버튼 클릭인지 확인 (버튼이면 드래그하지 않음)
            var element = e.OriginalSource as FrameworkElement;
            while (element != null)
            {
                if (element is Button)
                {
                    // 버튼 클릭이면 드래그 처리하지 않음
                    return;
                }
                element = element.Parent as FrameworkElement;
            }

            if (e.ClickCount == 2)
            {
                // 더블클릭 시 최대화/복원
                Maximize_Click(sender, e);
            }
            else
            {
                _isDragging = true;
                var canvas = (Canvas)this.Parent;
                _lastPosition = e.GetPosition(canvas);
                this.CaptureMouse();
                
                // 활성 윈도우로 설정 (bring to front)
                MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
                WindowActivated?.Invoke(this, EventArgs.Empty);
            }
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
                
                // 경계 체크
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

            // 최소 크기 제한
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

            // 캔버스 경계 체크
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
            
            // 활성 윈도우로 설정 (bring to front)
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
            WindowActivated?.Invoke(this, EventArgs.Empty);
        }

        // 모든 리사이즈 핸들러들
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

        // 기존 메서드 유지 (호환성을 위해)
        private void ResizeHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            ResizeBottomRight_MouseLeftButtonDown(sender, e);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (_isMinimized)
            {
                // 최소화 해제 - 이전 크기로 복원
                RestoreNormalSize();
            }
            else
            {
                // 최소화 - 타이틀바만 보이도록
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
                // 최대화 해제 - 기본 크기로 복원
                RestoreNormalSize();
            }
            else
            {
                // 최대화
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

        #region 크기 설정 메서드들

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
                    MessageBox.Show("올바른 숫자를 입력해주세요.", "크기 설정", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"크기 설정 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
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
                // 캔버스 크기를 고려하여 적절히 조절
                var maxWidth = Math.Min(1920, canvas.ActualWidth - 50);
                var maxHeight = Math.Min(1080, canvas.ActualHeight - 50);
                SetWindowSize(maxWidth, maxHeight);
            }
            else
            {
                SetWindowSize(1200, 800); // 캔버스가 없으면 기본 크기
            }
        }

        private void SetWindowSize(double width, double height)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;

            // 최소/최대 크기 제한
            const double minWidth = 300;
            const double minHeight = 200;
            
            width = Math.Max(minWidth, Math.Min(width, canvas.ActualWidth - 20));
            height = Math.Max(minHeight, Math.Min(height, canvas.ActualHeight - 20));

            // 현재 위치가 새 크기에서 캔버스를 벗어나지 않도록 조정
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
            
            // 최대화 상태 해제
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