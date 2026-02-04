using System;
using System.Collections.Generic;
using System.Windows;
using MGK_Analyzer.Services.Reporting;
using Microsoft.Win32;

namespace MGK_Analyzer.Windows
{
    public partial class AnalysisResultWindow : Window
    {
        private readonly AnalysisResultViewModel _viewModel;
        private readonly AnalysisResultWordExporter _wordExporter = new();

        public AnalysisResultWindow(AnalysisResultViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
            _viewModel = vm;
        }

        private void ExportToWord_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Word document (*.docx)|*.docx",
                FileName = string.IsNullOrWhiteSpace(_viewModel.ReportTitle) ? "analysis-result" : _viewModel.ReportTitle
            };

            if (dialog.ShowDialog(this) != true)
            {
                return;
            }

            try
            {
                _wordExporter.Export(_viewModel, dialog.FileName);
                MessageBox.Show(this, $"Analysis report saved to {dialog.FileName}.", "Export complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Failed to export Word report: {ex.Message}", "Export failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    public class AnalysisResultViewModel
    {
		public required string ReportTitle { get; init; }
		public required string ReportDate { get; init; }
		public required string TesterName { get; init; }
		public required string NoLoadCsvPath { get; init; }
		public required string LoadCsvPath { get; init; }

		public required string MotorRatedVoltage { get; init; }
		public required string MotorRatedFrequency { get; init; }
		public required string MotorPoleCount { get; init; }
		public required string MotorRatedPower { get; init; }

		public required string TempCoolant { get; init; }
		public required string TempConductorCoeff { get; init; }
		public required string TempResistanceMeasure { get; init; }
		public required string TempWindingRated { get; init; }

		public required string DcR12 { get; init; }
		public required string DcR23 { get; init; }
		public required string DcR31 { get; init; }

        public required string LldA { get; init; }
        public required string LldB { get; init; }
        public required string Correlation { get; init; }
        public required IReadOnlyList<AnalysisResultRow> Rows { get; init; }
		public required IReadOnlyList<ResidualLossChartPoint> ResidualLoss { get; init; }

		public required RatedSummaryRowVm RatedSummary { get; init; }
		public required IReadOnlyList<NoLoadCalcRowVm> NoLoadCalcRows { get; init; }
		public required NoLoadSummaryVm NoLoadSummary { get; init; }
		public required IReadOnlyList<LoadCalcRowVm> LoadCalcRows { get; init; }
		public required IReadOnlyList<ResidualAdditionalLossRowVm> ResidualAdditionalLossRows { get; init; }
		public required IReadOnlyList<FinalLoadSummaryRowVm> FinalLoadSummaryRows { get; init; }

		public required IReadOnlyList<ResidualLossScatterPoint> ResidualScatterMeasured { get; init; }
		public required IReadOnlyList<ResidualLossScatterPoint> ResidualScatterFit { get; init; }
		public required IReadOnlyList<ResidualLossScatterPoint> AdditionalLossScatter { get; init; }
    }

	public class ResidualLossChartPoint
	{
		public double TorqueSquared { get; init; }
		public double ResidualLoss { get; init; }
	}

	public class RatedSummaryRowVm
	{
		public double LoadPct { get; init; }
		public double InputPowerP1 { get; init; }
		public double OutputPowerP2 { get; init; }
		public double StatorCopperLossPCuS { get; init; }
		public double RotorCopperLossPCuR { get; init; }
		public double ResidualLossPRes { get; init; }
		public double IronLossPFe { get; init; }
		public double FrictionWindageLossPFw { get; init; }
		public double AdditionalLossPLL { get; init; }
		public double TotalLossPLoss { get; init; }
		public double Efficiency { get; init; }
	}

	public class NoLoadCalcRowVm
	{
		public double StepPoint { get; init; }
		public double Rll { get; init; }
		public double StatorCopperLoss { get; init; }
		public double CorrectedNoLoadPower { get; init; }
	}

	public class NoLoadSummaryVm
	{
		public double FrictionWindageLoss { get; init; }
		public double IronLoss { get; init; }
	}

	public class LoadCalcRowVm
	{
		public double LoadPct { get; init; }
		public double OutputPowerP2 { get; init; }
		public double SyncSpeedNs { get; init; }
		public double Slip { get; init; }
		public double RllThetaW { get; init; }
		public double StatorCopperLoss { get; init; }
		public double RotorCopperLoss { get; init; }
	}

	public class ResidualAdditionalLossRowVm
	{
		public double LoadPct { get; init; }
		public double ResidualLossPRes { get; init; }
		public double ResidualLossFitPLr { get; init; }
		public double AdditionalLossPll { get; init; }
		public double AdditionalLossEffPll { get; init; }
	}

	public class FinalLoadSummaryRowVm
	{
		public double LoadPct { get; init; }
		public double RllThetaW { get; init; }
		public double StatorCopperLoss { get; init; }
		public double RotorCopperLoss { get; init; }
		public double ResidualLossPRes { get; init; }
		public double AdditionalLossPll { get; init; }
		public double FrictionWindageLossPFw { get; init; }
		public double IronLossPFe { get; init; }
		public double Efficiency { get; init; }
	}

	public class ResidualLossScatterPoint
	{
		public double TorqueSquared { get; init; }
		public double Loss { get; init; }
	}

    public class AnalysisResultRow
    {
        public double LoadPct { get; init; }
        public double InputPower { get; init; }
        public double OutputPower { get; init; }
        public double Efficiency { get; init; }
    }
}
