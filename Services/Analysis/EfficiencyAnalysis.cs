using System;
using System.Collections.Generic;
using System.Linq;

namespace MGK_Analyzer.Services.Analysis
{
    // 효율(성능) 분석 엔진의 외부 계약(인터페이스)
    public interface IEfficiencyAnalysisEngine
    {
        // 입력 데이터(정격/온도보정/무부하/부하)를 받아 분석 결과를 반환
        AnalysisResult Analyze(AnalysisInput input);
    }

    // 실제 분석 로직 구현 클래스
    public sealed class EfficiencyAnalysisEngine : IEfficiencyAnalysisEngine
    {
        public AnalysisResult Analyze(AnalysisInput input)
        {
            if (input is null)
                throw new ArgumentNullException(nameof(input));
            if (input.Motor is null)
                throw new ArgumentException("Motor input is required.", nameof(input));
            if (input.Temperature is null)
                throw new ArgumentException("Temperature input is required.", nameof(input));
            if (input.Resistance is null)
                throw new ArgumentException("Resistance input is required.", nameof(input));
            if (input.NoLoad is null || input.NoLoad.Count == 0)
                throw new ArgumentException("NoLoad points are required.", nameof(input));
            if (input.Load is null || input.Load.Count == 0)
                throw new ArgumentException("Load points are required.", nameof(input));

            // NOTE:
            // KCS_Eff_LIB uses fixed arrays of length 8 for both no-load and load.
            // This engine adapts by taking up to 8 points, ordered by StepPoint.

            var eff = new KCS_Eff_LIB.EFF_LIB();

            // Map to KCS_Eff_LIB.BasicInputInfo
            var basic = new KCS_Eff_LIB.BasicInputInfo(
                U_N: input.Motor.RatedVoltage,
                f: input.Motor.RatedFrequencyHz,
                p: input.Motor.PoleCount,
                P_N: input.Motor.RatedPowerW,
                θ_c: input.Temperature.CoolantTempC,
                K_cond: input.Temperature.ConductorTempCoeff,
                R12: input.Resistance.R12,
                R23: input.Resistance.R23,
                R31: input.Resistance.R31,
                θ0: input.Temperature.ResistanceMeasureTempC,
                θ_w: input.Temperature.WindingRatedTempC);

            eff.setBasicInputInfo(basic);

            // Preserve caller order to match legacy library behavior.
            // (Ordering can change which points are used if more than 8 are provided.)
            var noLoadPoints = input.NoLoad
                .Take(8)
                .ToArray();

            for (int i = 0; i < noLoadPoints.Length; i++)
            {
                var p = noLoadPoints[i];
                eff.addNoLoadEntity(i, new KCS_Eff_LIB.NoLoadEntity(
                    step_Point: p.StepPoint,
                    V: p.Voltage,
                    A: p.Current,
                    W: p.Power));
            }

            // Preserve caller order to match legacy library behavior.
            var loadPoints = input.Load
                .Take(8)
                .ToArray();

            for (int i = 0; i < loadPoints.Length; i++)
            {
                var p = loadPoints[i];
                eff.addLoadEntity(i, new KCS_Eff_LIB.LoadEntity(
                    step_Point: p.StepPoint,
                    V: p.Voltage,
                    A: p.Current,
                    W: p.Power,
                    T: p.TorqueNm,
                    n: p.SpeedRpm));
            }

            eff.AnalyzeAll();

            // Build per-step output rows.
            // KCS_Eff_LIB stores efficiency as a ratio (0..1). We return percentage (0..100).
            var rows = new List<AnalysisResultRow>(loadPoints.Length);
            for (int i = 0; i < loadPoints.Length; i++)
            {
                var lp = loadPoints[i];

                double outputPower = (eff.출력전력.Length > i) ? eff.출력전력[i] : (lp.TorqueNm * 2 * Math.PI * lp.SpeedRpm / 60.0);
                double inputPower = lp.Power;

                double effPct;
                if (eff.계산효율.Length > i)
                    effPct = eff.계산효율[i] * 100.0;
                else
                    effPct = inputPower != 0 ? (outputPower / inputPower) * 100.0 : 0;

                rows.Add(new AnalysisResultRow(
                    LoadPct: lp.StepPoint,
                    InputPower: inputPower,
                    OutputPower: outputPower,
                    EfficiencyPct: effPct));
            }

			var residual = new List<ResidualLossPoint>(loadPoints.Length);
			for (int i = 0; i < loadPoints.Length; i++)
			{
				double t2 = (eff.토크제곱.Length > i) ? eff.토크제곱[i] : (loadPoints[i].TorqueNm * loadPoints[i].TorqueNm);
				double pRes = (eff.잔류손실.Length > i) ? eff.잔류손실[i] : double.NaN;
				residual.Add(new ResidualLossPoint(TorqueSquared: t2, ResidualLoss: pRes));
			}

			// Rated summary: prefer 100% load point if present, otherwise the closest to 100.
			int ratedIndex = 0;
			if (loadPoints.Length > 0)
			{
				double bestDiff = double.MaxValue;
				for (int i = 0; i < loadPoints.Length; i++)
				{
					double diff = Math.Abs(loadPoints[i].StepPoint - 100.0);
					if (diff < bestDiff)
					{
						bestDiff = diff;
						ratedIndex = i;
					}
				}
			}

			double ratedInput = loadPoints[ratedIndex].Power;
			double ratedOutput = (eff.출력전력.Length > ratedIndex) ? eff.출력전력[ratedIndex] : (loadPoints[ratedIndex].TorqueNm * 2 * Math.PI * loadPoints[ratedIndex].SpeedRpm / 60.0);
			double ratedPcuS = (eff.부하_고정자권선손실.Length > ratedIndex) ? eff.부하_고정자권선손실[ratedIndex] : 0;
			double ratedPcuR = (eff.부하_회전자권선손실.Length > ratedIndex) ? eff.부하_회전자권선손실[ratedIndex] : 0;
			double ratedPres = (eff.잔류손실.Length > ratedIndex) ? eff.잔류손실[ratedIndex] : 0;
			double ratedPfe = eff.철손;
			double ratedPfw = eff.풍손마찰손;
			double ratedPll = (eff.추가부하손실.Length > ratedIndex) ? eff.추가부하손실[ratedIndex] : (eff.A * ((eff.토크제곱.Length > ratedIndex) ? eff.토크제곱[ratedIndex] : 0));
			double ratedPloss = (eff.총손실.Length > ratedIndex) ? eff.총손실[ratedIndex] : (ratedPcuS + ratedPcuR + ratedPfe + ratedPfw + ratedPll);
			double ratedEff = (eff.계산효율.Length > ratedIndex) ? eff.계산효율[ratedIndex] : (ratedInput != 0 ? (ratedOutput / ratedInput) : 0);

			var ratedSummary = new RatedSummaryRow(
				LoadPct: loadPoints[ratedIndex].StepPoint,
				InputPowerP1: ratedInput,
				OutputPowerP2: ratedOutput,
				StatorCopperLossPCuS: ratedPcuS,
				RotorCopperLossPCuR: ratedPcuR,
				ResidualLossPRes: ratedPres,
				IronLossPFe: ratedPfe,
				FrictionWindageLossPFw: ratedPfw,
				AdditionalLossPLL: ratedPll,
				TotalLossPLoss: ratedPloss,
				Efficiency: ratedEff);

			// No-load calc table
			var noLoadCalcRows = new List<NoLoadCalcRow>(noLoadPoints.Length);
			for (int i = 0; i < noLoadPoints.Length; i++)
			{
				double pCu = (eff.고정자권선손실.Length > i) ? eff.고정자권선손실[i] : 0;
				double p0p = (eff.보정무부하전력.Length > i) ? eff.보정무부하전력[i] : (noLoadPoints[i].Power - pCu);
				noLoadCalcRows.Add(new NoLoadCalcRow(
					StepPoint: noLoadPoints[i].StepPoint,
					Rll: eff.RLL,
					StatorCopperLoss: pCu,
					CorrectedNoLoadPower: p0p));
			}

			var noLoadSummary = new NoLoadSummary(
				FrictionWindageLoss: eff.풍손마찰손,
				IronLoss: eff.철손);

			// Load calc table
			var loadCalcRows = new List<LoadCalcRow>(loadPoints.Length);
			for (int i = 0; i < loadPoints.Length; i++)
			{
				loadCalcRows.Add(new LoadCalcRow(
					LoadPct: loadPoints[i].StepPoint,
					OutputPowerP2: (eff.출력전력.Length > i) ? eff.출력전력[i] : 0,
					SyncSpeedNs: eff.동기속도,
					Slip: (eff.슬립.Length > i) ? eff.슬립[i] : 0,
					RllThetaW: eff.부하고정자권선저항,
					StatorCopperLoss: (eff.부하_고정자권선손실.Length > i) ? eff.부하_고정자권선손실[i] : 0,
					RotorCopperLoss: (eff.부하_회전자권선손실.Length > i) ? eff.부하_회전자권선손실[i] : 0));
			}

			// Residual & additional loss rows (includes fit lines)
			var residualAdditionalLossRows = new List<ResidualAdditionalLossRow>(loadPoints.Length);
			for (int i = 0; i < loadPoints.Length; i++)
			{
				double t2 = (eff.토크제곱.Length > i) ? eff.토크제곱[i] : (loadPoints[i].TorqueNm * loadPoints[i].TorqueNm);
				double pRes = (eff.잔류손실.Length > i) ? eff.잔류손실[i] : 0;
				double pLrFit = eff.A * t2 + eff.B;
				double pLl = eff.A * t2;
				double pLlEff = Math.Max(0, pLl);
				residualAdditionalLossRows.Add(new ResidualAdditionalLossRow(
					LoadPct: loadPoints[i].StepPoint,
					ResidualLossPRes: pRes,
					ResidualLossFitPLr: pLrFit,
					AdditionalLossPll: pLl,
					AdditionalLossEffPll: pLlEff));
			}

			// Final per-load summary
			var finalLoadSummaryRows = new List<FinalLoadSummaryRow>(loadPoints.Length);
			for (int i = 0; i < loadPoints.Length; i++)
			{
				double t2 = (eff.토크제곱.Length > i) ? eff.토크제곱[i] : (loadPoints[i].TorqueNm * loadPoints[i].TorqueNm);
				double pLl = (eff.추가부하손실.Length > i) ? eff.추가부하손실[i] : (eff.A * t2);
				finalLoadSummaryRows.Add(new FinalLoadSummaryRow(
					LoadPct: loadPoints[i].StepPoint,
					RllThetaW: eff.부하고정자권선저항,
					StatorCopperLoss: (eff.부하_고정자권선손실.Length > i) ? eff.부하_고정자권선손실[i] : 0,
					RotorCopperLoss: (eff.부하_회전자권선손실.Length > i) ? eff.부하_회전자권선손실[i] : 0,
					ResidualLossPRes: (eff.잔류손실.Length > i) ? eff.잔류손실[i] : 0,
					AdditionalLossPll: pLl,
					FrictionWindageLossPFw: eff.풍손마찰손,
					IronLossPFe: eff.철손,
					Efficiency: (eff.계산효율.Length > i) ? eff.계산효율[i] : 0));
			}

            return new AnalysisResult(
                LldA: eff.A,
                LldB: eff.B,
                Correlation: eff.Y,
                Rows: rows,
				ResidualLoss: residual,
				RatedSummary: ratedSummary,
				NoLoadCalcRows: noLoadCalcRows,
				NoLoadSummary: noLoadSummary,
				LoadCalcRows: loadCalcRows,
				ResidualAdditionalLossRows: residualAdditionalLossRows,
				FinalLoadSummaryRows: finalLoadSummaryRows);
        }
    }

