using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Charts;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Controls
{
    public partial class Mdi3DSurfaceWindow : UserControl, INotifyPropertyChanged
    {
        #region Dependency Properties
        
        public static readonly DependencyProperty WindowTitleProperty =
            DependencyProperty.Register("WindowTitle", typeof(string), typeof(Mdi3DSurfaceWindow),
                new PropertyMetadata("3D Surface Chart"));

        public string WindowTitle
        {
            get { return (string)GetValue(WindowTitleProperty); }
            set { SetValue(WindowTitleProperty, value); }
        }

        public static readonly DependencyProperty SeriesListProperty =
            DependencyProperty.Register("SeriesList", typeof(ObservableCollection<Surface3DSeriesViewModel>), typeof(Mdi3DSurfaceWindow),
                new PropertyMetadata(new ObservableCollection<Surface3DSeriesViewModel>()));

        public ObservableCollection<Surface3DSeriesViewModel> SeriesList
        {
            get { return (ObservableCollection<Surface3DSeriesViewModel>)GetValue(SeriesListProperty); }
            set { SetValue(SeriesListProperty, value); }
        }

        // SfSurfaceChart를 위한 새로운 속성들
        public static readonly DependencyProperty DataValuesProperty =
            DependencyProperty.Register("DataValues", typeof(ObservableCollection<Surface3DPoint>), typeof(Mdi3DSurfaceWindow),
                new PropertyMetadata(new ObservableCollection<Surface3DPoint>()));

        public ObservableCollection<Surface3DPoint> DataValues
        {
            get { return (ObservableCollection<Surface3DPoint>)GetValue(DataValuesProperty); }
            set { SetValue(DataValuesProperty, value); }
        }

        public static readonly DependencyProperty RowSizeProperty =
            DependencyProperty.Register("RowSize", typeof(int), typeof(Mdi3DSurfaceWindow),
                new PropertyMetadata(20));

        public int RowSize
        {
            get { return (int)GetValue(RowSizeProperty); }
            set { SetValue(RowSizeProperty, value); }
        }

        public static readonly DependencyProperty ColumnSizeProperty =
            DependencyProperty.Register("ColumnSize", typeof(int), typeof(Mdi3DSurfaceWindow),
                new PropertyMetadata(20));

        public int ColumnSize
        {
            get { return (int)GetValue(ColumnSizeProperty); }
            set { SetValue(ColumnSizeProperty, value); }
        }

        #endregion

        #region Events

        public event EventHandler<EventArgs>? WindowClosed;
        public event EventHandler<EventArgs>? WindowMinimized;
        public event EventHandler<EventArgs>? WindowMaximized;
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Private Fields

        private bool _isResizing = false;
        private Point _startPoint;
        private Size _originalSize;
        private bool _isPanelPinned = false;

        #endregion

        public Mdi3DSurfaceWindow()
        {
            InitializeComponent();
            DataContext = this;
            
            // Initialize SeriesList
            SeriesList = new ObservableCollection<Surface3DSeriesViewModel>();
            
            // Initialize with sample data
            InitializeSampleData();
            
            // Subscribe to SeriesList changes
            SeriesList.CollectionChanged += SeriesList_CollectionChanged;
        }

        #region Sample Data Initialization

        private void InitializeSampleData()
        {
            try
            {
                // SfSurfaceChart용 데이터 초기화
                RowSize = 20;
                ColumnSize = 20;
                
                // Surface 데이터 생성
                var surfaceData = Surface3DDataGenerator.GenerateSimpleSurface(RowSize, ColumnSize);
                DataValues = surfaceData;

                // SeriesList도 유지 (UI 표시용)
                var simpleSurfaceData = new Surface3DSeriesData("효율맵 Surface", "효율")
                {
                    Color = Colors.Blue,
                    SurfaceType = "Surface",
                    DataPoints = surfaceData
                };
                
                var simpleSurfaceViewModel = new Surface3DSeriesViewModel(simpleSurfaceData);
                SeriesList.Add(simpleSurfaceViewModel);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Sample data initialization failed: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SeriesList_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            RefreshChart();
        }

        private void RefreshChart()
        {
            try
            {
                if (SurfaceChartControl == null) return;

                // 첫 번째 선택된 시리즈의 데이터를 사용
                var selectedSeries = SeriesList.FirstOrDefault(s => s.IsSelected && s.SeriesData.IsVisible);
                if (selectedSeries != null)
                {
                    DataValues = selectedSeries.SeriesData.DataPoints;
                    // RowSize와 ColumnSize는 데이터 생성 시 설정됨
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Chart refresh failed: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Window Controls

        public void Close_Click(object? sender, RoutedEventArgs? e)
        {
            WindowClosed?.Invoke(this, EventArgs.Empty);
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            WindowMinimized?.Invoke(this, EventArgs.Empty);
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            WindowMaximized?.Invoke(this, EventArgs.Empty);
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
        }

        #endregion

        #region Size Management

        private void SetSmallSize_Click(object sender, RoutedEventArgs e)
        {
            Width = 400;
            Height = 300;
        }

        private void SetMediumSize_Click(object sender, RoutedEventArgs e)
        {
            Width = 800;
            Height = 600;
        }

        private void SetLargeSize_Click(object sender, RoutedEventArgs e)
        {
            Width = 1200;
            Height = 800;
        }

        private void SetHDSize_Click(object sender, RoutedEventArgs e)
        {
            Width = 1920;
            Height = 1080;
        }

        private void ShowCustomSizeDialog_Click(object sender, RoutedEventArgs e)
        {
            // Custom size dialog - to be implemented
        }

        private void ApplySize_Click(object sender, RoutedEventArgs e)
        {
            if (double.TryParse(WidthTextBox.Text, out double width) && 
                double.TryParse(HeightTextBox.Text, out double height))
            {
                Width = width;
                Height = height;
            }
        }

        private void ResetSize_Click(object sender, RoutedEventArgs e)
        {
            Width = 800;
            Height = 600;
        }

        #endregion

        #region Surface Chart Controls

        private void RotationSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (SurfaceChartControl != null)
            {
                SurfaceChartControl.Rotate = e.NewValue;
            }
        }

        private void TiltSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // SfSurfaceChart는 Tilt 속성이 없으므로 무시하거나 다른 속성으로 대체
        }

        private void DepthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            // SfSurfaceChart는 Depth 속성이 없으므로 무시하거나 다른 속성으로 대체
        }

        private void EnableRotationCheckBox_Checked(object sender, RoutedEventArgs e)
        {
            // SfSurfaceChart는 항상 회전 가능하므로 별도 설정 불필요
        }

        private void EnableRotationCheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            // SfSurfaceChart는 항상 회전 가능하므로 별도 설정 불필요
        }

        #endregion

        #region Panel Management

        private void PanelPinButton_Checked(object sender, RoutedEventArgs e)
        {
            _isPanelPinned = true;
        }

        private void PanelPinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isPanelPinned = false;
        }

        private void ManagementPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            if (!_isPanelPinned)
            {
                var storyboard = (Storyboard)Resources["PanelSlideOutStoryboard"];
                storyboard.Begin();
                ManagementPanelTab.Visibility = Visibility.Visible;
            }
        }

        private void ManagementPanelTab_MouseEnter(object sender, MouseEventArgs e)
        {
            var storyboard = (Storyboard)Resources["PanelSlideInStoryboard"];
            storyboard.Begin();
            ManagementPanelTab.Visibility = Visibility.Collapsed;
        }

        private void ManagementPanelTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var storyboard = (Storyboard)Resources["PanelSlideInStoryboard"];
            storyboard.Begin();
            ManagementPanelTab.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Resize Handlers

        private void ResizeTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "Top");
        }

        private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "Bottom");
        }

        private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "Left");
        }

        private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "Right");
        }

        private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "TopLeft");
        }

        private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "TopRight");
        }

        private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "BottomLeft");
        }

        private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(sender, e, "BottomRight");
        }

        private void StartResize(object sender, MouseButtonEventArgs e, string direction)
        {
            _isResizing = true;
            _startPoint = e.GetPosition(this);
            _originalSize = new Size(ActualWidth, ActualHeight);
            
            FrameworkElement element = (FrameworkElement)sender;
            element.CaptureMouse();
            element.MouseMove += (s, args) => HandleResize(args, direction);
            element.MouseLeftButtonUp += (s, args) => StopResize(element);
        }

        private void HandleResize(MouseEventArgs e, string direction)
        {
            if (!_isResizing) return;

            Point currentPoint = e.GetPosition(this);
            double deltaX = currentPoint.X - _startPoint.X;
            double deltaY = currentPoint.Y - _startPoint.Y;

            double newWidth = _originalSize.Width;
            double newHeight = _originalSize.Height;

            switch (direction)
            {
                case "Right":
                    newWidth = Math.Max(200, _originalSize.Width + deltaX);
                    break;
                case "Left":
                    newWidth = Math.Max(200, _originalSize.Width - deltaX);
                    break;
                case "Bottom":
                    newHeight = Math.Max(150, _originalSize.Height + deltaY);
                    break;
                case "Top":
                    newHeight = Math.Max(150, _originalSize.Height - deltaY);
                    break;
                case "BottomRight":
                    newWidth = Math.Max(200, _originalSize.Width + deltaX);
                    newHeight = Math.Max(150, _originalSize.Height + deltaY);
                    break;
                case "TopLeft":
                    newWidth = Math.Max(200, _originalSize.Width - deltaX);
                    newHeight = Math.Max(150, _originalSize.Height - deltaY);
                    break;
                case "TopRight":
                    newWidth = Math.Max(200, _originalSize.Width + deltaX);
                    newHeight = Math.Max(150, _originalSize.Height - deltaY);
                    break;
                case "BottomLeft":
                    newWidth = Math.Max(200, _originalSize.Width - deltaX);
                    newHeight = Math.Max(150, _originalSize.Height + deltaY);
                    break;
            }

            Width = newWidth;
            Height = newHeight;
        }

        private void StopResize(FrameworkElement? element)
        {
            _isResizing = false;
            element?.ReleaseMouseCapture();
        }

        #endregion

        #region 3D Surface Management

        public void AddSurfaceSeries(Surface3DSeriesData seriesData)
        {
            try
            {
                var viewModel = new Surface3DSeriesViewModel(seriesData);
                SeriesList.Add(viewModel);
                
                // Subscribe to property changes
                viewModel.PropertyChanged += SeriesViewModel_PropertyChanged;
                seriesData.PropertyChanged += SeriesData_PropertyChanged;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add surface series: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void RemoveSurfaceSeries(Surface3DSeriesViewModel seriesViewModel)
        {
            try
            {
                if (SeriesList.Contains(seriesViewModel))
                {
                    // Unsubscribe from events
                    seriesViewModel.PropertyChanged -= SeriesViewModel_PropertyChanged;
                    seriesViewModel.SeriesData.PropertyChanged -= SeriesData_PropertyChanged;
                    
                    SeriesList.Remove(seriesViewModel);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to remove surface series: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void AddSampleSurfaces()
        {
            try
            {
                // 다른 타입의 Surface 추가
                var gaussianData = new Surface3DSeriesData("가우시안 Surface", "강도")
                {
                    Color = Colors.Green,
                    DataPoints = Surface3DDataGenerator.GenerateGaussian(15.0, 3.0, 3.0, RowSize, ColumnSize)
                };
                AddSurfaceSeries(gaussianData);
                
                // 새로 추가된 Surface로 차트 업데이트
                RefreshChart();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to add sample surfaces: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SeriesViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Surface3DSeriesViewModel.IsSelected))
            {
                RefreshChart();
            }
        }

        private void SeriesData_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Surface3DSeriesData.IsVisible) ||
                e.PropertyName == nameof(Surface3DSeriesData.Color) ||
                e.PropertyName == nameof(Surface3DSeriesData.Name))
            {
                RefreshChart();
            }
        }

        #endregion

        #region Chart Interaction

        public void ResetView()
        {
            try
            {
                if (SurfaceChartControl != null)
                {
                    SurfaceChartControl.Rotate = 43;
                    
                    // Update sliders
                    if (RotationSlider != null) RotationSlider.Value = 43;
                    if (TiltSlider != null) TiltSlider.Value = 20;  // UI만 업데이트
                    if (DepthSlider != null) DepthSlider.Value = 100; // UI만 업데이트
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to reset view: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void ExportChart()
        {
            try
            {
                // 3D 차트 내보내기 기능 (추후 구현 예정)
                MessageBox.Show("Export functionality will be implemented in the next phase.", 
                               "Export Chart", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error", 
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Button Event Handlers

        private void AddSampleSurfaces_Click(object sender, RoutedEventArgs e)
        {
            AddSampleSurfaces();
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            ResetView();
        }

        private void ExportChart_Click(object sender, RoutedEventArgs e)
        {
            ExportChart();
        }

        #endregion

        #region INotifyPropertyChanged

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        #endregion
    }
}