using System;
using System.Collections.Generic;
using System.Linq;
using MGK_Analyzer.Services.Analysis;

namespace MGK_Analyzer.Services
{
    public sealed class SampleEfficiencyAnalysisService
    {
        public SampleAnalysisResult Run()
        {
            // NOTE:
            // - This uses KCS_Eff_LIB but keeps the dependency out of the XAML code-behind.
            // - CSV loading/validation is intentionally deferred.

            var analyzer = new KCS_Eff_LIB.EFF_LIB();
            analyzer.setBasicInputInfo(new KCS_Eff_LIB.BasicInputInfo(
                U_N: 380,
                f: 60,
                p: 4,
                P_N: 3700,
                θ_c: 25,
                K_cond: 235,
                R12: 1.2,
                R23: 1.25,
                R31: 1.22,
                θ0: 25,
                θ_w: 85));

            // No-load sample
            var noLoad = new[]
            {
                new KCS_Eff_LIB.NoLoadEntity(110, 418, 4.2, 420),
                new KCS_Eff_LIB.NoLoadEntity(100, 380, 3.8, 380),
                new KCS_Eff_LIB.NoLoadEntity(95, 361, 3.5, 340),
                new KCS_Eff_LIB.NoLoadEntity(90, 342, 3.2, 300),
                new KCS_Eff_LIB.NoLoadEntity(60, 228, 2.1, 180),
                new KCS_Eff_LIB.NoLoadEntity(50, 190, 1.8, 150),
                new KCS_Eff_LIB.NoLoadEntity(40, 152, 1.4, 120),
                new KCS_Eff_LIB.NoLoadEntity(30, 114, 1.1, 95)
            };

            for (int i = 0; i < noLoad.Length; i++)
                analyzer.addNoLoadEntity(i, noLoad[i]);

            // Load sample
            var loads = new[]
            {
                new KCS_Eff_LIB.LoadEntity(125, 380, 8.5, 5200, 18, 1750),
                new KCS_Eff_LIB.LoadEntity(115, 380, 8.0, 4800, 17, 1760),
                new KCS_Eff_LIB.LoadEntity(100, 380, 7.4, 4400, 15, 1770),
                new KCS_Eff_LIB.LoadEntity(75, 380, 6.2, 3200, 11, 1780),
                new KCS_Eff_LIB.LoadEntity(50, 380, 5.0, 2100, 7, 1790),
                new KCS_Eff_LIB.LoadEntity(25, 380, 3.8, 1200, 4, 1795)
            };

            for (int i = 0; i < loads.Length; i++)
                analyzer.addLoadEntity(i, loads[i]);

            analyzer.AnalyzeAll();

            var validLoads = analyzer.load_Entities.Where(l => l != null).ToArray();
            int count = validLoads.Length;

            var rows = new List<SampleAnalysisRow>(count);
            for (int i = 0; i < count; i++)
            {
                var l = validLoads[i]!;
                rows.Add(new SampleAnalysisRow(
                    LoadPct: l.step_Point,
                    InputPower: l.W,
                    OutputPower: i < analyzer.출력전력.Length ? analyzer.출력전력[i] : 0,
                    EfficiencyPct: i < analyzer.계산효율.Length ? analyzer.계산효율[i] * 100 : 0));
            }

            return new SampleAnalysisResult(
                LldA: analyzer.A,
                LldB: analyzer.B,
                Correlation: analyzer.Y,
                Rows: rows);
        }
    }

    public sealed record SampleAnalysisResult(
        double LldA,
        double LldB,
        double Correlation,
        IReadOnlyList<SampleAnalysisRow> Rows);

    public sealed record SampleAnalysisRow(
        double LoadPct,
        double InputPower,
        double OutputPower,
        double EfficiencyPct);
}
