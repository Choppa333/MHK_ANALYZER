using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using MGK_Analyzer.Services;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using OxyPlot.Annotations;

namespace MGK_Analyzer.Controls
{
    public partial class MdiContour2DWindow : UserControl, INotifyPropertyChanged
    {
        // Window chrome state
        private bool _isDragging = false;
        private bool _isResizing = false;
        private Point _lastPosition;
        private double _normalWidth = 1000;
        private double _normalHeight = 700;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private bool _isMaximized = false;
        private bool _isMinimized = false;

        private ResizeDirection _resizeDirection = ResizeDirection.None;
        private Point _resizeStartPosition;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private double _resizeStartLeft;
        private double _resizeStartTop;

        private bool _isManagementPanelExpanded = true;
        private bool _isManagementPanelPinned = false;

        // Palette controls
        private LinearColorAxis? _colorAxis;
        private int _paletteVariant = 0;        // 0: Custom, 1: Viridis, 2: Hot, 3: Cool
        private double _paletteIntensity = 1.0; // 0..1 (0=grayscale, 1=original)

        public static readonly DependencyProperty WindowTitleProperty =
            DependencyProperty.Register("WindowTitle", typeof(string), typeof(MdiContour2DWindow),
                new PropertyMetadata("Contour 2D Window"));

        public string WindowTitle
        {
            get => (string)GetValue(WindowTitleProperty);
            set { SetValue(WindowTitleProperty, value); OnPropertyChanged(); }
        }

        public event EventHandler? WindowClosed;
        public event EventHandler? WindowActivated;

        // Raw sample data (CSV) shared with 3D
        private static readonly string RawData = EfficiencySampleData.Csv;

        private record RawPoint(double Speed, double Torque, double Efficiency);

        public MdiContour2DWindow()
        {
            InitializeComponent();
            DataContext = this;
            this.PreviewMouseDown += (_, __) => MdiZOrderService.BringToFront(this);
            Loaded += (_, __) => CreateEfficiencyContour();
        }

        private void CreateEfficiencyContour()
        {
            var raw = ParseRawData(RawData);
            if (raw.Count == 0) return;

            // Uniform axes (can be tuned)
            double sMin = raw.Min(p => p.Speed), sMax = raw.Max(p => p.Speed);
            double tMin = raw.Min(p => p.Torque), tMax = raw.Max(p => p.Torque);
            double sStep = 250.0;
            double tStep = 25.0;

            var xs = new List<double>();
            for (double s = sMin; s <= sMax + 1e-9; s += sStep) xs.Add(Math.Round(s, 1));
            var ys = new List<double>();
            for (double t = tMin; t <= tMax + 1e-9; t += tStep) ys.Add(Math.Round(t, 1));

            double[,] z = BuildGrid(xs, ys, raw);

            var model = new PlotModel { Title = "Efficiency Map" };

            // Color axis with custom palette and external controls
            _colorAxis = new LinearColorAxis
            {
                Position = AxisPosition.Right,
                Title = "Efficiency [%]",
                Minimum = 50,
                Maximum = 100,
                Palette = BuildPaletteFromVariant(50, 100, _paletteVariant, _paletteIntensity)
            };
            model.Axes.Add(_colorAxis);

            model.Axes.Add(new LinearAxis { Position = AxisPosition.Bottom, Title = "Speed [rpm]" });
            model.Axes.Add(new LinearAxis { Position = AxisPosition.Left, Title = "Torque [Nm]" });

            var heatmap = new HeatMapSeries
            {
                X0 = xs.First(), X1 = xs.Last(),
                Y0 = ys.First(), Y1 = ys.Last(),
                Data = z,
                Interpolate = true
            };
            model.Series.Add(heatmap);

            var contour = new ContourSeries
            {
                Data = z,
                ColumnCoordinates = xs.ToArray(),
                RowCoordinates = ys.ToArray(),
                ContourLevels = new double[] { 70, 75, 80, 85, 90, 92, 94, 96 },
                LabelBackground = OxyColors.Undefined
            };
            contour.CalculateContours();
            model.Series.Add(contour);

            // OXY POINTS & LABELS: 원 데이터 포인트 + 값 라벨 추가
            var scatter = new ScatterSeries
            {
                MarkerType = MarkerType.Circle,
                MarkerSize = 3,
                MarkerFill = OxyColors.Black,
                MarkerStroke = OxyColors.White,
                MarkerStrokeThickness = 0.5,
            };
            foreach (var p in raw)
            {
                var sp = new ScatterPoint(p.Speed, p.Torque, 3, double.NaN) { Tag = p.Efficiency };
                scatter.Points.Add(sp);
            }
            model.Series.Add(scatter);

            bool useLabelFilter = false; // 필요 시 true로 설정
            double minDx = 300;          // 속도 최소 간격
            double minDy = 20;           // 토크 최소 간격
            var placed = new List<(double X, double Y)>();

            foreach (var p in raw.OrderBy(d => d.Speed).ThenBy(d => d.Torque))
            {
                if (useLabelFilter)
                {
                    bool tooClose = placed.Any(q => Math.Abs(q.X - p.Speed) < minDx && Math.Abs(q.Y - p.Torque) < minDy);
                    if (tooClose) continue;
                    placed.Add((p.Speed, p.Torque));
                }

                model.Annotations.Add(new TextAnnotation
                {
                    Text = $"{p.Efficiency:F1}",
                    TextPosition = new DataPoint(p.Speed, p.Torque),
                    Stroke = OxyColors.Transparent,
                    TextHorizontalAlignment = OxyPlot.HorizontalAlignment.Center,
                    TextVerticalAlignment = OxyPlot.VerticalAlignment.Middle,
                    FontSize = 8,
                    TextColor = OxyColors.Black
                });
            }

            scatter.TrackerFormatString = "Speed: {2:0}\nTorque: {3:0}\nEfficiency: {Tag:0.0}%";

            PlotView1.Model = model;

            var controller = new PlotController();
            controller.UnbindMouseDown(OxyMouseButton.Left);
            controller.UnbindMouseDown(OxyMouseButton.Right);
            controller.UnbindMouseDown(OxyMouseButton.Middle);
            controller.UnbindMouseWheel();
            PlotView1.Controller = controller;

            PlotView1.InvalidatePlot(true);
        }

