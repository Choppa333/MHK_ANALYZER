using System.Collections.Generic;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
using MGK_Analyzer.Services;
using MGK_Analyzer.Views;
using MGK_Analyzer.Models;
using MGK_Analyzer.Utils;
using MGK_Analyzer.Windows;
using Microsoft.Win32;
using System.IO;
using System.Linq;
using MGK_Analyzer.Controls;

namespace MGK_Analyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private CsvDataLoader _csvLoader;
        private LogViewerWindow? _logViewerWindow;
        private int _chartWindowCount = 0;
        private MdiWindowManager? _mdiWindowManager;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            
            // 테마 변경 이벤트 구독
            ThemeManager.Instance.ThemeChanged += OnThemeChanged;
            
            // 설정 로드
            _settings = AppSettings.Load();
            
            // 서비스 초기화
            _csvLoader = new CsvDataLoader();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            using var timer = PerformanceLogger.Instance.StartTimer("MainWindow 로드", "Application");
            
            UpdateStatusBar("MGK Analyzer가 시작되었습니다.");
            
            // 저장된 테마 적용
            ThemeManager.Instance.InitializeTheme();
            
            // MDI 매니저 초기화
            _mdiWindowManager = new MdiWindowManager(MdiCanvas);
            
            // 현재 테마 상태 표시
            var currentTheme = ThemeManager.Instance.GetThemeDisplayName(ThemeManager.Instance.CurrentTheme);
            UpdateStatusBar($"MGK Analyzer가 시작되었습니다. 현재 테마: {currentTheme}");
            
            // 윈도우 개수 업데이트
            UpdateWindowCount();
            
            PerformanceLogger.Instance.LogInfo("MainWindow 초기화 완료", "Application");
        }

        private void OnThemeChanged(object? sender, ThemeManager.ThemeType newTheme)
        {
            UpdateStatusBar($"테마가 '{ThemeManager.Instance.GetThemeDisplayName(newTheme)}'로 변경되었습니다.");
        }

        private void UpdateStatusBar(string message)
        {
            try
            {
                var statusText = FindName("StatusText") as TextBlock;
                if (statusText != null)
                {
                    statusText.Text = message;
                }
            }
            catch
            {
                // StatusText를 찾을 수 없는 경우 무시
            }
        }

        #region 기존 메뉴 이벤트 핸들러

        private void NewFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // UTF-8 BOM 테스트 CSV 파일 생성
                var testFilePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "TestData_UTF8.csv");
                CsvFileGenerator.CreateUtf8TestFile(testFilePath);
                
                UpdateStatusBar($"UTF-8 테스트 파일이 생성되었습니다: {testFilePath}");
                MessageBox.Show($"UTF-8 BOM이 포함된 테스트 CSV 파일이 생성되었습니다.\n\n위치: {testFilePath}\n\n이 파일을 '데이터 가져오기'로 로드해보세요.", 
                              "테스트 파일 생성", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"테스트 파일 생성 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("테스트 파일 생성 실패");
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("파일을 열고 있습니다...");
            Microsoft.Win32.OpenFileDialog openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "모든 파일 (*.*)|*.*|텍스트 파일 (*.txt)|*.txt|Excel 파일 (*.xlsx)|*.xlsx"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                UpdateStatusBar($"파일이 열렸습니다: {System.IO.Path.GetFileName(openFileDialog.FileName)}");
            }
            else
            {
                UpdateStatusBar("파일 열기가 취소되었습니다.");
            }
        }

        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("파일을 저장했습니다.");
            MessageBox.Show("파일 저장 기능이 실행되었습니다.", "파일", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("정말로 종료하시겠습니까?", "종료", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                Application.Current.Shutdown();
            }
        }

        private async void ImportData_Click(object sender, RoutedEventArgs e)
        {
            if (_mdiWindowManager == null)
            {
                UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                return;
            }
            var openFileDialog = new OpenFileDialog
            {
                Title = "CSV 데이터 파일 선택",
                Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                await LoadCsvAndDisplayChartAsync(openFileDialog.FileName, "CSV 데이터 가져오기");
            }
            else
            {
                UpdateStatusBar("CSV 파일 선택이 취소되었습니다.");
            }
        }

        private async Task LoadCsvAndDisplayChartAsync(string filePath, string actionTitle)
        {
            if (_mdiWindowManager == null)
            {
                UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                return;
            }

            using var overallTimer = PerformanceLogger.Instance.StartTimer($"{actionTitle}: {System.IO.Path.GetFileName(filePath)}", "Data_Import");
            var fileInfo = new FileInfo(filePath);
            var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);

            UpdateStatusBar($"{actionTitle} 파일을 로딩하고 있습니다... (크기: {fileSizeMB:F1}MB)");
            WelcomeMessage.Visibility = Visibility.Collapsed;

            if (fileSizeMB > 10)
            {
                var result = MessageBox.Show(
                    $"큰 파일입니다 (크기: {fileSizeMB:F1}MB).\n" +
                    "로딩에 시간이 걸릴 수 있습니다.\n\n" +
                    "계속 진행하시겠습니까?",
                    "대용량 파일 경고",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.No)
                {
                    PerformanceLogger.Instance.LogInfo("사용자가 대용량 파일 로딩을 취소했습니다", "Data_Import");
                    UpdateStatusBar("파일 로딩이 취소되었습니다.");
                    return;
                }
            }

            var progress = new Progress<int>(percent =>
            {
                var statusMessage = fileSizeMB > 5
                    ? $"CSV 파일 로딩 중... {percent}% (크기: {fileSizeMB:F1}MB - 잠시만 기다려주세요)"
                    : $"CSV 파일 로딩 중... {percent}%";
                UpdateStatusBar(statusMessage);
            });

            try
            {
                MemoryOptimizedDataSet dataSet;
                using (var csvTimer = PerformanceLogger.Instance.StartTimer("CSV 파일 로딩", "Data_Import"))
                {
                    dataSet = await _csvLoader.LoadCsvDataAsync(filePath, progress);
                }

                CreateChartWindowFromDataSet(dataSet);

                var loadMessage = fileSizeMB > 5
                    ? $"'{dataSet.FileName}' 파일이 성공적으로 로드되었습니다. (크기: {fileSizeMB:F1}MB, 데이터: {dataSet.TotalSamples:N0}개, 시리즈: {dataSet.SeriesData.Count}개) - 다운샘플링 적용됨"
                    : $"'{dataSet.FileName}' 파일이 성공적으로 로드되었습니다. (데이터: {dataSet.TotalSamples:N0}개, 시리즈: {dataSet.SeriesData.Count}개)";

                UpdateStatusBar(loadMessage);
                PerformanceLogger.Instance.LogInfo($"{actionTitle} 완료: {dataSet.FileName}", "Data_Import");
                UpdateWindowCount();
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"데이터 가져오기 실패: {ex.Message}", "Data_Import");
                MessageBox.Show($"CSV 파일 로딩 중 오류가 발생했습니다:\n\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("CSV 파일 로딩 실패");
            }
        }

        private void CreateChartWindowFromDataSet(MemoryOptimizedDataSet dataSet)
        {
            using (var chartTimer = PerformanceLogger.Instance.StartTimer("차트 윈도우 생성", "Data_Import"))
            {
                var mdi = _mdiWindowManager!.CreateChartWindow(dataSet.FileName, dataSet);
                mdi.WindowClosed += (s, args) => UpdateWindowCount();
                _chartWindowCount = _mdiWindowManager.WindowCount;
                PerformanceLogger.Instance.LogInfo($"MDI 차트 윈도우 생성 완료: {dataSet.FileName}", "Data_Import");
            }
        }

        private void ExportData_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("데이터를 내보내고 있습니다...");
            MessageBox.Show("데이터 내보내기 기능이 실행되었습니다.", "데이터", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void AnalyzeData_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("데이터를 분석하고 있습니다...");
            MessageBox.Show("데이터 분석 기능이 실행되었습니다.", "데이터", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void FilterData_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("데이터 필터링 중...");
            MessageBox.Show("데이터 필터링 기능이 실행되었습니다.", "데이터", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("새 프로젝트를 만들었습니다.");
            MessageBox.Show("새 프로젝트 기능이 실행되었습니다.", "프로젝트", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("프로젝트를 열고 있습니다...");
            MessageBox.Show("프로젝트 열기 기능이 실행되었습니다.", "프로젝트", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SaveProject_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("프로젝트를 저장했습니다.");
            MessageBox.Show("프로젝트 저장 기능이 실행되었습니다.", "프로젝트", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ProjectSettings_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("프로젝트 설정을 열었습니다.");
            MessageBox.Show("프로젝트 설정 기능이 실행되었습니다.", "프로ject", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Options_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("옵션 창을 열었습니다.");
            MessageBox.Show("옵션 기능이 실행되었습니다.", "도구", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("테마 설정 창을 열었습니다.");
            
            // 테마 설정 창 열기
            var themeSettingsWindow = new ThemeSettingsWindow
            {
                Owner = this
            };
            themeSettingsWindow.ShowDialog();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("정보 창을 열었습니다.");
            MessageBox.Show("MGK Analyzer v1.0\n\n데이터 분석 도구\n개발자: MGK Team", "정보", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ShowLogViewer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_logViewerWindow == null)
                {
                    _logViewerWindow = new LogViewerWindow
                    {
                        Owner = this
                    };
                }
                
                if (!_logViewerWindow.IsVisible)
                {
                    _logViewerWindow.Show();
                }
                else
                {
                    _logViewerWindow.Activate();
                }
                
                PerformanceLogger.Instance.LogInfo("성능 로그 뷰어 창 열기", "Application");
                UpdateStatusBar("성능 로그 뷰어를 열었습니다.");
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"로그 뷰어 창 열기 실패: {ex.Message}", "Application");
                MessageBox.Show($"로그 뷰어를 열 수 없습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region 시험 유형 이벤트 핸들러

        private async void StandardTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mdiWindowManager == null)
                {
                    UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                    return;
                }

                var openFileDialog = new OpenFileDialog
                {
                    Title = "정격시험 CSV 파일 선택",
                    Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                    CheckFileExists = true,
                    CheckPathExists = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    UpdateStatusBar("정격시험 데이터를 로딩하고 있습니다...");
                    PerformanceLogger.Instance.LogInfo("정격시험 파일 선택", "TestMode");
                    await LoadCsvAndDisplayChartAsync(openFileDialog.FileName, "정격시험 데이터");
                }
                else
                {
                    UpdateStatusBar("정격시험 파일 선택이 취소되었습니다.");
                }
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"정격시험 모드 오류: {ex.Message}", "TestMode");
                MessageBox.Show($"정격시험 모드에서 오류 발생:\n{ex.Message}",
                              "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private MemoryOptimizedDataSet CreateStandardTestSampleData()
        {
            var dataSet = new MemoryOptimizedDataSet
            {
                FileName = "정격시험 샘플 데이터",
                BaseTime = DateTime.Now.AddSeconds(-100),
                TimeInterval = 0.1f,
                TotalSamples = 1000
            };

            var voltageSeries = new SeriesData
            {
                Name = "전압",
                Unit = "V",
                DataType = typeof(double),
                Color = new SolidColorBrush(Colors.Blue),
                IsVisible = true
            };
            voltageSeries.Values = new float[dataSet.TotalSamples];
            for (int i = 0; i < voltageSeries.Values.Length; i++)
            {
                voltageSeries.Values[i] = (float)(220 + 10 * Math.Sin(i * 0.1));
            }
            voltageSeries.MinValue = voltageSeries.Values.Min();
            voltageSeries.MaxValue = voltageSeries.Values.Max();
            voltageSeries.AvgValue = voltageSeries.Values.Average();

            var currentSeries = new SeriesData
            {
                Name = "전류",
                Unit = "A",
                DataType = typeof(double),
                Color = new SolidColorBrush(Colors.Red),
                IsVisible = true
            };
            currentSeries.Values = new float[dataSet.TotalSamples];
            for (int i = 0; i < currentSeries.Values.Length; i++)
            {
                currentSeries.Values[i] = (float)(10 + 2 * Math.Cos(i * 0.1));
            }
            currentSeries.MinValue = currentSeries.Values.Min();
            currentSeries.MaxValue = currentSeries.Values.Max();
            currentSeries.AvgValue = currentSeries.Values.Average();

            var tempSeries = new SeriesData
            {
                Name = "온도",
                Unit = "°C",
                DataType = typeof(double),
                Color = new SolidColorBrush(Colors.Orange),
                IsVisible = true
            };
            tempSeries.Values = new float[dataSet.TotalSamples];
            for (int i = 0; i < tempSeries.Values.Length; i++)
            {
                tempSeries.Values[i] = (float)(25 + i * 0.05 + Math.Sin(i * 0.3) * 2);
            }
            tempSeries.MinValue = tempSeries.Values.Min();
            tempSeries.MaxValue = tempSeries.Values.Max();
            tempSeries.AvgValue = tempSeries.Values.Average();

            dataSet.SeriesData.Add(voltageSeries.Name, voltageSeries);
            dataSet.SeriesData.Add(currentSeries.Name, currentSeries);
            dataSet.SeriesData.Add(tempSeries.Name, tempSeries);

            return dataSet;
        }

        private void LoadTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mdiWindowManager == null)
                {
                    UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                    return;
                }
                
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                    Title = "부하시험 데이터 파일 선택"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    // 파일 선택 완료 - 여기까지 진행
                    UpdateStatusBar($"부하시험 데이터 파일이 선택되었습니다: {System.IO.Path.GetFileName(openFileDialog.FileName)}");
                    PerformanceLogger.Instance.LogInfo($"부하시험 데이터 파일 선택: {openFileDialog.FileName}", "TestMode");
                    
                    // 다음 단계는 별도 구현 예정
                    return;
                }
                else
                {
                    UpdateStatusBar("부하시험 데이터 파일 선택이 취소되었습니다.");
                }
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"부하시험 모드 오류: {ex.Message}", "TestMode");
                MessageBox.Show($"부하시험 모드에서 오류 발생:\n{ex.Message}", 
                              "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void NoLoadTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mdiWindowManager == null)
                {
                    UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                    return;
                }
                
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                    Title = "무부하시험 데이터 파일 선택"
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    // 파일 선택 완료 - 여기까지 진행
                    UpdateStatusBar($"무부하시험 데이터 파일이 선택되었습니다: {System.IO.Path.GetFileName(openFileDialog.FileName)}");
                    PerformanceLogger.Instance.LogInfo($"무부하시험 데이터 파일 선택: {openFileDialog.FileName}", "TestMode");
                    
                    // 다음 단계는 별도 구현 예정
                    return;
                }
                else
                {
                    UpdateStatusBar("무부하시험 데이터 파일 선택이 취소되었습니다.");
                }
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"무부하시험 모드 오류: {ex.Message}", "TestMode");
                MessageBox.Show($"무부하시험 모드에서 오류 발생:\n{ex.Message}", 
                              "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private MemoryOptimizedDataSet CreateNtCurveTestSampleData()
        {
            var dataSet = new MemoryOptimizedDataSet
            {
                FileName = "NT-Curve 시험 샘플 데이터",
                BaseTime = DateTime.Now.AddSeconds(-120),
                TimeInterval = 0.1f,
                TotalSamples = 1200
            };

            // 토크 시리즈
            var torqueSeries = new SeriesData
            {
                Name = "토크",
                Unit = "Nm",
                DataType = typeof(double),
                Color = new SolidColorBrush(Colors.MediumPurple),
                IsVisible = true
            };
            torqueSeries.Values = new float[1200];
            for (int i = 0; i < 1200; i++)
            {
                torqueSeries.Values[i] = (float)(50 + 20 * Math.Sin(i * 0.01) + 5 * Math.Sin(i * 0.05));
            }
            torqueSeries.MinValue = torqueSeries.Values.Min();
            torqueSeries.MaxValue = torqueSeries.Values.Max();
            torqueSeries.AvgValue = torqueSeries.Values.Average();

            // 속도 시리즈
            var speedSeries = new SeriesData
            {
                Name = "속도",
                Unit = "rpm",
                DataType = typeof(double),
                Color = new SolidColorBrush(Colors.DarkOrange),
                IsVisible = true
            };
            speedSeries.Values = new float[1200];
            for (int i = 0; i < 1200; i++)
            {
                speedSeries.Values[i] = (float)(1500 + 200 * Math.Cos(i * 0.01) + 50 * Math.Sin(i * 0.02));
            }
            speedSeries.MinValue = speedSeries.Values.Min();
            speedSeries.MaxValue = speedSeries.Values.Max();
            speedSeries.AvgValue = speedSeries.Values.Average();

            // 효율 시리즈
            var efficiencySeries = new SeriesData
            {
                Name = "효율",
                Unit = "%",
                DataType = typeof(double),
                Color = new SolidColorBrush(Colors.ForestGreen),
                IsVisible = true
            };
            efficiencySeries.Values = new float[1200];
            for (int i = 0; i < 1200; i++)
            {
                efficiencySeries.Values[i] = (float)(85 + 5 * Math.Sin(i * 0.015) + 2 * Math.Cos(i * 0.03));
            }
            efficiencySeries.MinValue = efficiencySeries.Values.Min();
            efficiencySeries.MaxValue = efficiencySeries.Values.Max();
            efficiencySeries.AvgValue = efficiencySeries.Values.Average();

            dataSet.SeriesData.Add(torqueSeries.Name, torqueSeries);
            dataSet.SeriesData.Add(speedSeries.Name, speedSeries);
            dataSet.SeriesData.Add(efficiencySeries.Name, efficiencySeries);

            return dataSet;
        }

        private void NtCurveTest_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mdiWindowManager == null)
                {
                    UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                    return;
                }
                UpdateStatusBar("NT-Curve 시험 차트를 생성하고 있습니다...");
                PerformanceLogger.Instance.LogInfo("NT-Curve 시험 모드 선택", "TestMode");
                
                // NT-Curve 샘플 데이터 생성
                var sampleDataSet = CreateNtCurveTestSampleData();
                
                // 차트 윈도우 생성
                var mdi = _mdiWindowManager.CreateChartWindow("NT-Curve 시험 - " + DateTime.Now.ToString("HH:mm:ss"), sampleDataSet);
                mdi.WindowClosed += (s, args) => UpdateWindowCount();
                
                UpdateStatusBar($"NT-Curve 시험 차트가 생성되었습니다. (샘플 데이터: {sampleDataSet.TotalSamples}개)");
                PerformanceLogger.Instance.LogInfo("NT-Curve 시험 차트 생성 완료", "TestMode");
                UpdateWindowCount();
                
                WelcomeMessage.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"NT-Curve 시험 모드 오류: {ex.Message}", "TestMode");
                MessageBox.Show($"NT-Curve 시험 차트 생성 중 오류 발생:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion // 시험 유형 이벤트 핸들러

        #region 윈도우 관리
        private void CascadeWindows_Click(object sender, RoutedEventArgs e)
        {
            if (_mdiWindowManager == null) return;
            _mdiWindowManager.CascadeWindows();
            UpdateStatusBar("윈도우를 계단식으로 배치했습니다.");
        }

        private void TileWindows_Click(object sender, RoutedEventArgs e)
        {
            if (_mdiWindowManager == null) return;
            _mdiWindowManager.TileWindows();
            UpdateStatusBar("윈도우를 타일로 배치했습니다.");
        }

        private void MinimizeAll_Click(object sender, RoutedEventArgs e)
        {
            if (_mdiWindowManager == null) return;
            _mdiWindowManager.MinimizeAll();
            UpdateStatusBar("모든 윈도우를 최소화했습니다.");
        }

        private void UpdateWindowCount()
        {
            if (WindowCountText == null) return;
            if (_mdiWindowManager != null)
            {
                _chartWindowCount = _mdiWindowManager.WindowCount;
            }
            else
            {
                _chartWindowCount = 0;
                if (MdiCanvas != null)
                {
                    foreach (var child in MdiCanvas.Children)
                    {
                        if (child is MdiChartWindow)
                            _chartWindowCount++;
                    }
                }
            }

            WindowCountText.Text = $"Windows: {_chartWindowCount}";

            if (WelcomeMessage != null)
            {
                WelcomeMessage.Visibility = _chartWindowCount == 0 ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void CreateContour2DChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mdiWindowManager == null)
                {
                    UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                    return;
                }
                UpdateStatusBar("효율맵2D 차트를 생성하고 있습니다...");
                PerformanceLogger.Instance.LogInfo("효율맵2D 차트 생성 시작", "Contour2D");

                var mdi = _mdiWindowManager.CreateContour2DWindow("효율맵2D - " + DateTime.Now.ToString("HH:mm:ss"));
                mdi.WindowClosed += (s, args) => UpdateWindowCount();

                UpdateStatusBar("효율맵2D 차트가 생성되었습니다.");
                PerformanceLogger.Instance.LogInfo("효율맵2D 차트 생성 완료", "Contour2D");
                UpdateWindowCount();

                WelcomeMessage.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"효율맵2D 차트 생성 오류: {ex.Message}", "Contour2D");
                MessageBox.Show($"효율맵2D 차트 생성 중 오류 발생:\n{ex.Message}", 
                              "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatusBar("효율맵2D 차트 생성 실패");
            }
        }

        private void CreateSurface3DChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_mdiWindowManager == null)
                {
                    UpdateStatusBar("MDI 초기화가 완료되지 않았습니다.");
                    return;
                }
                UpdateStatusBar("효율맵3D 차트를 생성하고 있습니다...");
                var mdi = _mdiWindowManager.CreateSurface3DWindow("효율맵3D - " + DateTime.Now.ToString("HH:mm:ss"));
                UpdateWindowCount();
                WelcomeMessage.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"효율맵3D 차트 생성 중 오류 발생:\n{ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        #endregion // 윈도우 관리
    }
}