    // 분석 입력 전체 묶음 (UI/CSV 로더에서 생성)
    public sealed record AnalysisInput(
        BasicMotorInfo Motor,
        TemperatureCorrectionInfo Temperature,
        DcResistanceInfo Resistance,
        IReadOnlyList<NoLoadPoint> NoLoad,
        IReadOnlyList<LoadPoint> Load);

    // 모터 기본 정격 정보
    public sealed record BasicMotorInfo(
        double RatedVoltage,
        double RatedFrequencyHz,
        int PoleCount,
        double RatedPowerW);

    // 온도/저항 보정 관련 입력
    public sealed record TemperatureCorrectionInfo(
        double CoolantTempC,
        double ConductorTempCoeff,
        double ResistanceMeasureTempC,
        double WindingRatedTempC);

    // 선간 DC 저항 입력 (필수)
    public sealed record DcResistanceInfo(
        double R12,
        double R23,
        double R31);

    // 무부하 측정 포인트 (정규화된 입력 형태)
    public sealed record NoLoadPoint(
        double StepPoint,
        double Voltage,
        double Current,
        double Power);

    // 부하 측정 포인트 (정규화된 입력 형태)
    public sealed record LoadPoint(
        double StepPoint,
        double Voltage,
        double Current,
        double Power,
        double TorqueNm,
        double SpeedRpm);

