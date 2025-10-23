using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Charts;
using MGK_Analyzer.Services;

namespace MGK_Analyzer.Windows
{
    public partial class EfficiencySurface3DWindow : Window
    {
        private static readonly string RawData = EfficiencySampleData.Csv;

        private sealed class RawPoint
        {
            public double Speed;
            public double Torque;
            public double Efficiency;
        }

        public class Data
        {
            public double X { get; set; }
            public double Y { get; set; }
            public double Z { get; set; }
        }

        private sealed class LegendItem
        {
            public Brush Fill { get; set; }
            public string Label { get; set; }
        }

        public System.Collections.ObjectModel.ObservableCollection<Data> DataValues { get; } = new System.Collections.ObjectModel.ObservableCollection<Data>();
        public int RowSize { get; set; }
        public int ColumnSize { get; set; }

        public EfficiencySurface3DWindow()
        {
            InitializeComponent();

            // 기본을 연속(Custom) 팔레트로 구성
            SurfaceChart.ColorModel = new ChartColorModel();
            SetContinuousPaletteForZRange(0, 100, 256);  // Default palette before data load

            SldTilt.ValueChanged     += (_, __) => SurfaceChart.Tilt = SldTilt.Value;
            SldRotate.ValueChanged   += (_, __) => SurfaceChart.Rotate = SldRotate.Value;
            SldLevels.ValueChanged   += (_, __) => { ApplyPalette(); UpdateZLegend(); };
            SldOpacity.ValueChanged  += (_, __) => SurfaceChart.Opacity = SldOpacity.Value;
            CmbPalette.SelectionChanged += (_, __) => { ApplyPalette(); UpdateZLegend(); };
            BtnResetView.Click += (_, __) => ResetView();

            LoadAndRender();   // SURFACE3D
        }

