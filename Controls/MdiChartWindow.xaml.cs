using System;
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

        // Management panel event handlers referenced from XAML
        private void ManagementPanel_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            if (!_isManagementPanelPinned)
            {
                ManagementPanel.Visibility = Visibility.Collapsed;
                ManagementPanelTab.Visibility = Visibility.Visible;
                _isManagementPanelExpanded = false;
            }
        }

        private void PanelPinButton_Checked(object sender, RoutedEventArgs e)
        {
            _isManagementPanelPinned = true;
        }

        private void PanelPinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isManagementPanelPinned = false;
        }

        private void ManagementPanelTab_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ManagementPanel.Visibility = Visibility.Visible;
            ManagementPanelTab.Visibility = Visibility.Collapsed;
            _isManagementPanelExpanded = true;
        }

        private void ManagementPanelTab_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // Toggle panel on click
            if (_isManagementPanelExpanded)
            {
                ManagementPanel.Visibility = Visibility.Collapsed;
                ManagementPanelTab.Visibility = Visibility.Visible;
                _isManagementPanelExpanded = false;
            }
            else
            {
                ManagementPanel.Visibility = Visibility.Visible;
                ManagementPanelTab.Visibility = Visibility.Collapsed;
                _isManagementPanelExpanded = true;
            }
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
            this.PreviewMouseDown += (_, __) => MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
            
            _isManagementPanelExpanded = true;
            _isManagementPanelPinned = false;

            // XAML에 정의된 기본 축을 사용하거나, 여기서 코드로 설정
            ChartControl.PrimaryAxis = new DateTimeAxis { Header = "Time", LabelFormat = "HH:mm:ss.fff" };
            ChartControl.SecondaryAxis = new NumericalAxis { Header = "Value" };
        }

        #region 관리 패널 자동 숨김/고정 기능
        // ... (기존 코드 유지)
        #endregion

        private void InitializeSeriesList()
        {
            using var timer = PerformanceLogger.Instance.StartTimer("시리즈 리스트 초기화", "Chart_Display");
            
            SeriesList.Clear();
            if (DataSet?.SeriesData == null) return;

            // Timestamp가 아닌 시리즈만 목록에 추가
            foreach (var series in DataSet.SeriesData.Values.Where(s => !s.Name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase)))
            {
                var viewModel = new ChartSeriesViewModel { SeriesData = series };
                viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
                SeriesList.Add(viewModel);
            }
        }

        // Removed duplicated overload. Single implementation below.

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

        private async Task AddSeriesToChart(SeriesData series)
        {
            if (DataSet == null)
            {
                return;
            }

            // 이미 차트에 시리즈가 있는지 확인
            if (ChartControl.Series.Any(s => (s.Tag as SeriesData)?.Name == series.Name))
            {
                return;
            }

            var seriesType = series.DataType == typeof(bool) ? typeof(StepLineSeries) : typeof(FastLineSeries);

            var sampleStep = Math.Max(1, DataSet.TotalSamples / 500);
            List<ChartDataPoint> dataPoints;

            if (series.DataType == typeof(bool))
            {
                var boolStep = Math.Max(1, DataSet.TotalSamples / 10000);
                dataPoints = await Task.Run(() => CreateBoolDataPoints(series, boolStep));
            }
            else
            {
                dataPoints = await Task.Run(() => CreateDoubleDataPoints(series, sampleStep));
            }

            if (dataPoints.Count == 0)
            {
                return;
            }

            var chartSeries = (ChartSeries)Activator.CreateInstance(seriesType);
            chartSeries.ItemsSource = dataPoints;

            // 일부 차트 시리즈는 X/Y 바인딩 속성이 없음, dynamic으로 설정
            dynamic dynSeries = chartSeries;
            try
            {
                dynSeries.XBindingPath = "Time";
            }
            catch { }
            try
            {
                dynSeries.YBindingPath = "Value";
            }
            catch { }
            dynSeries.Label = series.Name;
            dynSeries.Tag = series;
            try
            {
                dynSeries.EnableTooltip = true;
            }
            catch { }

            if (chartSeries is FastLineSeries fastLineSeries)
            {
                fastLineSeries.Stroke = series.Color;
            }
            else if (chartSeries is StepLineSeries stepLineSeries)
            {
                stepLineSeries.Stroke = series.Color;
            }

            ChartControl.Series.Add(chartSeries);
        }

        private List<ChartDataPoint> CreateBoolDataPoints(SeriesData series, int step)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Bool 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
            var dataPoints = new List<ChartDataPoint>();
            var pointCount = 0;
            
            for (int i = 0; i < DataSet.TotalSamples && pointCount < 10000; i += step)
            {
                bool value = GetBoolValue(series.BitValues, i);
                dataPoints.Add(new ChartDataPoint(DataSet.GetTimeAt(i), value ? 1.0 : 0.0)
                {
                    SeriesName = series.Name
                });
                pointCount++;
            }
            
            PerformanceLogger.Instance.LogInfo($"Bool 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
            return dataPoints;
        }

        private List<ChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step)
        {
            Console.WriteLine($"=== CreateDoubleDataPoints 시작: {series.Name} ===");
            using var timer = PerformanceLogger.Instance.StartTimer($"Double 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
            var dataPoints = new List<ChartDataPoint>();
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
                    
                    dataPoints.Add(new ChartDataPoint(time, value)
                    {
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

        private bool GetBoolValue(System.Collections.BitArray? bitValues, int index)
        {
            if (bitValues == null) return false;
            if (index < 0 || index >= bitValues.Length) return false;
            return bitValues[index];
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