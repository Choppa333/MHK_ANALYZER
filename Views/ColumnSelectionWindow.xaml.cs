using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Views
{
    public partial class ColumnSelectionWindow : Window, INotifyPropertyChanged
    {
        public ObservableCollection<SeriesSelectionOption> Options { get; } = new ObservableCollection<SeriesSelectionOption>();

        public ColumnSelectionWindow(IEnumerable<SeriesData> seriesItems, IEnumerable<string>? preselected = null)
        {
            InitializeComponent();
            DataContext = this;

            var preselectedSet = new HashSet<string>(preselected ?? Enumerable.Empty<string>(), System.StringComparer.OrdinalIgnoreCase);
            var defaultSelectAll = !preselectedSet.Any();

            foreach (var series in seriesItems.OrderBy(s => s.Name))
            {
                var isSelected = defaultSelectAll || preselectedSet.Contains(series.Name);
                var option = new SeriesSelectionOption(series.Name, series.Unit, isSelected);
                option.PropertyChanged += Option_PropertyChanged;
                Options.Add(option);
            }

            OnPropertyChanged(nameof(HasSelection));
        }

        public bool HasSelection => Options.Any(o => o.IsSelected);

        public IEnumerable<string> SelectedSeries => Options.Where(o => o.IsSelected).Select(o => o.Name);

        private void Option_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SeriesSelectionOption.IsSelected))
            {
                OnPropertyChanged(nameof(HasSelection));
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAll(true);
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            SetAll(false);
        }

        private void SetAll(bool value)
        {
            foreach (var option in Options)
            {
                option.IsSelected = value;
            }

            OnPropertyChanged(nameof(HasSelection));
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            if (!HasSelection)
            {
                MessageBox.Show("표시할 컬럼을 하나 이상 선택해야 합니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class SeriesSelectionOption : INotifyPropertyChanged
    {
        private bool _isSelected;

        public SeriesSelectionOption(string name, string unit, bool isSelected)
        {
            Name = name;
            Unit = unit;
            _isSelected = isSelected;
        }

        public string Name { get; }

        public string Unit { get; }

        public string DisplayName => string.IsNullOrWhiteSpace(Unit) ? Name : $"{Name} ({Unit})";

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
