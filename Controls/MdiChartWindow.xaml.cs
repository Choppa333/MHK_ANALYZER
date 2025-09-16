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
                using var timer = PerformanceLogger.Instance.StartTimer("차트 윈도우 데이터 설정", "Chart_Display");
                
                _dataSet = value; 
                OnPropertyChanged();
                
                PerformanceLogger.Instance.LogInfo($"차트 데이터 설정 - 파일: {value?.FileName}, 시리즈: {value?.SeriesData?.Count}", "Chart_Display");
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
            _isDragging = true;
            var canvas = (Canvas)this.Parent;
            _lastPosition = e.GetPosition(canvas);
            this.CaptureMouse();
            
            // 활성 윈도우로 설정
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
            this.Height = 30; // 타이틀바만 보이도록
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