using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Charts;
using MGK_Analyzer.Services;

namespace MGK_Analyzer.Controls
{
    public partial class MdiSurface3DWindow : UserControl
    {
        // Drag/resize state
        private bool _isDragging;
        private bool _isResizing;
        private Point _lastPosition;
        private ResizeDirection _resizeDirection = ResizeDirection.None;
        private Point _resizeStartPosition;
        private double _resizeStartWidth;
        private double _resizeStartHeight;
        private double _resizeStartLeft;
        private double _resizeStartTop;

        // Max/Min state
        private double _normalWidth = 1000;
        private double _normalHeight = 700;
        private double _normalLeft = 0;
        private double _normalTop = 0;
        private bool _isMaximized = false;
        private bool _isMinimized = false;

        // Events like 2D window
        public event EventHandler? WindowClosed;
        public event EventHandler? WindowActivated;

        public string WindowTitle
        {
            get => (string)GetValue(WindowTitleProperty);
            set => SetValue(WindowTitleProperty, value);
        }
        public static readonly DependencyProperty WindowTitleProperty = DependencyProperty.Register(
            nameof(WindowTitle), typeof(string), typeof(MdiSurface3DWindow), new PropertyMetadata("3D Efficiency Surface"));

        public MdiSurface3DWindow()
        {
            InitializeComponent();
            this.PreviewMouseDown += (_, __) => MdiZOrderService.BringToFront(this);
            Loaded += (_, __) => LoadAndRender();

            // toolbar events
            SldTilt.ValueChanged     += (_, __) => SurfaceChart.Tilt = SldTilt.Value;
            SldRotate.ValueChanged   += (_, __) => SurfaceChart.Rotate = SldRotate.Value;
            SldLevels.ValueChanged   += (_, __) => { ApplyPalette(); UpdateZLegend(); };
            SldOpacity.ValueChanged  += (_, __) => SurfaceChart.Opacity = SldOpacity.Value;
            CmbPalette.SelectionChanged += (_, __) => { ApplyPalette(); UpdateZLegend(); };
            BtnResetView.Click += BtnResetView_Click;

            // prepare color model
            SurfaceChart.ColorModel = new ChartColorModel();
            SurfaceChart.Palette = ChartColorPalette.Metro;
        }

        private void BtnResetView_Click(object sender, RoutedEventArgs e) => ResetView();

        private void ResetView()
        {
            SldTilt.Value = 15;
            SldRotate.Value = 30;
            SurfaceChart.Tilt = 15;
            SurfaceChart.Rotate = 30;
            SurfaceChart.Opacity = SldOpacity.Value = 1.0;
        }

