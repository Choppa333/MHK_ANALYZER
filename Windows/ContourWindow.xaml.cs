using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Windows
{
    public partial class ContourWindow : Window, INotifyPropertyChanged
    {
        private string _windowTitle = "Contour Chart";
        public List<Surface3DPoint> DataPoints { get; set; } = new List<Surface3DPoint>();

        public string WindowTitle
        {
            get => _windowTitle;
            set { _windowTitle = value; OnPropertyChanged(); }
        }

        public ContourWindow()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void ContourWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeChartWithData();
        }

        private void InitializeChartWithData()
        {
            if (DataPoints == null || DataPoints.Count == 0)
            {
                DataPoints = new List<Surface3DPoint>();
                for (int i = 0; i < 20; i++)
                {
                    for (int j = 0; j < 20; j++)
                    {
                        DataPoints.Add(new Surface3DPoint(i, j, Math.Sin(i * 0.5) * Math.Cos(j * 0.5)));
                    }
                }
            }

            CreateContourChart();
            UpdateDataInfo();
        }

        private void CreateContourChart()
        {
            if (DataPoints == null || DataPoints.Count == 0) return;
            
            try
            {
                var validPoints = DataPoints.Where(p => !double.IsNaN(p.Z) && !double.IsInfinity(p.Z)).ToList();
                
                if (validPoints.Count == 0)
                {
                    MessageBox.Show("Valid data points not found. All Z values are NaN or Infinity.", 
                                  "Invalid Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                ContourChart.ItemsSource = validPoints;
                
                var uniqueX = validPoints.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
                var uniqueY = validPoints.Select(p => p.Y).Distinct().OrderBy(y => y).ToList();
                
                ContourChart.RowSize = uniqueY.Count;
                ContourChart.ColumnSize = uniqueX.Count;
                
                ApplyColorPalette();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error creating contour chart: {ex.Message}", 
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ApplyColorPalette()
        {
            // SfSurfaceChart는 자체 ColorBar를 사용
        }

        private void UpdateDataInfo()
        {
            if (DataPoints != null && DataPoints.Count > 0)
            {
                DataPointsCountText.Text = $"Data Points: {DataPoints.Count}";
                var minZ = DataPoints.Min(p => p.Z);
                var maxZ = DataPoints.Max(p => p.Z);
                ValueRangeText.Text = $"Value Range: {minZ:F2} ~ {maxZ:F2}";
            }
        }

        private void ColorPalette_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && DataPoints != null && DataPoints.Count > 0)
            {
                ApplyColorPalette();
            }
        }

        private void ContourLevels_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Contour levels can be updated here if needed
        }

        private void ShowLabels_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded && ContourChart?.ColorBar != null)
            {
                ContourChart.ColorBar.ShowLabel = ShowLabelsCheckBox?.IsChecked ?? true;
            }
        }

        private void ShowLegend_Changed(object sender, RoutedEventArgs e)
        {
            if (IsLoaded && ContourChart?.ColorBar != null)
            {
                var visibility = ShowLegendCheckBox?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                if (ContourChart.ColorBar is FrameworkElement colorBar)
                {
                    colorBar.Visibility = visibility;
                }
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            CreateContourChart();
        }

        private void ExportChart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var saveDialog = new Microsoft.Win32.SaveFileDialog
                {
                    Filter = "PNG Image|*.png|JPEG Image|*.jpg",
                    FileName = $"ContourChart_{DateTime.Now:yyyyMMdd_HHmmss}"
                };

                if (saveDialog.ShowDialog() == true)
                {
                    var renderBitmap = new RenderTargetBitmap(
                        (int)ContourChart.ActualWidth,
                        (int)ContourChart.ActualHeight,
                        96, 96,
                        PixelFormats.Pbgra32);

                    renderBitmap.Render(ContourChart);

                    BitmapEncoder encoder;
                    if (saveDialog.FileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
                    {
                        encoder = new JpegBitmapEncoder();
                    }
                    else
                    {
                        encoder = new PngBitmapEncoder();
                    }

                    encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

                    using (var fileStream = new System.IO.FileStream(saveDialog.FileName, System.IO.FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }

                    MessageBox.Show("Chart exported successfully!", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
