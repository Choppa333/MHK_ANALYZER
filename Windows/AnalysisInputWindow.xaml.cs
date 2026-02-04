using System.Linq;
using System.Windows;
using MGK_Analyzer.Services;
using MGK_Analyzer.Services.Analysis;
using MGK_Analyzer.ViewModels;
namespace MGK_Analyzer.Windows
{
    public partial class AnalysisInputWindow : Window
    {
        public AnalysisInputWindow()
        {
            InitializeComponent();
        }

        private async void RunSampleAnalysis_Click(object sender, RoutedEventArgs e)
        {
            // Run analysis using current input values.
            if (DataContext is not AnalysisInputViewModel inputVm)
            {
                MessageBox.Show("Input view model is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

			var csvService = new LossAnalysisCsvService();
			var noLoadTask = System.Threading.Tasks.Task.Run(() => csvService.ValidateAndComputeNoLoad(inputVm.NoLoadCsvPath));
			var loadTask = System.Threading.Tasks.Task.Run(() => csvService.ValidateAndComputeLoad(inputVm.LoadCsvPath));
			var noLoadResult = await noLoadTask.ConfigureAwait(true);
			var loadResult = await loadTask.ConfigureAwait(true);

			System.Console.WriteLine("=== CSV Validation (No-Load) ===");
			System.Console.WriteLine(LossAnalysisCsvService.FormatValidationMessages(noLoadResult.Messages));
			System.Console.WriteLine("=== CSV Validation (Load) ===");
			System.Console.WriteLine(LossAnalysisCsvService.FormatValidationMessages(loadResult.Messages));

			var hasError = noLoadResult.Messages.Any(m => m.Severity == LossAnalysisCsvService.ValidationSeverity.Error) ||
			               loadResult.Messages.Any(m => m.Severity == LossAnalysisCsvService.ValidationSeverity.Error);
			if (hasError)
			{
				MessageBox.Show("CSV 검증 실패: 콘솔 출력의 [Error] 항목을 확인하세요.", "Analysis", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			var noLoad = LossAnalysisCsvService.ToNoLoadPoints(noLoadResult.Points);
			var load = LossAnalysisCsvService.ToLoadPoints(loadResult.Points);

			System.Console.WriteLine(LossAnalysisCsvService.FormatNoLoadSummary(noLoadResult.Points));
			System.Console.WriteLine(LossAnalysisCsvService.FormatLoadSummary(loadResult.Points));

            var engine = new EfficiencyAnalysisEngine();
            var result = engine.Analyze(new AnalysisInput(
                Motor: new BasicMotorInfo(
                    // Align defaults with lib_test sample to reproduce A/B/Y.
                    RatedVoltage: inputVm.RatedVoltage > 0 ? inputVm.RatedVoltage : 380,
                    RatedFrequencyHz: inputVm.RatedFrequencyHz > 0 ? inputVm.RatedFrequencyHz : 60,
                    PoleCount: inputVm.PoleCount > 0 ? inputVm.PoleCount : 4,
                    RatedPowerW: inputVm.RatedPowerKw > 0 ? inputVm.RatedPowerKw * 1000.0 : 3700),
                Temperature: new TemperatureCorrectionInfo(
                    CoolantTempC: inputVm.CoolantTemp != 0 ? inputVm.CoolantTemp : (inputVm.TemperatureC != 0 ? inputVm.TemperatureC : 25),
                    ConductorTempCoeff: inputVm.ConductorTempCoeff != 0 ? inputVm.ConductorTempCoeff : 235,
                    ResistanceMeasureTempC: inputVm.ResistanceMeasureTemp != 0 ? inputVm.ResistanceMeasureTemp : 25,
                    WindingRatedTempC: inputVm.WindingRatedTemp != 0 ? inputVm.WindingRatedTemp : 85),
                Resistance: new DcResistanceInfo(
                    R12: inputVm.R12 > 0 ? inputVm.R12 : 1.2,
                    R23: inputVm.R23 > 0 ? inputVm.R23 : 1.25,
                    R31: inputVm.R31 > 0 ? inputVm.R31 : 1.22),
                NoLoad: noLoad,
                Load: load));

			var residualLossPoints = result.ResidualLoss.ToList();

			var resultVm = new AnalysisResultViewModel
			{
				ReportTitle = "Efficiency Analysis Report",
				ReportDate = (inputVm.TestDate ?? System.DateTime.Today).ToString("yyyy-MM-dd"),
				TesterName = string.IsNullOrWhiteSpace(inputVm.TesterName) ? "-" : inputVm.TesterName,
				NoLoadCsvPath = string.IsNullOrWhiteSpace(inputVm.NoLoadCsvPath) ? "-" : inputVm.NoLoadCsvPath,
				LoadCsvPath = string.IsNullOrWhiteSpace(inputVm.LoadCsvPath) ? "-" : inputVm.LoadCsvPath,

				MotorRatedVoltage = $"{(inputVm.RatedVoltage > 0 ? inputVm.RatedVoltage : 380):0.###} V",
				MotorRatedFrequency = $"{(inputVm.RatedFrequencyHz > 0 ? inputVm.RatedFrequencyHz : 60):0.###} Hz",
				MotorPoleCount = $"{(inputVm.PoleCount > 0 ? inputVm.PoleCount : 4)}",
				MotorRatedPower = $"{(inputVm.RatedPowerKw > 0 ? inputVm.RatedPowerKw : 3.7):0.###} kW",

				TempCoolant = $"{(inputVm.CoolantTemp != 0 ? inputVm.CoolantTemp : (inputVm.TemperatureC != 0 ? inputVm.TemperatureC : 25)):0.###} °C",
				TempConductorCoeff = $"{(inputVm.ConductorTempCoeff != 0 ? inputVm.ConductorTempCoeff : 235):0.###}",
				TempResistanceMeasure = $"{(inputVm.ResistanceMeasureTemp != 0 ? inputVm.ResistanceMeasureTemp : 25):0.###} °C",
				TempWindingRated = $"{(inputVm.WindingRatedTemp != 0 ? inputVm.WindingRatedTemp : 85):0.###} °C",

				DcR12 = $"{(inputVm.R12 > 0 ? inputVm.R12 : 0):0.####} Ω",
				DcR23 = $"{(inputVm.R23 > 0 ? inputVm.R23 : 0):0.####} Ω",
				DcR31 = $"{(inputVm.R31 > 0 ? inputVm.R31 : 0):0.####} Ω",

				LldA = result.LldA.ToString("F6"),
				LldB = result.LldB.ToString("F6"),
				Correlation = result.Correlation.ToString("F6"),
				ResidualLoss = residualLossPoints
					.Where(p => !double.IsNaN(p.ResidualLoss))
					.Select(p => new ResidualLossChartPoint
					{
						TorqueSquared = p.TorqueSquared,
						ResidualLoss = p.ResidualLoss
					})
					.ToList(),
				ResidualScatterMeasured = residualLossPoints
					.Select(p => new ResidualLossScatterPoint { TorqueSquared = p.TorqueSquared, Loss = p.ResidualLoss })
					.ToList(),
				ResidualScatterFit = result.ResidualAdditionalLossRows
					.Select((p, idx) => new ResidualLossScatterPoint
					{
						TorqueSquared = idx < residualLossPoints.Count ? residualLossPoints[idx].TorqueSquared : 0,
						Loss = p.ResidualLossFitPLr
					})
					.ToList(),
				AdditionalLossScatter = result.ResidualAdditionalLossRows
					.Select((p, idx) => new ResidualLossScatterPoint
					{
						TorqueSquared = idx < residualLossPoints.Count ? residualLossPoints[idx].TorqueSquared : 0,
						Loss = p.AdditionalLossPll
					})
					.ToList(),
				Rows = result.Rows.Select(r => new AnalysisResultRow
				{
					LoadPct = r.LoadPct,
					InputPower = r.InputPower,
					OutputPower = r.OutputPower,
					Efficiency = r.EfficiencyPct
				}).ToList(),
				RatedSummary = new RatedSummaryRowVm
				{
					LoadPct = result.RatedSummary.LoadPct,
					InputPowerP1 = result.RatedSummary.InputPowerP1,
					OutputPowerP2 = result.RatedSummary.OutputPowerP2,
					StatorCopperLossPCuS = result.RatedSummary.StatorCopperLossPCuS,
					RotorCopperLossPCuR = result.RatedSummary.RotorCopperLossPCuR,
					ResidualLossPRes = result.RatedSummary.ResidualLossPRes,
					IronLossPFe = result.RatedSummary.IronLossPFe,
					FrictionWindageLossPFw = result.RatedSummary.FrictionWindageLossPFw,
					AdditionalLossPLL = result.RatedSummary.AdditionalLossPLL,
					TotalLossPLoss = result.RatedSummary.TotalLossPLoss,
					Efficiency = result.RatedSummary.Efficiency
				},
				NoLoadCalcRows = result.NoLoadCalcRows.Select(r => new NoLoadCalcRowVm
				{
					StepPoint = r.StepPoint,
					Rll = r.Rll,
					StatorCopperLoss = r.StatorCopperLoss,
					CorrectedNoLoadPower = r.CorrectedNoLoadPower
				}).ToList(),
				NoLoadSummary = new NoLoadSummaryVm
				{
					FrictionWindageLoss = result.NoLoadSummary.FrictionWindageLoss,
					IronLoss = result.NoLoadSummary.IronLoss
				},
				LoadCalcRows = result.LoadCalcRows.Select(r => new LoadCalcRowVm
				{
					LoadPct = r.LoadPct,
					OutputPowerP2 = r.OutputPowerP2,
					SyncSpeedNs = r.SyncSpeedNs,
					Slip = r.Slip,
					RllThetaW = r.RllThetaW,
					StatorCopperLoss = r.StatorCopperLoss,
					RotorCopperLoss = r.RotorCopperLoss
				}).ToList(),
				ResidualAdditionalLossRows = result.ResidualAdditionalLossRows.Select(r => new ResidualAdditionalLossRowVm
				{
					LoadPct = r.LoadPct,
					ResidualLossPRes = r.ResidualLossPRes,
					ResidualLossFitPLr = r.ResidualLossFitPLr,
					AdditionalLossPll = r.AdditionalLossPll,
					AdditionalLossEffPll = r.AdditionalLossEffPll
				}).ToList(),
				FinalLoadSummaryRows = result.FinalLoadSummaryRows.Select(r => new FinalLoadSummaryRowVm
				{
					LoadPct = r.LoadPct,
					RllThetaW = r.RllThetaW,
					StatorCopperLoss = r.StatorCopperLoss,
					RotorCopperLoss = r.RotorCopperLoss,
					ResidualLossPRes = r.ResidualLossPRes,
					AdditionalLossPll = r.AdditionalLossPll,
					FrictionWindageLossPFw = r.FrictionWindageLossPFw,
					IronLossPFe = r.IronLossPFe,
					Efficiency = r.Efficiency
				}).ToList(),
			};

            var resultWindow = new AnalysisResultWindow(resultVm)
            {
                Owner = this
            };
            resultWindow.ShowDialog();
        }
    }
}
