using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Windows
{
    public partial class Surface3DWindow : Window, INotifyPropertyChanged
    {
        private string _windowTitle = "3D Surface Chart";
        private ObservableCollection<Surface3DPoint> _dataValues = new();
        private int _rowSize = 20;
        private int _columnSize = 20;

        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Surface3DPoint> DataValues
        {
            get => _dataValues;
            set { _dataValues = value; OnPropertyChanged(); }
        }

        public int RowSize
        {
            get => _rowSize;
            set { _rowSize = value; OnPropertyChanged(); }
        }

        public int ColumnSize
        {
            get => _columnSize;
            set { _columnSize = value; OnPropertyChanged(); }
        }

        public ObservableCollection<Surface3DSeriesViewModel> SeriesList { get; set; } = new();

        public Surface3DWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        public void SetDataPoints(System.Collections.Generic.List<Surface3DPoint> dataPoints)
        {
            if (dataPoints != null && dataPoints.Any())
            {
                var syncfusionData = new ObservableCollection<Surface3DPoint>();
                foreach (var point in dataPoints)
                {
                    syncfusionData.Add(new Surface3DPoint(point.X, point.Y, point.Z));
                }
                DataValues = syncfusionData;

                var xCount = dataPoints.Select(p => p.X).Distinct().Count();
                var yCount = dataPoints.Select(p => p.Y).Distinct().Count();

                if (xCount > 1 && yCount > 1 && xCount * yCount >= dataPoints.Count)
                {
                    RowSize = yCount;
                    ColumnSize = xCount;
                }
                else
                {
                    var size = (int)Math.Ceiling(Math.Sqrt(dataPoints.Count));
                    RowSize = size;
                    ColumnSize = size;
                }
            }
        }

        private void RotationSlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (SurfaceChartControl != null)
            {
                SurfaceChartControl.Rotate = e.NewValue;
            }
        }

        private void AddSampleSurfaces_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var gaussianData = new Surface3DSeriesData("가우시안 Surface", "강도")
                {
                    Color = Colors.Green,
                    DataPoints = Surface3DDataGenerator.GenerateGaussian(15.0, 3.0, 3.0, RowSize, ColumnSize)
                };
                
                var viewModel = new Surface3DSeriesViewModel(gaussianData);
                SeriesList.Add(viewModel);
                
                MessageBox.Show("Sample surface added successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add sample surface: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            if (SurfaceChartControl != null && RotationSlider != null)
            {
                SurfaceChartControl.Rotate = 43;
                RotationSlider.Value = 43;
            }
        }

        private void ExportChart_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Export functionality will be implemented in the next phase.", 
                           "Export Chart", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
