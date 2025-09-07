using System;
using System.Collections.Generic;
using System.Windows;
using Syncfusion.SfSkinManager;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Services
{
    public class ThemeManager
    {
        public enum ThemeType
        {
            MaterialLight,
            MaterialDark,
            FluentLight,
            Default
        }

        private static ThemeManager? _instance;
        public static ThemeManager Instance => _instance ??= new ThemeManager();

        public event EventHandler<ThemeType>? ThemeChanged;

        private ThemeType _currentTheme = ThemeType.MaterialLight;
        private ThemeType _originalTheme = ThemeType.MaterialLight;
        private AppSettings _settings;

        private ThemeManager()
        {
            _settings = AppSettings.Load();
            _currentTheme = _settings.SelectedTheme;
            _originalTheme = _currentTheme;
        }

        public ThemeType CurrentTheme
        {
            get => _currentTheme;
            private set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    ThemeChanged?.Invoke(this, value);
                }
            }
        }

        public void InitializeTheme()
        {
            // �� ���� �� ����� �׸� ����
            ApplyThemeInternal(_settings.SelectedTheme);
            _originalTheme = _settings.SelectedTheme;
        }

        public void PreviewTheme(ThemeType theme)
        {
            // �̸������ - ���� �������� ����
            ApplyThemeInternal(theme);
        }

        public void ApplyTheme(ThemeType theme)
        {
            // ���� �׸� ���� �� ����
            ApplyThemeInternal(theme);
            _settings.SelectedTheme = theme;
            _settings.Save();
            _originalTheme = theme;
        }

        public void CancelThemeChange()
        {
            // ���� �׸��� �ǵ�����
            ApplyThemeInternal(_originalTheme);
        }

        private void ApplyThemeInternal(ThemeType theme)
        {
            try
            {
                if (Application.Current?.MainWindow == null)
                    return;

                switch (theme)
                {
                    case ThemeType.MaterialLight:
                        SfSkinManager.SetTheme(Application.Current.MainWindow, new Theme("MaterialLight"));
                        break;
                    case ThemeType.MaterialDark:
                        SfSkinManager.SetTheme(Application.Current.MainWindow, new Theme("MaterialDark"));
                        break;
                    case ThemeType.FluentLight:
                        SfSkinManager.SetTheme(Application.Current.MainWindow, new Theme("FluentLight"));
                        break;
                    case ThemeType.Default:
                    default:
                        // �׸� ���� (�⺻ �׸���)
                        SfSkinManager.SetTheme(Application.Current.MainWindow, new Theme("Default"));
                        break;
                }

                CurrentTheme = theme;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"�׸� ���� �� ������ �߻��߽��ϴ�: {ex.Message}", "�׸� ����", 
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        public string GetThemeDisplayName(ThemeType theme)
        {
            return theme switch
            {
                ThemeType.MaterialLight => "Material Light",
                ThemeType.MaterialDark => "Material Dark",
                ThemeType.FluentLight => "Fluent Light",
                ThemeType.Default => "�⺻ �׸�",
                _ => "�� �� ����"
            };
        }

        public List<ThemeType> GetAvailableThemes()
        {
            return new List<ThemeType>
            {
                ThemeType.Default,
                ThemeType.MaterialLight,
                ThemeType.MaterialDark,
                ThemeType.FluentLight
            };
        }
    }
}