        private void LoadAndRender()
        {
            var raw = ParseRawData(RawData, out var diag); // SURFACE3D DIAG
            Debug.WriteLine($"[Surface3D] CSV lines={diag.TotalLines}, dataLines={diag.DataLines}, parsed={diag.ParsedCount}, delim='{diag.Delimiter}', dec='{diag.DecimalSeparator}', firstData='{diag.FirstDataLine}'");

            if (raw.Count == 0)
            {
                var msg =
                    "CSV 데이터 파싱 실패\n" +
                    $"- 전체 라인: {diag.TotalLines}, 데이터 라인: {diag.DataLines}\n" +
                    $"- 구분자 추정: '{diag.Delimiter}', 소수점: '{diag.DecimalSeparator}'\n" +
                    "- 원인: 데이터 비어 있음 또는 구분자/소수점 불일치\n\n" +
                    "임시 3x3 샘플 데이터로 대체하여 렌더링을 시도합니다.";
                MessageBox.Show(msg, "Surface3D CSV 파싱 오류", MessageBoxButton.OK, MessageBoxImage.Warning);

                raw = new List<RawPoint>
                {
                    new RawPoint{ Speed=1000, Torque= 50, Efficiency=80}, new RawPoint{ Speed=2000, Torque= 50, Efficiency=85}, new RawPoint{ Speed=3000, Torque= 50, Efficiency=83},
                    new RawPoint{ Speed=1000, Torque=150, Efficiency=88}, new RawPoint{ Speed=2000, Torque=150, Efficiency=92}, new RawPoint{ Speed=3000, Torque=150, Efficiency=90},
                    new RawPoint{ Speed=1000, Torque=250, Efficiency=82}, new RawPoint{ Speed=2000, Torque=250, Efficiency=89}, new RawPoint{ Speed=3000, Torque=250, Efficiency=87},
                };
                diag.ParsedCount = raw.Count;
            }

            double sMin = raw.Min(p => p.Speed),  sMax = raw.Max(p => p.Speed);
            double tMin = raw.Min(p => p.Torque), tMax = raw.Max(p => p.Torque);

            // Grid density
            var speedGrid  = BuildGrid(sMin, sMax, 150.0);
            var torqueGrid = BuildGrid(tMin, tMax, 15.0);

            // ?? CRITICAL: RowSize = outer loop (Torque), ColumnSize = inner loop (Speed)
            // Our loop: foreach(torque) { foreach(speed) { ... } }
            // So: RowSize = torque count, ColumnSize = speed count
            RowSize    = torqueGrid.Count;
            ColumnSize = speedGrid.Count;

            if (ColumnSize < 2 || RowSize < 2)
            {
                MessageBox.Show($"SurfaceGrid 크기가 너무 작습니다. (grid={RowSize}×{ColumnSize})\n최소 2×2 이상이어야 Surface 메쉬가 생성됩니다.", "Surface3D Grid 경고", MessageBoxButton.OK, MessageBoxImage.Information); // DIAG
            }

            double rangeS = Math.Max(1e-9, sMax - sMin);
            double rangeT = Math.Max(1e-9, tMax - tMin);

            var exact = new Dictionary<(string sx, string ty), double>();
            foreach (var p in raw)
            {
                var k = (Math.Round(p.Speed, 1).ToString("F1", CultureInfo.InvariantCulture),
                         Math.Round(p.Torque, 1).ToString("F1", CultureInfo.InvariantCulture));
                exact[k] = p.Efficiency;
            }

            DataValues.Clear();
            foreach (var t in torqueGrid)
            {
                foreach (var s in speedGrid)
                {
                    var key = ($"{s:F1}", $"{t:F1}");
                    double z = exact.TryGetValue(key, out var hit) ? hit : IDW(s, t, raw, 12, 2.0, rangeS, rangeT);
                    // X=Speed, Y=Efficiency, Z=Torque (changed from X=Speed, Y=Torque, Z=Efficiency)
                    DataValues.Add(new Data { X = s, Y = z, Z = t });
                }
            }

            // DIAG: Count consistency
            int expected = RowSize * ColumnSize;
            if (DataValues.Count != expected)
            {
                MessageBox.Show($"ItemsSource 크기({DataValues.Count})와 Grid 크기({RowSize}×{ColumnSize}={expected})가 일치하지 않습니다.", "Surface3D 데이터 경고", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // Calculate Z range BEFORE setting ItemsSource
            double zMin = DataValues.Count > 0 ? DataValues.Min(r => r.Z) : 0;
            double zMax = DataValues.Count > 0 ? DataValues.Max(r => r.Z) : 1;
            Debug.WriteLine($"[Surface3D] Z Range: {zMin} - {zMax}");

            SurfaceChart.RowSize      = RowSize;     // set before ItemsSource
            SurfaceChart.ColumnSize   = ColumnSize;
            SurfaceChart.XBindingPath = nameof(Data.X);
            SurfaceChart.YBindingPath = nameof(Data.Y);
            SurfaceChart.ZBindingPath = nameof(Data.Z);
            SurfaceChart.ItemsSource  = DataValues;

            Debug.WriteLine($"[Surface3D-CRITICAL] RowSize={RowSize}, ColumnSize={ColumnSize}");
            Debug.WriteLine($"[Surface3D-CRITICAL] XBindingPath={SurfaceChart.XBindingPath}, YBindingPath={SurfaceChart.YBindingPath}, ZBindingPath={SurfaceChart.ZBindingPath}");
            if (DataValues.Count > 0)
            {
                Debug.WriteLine($"[Surface3D-CRITICAL] First 3 DataValues:");
                for (int i = 0; i < Math.Min(3, DataValues.Count); i++)
                {
                    var d = DataValues[i];
                    Debug.WriteLine($"  [{i}] X={d.X}, Y={d.Y}, Z={d.Z}");
                }
            }

            SurfaceChart.Type = SurfaceType.Surface;
            ApplyZRange(zMin, zMax);      // Pass Z range explicitly
            ApplyPalette(zMin, zMax);     // Pass Z range to generate correct palette
            UpdateZLegend();    // display legend only

            this.Title = $"3D Efficiency Surface | parsed={diag.ParsedCount} pts | CSV lines={diag.TotalLines} | grid={RowSize}×{ColumnSize}"; // DIAG

            SurfaceChart.InvalidateMeasure();
            SurfaceChart.InvalidateVisual();
        }

        private void UpdateZLegend()
        {
            try
            {
                double zMin = DataValues.Count > 0 ? DataValues.Min(r => r.Z) : 0;
                double zMax = DataValues.Count > 0 ? DataValues.Max(r => r.Z) : 1;
                int levels = Math.Max(2, (int)Math.Round(SldLevels.Value));

                // Sample directly from continuous custom palette
                var brushesSource = SurfaceChart.ColorModel?.CustomBrushes;
                var brushList = (brushesSource != null && brushesSource.Count > 0)
                    ? brushesSource.ToList()
                    : new List<Brush>();

                if (brushList.Count == 0)
                {
                    brushList.Add(new SolidColorBrush(Colors.Blue));
                    brushList.Add(new SolidColorBrush(Colors.Red));
                }

                var brushes = new List<Brush>();
                for (int i = 0; i < levels; i++)
                {
                    int idx = brushList.Count <= 1 ? 0 : (int)Math.Round(i * (brushList.Count - 1) / (double)(levels - 1));
                    brushes.Add(brushList[idx]);
                }

                double step = (zMax - zMin) / levels;
                string fmt = (SurfaceChart.ZAxis as SurfaceAxis)?.LabelFormat ?? "0.0";
                var items = new List<LegendItem>();
                double start = zMin;
                for (int i = 0; i < levels; i++)
                {
                    double end = (i == levels - 1) ? zMax : start + step;
                    items.Add(new LegendItem
                    {
                        Fill = brushes[Math.Min(i, brushes.Count - 1)],
                        Label = $"{start.ToString(fmt, CultureInfo.InvariantCulture)} - {end.ToString(fmt, CultureInfo.InvariantCulture)}"
                    });
                    start = end;
                }

                ZLegend.ItemsSource = items; // display-only legend
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Surface3D] UpdateZLegend error: {ex.Message}");
            }
        }

        private class CsvParseDiag
        {
            public int TotalLines { get; set; }
            public int DataLines { get; set; }
            public int ParsedCount { get; set; }
            public string Delimiter { get; set; } = ",";
            public string DecimalSeparator { get; set; } = ".";
            public string FirstDataLine { get; set; } = string.Empty;
        }

        private static List<RawPoint> ParseRawData(string csv, out CsvParseDiag diag)
        {
            diag = new CsvParseDiag();
            var list = new List<RawPoint>();
            if (string.IsNullOrWhiteSpace(csv)) return list;

            using var reader = new StringReader(csv);
            string? line; _ = reader.ReadLine(); // header
            var lines = new List<string>();
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
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
                    {
                        parsed = double.TryParse(s0, NumberStyles.Float, nfiDot, out s) &&
                                 double.TryParse(s1, NumberStyles.Float, nfiDot, out t) &&
                                 double.TryParse(s2, NumberStyles.Float, nfiDot, out e);
                    }
                }
                else
                {
                    parsed = double.TryParse(s0, NumberStyles.Float, nfiDot, out s) &&
                             double.TryParse(s1, NumberStyles.Float, nfiDot, out t) &&
                             double.TryParse(s2, NumberStyles.Float, nfiDot, out e);
                    if (!parsed)
                    {
                        parsed = double.TryParse(s0, NumberStyles.Float, nfiComma, out s) &&
                                 double.TryParse(s1, NumberStyles.Float, nfiComma, out t) &&
                                 double.TryParse(s2, NumberStyles.Float, nfiComma, out e);
                    }
                }

                if (parsed)
                {
                    list.Add(new RawPoint { Speed = s, Torque = t, Efficiency = e });
                }
            }

