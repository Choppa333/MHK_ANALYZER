using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            private readonly List<ChartCursorHandle> _cursorHandles = new();
            private readonly Dictionary<string, NumericalAxis> _unitAxes = new(StringComparer.OrdinalIgnoreCase);
            private NumericalAxis? _defaultSecondaryAxis;
            private static readonly PropertyInfo? PlotAreaClipRectProperty =
                typeof(SfChart).GetProperty("PlotAreaClipRect", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        private HashSet<string> _initialSeriesSelection = new(StringComparer.OrdinalIgnoreCase);
        
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
                
                ClearCursorHandles();
                _dataSet = value; 
                OnPropertyChanged();
                
                PerformanceLogger.Instance.LogInfo($"차트 데이터 설정 - 파일: {value?.FileName}, 시리즈: {value?.SeriesData?.Count}", "Chart_Display");
                InitializeSeriesList();
            }
        }

        public void SetInitialSeriesSelection(IEnumerable<string>? seriesNames)
        {
            _initialSeriesSelection = seriesNames != null
                ? new HashSet<string>(seriesNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            if (ChartControl.SecondaryAxis is NumericalAxis numericAxis)
            {
                _defaultSecondaryAxis = numericAxis;
            }
            else
            {
                _defaultSecondaryAxis = new NumericalAxis { Header = "Value" };
                ChartControl.SecondaryAxis = _defaultSecondaryAxis;
            }

            ChartControl.SizeChanged += (_, __) => RefreshCursorHandles();
            ChartControl.LayoutUpdated += (_, __) => RefreshCursorHandles();
            ChartControl.Loaded += (_, __) => RefreshCursorHandles();
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
            var initialSelection = new HashSet<string>(_initialSeriesSelection, StringComparer.OrdinalIgnoreCase);
            _initialSeriesSelection.Clear();
            foreach (var series in DataSet.SeriesData.Values.Where(s => !s.Name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase)))
            {
                var viewModel = new ChartSeriesViewModel { SeriesData = series };
                viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
                viewModel.IsSelected = initialSelection.Contains(series.Name);
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
                dynSeries.YAxis = GetAxisForSeries(series);
            }
            catch
            {
                // Some series types may not expose a YAxis property; ignore in that case.
            }
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

        private (double min, double max) GetVisibleValueRange()
        {
            if (ChartControl.SecondaryAxis is NumericalAxis numericAxis && numericAxis.VisibleRange != null)
            {
                var range = numericAxis.VisibleRange;
                if (range.End > range.Start)
                {
                    return (range.Start, range.End);
                }
            }

            if (DataSet?.SeriesData != null)
            {
                var (min, max) = (double.MaxValue, double.MinValue);
                foreach (var series in DataSet.SeriesData.Values)
                {
                    if (series.Values == null || series.Values.Length == 0) continue;
                    var seriesMin = series.Values.Min();
                    var seriesMax = series.Values.Max();
                    if (seriesMin < min) min = seriesMin;
                    if (seriesMax > max) max = seriesMax;
                }

                if (min != double.MaxValue && max != double.MinValue && min != max)
                {
                    return (min, max);
                }
            }

            return (0, 1);
        }

        private NumericalAxis GetAxisForSeries(SeriesData series)
        {
            if (series == null)
            {
                return _defaultSecondaryAxis ?? (NumericalAxis)(ChartControl.SecondaryAxis ?? new NumericalAxis { Header = "Value" });
            }

            var unitKey = string.IsNullOrWhiteSpace(series.Unit) ? "Default" : series.Unit.Trim();
            if (unitKey.Equals("Default", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(series.Unit))
            {
                return _defaultSecondaryAxis ?? (NumericalAxis)(ChartControl.SecondaryAxis ?? new NumericalAxis { Header = "Value" });
            }

            if (_unitAxes.TryGetValue(unitKey, out var existingAxis))
            {
                return existingAxis;
            }

            var axis = new NumericalAxis
            {
                Header = series.Unit,
                OpposedPosition = true,
                ShowGridLines = false
            };
            ChartControl.Axes.Add(axis);
            _unitAxes[unitKey] = axis;
            return axis;
        }

        private int GetNextCursorIndex()
        {
            if (DataSet == null || DataSet.TotalSamples == 0)
            {
                return 0;
            }

            var step = Math.Max(1, DataSet.TotalSamples / 8);
            var targetIndex = Math.Min(DataSet.TotalSamples - 1, _cursorHandles.Count * step);
            return targetIndex;
        }

        private void AddCursor_Click(object sender, RoutedEventArgs e)
        {
            if (DataSet == null || ChartControl == null || CursorOverlayCanvas == null) return;

            var cursorIndex = GetNextCursorIndex();
            var cursorTime = DataSet.GetTimeAt(cursorIndex);
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

            var annotation = new VerticalLineAnnotation
            {
                X1 = cursorTime,
                X2 = cursorTime,
                Y1 = minValue,
                Y2 = maxValue,
                Stroke = Brushes.Transparent,
                StrokeThickness = 0
            };

            ChartControl.Annotations?.Add(annotation);

            var thumb = new Thumb
            {
                Style = (Style?)TryFindResource("CursorThumbStyle"),
                Width = 12,
                Cursor = Cursors.SizeWE
            };
            thumb.DragDelta += CursorThumb_DragDelta;
            thumb.DragStarted += CursorThumb_DragStarted;
            thumb.DragCompleted += CursorThumb_DragCompleted;
            Canvas.SetZIndex(thumb, 100);
            CursorOverlayCanvas.Children.Add(thumb);

            var handle = new ChartCursorHandle
            {
                Annotation = annotation,
                Thumb = thumb,
                Index = cursorIndex
            };
            thumb.Tag = handle;
            _cursorHandles.Add(handle);

            RefreshCursorHandles();
        }

        private void RemoveCursor_Click(object sender, RoutedEventArgs e)
        {
            if (_cursorHandles.Count == 0 || ChartControl == null) return;

            var lastCursor = _cursorHandles[^1];
            _cursorHandles.RemoveAt(_cursorHandles.Count - 1);
            ChartControl.Annotations?.Remove(lastCursor.Annotation);
            CursorOverlayCanvas?.Children.Remove(lastCursor.Thumb);
        }

        private void CursorThumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is Thumb thumb)
            {
                Canvas.SetZIndex(thumb, 200);
            }
        }

        private void CursorThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (sender is Thumb thumb)
            {
                Canvas.SetZIndex(thumb, 100);
            }

            RefreshCursorHandles();
        }

        private void CursorThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb || thumb.Tag is not ChartCursorHandle handle || DataSet == null)
            {
                return;
            }

            var plotRect = GetPlotAreaRect();
            if (plotRect.Width <= 0)
            {
                return;
            }

            var maxIndex = Math.Max(0, DataSet.TotalSamples - 1);
            var deltaIndex = (e.HorizontalChange / plotRect.Width) * Math.Max(1, maxIndex);
            handle.Index = Math.Clamp(handle.Index + deltaIndex, 0, maxIndex);
            UpdateCursorAnnotation(handle);
            UpdateCursorHandlePosition(handle, plotRect);
        }

        private void UpdateCursorAnnotation(ChartCursorHandle handle)
        {
            if (DataSet == null)
            {
                return;
            }

            var sampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, DataSet.TotalSamples - 1);
            var cursorTime = DataSet.GetTimeAt(sampleIndex);
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

            if (handle.Annotation != null)
            {
                handle.Annotation.X1 = cursorTime;
                handle.Annotation.X2 = cursorTime;
                handle.Annotation.Y1 = minValue;
                handle.Annotation.Y2 = maxValue;
            }
        }

        private void UpdateCursorHandlePosition(ChartCursorHandle handle, Rect plotRect)
        {
            if (CursorOverlayCanvas == null || DataSet == null)
            {
                return;
            }

            var denominator = Math.Max(1, DataSet.TotalSamples - 1);
            var ratio = denominator == 0 ? 0 : handle.Index / denominator;
            var targetX = plotRect.Left + ratio * plotRect.Width;

            Canvas.SetLeft(handle.Thumb, targetX - handle.Thumb.Width / 2);
            Canvas.SetTop(handle.Thumb, plotRect.Top);
            handle.Thumb.Height = plotRect.Height;
            handle.Thumb.Visibility = Visibility.Visible;
        }

        private void RefreshCursorHandles()
        {
            if (CursorOverlayCanvas == null || _cursorHandles.Count == 0 || DataSet == null)
            {
                return;
            }

            var plotRect = GetPlotAreaRect();
            if (plotRect.Width <= 0 || plotRect.Height <= 0)
            {
                return;
            }

            foreach (var handle in _cursorHandles)
            {
                UpdateCursorAnnotation(handle);
                UpdateCursorHandlePosition(handle, plotRect);
            }
        }

        private Rect GetPlotAreaRect()
        {
            if (ChartControl == null)
            {
                return new Rect();
            }

            if (PlotAreaClipRectProperty?.GetValue(ChartControl) is Rect clipRect && !clipRect.IsEmpty)
            {
                return clipRect;
            }

            return new Rect(0, 0, ChartControl.ActualWidth, ChartControl.ActualHeight);
        }

        private void ClearCursorHandles()
        {
            if (ChartControl != null)
            {
                foreach (var handle in _cursorHandles)
                {
                    if (handle.Annotation != null)
                    {
                        ChartControl.Annotations?.Remove(handle.Annotation);
                    }
                }
            }

            _cursorHandles.Clear();
            CursorOverlayCanvas?.Children.Clear();
        }

        private sealed class ChartCursorHandle
        {
            public VerticalLineAnnotation? Annotation { get; set; }
            public Thumb Thumb { get; set; } = null!;
            public double Index { get; set; }
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