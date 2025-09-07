using System.Text;
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

        public MainWindow()
        {
            InitializeComponent();
            Loaded += MainWindow_Loaded;
            
            // 테마 변경 이벤트 구독
            ThemeManager.Instance.ThemeChanged += OnThemeChanged;
            
            // 설정 로드
            _settings = AppSettings.Load();
            _isFileExplorerPinned = _settings.IsFileExplorerPinned;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("MGK Analyzer가 시작되었습니다.");
            
            // 저장된 테마 적용
            ThemeManager.Instance.InitializeTheme();
            
            // 파일 탐색기 초기화
            InitializeFileExplorer();
            
            // 현재 테마 상태 표시
            var currentTheme = ThemeManager.Instance.GetThemeDisplayName(ThemeManager.Instance.CurrentTheme);
            UpdateStatusBar($"MGK Analyzer가 시작되었습니다. 현재 테마: {currentTheme}");
        }

        private void InitializeFileExplorer()
        {
            // 파일 트리뷰 초기화
            FileExplorerService.Instance.PopulateTreeView(FileTreeView);
            
            // 저장된 설정 복원
            FileExplorerPanel.Width = _settings.FileExplorerWidth;
            PinButton.IsChecked = _isFileExplorerPinned;
            
            if (_isFileExplorerPinned)
            {
                ExpandFileExplorer(false); // 애니메이션 없이 확장
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
            }
        }

        private void FileExplorerTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isFileExplorerExpanded)
            {
                ExpandFileExplorer(true);
            }
        }

        private void FileExplorerPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            if (_isFileExplorerExpanded && !_isFileExplorerPinned)
            {
                CollapseFileExplorer(true);
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
            FileExplorerSplitter.Visibility = Visibility.Visible;
            FileExplorerSplitter.IsEnabled = true;
            
            UpdateStatusBar("파일 탐색기가 고정되었습니다.");
        }

        private void PinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isFileExplorerPinned = false;
            _settings.IsFileExplorerPinned = false;
            _settings.Save();
            
            // GridSplitter 비활성화
            FileExplorerSplitter.Visibility = Visibility.Collapsed;
            FileExplorerSplitter.IsEnabled = false;
            
            UpdateStatusBar("파일 탐색기 고정이 해제되었습니다.");
        }

        private void ExpandFileExplorer(bool animated)
        {
            _isFileExplorerExpanded = true;
            
            // 화면 너비의 20%로 설정 (최소 150, 최대 300)
            var targetWidth = Math.Max(150, Math.Min(300, this.ActualWidth * 0.2));
            
            if (animated)
            {
                var storyboard = FindResource("SlideInStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    var animation = storyboard.Children[0] as DoubleAnimation;
                    if (animation != null)
                    {
                        animation.To = targetWidth;
                    }
                    storyboard.Begin();
                }
            }
            else
            {
                FileExplorerPanel.Width = targetWidth;
            }
            
            // 탭 숨기기
            FileExplorerTab.Visibility = Visibility.Collapsed;
        }

        private void CollapseFileExplorer(bool animated)
        {
            _isFileExplorerExpanded = false;
            
            // 현재 너비 저장
            _settings.FileExplorerWidth = FileExplorerPanel.Width;
            _settings.Save();
            
            if (animated)
            {
                var storyboard = FindResource("SlideOutStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    var animation = storyboard.Children[0] as DoubleAnimation;
                    if (animation != null)
                    {
                        animation.From = FileExplorerPanel.Width;
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
            UpdateStatusBar("새 파일을 만들었습니다.");
            MessageBox.Show("새 파일 기능이 실행되었습니다.", "파일", MessageBoxButton.OK, MessageBoxImage.Information);
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

        private void ImportData_Click(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar("데이터를 가져오고 있습니다...");
            MessageBox.Show("데이터 가져오기 기능이 실행되었습니다.", "데이터", MessageBoxButton.OK, MessageBoxImage.Information);
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
            MessageBox.Show("프로젝트 설정 기능이 실행되었습니다.", "프로젝트", MessageBoxButton.OK, MessageBoxImage.Information);
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

        #endregion
    }
}