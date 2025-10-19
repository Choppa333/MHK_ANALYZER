using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MGK_Analyzer.Models;
using Syncfusion.UI.Xaml.Charts;

namespace MGK_Analyzer.Controls
{
    public partial class MdiContourWindow : UserControl, INotifyPropertyChanged
    {
        public static readonly DependencyProperty WindowTitleProperty =
            DependencyProperty.Register("WindowTitle", typeof(string), typeof(MdiContourWindow),
            new PropertyMetadata("Contour Chart"));

        public string WindowTitle
        {
            get => (string)GetValue(WindowTitleProperty);
            set => SetValue(WindowTitleProperty, value);
        }

        public List<Surface3DPoint> DataPoints { get; set; } = new List<Surface3DPoint>();

        // 리사이즈 및 드래그 관련 필드
        private bool _isResizing = false;
        private bool _isDragging = false;
        private Point _startPoint;
        private ResizeDirection _resizeDirection = ResizeDirection.None;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private double _resizeStartLeft;
        private double _resizeStartTop;
        private bool _isPanelPinned = false;
        private static int _globalZIndex = 1;  // 전역 Z-Index (다른 윈도우와 공유)
        
        // 윈도우 상태 관련 필드
        private double _normalWidth = 1000;
        private double _normalHeight = 700;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private bool _isMaximized = false;
        private bool _isMinimized = false;

        public event EventHandler? WindowClosed;
        public event EventHandler? WindowMinimized;
        public event EventHandler? WindowMaximized;

        public MdiContourWindow()
        {
            InitializeComponent();
            // Bring to front on any mouse down inside the control
            this.PreviewMouseDown += (_, __) => MGK_Analyzer.Services.MdiZOrderService.BringToFront(this);
        }

        private void MdiContourWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializeChartWithData();
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

