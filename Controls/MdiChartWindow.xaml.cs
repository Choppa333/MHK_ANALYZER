using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        private bool _isLeftPanelExpanded = true;
        private bool _isLeftPanelPinned = false;
		private readonly List<ChartCursorHandle> _cursorHandles = new();
		private const int MaxActiveSeriesCount = 10;
		private const int MaxSnapshotSeriesValues = MaxActiveSeriesCount;
		private const bool EnableCursorDebugLogging = false;
		private bool _isRefreshingCursorHandles;
		private bool _pendingSnapshotRefresh;
		private bool _isUpdatingSeriesSelection;
            private static readonly Brush[] CursorColorPalette =
            {
                Brushes.Crimson,
                Brushes.DodgerBlue,
                Brushes.MediumSeaGreen,
                Brushes.Goldenrod,
                Brushes.MediumOrchid
            };
		private readonly Dictionary<string, NumericalAxis> _unitAxes = new(StringComparer.OrdinalIgnoreCase);
		private NumericalAxis? _defaultSecondaryAxis;
		private (double min, double max)? _cachedDataValueRange;
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

        private void LeftPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isLeftPanelPinned)
            {
                LeftPanel.Visibility = Visibility.Collapsed;
                LeftPanelTab.Visibility = Visibility.Visible;
                _isLeftPanelExpanded = false;
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

        private void LeftPanelPinButton_Checked(object sender, RoutedEventArgs e)
        {
            _isLeftPanelPinned = true;
        }

        private void LeftPanelPinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isLeftPanelPinned = false;
        }

        private void ManagementPanelTab_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            ManagementPanel.Visibility = Visibility.Visible;
            ManagementPanelTab.Visibility = Visibility.Collapsed;
            _isManagementPanelExpanded = true;
        }

        private void LeftPanelTab_MouseEnter(object sender, MouseEventArgs e)
        {
            LeftPanel.Visibility = Visibility.Visible;
            LeftPanelTab.Visibility = Visibility.Collapsed;
            _isLeftPanelExpanded = true;
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

        private void LeftPanelTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_isLeftPanelExpanded)
            {
                LeftPanel.Visibility = Visibility.Collapsed;
                LeftPanelTab.Visibility = Visibility.Visible;
                _isLeftPanelExpanded = false;
            }
            else
            {
                LeftPanel.Visibility = Visibility.Visible;
                LeftPanelTab.Visibility = Visibility.Collapsed;
                _isLeftPanelExpanded = true;
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
				RebuildDataValueRangeCache();
                
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
        public ObservableCollection<CursorSnapshotViewModel> SnapshotList { get; } = new();

        public event EventHandler? WindowClosed;
        public event EventHandler? WindowActivated;

        public MdiChartWindow()
        {
            InitializeComponent();
            DataContext = this;
            this.PreviewMouseDown += (_, __) => MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
            
            _isManagementPanelExpanded = true;
            _isManagementPanelPinned = false;
            _isLeftPanelExpanded = true;
            _isLeftPanelPinned = false;

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
				if (_isUpdatingSeriesSelection)
				{
					return;
				}

                var viewModel = (ChartSeriesViewModel)sender;
                var seriesName = viewModel.SeriesData.Name;
                
                PerformanceLogger.Instance.LogInfo($"시리즈 선택 변경: {seriesName} -> {viewModel.IsSelected}", "Chart_Display");
                Console.WriteLine($"=== 시리즈 선택 변경: {seriesName} -> {viewModel.IsSelected} ===");
                
                if (viewModel.IsSelected)
                {
					if (!CanAddMoreSeries())
					{
						ShowSeriesLimitMessage();
						SetSeriesSelection(viewModel, false);
						return;
					}

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
			if (DataSet == null || ChartControl == null)
            {
                return;
            }

            // 이미 차트에 시리즈가 있는지 확인
            if (ChartControl.Series.Any(s => (s.Tag as SeriesData)?.Name == series.Name))
            {
                return;
            }

			if (!CanAddMoreSeries())
			{
				ShowSeriesLimitMessage();
				return;
			}

			var seriesType = series.DataType == typeof(bool) ? typeof(StepLineSeries) : typeof(FastLineBitmapSeries);
			var targetPointBudget = GetTargetPointBudget();
			var sampleStep = Math.Max(1, DataSet.TotalSamples / Math.Max(1, targetPointBudget));
            List<ChartDataPoint> dataPoints;

			if (series.DataType == typeof(bool))
			{
				var boolBudget = Math.Max(200, Math.Min(1200, targetPointBudget));
				var boolStep = Math.Max(1, DataSet.TotalSamples / boolBudget);
				dataPoints = await Task.Run(() => CreateBoolDataPoints(series, boolStep, boolBudget));
			}
            else
            {
				dataPoints = await Task.Run(() => CreateDoubleDataPoints(series, sampleStep, targetPointBudget));
            }

            if (dataPoints.Count == 0)
            {
                return;
            }

			var chartSeries = (ChartSeries)Activator.CreateInstance(seriesType);
			chartSeries.ItemsSource = dataPoints;
			ConfigureSeriesPerformance(chartSeries, dataPoints.Count);

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

			if (chartSeries is FastLineBitmapSeries fastBitmapSeries)
			{
				fastBitmapSeries.Stroke = series.Color;
			}
			else if (chartSeries is StepLineSeries stepLineSeries)
			{
				stepLineSeries.Stroke = series.Color;
			}

            ChartControl.Series.Add(chartSeries);
            RefreshAllSnapshots();
        }

		private void ConfigureSeriesPerformance(ChartSeries chartSeries, int dataPointCount)
		{
			chartSeries.EnableAnimation = false;
			chartSeries.AnimationDuration = TimeSpan.Zero;

			var enableTooltips = dataPointCount <= 300;
			try
			{
				dynamic dynSeries = chartSeries;
				dynSeries.EnableTooltip = enableTooltips;
			}
			catch
			{
				// ignore tooltip capability if not exposed
			}
		}

		private List<ChartDataPoint> CreateBoolDataPoints(SeriesData series, int step, int maxPoints)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Bool 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
            var dataPoints = new List<ChartDataPoint>();
			var pointCount = 0;
            
			for (int i = 0; i < DataSet.TotalSamples && pointCount < maxPoints; i += step)
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

		private List<ChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step, int maxPointsToCreate)
		{
			using var timer = PerformanceLogger.Instance.StartTimer($"Double 데이터 포인트 생성: {series.Name}", "Chart_Display");
			var dataPoints = new List<ChartDataPoint>();
			var pointCount = 0;
			if (series.Values == null || series.Values.Length == 0)
			{
				return dataPoints;
			}

			var maxSafeIndex = Math.Min(DataSet.TotalSamples, series.Values.Length);
			for (int i = 0; i < maxSafeIndex && pointCount < maxPointsToCreate; i += step)
			{
				var value = series.Values[i];
				if (float.IsNaN(value) || float.IsInfinity(value))
				{
					continue;
				}

				var time = DataSet.GetTimeAt(i);
				dataPoints.Add(new ChartDataPoint(time, value)
				{
					SeriesName = series.Name
				});
				pointCount++;
			}

			PerformanceLogger.Instance.LogInfo($"Double 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
			return dataPoints;
		}

        private void RemoveSeriesFromChart(SeriesData series)
        {
            var seriesToRemove = ChartControl.Series.FirstOrDefault(s => 
                s.Label?.ToString() == series.Name);
            if (seriesToRemove != null)
            {
                ChartControl.Series.Remove(seriesToRemove);
                RefreshAllSnapshots();
            }
        }

		private void RebuildDataValueRangeCache()
		{
			_cachedDataValueRange = null;
			if (DataSet?.SeriesData == null || DataSet.SeriesData.Count == 0)
			{
				return;
			}

			double min = double.MaxValue;
			double max = double.MinValue;

			foreach (var series in DataSet.SeriesData.Values)
			{
				if (series.DataType == typeof(bool))
				{
					min = Math.Min(min, 0);
					max = Math.Max(max, 1);
					continue;
				}

				if (series.Values == null || series.Values.Length == 0)
				{
					continue;
				}

				foreach (var value in series.Values)
				{
					if (float.IsNaN(value) || float.IsInfinity(value))
					{
						continue;
					}

					var doubleValue = value;
					if (doubleValue < min) min = doubleValue;
					if (doubleValue > max) max = doubleValue;
				}
			}

			if (min == double.MaxValue || max == double.MinValue)
			{
				return;
			}

			if (Math.Abs(max - min) < 0.001)
			{
				max = min + 1;
			}

			_cachedDataValueRange = (min, max);
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

			if (_cachedDataValueRange.HasValue)
			{
				return _cachedDataValueRange.Value;
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

		private int GetTargetPointBudget()
		{
			const int minBudget = 250;
			const int maxBudget = 1200;
			var width = ChartControl?.ActualWidth ?? 800;
			return (int)Math.Clamp(width, minBudget, maxBudget);
		}

		private bool CanAddMoreSeries()
		{
			if (ChartControl == null)
			{
				return true;
			}

			var activeCount = ChartControl.Series.Count(series => series.Tag is SeriesData);
			return activeCount < MaxActiveSeriesCount;
		}

		private void ShowSeriesLimitMessage()
		{
			MessageBox.Show(
				$"시리즈는 최대 {MaxActiveSeriesCount}개까지 추가할 수 있습니다.",
				"시리즈 제한",
				MessageBoxButton.OK,
				MessageBoxImage.Information);
		}

		private void SetSeriesSelection(ChartSeriesViewModel viewModel, bool isSelected)
		{
			if (viewModel.IsSelected == isSelected)
			{
				return;
			}

			_isUpdatingSeriesSelection = true;
			try
			{
				viewModel.IsSelected = isSelected;
			}
			finally
			{
				_isUpdatingSeriesSelection = false;
			}
		}

		private bool HasActiveCursorDrag()
		{
			for (int i = 0; i < _cursorHandles.Count; i++)
			{
				if (_cursorHandles[i].IsDragging)
				{
					return true;
				}
			}
			return false;
		}

		private void AddCursor_Click(object sender, RoutedEventArgs e)
		{
			if (DataSet == null || ChartControl == null) return;
            const int MaxCursorCount = 5;
            if (_cursorHandles.Count >= MaxCursorCount)
            {
                PerformanceLogger.Instance.LogInfo("커서는 최대 5개까지 추가할 수 있습니다.", "Chart_Display");
                return;
            }

            var cursorIndex = GetNextCursorIndex();
            var cursorTime = DataSet.GetTimeAt(cursorIndex);
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

            var cursorBrush = GetCursorColorByIndex(_cursorHandles.Count);

            var annotation = new VerticalLineAnnotation
            {
                X1 = cursorTime,
                X2 = cursorTime,
                Y1 = minValue,
                Y2 = maxValue,
                Stroke = cursorBrush,
				StrokeThickness = 2.5,
				CanDrag = true,
				CanResize = false,
				DraggingMode = AxisMode.Horizontal
            };

            ChartControl.Annotations?.Add(annotation);

            var snapshot = new CursorSnapshotViewModel
            {
                CursorIndex = _cursorHandles.Count + 1,
                CursorBrush = cursorBrush
            };
            SnapshotList.Add(snapshot);

            var handle = new ChartCursorHandle
            {
                Annotation = annotation,
                Index = cursorIndex,
                Snapshot = snapshot,
                CursorBrush = cursorBrush
            };
			annotation.Tag = handle;
			annotation.DragDelta += CursorAnnotation_DragDelta;
			annotation.DragCompleted += CursorAnnotation_DragCompleted;
			annotation.MouseLeftButtonDown += CursorAnnotation_MouseLeftButtonDown;
			annotation.MouseLeftButtonUp += CursorAnnotation_MouseLeftButtonUp;
            _cursorHandles.Add(handle);
            UpdateSnapshotForHandle(handle);
            RefreshSnapshotOrdering();

            RefreshCursorHandles();
        }

        private void RemoveCursor_Click(object sender, RoutedEventArgs e)
        {
			if (_cursorHandles.Count == 0 || ChartControl == null) return;

            var lastCursor = _cursorHandles[^1];
            _cursorHandles.RemoveAt(_cursorHandles.Count - 1);
			DetachCursorAnnotation(lastCursor);
			ChartControl.Annotations?.Remove(lastCursor.Annotation);
            if (lastCursor.Snapshot != null)
            {
                SnapshotList.Remove(lastCursor.Snapshot);
            }
            RefreshSnapshotOrdering();
        }

		private void CursorAnnotation_DragDelta(object? sender, AnnotationDragDeltaEventArgs e)
		{
			if (sender is not VerticalLineAnnotation annotation || annotation.Tag is not ChartCursorHandle handle || DataSet == null || e.NewValue == null)
			{
				return;
			}

			handle.IsDragging = true;

			double axisValue;
			var axisValueObject = e.NewValue.X1;
			var pointer = ChartControl != null ? Mouse.GetPosition(ChartControl) : new Point(double.NaN, double.NaN);
			switch (axisValueObject)
			{
				case double axisDouble:
					axisValue = axisDouble;
					break;
				case DateTime axisDateTimeValue:
					axisValue = axisDateTimeValue.ToOADate();
					break;
				case IConvertible convertible:
					axisValue = convertible.ToDouble(CultureInfo.InvariantCulture);
					break;
				default:
					LogCursorDebug("Annotation drag delta skipped: unsupported axis value type.");
					return;
			}

			if (!TryConvertAxisValueToIndex(axisValue, out var targetIndex))
			{
				e.Cancel = true;
				LogCursorDebug("Annotation drag delta cancelled: unable to convert axis value to index.");
				return;
			}

			handle.Index = targetIndex;
			UpdateCursorAnnotation(handle);

			var axisDateTime = DateTime.FromOADate(axisValue);
			var secondsOffset = (axisDateTime - DataSet.BaseTime).TotalSeconds;
			var interval = Math.Max(DataSet.TimeInterval, 0.001f);
			var theoreticalIndex = secondsOffset / interval;
			var indexError = handle.Index - theoreticalIndex;
			var pointerX = pointer.X;
			var shouldLog = ShouldLogCursorDelta(handle, axisValue, pointerX, handle.Index, indexError, forceLog: false, out var triggerReason);
			if (shouldLog)
			{
				var message = $"Drag snapshot[{triggerReason}] cursor={handle.Index:F2}, axisValue={axisValue:F5}, secondsOffset={secondsOffset:F2}s, interval={interval:F3}s, pointerX={(double.IsNaN(pointerX) ? double.NaN : pointerX):F2}, idxError={indexError:F2}";
				LogCursorDebug(message);
			}
		}

		private void CursorAnnotation_DragCompleted(object? sender, AnnotationDragCompletedEventArgs e)
		{
			if (sender is not VerticalLineAnnotation annotation || annotation.Tag is not ChartCursorHandle handle)
			{
				return;
			}

			var wasDragging = handle.IsDragging;
			handle.IsDragging = false;
			LogCursorDebug($"Annotation drag completed for cursor index={handle.Index:F2}");
			UpdateCursorAnnotation(handle);
			if (wasDragging || _pendingSnapshotRefresh)
			{
				RefreshAllSnapshots();
			}
		}

		private void CursorAnnotation_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
		{
			if (sender is not VerticalLineAnnotation annotation || annotation.Tag is not ChartCursorHandle handle)
			{
				return;
			}

			handle.IsDragging = true;
			var pointer = ChartControl != null ? e.GetPosition(ChartControl) : new Point(double.NaN, double.NaN);
			var axisValue = annotation.X1 is DateTime dt ? dt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : annotation.X1?.ToString() ?? "<null>";
			LogCursorDebug($"Annotation mouse down -> cursorIndex={handle.Index:F2}, pointer=({pointer.X:F2},{pointer.Y:F2}), axis={axisValue}");
		}

		private void CursorAnnotation_MouseLeftButtonUp(object? sender, MouseButtonEventArgs e)
		{
			if (sender is not VerticalLineAnnotation annotation || annotation.Tag is not ChartCursorHandle handle)
			{
				return;
			}

			var wasDragging = handle.IsDragging;
			handle.IsDragging = false;
			var pointer = ChartControl != null ? e.GetPosition(ChartControl) : new Point(double.NaN, double.NaN);
			var axisValue = annotation.X1 is DateTime dt ? dt.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture) : annotation.X1?.ToString() ?? "<null>";
			LogCursorDebug($"Annotation mouse up -> cursorIndex={handle.Index:F2}, pointer=({pointer.X:F2},{pointer.Y:F2}), axis={axisValue}");
			UpdateCursorAnnotation(handle);
			if (wasDragging || _pendingSnapshotRefresh)
			{
				RefreshAllSnapshots();
			}
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
                handle.Annotation.StrokeThickness = 2.5;
                handle.Annotation.Stroke = handle.CursorBrush;
            }

			if (!handle.IsDragging)
			{
				UpdateSnapshotForHandle(handle);
			}
        }



		private void RefreshCursorHandles()
		{
			if (_cursorHandles.Count == 0 || DataSet == null || ChartControl == null)
			{
				return;
			}

			if (_isRefreshingCursorHandles || HasActiveCursorDrag())
			{
				return;
			}

			try
			{
				_isRefreshingCursorHandles = true;
				foreach (var handle in _cursorHandles)
				{
					UpdateCursorAnnotation(handle);
				}
			}
			finally
			{
				_isRefreshingCursorHandles = false;
			}
		}

        private void RefreshSnapshotOrdering()
        {
            for (int i = 0; i < _cursorHandles.Count; i++)
            {
                var handle = _cursorHandles[i];
                if (handle.Snapshot != null)
                {
                    handle.Snapshot.CursorIndex = i + 1;
                }
            }
        }

		private void RefreshAllSnapshots()
		{
			if (_cursorHandles.Count == 0 || DataSet == null)
			{
				_pendingSnapshotRefresh = false;
				return;
			}

			if (HasActiveCursorDrag())
			{
				_pendingSnapshotRefresh = true;
				return;
			}

			_pendingSnapshotRefresh = false;
			foreach (var handle in _cursorHandles)
			{
				if (handle.IsDragging)
				{
					continue;
				}
				UpdateSnapshotForHandle(handle);
			}
		}

        private void UpdateSnapshotForHandle(ChartCursorHandle handle)
        {
			if (DataSet == null || handle.Snapshot == null || handle.IsDragging)
            {
                return;
            }

            var sampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet.TotalSamples - 1));
            var cursorTime = DataSet.TotalSamples > 0 ? DataSet.GetTimeAt(sampleIndex) : default;
            handle.Snapshot.CursorTime = cursorTime;

            var activeSeries = GetActiveSeriesData().ToList();
            UpdateSnapshotSeriesValues(handle.Snapshot, activeSeries, sampleIndex);
        }

		private void UpdateSnapshotSeriesValues(CursorSnapshotViewModel snapshot, List<SeriesData> activeSeries, int sampleIndex)
        {
			var limitedSeries = activeSeries.Take(MaxSnapshotSeriesValues).ToList();
            for (int i = snapshot.SeriesValues.Count - 1; i >= 0; i--)
            {
                var existing = snapshot.SeriesValues[i];
				if (!limitedSeries.Any(s => string.Equals(s.Name, existing.SeriesName, StringComparison.OrdinalIgnoreCase)))
                {
                    snapshot.SeriesValues.RemoveAt(i);
                }
            }

			foreach (var series in limitedSeries)
            {
                var valueText = GetSeriesValueText(series, sampleIndex);
                var unitText = series.Unit ?? string.Empty;
                var entry = snapshot.SeriesValues.FirstOrDefault(v => string.Equals(v.SeriesName, series.Name, StringComparison.OrdinalIgnoreCase));
                if (entry == null)
                {
                    snapshot.SeriesValues.Add(new CursorSeriesValueViewModel
                    {
                        SeriesName = series.Name,
                        Unit = unitText,
                        ValueText = valueText
                    });
                }
                else
                {
                    entry.Unit = unitText;
                    entry.ValueText = valueText;
                }
            }
        }

        private IEnumerable<SeriesData> GetActiveSeriesData()
        {
            if (ChartControl == null)
            {
                yield break;
            }

            foreach (var chartSeries in ChartControl.Series)
            {
                if (chartSeries.Tag is SeriesData data)
                {
                    yield return data;
                }
            }
        }

        private string GetSeriesValueText(SeriesData series, int sampleIndex)
        {
            if (series.DataType == typeof(bool))
            {
                if (series.BitValues == null || sampleIndex < 0 || sampleIndex >= series.BitValues.Length)
                {
                    return "-";
                }

                return series.BitValues[sampleIndex] ? "On" : "Off";
            }

            if (series.Values == null || sampleIndex < 0 || sampleIndex >= series.Values.Length)
            {
                return "-";
            }

            var value = series.Values[sampleIndex];
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return "-";
            }

            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }


        private bool TryConvertAxisValueToIndex(double axisValue, out int sampleIndex)
        {
            sampleIndex = 0;
            if (DataSet == null || DataSet.TotalSamples <= 0)
            {
                return false;
            }

            var time = DateTime.FromOADate(axisValue);
            var interval = Math.Max(DataSet.TimeInterval, 0.001f);
            var seconds = (time - DataSet.BaseTime).TotalSeconds;
            var index = (int)Math.Round(seconds / interval);
            sampleIndex = (int)Math.Clamp(index, 0, DataSet.TotalSamples - 1);
            return true;
        }

		private static bool ShouldLogCursorDelta(
			ChartCursorHandle handle,
			double axisValue,
			double pointerX,
			double targetIndex,
			double indexError,
			bool forceLog,
			out string triggerReason)
		{
			const double axisThreshold = 0.03; // ~= 43.2 min in OADate units
			const double indexThreshold = 160;
			const double pointerThreshold = 80; // pixels
			const double driftThreshold = 3.0; // indices
			var now = DateTime.UtcNow;
			var elapsed = now - handle.LastLogTimestamp;
			var axisChanged = double.IsNaN(handle.LastLoggedAxisValue) || Math.Abs(axisValue - handle.LastLoggedAxisValue) >= axisThreshold;
			var indexChanged = double.IsNaN(handle.LastLoggedIndex) || Math.Abs(targetIndex - handle.LastLoggedIndex) >= indexThreshold;
			var pointerChanged = !double.IsNaN(pointerX) && (double.IsNaN(handle.LastLoggedPointerX) || Math.Abs(pointerX - handle.LastLoggedPointerX) >= pointerThreshold);
			var driftDetected = Math.Abs(indexError) >= driftThreshold;
			var minInterval = handle.IsDragging ? TimeSpan.FromSeconds(1.25) : TimeSpan.FromSeconds(0.9);
			if (elapsed < minInterval)
			{
				axisChanged = false;
				indexChanged = false;
				pointerChanged = false;
			}
			var timeExpired = elapsed >= (handle.IsDragging ? TimeSpan.FromSeconds(4.5) : TimeSpan.FromSeconds(3));
			triggerReason = string.Empty;

			bool shouldLog = forceLog || driftDetected || axisChanged || indexChanged || pointerChanged || timeExpired;
			if (shouldLog)
			{
				handle.LastLoggedAxisValue = axisValue;
				handle.LastLoggedIndex = targetIndex;
				handle.LastLoggedPointerX = pointerX;
				handle.LastLogTimestamp = now;
				triggerReason = driftDetected ? "drift" :
					axisChanged ? "axis" :
					indexChanged ? "index" :
					pointerChanged ? "pointer" :
					timeExpired ? "interval" : "forced";
			}
			return shouldLog;
		}

        private void LogCursorDebug(string message)
        {
			if (!EnableCursorDebugLogging)
			{
				return;
			}
            PerformanceLogger.Instance.LogInfo(message, "Cursor_Debug");
            Console.WriteLine($"[CursorDebug] {message}");
        }

        private void ClearCursorHandles()
        {
			if (ChartControl != null)
			{
				foreach (var handle in _cursorHandles)
				{
					DetachCursorAnnotation(handle);
					if (handle.Annotation != null)
					{
						ChartControl.Annotations?.Remove(handle.Annotation);
					}
				}
			}

            _cursorHandles.Clear();
            SnapshotList.Clear();
        }

		private void DetachCursorAnnotation(ChartCursorHandle handle)
		{
			if (handle.Annotation == null)
			{
				return;
			}

			handle.Annotation.DragDelta -= CursorAnnotation_DragDelta;
			handle.Annotation.DragCompleted -= CursorAnnotation_DragCompleted;
			handle.Annotation.MouseLeftButtonDown -= CursorAnnotation_MouseLeftButtonDown;
			handle.Annotation.MouseLeftButtonUp -= CursorAnnotation_MouseLeftButtonUp;
		}

	private sealed class ChartCursorHandle
	{
		public VerticalLineAnnotation? Annotation { get; set; }
		public double Index { get; set; }
		public CursorSnapshotViewModel? Snapshot { get; set; }
		public Brush CursorBrush { get; set; } = Brushes.Crimson;
		public double LastLoggedAxisValue { get; set; } = double.NaN;
		public double LastLoggedIndex { get; set; } = double.NaN;
		public double LastLoggedPointerX { get; set; } = double.NaN;
		public DateTime LastLogTimestamp { get; set; } = DateTime.MinValue;
		public bool IsDragging { get; set; }
	}

        public sealed class CursorSnapshotViewModel : INotifyPropertyChanged
        {
            private int _cursorIndex;
            private DateTime _cursorTime;
            private Brush _cursorBrush = Brushes.Gray;

            public int CursorIndex
            {
                get => _cursorIndex;
                set
                {
                    if (_cursorIndex != value)
                    {
                        _cursorIndex = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(CursorLabel));
                    }
                }
            }

            public DateTime CursorTime
            {
                get => _cursorTime;
                set
                {
                    if (_cursorTime != value)
                    {
                        _cursorTime = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(CursorTimeDisplay));
                    }
                }
            }

            public string CursorLabel => $"Cursor {_cursorIndex}";
            public string CursorTimeDisplay => _cursorTime == default ? "-" : _cursorTime.ToString("yyyy-MM-dd HH:mm:ss.fff");
            public Brush CursorBrush
            {
                get => _cursorBrush;
                set
                {
                    if (_cursorBrush != value)
                    {
                        _cursorBrush = value;
                        OnPropertyChanged();
                    }
                }
            }

            public ObservableCollection<CursorSeriesValueViewModel> SeriesValues { get; } = new();

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        public sealed class CursorSeriesValueViewModel : INotifyPropertyChanged
        {
            private string _seriesName = string.Empty;
            private string _unit = string.Empty;
            private string _valueText = "-";

            public string SeriesName
            {
                get => _seriesName;
                set
                {
                    if (_seriesName != value)
                    {
                        _seriesName = value;
                        OnPropertyChanged();
                    }
                }
            }

            public string Unit
            {
                get => _unit;
                set
                {
                    if (_unit != value)
                    {
                        _unit = value;
                        OnPropertyChanged();
                    }
                }
            }

            public string ValueText
            {
                get => _valueText;
                set
                {
                    if (_valueText != value)
                    {
                        _valueText = value;
                        OnPropertyChanged();
                    }
                }
            }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private bool GetBoolValue(System.Collections.BitArray? bitValues, int index)
        {
            if (bitValues == null) return false;
            if (index < 0 || index >= bitValues.Length) return false;
            return bitValues[index];
        }

        private Brush GetCursorColorByIndex(int index)
        {
            if (CursorColorPalette.Length == 0)
            {
                return Brushes.Crimson;
            }

            return CursorColorPalette[index % CursorColorPalette.Length];
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