        private static List<RawPoint> ParseRawData(string csv)
        {
            var list = new List<RawPoint>();
            using var reader = new StringReader(csv);
            string? line = reader.ReadLine(); // header
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split(',', StringSplitOptions.TrimEntries);
                if (parts.Length < 3) continue;
                if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var sp)) continue;
                if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var tq)) continue;
                if (!double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ef)) continue;
                if (double.IsNaN(sp) || double.IsNaN(tq) || double.IsNaN(ef)) continue;
                if (sp <= 0 || tq <= 0) continue;
                list.Add(new RawPoint(sp, tq, ef));
            }

            // Deduplicate to 0.1 precision
            return list
                .GroupBy(p => ($"{Math.Round(p.Speed, 1, MidpointRounding.AwayFromZero):F1}", $"{Math.Round(p.Torque, 1, MidpointRounding.AwayFromZero):F1}"))
                .Select(g => new RawPoint(
                    double.Parse(g.Key.Item1, CultureInfo.InvariantCulture),
                    double.Parse(g.Key.Item2, CultureInfo.InvariantCulture),
                    g.Average(x => x.Efficiency)))
                .OrderBy(p => p.Speed).ThenBy(p => p.Torque)
                .ToList();
        }

        private static double[,] BuildGrid(IReadOnlyList<double> xs, IReadOnlyList<double> ys, List<RawPoint> raw)
        {
            // Orient as [nx, ny]: first dimension = columns (X), second = rows (Y)
            int nx = xs.Count, ny = ys.Count;
            var grid = new double[nx, ny];

            double sMin = xs.First(), sMax = xs.Last();
            double tMin = ys.First(), tMax = ys.Last();
            double rangeS = Math.Max(1e-6, sMax - sMin);
            double rangeT = Math.Max(1e-6, tMax - tMin);

            var exact = raw.ToDictionary(
                p => ($"{Math.Round(p.Speed, 1):F1}", $"{Math.Round(p.Torque, 1):F1}"),
                p => p.Efficiency);

            for (int i = 0; i < nx; i++)
            {
                double s = xs[i];
                for (int j = 0; j < ny; j++)
                {
                    double t = ys[j];
                    var key = ($"{s:F1}", $"{t:F1}");
                    if (exact.TryGetValue(key, out var ef))
                    {
                        grid[i, j] = ef;
                    }
                    else
                    {
                        grid[i, j] = IDW(s, t, raw, rangeS, rangeT, 8, 2.0);
                    }
                }
            }
            return grid;
        }

        private static double IDW(double s, double t, List<RawPoint> raw, double rangeS, double rangeT, int k, double power)
        {
            var neighbors = raw
                .Select(p =>
                {
                    double dx = (p.Speed - s) / Math.Max(rangeS, 1e-12);
                    double dy = (p.Torque - t) / Math.Max(rangeT, 1e-12);
                    double d = Math.Sqrt(dx * dx + dy * dy);
                    return new { Point = p, D = d };
                })
                .OrderBy(x => x.D)
                .Take(Math.Max(1, k))
                .ToList();

            if (neighbors[0].D < 1e-12) return neighbors[0].Point.Efficiency;

            double num = 0.0, den = 0.0;
            foreach (var n in neighbors)
            {
                double w = 1.0 / Math.Pow(n.D + 1e-12, power);
                num += w * n.Point.Efficiency;
                den += w;
            }
            return den <= 0 ? neighbors.Average(n => n.Point.Efficiency) : num / den;
        }

        // Build a robust custom palette with non-uniform anchors mapped to [min..max]
        private static OxyPalette BuildEfficiencyPalette(double min, double max, int size = 256)
        {
            var anchors = new List<(double value, OxyColor color)>
            {
                (50, OxyColors.DarkBlue),     // 50?60: blue range
                (60, OxyColors.Blue),
                (70, OxyColors.LimeGreen),    // 70?80: green range
                (80, OxyColors.Yellow),       // 80?90: yellow/orange
                (90, OxyColors.Orange),
                (100, OxyColors.DarkRed)      // 90+: red range
            };

            anchors = anchors.OrderBy(a => a.value).ToList();

            var colors = new OxyColor[size];
            int lastIndex = size - 1;

            for (int i = 0; i < size; i++)
            {
                double v = min + (max - min) * i / Math.Max(1, lastIndex);

                // Clamp to ends
                if (v <= anchors[0].value)
                {
                    colors[i] = anchors[0].color;
                    continue;
                }
                if (v >= anchors[^1].value)
                {
                    colors[i] = anchors[^1].color;
                    continue;
                }

                // Find surrounding anchor interval
                int idx = 0;
                while (idx < anchors.Count - 1 && !(anchors[idx].value <= v && v <= anchors[idx + 1].value))
                    idx++;

                var (v0, c0) = anchors[idx];
                var (v1, c1) = anchors[idx + 1];
                double t = (v - v0) / Math.Max(1e-9, v1 - v0);
                colors[i] = LerpColor(c0, c1, t);
            }

            return new OxyPalette(colors);

            static OxyColor LerpColor(OxyColor a, OxyColor b, double t)
            {
                byte Lerp(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
                return OxyColor.FromArgb(Lerp(a.A, b.A), Lerp(a.R, b.R), Lerp(a.G, b.G), Lerp(a.B, b.B));
            }
        }

        // Build palette based on variant and intensity
        private static OxyPalette BuildPaletteFromVariant(double min, double max, int variant, double intensity)
        {
            OxyPalette basePalette = variant switch
            {
                0 => BuildEfficiencyPalette(min, max),
                1 => OxyPalettes.Viridis(256),
                2 => OxyPalettes.Hot(256),
                3 => OxyPalettes.Cool(256),
                _ => BuildEfficiencyPalette(min, max)
            };
            return AdjustPaletteIntensity(basePalette, intensity);
        }

        // Intensity: 0 -> grayscale, 1 -> original
        private static OxyPalette AdjustPaletteIntensity(OxyPalette palette, double intensity)
        {
            intensity = Math.Max(0, Math.Min(1, intensity));
            var arr = palette.Colors.Select(c => Lerp(Grey(c), c, intensity)).ToArray();
            return new OxyPalette(arr);

            static OxyColor Grey(OxyColor c)
            {
                // Perceptual luminance approximation
                var y = (byte)Math.Max(0, Math.Min(255, (0.2126 * c.R) + (0.7152 * c.G) + (0.0722 * c.B)));
                return OxyColor.FromArgb(c.A, y, y, y);
            }
            static OxyColor Lerp(OxyColor a, OxyColor b, double t)
            {
                byte LerpB(byte x, byte y) => (byte)Math.Round(x + (y - x) * t);
                return OxyColor.FromArgb(LerpB(a.A, b.A), LerpB(a.R, b.R), LerpB(a.G, b.G), LerpB(a.B, b.B));
            }
        }

        private void UpdateColorAxisPalette()
        {
            if (_colorAxis == null) return;
            _colorAxis.Palette = BuildPaletteFromVariant(_colorAxis.Minimum, _colorAxis.Maximum, _paletteVariant, _paletteIntensity);
            PlotView1.InvalidatePlot(false);
        }

        // Syncfusion slider event handlers
        private void PaletteThemeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _paletteVariant = (int)Math.Round(e.NewValue);
            UpdateColorAxisPalette();
        }
        private void PaletteIntensitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            _paletteIntensity = Math.Max(0.0, Math.Min(1.0, e.NewValue / 100.0));
            UpdateColorAxisPalette();
        }

        // ========== UI plumbing (window chrome) ==========
        private void ManagementPanelTab_MouseEnter(object sender, MouseEventArgs e)
        {
            if (!_isManagementPanelExpanded && !_isManagementPanelPinned)
            {
                ExpandManagementPanel(true);
            }
        }

        private void ManagementPanelTab_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isManagementPanelExpanded)
            {
                ExpandManagementPanel(true);
            }
        }

        private void ManagementPanel_MouseLeave(object sender, MouseEventArgs e)
        {
            var position = e.GetPosition(ManagementPanel);
            var bounds = new Rect(0, 0, ManagementPanel.ActualWidth, ManagementPanel.ActualHeight);

            if (_isManagementPanelExpanded && !_isManagementPanelPinned && !bounds.Contains(position))
            {
                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(300)
                };
                timer.Tick += (s, args) =>
                {
                    timer.Stop();
                    if (!_isManagementPanelPinned && !ManagementPanel.IsMouseOver)
                    {
                        CollapseManagementPanel(true);
                    }
                };
                timer.Start();
            }
        }

        private void PanelPinButton_Checked(object sender, RoutedEventArgs e)
        {
            _isManagementPanelPinned = true;
            if (!_isManagementPanelExpanded) ExpandManagementPanel(true);
        }

        private void PanelPinButton_Unchecked(object sender, RoutedEventArgs e)
        {
            _isManagementPanelPinned = false;
        }

        private void ExpandManagementPanel(bool animated)
        {
            _isManagementPanelExpanded = true;

            if (animated)
            {
                var storyboard = FindResource("PanelSlideInStoryboard") as Storyboard;
                storyboard?.Begin();
            }
            else
            {
                ManagementPanel.Width = 250;
            }

            ManagementPanelTab.Visibility = Visibility.Collapsed;
        }

        private void CollapseManagementPanel(bool animated)
        {
            _isManagementPanelExpanded = false;

            if (animated)
            {
                var storyboard = FindResource("PanelSlideOutStoryboard") as Storyboard;
                if (storyboard != null)
                {
                    storyboard.Completed += (s, e) =>
                    {
                        ManagementPanelTab.Visibility = Visibility.Visible;
                    };
                    storyboard.Begin();
                }
            }
            else
            {
                ManagementPanel.Width = 0;
                ManagementPanelTab.Visibility = Visibility.Visible;
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = e.OriginalSource as FrameworkElement;
            while (element != null)
            {
                if (element is Button) return;
                element = element.Parent as FrameworkElement;
            }

            if (e.ClickCount == 2)
            {
                Maximize_Click(sender, e);
            }
            else
            {
                _isDragging = true;
                var canvas = (Canvas)this.Parent;
                _lastPosition = e.GetPosition(canvas);
                this.CaptureMouse();
                MdiZOrderService.BringToFront(this);
                WindowActivated?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (_isDragging && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas)this.Parent;
                if (canvas == null) return;

                var currentPosition = e.GetPosition(canvas);
                var deltaX = currentPosition.X - _lastPosition.X;
                var deltaY = currentPosition.Y - _lastPosition.Y;

                var newLeft = Canvas.GetLeft(this) + deltaX;
                var newTop = Canvas.GetTop(this) + deltaY;

                newLeft = Math.Max(0, Math.Min(newLeft, canvas.ActualWidth - this.ActualWidth));
                newTop = Math.Max(0, Math.Min(newTop, canvas.ActualHeight - this.ActualHeight));

                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
                _lastPosition = currentPosition;
            }

            if (_isResizing && e.LeftButton == MouseButtonState.Pressed)
            {
                var canvas = (Canvas)this.Parent;
                if (canvas == null) return;
                PerformResize(e.GetPosition(canvas));
            }

            base.OnMouseMove(e);
        }

        private void PerformResize(Point currentPosition)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;

            var deltaX = currentPosition.X - _resizeStartPosition.X;
            var deltaY = currentPosition.Y - _resizeStartPosition.Y;

            var newWidth = _resizeStartWidth;
            var newHeight = _resizeStartHeight;
            var newLeft = _resizeStartLeft;
            var newTop = _resizeStartTop;

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

            if (newLeft >= 0 && newLeft + newWidth <= canvas.ActualWidth &&
                newTop >= 0 && newTop + newHeight <= canvas.ActualHeight)
            {
                this.Width = newWidth;
                this.Height = newHeight;
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
            }
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

        private void StartResize(ResizeDirection direction, Point startPosition)
        {
            _isResizing = true;
            _resizeDirection = direction;
            _resizeStartPosition = startPosition;
            _resizeStartWidth = this.ActualWidth;
            _resizeStartHeight = this.ActualHeight;
            _resizeStartLeft = Canvas.GetLeft(this);
            _resizeStartTop = Canvas.GetTop(this);
            this.CaptureMouse();
            MdiZOrderService.BringToFront(this);
            WindowActivated?.Invoke(this, EventArgs.Empty);
        }

        private void ResizeTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Top, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Bottom, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Left, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.Right, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.TopLeft, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.TopRight, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.BottomLeft, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                StartResize(ResizeDirection.BottomRight, e.GetPosition(canvas));
                e.Handled = true;
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            if (_isMinimized)
            {
                RestoreNormalSize();
            }
            else
            {
                SaveCurrentSize();
                this.Height = 30;
                _isMinimized = true;
                _isMaximized = false;
            }
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas == null) return;

            if (_isMaximized)
            {
                RestoreNormalSize();
            }
            else
            {
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

        public void Close_Click(object? sender, RoutedEventArgs? e)
        {
            WindowClosed?.Invoke(this, EventArgs.Empty);
        }

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
                    MessageBox.Show("유효한 숫자를 입력하세요.", "크기 설정", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"크기 적용 중 오류: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ResetSize_Click(object sender, RoutedEventArgs e)
        {
            SetWindowSize(1000, 700);
        }

        private void SetSmallSize_Click(object sender, RoutedEventArgs e) => SetWindowSize(400, 300);
        private void SetMediumSize_Click(object sender, RoutedEventArgs e) => SetWindowSize(800, 600);
        private void SetLargeSize_Click(object sender, RoutedEventArgs e) => SetWindowSize(1200, 800);

        private void SetHDSize_Click(object sender, RoutedEventArgs e)
        {
            var canvas = (Canvas)this.Parent;
            if (canvas != null)
            {
                var maxWidth = Math.Min(1920, canvas.ActualWidth - 50);
                var maxHeight = Math.Min(1080, canvas.ActualHeight - 50);
                SetWindowSize(maxWidth, maxHeight);
            }
            else
            {
                SetWindowSize(1200, 800);
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

            const double minWidth = 300;
            const double minHeight = 200;

            width = Math.Max(minWidth, Math.Min(width, canvas.ActualWidth - 20));
            height = Math.Max(minHeight, Math.Min(height, canvas.ActualHeight - 20));

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
            _isMaximized = false;
            _isMinimized = false;
        }

        private void SetContourMode_Click(object sender, RoutedEventArgs e)
        {
            // No-op for OxyPlot (always contour). Could adjust contour levels
            CreateEfficiencyContour();
        }
        private void SetSurfaceMode_Click(object sender, RoutedEventArgs e)
        {
            // No 3D surface in OxyPlot. Keep contour.
            CreateEfficiencyContour();
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
