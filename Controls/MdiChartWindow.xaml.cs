using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Microsoft.Win32;
using System.IO;
using System.Reflection;
using MGK_Analyzer.Models;
using MGK_Analyzer.Services;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Charts;
using OfficeOpenXml;
using OfficeOpenXml.Table;

namespace MGK_Analyzer.Controls
{
        public partial class MdiChartWindow : UserControl, INotifyPropertyChanged
        {
            private bool _isDragging = false;
            private bool _isResizing = false;
            private Point _lastPosition;
        private static int _globalZIndex = 1; // deprecated local counter; retained to avoid widespread edits
        private MemoryOptimizedDataSet? _dataSet;
        private DataView? _dataTableView;
        private DataTable? _dataTableBackingStore;
        private string _dataTableStatus = "No data loaded.";
        private string _baseDataTableStatus = "No data loaded.";
        private CancellationTokenSource? _dataTableBuildCancellation;
        private const int DataTableRowLimit = 20000;
        private const int DataTablePageSize = 100;
        private int _currentPageIndex;
        private int _totalPageCount;
        private bool _isManagementPanelExpanded = true;
        private bool _isManagementPanelPinned = false;
        private bool _isLeftPanelExpanded = true;
        private bool _isLeftPanelPinned = false;
		private const double LeftPanelCollapsedWidth = 12;
		private const double LeftPanelTabColumnWidth = 12;
        private const double LeftPanelSplitterVisibleWidth = 5;
        private double _savedLeftPanelWidth = 320;
        private ColumnDefinition? _leftPanelColumn;
        private ColumnDefinition? _leftPanelSplitterColumn;
		private readonly List<ChartCursorHandle> _cursorHandles = new();
		private const int MaxActiveSeriesCount = 10;
		private const int MaxSnapshotSeriesValues = MaxActiveSeriesCount;
		private const bool EnableCursorDebugLogging = false;
		private const bool EnableSeriesAxisDebugLogging = true;
		private bool _isRefreshingCursorHandles;
		private bool _pendingSnapshotRefresh;
		private bool _isUpdatingSeriesSelection;
		private readonly DispatcherTimer _cursorSnapshotTimer;
		private bool _cursorSnapshotDirty;
		private readonly DispatcherTimer _cursorLayoutTimer;
		private readonly DispatcherTimer _cursorResumeTimer;
		private bool _cursorLayoutPending;
		private bool _suspendCursorRefresh;
            private static readonly Brush[] CursorColorPalette =
            {
                Brushes.Crimson,
                Brushes.DodgerBlue,
                Brushes.MediumSeaGreen,
                Brushes.Goldenrod,
                Brushes.MediumOrchid
            };
		private readonly Dictionary<string, NumericalAxis> _seriesAxes = new(StringComparer.OrdinalIgnoreCase);
		private NumericalAxis? _defaultSecondaryAxis;
		private AxisAppearanceSnapshot? _defaultSecondaryAxisAppearance;
		private Canvas? _cursorOverlay;
		private (double min, double max)? _cachedDataValueRange;
        private HashSet<string> _initialSeriesSelection = new(StringComparer.OrdinalIgnoreCase);
		private bool _overlayTransformDirty = true;
		private double _overlayOffsetX;
		private double _overlayOffsetY;
        
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

        public DataView? DataTableView
        {
            get => _dataTableView;
            private set { _dataTableView = value; OnPropertyChanged(); }
        }

        public string DataTableStatus
        {
            get => _dataTableStatus;
            private set { _dataTableStatus = value; OnPropertyChanged(); }
        }

        public int TotalPageCount
        {
            get => _totalPageCount;
            private set
            {
                if (_totalPageCount != value)
                {
                    _totalPageCount = value;
                    OnPropertyChanged();
                }
            }
		}

