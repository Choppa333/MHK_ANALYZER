using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
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

        #region Public Properties

        /// <summary>
        /// 외부에서 전달받은 3D 데이터 포인트 리스트입니다.
        /// </summary>
        public List<Surface3DPoint> DataPoints { get; set; } = new List<Surface3DPoint>();

        #endregion

        #region Events

        public event EventHandler<EventArgs>? WindowClosed;
        public event EventHandler<EventArgs>? WindowMinimized;
        public event EventHandler<EventArgs>? WindowMaximized;
        public event PropertyChangedEventHandler? PropertyChanged;

        #endregion

        #region Private Fields

        private bool _isResizing = false;
        private bool _isDragging = false;
        private Point _startPoint;
        private ResizeDirection _resizeDirection = ResizeDirection.None;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private double _resizeStartLeft;
        private double _resizeStartTop;
        private bool _isPanelPinned = false;
        private static int _globalZIndex = 1;  // deprecated local counter; using MdiZOrderService instead
        
        // 윈도우 상태 관련 필드
        private double _normalWidth = 1000;
        private double _normalHeight = 700;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private bool _isMaximized = false;
        private bool _isMinimized = false;

        #endregion

        public Mdi3DSurfaceWindow()
        {
            InitializeComponent();
            DataContext = this;
            // Bring to front on any mouse down inside the control
            this.PreviewMouseDown += (_, __) => MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
            
            // Initialize SeriesList
            SeriesList = new ObservableCollection<Surface3DSeriesViewModel>();
            
            // Loaded 이벤트 핸들러 연결
            Loaded += Mdi3DSurfaceWindow_Loaded;
            
            // Subscribe to SeriesList changes
            SeriesList.CollectionChanged += SeriesList_CollectionChanged;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas?)this.Parent;
                if (canvas == null) return;

                var currentPosition = e.GetPosition(canvas);
                var deltaX = currentPosition.X - _startPoint.X;
                var deltaY = currentPosition.Y - _startPoint.Y;
                
                var newLeft = Canvas.GetLeft(this) + deltaX;
                var newTop = Canvas.GetTop(this) + deltaY;
                
                // 경계 체크
                newLeft = Math.Max(0, Math.Min(newLeft, canvas.ActualWidth - this.ActualWidth));
                newTop = Math.Max(0, Math.Min(newTop, canvas.ActualHeight - this.ActualHeight));
                
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
                
                _startPoint = currentPosition;
            }
            
            if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas?)this.Parent;
                if (canvas == null) return;

                var currentPosition = e.GetPosition(canvas);
                PerformResize(currentPosition);
            }

            base.OnMouseMove(e);
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            if (_isDragging || _isResizing)
            {
                _isDragging = false;
                _isResizing = false;
                _resizeDirection = ResizeDirection.None;
                this.ReleaseMouseCapture();
            }
            base.OnMouseLeftButtonUp(e);
        }

        private void Mdi3DSurfaceWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // 전달받은 데이터가 있는지 확인
            if (DataPoints != null && DataPoints.Any())
            {
                InitializeChartWithData();
            }
            else
            {
                // 데이터가 없으면 기본 샘플 데이터 생성
                InitializeSampleData();
            }
        }

        private void InitializeChartWithData()
        {
            try
            {
                // 전달받은 데이터를 Syncfusion Surface3DPoint로 변환
                var syncfusionData = new ObservableCollection<Surface3DPoint>();
                foreach (var point in DataPoints)
                {
                    syncfusionData.Add(new Surface3DPoint(point.X, point.Y, point.Z));
                }
                DataValues = syncfusionData;

                // 데이터의 X, Y 축 고유 값 개수를 계산하여 RowSize와 ColumnSize 설정
                var xCount = DataPoints.Select(p => p.X).Distinct().Count();
                var yCount = DataPoints.Select(p => p.Y).Distinct().Count();

                if (xCount > 1 && yCount > 1 && xCount * yCount >= DataPoints.Count)
                {
                    RowSize = yCount;
                    ColumnSize = xCount;
                }
                else
                {
                    var size = (int)Math.Ceiling(Math.Sqrt(DataPoints.Count));
                    RowSize = size;
                    ColumnSize = size;
                }

                OnPropertyChanged(nameof(DataValues));
                OnPropertyChanged(nameof(RowSize));
                OnPropertyChanged(nameof(ColumnSize));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"차트 데이터 처리 중 오류 발생: {ex.Message}", "Error",
                               MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            if (_isMinimized)
            {
                // 최소화 해제 - 이전 크기로 복원
                RestoreNormalSize();
            }
            else
            {
                // 최소화 - 타이틀바만 보이도록 (MdiChartWindow와 동일한 높이)
                SaveCurrentSize();
                this.Height = 30;
                _isMinimized = true;
                _isMaximized = false;
            }
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas?)this.Parent;
            if (canvas == null) return;
            
            if (_isMaximized)
            {
                // 최대화 해제 - 기본 크기로 복원
                RestoreNormalSize();
            }
            else
            {
                // 최대화
                SaveCurrentSize();
                this.Width = canvas.ActualWidth - 10;
                this.Height = canvas.ActualHeight - 10;
                Canvas.SetLeft(this, 5);
                Canvas.SetTop(this, 5);
                _isMaximized = true;
                _isMinimized = false;
            }
        }
        
        private void SaveCurrentSize()
        {
            if (!_isMaximized && !_isMinimized)
            {
                _normalWidth = this.ActualWidth;
                _normalHeight = this.ActualHeight;
                _normalLeft = Canvas.GetLeft(this);
                _normalTop = Canvas.GetTop(this);
            }
        }
        
        private void RestoreNormalSize()
        {
            this.Width = _normalWidth;
            this.Height = _normalHeight;
            Canvas.SetLeft(this, _normalLeft);
            Canvas.SetTop(this, _normalTop);
            _isMaximized = false;
            _isMinimized = false;
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 버튼 클릭인지 확인 (버튼이면 드래그하지 않음)
            var element = e.OriginalSource as FrameworkElement;
            while (element != null)
            {
                if (element is Button)
                {
                    // 버튼 클릭이면 드래그 처리하지 않음
                    return;
                }
                element = element.Parent as FrameworkElement;
            }

            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                if (Parent is Canvas canvas)
                {
                    // Bring to front
                    MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
                    _startPoint = e.GetPosition(canvas);
                    _isDragging = true;
                    this.CaptureMouse();
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Size Management

        private void ApplySize_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (double.TryParse(WidthTextBox.Text, out double width) && 
                    double.TryParse(HeightTextBox.Text, out double height))
                {
                    SetWindowSize(width, height);
                }
                else
                {
                    MessageBox.Show("올바른 숫자를 입력해주세요.", "크기 설정", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"크기 설정 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(800, 600);
        }

        private void SetSmallSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(400, 300);
        }

        private void SetMediumSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(800, 600);
        }

        private void SetLargeSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(1200, 800);
        }

        private void SetHDSize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                // 캔버스 크기를 고려하여 적절히 조절
                var maxWidth = Math.Min(1920, canvas.ActualWidth - 50);
                var maxHeight = Math.Min(1080, canvas.ActualHeight - 50);
                SetWindowSize(maxWidth, maxHeight);
            }
            else
            {
                SetWindowSize(1200, 800); // 캔버스가 없으면 기본 크기
            }
        }

        private void ShowCustomSizeDialog_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CustomSizeDialog(this.ActualWidth, this.ActualHeight)
            {
                Owner = Window.GetWindow(this)
            };

            if (dialog.ShowDialog() == true)
            {
                SetWindowSize(dialog.SelectedWidth, dialog.SelectedHeight);
            }
        }

        private void SetWindowSize(double width, double height)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;

            // 최소/최대 크기 제한
            const double minWidth = 300;
            const double minHeight = 200;
            
            width = Math.Max(minWidth, Math.Min(width, canvas.ActualWidth - 20));
            height = Math.Max(minHeight, Math.Min(height, canvas.ActualHeight - 20));

            // 현재 위치가 새 크기에서 캔버스를 벗어나지 않도록 조정
            var currentLeft = Canvas.GetLeft(this);
            var currentTop = Canvas.GetTop(this);
            
            if (currentLeft + width > canvas.ActualWidth)
            {
                currentLeft = Math.Max(0, canvas.ActualWidth - width);
                Canvas.SetLeft(this, currentLeft);
            }
            
            if (currentTop + height > canvas.ActualHeight)
            {
                currentTop = Math.Max(0, canvas.ActualHeight - height);
                Canvas.SetTop(this, currentTop);
            }

            this.Width = width;
            this.Height = height;
            
            // 최대화 상태 해제
            _isMaximized = false;
            _isMinimized = false;
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
            StartResize(ResizeDirection.Top, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.Bottom, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.Left, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.Right, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.TopLeft, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.TopRight, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.BottomLeft, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            StartResize(ResizeDirection.BottomRight, e.GetPosition(Parent as Canvas));
            e.Handled = true;
        }

        private void StartResize(ResizeDirection direction, Point startPosition)
        {
            var canvas = Parent as Canvas;
            if (canvas == null) return;

            _isResizing = true;
            _resizeDirection = direction;
            _startPoint = startPosition;
            _resizeStartWidth = this.ActualWidth;
            _resizeStartHeight = this.ActualHeight;
            _resizeStartLeft = Canvas.GetLeft(this);
            _resizeStartTop = Canvas.GetTop(this);
            this.CaptureMouse();
            
            // 활성 윈도우로 설정 (bring to front)
            MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
        }

        private void PerformResize(Point currentPosition)
        {
            var canvas = (Canvas?)this.Parent;
            if (canvas == null) return;

            var deltaX = currentPosition.X - _startPoint.X;
            var deltaY = currentPosition.Y - _startPoint.Y;

            var newWidth = _resizeStartWidth;
            var newHeight = _resizeStartHeight;
            var newLeft = _resizeStartLeft;
            var newTop = _resizeStartTop;

            // 최소 크기 설정 (MdiChartWindow와 동일)
            const double minWidth = 300;
            const double minHeight = 200;

            switch (_resizeDirection)
            {
                case ResizeDirection.Right:
                    newWidth = Math.Max(minWidth, _resizeStartWidth + deltaX);
                    break;

                case ResizeDirection.Bottom:
                    newHeight = Math.Max(minHeight, _resizeStartHeight + deltaY);
                    break;

                case ResizeDirection.Left:
                    var newWidthLeft = Math.Max(minWidth, _resizeStartWidth - deltaX);
                    if (newWidthLeft >= minWidth)
                    {
                        newWidth = newWidthLeft;
                        newLeft = _resizeStartLeft + deltaX;
                    }
                    break;

                case ResizeDirection.Top:
                    var newHeightTop = Math.Max(minHeight, _resizeStartHeight - deltaY);
                    if (newHeightTop >= minHeight)
                    {
                        newHeight = newHeightTop;
                        newTop = _resizeStartTop + deltaY;
                    }
                    break;

                case ResizeDirection.TopLeft:
                    var newWidthTL = Math.Max(minWidth, _resizeStartWidth - deltaX);
                    var newHeightTL = Math.Max(minHeight, _resizeStartHeight - deltaY);
                    if (newWidthTL >= minWidth && newHeightTL >= minHeight)
                    {
                        newWidth = newWidthTL;
                        newHeight = newHeightTL;
                        newLeft = _resizeStartLeft + deltaX;
                        newTop = _resizeStartTop + deltaY;
                    }
                    break;

                case ResizeDirection.TopRight:
                    var newWidthTR = Math.Max(minWidth, _resizeStartWidth + deltaX);
                    var newHeightTR = Math.Max(minHeight, _resizeStartHeight - deltaY);
                    if (newWidthTR >= minWidth && newHeightTR >= minHeight)
                    {
                        newWidth = newWidthTR;
                        newHeight = newHeightTR;
                        newTop = _resizeStartTop + deltaY;
                    }
                    break;

                case ResizeDirection.BottomLeft:
                    var newWidthBL = Math.Max(minWidth, _resizeStartWidth - deltaX);
                    var newHeightBL = Math.Max(minHeight, _resizeStartHeight + deltaY);
                    if (newWidthBL >= minWidth && newHeightBL >= minHeight)
                    {
                        newWidth = newWidthBL;
                        newHeight = newHeightBL;
                        newLeft = _resizeStartLeft + deltaX;
                    }
                    break;

                case ResizeDirection.BottomRight:
                    newWidth = Math.Max(minWidth, _resizeStartWidth + deltaX);
                    newHeight = Math.Max(minHeight, _resizeStartHeight + deltaY);
                    break;
            }

            // 캔버스 경계 체크
            if (newLeft >= 0 && newLeft + newWidth <= canvas.ActualWidth &&
                newTop >= 0 && newTop + newHeight <= canvas.ActualHeight)
            {
                this.Width = newWidth;
                this.Height = newHeight;
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
            }
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