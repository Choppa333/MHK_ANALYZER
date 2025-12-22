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
using Microsoft.Win32;
using System.IO;
using MGK_Analyzer.Models;
using MGK_Analyzer.Services;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Charts;
using OfficeOpenXml;
using OfficeOpenXml.Table;

namespace MGK_Analyzer.Controls
{
        public partial class MdiNTCurveChartWindow : UserControl, INotifyPropertyChanged
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
		// TODO(cursor): Not fully resolved. Remaining mismatch occurs when X-axis values are not uniformly spaced.
		// We snap `handle.Index` to the nearest sample (for snapshot values), but the sample's X-value may not match
		// the pointer-derived axis value; this can produce apparent pixel drift when the cursor is re-positioned from
		// the sample axis value.
		// Mode A below keeps the cursor visually at the pointer pixel on release to reduce user-visible jumping.
		// Next options:
		// - Mode B: always map cursor pixel from sample's X (strict snap, accept drift)
		// - Interpolated cursor: keep pixel and compute interpolated values instead of snapping index
		// - Improve `TryConvertAxisValueToIndex` (binary search / monotonic assumptions) if axis values are monotonic
		private const bool KeepCursorPixelOnRelease = true;
		private const string PrimaryAxisName = "NtCurvePrimaryAxis";
		private const string SecondaryAxisName = "NtCurveSecondaryAxis";
		private const bool EnableAnnotationDebugLogging = false;
		private bool _isRefreshingCursorHandles;
		private bool _pendingSnapshotRefresh;
		private bool _isUpdatingSeriesSelection;
		private bool _isUpdatingAxisSelection;
            private static readonly Brush[] CursorColorPalette =
            {
                Brushes.Crimson,
                Brushes.DodgerBlue,
                Brushes.MediumSeaGreen,
                Brushes.Goldenrod,
                Brushes.MediumOrchid
            };
		private readonly Dictionary<string, NumericalAxis> _unitAxes = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, HashSet<string>> _unitAxisSeries = new(StringComparer.OrdinalIgnoreCase);
		private NumericalAxis? _defaultSecondaryAxis;
		private Canvas? _cursorOverlay;
		private (double min, double max)? _cachedDataValueRange;
		private SeriesData? _selectedXAxisSeries;
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
            DependencyProperty.Register("WindowTitle", typeof(string), typeof(MdiNTCurveChartWindow), 
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
                InitializeAxisOptions();
                ResetChartForAxisChange();
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
        public ObservableCollection<AxisSeriesOption> AxisSeriesOptions { get; } = new();
        public ObservableCollection<CursorSnapshotViewModel> SnapshotList { get; } = new();

        public SeriesData? SelectedXAxisSeries
        {
            get => _selectedXAxisSeries;
            private set
            {
                if (!ReferenceEquals(_selectedXAxisSeries, value))
                {
                    _selectedXAxisSeries = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(IsXAxisSelected));
                    UpdatePrimaryAxisHeader();
                    UpdateSeriesSelectability();
                }
            }
        }

        public bool IsXAxisSelected => SelectedXAxisSeries != null;

        public event EventHandler? WindowClosed;
        public event EventHandler? WindowActivated;

        public MdiNTCurveChartWindow()
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

			InitializeChartAxes();
			UpdatePrimaryAxisHeader();

			ChartControl.SizeChanged += (_, __) => RefreshCursorHandles();
			ChartControl.Loaded += (_, __) =>
			{
				_cursorOverlay ??= FindName("CursorOverlay") as Canvas;
				RefreshCursorHandles();
			};
			if (_cursorOverlay != null)
			{
				_cursorOverlay.SizeChanged += (_, __) => RefreshCursorHandles();
				_cursorOverlay.LayoutUpdated += (_, __) => RefreshCursorHandles();
			}
            Loaded += (_, __) => InitializeLeftPanelSizing();
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

		private void InitializeChartAxes()
		{
			if (ChartControl == null)
			{
				return;
			}

			NumericalAxis primaryAxis;
			if (ChartControl.PrimaryAxis is NumericalAxis existingPrimary)
			{
				primaryAxis = existingPrimary;
				primaryAxis.Header = "Select X Axis";
				primaryAxis.LabelFormat = "0.###";
			}
			else
			{
				primaryAxis = new NumericalAxis
				{
					Header = "Select X Axis",
					LabelFormat = "0.###"
				};
				ChartControl.PrimaryAxis = primaryAxis;
			}
			RegisterChartAxisName(primaryAxis, PrimaryAxisName);

			NumericalAxis secondaryAxis;
			if (ChartControl.SecondaryAxis is NumericalAxis existingSecondary)
			{
				secondaryAxis = existingSecondary;
			}
			else
			{
				secondaryAxis = new NumericalAxis { Header = "Value" };
				ChartControl.SecondaryAxis = secondaryAxis;
			}
			_defaultSecondaryAxis = secondaryAxis;
			RegisterChartAxisName(secondaryAxis, SecondaryAxisName);

			LogAnnotationDebug($"Axes initialized: PrimaryAxisType={ChartControl.PrimaryAxis?.GetType().Name ?? "<null>"}, SecondaryAxisType={ChartControl.SecondaryAxis?.GetType().Name ?? "<null>"}");
		}

		private void RegisterChartAxisName(ChartAxis? axis, string name)
		{
			if (ChartControl == null || axis == null || string.IsNullOrWhiteSpace(name))
			{
				LogAnnotationDebug($"RegisterChartAxisName skipped: axis={(axis == null ? "<null>" : axis.GetType().Name)}, name={name}");
				return;
			}

			try
			{
				ChartControl.UnregisterName(name);
			}
			catch
			{
				// ignore if name is not registered yet
			}

			try
			{
				ChartControl.RegisterName(name, axis);
				LogAnnotationDebug($"Axis name registered: {name} -> {axis.GetType().Name}");
			}
			catch
			{
				// ignore duplicate registrations
				LogAnnotationDebug($"Axis name registration failed (duplicate?): {name}");
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

        private void InitializeAxisOptions()
        {
            foreach (var option in AxisSeriesOptions)
            {
                option.PropertyChanged -= AxisOption_PropertyChanged;
            }

            AxisSeriesOptions.Clear();

            if (DataSet?.SeriesData == null || DataSet.SeriesData.Count == 0)
            {
                SelectedXAxisSeries = null;
                UpdateSeriesSelectability();
                return;
            }

            foreach (var series in DataSet.SeriesData.Values.Where(IsAxisCandidate))
            {
                var option = new AxisSeriesOption(series);
                option.PropertyChanged += AxisOption_PropertyChanged;
                AxisSeriesOptions.Add(option);
            }

            _isUpdatingAxisSelection = true;
            foreach (var option in AxisSeriesOptions)
            {
                option.IsSelected = false;
            }
            _isUpdatingAxisSelection = false;

            SelectedXAxisSeries = null;
			UpdateSeriesSelectability();
        }

        private static bool IsAxisCandidate(SeriesData? series)
        {
            return series != null && series.DataType != typeof(bool) && !IsTimestampSeries(series.Name);
        }

        private void AxisOption_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AxisSeriesOption.IsSelected) || sender is not AxisSeriesOption option)
            {
                return;
            }

