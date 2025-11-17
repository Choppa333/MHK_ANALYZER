using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using MGK_Analyzer.Models;
using MGK_Analyzer.Services;
using Syncfusion.UI.Xaml.Charts;

namespace MGK_Analyzer.Windows
{
    public partial class ChartWindow : Window, INotifyPropertyChanged
    {
        private MemoryOptimizedDataSet? _dataSet;
        private string _windowTitle = "Chart Window";

        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
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

        public ChartWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void InitializeSeriesList()
        {
            using var timer = PerformanceLogger.Instance.StartTimer("시리즈 리스트 초기화", "Chart_Display");
            
            SeriesList.Clear();
            if (DataSet?.SeriesData != null)
            {
                foreach (var series in DataSet.SeriesData.Values)
                {
                    var viewModel = new ChartSeriesViewModel { SeriesData = series };
                    viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
                    SeriesList.Add(viewModel);
                }
                
                PerformanceLogger.Instance.LogInfo($"시리즈 리스트 초기화 완료 - {SeriesList.Count}개 시리즈", "Chart_Display");
            }
        }

        private async void SeriesViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChartSeriesViewModel.IsSelected))
            {
                var viewModel = (ChartSeriesViewModel)sender;
                var seriesName = viewModel.SeriesData.Name;
                
                PerformanceLogger.Instance.LogInfo($"시리즈 선택 변경: {seriesName} -> {viewModel.IsSelected}", "Chart_Display");
                
                if (viewModel.IsSelected)
                {
                    await Task.Run(() =>
                    {
                        using var timer = PerformanceLogger.Instance.StartTimer($"시리즈 추가: {seriesName}", "Chart_Display");
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
            using var overallTimer = PerformanceLogger.Instance.StartTimer($"전체 차트 시리즈 추가: {series.Name}", "Chart_Display");
            
            if (DataSet == null || DataSet.TotalSamples == 0) return;
            
            var maxPoints = 500;
            var totalPoints = DataSet.TotalSamples;
            var step = Math.Max(1, totalPoints / maxPoints);
            
            PerformanceLogger.Instance.LogInfo($"다운샘플링 설정 - 전체: {totalPoints:N0}, 단계: {step}, 표시: {totalPoints/step:N0}개 포인트", "Chart_Display");
            
            try
            {
                if (series.DataType == typeof(bool))
                {
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
                    stepSeries.ItemsSource = dataPoints;
                    ChartControl.Series.Add(stepSeries);
                    
                    PerformanceLogger.Instance.LogInfo($"Bool 시리즈 추가 완료: {series.Name} ({dataPoints.Count:N0}개 포인트)", "Chart_Display");
                }
                else
                {
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
                    PerformanceLogger.Instance.LogInfo($"샘플 데이터 포인트(처음5) for {series.Name}: count={dataPoints.Count}", "Chart_Display");
                    int idx = 0;
                    foreach (var dp in dataPoints)
                    {
                        if (idx++ >= 5) break;
                        PerformanceLogger.Instance.LogInfo($"  DP[{idx-1}]: Time={dp.Time}, Value={dp.Value}", "Chart_Display");
                    }

                    lineSeries.ItemsSource = dataPoints;
                    ChartControl.Series.Add(lineSeries);
                    ChartControl.InvalidateVisual();
                    ChartControl.UpdateLayout();
                    
                    PerformanceLogger.Instance.LogInfo($"Double 시리즈 추가 완료: {series.Name} ({dataPoints.Count:N0}개 포인트)", "Chart_Display");
                }
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"시리즈 추가 오류: {series.Name} - {ex.Message}\n{ex}", "Chart_Display");
            }
            
            if (ChartControl.Legend is ChartLegend legend)
            {
                legend.Visibility = Visibility.Visible;
            }
            
            PerformanceLogger.Instance.LogInfo($"현재 차트의 총 시리즈 수: {ChartControl.Series.Count}", "Chart_Display");
        }

        private ObservableCollection<ChartDataPoint> CreateBoolDataPoints(SeriesData series, int step)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Bool 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
            var dataPoints = new ObservableCollection<ChartDataPoint>();
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

        private ObservableCollection<ChartDataPoint> CreateDoubleDataPoints(SeriesData series, int step)
        {
            using var timer = PerformanceLogger.Instance.StartTimer($"Double 데이터 포인트 생성: {series.Name}", "Chart_Display");
            
            var dataPoints = new ObservableCollection<ChartDataPoint>();
            var pointCount = 0;
            var maxPointsToCreate = 500;
            
            if (series.Values == null || series.Values.Length == 0)
            {
                return dataPoints;
            }
            
            var maxSafeIndex = Math.Min(DataSet.TotalSamples, series.Values.Length);
            
            for (int i = 0; i < maxSafeIndex && pointCount < maxPointsToCreate; i += step)
            {
                try
                {
                    var time = DataSet.GetTimeAt(i);
                    var value = series.Values[i];
                    
                    if (float.IsNaN(value) || float.IsInfinity(value))
                    {
                        continue;
                    }
                    
                    dataPoints.Add(new ChartDataPoint(time, value)
                    {
                        SeriesName = series.Name
                    });
                    
                    pointCount++;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"ERROR: 데이터 포인트 생성 중 오류 (인덱스 {i}): {ex.Message}");
                    break;
                }
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
            }
        }

        private bool GetBoolValue(System.Collections.BitArray? bitValues, int index)
        {
            if (bitValues == null) return false;
            if (index < 0 || index >= bitValues.Length) return false;
            return bitValues[index];
        }

        private void ExportChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                    FileName = $"Chart_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    MessageBox.Show("Export functionality will be implemented in the next phase.", 
                                   "Export Chart", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
