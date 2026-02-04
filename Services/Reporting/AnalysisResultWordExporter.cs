using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Syncfusion.DocIO;
using Syncfusion.DocIO.DLS;
using Syncfusion.Drawing;
using Syncfusion.OfficeChart;
using MGK_Analyzer.Windows;

namespace MGK_Analyzer.Services.Reporting
{
    public sealed class AnalysisResultWordExporter
    {
        public void Export(AnalysisResultViewModel vm, string filePath)
        {
            using var document = new WordDocument();
            document.EnsureMinimal();

            WSection section = document.LastSection;
            section.PageSetup.Orientation = PageOrientation.Portrait;
            section.PageSetup.PageSize = new Syncfusion.Drawing.SizeF(595f, 842f);
            section.PageSetup.Margins.All = 72f;

            AppendTitle(section, vm.ReportTitle);
            AppendMetadata(section, vm);
            AppendInputMetrics(section, vm);
            AppendOverviewMetrics(section, vm);
            AppendTable(section, "No-Load Calculations", new[] { "STEP (%)", "RLL", "P_Cu,s0 (W)", "P0' (W)" }, vm.NoLoadCalcRows, row => new[]
            {
                FormatNumber(row.StepPoint, "0.##"),
                FormatNumber(row.Rll),
                FormatNumber(row.StatorCopperLoss),
                FormatNumber(row.CorrectedNoLoadPower)
            });
            AppendTable(section, "Load Calculations", new[] { "STEP (%)", "P2 (W)", "ns (rpm)", "Slip", "RLL(theta_w)", "P_Cu,s (W)", "P_Cu,r (W)" }, vm.LoadCalcRows, row => new[]
            {
                FormatNumber(row.LoadPct, "0.##"),
                FormatNumber(row.OutputPowerP2),
                FormatNumber(row.SyncSpeedNs),
                FormatNumber(row.Slip),
                FormatNumber(row.RllThetaW),
                FormatNumber(row.StatorCopperLoss),
                FormatNumber(row.RotorCopperLoss)
            });
            AppendTable(section, "Final Load Summary", new[] { "STEP (%)", "RLL(theta_w)", "P_Cu,s", "P_Cu,r", "P_res", "P_LL", "Friction", "Iron", "Efficiency" }, vm.FinalLoadSummaryRows, row => new[]
            {
                FormatNumber(row.LoadPct, "0.##"),
                FormatNumber(row.RllThetaW),
                FormatNumber(row.StatorCopperLoss),
                FormatNumber(row.RotorCopperLoss),
                FormatNumber(row.ResidualLossPRes),
                FormatNumber(row.AdditionalLossPll),
                FormatNumber(row.FrictionWindageLossPFw),
                FormatNumber(row.IronLossPFe),
                FormatNumber(row.Efficiency, "0.##")
            });

            AppendResidualChart(section, vm);

            document.Save(filePath, FormatType.Docx);
        }

        private static void AppendResidualChart(WSection section, AnalysisResultViewModel vm)
        {
            var seriesList = new List<(string Name, IReadOnlyList<ResidualLossScatterPoint> Points)>
            {
                ("P_res", vm.ResidualScatterMeasured),
                ("P_Lr_fit", vm.ResidualScatterFit),
                ("P_LL", vm.AdditionalLossScatter)
            };

            if (seriesList.All(s => s.Points == null || s.Points.Count == 0))
            {
                return;
            }

            AddSubheading(section, "Residual/Additional Loss Chart");
            var paragraph = section.AddParagraph();
            var chart = new WChart(section.Document)
            {
                ChartType = OfficeChartType.Line_Markers,
                Width = section.PageSetup.ClientWidth,
                Height = 280,
                ChartTitle = "Residual/Additional Loss vs Torque^2"
            };
            paragraph.ChildEntities.Add(chart);
            chart.PrimaryCategoryAxis.Title = "Torque^2 (Nm^2)";
            chart.PrimaryValueAxis.Title = "Loss (W)";

            var maxCount = seriesList.Max(s => s.Points?.Count ?? 0);
            if (maxCount == 0)
            {
                return;
            }

            // Header row
            chart.ChartData.SetValue(1, 1, "Torque^2");
            var columnIndex = 2;
            foreach (var (name, points) in seriesList)
            {
                if (points == null || points.Count == 0)
                {
                    columnIndex++;
                    continue;
                }

                chart.ChartData.SetValue(1, columnIndex, name);
                var sorted = points.OrderBy(p => p.TorqueSquared).ToList();
                for (var i = 0; i < sorted.Count; i++)
                {
                    chart.ChartData.SetValue(i + 2, 1, sorted[i].TorqueSquared);
                    chart.ChartData.SetValue(i + 2, columnIndex, sorted[i].Loss);
                }

                var serie = chart.Series.Add(name);
                serie.CategoryLabels = chart.ChartData[2, 1, sorted.Count + 1, 1];
                serie.Values = chart.ChartData[2, columnIndex, sorted.Count + 1, columnIndex];
                serie.SerieFormat.MarkerStyle = OfficeChartMarkerType.Circle;

                columnIndex++;
            }

            paragraph.ParagraphFormat.AfterSpacing = 12f;
        }

