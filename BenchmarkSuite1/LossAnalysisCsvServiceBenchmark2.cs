using BenchmarkDotNet.Attributes;
using MGK_Analyzer.Services.Analysis;
using System.IO;
using System.Text;

namespace BenchmarkSuite1
{
	// CPU diagnoser attribute removed to avoid missing reference when Microsoft.VSDiagnostics package is unavailable.
    public class LossAnalysisCsvServiceBenchmark
    {
        private string _loadCsvPath = string.Empty;
        private LossAnalysisCsvService _service = null !;
        private static readonly int[] _steps = new[]
        {
            125,
            115,
            100,
            75,
            50,
            25
        };
        [Params(1000, 10000)]
        public int RowCount { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _service = new LossAnalysisCsvService();
            const int headerLength = 160;
            const int avgCharsPerRow = 32;
            var sb = new StringBuilder(headerLength + RowCount * avgCharsPerRow);
            sb.AppendLine("#TYPE: 1");
            sb.AppendLine("DATA_TYPE,DBL,DBL,DBL,DBL,DBL,DBL");
            sb.AppendLine("UNIT,%,V,A,W,N,rpm");
            sb.AppendLine("STEP,U,I,P1,T,N");
            for (int i = 0; i < RowCount; i++)
            {
                var step = _steps[i % _steps.Length];
                sb.Append(step).Append(",380,10,1200,50,1500").AppendLine();
            }

            _loadCsvPath = Path.Combine(Path.GetTempPath(), $"load_benchmark_{RowCount}.csv");
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            File.WriteAllText(_loadCsvPath, sb.ToString(), utf8NoBom);
        }

        [GlobalCleanup]
        public void Cleanup()
        {
            if (!string.IsNullOrEmpty(_loadCsvPath) && File.Exists(_loadCsvPath))
            {
                try
                {
                    File.Delete(_loadCsvPath);
                }
                catch
                {
                // Best-effort cleanup: avoid throwing during benchmark teardown
                }
            }
        }

        [Benchmark]
        public LossAnalysisCsvService.LoadValidationResult ValidateLoadCsv()
        {
            return _service.ValidateAndComputeLoad(_loadCsvPath);
        }
    }
}