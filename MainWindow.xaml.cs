﻿using System.Text;
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
using Microsoft.Win32;
using System.IO;

namespace MGK_Analyzer
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private AppSettings _settings;
        private bool _isFileExplorerExpanded = false;
        private bool _isFileExplorerPinned = false;
        private MdiWindowManager _mdiManager;
        private CsvDataLoader _csvLoader;
        private LogViewerWindow _logViewerWindow;

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            
            // 테마 변경 이벤트 구독
            ThemeManager.Instance.ThemeChanged += OnThemeChanged;
            
            // 설정 로드
            _settings = AppSettings.Load();
            _isFileExplorerPinned = _settings.IsFileExplorerPinned;
            
            // 서비스 초기화
            _csvLoader = new CsvDataLoader();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            using var timer = PerformanceLogger.Instance.StartTimer("MainWindow 로드", "Application");
            
            UpdateStatusBar("MGK Analyzer가 시작되었습니다.");
            
            // MDI 매니저 초기화
            _mdiManager = new MdiWindowManager(MdiCanvas);
            
            // 저장된 테마 적용
            ThemeManager.Instance.InitializeTheme();
            
            // 파일 탐색기 초기화
            InitializeFileExplorer();
            
            // 현재 테마 상태 표시
            var currentTheme = ThemeManager.Instance.GetThemeDisplayName(ThemeManager.Instance.CurrentTheme);
            UpdateStatusBar($"MGK Analyzer가 시작되었습니다. 현재 테마: {currentTheme}");
            
            // MDI 창 개수 업데이트
            UpdateWindowCount();
            
            PerformanceLogger.Instance.LogInfo("MainWindow 초기화 완료", "Application");
        }

        private void InitializeFileExplorer()
        {
            try
            {
                // 파일 트리뷰 초기화
                FileExplorerService.Instance.PopulateTreeView(FileTreeView);
                
                // 저장된 설정 복원
                if (_settings.FileExplorerWidth > 0)
                {
                    // 애니메이션 타겟 너비 업데이트
                    UpdateAnimationTargetWidth(_settings.FileExplorerWidth);
                }
                
                PinButton.IsChecked = _isFileExplorerPinned;
                
                if (_isFileExplorerPinned)
                {
                    ExpandFileExplorer(false); // 애니메이션 없이 확장
                }
                else
                {
                    // 초기 상태는 숨김
                    FileExplorerPanel.Width = 0;
                    FileExplorerTab.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusBar($"파일 탐색기 초기화 오류: {ex.Message}");
            }
        }

        private void UpdateAnimationTargetWidth(double width)
        {
            var slideInStoryboard = FindResource("SlideInStoryboard") as Storyboard;
            var slideOutStoryboard = FindResource("SlideOutStoryboard") as Storyboard;
            
            if (slideInStoryboard?.Children[0] is DoubleAnimation slideInAnimation)
            {
                slideInAnimation.To = width;
            }
            
            if (slideOutStoryboard?.Children[0] is DoubleAnimation slideOutAnimation)
            {
                slideOutAnimation.From = width;
            }
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

        #region 파일 탐색기 이벤트 핸들러

        private void FileExplorerTab_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isFileExplorerExpanded && !_isFileExplorerPinned)
            {
                ExpandFileExplorer(true);
                UpdateStatusBar("파일 탐색기를 열었습니다.");
            }
        }

        private void FileExplorerTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isFileExplorerExpanded)
            {
                ExpandFileExplorer(true);
                UpdateStatusBar("파일 탐색기를 열었습니다.");
            }
        }

        private void FileExplorerPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            // 마우스가 완전히 패널을 벗어났는지 확인
            var position = e.GetPosition(FileExplorerPanel);
            var bounds = new Rect(0, 0, FileExplorerPanel.ActualWidth, FileExplorerPanel.ActualHeight);
            
            if (_isFileExplorerExpanded && !_isFileExplorerPinned && !bounds.Contains(position))
            {
                // 약간의 지연을 두어 실수로 닫히는 것을 방지
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(300);
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    if (!_isFileExplorerPinned && !FileExplorerPanel.IsMouseOver)
                    {
                        CollapseFileExplorer(true);
                        UpdateStatusBar("파일 탐색기를 닫았습니다.");
                    }
                };
                timer.Start();
            }
        }

        private void PinButton_Checked(object sender, RoutedEventArgs e)
        {
            _isFileExplorerPinned = true;
            _settings.IsFileExplorerPinned = true;
            _settings.Save();
            
            if (!_isFileExplorerExpanded)
            {
                ExpandFileExplorer(true);
            }
            
            // GridSplitter 활성화
            EnableGridSplitter();
            
            UpdateStatusBar("파일 탐색기가 고정되었습니다.");
        }

        private void PinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isFileExplorerPinned = false;
            _settings.IsFileExplorerPinned = false;
            _settings.Save();
            
            // GridSplitter 비활성화
            DisableGridSplitter();
            
            UpdateStatusBar("파일 탐색기 고정이 해제되었습니다.");
        }

        private void EnableGridSplitter()
        {
            // 파일 탐색기 컬럼을 고정 너비로 설정
            var currentWidth = Math.Max(200, FileExplorerPanel.ActualWidth + 25); // 탭 너비 포함
            
            var parentGrid = (Grid)FileExplorerContainer.Parent;
            if (parentGrid != null)
            {
                parentGrid.ColumnDefinitions[0].Width = new GridLength(currentWidth);
                
                // GridSplitter 활성화
                FileExplorerSplitter.Visibility = Visibility.Visible;
                FileExplorerSplitter.IsEnabled = true;
            }
        }

        private void DisableGridSplitter()
        {
            // 컬럼을 Auto로 되돌리기
            var parentGrid = (Grid)FileExplorerContainer.Parent;
            if (parentGrid != null)
            {
                parentGrid.ColumnDefinitions[0].Width = GridLength.Auto;
                
                // GridSplitter 비활성화
                FileExplorerSplitter.Visibility = Visibility.Collapsed;
                FileExplorerSplitter.IsEnabled = false;
            }
        }

        private void ExpandFileExplorer(bool animated)
        {
            _isFileExplorerExpanded = true;
            
            // 화면 너비의 20%로 설정 (최소 200, 최대 400)
            var targetWidth = Math.Max(200, Math.Min(400, this.ActualWidth * 0.2));
            
            // 저장된 너비가 있으면 사용
            if (_settings.FileExplorerWidth > 0)
            {
                targetWidth = _settings.FileExplorerWidth;
            }
            
            if (animated)
            {
                UpdateAnimationTargetWidth(targetWidth);
                var storyboard = FindResource("SlideInStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    storyboard.Completed += (s, e) =>
                    {
                        // 애니메이션 완료 후 GridSplitter 활성화
                        if (_isFileExplorerPinned)
                        {
                            EnableGridSplitter();
                        }
                    };
                    storyboard.Begin();
                }
            }
            else
            {
                FileExplorerPanel.Width = targetWidth;
                if (_isFileExplorerPinned)
                {
                    EnableGridSplitter();
                }
            }
            
            // 탭 숨기기
            FileExplorerTab.Visibility = Visibility.Collapsed;
        }

        private void CollapseFileExplorer(bool animated)
        {
            _isFileExplorerExpanded = false;
            
            // 현재 너비 저장 (GridSplitter에 의해 변경된 경우)
            var parentGrid = (Grid)FileExplorerContainer.Parent;
            if (parentGrid != null && parentGrid.ColumnDefinitions[0].Width.IsAbsolute)
            {
                _settings.FileExplorerWidth = parentGrid.ColumnDefinitions[0].Width.Value - 25; // 탭 너비 제외
                _settings.Save();
            }
            else if (FileExplorerPanel.ActualWidth > 0)
            {
                _settings.FileExplorerWidth = FileExplorerPanel.ActualWidth;
                _settings.Save();
            }
            
            // GridSplitter 비활성화
            DisableGridSplitter();
            
            if (animated)
            {
                var storyboard = FindResource("SlideOutStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    var animation = storyboard.Children[0] as DoubleAnimation;
                    if (animation != null)
                    {
                        animation.From = FileExplorerPanel.ActualWidth;
                    }
                    storyboard.Completed += (s, e) => 
                    {
                        FileExplorerTab.Visibility = Visibility.Visible;
                    };
                    storyboard.Begin();
                }
            }
            else
            {
                FileExplorerPanel.Width = 0;
                FileExplorerTab.Visibility = Visibility.Visible;
            }
        }

        #endregion

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
            var openFileDialog = new OpenFileDialog
            {
                Title = "CSV 데이터 파일 선택",
                Filter = "CSV 파일 (*.csv)|*.csv|모든 파일 (*.*)|*.*",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (openFileDialog.ShowDialog() == true)
            {
                using var overallTimer = PerformanceLogger.Instance.StartTimer($"전체 데이터 가져오기: {System.IO.Path.GetFileName(openFileDialog.FileName)}", "Data_Import");
                
                // 파일 크기 확인
                var fileInfo = new FileInfo(openFileDialog.FileName);
                var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                
                try
                {
                    PerformanceLogger.Instance.LogInfo($"데이터 가져오기 시작: {openFileDialog.FileName} ({fileSizeMB:F1}MB)", "Data_Import");
                    UpdateStatusBar($"CSV 파일을 로딩하고 있습니다... (크기: {fileSizeMB:F1}MB)");
                    WelcomeMessage.Visibility = Visibility.Collapsed;

                    // 대용량 파일에 대한 경고
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

                    // 진행률 표시를 위한 프로그레스 핸들러
                    var progress = new Progress<int>(percent =>
                    {
                        var statusMessage = fileSizeMB > 5 
                            ? $"CSV 파일 로딩 중... {percent}% (크기: {fileSizeMB:F1}MB - 잠시만 기다려주세요)"
                            : $"CSV 파일 로딩 중... {percent}%";
                        UpdateStatusBar(statusMessage);
                    });

                    // 버튼 비활성화
                    ImportButton.IsEnabled = false;

                    // CSV 파일 로딩 (비동기)
                    MemoryOptimizedDataSet dataSet;
                    using (var csvTimer = PerformanceLogger.Instance.StartTimer("CSV 파일 로딩", "Data_Import"))
                    {
                        dataSet = await _csvLoader.LoadCsvDataAsync(openFileDialog.FileName, progress);
                    }

                    // MDI 차트 윈도우 생성
                    using (var chartTimer = PerformanceLogger.Instance.StartTimer("차트 윈도우 생성", "Data_Import"))
                    {
                        var chartWindow = _mdiManager.CreateChartWindow(dataSet.FileName, dataSet);
                        PerformanceLogger.Instance.LogInfo($"차트 윈도우 생성 완료: {dataSet.FileName}", "Data_Import");
                    }

                    var loadMessage = fileSizeMB > 5
                        ? $"'{dataSet.FileName}' 파일이 성공적으로 로드되었습니다. (크기: {fileSizeMB:F1}MB, 데이터: {dataSet.TotalSamples:N0}개, 시리즈: {dataSet.SeriesData.Count}개) - 다운샘플링 적용됨"
                        : $"'{dataSet.FileName}' 파일이 성공적으로 로드되었습니다. (데이터: {dataSet.TotalSamples:N0}개, 시리즈: {dataSet.SeriesData.Count}개)";
                        
                    UpdateStatusBar(loadMessage);
                    PerformanceLogger.Instance.LogInfo($"데이터 가져오기 완료: {dataSet.FileName}", "Data_Import");
                    UpdateWindowCount();
                }
                catch (Exception ex)
                {
                    PerformanceLogger.Instance.LogError($"데이터 가져오기 실패: {ex.Message}", "Data_Import");
                    MessageBox.Show($"CSV 파일 로딩 중 오류가 발생했습니다:\n\n{ex.Message}", 
                                  "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateStatusBar("CSV 파일 로딩 실패");
                }
                finally
                {
                    // 버튼 활성화
                    ImportButton.IsEnabled = true;
                }
            }
            else
            {
                UpdateStatusBar("CSV 파일 선택이 취소되었습니다.");
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

        #region MDI 윈도우 관리

        private void CascadeWindows_Click(object sender, RoutedEventArgs e)
        {
            _mdiManager?.CascadeWindows();
            UpdateStatusBar("윈도우를 계단식으로 정렬했습니다.");
        }

        private void TileWindows_Click(object sender, RoutedEventArgs e)
        {
            _mdiManager?.TileWindows();
            UpdateStatusBar("윈도우를 타일式으로 정렬했습니다.");
        }

        private void MinimizeAll_Click(object sender, RoutedEventArgs e)
        {
            _mdiManager?.MinimizeAll();
            UpdateStatusBar("모든 윈도우를 최소화했습니다.");
        }

        private void UpdateWindowCount()
        {
            if (WindowCountText != null && _mdiManager != null)
            {
                WindowCountText.Text = $"Windows: {_mdiManager.WindowCount}";
                
                // 윈도우가 없으면 환영 메시지 표시
                if (WelcomeMessage != null)
                {
                    if (_mdiManager.WindowCount == 0)
                    {
                        WelcomeMessage.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        WelcomeMessage.Visibility = Visibility.Collapsed;
                    }
                }
            }
        }

        #endregion
    }
}