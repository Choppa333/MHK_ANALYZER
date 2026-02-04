using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MGK_Analyzer.Services;

namespace MGK_Analyzer.Views
{
    public partial class ThemeSettingsWindow : Window
    {
        private ThemeManager.ThemeType _selectedTheme;
        private ThemeManager.ThemeType _originalTheme;

        public ThemeSettingsWindow()
        {
            InitializeComponent();
            _originalTheme = ThemeManager.Instance.CurrentTheme;
            _selectedTheme = _originalTheme;
            InitializeThemeList();
        }

        private void InitializeThemeList()
        {
            ThemeListPanel.Children.Clear();

            var availableThemes = ThemeManager.Instance.GetAvailableThemes();

            foreach (var theme in availableThemes)
            {
                var themeItem = CreateThemeItem(theme);
                ThemeListPanel.Children.Add(themeItem);
            }
        }

        private Border CreateThemeItem(ThemeManager.ThemeType theme)
        {
            var isSelected = theme == _selectedTheme;

            // Main container
            var border = new Border
            {
                Background = isSelected ? new SolidColorBrush(Color.FromRgb(230, 244, 255)) : Brushes.White,
                BorderBrush = isSelected ? new SolidColorBrush(Color.FromRgb(0, 120, 215)) : new SolidColorBrush(Color.FromRgb(200, 200, 200)),
                BorderThickness = new Thickness(isSelected ? 2 : 1),
                CornerRadius = new CornerRadius(5),
                Margin = new Thickness(0, 5, 0, 5),
                Padding = new Thickness(15),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            // Content panel
            var stackPanel = new StackPanel();

            // Theme name
            var nameText = new TextBlock
            {
                Text = ThemeManager.Instance.GetThemeDisplayName(theme),
                FontSize = 16,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                Foreground = isSelected ? new SolidColorBrush(Color.FromRgb(0, 120, 215)) : Brushes.Black
            };

            // Theme description
            var descriptionText = new TextBlock
            {
                Text = GetThemeDescription(theme),
                FontSize = 12,
                Foreground = Brushes.Gray,
                Margin = new Thickness(0, 5, 0, 0)
            };

            // Selection indicator
            if (isSelected)
            {
                var selectedText = new TextBlock
                {
                    Text = "? Selected for Preview",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 120, 215)),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                stackPanel.Children.Add(selectedText);
            }

            // Current theme indicator
            if (theme == _originalTheme && theme != _selectedTheme)
            {
                var currentText = new TextBlock
                {
                    Text = "● Currently Applied",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0, 150, 0)),
                    FontWeight = FontWeights.Bold,
                    Margin = new Thickness(0, 5, 0, 0)
                };
                stackPanel.Children.Add(currentText);
            }

            stackPanel.Children.Add(nameText);
            stackPanel.Children.Add(descriptionText);

            border.Child = stackPanel;

            // Click event - Preview theme immediately
            border.MouseLeftButtonDown += (s, e) =>
            {
                _selectedTheme = theme;
                ThemeManager.Instance.PreviewTheme(theme); // 즉시 미리보기 적용
                InitializeThemeList(); // Refresh list
            };

            // Hover effects
            border.MouseEnter += (s, e) =>
            {
                if (!isSelected)
                {
                    border.Background = new SolidColorBrush(Color.FromRgb(245, 245, 245));
                }
            };

            border.MouseLeave += (s, e) =>
            {
                if (!isSelected)
                {
                    border.Background = Brushes.White;
                }
            };

            return border;
        }

        private string GetThemeDescription(ThemeManager.ThemeType theme)
        {
            return theme switch
            {
                ThemeManager.ThemeType.MaterialLight => "Google Material Design light theme.",
                ThemeManager.ThemeType.MaterialDark => "Google Material Design dark theme.",
                _ => "No description available."
            };
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 선택한 테마를 실제로 적용하고 저장
                ThemeManager.Instance.ApplyTheme(_selectedTheme);
                _originalTheme = _selectedTheme;
                
                MessageBox.Show($"{ThemeManager.Instance.GetThemeDisplayName(_selectedTheme)} theme has been applied and saved.", 
                    "Theme Applied", MessageBoxButton.OK, MessageBoxImage.Information);
                
                // 적용 후 리스트 새로고침
                InitializeThemeList();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying theme: {ex.Message}", 
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // 원래 테마로 되돌리기
            ThemeManager.Instance.CancelThemeChange();
            _selectedTheme = _originalTheme;
            InitializeThemeList();
            
            MessageBox.Show("Theme changes have been cancelled and reverted to the original theme.", 
                "Changes Cancelled", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            // 창을 닫을 때 원래 테마로 되돌리기 (적용하지 않은 경우)
            if (_selectedTheme != _originalTheme)
            {
                var result = MessageBox.Show(
                    "You have preview changes that haven't been applied. Do you want to apply them?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ApplyButton_Click(sender, e);
                }
                else if (result == MessageBoxResult.No)
                {
                    ThemeManager.Instance.CancelThemeChange();
                }
                else // Cancel
                {
                    return; // Don't close the window
                }
            }

            Close();
        }

        // Window closing event handler
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (_selectedTheme != _originalTheme)
            {
                var result = MessageBox.Show(
                    "You have preview changes that haven't been applied. Do you want to apply them?",
                    "Unsaved Changes",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    ThemeManager.Instance.ApplyTheme(_selectedTheme);
                }
                else if (result == MessageBoxResult.No)
                {
                    ThemeManager.Instance.CancelThemeChange();
                }
                else // Cancel
                {
                    e.Cancel = true; // Cancel closing
                    return;
                }
            }

            base.OnClosing(e);
        }
    }
}