        private void LoadAndRender()
        {
            // SURFACE3D DIAG: CSV 파싱 및 진단
            var raw = ParseRawData(EfficiencySampleData.Csv, out var diag);
            Debug.WriteLine($"[Surface3D/MDI] CSV lines={diag.TotalLines}, dataLines={diag.DataLines}, parsed={diag.ParsedCount}, delim='{diag.Delimiter}', dec='{diag.DecimalSeparator}', firstData='{diag.FirstDataLine}'"); // DIAG

            if (raw.Count == 0)
            {
                var msg =
                    "CSV 데이터 파싱 실패\n" +
                    $"- 전체 라인: {diag.TotalLines}, 데이터 라인: {diag.DataLines}\n" +
                    $"- 구분자 추정: '{diag.Delimiter}', 소수점: '{diag.DecimalSeparator}'\n" +
                    "- 원인: 데이터 비어 있음 또는 구분자/소수점 불일치\n\n" +
                    "임시 3x3 샘플 데이터로 대체하여 렌더링을 시도합니다.";
                MessageBox.Show(msg, "Surface3D CSV 파싱 오류", MessageBoxButton.OK, MessageBoxImage.Warning);

                raw = new List<(double Speed, double Torque, double Efficiency)>
                {
                    (1000,  50, 80), (2000,  50, 85), (3000,  50, 83),
                    (1000, 150, 88), (2000, 150, 92), (3000, 150, 90),
                    (1000, 250, 82), (2000, 250, 89), (3000, 250, 87),
                };
                diag.ParsedCount = raw.Count;
            }

            double sMin = raw.Min(p => p.Speed), sMax = raw.Max(p => p.Speed);
            double tMin = raw.Min(p => p.Torque), tMax = raw.Max(p => p.Torque);

            // 그리드 해상도 상향
            var speedGrid = BuildGrid(sMin, sMax, 150.0);  // 250 → 150
            var torqueGrid = BuildGrid(tMin, tMax, 15.0);  // 25 → 15

            int columnSize = speedGrid.Count;
            int rowSize = torqueGrid.Count;

            if (columnSize < 2 || rowSize < 2)
            {
                MessageBox.Show($"SurfaceGrid 크기가 너무 작습니다. (grid={rowSize}×{columnSize})\n최소 2×2 이상이어야 Surface 메쉬가 생성됩니다.", "Surface3D Grid 경고", MessageBoxButton.OK, MessageBoxImage.Information); // DIAG
            }

            double rangeS = Math.Max(1e-9, sMax - sMin);
            double rangeT = Math.Max(1e-9, tMax - tMin);

            var exact = raw.ToDictionary(
                p => ($"{Math.Round(p.Speed,1):F1}", $"{Math.Round(p.Torque,1):F1}"),
                p => p.Efficiency);

            var points = new ObservableCollection<SurfacePoint>();
            foreach (var ty in torqueGrid)
            {
                foreach (var sx in speedGrid)
                {
                    var key = ($"{sx:F1}", $"{ty:F1}");
                    double z = exact.TryGetValue(key, out var hit)
                        ? hit
                        : IDW(sx, ty, raw, 12, 2.0, rangeS, rangeT);  // k=10 → 12
                    // X=Speed, Y=Efficiency, Z=Torque (changed from X=Speed, Y=Torque, Z=Efficiency)
                    points.Add(new SurfacePoint { X = sx, Y = z, Z = ty });
                }
            }

            // DIAG: Count consistency
            int expected = rowSize * columnSize;
            if (points.Count != expected)
            {
                MessageBox.Show($"ItemsSource 크기({points.Count})와 Grid 크기({rowSize}×{columnSize}={expected})가 일치하지 않습니다.", "Surface3D 데이터 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Calculate Z range BEFORE setting ItemsSource
            double zMin = points.Count > 0 ? points.Min(p => p.Z) : 0;
            double zMax = points.Count > 0 ? points.Max(p => p.Z) : 1;
            Debug.WriteLine($"[Surface3D/MDI] Z Range: {zMin} - {zMax}");

            // Set grid sizes BEFORE ItemsSource per Syncfusion docs
            SurfaceChart.RowSize = rowSize;
            SurfaceChart.ColumnSize = columnSize;
            SurfaceChart.XBindingPath = nameof(SurfacePoint.X);
            SurfaceChart.YBindingPath = nameof(SurfacePoint.Y);
            SurfaceChart.ZBindingPath = nameof(SurfacePoint.Z);
            SurfaceChart.ItemsSource = points;

            SurfaceChart.Type = SurfaceType.Surface;
            ApplyZRange(points, zMin, zMax);
            ApplyPalette(zMin, zMax);
            UpdateZLegend();

            WindowTitle = $"3D Efficiency Surface | parsed={diag.ParsedCount} pts | CSV lines={diag.TotalLines} | grid={rowSize}×{columnSize}"; // DIAG

            SurfaceChart.InvalidateMeasure();
            SurfaceChart.InvalidateVisual();
        }

        private void UpdateZLegend()
        {
            try
            {
                var pts = SurfaceChart.ItemsSource as ObservableCollection<SurfacePoint>;
                if (pts == null || pts.Count == 0) { ZLegend.ItemsSource = null; return; }

                // Y 범위(효율성)를 기반으로 범례 생성 - Z 범위(토크) 대신
                double yMin = pts.Min(p => p.Y);
                double yMax = pts.Max(p => p.Y);
                int levels = Math.Max(2, (int)Math.Round(SldLevels.Value));

                // Get brushes from current palette
                List<Brush> brushList;
                if (SurfaceChart.Palette == ChartColorPalette.Custom && SurfaceChart.ColorModel?.CustomBrushes != null && SurfaceChart.ColorModel.CustomBrushes.Count > 0)
                {
                    brushList = SurfaceChart.ColorModel.CustomBrushes.ToList();
                }
                else
                {
                    brushList = SurfaceChart.ColorModel.GetBrushes(SurfaceChart.Palette) ?? new List<Brush>();
                }

                // Sample evenly for legend only
                var brushes = new List<Brush>();
                for (int i = 0; i < levels; i++)
                {
                    int idx = brushList.Count <= 1 ? 0 : (int)Math.Round(i * (brushList.Count - 1) / (double)(levels - 1));
                    brushes.Add(brushList[idx]);
                }

                double step = (yMax - yMin) / levels;
                string fmt = (SurfaceChart.YAxis as SurfaceAxis)?.LabelFormat ?? "0.0";  // Y??(?????) ???? ???
                var items = new List<object>();
                double start = yMin;
                for (int i = 0; i < levels; i++)
                {
                    double end = (i == levels - 1) ? yMax : start + step;
                    string label = (double.IsNaN(start) || double.IsNaN(end)) ? string.Empty :
                        $"{start.ToString(fmt, CultureInfo.InvariantCulture)} - {end.ToString(fmt, CultureInfo.InvariantCulture)}";
                    items.Add(new { Brush = brushes[Math.Min(i, brushes.Count - 1)], Label = label });
                    start = end;
                }

                ZLegend.ItemsSource = items;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Surface3D/MDI] UpdateYLegend error: {ex.Message}");
            }
        }

        // DIAG: CSV 파싱 진단 정보 컨테이너
        private class CsvParseDiag
        {
            public int TotalLines { get; set; }
            public int DataLines { get; set; }
            public int ParsedCount { get; set; }
            public string Delimiter { get; set; } = ",";
            public string DecimalSeparator { get; set; } = ".";
            public string FirstDataLine { get; set; } = string.Empty;
        }

        private static List<(double Speed, double Torque, double Efficiency)> ParseRawData(string csv, out CsvParseDiag diag)
        {
            diag = new CsvParseDiag();
            var list = new List<(double, double, double)>();
            if (string.IsNullOrWhiteSpace(csv)) return list;

            var lines = csv.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (lines.Count == 0) return list;

            // drop header if looks like header
            if (lines[0].IndexOf("Speed", StringComparison.OrdinalIgnoreCase) >= 0)
                lines.RemoveAt(0);

            diag.TotalLines = 1 + lines.Count;
            diag.DataLines = lines.Count;
            if (lines.Count > 0) diag.FirstDataLine = lines[0].Trim();

            int semicolons = lines.Count(l => l.Contains(';'));
            int commas = lines.Count(l => l.Contains(','));
            char delimiter = (semicolons > commas) ? ';' : ',';
            diag.Delimiter = delimiter.ToString();

            bool probableDecimalComma = delimiter == ';' && lines.Any(l => System.Text.RegularExpressions.Regex.IsMatch(l, "\\d,\\d"));
            diag.DecimalSeparator = probableDecimalComma ? "," : ".";

            var nfiDot = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone(); nfiDot.NumberDecimalSeparator = ".";
            var nfiComma = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone(); nfiComma.NumberDecimalSeparator = ",";

            foreach (var ln in lines)
            {
                var parts = ln.Split(delimiter);
                if (parts.Length < 3) continue;
                string s0 = parts[0].Trim();
                string s1 = parts[1].Trim();
                string s2 = parts[2].Trim();

                bool parsed;
                double s = 0, t = 0, e = 0;
                if (probableDecimalComma)
                {
                    parsed = double.TryParse(s0, NumberStyles.Float, nfiComma, out s) &&
                             double.TryParse(s1, NumberStyles.Float, nfiComma, out t) &&
                             double.TryParse(s2, NumberStyles.Float, nfiComma, out e);
                    if (!parsed)
                        parsed = double.TryParse(s0, NumberStyles.Float, nfiDot, out s) &&
                                 double.TryParse(s1, NumberStyles.Float, nfiDot, out t) &&
                                 double.TryParse(s2, NumberStyles.Float, nfiDot, out e);
                }
                else
                {
                    parsed = double.TryParse(s0, NumberStyles.Float, nfiDot, out s) &&
                             double.TryParse(s1, NumberStyles.Float, nfiDot, out t) &&
                             double.TryParse(s2, NumberStyles.Float, nfiDot, out e);
                    if (!parsed)
                        parsed = double.TryParse(s0, NumberStyles.Float, nfiComma, out s) &&
                                 double.TryParse(s1, NumberStyles.Float, nfiComma, out t) &&
                                 double.TryParse(s2, NumberStyles.Float, nfiComma, out e);
                }

                if (parsed) list.Add((s, t, e));
            }

            diag.ParsedCount = list.Count;
            return list;
        }

        private static List<double> BuildGrid(double min, double max, double step)
        {
            var g = new List<double>();
            for (double v = min; v <= max + 1e-9; v += step)
                g.Add(Math.Round(v, 1));
            if (g.Count == 0) g.Add(min);
            return g;
        }

        private static double IDW(double s, double t, List<(double Speed, double Torque, double Efficiency)> raw, int k, double power, double rangeS, double rangeT)
        {
            var neigh = raw.Select(p =>
            {
                double dx = (p.Speed - s) / rangeS;
                double dy = (p.Torque - t) / rangeT;
                double d = Math.Sqrt(dx * dx + dy * dy);
                return (d, p.Efficiency);
            }).OrderBy(o => o.d).Take(Math.Max(1, k)).ToList();

            if (neigh.Count > 0 && neigh[0].d < 1e-12) return neigh[0].Item2;

            double num = 0, den = 0;
            foreach (var (d, z) in neigh)
            {
                double w = 1.0 / Math.Pow(d + 1e-12, power);
                num += w * z; den += w;
            }
            return den > 0 ? num / den : neigh.Average(n => n.Item2);
        }

        private void ApplyZRange(ObservableCollection<SurfacePoint> pts, double zMin, double zMax)
        {
            if (SurfaceChart.ZAxis is SurfaceAxis zAxis)
            {
                zAxis.Minimum = zMin;
                zAxis.Maximum = zMax;
                Debug.WriteLine($"[Surface3D/MDI] ZAxis set to [{zMin}, {zMax}]");
            }
            TryForceColorMappingByZ();
        }

        private void ApplyPalette(double zMin, double zMax)
        {
            if (SurfaceChart.ColorModel == null)
                SurfaceChart.ColorModel = new ChartColorModel();

            int levels = (int)Math.Round(SldLevels.Value);
            if (levels < 2) levels = 2;

            // Use ComboBox selection
            var sel = CmbPalette?.SelectedIndex ?? 0;
            if (sel == 3) // Custom (Blue-Yellow-Red)
            {
                SetContinuousPaletteForZRange(zMin, zMax, Math.Max(64, levels * 16));
            }
            else
            {
                // Built-in palettes
                SurfaceChart.Palette = sel switch
                {
                    0 => ChartColorPalette.Metro,
                    1 => ChartColorPalette.BlueChrome,
                    2 => ChartColorPalette.GreenChrome,
                    _ => ChartColorPalette.Metro
                };
                // Reset to default model for built-in palette
                SurfaceChart.ColorModel = new ChartColorModel();
            }

            TryForceColorMappingByZ();
        }

        // 원래 ApplyPalette - slider changed handler에서 호출
        private void ApplyPalette()
        {
            var pts = SurfaceChart.ItemsSource as ObservableCollection<SurfacePoint>;
            if (pts == null || pts.Count == 0) return;
            // Y 범위(효율성)를 기반으로 팔레트 생성
            double yMin = pts.Min(p => p.Y);
            double yMax = pts.Max(p => p.Y);
            ApplyPalette(yMin, yMax);
        }

        // Z 범위를 기반으로 연속 팔레트 생성
        private void SetContinuousPaletteForZRange(double zMin, double zMax, int steps = 256)
        {
            if (SurfaceChart.ColorModel == null)
                SurfaceChart.ColorModel = new ChartColorModel();

            SurfaceChart.Palette = ChartColorPalette.Custom;
            var model = new ChartColorModel();
            model.CustomBrushes.Clear();

            steps = Math.Max(64, steps);
            for (int i = 0; i < steps; i++)
            {
                double t = steps == 1 ? 1.0 : (double)i / (steps - 1);
                model.CustomBrushes.Add(new SolidColorBrush(LerpBlueYellowRed(t)));
            }
            SurfaceChart.ColorModel = model;
            Debug.WriteLine($"[Surface3D/MDI] SetContinuousPaletteForZRange: steps={steps}, zRange=[{zMin},{zMax}]");
        }

        // Try to force Z-based color mapping if the SDK exposes such a property
        private void TryForceColorMappingByZ()
        {
            try
            {
                ForceZMappingOn(SurfaceChart);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Surface3D/MDI] TryForceColorMappingByZ failed: {ex.Message}");
            }
        }