        private void InitializeChartWithData()
        {
            if (DataPoints == null || DataPoints.Count == 0)
            {
                // 샘플 데이터로 초기화
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
            System.Diagnostics.Debug.WriteLine($"[Contour] CreateContourChart 시작 - DataPoints Count: {DataPoints?.Count ?? 0}");

            // DataPoints 유효성 검사
            if (DataPoints == null || DataPoints.Count == 0)
            {
                System.Diagnostics.Debug.WriteLine("[Contour] DataPoints가 null이거나 비어있음");
                return;
            }
            
            try
            {
                System.Diagnostics.Debug.WriteLine("[Contour] 유효한 포인트 필터링 시작");
                
                // 값의 범위 계산 - NaN과 Infinity 필터링
                var validPoints = DataPoints.Where(p => !double.IsNaN(p.Z) && !double.IsInfinity(p.Z)).ToList();
                
                System.Diagnostics.Debug.WriteLine($"[Contour] 유효한 포인트 개수: {validPoints.Count}");
                
                if (validPoints.Count == 0)
                {
                    System.Diagnostics.Debug.WriteLine("[Contour] 유효한 포인트가 없음 - NaN/Infinity");
                    MessageBox.Show("Valid data points not found. All Z values are NaN or Infinity.", 
                                  "Invalid Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                // 데이터를 SfSurfaceChart에 바인딩
                ContourChart.ItemsSource = validPoints;
                
                // Grid 크기 계산
                var uniqueX = validPoints.Select(p => p.X).Distinct().OrderBy(x => x).ToList();
                var uniqueY = validPoints.Select(p => p.Y).Distinct().OrderBy(y => y).ToList();
                
                ContourChart.RowSize = uniqueY.Count;
                ContourChart.ColumnSize = uniqueX.Count;
                
                System.Diagnostics.Debug.WriteLine($"[Contour] Grid 크기: {ContourChart.ColumnSize} x {ContourChart.RowSize}");
                
                // Color Palette 적용
                ApplyColorPalette();
                
                System.Diagnostics.Debug.WriteLine("[Contour] CreateContourChart 완료");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Contour] 예외 발생: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[Contour] StackTrace: {ex.StackTrace}");
                MessageBox.Show($"Error creating contour chart: {ex.Message}\n\nDataPoints Count: {DataPoints.Count}", 
                              "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private void ApplyColorPalette()
        {
            var palette = ColorPaletteComboBox?.SelectedItem as ComboBoxItem;
            var paletteName = palette?.Content?.ToString() ?? "Rainbow";

            // SfSurfaceChart는 자체 ColorBar를 사용하므로 
            // ColorModel을 통해 색상을 설정할 수 있습니다
            // 필요시 추가 커스터마이징 가능
            
            System.Diagnostics.Debug.WriteLine($"[Contour] Color Palette 적용: {paletteName}");
        }

        private Color[] GetColorPalette(int count)
        {
            var palette = ColorPaletteComboBox?.SelectedItem as ComboBoxItem;
            var paletteName = palette?.Content?.ToString() ?? "Rainbow";

            var colors = new Color[count];

            switch (paletteName)
            {
                case "Jet":
                    for (int i = 0; i < count; i++)
                    {
                        float ratio = (float)i / (count - 1);
                        colors[i] = GetJetColor(ratio);
                    }
                    break;

                case "Hot":
                    for (int i = 0; i < count; i++)
                    {
                        float ratio = (float)i / (count - 1);
                        colors[i] = GetHotColor(ratio);
                    }
                    break;

                case "Cool":
                    for (int i = 0; i < count; i++)
                    {
                        float ratio = (float)i / (count - 1);
                        colors[i] = GetCoolColor(ratio);
                    }
                    break;

                case "Grayscale":
                    for (int i = 0; i < count; i++)
                    {
                        byte gray = (byte)(255 * i / (count - 1));
                        colors[i] = Color.FromRgb(gray, gray, gray);
                    }
                    break;

                case "Rainbow":
                default:
                    for (int i = 0; i < count; i++)
                    {
                        float ratio = (float)i / (count - 1);
                        colors[i] = GetRainbowColor(ratio);
                    }
                    break;
            }

            return colors;
        }

        private Color GetRainbowColor(float ratio)
        {
            // Rainbow: 빨강 -> 주황 -> 노랑 -> 초록 -> 파랑 -> 남색 -> 보라
            int segment = (int)(ratio * 6);
            float segmentRatio = (ratio * 6) - segment;

            switch (segment)
            {
                case 0: return Color.FromRgb(255, (byte)(segmentRatio * 255), 0);
                case 1: return Color.FromRgb((byte)(255 - segmentRatio * 255), 255, 0);
                case 2: return Color.FromRgb(0, 255, (byte)(segmentRatio * 255));
                case 3: return Color.FromRgb(0, (byte)(255 - segmentRatio * 255), 255);
                case 4: return Color.FromRgb((byte)(segmentRatio * 255), 0, 255);
                case 5: return Color.FromRgb(255, 0, (byte)(255 - segmentRatio * 255));
                default: return Colors.Purple;
            }
        }

        private Color GetJetColor(float ratio)
        {
            // Jet colormap: 파랑 -> 청록 -> 초록 -> 노랑 -> 빨강
            byte r, g, b;

            if (ratio < 0.125f)
            {
                r = 0;
                g = 0;
                b = (byte)(128 + ratio * 1024);
            }
            else if (ratio < 0.375f)
            {
                r = 0;
                g = (byte)((ratio - 0.125f) * 1024);
                b = 255;
            }
            else if (ratio < 0.625f)
            {
                r = (byte)((ratio - 0.375f) * 1024);
                g = 255;
                b = (byte)(255 - (ratio - 0.375f) * 1024);
            }
            else if (ratio < 0.875f)
            {
                r = 255;
                g = (byte)(255 - (ratio - 0.625f) * 1024);
                b = 0;
            }
            else
            {
                r = (byte)(255 - (ratio - 0.875f) * 1024);
                g = 0;
                b = 0;
            }

            return Color.FromRgb(r, g, b);
        }

        private Color GetHotColor(float ratio)
        {
            // Hot colormap: 검정 -> 빨강 -> 노랑 -> 흰색
            byte r = (byte)Math.Min(255, ratio * 3 * 255);
            byte g = (byte)Math.Max(0, Math.Min(255, (ratio - 0.33f) * 3 * 255));
            byte b = (byte)Math.Max(0, Math.Min(255, (ratio - 0.66f) * 3 * 255));

            return Color.FromRgb(r, g, b);
        }

        private Color GetCoolColor(float ratio)
        {
            // Cool colormap: 청록색 -> 자홍색
            byte r = (byte)(ratio * 255);
            byte g = (byte)((1 - ratio) * 255);
            byte b = 255;

            return Color.FromRgb(r, g, b);
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

        #region Event Handlers

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
                // 최소화 - 타이틀바만 보이도록 (통일된 높이)
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
                var canvas = Parent as Canvas;
                if (canvas != null)
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

            // 최소 크기 설정 (통일)
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

        #region Management Panel Handlers

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
                var timer = new System.Windows.Threading.DispatcherTimer();
                timer.Interval = TimeSpan.FromMilliseconds(300);
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    if (!_isPanelPinned && !ManagementPanel.IsMouseOver)
                    {
                        var storyboard = FindResource("SlideOutStoryboard") as System.Windows.Media.Animation.Storyboard;
                        storyboard?.Begin();
                        ManagementPanelTab.Visibility = Visibility.Visible;
                    }
                };
                timer.Start();
            }
        }

        private void ManagementPanelTab_MouseEnter(object sender, MouseEventArgs e)
        {
            var storyboard = FindResource("SlideInStoryboard") as System.Windows.Media.Animation.Storyboard;
            storyboard?.Begin();
            ManagementPanelTab.Visibility = Visibility.Collapsed;
        }

        private void ManagementPanelTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var storyboard = FindResource("SlideInStoryboard") as System.Windows.Media.Animation.Storyboard;
            storyboard?.Begin();
            ManagementPanelTab.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Settings Event Handlers

        private void ColorPalette_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (IsLoaded && DataPoints != null && DataPoints.Count > 0)
            {
                ApplyColorPalette();
            }
        }

        private void ContourLevels_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (IsLoaded && DataPoints != null && DataPoints.Count > 0)
            {
                // SfSurfaceChart는 자동으로 레벨을 관리하므로
                // 필요시 ColorBar 설정을 업데이트할 수 있습니다
            }
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
                // ColorBar의 가시성을 조절
                var visibility = ShowLegendCheckBox?.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
                if (ContourChart.ColorBar is FrameworkElement colorBar)
                {
                    colorBar.Visibility = visibility;
                }
            }
        }

        private void ResetView_Click(object sender, RoutedEventArgs e)
        {
            // Contour 차트 재생성
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
                    // Create a RenderTargetBitmap to capture the chart
                    var renderBitmap = new RenderTargetBitmap(
                        (int)ContourChart.ActualWidth,
                        (int)ContourChart.ActualHeight,
                        96, 96,
                        PixelFormats.Pbgra32);

                    renderBitmap.Render(ContourChart);

                    // Save to file
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

        #endregion

        #region Size Management Methods

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

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
