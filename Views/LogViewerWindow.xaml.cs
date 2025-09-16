using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using MGK_Analyzer.Services;

namespace MGK_Analyzer.Views
{
    public partial class LogViewerWindow : Window
    {
        private CollectionViewSource _viewSource;
        private PerformanceLogger _logger;

        public LogViewerWindow()
        {
            InitializeComponent();
            
            _logger = PerformanceLogger.Instance;
            
            // CollectionViewSource 설정
            _viewSource = new CollectionViewSource
            {
                Source = _logger.LogEntries
            };
            
            DataContext = _logger;
            LogListBox.ItemsSource = _viewSource.View;
            
            // 로그 추가 시 자동 스크롤
            _logger.LogEntries.CollectionChanged += LogEntries_CollectionChanged;
            
            UpdateStatus();
        }

        private void LogEntries_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (AutoScrollCheckBox.IsChecked == true && e.Action == NotifyCollectionChangedAction.Add)
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (LogListBox.Items.Count > 0)
                    {
                        LogListBox.ScrollIntoView(LogListBox.Items[0]);
                    }
                });
            }
            
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            Dispatcher.BeginInvoke(() =>
            {
                var totalCount = _logger.LogEntries.Count;
                var visibleCount = _viewSource.View?.Cast<object>().Count() ?? 0;
                StatusText.Text = $"Total: {totalCount}, Visible: {visibleCount}";
            });
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            _logger.Clear();
            UpdateStatus();
        }

        private void FilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_viewSource == null) return;

            var selectedItem = FilterComboBox.SelectedItem as ComboBoxItem;
            var filterText = selectedItem?.Content?.ToString();

            if (filterText == "All")
            {
                _viewSource.View.Filter = null;
            }
            else
            {
                _viewSource.View.Filter = obj =>
                {
                    if (obj is LogEntry entry)
                    {
                        return entry.Level.ToString() == filterText;
                    }
                    return false;
                };
            }
            
            UpdateStatus();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            // 윈도우를 닫지 말고 숨김
            e.Cancel = true;
            this.Hide();
        }
    }
}