            if (_isUpdatingAxisSelection)
            {
                return;
            }

            if (option.IsSelected)
            {
                _isUpdatingAxisSelection = true;
                foreach (var other in AxisSeriesOptions)
                {
                    if (!ReferenceEquals(other, option) && other.IsSelected)
                    {
                        other.IsSelected = false;
                    }
                }
                _isUpdatingAxisSelection = false;

                ApplyXAxisSelection(option.SeriesData);
            }
            else if (ReferenceEquals(SelectedXAxisSeries, option.SeriesData))
            {
                ApplyXAxisSelection(null);
            }
        }

        private void ApplyXAxisSelection(SeriesData? axisSeries)
        {
            var wasSelected = SelectedXAxisSeries;
            SelectedXAxisSeries = axisSeries;

            if (!ReferenceEquals(wasSelected, axisSeries) || axisSeries == null)
            {
                ResetChartForAxisChange();
            }
        }

        private void ResetChartForAxisChange()
        {
            ResetYAxisSelections();
			_cachedDataValueRange = null;
            _unitAxes.Clear();
			_unitAxisSeries.Clear();
            ChartControl.Axes.Clear();
            ChartControl.Series.Clear();
			RegisterChartAxisName(ChartControl.PrimaryAxis as ChartAxis, PrimaryAxisName);
            ClearCursorHandles();

            EnsureSecondaryAxis();
            RegisterChartAxisName(ChartControl.SecondaryAxis as ChartAxis, SecondaryAxisName);
            UpdatePrimaryAxisHeader();
            RefreshAllSnapshots();
        }

        private void ResetYAxisSelections()
        {
            if (SeriesList.Count == 0)
            {
                return;
            }

            foreach (var viewModel in SeriesList.ToList())
            {
                if (viewModel.IsSelected)
                {
                    SetSeriesSelection(viewModel, false);
                }
            }
        }

        private void UpdateSeriesSelectability()
        {
            var axisSeries = SelectedXAxisSeries;
            var canSelect = axisSeries != null;

            foreach (var viewModel in SeriesList)
            {
                var isSelectable = canSelect && !ReferenceEquals(viewModel.SeriesData, axisSeries);
                viewModel.IsSelectable = isSelectable;

                if (!isSelectable && viewModel.IsSelected)
                {
                    SetSeriesSelection(viewModel, false);
                }
            }
        }

        private void UpdatePrimaryAxisHeader()
        {
            if (ChartControl?.PrimaryAxis is not NumericalAxis axis)
            {
                return;
            }

            if (SelectedXAxisSeries == null)
            {
                axis.Header = "Select X Axis";
            }
            else
            {
                axis.Header = GetAxisHeaderText(SelectedXAxisSeries);
            }
        }

        private void EnsureSecondaryAxis()
        {
			if (ChartControl == null)
			{
				return;
			}

			// NOTE: In this project we add custom Y-axes via ChartControl.Axes.
			// Syncfusion's SfChart may leave SecondaryAxis null at runtime even if the XAML defines one.
			// Cursor annotations need a stable Y-axis reference, so ensure SecondaryAxis always points
			// to an existing NumericalAxis instance (and keep it registered by name).
			if (ChartControl.SecondaryAxis is NumericalAxis numericAxis)
			{
				_defaultSecondaryAxis = numericAxis;
				RegisterChartAxisName(_defaultSecondaryAxis, SecondaryAxisName);
				return;
			}

			// Reuse an existing default axis if present in the chart axes collection.
			if (_defaultSecondaryAxis != null)
			{
				ChartControl.SecondaryAxis = _defaultSecondaryAxis;
				RegisterChartAxisName(_defaultSecondaryAxis, SecondaryAxisName);
				return;
			}

			_defaultSecondaryAxis = new NumericalAxis { Header = "Value" };
			ChartControl.SecondaryAxis = _defaultSecondaryAxis;
			RegisterChartAxisName(_defaultSecondaryAxis, SecondaryAxisName);
        }

        private static string GetAxisHeaderText(SeriesData axisSeries)
        {
            if (string.IsNullOrWhiteSpace(axisSeries.Unit))
            {
                return axisSeries.Name;
            }

            return $"{axisSeries.Name} ({axisSeries.Unit})";
        }

        private double? GetAxisValueAt(int index)
        {
            if (SelectedXAxisSeries?.Values == null)
            {
                return null;
            }

            if (index < 0 || index >= SelectedXAxisSeries.Values.Length)
            {
                return null;
            }

            var raw = SelectedXAxisSeries.Values[index];
            if (float.IsNaN(raw) || float.IsInfinity(raw))
            {
                return null;
            }

            return raw;
        }

        private int GetSampleLimit(SeriesData series)
        {
            if (DataSet == null)
            {
                return 0;
            }

            var limit = DataSet.TotalSamples;

            if (SelectedXAxisSeries?.Values != null)
            {
                limit = Math.Min(limit, SelectedXAxisSeries.Values.Length);
            }

            if (series.DataType == typeof(bool))
            {
                if (series.BitValues != null)
                {
                    limit = Math.Min(limit, series.BitValues.Length);
                }
            }
            else if (series.Values != null)
            {
                limit = Math.Min(limit, series.Values.Length);
            }

            return Math.Max(0, limit);
        }

        #region 관리 패널 자동 숨김/고정 기능
        // ... (기존 코드 유지)
        #endregion

        private void InitializeSeriesList()
        {
            using var timer = PerformanceLogger.Instance.StartTimer("시리즈 리스트 초기화", "Chart_Display");
            
			foreach (var existing in SeriesList)
			{
				existing.PropertyChanged -= SeriesViewModel_PropertyChanged;
			}
			SeriesList.Clear();
            if (DataSet?.SeriesData == null) return;

            // Timestamp가 아닌 시리즈만 목록에 추가
            var initialSelection = new HashSet<string>(_initialSeriesSelection, StringComparer.OrdinalIgnoreCase);
            _initialSeriesSelection.Clear();
            foreach (var series in DataSet.SeriesData.Values.Where(s => !s.Name.Equals("Timestamp", StringComparison.OrdinalIgnoreCase)))
            {
                var viewModel = new ChartSeriesViewModel { SeriesData = series };
                viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
				_isUpdatingSeriesSelection = true;
				viewModel.IsSelected = initialSelection.Contains(series.Name);
				_isUpdatingSeriesSelection = false;
                SeriesList.Add(viewModel);
            }

			UpdateSeriesSelectability();
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

				if (!IsXAxisSelected || ReferenceEquals(viewModel.SeriesData, SelectedXAxisSeries))
				{
					if (viewModel.IsSelected)
					{
						SetSeriesSelection(viewModel, false);
					}
					return;
				}
                
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

					try
					{
						using var timer = PerformanceLogger.Instance.StartTimer($"시리즈 추가: {seriesName}", "Chart_Display");
						Console.WriteLine($"시리즈 추가 작업 시작: {seriesName}");
						await AddSeriesToChart(viewModel.SeriesData);
					}
					catch (Exception addEx)
					{
						PerformanceLogger.Instance.LogError($"시리즈 추가 중 오류: {seriesName} - {addEx.Message}", "Chart_Display");
						SetSeriesSelection(viewModel, false);
					}
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
			if (DataSet == null || ChartControl == null || !IsXAxisSelected)
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
			List<NtChartDataPoint> dataPoints;

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
				dynSeries.XBindingPath = "X";
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

		private List<NtChartDataPoint> CreateBoolDataPoints(SeriesData series, int step, int maxPoints)
		{
			using var timer = PerformanceLogger.Instance.StartTimer($"Bool 데이터 포인트 생성: {series.Name}", "Chart_Display");
			var dataPoints = new List<NtChartDataPoint>();
			var pointCount = 0;
			var limit = GetSampleLimit(series);

			for (int i = 0; i < limit && pointCount < maxPoints; i += step)
			{
				var axisValue = GetAxisValueAt(i);
				if (!axisValue.HasValue)
				{
					continue;
				}

				bool value = GetBoolValue(series.BitValues, i);
				dataPoints.Add(new NtChartDataPoint(axisValue.Value, value ? 1.0 : 0.0)
				{
					SeriesName = series.Name
				});
				pointCount++;
			}

			PerformanceLogger.Instance.LogInfo($"Bool 데이터 포인트 생성 완료: {pointCount:N0}개", "Chart_Display");
			return dataPoints;
		}

		private List<NtChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step, int maxPointsToCreate)
		{
			using var timer = PerformanceLogger.Instance.StartTimer($"Double 데이터 포인트 생성: {series.Name}", "Chart_Display");
			var dataPoints = new List<NtChartDataPoint>();
			var pointCount = 0;
			if (series.Values == null || series.Values.Length == 0)
			{
				return dataPoints;
			}

			var maxSafeIndex = Math.Min(GetSampleLimit(series), series.Values.Length);
			for (int i = 0; i < maxSafeIndex && pointCount < maxPointsToCreate; i += step)
			{
				var axisValue = GetAxisValueAt(i);
				if (!axisValue.HasValue)
				{
					continue;
				}

				var value = series.Values[i];
				if (float.IsNaN(value) || float.IsInfinity(value))
				{
					continue;
				}

				dataPoints.Add(new NtChartDataPoint(axisValue.Value, value)
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
				ReleaseAxisForSeries(series);
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

            var unitKey = string.IsNullOrWhiteSpace(series.Unit) ? "Default" : series.Unit.Trim();
            if (unitKey.Equals("Default", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(series.Unit))
            {
                return _defaultSecondaryAxis ?? (NumericalAxis)(ChartControl.SecondaryAxis ?? new NumericalAxis { Header = "Value" });
            }

			if (!_unitAxisSeries.TryGetValue(unitKey, out var seriesSet))
			{
				seriesSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
				_unitAxisSeries[unitKey] = seriesSet;
			}
			if (!string.IsNullOrWhiteSpace(series.Name))
			{
				seriesSet.Add(series.Name);
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

		private void ReleaseAxisForSeries(SeriesData series)
		{
			if (ChartControl == null || series == null)
			{
				return;
			}

			var unitKey = string.IsNullOrWhiteSpace(series.Unit) ? "Default" : series.Unit.Trim();
			if (unitKey.Equals("Default", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(series.Unit))
			{
				return;
			}

			if (!_unitAxisSeries.TryGetValue(unitKey, out var seriesSet))
			{
				return;
			}

			if (!string.IsNullOrWhiteSpace(series.Name))
			{
				seriesSet.Remove(series.Name);
			}

			if (seriesSet.Count > 0)
			{
				return;
			}

			_unitAxisSeries.Remove(unitKey);
			if (_unitAxes.TryGetValue(unitKey, out var axis))
			{
				ChartControl.Axes.Remove(axis);
				_unitAxes.Remove(unitKey);
			}
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
			PerformanceLogger.Instance.LogInfo("커서 추가 요청이 발생했습니다.", "Chart_Display");
			if (DataSet == null || ChartControl == null)
			{
				PerformanceLogger.Instance.LogError("커서 추가 실패: DataSet 또는 ChartControl이 null 입니다.", "Chart_Display");
				return;
			}

			EnsureSecondaryAxis();

			LogAnnotationDebug($"AddCursor: SeriesCount={ChartControl.Series?.Count ?? 0}, AnnotationsCount={ChartControl.Annotations?.Count ?? -1}, PrimaryAxis={ChartControl.PrimaryAxis?.GetType().Name ?? "<null>"}, SecondaryAxis={ChartControl.SecondaryAxis?.GetType().Name ?? "<null>"}");
			LogAnnotationDebug($"AddCursor: PrimaryAxisName='{PrimaryAxisName}', SecondaryAxisName='{SecondaryAxisName}'");

			if (ChartControl.PrimaryAxis == null || ChartControl.SecondaryAxis == null)
			{
				PerformanceLogger.Instance.LogError($"커서 추가 실패: 차트 축이 준비되지 않았습니다. PrimaryAxis={(ChartControl.PrimaryAxis == null ? "null" : ChartControl.PrimaryAxis.GetType().Name)}, SecondaryAxis={(ChartControl.SecondaryAxis == null ? "null" : ChartControl.SecondaryAxis.GetType().Name)}", "Chart_Display");
				LogAnnotationDebug("AddCursor aborted: axis not ready (null).");
				return;
			}

			// Syncfusion annotation rendering can throw NRE if the axis names cannot be resolved at the moment of add.
			// Re-register names right before adding to ensure template name-scope resolution is valid.
			RegisterChartAxisName(ChartControl.PrimaryAxis as ChartAxis, PrimaryAxisName);
			RegisterChartAxisName(ChartControl.SecondaryAxis as ChartAxis, SecondaryAxisName);

			if (ChartControl.ActualWidth <= 0 || ChartControl.ActualHeight <= 0)
			{
				LogAnnotationDebug($"AddCursor aborted: ChartControl not measured yet. ActualWidth={ChartControl.ActualWidth}, ActualHeight={ChartControl.ActualHeight}");
				return;
			}

			if (!IsXAxisSelected)
			{
				PerformanceLogger.Instance.LogError("커서 추가 실패: X 축 시리즈가 선택되지 않았습니다.", "Chart_Display");
				return;
			}
            const int MaxCursorCount = 5;
            if (_cursorHandles.Count >= MaxCursorCount)
            {
                PerformanceLogger.Instance.LogInfo("커서는 최대 5개까지 추가할 수 있습니다.", "Chart_Display");
                return;
            }

			var cursorIndex = GetNextCursorIndex();
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

            var cursorBrush = GetCursorColorByIndex(_cursorHandles.Count);

			var annotation = new VerticalLineAnnotation
            {
				X1 = 0,
				X2 = 0,
                Y1 = minValue,
                Y2 = maxValue,
                Stroke = cursorBrush,
				StrokeThickness = 3.5,
				CanDrag = true,
				CanResize = false,
				DraggingMode = AxisMode.Horizontal,
				CoordinateUnit = CoordinateUnit.Pixel,
				XAxisName = PrimaryAxisName,
				YAxisName = SecondaryAxisName
            };

			// Syncfusion VerticalLineAnnotation does not reliably render dash/round caps in this build.
			// We'll draw the visible cursor as a WPF Line on an overlay Canvas.
			annotation.Stroke = Brushes.Transparent;

			// Note: this Syncfusion build does not expose XAxis/YAxis on VerticalLineAnnotation.
			// We rely on XAxisName/YAxisName and must keep those names registered.

			if (ChartControl.Annotations == null)
			{
				PerformanceLogger.Instance.LogError("차트 어노테이션 컬렉션이 null 입니다. 커서를 추가할 수 없습니다.", "Chart_Display");
				return;
			}

			try
			{
				ChartControl.Annotations.Add(annotation);
				PerformanceLogger.Instance.LogInfo("커서 어노테이션이 성공적으로 추가되었습니다.", "Chart_Display");
				LogAnnotationDebug($"AddCursor: annotation added. X1={annotation.X1}, Y1={annotation.Y1}, Y2={annotation.Y2}, AxisMode={annotation.CoordinateUnit}, XAxisName={annotation.XAxisName}, YAxisName={annotation.YAxisName}");
			}
			catch (NullReferenceException nre)
			{
				// Syncfusion may throw this when axis resolution fails during annotation add.
				PerformanceLogger.Instance.LogError($"커서 어노테이션 추가 중 NullReferenceException: {nre.Message}", "Chart_Display");
				LogAnnotationDebug($"AddCursor: NullReferenceException while adding annotation. PrimaryAxis={ChartControl.PrimaryAxis?.GetType().Name ?? "<null>"}, SecondaryAxis={ChartControl.SecondaryAxis?.GetType().Name ?? "<null>"}, XAxisName={annotation.XAxisName}, YAxisName={annotation.YAxisName}");
				LogAnnotationDebug($"AddCursor: NRE stack: {nre}");
				return;
			}
			catch (Exception ex)
			{
				PerformanceLogger.Instance.LogError($"커서 어노테이션 추가 실패: {ex.Message}", "Chart_Display");
				LogAnnotationDebug($"AddCursor: exception while adding annotation: {ex}");
				return;
			}

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
			annotation.Tag = handle;
			annotation.DragDelta += CursorAnnotation_DragDelta;
			annotation.DragCompleted += CursorAnnotation_DragCompleted;
			annotation.MouseLeftButtonDown += CursorAnnotation_MouseLeftButtonDown;
			annotation.MouseLeftButtonUp += CursorAnnotation_MouseLeftButtonUp;
            _cursorHandles.Add(handle);
            UpdateSnapshotForHandle(handle);
            RefreshSnapshotOrdering();

			UpdateCursorAnnotation(handle);
			RefreshCursorHandles();
        }

		private static void TryApplyCursorLineStyle(VerticalLineAnnotation annotation)
		{
			// Some versions of Syncfusion's VerticalLineAnnotation expose WPF-like StrokeDashArray/LineCap
			// APIs, but others do not. Use reflection to apply styles opportunistically.
			if (annotation == null)
			{
				return;
			}

			try
			{
				var type = annotation.GetType();

				// Dashed line
				var dashProp = type.GetProperty("StrokeDashArray");
				if (dashProp?.CanWrite == true)
				{
					var dashArray = new DoubleCollection { 6, 4 };
					dashProp.SetValue(annotation, dashArray);
				}

				// Rounded caps (try common property names). Value type can be PenLineCap or a Syncfusion enum.
				TrySetLineCap(type, annotation, "StrokeStartLineCap", PenLineCap.Round);
				TrySetLineCap(type, annotation, "StrokeEndLineCap", PenLineCap.Round);
				TrySetLineCap(type, annotation, "StrokeDashCap", PenLineCap.Round);
			}
			catch
			{
				// best effort - ignore if API not supported
			}
		}

		private static void TrySetLineCap(Type annotationType, VerticalLineAnnotation annotation, string propertyName, PenLineCap desiredCap)
		{
			var prop = annotationType.GetProperty(propertyName);
			if (prop?.CanWrite != true)
			{
				return;
			}

			var propType = prop.PropertyType;
			try
			{
				if (propType == typeof(PenLineCap))
				{
					prop.SetValue(annotation, desiredCap);
					return;
				}

				if (propType.IsEnum)
				{
					var enumValue = Enum.Parse(propType, desiredCap.ToString(), ignoreCase: true);
					prop.SetValue(annotation, enumValue);
				}
			}
			catch
			{
				// best effort - ignore if conversion fails
			}
		}

        private void RemoveCursor_Click(object sender, RoutedEventArgs e)
        {
			if (_cursorHandles.Count == 0 || ChartControl == null) return;

            var lastCursor = _cursorHandles[^1];
            _cursorHandles.RemoveAt(_cursorHandles.Count - 1);
			DetachCursorAnnotation(lastCursor);
			ChartControl.Annotations?.Remove(lastCursor.Annotation);
			DetachOverlayLine(lastCursor);
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

			// In pixel coordinate mode, Syncfusion reports pixel positions in X1.
			// Convert pixel -> axis value using the current visible range, then map to nearest sample index.
			double axisValue;
			var pointer = ChartControl != null ? Mouse.GetPosition(ChartControl) : new Point(double.NaN, double.NaN);
			if (ChartControl == null || ChartControl.ActualWidth <= 0)
			{
				return;
			}

			var newX1Obj = e.NewValue.X1;
			if (newX1Obj is not double xPixel || double.IsNaN(xPixel) || double.IsInfinity(xPixel))
			{
				LogCursorDebug("Annotation drag delta skipped: pixel X1 is invalid.");
				return;
			}

			// Prefer the actual pointer position for higher fidelity (resolution can differ from annotation X1).
			if (!double.IsNaN(pointer.X) && !double.IsInfinity(pointer.X))
			{
				xPixel = pointer.X;
			}

			// clamp to chart bounds
			xPixel = Math.Clamp(xPixel, 0, ChartControl.ActualWidth);
			handle.DragPixelX = xPixel;

			var visible = ChartControl.PrimaryAxis?.VisibleRange;
			var start = visible?.Start ?? 0d;
			var end = visible?.End ?? 1d;
			if (Math.Abs(end - start) < 1e-12)
			{
				end = start + 1;
			}

			double normalized;
			if (TryGetSeriesClipRect(out var clipRect) && clipRect.Width > 0)
			{
				// Map pointer X into plot area coordinates.
				var plotX = Math.Clamp(xPixel - clipRect.X, 0, clipRect.Width);
				normalized = plotX / clipRect.Width;
			}
			else
			{
				normalized = xPixel / ChartControl.ActualWidth;
			}
			axisValue = start + normalized * (end - start);
			axisValue = Math.Clamp(axisValue, Math.Min(start, end), Math.Max(start, end));

			if (!TryConvertAxisValueToIndex(axisValue, out var targetIndex))
			{
				e.Cancel = true;
				LogCursorDebug("Annotation drag delta cancelled: unable to convert axis value to index.");
				return;
			}

			handle.Index = targetIndex;
			UpdateCursorAnnotation(handle);

			var indexError = 0d;
			var pointerX = pointer.X;
			var shouldLog = ShouldLogCursorDelta(handle, axisValue, pointerX, handle.Index, indexError, forceLog: false, out var triggerReason);
			if (shouldLog)
			{
				var message = $"Drag snapshot[{triggerReason}] cursor={handle.Index:F2}, axisValue={axisValue:F5}, pointerX={(double.IsNaN(pointerX) ? double.NaN : pointerX):F2}";
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
			handle.DragEndPointerX = handle.DragPixelX ?? double.NaN;
			if (KeepCursorPixelOnRelease && !double.IsNaN(handle.DragEndPointerX) && ChartControl != null)
			{
				// TODO(cursor): Mode A trade-off. The visual cursor stays at pointer pixel, while the selected sample/index
				// may represent a nearby axis value. This can make the displayed line and snapshot value slightly inconsistent.
				handle.ReleasedPixelX = Math.Clamp(handle.DragEndPointerX, 0, ChartControl.ActualWidth);
			}
			handle.DragEndAnnotationX = annotation.X1 is double annX ? annX : double.NaN;
			var visible = ChartControl?.PrimaryAxis?.VisibleRange;
			var visStart = visible?.Start ?? double.NaN;
			var visEnd = visible?.End ?? double.NaN;
			double normalized;
			if (!double.IsNaN(handle.DragEndPointerX) && TryGetSeriesClipRect(out var clipRect) && clipRect.Width > 0)
			{
				var plotX = Math.Clamp(handle.DragEndPointerX - clipRect.X, 0, clipRect.Width);
				normalized = plotX / clipRect.Width;
			}
			else
			{
				var chartWidth = ChartControl?.ActualWidth ?? double.NaN;
				normalized = (!double.IsNaN(handle.DragEndPointerX) && chartWidth > 0)
					? Math.Clamp(handle.DragEndPointerX / chartWidth, 0, 1)
					: double.NaN;
			}
			handle.DragEndAxisValue = (!double.IsNaN(normalized) && !double.IsNaN(visStart) && !double.IsNaN(visEnd))
				? (visStart + normalized * (visEnd - visStart))
				: double.NaN;
			handle.DragEndSampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet?.TotalSamples - 1 ?? 0));
			handle.DragPixelX = null;
			LogCursorDebug($"Annotation drag completed for cursor index={handle.Index:F2}");
			UpdateCursorAnnotation(handle);

			var finalSample = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet?.TotalSamples - 1 ?? 0));
			var finalAxis = GetAxisValueAt(finalSample) ?? double.NaN;
			var finalAnnX = handle.Annotation?.X1 is double finalX ? finalX : double.NaN;
			handle.DragFinalSampleIndex = finalSample;
			handle.DragFinalAxisValue = finalAxis;
			handle.DragFinalAnnotationX = finalAnnX;

			LogCursorEvent(
				$"DragEnd#{handle.DragSessionId} ptrX={handle.DragEndPointerX:0.##} annX={handle.DragEndAnnotationX:0.##} " +
				$"axisAtPtr={handle.DragEndAxisValue:0.#####} handleIdx={handle.Index:0.###} sampleEnd={handle.DragEndSampleIndex} | " +
				$"Final annX={handle.DragFinalAnnotationX:0.##} sampleFinal={handle.DragFinalSampleIndex} axisFinal={handle.DragFinalAxisValue:0.#####}");
			LogCursorEvent($"Layout#{handle.DragSessionId} {GetChartLayoutDiagnostics()}");

			if (!double.IsNaN(handle.DragEndPointerX) && !double.IsNaN(handle.DragFinalAnnotationX))
			{
				var driftPx = Math.Abs(handle.DragFinalAnnotationX - handle.DragEndPointerX);
				if (driftPx >= 12)
				{
					LogCursorEvent($"DragDrift#{handle.DragSessionId} driftPx={driftPx:0.##} ptrX={handle.DragEndPointerX:0.##} finalX={handle.DragFinalAnnotationX:0.##}");
				}
			}
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
			handle.DragSessionId++;
			handle.DragStartUtc = DateTime.UtcNow;
			var pointer = ChartControl != null ? e.GetPosition(ChartControl) : new Point(double.NaN, double.NaN);
			if (ChartControl != null && !double.IsNaN(pointer.X) && !double.IsInfinity(pointer.X))
			{
				handle.DragPixelX = Math.Clamp(pointer.X, 0, ChartControl.ActualWidth);
			}

			handle.DragStartPointerX = pointer.X;
			handle.DragStartAnnotationX = annotation.X1 is double annX ? annX : double.NaN;
			handle.DragStartSampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet?.TotalSamples - 1 ?? 0));
			handle.DragStartAxisValue = GetAxisValueAt(handle.DragStartSampleIndex) ?? double.NaN;

			var visible = ChartControl?.PrimaryAxis?.VisibleRange;
			var visStart = visible?.Start ?? double.NaN;
			var visEnd = visible?.End ?? double.NaN;
			var chartWidth = ChartControl?.ActualWidth ?? double.NaN;
			var normalized = (!double.IsNaN(pointer.X) && chartWidth > 0) ? Math.Clamp(pointer.X / chartWidth, 0, 1) : double.NaN;
			var axisAtPointer = (!double.IsNaN(normalized) && !double.IsNaN(visStart) && !double.IsNaN(visEnd))
				? (visStart + normalized * (visEnd - visStart))
				: double.NaN;
			var hasIndex = TryConvertAxisValueToIndex(axisAtPointer, out var indexFromPointer);

			LogCursorEvent(
				$"DragStart#{handle.DragSessionId} ptrX={pointer.X:0.##} annX={handle.DragStartAnnotationX:0.##} dragX={handle.DragPixelX:0.##} " +
				$"vis=({visStart:0.#####}-{visEnd:0.#####}) axisAtPtr={axisAtPointer:0.#####} idxAtPtr={(hasIndex ? indexFromPointer.ToString(CultureInfo.InvariantCulture) : "-")} " +
				$"handleIdx={handle.Index:0.###} sample={handle.DragStartSampleIndex} axisAtSample={handle.DragStartAxisValue:0.#####}");

			var xText = annotation.X1 is double value ? value.ToString("0.###", CultureInfo.InvariantCulture) : annotation.X1?.ToString() ?? "<null>";
			LogCursorDebug($"Annotation mouse down -> cursorIndex={handle.Index:F2}, pointer=({pointer.X:F2},{pointer.Y:F2}), x={xText}, unit={annotation.CoordinateUnit}");
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
			handle.DragEndPointerX = pointer.X;
			if (KeepCursorPixelOnRelease && ChartControl != null && !double.IsNaN(pointer.X) && !double.IsInfinity(pointer.X))
			{
				handle.ReleasedPixelX = Math.Clamp(pointer.X, 0, ChartControl.ActualWidth);
			}
			handle.DragEndAnnotationX = annotation.X1 is double annX ? annX : double.NaN;
			var visible = ChartControl?.PrimaryAxis?.VisibleRange;
			var visStart = visible?.Start ?? double.NaN;
			var visEnd = visible?.End ?? double.NaN;
			double normalized;
			if (!double.IsNaN(pointer.X) && TryGetSeriesClipRect(out var clipRect) && clipRect.Width > 0)
			{
				var plotX = Math.Clamp(pointer.X - clipRect.X, 0, clipRect.Width);
				normalized = plotX / clipRect.Width;
			}
			else
			{
				var chartWidth = ChartControl?.ActualWidth ?? double.NaN;
				normalized = (!double.IsNaN(pointer.X) && chartWidth > 0)
					? Math.Clamp(pointer.X / chartWidth, 0, 1)
					: double.NaN;
			}
			handle.DragEndAxisValue = (!double.IsNaN(normalized) && !double.IsNaN(visStart) && !double.IsNaN(visEnd))
				? (visStart + normalized * (visEnd - visStart))
				: double.NaN;
			handle.DragEndSampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet?.TotalSamples - 1 ?? 0));
			handle.DragPixelX = null;
			var xText = annotation.X1 is double value ? value.ToString("0.###", CultureInfo.InvariantCulture) : annotation.X1?.ToString() ?? "<null>";
			LogCursorDebug($"Annotation mouse up -> cursorIndex={handle.Index:F2}, pointer=({pointer.X:F2},{pointer.Y:F2}), x={xText}, unit={annotation.CoordinateUnit}");
			UpdateCursorAnnotation(handle);

			var finalSample = (int)Math.Clamp(Math.Round(handle.Index), 0, Math.Max(0, DataSet?.TotalSamples - 1 ?? 0));
			var finalAxis = GetAxisValueAt(finalSample) ?? double.NaN;
			var finalAnnX = handle.Annotation?.X1 is double finalX ? finalX : double.NaN;
			handle.DragFinalSampleIndex = finalSample;
			handle.DragFinalAxisValue = finalAxis;
			handle.DragFinalAnnotationX = finalAnnX;

			LogCursorEvent(
				$"MouseUp#{handle.DragSessionId} ptrX={handle.DragEndPointerX:0.##} annX={handle.DragEndAnnotationX:0.##} " +
				$"axisAtPtr={handle.DragEndAxisValue:0.#####} handleIdx={handle.Index:0.###} sampleEnd={handle.DragEndSampleIndex} | " +
				$"Final annX={handle.DragFinalAnnotationX:0.##} sampleFinal={handle.DragFinalSampleIndex} axisFinal={handle.DragFinalAxisValue:0.#####}");
			LogCursorEvent($"Layout#{handle.DragSessionId} {GetChartLayoutDiagnostics()}");

			if (!double.IsNaN(handle.DragEndPointerX) && !double.IsNaN(handle.DragFinalAnnotationX))
			{
				var driftPx = Math.Abs(handle.DragFinalAnnotationX - handle.DragEndPointerX);
				if (driftPx >= 12)
				{
					LogCursorEvent($"MouseUpDrift#{handle.DragSessionId} driftPx={driftPx:0.##} ptrX={handle.DragEndPointerX:0.##} finalX={handle.DragFinalAnnotationX:0.##}");
				}
			}
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

			if (ChartControl == null || ChartControl.ActualWidth <= 0 || ChartControl.ActualHeight <= 0)
			{
				return;
			}

            var sampleIndex = (int)Math.Clamp(Math.Round(handle.Index), 0, DataSet.TotalSamples - 1);
			var axisValue = GetAxisValueAt(sampleIndex);
			if (!axisValue.HasValue)
			{
				return;
			}
            var (minValue, maxValue) = GetVisibleValueRange();
            if (Math.Abs(maxValue - minValue) < 0.001)
            {
                maxValue = minValue + 1;
            }

			if (handle.Annotation != null)
            {
				handle.Annotation.CoordinateUnit = CoordinateUnit.Pixel;
				handle.Annotation.XAxisName = PrimaryAxisName;
				handle.Annotation.YAxisName = SecondaryAxisName;

				double xPixel;
				if (handle.IsDragging && handle.DragPixelX.HasValue)
				{
					xPixel = Math.Clamp(handle.DragPixelX.Value, 0, ChartControl.ActualWidth);
				}
				else if (KeepCursorPixelOnRelease && handle.ReleasedPixelX.HasValue)
				{
					// TODO(cursor): Mode A. Prefer release pixel to avoid post-release jump.
					// If you want strict snap-to-sample visual behavior, remove this branch (or set KeepCursorPixelOnRelease=false)
					// so X is always recomputed from the snapped sample's axis value.
					xPixel = Math.Clamp(handle.ReleasedPixelX.Value, 0, ChartControl.ActualWidth);
				}
				else
				{
					// Map axis value -> pixel position using the plot area (SeriesClipRect)
					// so cursor snapping aligns with Syncfusion's rendered series coordinates.
					var visible = ChartControl.PrimaryAxis?.VisibleRange;
					var start = visible?.Start ?? 0d;
					var end = visible?.End ?? 1d;
					if (Math.Abs(end - start) < 1e-12)
					{
						end = start + 1;
					}
					var normalized = (axisValue.Value - start) / (end - start);
					if (double.IsNaN(normalized) || double.IsInfinity(normalized))
					{
						normalized = 0;
					}
					normalized = Math.Clamp(normalized, 0, 1);

					if (TryGetSeriesClipRect(out var clipRect) && clipRect.Width > 0)
					{
						xPixel = clipRect.X + normalized * clipRect.Width;
					}
					else
					{
						xPixel = normalized * ChartControl.ActualWidth;
					}
				}

				handle.Annotation.X1 = xPixel;
				handle.Annotation.X2 = xPixel;
				handle.Annotation.Y1 = 0;
				handle.Annotation.Y2 = ChartControl.ActualHeight;
				handle.Annotation.StrokeThickness = 3.5;
                handle.Annotation.Stroke = handle.CursorBrush;
				// Make built-in annotation invisible; overlay line is the visible cursor.
				handle.Annotation.Stroke = Brushes.Transparent;
            }

			UpdateOverlayLine(handle);

			var renderedPixel = double.NaN;
			if (handle.Annotation?.X1 is double x1)
			{
				renderedPixel = x1;
			}
			LogCursorTrace("UpdateCursorAnnotation", handle, renderedPixel, axisValue.Value, sampleIndex);

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
			var axisValue = GetAxisValueAt(sampleIndex);
			handle.Snapshot.CursorTime = axisValue ?? double.NaN;

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
			if (DataSet == null || DataSet.TotalSamples <= 0 || SelectedXAxisSeries?.Values == null)
			{
				return false;
			}

			var axisValues = SelectedXAxisSeries.Values;
			var maxIndex = Math.Min(DataSet.TotalSamples, axisValues.Length) - 1;
			if (maxIndex < 0)
			{
				return false;
			}

			// Coarse-to-fine search: keep stride small enough to avoid missing the best match
			// when axis values are non-uniform.
			var stride = Math.Max(1, maxIndex / 8000);
			var bestIndex = 0;
			double bestDiff = double.MaxValue;

			for (int i = 0; i <= maxIndex; i += stride)
			{
				var candidate = axisValues[i];
				if (float.IsNaN(candidate) || float.IsInfinity(candidate))
				{
					continue;
				}

				var diff = Math.Abs(candidate - axisValue);
				if (diff < bestDiff)
				{
					bestDiff = diff;
					bestIndex = i;
				}
			}

			var window = Math.Max(4, stride * 8);
			var start = Math.Max(0, bestIndex - window);
			var end = Math.Min(maxIndex, bestIndex + window);
			for (int i = start; i <= end; i++)
			{
				var candidate = axisValues[i];
				if (float.IsNaN(candidate) || float.IsInfinity(candidate))
				{
					continue;
				}

				var diff = Math.Abs(candidate - axisValue);
				if (diff < bestDiff)
				{
					bestDiff = diff;
					bestIndex = i;
					if (diff <= 1e-6)
					{
						break;
					}
				}
			}

			sampleIndex = bestIndex;
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
			const double axisThreshold = 0.03; // axis units
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

		private void LogAnnotationDebug(string message)
		{
			if (!EnableAnnotationDebugLogging)
			{
				return;
			}
			PerformanceLogger.Instance.LogInfo(message, "Annotation_Debug");
			Console.WriteLine($"[AnnotationDebug] {message}");
		}

		private void LogCursorEvent(string message)
		{
			PerformanceLogger.Instance.LogInfo(message, "Cursor_Event");
			Console.WriteLine($"[CursorEvent] {message}");
		}

		private string GetChartLayoutDiagnostics()
		{
			if (ChartControl == null)
			{
				return "Chart=<null>";
			}

			static string FormatRect(object? value)
			{
				if (value == null)
				{
					return "<null>";
				}
				return value.ToString() ?? value.GetType().Name;
			}

			var t = ChartControl.GetType();
			var candidates = new[]
			{
				"SeriesClipRect",
				"ChartAreaRect",
				"PlotAreaRect",
				"ChartAxisLayoutPanel",
				"AxisLayoutPanel",
				"ChartPlotArea",
				"ActualSeriesClipRect",
				"ClipRect",
				"SeriesClipBounds",
				"ChartArea",
				"ChartContainer"
			};

			var parts = new List<string>(capacity: 8)
			{
				$"ChartType={t.Name}",
				$"Actual=({ChartControl.ActualWidth:0.##}x{ChartControl.ActualHeight:0.##})"
			};

			foreach (var name in candidates)
			{
				try
				{
					var prop = t.GetProperty(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
					if (prop == null)
					{
						continue;
					}

					object? value;
					try
					{
						value = prop.GetValue(ChartControl);
					}
					catch
					{
						value = "<get_failed>";
					}

					parts.Add($"{name}={FormatRect(value)}");
				}
				catch
				{
					// ignore reflection failure
				}
			}

			var visible = ChartControl.PrimaryAxis?.VisibleRange;
			if (visible != null)
			{
				try
				{
					var rangeObj = (object)visible;
					var rangeType = rangeObj.GetType();
					object? startObj = rangeType.GetProperty("Start")?.GetValue(rangeObj)
						?? rangeType.GetProperty("Minimum")?.GetValue(rangeObj)
						?? rangeType.GetProperty("Min")?.GetValue(rangeObj);
					object? endObj = rangeType.GetProperty("End")?.GetValue(rangeObj)
						?? rangeType.GetProperty("Maximum")?.GetValue(rangeObj)
						?? rangeType.GetProperty("Max")?.GetValue(rangeObj);

					if (startObj is double start && endObj is double end)
					{
						parts.Add($"Vis=({start:0.#####}-{end:0.#####})");
					}
					else
					{
						parts.Add($"Vis={visible}");
					}
				}
				catch
				{
					parts.Add($"Vis={visible}");
				}
			}

			return string.Join(" | ", parts);
		}

		private bool TryGetSeriesClipRect(out Rect clipRect)
		{
			clipRect = Rect.Empty;
			if (ChartControl == null)
			{
				return false;
			}

			try
			{
				// Public in Syncfusion SfChart (WPF). Coordinates are in chart's local coordinate space.
				clipRect = ChartControl.SeriesClipRect;
				return clipRect.Width > 0 && clipRect.Height > 0;
			}
			catch
			{
				return false;
			}
		}

		private void LogCursorTrace(string stage, ChartCursorHandle handle, double xPixel, double axisValue, int sampleIndex)
		{
			if (!EnableAnnotationDebugLogging)
			{
				return;
			}

			// Cursor rendering triggers very frequently (layout/size updates). Throttle to keep Output usable.
			var now = DateTime.UtcNow;
			var elapsed = now - handle.LastTraceTimestamp;
			var pixelChanged = double.IsNaN(handle.LastTracePixel) || double.IsNaN(xPixel)
				? false
				: Math.Abs(xPixel - handle.LastTracePixel) >= 0.75;
			var sampleChanged = handle.LastTraceSampleIndex != sampleIndex;
			var minInterval = handle.IsDragging ? TimeSpan.FromMilliseconds(150) : TimeSpan.FromMilliseconds(600);
			if (!sampleChanged && !pixelChanged && elapsed < minInterval)
			{
				return;
			}

			handle.LastTraceTimestamp = now;
			handle.LastTraceSampleIndex = sampleIndex;
			handle.LastTracePixel = xPixel;

			var visible = ChartControl?.PrimaryAxis?.VisibleRange;
			var start = visible?.Start ?? double.NaN;
			var end = visible?.End ?? double.NaN;
			var width = ChartControl?.ActualWidth ?? double.NaN;
			var annX = double.NaN;
			if (handle.Annotation?.X1 is double x1)
			{
				annX = x1;
			}
			LogAnnotationDebug(
				$"CursorTrace[{stage}] idx={handle.Index:0.###}, sample={sampleIndex}, xPixel={xPixel:0.##}/{width:0.##}, axis={axisValue:0.#####}, vis=({start:0.#####}-{end:0.#####}), annX={annX:0.##}");
		}

        private void ClearCursorHandles()
        {
			if (ChartControl != null)
			{
				foreach (var handle in _cursorHandles)
				{
					DetachCursorAnnotation(handle);
					DetachOverlayLine(handle);
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
		public System.Windows.Shapes.Line? OverlayLine { get; set; }
		public double Index { get; set; }
		public double? DragPixelX { get; set; }
			public double? ReleasedPixelX { get; set; }
		public int DragSessionId { get; set; }
		public DateTime DragStartUtc { get; set; } = DateTime.MinValue;
		public double DragStartPointerX { get; set; } = double.NaN;
		public double DragStartAnnotationX { get; set; } = double.NaN;
		public double DragStartAxisValue { get; set; } = double.NaN;
		public int DragStartSampleIndex { get; set; } = -1;
		public double DragEndPointerX { get; set; } = double.NaN;
		public double DragEndAnnotationX { get; set; } = double.NaN;
		public double DragEndAxisValue { get; set; } = double.NaN;
		public int DragEndSampleIndex { get; set; } = -1;
		public double DragFinalAnnotationX { get; set; } = double.NaN;
		public double DragFinalAxisValue { get; set; } = double.NaN;
		public int DragFinalSampleIndex { get; set; } = -1;
		public CursorSnapshotViewModel? Snapshot { get; set; }
		public Brush CursorBrush { get; set; } = Brushes.Crimson;
		public double LastLoggedAxisValue { get; set; } = double.NaN;
		public double LastLoggedIndex { get; set; } = double.NaN;
		public double LastLoggedPointerX { get; set; } = double.NaN;
		public DateTime LastLogTimestamp { get; set; } = DateTime.MinValue;
		public DateTime LastTraceTimestamp { get; set; } = DateTime.MinValue;
		public double LastTracePixel { get; set; } = double.NaN;
		public int LastTraceSampleIndex { get; set; } = -1;
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

		private void DetachOverlayLine(ChartCursorHandle handle)
		{
			if (handle.OverlayLine == null || _cursorOverlay == null)
			{
				return;
			}

			_cursorOverlay.Children.Remove(handle.OverlayLine);
			handle.OverlayLine = null;
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

			if (handle.Annotation?.X1 is not double xPixel || double.IsNaN(xPixel) || double.IsInfinity(xPixel))
			{
				line.Visibility = Visibility.Collapsed;
				return;
			}

			// Convert chart-local X pixel into overlay-local X.
			var xOnOverlay = ChartControl.TranslatePoint(new Point(xPixel, 0), _cursorOverlay).X;
			if (double.IsNaN(xOnOverlay) || double.IsInfinity(xOnOverlay))
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
			// Re-apply style each time in case templates/styles reset them.
			line.StrokeDashArray = new DoubleCollection { 6, 4 };
			line.StrokeStartLineCap = PenLineCap.Round;
			line.StrokeEndLineCap = PenLineCap.Round;
			line.StrokeDashCap = PenLineCap.Round;
			line.X1 = xOnOverlay;
			line.X2 = xOnOverlay;
			line.Y1 = 0;
			line.Y2 = Math.Max(0, height);
		}

		public sealed class CursorSnapshotViewModel : INotifyPropertyChanged
		{
			private int _cursorIndex;
			private double _cursorTime = double.NaN;
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

			public double CursorTime
			{
				get => _cursorTime;
				set
				{
					var valuesMatch = (double.IsNaN(_cursorTime) && double.IsNaN(value)) ||
						(!double.IsNaN(_cursorTime) && !double.IsNaN(value) && Math.Abs(_cursorTime - value) < double.Epsilon);
					if (valuesMatch)
					{
						return;
					}

					_cursorTime = value;
					OnPropertyChanged();
					OnPropertyChanged(nameof(CursorTimeDisplay));
				}
			}

			public string CursorLabel => $"Cursor {_cursorIndex}";
			public string CursorTimeDisplay => double.IsNaN(_cursorTime) ? "-" : _cursorTime.ToString("0.###", CultureInfo.InvariantCulture);
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

		private sealed class NtChartDataPoint
		{
			public NtChartDataPoint(double x, double value)
			{
				X = x;
				Value = value;
			}

			public double X { get; }
			public double Value { get; }
			public string? SeriesName { get; set; }
		}

		public sealed class AxisSeriesOption : INotifyPropertyChanged
		{
			public AxisSeriesOption(SeriesData seriesData)
			{
				SeriesData = seriesData;
			}

			private bool _isSelected;
			public SeriesData SeriesData { get; }

			public bool IsSelected
			{
				get => _isSelected;
				set
				{
					if (_isSelected != value)
					{
						_isSelected = value;
						PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
					}
				}
			}

			public event PropertyChangedEventHandler? PropertyChanged;
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