		private static void ApplyAxisHiddenStyle(ChartAxis axis)
		{
			var transparent = new SolidColorBrush(Colors.Transparent);

			axis.Foreground = transparent;

			if (axis.LabelStyle == null)
			{
				axis.LabelStyle = new Syncfusion.UI.Xaml.Charts.LabelStyle();
			}
			axis.LabelStyle.Foreground = transparent;

			if (axis.HeaderStyle == null)
			{
				axis.HeaderStyle = new Syncfusion.UI.Xaml.Charts.LabelStyle();
			}
			axis.HeaderStyle.Foreground = transparent;

			var axisLineStyle = new Style(typeof(System.Windows.Shapes.Line));
			axisLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeProperty, transparent));
			axisLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeThicknessProperty, 0.0));
			axis.AxisLineStyle = axisLineStyle;

			var tickLineStyle = new Style(typeof(System.Windows.Shapes.Line));
			tickLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeProperty, transparent));
			tickLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeThicknessProperty, 0.0));
			axis.MajorTickLineStyle = tickLineStyle;
			axis.MinorTickLineStyle = tickLineStyle;
		}

		private static void ApplyAxisVisibleStyle(ChartAxis axis)
		{
			// Restore to theme defaults by clearing our overrides.
			axis.Foreground = null;
			if (axis.LabelStyle != null)
			{
				axis.LabelStyle.Foreground = null;
			}
			if (axis.HeaderStyle != null)
			{
				axis.HeaderStyle.Foreground = null;
			}
			axis.AxisLineStyle = null;
			axis.MajorTickLineStyle = null;
			axis.MinorTickLineStyle = null;
		}

		private void UpdateDefaultValueAxisVisibility()
		{
			if (ChartControl?.SecondaryAxis == null)
			{
				return;
			}

			// When any per-series axes exist, hide the default "Value" axis so
			// it doesn't visually conflict with colored per-series axes.
			var hasCustomAxes = _seriesAxes.Count > 0;
			if (hasCustomAxes)
			{
				ApplyAxisHiddenStyle(ChartControl.SecondaryAxis);
			}
			else
			{
				if (_defaultSecondaryAxisAppearance != null)
				{
					_defaultSecondaryAxisAppearance.Restore(ChartControl.SecondaryAxis);
				}
				else
				{
					ApplyAxisVisibleStyle(ChartControl.SecondaryAxis);
				}
			}
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
                HideLeftPanel();
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
            ShowLeftPanel();
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
                HideLeftPanel();
            }
            else
            {
                ShowLeftPanel();
            }
        }

        private void HideLeftPanel()
        {
            if (!_isLeftPanelExpanded)
            {
                return;
            }

            StoreCurrentLeftPanelWidth();
            LeftPanel.Visibility = Visibility.Collapsed;
            LeftPanelTab.Visibility = Visibility.Visible;

			var collapsedWidth = LeftPanelTab.ActualWidth > 0
				? LeftPanelTab.ActualWidth
				: LeftPanelCollapsedWidth;
			collapsedWidth = Math.Min(collapsedWidth, LeftPanelTabColumnWidth);
            if (_leftPanelColumn != null)
            {
                _leftPanelColumn.Width = new GridLength(collapsedWidth);
            }

            if (_leftPanelSplitterColumn != null)
            {
                _leftPanelSplitterColumn.Width = new GridLength(0);
            }

            _isLeftPanelExpanded = false;
        }

        private void ShowLeftPanel()
        {
            if (_isLeftPanelExpanded)
            {
                return;
            }

            LeftPanel.Visibility = Visibility.Visible;
            LeftPanelTab.Visibility = Visibility.Collapsed;

            if (_leftPanelColumn != null)
            {
                var targetWidth = Math.Max(_savedLeftPanelWidth, _leftPanelColumn.MinWidth);
                _leftPanelColumn.Width = new GridLength(targetWidth);
            }

            if (_leftPanelSplitterColumn != null)
            {
                _leftPanelSplitterColumn.Width = new GridLength(LeftPanelSplitterVisibleWidth);
            }

            _isLeftPanelExpanded = true;
        }

        private void StoreCurrentLeftPanelWidth()
        {
            if (_leftPanelColumn == null)
            {
                return;
            }

            var currentWidth = _leftPanelColumn.ActualWidth;
            if (double.IsNaN(currentWidth) || currentWidth <= 0)
            {
                currentWidth = _leftPanelColumn.Width.Value;
            }

            if (currentWidth > 0)
            {
                _savedLeftPanelWidth = Math.Max(currentWidth, _leftPanelColumn.MinWidth);
            }
        }

        private void LeftPanelSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            StoreCurrentLeftPanelWidth();
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
                RebuildDataTableViewAsync();
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
            ApplyInheritedTheme();
			_cursorOverlay = FindName("CursorOverlay") as Canvas;
            _leftPanelColumn = (ColumnDefinition?)FindName("LeftPanelColumn");
            _leftPanelSplitterColumn = (ColumnDefinition?)FindName("LeftPanelSplitterColumn");
            this.PreviewMouseDown += (_, __) => MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
            
            _isManagementPanelExpanded = true;
            _isManagementPanelPinned = false;
            _isLeftPanelExpanded = true;
            _isLeftPanelPinned = false;

            // XAML에 정의된 기본 축을 사용하거나, 여기서 코드로 설정
			ChartControl.PrimaryAxis = new NumericalAxis { Header = "Elapsed Time (s)", LabelFormat = "0.###" };
            if (ChartControl.SecondaryAxis is NumericalAxis numericAxis)
            {
                _defaultSecondaryAxis = numericAxis;
            }
            else
            {
                _defaultSecondaryAxis = new NumericalAxis { Header = "Value" };
                ChartControl.SecondaryAxis = _defaultSecondaryAxis;
            }

			_defaultSecondaryAxisAppearance = AxisAppearanceSnapshot.Capture(ChartControl.SecondaryAxis);

            ChartControl.SizeChanged += (_, __) => ScheduleCursorLayoutRefresh();
            ChartControl.LayoutUpdated += (_, __) => ScheduleCursorLayoutRefresh();
            ChartControl.Loaded += (_, __) =>
            {
				_cursorOverlay ??= FindName("CursorOverlay") as Canvas;
                RefreshCursorHandles();
            };
			if (_cursorOverlay != null)
			{
				_cursorOverlay.SizeChanged += (_, __) => ScheduleCursorLayoutRefresh();
				_cursorOverlay.LayoutUpdated += (_, __) => ScheduleCursorLayoutRefresh();
			}
            Loaded += (_, __) => InitializeLeftPanelSizing();

			_cursorSnapshotTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(200)
			};
			_cursorSnapshotTimer.Tick += (_, __) => RefreshSnapshotsDuringDrag();

			_cursorLayoutTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(200)
			};
			_cursorLayoutTimer.Tick += (_, __) => ProcessCursorLayoutRefresh();

			_cursorResumeTimer = new DispatcherTimer
			{
				Interval = TimeSpan.FromMilliseconds(200)
			};
			_cursorResumeTimer.Tick += (_, __) => ResumeCursorRefresh();
        }

		private sealed record AxisAppearanceSnapshot(
			Brush? Foreground,
			SolidColorBrush? LabelForeground,
			SolidColorBrush? HeaderForeground,
			Style? AxisLineStyle,
			Style? MajorTickLineStyle,
			Style? MinorTickLineStyle)
		{
			public static AxisAppearanceSnapshot Capture(ChartAxis axis)
			{
				return new AxisAppearanceSnapshot(
					axis.Foreground,
					axis.LabelStyle?.Foreground,
					axis.HeaderStyle?.Foreground,
					axis.AxisLineStyle,
					axis.MajorTickLineStyle,
					axis.MinorTickLineStyle);
			}

			public void Restore(ChartAxis axis)
			{
				axis.Foreground = Foreground;
				if (axis.LabelStyle != null)
				{
					axis.LabelStyle.Foreground = LabelForeground;
				}
				if (axis.HeaderStyle != null)
				{
					axis.HeaderStyle.Foreground = HeaderForeground;
				}

				axis.AxisLineStyle = AxisLineStyle;
				axis.MajorTickLineStyle = MajorTickLineStyle;
				axis.MinorTickLineStyle = MinorTickLineStyle;
			}
		}

        private void InitializeLeftPanelSizing()
        {
            if (_leftPanelColumn != null)
            {
                var configuredWidth = _leftPanelColumn.Width.Value;
                if (configuredWidth > 0)
                {
                    _savedLeftPanelWidth = Math.Max(configuredWidth, _leftPanelColumn.MinWidth);
                }
            }

            if (_leftPanelSplitterColumn != null)
            {
                _leftPanelSplitterColumn.Width = new GridLength(LeftPanelSplitterVisibleWidth);
            }
        }

        private void ApplyInheritedTheme()
        {
            try
            {
                var mainWindow = Application.Current?.MainWindow;
                if (mainWindow == null)
                {
                    return;
                }

                var parentTheme = SfSkinManager.GetTheme(mainWindow);
                if (parentTheme != null)
                {
                    SfSkinManager.SetTheme(this, new Theme(parentTheme.ThemeName));
                }
            }
            catch
            {
                // Theme application is best-effort; ignore failures to avoid blocking window creation.
            }
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

			var seriesBrush = ResolveSeriesSolidBrush(series);
			LogSeriesAxisDebug($"AddSeriesToChart start: name='{series.Name}', series.Color={FormatBrush(series.Color)}, resolvedStroke={FormatBrush(seriesBrush)}");

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
			List<RelativeChartDataPoint> dataPoints;

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
				dynSeries.XBindingPath = "RelativeSeconds";
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
				var axis = GetAxisForSeries(series);
				dynSeries.YAxis = axis;
				LogSeriesAxisDebug($"AddSeriesToChart axis assigned: name='{series.Name}', axisHeader='{axis.Header}', axisForeground={FormatBrush(axis.Foreground)}");
            }
            catch
            {
                // Some series types may not expose a YAxis property; ignore in that case.
            }

			ApplySeriesBrush(chartSeries, seriesBrush);

            ChartControl.Series.Add(chartSeries);
			ApplySeriesBrush(chartSeries, seriesBrush);
			DumpChartSeriesBrushes(chartSeries, series);
			LogSeriesAxisDebug($"AddSeriesToChart done: name='{series.Name}', chartSeriesType='{chartSeries.GetType().Name}', seriesCount={ChartControl.Series.Count}");
			UpdateDefaultValueAxisVisibility();
			DumpAxesDebug($"after AddSeriesToChart '{series.Name}'");
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

		private List<RelativeChartDataPoint> CreateBoolDataPoints(SeriesData series, int step, int maxPoints)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Bool 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
			var dataPoints = new List<RelativeChartDataPoint>();
			var pointCount = 0;
            
			for (int i = 0; i < DataSet.TotalSamples && pointCount < maxPoints; i += step)
            {
                bool value = GetBoolValue(series.BitValues, i);
				dataPoints.Add(new RelativeChartDataPoint(DataSet.GetRelativeTimeAt(i), value ? 1.0 : 0.0)
                {
                    SeriesName = series.Name
                });
                pointCount++;
            }
            
            PerformanceLogger.Instance.LogInfo($"Bool 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
            return dataPoints;
        }

		private List<RelativeChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step, int maxPointsToCreate)
		{
			using var timer = PerformanceLogger.Instance.StartTimer($"Double 데이터 포인트 생성: {series.Name}", "Chart_Display");
			var dataPoints = new List<RelativeChartDataPoint>();
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

				var t = DataSet.GetRelativeTimeAt(i);
				dataPoints.Add(new RelativeChartDataPoint(t, value)
				{
					SeriesName = series.Name
				});
				pointCount++;
			}

			PerformanceLogger.Instance.LogInfo($"Double 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
			return dataPoints;
		}

		private sealed class RelativeChartDataPoint
		{
			public RelativeChartDataPoint(double relativeSeconds, double value)
			{
				RelativeSeconds = relativeSeconds;
				Value = value;
			}

			public double RelativeSeconds { get; }
			public double Value { get; }
			public string? SeriesName { get; set; }
		}

        private void RemoveSeriesFromChart(SeriesData series)
        {
            var seriesToRemove = ChartControl.Series.FirstOrDefault(s => 
                s.Label?.ToString() == series.Name);
            if (seriesToRemove != null)
            {
				LogSeriesAxisDebug($"RemoveSeriesFromChart: name='{series.Name}', seriesCountBefore={ChartControl.Series.Count}");
                ChartControl.Series.Remove(seriesToRemove);
				RemoveAxisForSeries(series);
                RefreshAllSnapshots();
				LogSeriesAxisDebug($"RemoveSeriesFromChart done: name='{series.Name}', seriesCountAfter={ChartControl.Series.Count}");
				UpdateDefaultValueAxisVisibility();
				DumpAxesDebug($"after RemoveSeriesFromChart '{series.Name}'");
            }
        }

		private void RemoveAxisForSeries(SeriesData series)
		{
			if (ChartControl == null || series == null)
			{
				return;
			}

			var key = string.IsNullOrWhiteSpace(series.Name) ? "<unknown>" : series.Name.Trim();
			if (_seriesAxes.TryGetValue(key, out var axis))
			{
				ChartControl.Axes.Remove(axis);
				_seriesAxes.Remove(key);
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

		private NumericalAxis CreateAxisForSeries(SeriesData series)
		{
			var header = string.IsNullOrWhiteSpace(series.Unit)
				? series.Name
				: $"{series.Name} ({series.Unit})";

			var brush = ResolveSeriesSolidBrush(series);
			LogSeriesAxisDebug($"CreateAxisForSeries: name='{series.Name}', unit='{series.Unit}', series.Color={FormatBrush(series.Color)}, resolved={FormatBrush(brush)}");
			var axisLineStyle = new Style(typeof(System.Windows.Shapes.Line));
			axisLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeProperty, brush));
			axisLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeThicknessProperty, 1.5));

			var tickLineStyle = new Style(typeof(System.Windows.Shapes.Line));
			tickLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeProperty, brush));
			tickLineStyle.Setters.Add(new Setter(System.Windows.Shapes.Line.StrokeThicknessProperty, 1.0));

			var labelStyle = new Syncfusion.UI.Xaml.Charts.LabelStyle
			{
				Foreground = brush
			};

			var headerStyle = new Syncfusion.UI.Xaml.Charts.LabelStyle
			{
				Foreground = brush
			};

			var axis = new NumericalAxis
			{
				Header = header,
				OpposedPosition = false,
				ShowGridLines = false,
				Foreground = brush,
				LabelStyle = labelStyle,
				HeaderStyle = headerStyle,
				AxisLineStyle = axisLineStyle,
				MajorTickLineStyle = tickLineStyle,
				MinorTickLineStyle = tickLineStyle
			};

			var labelForeground = axis.LabelStyle?.Foreground;
			var headerForeground = axis.HeaderStyle?.Foreground;
			LogSeriesAxisDebug($"CreateAxisForSeries result: header='{axis.Header}', foreground={FormatBrush(axis.Foreground)}, labelFg={FormatBrush(labelForeground)}, headerFg={FormatBrush(headerForeground)}");

			return axis;
		}

		private static SolidColorBrush ResolveSeriesSolidBrush(SeriesData? series)
		{
			if (series == null)
			{
				return new SolidColorBrush(Colors.Black);
			}

			if (series.Color is SolidColorBrush solid)
			{
				return solid;
			}

			if (series.Color is GradientBrush gradient && gradient.GradientStops != null && gradient.GradientStops.Count > 0)
			{
				return new SolidColorBrush(gradient.GradientStops[0].Color);
			}

			return new SolidColorBrush(Colors.Black);
		}

		private async void RebuildDataTableViewAsync()
		{
			_dataTableBuildCancellation?.Cancel();
			_dataTableBuildCancellation?.Dispose();

			if (DataSet == null || DataSet.SeriesData == null || DataSet.SeriesData.Count == 0 || DataSet.TotalSamples <= 0)
			{
                _dataTableBackingStore = null;
                DataTableView = null;
                DataTableStatus = "No data loaded.";
				return;
			}

			var cts = new CancellationTokenSource();
			_dataTableBuildCancellation = cts;
            DataTableStatus = "Building data table...";

			try
			{
				var result = await Task.Run(() => BuildDataTableInternal(DataSet!, cts.Token), cts.Token);
				if (cts.IsCancellationRequested)
				{
					return;
				}

				if (result.table == null)
				{
					_dataTableBackingStore = null;
					DataTableView = null;
                    DataTableStatus = string.IsNullOrWhiteSpace(result.status) ? "No data available." : result.status;
					_baseDataTableStatus = DataTableStatus;
					ResetPagingControls();
					return;
				}

				_dataTableBackingStore = result.table;
				_baseDataTableStatus = result.status;
				InitializePaging();
			}
			catch (OperationCanceledException)
			{
				// no-op
			}
			catch (Exception ex)
			{
                PerformanceLogger.Instance.LogError($"Data table generation failed: {ex.Message}", "Chart_Display");
				_dataTableBackingStore = null;
				DataTableView = null;
                DataTableStatus = "Unable to build data table.";
			}
			finally
			{
				if (_dataTableBuildCancellation == cts)
				{
					_dataTableBuildCancellation.Dispose();
					_dataTableBuildCancellation = null;
				}
			}
		}

		private (DataTable? table, string status) BuildDataTableInternal(MemoryOptimizedDataSet dataSet, CancellationToken token)
		{
			try
			{
				var dataSeries = dataSet.SeriesData.Values
					.Where(series => !IsTimestampSeries(series?.Name))
					.ToList();

				if (dataSeries.Count == 0)
				{
					return (null, "No measurement columns available.");
				}

				var table = new DataTable("MeasurementTable");
				table.Columns.Add("Elapsed Time (s)", typeof(double));

				foreach (var series in dataSeries)
				{
					var columnType = series.DataType == typeof(bool) ? typeof(bool) : typeof(double);
					var columnName = string.IsNullOrWhiteSpace(series.Name)
						? $"Column {table.Columns.Count}"
						: series.Name;
					table.Columns.Add(columnName, columnType);
				}

				var totalRows = dataSet.TotalSamples;
				var rowLimit = Math.Min(totalRows, DataTableRowLimit);

				for (int rowIndex = 0; rowIndex < rowLimit; rowIndex++)
				{
					token.ThrowIfCancellationRequested();

					var row = table.NewRow();
					row[0] = Math.Round(dataSet.GetRelativeTimeAt(rowIndex), 4);

					for (int columnIndex = 0; columnIndex < dataSeries.Count; columnIndex++)
					{
						row[columnIndex + 1] = GetSeriesCellValue(dataSeries[columnIndex], rowIndex);
					}

					table.Rows.Add(row);
				}

				var status = totalRows > rowLimit
					? $"Showing {rowLimit:N0} of {totalRows:N0} rows (relative time in seconds)"
					: $"Showing all {totalRows:N0} rows (relative time in seconds)";

				return (table, status);
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch (Exception ex)
			{
				return (null, $"Failed to build data table: {ex.Message}");
			}
		}

		private void InitializePaging()
		{
			if (_dataTableBackingStore == null || _dataTableBackingStore.Rows.Count == 0)
			{
				TotalPageCount = 0;
				DataTableView = null;
				UpdatePagingStatus(0, 0);
				ResetPagingControls();
				return;
			}

			TotalPageCount = (int)Math.Ceiling(_dataTableBackingStore.Rows.Count / (double)DataTablePageSize);
			_currentPageIndex = 0;
			LoadPage(_currentPageIndex);
		}

		private void LoadPage(int pageIndex)
		{
			if (_dataTableBackingStore == null || _dataTableBackingStore.Rows.Count == 0)
			{
				DataTableView = null;
				UpdatePagingStatus(0, 0);
				ResetPagingControls();
				return;
			}

			if (TotalPageCount == 0)
			{
				TotalPageCount = 1;
			}

			pageIndex = Math.Clamp(pageIndex, 0, Math.Max(0, TotalPageCount - 1));
			_currentPageIndex = pageIndex;

			var pageTable = _dataTableBackingStore.Clone();
			var startRow = pageIndex * DataTablePageSize;
			var endRow = Math.Min(startRow + DataTablePageSize, _dataTableBackingStore.Rows.Count);
			for (int i = startRow; i < endRow; i++)
			{
				pageTable.ImportRow(_dataTableBackingStore.Rows[i]);
			}

			DataTableView = pageTable.DefaultView;
			UpdatePagingStatus(startRow, endRow);
			UpdatePageNumberTextBox();
		}

		private void UpdatePagingStatus(int startRow, int endRow)
		{
			if (_dataTableBackingStore == null || _dataTableBackingStore.Rows.Count == 0)
			{
				DataTableStatus = _baseDataTableStatus;
				return;
			}

			var totalRows = _dataTableBackingStore.Rows.Count;
			if (endRow == 0)
			{
				endRow = Math.Min(DataTablePageSize, totalRows);
			}
			var pageInfo = TotalPageCount > 0
				? $"Page {_currentPageIndex + 1} / {TotalPageCount}"
				: "Page 0 / 0";
			DataTableStatus = $"{_baseDataTableStatus} | Rows {startRow + 1:N0}-{endRow:N0} of {totalRows:N0} | {pageInfo}";
		}

		private void ResetPagingControls()
		{
			_currentPageIndex = 0;
			TotalPageCount = 0;
			UpdatePageNumberTextBox();
		}

		private void UpdatePageNumberTextBox()
		{
			if (PageNumberTextBox == null)
			{
				return;
			}

			if (TotalPageCount == 0)
			{
				PageNumberTextBox.Text = string.Empty;
				PageNumberTextBox.IsEnabled = false;
			}
			else
			{
				PageNumberTextBox.IsEnabled = true;
				PageNumberTextBox.Text = (_currentPageIndex + 1).ToString(CultureInfo.InvariantCulture);
			}
		}

		private static bool IsTimestampSeries(string? name)
		{
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}

			return name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) ||
			       name.Equals("DateTime", StringComparison.OrdinalIgnoreCase);
		}

		private object GetSeriesCellValue(SeriesData series, int rowIndex)
		{
			if (series.DataType == typeof(bool))
			{
				return GetBoolValue(series.BitValues, rowIndex);
			}

			if (series.Values == null || rowIndex < 0 || rowIndex >= series.Values.Length)
			{
				return double.NaN;
			}

			var value = series.Values[rowIndex];
			if (float.IsNaN(value) || float.IsInfinity(value))
			{
				return double.NaN;
			}

			return Math.Round(value, 6);
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

			var key = string.IsNullOrWhiteSpace(series.Name) ? "<unknown>" : series.Name.Trim();
			if (_seriesAxes.TryGetValue(key, out var existingAxis))
			{
				LogSeriesAxisDebug($"GetAxisForSeries reuse: key='{key}', axisHeader='{existingAxis.Header}', axisForeground={FormatBrush(existingAxis.Foreground)}");
				return existingAxis;
			}

			var axis = CreateAxisForSeries(series);
			ChartControl.Axes.Add(axis);
			_seriesAxes[key] = axis;
			LogSeriesAxisDebug($"GetAxisForSeries add: key='{key}', axisHeader='{axis.Header}', axisForeground={FormatBrush(axis.Foreground)}, axesCount={ChartControl.Axes.Count}, cachedKeys=[{string.Join(",", _seriesAxes.Keys)}]");
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
			var cursorSeconds = DataSet.GetRelativeTimeAt(cursorIndex);
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

            var cursorBrush = GetCursorColorByIndex(_cursorHandles.Count);

            var annotation = new VerticalLineAnnotation
            {
				X1 = cursorSeconds,
				X2 = cursorSeconds,
                Y1 = minValue,
                Y2 = maxValue,
                Stroke = cursorBrush,
				StrokeThickness = 3.5,
				CanDrag = true,
				CanResize = false,
				DraggingMode = AxisMode.Horizontal
            };

			// Keep annotation barely visible so it remains hit-testable for drag.
			// (A fully transparent stroke prevents interaction in this Syncfusion build.)
			annotation.Opacity = 0.01;

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
			handle.OverlayLine = CreateCursorOverlayLine(cursorBrush);
			var caps = CreateCursorOverlayCaps(cursorBrush);
			if (caps.HasValue)
			{
				handle.OverlayTopCap = caps.Value.top;
				handle.OverlayBottomCap = caps.Value.bottom;
			}
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
			DetachOverlayLine(lastCursor);
			DetachOverlayCaps(lastCursor);
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
			MarkCursorSnapshotDirty();

			var secondsOffset = axisValue;
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
			StopCursorSnapshotTimer();
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
			StartCursorSnapshotTimer();
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
			StopCursorSnapshotTimer();
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
			var cursorSeconds = DataSet.GetRelativeTimeAt(sampleIndex);
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

            if (handle.Annotation != null)
            {
				handle.Annotation.X1 = cursorSeconds;
				handle.Annotation.X2 = cursorSeconds;
                handle.Annotation.Y1 = minValue;
                handle.Annotation.Y2 = maxValue;
				handle.Annotation.StrokeThickness = 3.5;
				handle.Annotation.Opacity = 0.01;
            }

			UpdateOverlayLine(handle);

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

			if (_isRefreshingCursorHandles || HasActiveCursorDrag() || _suspendCursorRefresh)
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

		private void ScheduleCursorLayoutRefresh()
		{
			if (_cursorHandles.Count == 0)
			{
				return;
			}

			_overlayTransformDirty = true;
			_cursorLayoutPending = true;
			if (!_cursorLayoutTimer.IsEnabled)
			{
				_cursorLayoutTimer.Start();
			}
		}

		private void ProcessCursorLayoutRefresh()
		{
			if (!_cursorLayoutPending)
			{
				_cursorLayoutTimer.Stop();
				return;
			}

			if (_suspendCursorRefresh || HasActiveCursorDrag())
			{
				return;
			}

			_cursorLayoutPending = false;
			_cursorLayoutTimer.Stop();
			RefreshCursorHandles();
		}

		private void SuspendCursorRefresh()
		{
			_suspendCursorRefresh = true;
			_cursorResumeTimer.Stop();
			_cursorResumeTimer.Start();
		}

		private void ResumeCursorRefresh()
		{
			_cursorResumeTimer.Stop();
			_suspendCursorRefresh = false;
			ScheduleCursorLayoutRefresh();
		}

		private void LeftPanelTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			SuspendCursorRefresh();
		}

		private void ManagementPanelTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			SuspendCursorRefresh();
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

		private void RefreshSnapshotsDuringDrag()
		{
			if (_cursorHandles.Count == 0 || DataSet == null)
			{
				StopCursorSnapshotTimer();
				return;
			}

			if (!_cursorSnapshotDirty)
			{
				return;
			}

			_cursorSnapshotDirty = false;
			foreach (var handle in _cursorHandles)
			{
				UpdateSnapshotForHandle(handle, allowWhileDragging: true);
			}
		}

		private void UpdateSnapshotForHandle(ChartCursorHandle handle, bool allowWhileDragging = false)
        {
			if (DataSet == null || handle.Snapshot == null || (handle.IsDragging && !allowWhileDragging))
            {
                return;
            }

            var sampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet.TotalSamples - 1));
			var cursorSeconds = DataSet.TotalSamples > 0 ? DataSet.GetRelativeTimeAt(sampleIndex) : 0.0;
			handle.Snapshot.CursorTimeSeconds = cursorSeconds;

            var activeSeries = GetActiveSeriesData().ToList();
            UpdateSnapshotSeriesValues(handle.Snapshot, activeSeries, sampleIndex);
        }

		private void StartCursorSnapshotTimer()
		{
			if (!_cursorSnapshotTimer.IsEnabled)
			{
				_cursorSnapshotTimer.Start();
			}
		}

		private void StopCursorSnapshotTimer()
		{
			if (_cursorSnapshotTimer.IsEnabled)
			{
				_cursorSnapshotTimer.Stop();
			}
			_cursorSnapshotDirty = false;
		}

		private void MarkCursorSnapshotDirty()
		{
			_cursorSnapshotDirty = true;
			StartCursorSnapshotTimer();
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

			var interval = Math.Max(DataSet.TimeInterval, 0.001f);
			var seconds = axisValue;
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

		private static string FormatBrush(Brush? brush)
		{
			if (brush == null)
			{
				return "<null>";
			}

			if (brush is SolidColorBrush solid)
			{
				return $"Solid #{solid.Color.A:X2}{solid.Color.R:X2}{solid.Color.G:X2}{solid.Color.B:X2}";
			}

			if (brush is GradientBrush gradient && gradient.GradientStops != null && gradient.GradientStops.Count > 0)
			{
				var c = gradient.GradientStops[0].Color;
				return $"{brush.GetType().Name} firstStop=#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
			}

			return brush.GetType().Name;
		}

		private void LogSeriesAxisDebug(string message)
		{
			if (!EnableSeriesAxisDebugLogging)
			{
				return;
			}

			PerformanceLogger.Instance.LogInfo(message, "SeriesAxis_Debug");
			Console.WriteLine($"[SeriesAxisDebug] {message}");
		}

		private void DumpChartSeriesBrushes(ChartSeries chartSeries, SeriesData seriesData)
		{
			if (!EnableSeriesAxisDebugLogging)
			{
				return;
			}

			if (chartSeries == null)
			{
				return;
			}

			static object? TryGetPropertyValue(object target, string propertyName)
			{
				try
				{
					var prop = target.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
					return prop?.GetValue(target);
				}
				catch
				{
					return null;
				}
			}

			var candidates = new[]
			{
				"Stroke",
				"Fill",
				"Interior",
				"SegmentColorPath",
				"LegendIcon",
				"LegendIconTemplate",
				"Brush",
				"Foreground",
				"Background"
			};

			var parts = new List<string>();
			foreach (var name in candidates)
			{
				var value = TryGetPropertyValue(chartSeries, name);
				if (value is Brush brush)
				{
					parts.Add($"{name}={FormatBrush(brush)}");
				}
				else if (value != null)
				{
					// keep non-brush values concise
					parts.Add($"{name}={value.GetType().Name}");
				}
			}

			LogSeriesAxisDebug($"SeriesBrushDump: series='{seriesData?.Name}', type='{chartSeries.GetType().Name}', label='{chartSeries.Label}', tagSeries='{(chartSeries.Tag as SeriesData)?.Name}' | {string.Join(", ", parts)}");
		}

		private void ApplySeriesBrush(ChartSeries chartSeries, SolidColorBrush brush)
		{
			if (chartSeries == null)
			{
				return;
			}

			// Explicitly set the known stroke properties.
			if (chartSeries is FastLineBitmapSeries fastBitmapSeries)
			{
				fastBitmapSeries.Stroke = brush;
			}
			else if (chartSeries is StepLineSeries stepLineSeries)
			{
				stepLineSeries.Stroke = brush;
			}

			// Some themes/palettes may use other brush properties. Best-effort set via reflection.
			foreach (var propertyName in new[] { "Stroke", "Fill", "Interior", "Brush", "Foreground" })
			{
				try
				{
					var prop = chartSeries.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
					if (prop == null || !prop.CanWrite)
					{
						continue;
					}

					if (!typeof(Brush).IsAssignableFrom(prop.PropertyType))
					{
						continue;
					}

					prop.SetValue(chartSeries, brush);
				}
				catch
				{
					// ignore: not all series expose all properties
				}
			}
		}

		private void DumpAxesDebug(string reason)
		{
			if (!EnableSeriesAxisDebugLogging)
			{
				return;
			}

			if (ChartControl == null)
			{
				LogSeriesAxisDebug($"AxesDump({reason}): ChartControl=<null>");
				return;
			}

			var axes = ChartControl.Axes;
			var count = axes?.Count ?? 0;
			LogSeriesAxisDebug($"AxesDump({reason}): axesCount={count}, primary='{ChartControl.PrimaryAxis?.Header}', secondary='{ChartControl.SecondaryAxis?.Header}'");

			if (axes == null)
			{
				return;
			}

			for (int i = 0; i < axes.Count; i++)
			{
				var axis = axes[i] as ChartAxis;
				if (axis == null)
				{
					LogSeriesAxisDebug($"AxesDump[{i}]: <non-ChartAxis> type='{axes[i]?.GetType().Name}'");
					continue;
				}

				var header = axis.Header?.ToString() ?? string.Empty;
				Brush? fg = null;
				Brush? labelFg = null;
				Brush? headerFg = null;
				try { fg = axis.Foreground; } catch { }
				try { labelFg = axis.LabelStyle?.Foreground; } catch { }
				try { headerFg = axis.HeaderStyle?.Foreground; } catch { }

				LogSeriesAxisDebug($"AxesDump[{i}]: type='{axis.GetType().Name}', header='{header}', fg={FormatBrush(fg)}, labelFg={FormatBrush(labelFg)}, headerFg={FormatBrush(headerFg)}, opposed={axis.OpposedPosition}");
			}
		}

        private void ClearCursorHandles()
        {
			if (ChartControl != null)
			{
				foreach (var handle in _cursorHandles)
				{
					DetachCursorAnnotation(handle);
					DetachOverlayLine(handle);
					DetachOverlayCaps(handle);
					if (handle.Annotation != null)
					{
						ChartControl.Annotations?.Remove(handle.Annotation);
					}
				}

				foreach (var axis in _seriesAxes.Values)
				{
					ChartControl.Axes.Remove(axis);
				}
				_seriesAxes.Clear();
				UpdateDefaultValueAxisVisibility();
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
		public System.Windows.Shapes.Line? OverlayLine { get; set; }
		public System.Windows.Shapes.Ellipse? OverlayTopCap { get; set; }
		public System.Windows.Shapes.Ellipse? OverlayBottomCap { get; set; }
		public double Index { get; set; }
		public CursorSnapshotViewModel? Snapshot { get; set; }
		public Brush CursorBrush { get; set; } = Brushes.Crimson;
		public double LastLoggedAxisValue { get; set; } = double.NaN;
		public double LastLoggedIndex { get; set; } = double.NaN;
		public double LastLoggedPointerX { get; set; } = double.NaN;
		public DateTime LastLogTimestamp { get; set; } = DateTime.MinValue;
		public bool IsDragging { get; set; }
	}

		private System.Windows.Shapes.Line? CreateCursorOverlayLine(Brush stroke)
		{
			if (_cursorOverlay == null)
			{
				return null;
			}

			var line = new System.Windows.Shapes.Line
			{
				Stroke = stroke,
				StrokeThickness = 3.5,
				StrokeDashArray = new DoubleCollection { 6, 4 },
				StrokeStartLineCap = PenLineCap.Round,
				StrokeEndLineCap = PenLineCap.Round,
				StrokeDashCap = PenLineCap.Round,
				SnapsToDevicePixels = true
			};

			_cursorOverlay.Children.Add(line);
			return line;
		}

		private (System.Windows.Shapes.Ellipse top, System.Windows.Shapes.Ellipse bottom)? CreateCursorOverlayCaps(Brush stroke)
		{
			if (_cursorOverlay == null)
			{
				return null;
			}

			System.Windows.Shapes.Ellipse CreateCap()
			{
				return new System.Windows.Shapes.Ellipse
				{
					Width = 8,
					Height = 8,
					Fill = stroke,
					Stroke = stroke,
					StrokeThickness = 0,
					SnapsToDevicePixels = true
				};
			}

			var top = CreateCap();
			var bottom = CreateCap();
			_cursorOverlay.Children.Add(top);
			_cursorOverlay.Children.Add(bottom);
			return (top, bottom);
		}

		private void DetachOverlayLine(ChartCursorHandle handle)
		{
			if (handle.OverlayLine == null || _cursorOverlay == null)
			{
				return;
			}

			_cursorOverlay.Children.Remove(handle.OverlayLine);
			handle.OverlayLine = null;
		}

		private void DetachOverlayCaps(ChartCursorHandle handle)
		{
			if (_cursorOverlay == null)
			{
				return;
			}

			if (handle.OverlayTopCap != null)
			{
				_cursorOverlay.Children.Remove(handle.OverlayTopCap);
				handle.OverlayTopCap = null;
			}
			if (handle.OverlayBottomCap != null)
			{
				_cursorOverlay.Children.Remove(handle.OverlayBottomCap);
				handle.OverlayBottomCap = null;
			}
		}

		private void UpdateOverlayLine(ChartCursorHandle handle)
		{
			if (ChartControl == null || _cursorOverlay == null)
			{
				return;
			}

			var line = handle.OverlayLine;
			if (line == null)
			{
				return;
			}

			if (handle.Annotation == null)
			{
				line.Visibility = Visibility.Collapsed;
				return;
			}

			var cursorTime = handle.Annotation.X1;
			var plotX = GetCursorPixelX(cursorTime);
			if (plotX == null)
			{
				line.Visibility = Visibility.Collapsed;
				return;
			}

			if (!EnsureOverlayTransformCache())
			{
				line.Visibility = Visibility.Collapsed;
				return;
			}

			var chartToOverlay = plotX.Value + _overlayOffsetX;
			if (double.IsNaN(chartToOverlay) || double.IsInfinity(chartToOverlay))
			{
				line.Visibility = Visibility.Collapsed;
				return;
			}

			var height = _cursorOverlay.ActualHeight;
			if (height <= 0)
			{
				height = ChartControl.ActualHeight;
			}

			line.Visibility = Visibility.Visible;
			line.Stroke = handle.CursorBrush;
			line.StrokeThickness = 3.5;
			line.StrokeDashArray = new DoubleCollection { 6, 4 };
			line.StrokeStartLineCap = PenLineCap.Round;
			line.StrokeEndLineCap = PenLineCap.Round;
			line.StrokeDashCap = PenLineCap.Round;
			line.X1 = chartToOverlay;
			line.X2 = chartToOverlay;
			line.Y1 = 0;
			line.Y2 = Math.Max(0, height);

			if (handle.OverlayTopCap == null || handle.OverlayBottomCap == null)
			{
				var caps = CreateCursorOverlayCaps(handle.CursorBrush);
				if (caps.HasValue)
				{
					handle.OverlayTopCap = caps.Value.top;
					handle.OverlayBottomCap = caps.Value.bottom;
				}
			}

			if (handle.OverlayTopCap != null && handle.OverlayBottomCap != null)
			{
				var capSize = handle.OverlayTopCap.Width;
				handle.OverlayTopCap.Fill = handle.CursorBrush;
				handle.OverlayBottomCap.Fill = handle.CursorBrush;
				handle.OverlayTopCap.Visibility = Visibility.Visible;
				handle.OverlayBottomCap.Visibility = Visibility.Visible;
				Canvas.SetLeft(handle.OverlayTopCap, chartToOverlay - capSize / 2);
				Canvas.SetTop(handle.OverlayTopCap, 0 - capSize / 2);
				Canvas.SetLeft(handle.OverlayBottomCap, chartToOverlay - capSize / 2);
				Canvas.SetTop(handle.OverlayBottomCap, Math.Max(0, height) - capSize / 2);
			}
		}

		private bool EnsureOverlayTransformCache()
		{
			if (ChartControl == null || _cursorOverlay == null)
			{
				return false;
			}

			if (!_overlayTransformDirty)
			{
				return true;
			}

			try
			{
				var origin = ChartControl.TranslatePoint(new Point(0, 0), _cursorOverlay);
				_overlayOffsetX = origin.X;
				_overlayOffsetY = origin.Y;
				_overlayTransformDirty = false;
				return true;
			}
			catch
			{
				return false;
			}
		}

		private double? GetCursorPixelX(object? axisValue)
		{
			if (ChartControl == null)
			{
				return null;
			}

			var visible = ChartControl.PrimaryAxis?.VisibleRange;
			var start = visible?.Start ?? double.NaN;
			var end = visible?.End ?? double.NaN;
			if (double.IsNaN(start) || double.IsNaN(end) || Math.Abs(end - start) < 1e-12)
			{
				return null;
			}

			double value;
			switch (axisValue)
			{
				case DateTime dt:
					value = dt.ToOADate();
					break;
				case double d:
					value = d;
					break;
				case IConvertible conv:
					value = conv.ToDouble(CultureInfo.InvariantCulture);
					break;
				default:
					return null;
			}

			var normalized = (value - start) / (end - start);
			if (double.IsNaN(normalized) || double.IsInfinity(normalized))
			{
				return null;
			}
			normalized = Math.Clamp(normalized, 0, 1);

			try
			{
				var clip = ChartControl.SeriesClipRect;
				if (clip.Width > 0)
				{
					return clip.X + normalized * clip.Width;
				}
			}
			catch
			{
				// ignore and fall back
			}

			return normalized * ChartControl.ActualWidth;
		}

        public sealed class CursorSnapshotViewModel : INotifyPropertyChanged
        {
            private int _cursorIndex;
			private double _cursorTimeSeconds;
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

			public double CursorTimeSeconds
            {
				get => _cursorTimeSeconds;
                set
                {
					if (Math.Abs(_cursorTimeSeconds - value) > 0.0000001)
                    {
						_cursorTimeSeconds = value;
                        OnPropertyChanged();
                        OnPropertyChanged(nameof(CursorTimeDisplay));
                    }
                }
            }

            public string CursorLabel => $"Cursor {_cursorIndex}";
			public string CursorTimeDisplay => _cursorTimeSeconds <= 0 ? "0.000 s" : $"{_cursorTimeSeconds:0.###} s";
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

        private void ExportDataTable_Click(object sender, RoutedEventArgs e)
        {
            if (_dataTableBackingStore == null || _dataTableBackingStore.Rows.Count == 0)
            {
                MessageBox.Show("내보낼 데이터가 없습니다.", "엑셀 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveDialog = new SaveFileDialog
            {
                Filter = "Excel 통합 문서 (*.xlsx)|*.xlsx",
                FileName = string.IsNullOrWhiteSpace(WindowTitle)
                    ? "ChartData"
                    : $"{WindowTitle.Replace(' ', '_')}"
            };

            if (saveDialog.ShowDialog() != true)
            {
                return;
            }

            try
            {
                ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
                using var package = new ExcelPackage();
                var worksheet = package.Workbook.Worksheets.Add("Data");
                worksheet.Cells["A1"].LoadFromDataTable(_dataTableBackingStore, true, TableStyles.Medium6);
                worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                package.SaveAs(new FileInfo(saveDialog.FileName));

                MessageBox.Show("데이터가 Excel 파일로 저장되었습니다.", "엑셀 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다: {ex.Message}", "엑셀 내보내기", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

		private void PreviousPage_Click(object sender, RoutedEventArgs e)
		{
			if (_currentPageIndex <= 0)
			{
				return;
			}

			LoadPage(_currentPageIndex - 1);
		}

		private void NextPage_Click(object sender, RoutedEventArgs e)
		{
			if (TotalPageCount == 0 || _currentPageIndex >= TotalPageCount - 1)
			{
				return;
			}

			LoadPage(_currentPageIndex + 1);
		}

		private void GoToPage_Click(object sender, RoutedEventArgs e)
		{
			if (TotalPageCount == 0 || PageNumberTextBox == null)
			{
				return;
			}

			if (!int.TryParse(PageNumberTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var requestedPage))
			{
				MessageBox.Show("Enter a valid page number.", "Paging", MessageBoxButton.OK, MessageBoxImage.Information);
				UpdatePageNumberTextBox();
				return;
			}

			requestedPage = Math.Clamp(requestedPage, 1, TotalPageCount);
			LoadPage(requestedPage - 1);
		}

        #endregion

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}