        private void ForceZMappingOn(object target)
        {
            var type = target.GetType();
            // 1) enum-like mapping properties
            foreach (var p in type.GetProperties())
            {
                if (!p.CanWrite) continue;
                var name = p.Name;
                if (name.IndexOf("Mapping", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("Mode", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ColorScale", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (p.PropertyType.IsEnum)
                    {
                        var enumNames = Enum.GetNames(p.PropertyType);
                        var pick = enumNames.FirstOrDefault(n => n.IndexOf("ByZ", StringComparison.OrdinalIgnoreCase) >= 0)
                                  ?? enumNames.FirstOrDefault(n => n.IndexOf("Z", StringComparison.OrdinalIgnoreCase) >= 0)
                                  ?? enumNames.FirstOrDefault(n => n.IndexOf("Value", StringComparison.OrdinalIgnoreCase) >= 0)
                                  ?? enumNames.FirstOrDefault(n => n.IndexOf("Data", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (pick != null)
                        {
                            var val = Enum.Parse(p.PropertyType, pick);
                            p.SetValue(target, val);
                            Debug.WriteLine($"[Surface3D/MDI] {type.Name}.{name} = {pick}");
                        }
                    }
                }
            }

            // 2) value path-like properties
            var zPath = nameof(SurfacePoint.Z);
            foreach (var p in type.GetProperties())
            {
                if (!p.CanWrite) continue;
                var name = p.Name;
                if (name.IndexOf("ColorValuePath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ValuePath", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    name.IndexOf("ColorPath", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    if (p.PropertyType == typeof(string))
                    {
                        p.SetValue(target, zPath);
                        Debug.WriteLine($"[Surface3D/MDI] {type.Name}.{name} = '{zPath}'");
                    }
                }
            }
        }

        private static Color LerpBlueYellowRed(double t)
        {
            t = Math.Clamp(t, 0, 1);
            if (t < 0.5)
            {
                double u = t / 0.5;
                return Color.FromScRgb(1f, (float)u, (float)u, (float)(1 - u));
            }
            else
            {
                double u = (t - 0.5) / 0.5;
                return Color.FromScRgb(1f, 1f, (float)(1 - u), 0f);
            }
        }

        private class SurfacePoint
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        // ===== Window chrome (drag/move/resize) =====
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
                return;
            }

            _isDragging = true;
            var canvas = (Canvas)this.Parent;
            _lastPosition = e.GetPosition(canvas);
            this.CaptureMouse();
            MdiZOrderService.BringToFront(this);
            WindowActivated?.Invoke(this, EventArgs.Empty);
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

            if (newLeft >= 0 && newLeft + newWidth <= canvas.ActualWidth && newTop >= 0 && newTop + newHeight <= canvas.ActualHeight)
            {
                this.Width = newWidth;
                this.Height = newHeight;
                Canvas.SetLeft(this, newLeft);
                Canvas.SetTop(this, newTop);
            }
        }

        private void ResizeTop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.Top, e.GetPosition((Canvas)this.Parent));
        private void ResizeBottom_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.Bottom, e.GetPosition((Canvas)this.Parent));
        private void ResizeLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.Left, e.GetPosition((Canvas)this.Parent));
        private void ResizeRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.Right, e.GetPosition((Canvas)this.Parent));
        private void ResizeTopLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.TopLeft, e.GetPosition((Canvas)this.Parent));
        private void ResizeTopRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.TopRight, e.GetPosition((Canvas)this.Parent));
        private void ResizeBottomLeft_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.BottomLeft, e.GetPosition((Canvas)this.Parent));
        private void ResizeBottomRight_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => StartResize(ResizeDirection.BottomRight, e.GetPosition((Canvas)this.Parent));

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
                this.Width = Math.Max(300, canvas.ActualWidth - 10);
                this.Height = Math.Max(200, canvas.ActualHeight - 10);
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
            var canvas = this.Parent as Canvas;
            canvas?.Children.Remove(this);
        }
    }
}