        private static void AppendTitle(WSection section, string title)
        {
            var paragraph = section.AddParagraph();
            var textRange = paragraph.AppendText(string.IsNullOrWhiteSpace(title) ? "Analysis Result" : title);
            paragraph.ParagraphFormat.HorizontalAlignment = HorizontalAlignment.Center;
            textRange.CharacterFormat.Bold = true;
            textRange.CharacterFormat.FontSize = 18f;
            paragraph.ParagraphFormat.AfterSpacing = 12f;
        }

        private static void AppendMetadata(WSection section, AnalysisResultViewModel vm)
        {
            var info = new[]
            {
                new KeyValuePair<string, string>("Report Date", vm.ReportDate),
                new KeyValuePair<string, string>("Tester", vm.TesterName),
                new KeyValuePair<string, string>("No-Load CSV", vm.NoLoadCsvPath),
                new KeyValuePair<string, string>("Load CSV", vm.LoadCsvPath)
            };

            AppendKeyValueTable(section, "Source", info);
        }

        private static void AppendInputMetrics(WSection section, AnalysisResultViewModel vm)
        {
            var inputs = new[]
            {
                new KeyValuePair<string, string>("Rated Voltage", vm.MotorRatedVoltage),
                new KeyValuePair<string, string>("Rated Frequency", vm.MotorRatedFrequency),
                new KeyValuePair<string, string>("Pole Count", vm.MotorPoleCount),
                new KeyValuePair<string, string>("Rated Power", vm.MotorRatedPower),
                new KeyValuePair<string, string>("Coolant Temp", vm.TempCoolant),
                new KeyValuePair<string, string>("Conductor Coeff", vm.TempConductorCoeff),
                new KeyValuePair<string, string>("Resistance Measure", vm.TempResistanceMeasure),
                new KeyValuePair<string, string>("Winding Rated", vm.TempWindingRated)
            };

            AppendKeyValueTable(section, "Inputs", inputs);
        }

        private static void AppendOverviewMetrics(WSection section, AnalysisResultViewModel vm)
        {
            var overview = new[]
            {
                new KeyValuePair<string, string>("Regression A", vm.LldA),
                new KeyValuePair<string, string>("Regression B", vm.LldB),
                new KeyValuePair<string, string>("Correlation", vm.Correlation),
                new KeyValuePair<string, string>("DC R12", vm.DcR12),
                new KeyValuePair<string, string>("DC R23", vm.DcR23),
                new KeyValuePair<string, string>("DC R31", vm.DcR31)
            };

            AppendKeyValueTable(section, "Overview", overview);
        }

        private static void AppendKeyValueTable(WSection section, string title, IEnumerable<KeyValuePair<string, string>> rows)
        {
            var entries = rows.Where(kvp => !string.IsNullOrWhiteSpace(kvp.Value)).ToList();
            if (entries.Count == 0)
            {
                return;
            }

            AddSubheading(section, title);

            var table = (WTable)section.AddTable();
            table.ResetCells(entries.Count + 1, 2);
            foreach (WTableRow row in table.Rows)
            {
                row.RowFormat.IsBreakAcrossPages = false;
            }
            SetCell(table, 0, 0, "Field", true);
            SetCell(table, 0, 1, "Value", true);

            for (var i = 0; i < entries.Count; i++)
            {
                SetCell(table, i + 1, 0, entries[i].Key);
                SetCell(table, i + 1, 1, entries[i].Value);
            }

            section.AddParagraph();
        }

        private static void AppendTable<T>(WSection section, string title, IReadOnlyList<string> headers, IEnumerable<T> rows, Func<T, IReadOnlyList<string>> extractor)
        {
            var normalizedRows = rows.Select(extractor).Where(row => row != null).ToList();
            if (normalizedRows.Count == 0)
            {
                return;
            }

            AddSubheading(section, title);

            var table = (WTable)section.AddTable();
            table.ResetCells(normalizedRows.Count + 1, headers.Count);
            foreach (WTableRow row in table.Rows)
            {
                row.RowFormat.IsBreakAcrossPages = false;
            }

            for (var c = 0; c < headers.Count; c++)
            {
                SetCell(table, 0, c, headers[c], true);
            }

            for (var r = 0; r < normalizedRows.Count; r++)
            {
                var rowValues = normalizedRows[r];
                for (var c = 0; c < headers.Count; c++)
                {
                    var value = c < rowValues.Count ? rowValues[c] : string.Empty;
                    SetCell(table, r + 1, c, value);
                }
            }

            table.ApplyStyle(BuiltinTableStyle.MediumShading1Accent1);
            section.AddParagraph();
        }

        private static void SetCell(WTable table, int row, int column, string text, bool isHeader = false)
        {
            var cell = table[row, column];
            var paragraph = cell.AddParagraph();
            var run = paragraph.AppendText(text ?? string.Empty);
            if (isHeader)
            {
                run.CharacterFormat.Bold = true;
            }
        }

        private static void AddSubheading(WSection section, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var paragraph = section.AddParagraph();
            var textRange = paragraph.AppendText(text);
            textRange.CharacterFormat.Bold = true;
            textRange.CharacterFormat.FontSize = 12f;
            paragraph.ParagraphFormat.BeforeSpacing = 6f;
            paragraph.ParagraphFormat.AfterSpacing = 4f;
        }

        private static string FormatNumber(double value, string format = "0.###")
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                return "-";
            }

            return value.ToString(format, CultureInfo.InvariantCulture);
        }
    }
}