    // 분석 전체 결과
    public sealed record AnalysisResult(
        double LldA,
        double LldB,
        double Correlation,
		IReadOnlyList<AnalysisResultRow> Rows,
		IReadOnlyList<ResidualLossPoint> ResidualLoss,
		RatedSummaryRow RatedSummary,
		IReadOnlyList<NoLoadCalcRow> NoLoadCalcRows,
		NoLoadSummary NoLoadSummary,
		IReadOnlyList<LoadCalcRow> LoadCalcRows,
		IReadOnlyList<ResidualAdditionalLossRow> ResidualAdditionalLossRows,
		IReadOnlyList<FinalLoadSummaryRow> FinalLoadSummaryRows);

	public sealed record ResidualLossPoint(
		double TorqueSquared,
		double ResidualLoss);

	public sealed record RatedSummaryRow(
		double LoadPct,
		double InputPowerP1,
		double OutputPowerP2,
		double StatorCopperLossPCuS,
		double RotorCopperLossPCuR,
		double ResidualLossPRes,
		double IronLossPFe,
		double FrictionWindageLossPFw,
		double AdditionalLossPLL,
		double TotalLossPLoss,
		double Efficiency);

	public sealed record NoLoadCalcRow(
		double StepPoint,
		double Rll,
		double StatorCopperLoss,
		double CorrectedNoLoadPower);

	public sealed record NoLoadSummary(
		double FrictionWindageLoss,
		double IronLoss);

	public sealed record LoadCalcRow(
		double LoadPct,
		double OutputPowerP2,
		double SyncSpeedNs,
		double Slip,
		double RllThetaW,
		double StatorCopperLoss,
		double RotorCopperLoss);

	public sealed record ResidualAdditionalLossRow(
		double LoadPct,
		double ResidualLossPRes,
		double ResidualLossFitPLr,
		double AdditionalLossPll,
		double AdditionalLossEffPll);

	public sealed record FinalLoadSummaryRow(
		double LoadPct,
		double RllThetaW,
		double StatorCopperLoss,
		double RotorCopperLoss,
		double ResidualLossPRes,
		double AdditionalLossPll,
		double FrictionWindageLossPFw,
		double IronLossPFe,
		double Efficiency);

    // 부하 스텝별 결과 행
    public sealed record AnalysisResultRow(
        double LoadPct,
        double InputPower,
        double OutputPower,
        double EfficiencyPct);
}