            diag.ParsedCount = list.Count;
            return list;
        }

        private static List<double> BuildGrid(double min, double max, double step)
        {
            var g = new List<double>();
            for (double v = min; v <= max + 1e-9; v += step) g.Add(Math.Round(v, 1));
            if (g.Count == 0) g.Add(min);
            return g;
        }

        private static double IDW(double s, double t, List<RawPoint> raw, int k, double power, double rangeS, double rangeT)
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

        private void ApplyZRange(double zMin, double zMax)
        {
            if (SurfaceChart.ZAxis is SurfaceAxis zAxis)
            {
                zAxis.Minimum = zMin;
                zAxis.Maximum = zMax;
                Debug.WriteLine($"[Surface3D] ZAxis set to [{zMin}, {zMax}]");
            }
            TryForceColorMappingByZ();
        }

        private void ApplyPalette(double zMin, double zMax)
        {
            if (SurfaceChart.ColorModel == null) SurfaceChart.ColorModel = new ChartColorModel();

            int levels = (int)Math.Round(SldLevels.Value);
            if (levels < 2) levels = 2;

            // 생성 시 Z 범위를 기반으로 색상 생성
            SetContinuousPaletteForZRange(zMin, zMax, Math.Max(64, levels * 16));

            TryForceColorMappingByZ();
        }

        // 원래 ApplyPalette - slider changed handler에서 호출
        private void ApplyPalette()
        {
            if (DataValues.Count == 0) return;
            double zMin = DataValues.Min(r => r.Z);
            double zMax = DataValues.Max(r => r.Z);
            ApplyPalette(zMin, zMax);
        }

        // Z 범위를 기반으로 팔레트 생성
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
            Debug.WriteLine($"[Surface3D] SetContinuousPaletteForZRange: steps={steps}, zRange=[{zMin},{zMax}]");
        }

        // Try to force Z-based color mapping if the SDK exposes such a property (done via reflection to avoid hard dependency)
        private void TryForceColorMappingByZ()
        {
            try
            {
                ForceZMappingOn(SurfaceChart);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Surface3D] TryForceColorMappingByZ failed: {ex.Message}");
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
                            Debug.WriteLine($"[Surface3D] {type.Name}.{name} = {pick}");
                        }
                    }
                }
            }

            // 2) value path-like properties
            var zPath = nameof(Data.Z);
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
                        Debug.WriteLine($"[Surface3D] {type.Name}.{name} = '{zPath}'");
                    }
                }
            }
        }

        private static Color LerpBlueYellowRed(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
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

        private void ResetView()
        {
            SldTilt.Value = 15;
            SldRotate.Value = 30;
            SurfaceChart.Tilt = 15;
            SurfaceChart.Rotate = 30;
            SurfaceChart.Opacity = SldOpacity.Value = 1.0;
        }
    }
}
