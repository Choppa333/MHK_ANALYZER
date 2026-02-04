using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using MGK_Analyzer.Services.Analysis; // Added missing using for NoLoadPoint and LoadPoint

namespace MGK_Analyzer.Services.Analysis
{
	public sealed class LossAnalysisCsvService
	{
		private static readonly int[] NoLoadSteps = { 110, 100, 95, 90, 60, 50, 40, 30 };
		private static readonly int[] LoadSteps = { 125, 115, 100, 75, 50, 25 };
		private const int NoLoadMetaType = 2;
		private const int LoadMetaType = 1;

		public sealed record NoLoadAveragedPoint(int Step, double U0, double I0, double P0);
		public sealed record LoadAveragedPoint(int Step, double U, double I, double P1, double T, double N);

		public sealed record ValidationMessage(ValidationSeverity Severity, string Message);
		public enum ValidationSeverity
		{
			Info,
			Warning,
			Error
		}

		public sealed record NoLoadValidationResult(
			IReadOnlyList<ValidationMessage> Messages,
			IReadOnlyList<NoLoadAveragedPoint> Points);

		public sealed record LoadValidationResult(
			IReadOnlyList<ValidationMessage> Messages,
			IReadOnlyList<LoadAveragedPoint> Points);

		public static IReadOnlyList<NoLoadPoint> ToNoLoadPoints(IReadOnlyList<NoLoadAveragedPoint> points)
		{
			return points
				.Where(p => !double.IsNaN(p.U0) && !double.IsNaN(p.I0) && !double.IsNaN(p.P0))
				.OrderByDescending(p => p.Step)
				.Select(p => new NoLoadPoint(p.Step, p.U0, p.I0, p.P0))
				.ToList();
		}

		public static IReadOnlyList<LoadPoint> ToLoadPoints(IReadOnlyList<LoadAveragedPoint> points)
		{
			return points
				.Where(p => !double.IsNaN(p.U) && !double.IsNaN(p.I) && !double.IsNaN(p.P1) && !double.IsNaN(p.T) && !double.IsNaN(p.N))
				.OrderByDescending(p => p.Step)
				.Select(p => new LoadPoint(p.Step, p.U, p.I, p.P1, p.T, p.N))
				.ToList();
		}

		public NoLoadValidationResult ValidateAndComputeNoLoad(string csvPath)
		{
			var messages = new List<ValidationMessage>();
			var rows = ReadRowsWithSchema(csvPath);
			ValidateMetaType(rows.meta, NoLoadMetaType, "no-load", messages);

			var map = CreateColumnIndexMapWithAliases(rows.columns, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["U0"] = "UO",
				["I0"] = "UI",
				["P0"] = "PO"
			});
			ValidateRequiredColumns(map, new[] { "STEP", "U0", "I0", "P0" }, messages);
			ValidateColumnDataTypes(rows.dataTypes, map, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["STEP"] = "DBL",
				["U0"] = "DBL",
				["I0"] = "DBL",
				["P0"] = "DBL"
			}, "no-load", messages);
			ValidateColumnUnits(rows.units, map, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["U0"] = "V",
				["I0"] = "A",
				["P0"] = "W"
			}, "no-load", messages);

            var parsedRows = new List<(int Step, double U0, double I0, double P0)>(rows.data.Count);
            foreach (var line in rows.data)
            {
                var parsed = ParseNoLoadRowWithDiagnostics(line, map, messages);
                if (parsed.HasValue)
                {
                    parsedRows.Add(parsed.Value);
                }
            }

			ValidateStepCoverage(parsedRows.Select(r => r.Step), NoLoadSteps, "no-load", messages);
			ValidateUnknownSteps(parsedRows.Select(r => r.Step), NoLoadSteps, "no-load", messages);

			var points = ComputeNoLoadFromParsed(parsedRows);
			return new NoLoadValidationResult(messages, points);
		}

		public LoadValidationResult ValidateAndComputeLoad(string csvPath)
		{
			var messages = new List<ValidationMessage>();
			var rows = ReadRowsWithSchema(csvPath);
			ValidateMetaType(rows.meta, LoadMetaType, "load", messages);

			var map = CreateColumnIndexMap(rows.columns);
			ValidateRequiredColumns(map, new[] { "STEP", "U", "I", "P1", "T", "N" }, messages);
			ValidateColumnDataTypes(rows.dataTypes, map, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["STEP"] = "DBL",
				["U"] = "DBL",
				["I"] = "DBL",
				["P1"] = "DBL",
				["T"] = "DBL",
				["N"] = "DBL"
			}, "load", messages);
			ValidateColumnUnits(rows.units, map, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				["U"] = "V",
				["I"] = "A",
				["P1"] = "W",
				["T"] = "N", // Accepts N, N.m, N¡¤m; validated loosely
				["N"] = "rpm"
			}, "load", messages);

            var parsedRows = new List<(int Step, double U, double I, double P1, double T, double N)>(rows.data.Count);
            foreach (var line in rows.data)
            {
                var parsed = ParseLoadRowWithDiagnostics(line, map, messages);
                if (parsed.HasValue)
                {
                    parsedRows.Add(parsed.Value);
                }
            }

			ValidateStepCoverage(parsedRows.Select(r => r.Step), LoadSteps, "load", messages);
			ValidateUnknownSteps(parsedRows.Select(r => r.Step), LoadSteps, "load", messages);

			var points = ComputeLoadFromParsed(parsedRows);
			return new LoadValidationResult(messages, points);
		}

		private static void ValidateColumnUnits(
			string[] unitRow,
			Dictionary<string, int> columnMap,
			IReadOnlyDictionary<string, string> required,
			string label,
			List<ValidationMessage> messages)
		{
			if (unitRow.Length == 0)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Missing UNIT row in {label} CSV."));
				return;
			}

			foreach (var (columnName, expectedUnit) in required)
			{
				if (!columnMap.TryGetValue(columnName, out var index))
				{
					continue;
				}
				if (index < 0 || index >= unitRow.Length)
				{
					messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"UNIT row is shorter than header for column '{columnName}' in {label} CSV."));
					continue;
				}

				var actual = (unitRow[index] ?? string.Empty).Trim();
				if (string.IsNullOrWhiteSpace(actual))
				{
					messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"UNIT missing for column '{columnName}' in {label} CSV."));
					continue;
				}

				actual = actual.Trim(',');
				var ok = expectedUnit.Equals("N", StringComparison.OrdinalIgnoreCase)
					? actual.StartsWith("N", StringComparison.OrdinalIgnoreCase)
					: actual.Equals(expectedUnit, StringComparison.OrdinalIgnoreCase);

				if (!ok)
				{
					messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"UNIT mismatch for '{columnName}' in {label} CSV: '{actual}' (expected {expectedUnit})."));
				}
			}
		}

		public IReadOnlyList<NoLoadAveragedPoint> ComputeNoLoadStepAverages(string csvPath)
		{
			// Backward-compatible wrapper: compute without surfacing validation details.
			return ValidateAndComputeNoLoad(csvPath).Points;
		}

		public IReadOnlyList<LoadAveragedPoint> ComputeLoadStepAverages(string csvPath)
		{
			// Backward-compatible wrapper: compute without surfacing validation details.
			return ValidateAndComputeLoad(csvPath).Points;
		}

		public static string FormatValidationMessages(IReadOnlyList<ValidationMessage> messages)
		{
			var sb = new StringBuilder();
			foreach (var msg in messages)
			{
				sb.AppendLine($"[{msg.Severity}] {msg.Message}");
			}
			return sb.ToString();
		}

		public static string FormatNoLoadSummary(IReadOnlyList<NoLoadAveragedPoint> points)
		{
			var sb = new StringBuilder();
			sb.AppendLine("[No-Load STEP averages]");
			sb.AppendLine("STEP\tU0(V)\tI0(A)\tP0(W)");
			foreach (var p in points)
			{
				sb.AppendLine($"{p.Step}%\t{FormatNumber(p.U0)}\t{FormatNumber(p.I0)}\t{FormatNumber(p.P0)}");
			}
			return sb.ToString();
		}

		public static string FormatLoadSummary(IReadOnlyList<LoadAveragedPoint> points)
		{
			var sb = new StringBuilder();
			sb.AppendLine("[Load STEP averages]");
			sb.AppendLine("STEP\tU(V)\tI(A)\tP1(W)\tT(Nm)\tN(rpm)");
			foreach (var p in points)
			{
				sb.AppendLine($"{p.Step}%\t{FormatNumber(p.U)}\t{FormatNumber(p.I)}\t{FormatNumber(p.P1)}\t{FormatNumber(p.T)}\t{FormatNumber(p.N)}");
			}
			return sb.ToString();
		}

		private static string FormatNumber(double value)
		{
			if (double.IsNaN(value) || double.IsInfinity(value))
			{
				return "-";
			}

			return value.ToString("0.###", CultureInfo.InvariantCulture);
		}

		private static (Dictionary<string, string> meta, string[] dataTypes, string[] units, string[] columns, List<string> data) ReadRowsWithSchema(string csvPath)
		{
			if (string.IsNullOrWhiteSpace(csvPath))
			{
				throw new ArgumentException("CSV path is empty.", nameof(csvPath));
			}
			if (!File.Exists(csvPath))
			{
				throw new FileNotFoundException("CSV file not found.", csvPath);
			}

			const int fileBufferSize = 256 * 1024; // 256 KB
			const int readerBufferSize = 128 * 1024; // 128 KB
			using var fileStream = new FileStream(
				csvPath,
				FileMode.Open,
				FileAccess.Read,
				FileShare.Read,
				bufferSize: fileBufferSize,
				FileOptions.SequentialScan);
			using var reader = new StreamReader(
				fileStream,
				Encoding.UTF8,
				detectEncodingFromByteOrderMarks: true,
				bufferSize: readerBufferSize,
				leaveOpen: false);
			var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			string? line;
			string[]? dataTypes = null;
			string[]? units = null;
			string[]? header = null;
			var data = new List<string>();

			while ((line = reader.ReadLine()) != null)
			{
				if (string.IsNullOrWhiteSpace(line))
				{
					continue;
				}

				var trimmed = line.TrimStart();
				if (trimmed.StartsWith("#", StringComparison.Ordinal))
				{
					ParseMetaLine(line, meta);
					continue;
				}

				if (dataTypes == null && StartsWithToken(trimmed, "DATA_TYPE"))
				{
					dataTypes = SplitCsvLine(line);
					continue;
				}

				if (units == null && StartsWithToken(trimmed, "UNIT"))
				{
					units = SplitCsvLine(line);
					continue;
				}

				if (header == null)
				{
					header = SplitCsvLine(line);
					continue;
				}

				data.Add(line);
			}

			if (header == null)
			{
				throw new InvalidDataException("CSV header line not found.");
			}

			dataTypes ??= Array.Empty<string>();
			units ??= Array.Empty<string>();
			return (meta, dataTypes, units, header, data);
		}

		private static void ValidateColumnDataTypes(
			string[] dataTypeRow,
			Dictionary<string, int> columnMap,
			IReadOnlyDictionary<string, string> required,
			string label,
			List<ValidationMessage> messages)
		{
			if (dataTypeRow.Length == 0)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Missing DATA_TYPE row in {label} CSV."));
				return;
			}

			foreach (var (columnName, expectedType) in required)
			{
				if (!columnMap.TryGetValue(columnName, out var index))
				{
					continue;
				}

				if (index < 0 || index >= dataTypeRow.Length)
				{
					messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"DATA_TYPE row is shorter than header for column '{columnName}' in {label} CSV."));
					continue;
				}

				var actual = (dataTypeRow[index] ?? string.Empty).Trim();
				if (string.IsNullOrWhiteSpace(actual))
				{
					messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"DATA_TYPE missing for column '{columnName}' in {label} CSV."));
					continue;
				}

				// Excel can re-save with extra delimiters; keep only leading token.
				actual = actual.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? actual;
				actual = actual.Trim(',');

				if (!actual.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
				{
					messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"DATA_TYPE mismatch for '{columnName}' in {label} CSV: '{actual}' (expected {expectedType})."));
				}
			}
		}

		private static void ParseMetaLine(string line, Dictionary<string, string> meta)
		{
			var content = line.TrimStart();
			if (!content.StartsWith("#", StringComparison.Ordinal))
			{
				return;
			}

			content = content[1..].Trim();
			if (content.Length == 0)
			{
				return;
			}

			// Support either "KEY:VALUE" or Excel-saved "KEY,VALUE,,,," formats.
			string key;
			string value;
			if (content.Contains(':'))
			{
				var parts = content.Split(new[] { ':' }, 2, StringSplitOptions.RemoveEmptyEntries);
				key = parts.Length > 0 ? parts[0].Trim() : string.Empty;
				value = parts.Length > 1 ? parts[1].Trim() : string.Empty;
			}
			else if (content.Contains(',') || content.Contains('\t'))
			{
				var tokens = SplitCsvLine(content);
				if (tokens.Length == 0)
				{
					return;
				}
				key = tokens[0].Trim();
				value = tokens.Length > 1 ? tokens[1].Trim() : string.Empty;
			}
			else
			{
				var parts = content.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
				key = parts.Length > 0 ? parts[0].Trim() : string.Empty;
				value = parts.Length > 1 ? parts[1].Trim() : string.Empty;
			}

			value = NormalizeMetaValue(value);
			if (!string.IsNullOrWhiteSpace(key))
			{
				meta[key] = value;
			}
		}

		private static string NormalizeMetaValue(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return string.Empty;
			}

			// Excel can produce "2,,,," when saving after opening.
			return value.Trim().Trim(',');
		}

		private static bool StartsWithToken(string line, string token)
		{
			if (string.IsNullOrWhiteSpace(line))
			{
				return false;
			}

			var trimmed = line.TrimStart();
			if (trimmed.StartsWith(token, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			// Also handle token as first CSV cell
			var cells = SplitCsvLine(trimmed);
			return cells.Length > 0 && cells[0].Equals(token, StringComparison.OrdinalIgnoreCase);
		}

		private static void ValidateMetaType(Dictionary<string, string> meta, int expectedType, string label, List<ValidationMessage> messages)
		{
			if (!meta.TryGetValue("TYPE", out var typeText) || string.IsNullOrWhiteSpace(typeText))
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Missing META '#TYPE' in {label} CSV (expected {expectedType})."));
				return;
			}

			if (!int.TryParse(typeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) || parsed != expectedType)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Error, $"META '#TYPE' mismatch in {label} CSV: '{typeText}' (expected {expectedType})."));
			}
			else
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Info, $"META '#TYPE' ok: {parsed} ({label})."));
			}
		}

		private static string[] SplitCsvLine(string line)
		{
			// Loss-analysis exports may be comma-separated or tab-separated (Excel copy/save).
			return line.Split(new[] { ',', '\t' }, StringSplitOptions.TrimEntries);
		}

        private static string[] SplitCsvLineUpTo(string line, int maxIndex)
        {
            // Split only up to the required column index to avoid work on trailing empty cells.
            var tokens = new List<string>(maxIndex + 1);
            int start = 0;
            for (int i = 0; i < line.Length && tokens.Count <= maxIndex; i++)
            {
                var ch = line[i];
                if (ch == ',' || ch == '\t')
                {
                    var slice = line.AsSpan(start, i - start).Trim();
                    tokens.Add(slice.ToString());
                    start = i + 1;
                }
            }

            if (tokens.Count <= maxIndex)
            {
                var slice = line.AsSpan(start, Math.Max(0, line.Length - start)).Trim();
                tokens.Add(slice.ToString());
            }

            while (tokens.Count <= maxIndex)
            {
                tokens.Add(string.Empty);
            }

            return tokens.ToArray();
        }

		private static Dictionary<string, int> CreateColumnIndexMap(string[] columns)
		{
			var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			for (int i = 0; i < columns.Length; i++)
			{
				var name = columns[i]?.Trim();
				if (string.IsNullOrWhiteSpace(name))
				{
					continue;
				}
				if (!map.ContainsKey(name))
				{
					map[name] = i;
				}
			}
			return map;
		}

		private static Dictionary<string, int> CreateColumnIndexMapWithAliases(string[] columns, IReadOnlyDictionary<string, string> aliases)
		{
			var map = CreateColumnIndexMap(columns);
			foreach (var (canonical, alias) in aliases)
			{
				if (map.ContainsKey(canonical))
				{
					continue;
				}

				if (map.TryGetValue(alias, out var index))
				{
					map[canonical] = index;
				}
			}

			return map;
		}

		private static void ValidateRequiredColumns(Dictionary<string, int> map, IEnumerable<string> required, List<ValidationMessage> messages)
		{
			var missing = required.Where(r => !map.ContainsKey(r)).ToArray();
			if (missing.Length > 0)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Error, "Missing required columns: " + string.Join(", ", missing)));
			}
			else
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Info, "Required columns ok: " + string.Join(", ", required)));
			}
		}

		private static void ValidateStepCoverage(IEnumerable<int> stepsPresent, IReadOnlyList<int> expectedSteps, string label, List<ValidationMessage> messages)
		{
			var present = new HashSet<int>(stepsPresent);
			var missing = expectedSteps.Where(step => !present.Contains(step)).ToArray();
			if (missing.Length > 0)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Error, $"Missing STEP values in {label} CSV: {string.Join(", ", missing)}"));
			}
			else
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Info, $"STEP coverage ok for {label} CSV."));
			}

			var duplicates = stepsPresent
				.GroupBy(x => x)
				.Where(g => g.Count() > 1)
				.Select(g => g.Key)
				.OrderBy(x => x)
				.ToArray();
			if (duplicates.Length > 0)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Info, $"Duplicate STEP groups detected in {label} CSV (this is expected if multiple samples exist per point): {string.Join(", ", duplicates)}"));
			}
		}

		private static void ValidateUnknownSteps(IEnumerable<int> stepsPresent, IReadOnlyList<int> expectedSteps, string label, List<ValidationMessage> messages)
		{
			var expected = new HashSet<int>(expectedSteps);
			var unknown = stepsPresent.Where(s => !expected.Contains(s)).Distinct().OrderBy(s => s).ToArray();
			if (unknown.Length > 0)
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Unknown STEP values in {label} CSV (ignored for averaging): {string.Join(", ", unknown)}"));
			}
		}

		private static (int Step, double U0, double I0, double P0)? ParseNoLoadRowWithDiagnostics(string line, Dictionary<string, int> map, List<ValidationMessage> messages)
		{
			if (map.Count == 0)
			{
				return null;
			}
			if (!map.ContainsKey("STEP") || !map.ContainsKey("U0") || !map.ContainsKey("I0") || !map.ContainsKey("P0"))
			{
				return null;
			}

            var maxIndex = Math.Max(map["STEP"], Math.Max(map["U0"], Math.Max(map["I0"], map["P0"])));
            var parts = SplitCsvLineUpTo(line, maxIndex);
			if (!TryGetInt(parts, map["STEP"], out var step))
			{
				//messages.Add(new ValidationMessage(ValidationSeverity.Warning, "Skipping row: STEP not parseable."));
				return null;
			}

			if (!TryGetDouble(parts, map["U0"], out var u0) ||
				!TryGetDouble(parts, map["I0"], out var i0) ||
				!TryGetDouble(parts, map["P0"], out var p0))
			{
				//messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Skipping row: numeric parse failed for STEP={step}."));
				return null;
			}

			return (step, u0, i0, p0);
		}

		private static (int Step, double U, double I, double P1, double T, double N)? ParseLoadRowWithDiagnostics(string line, Dictionary<string, int> map, List<ValidationMessage> messages)
		{
			if (map.Count == 0)
			{
				return null;
			}
			if (!map.ContainsKey("STEP") || !map.ContainsKey("U") || !map.ContainsKey("I") || !map.ContainsKey("P1") || !map.ContainsKey("T") || !map.ContainsKey("N"))
			{
				return null;
			}

            var maxIndex = Math.Max(map["STEP"], Math.Max(map["U"], Math.Max(map["I"], Math.Max(map["P1"], Math.Max(map["T"], map["N"])))));
            var parts = SplitCsvLineUpTo(line, maxIndex);
			if (!TryGetInt(parts, map["STEP"], out var step))
			{
				//messages.Add(new ValidationMessage(ValidationSeverity.Warning, "Skipping row: STEP not parseable."));
				return null;
			}

			if (!TryGetDouble(parts, map["U"], out var u) ||
				!TryGetDouble(parts, map["I"], out var i) ||
				!TryGetDouble(parts, map["P1"], out var p1) ||
				!TryGetDouble(parts, map["T"], out var t) ||
				!TryGetDouble(parts, map["N"], out var n))
			{
				messages.Add(new ValidationMessage(ValidationSeverity.Warning, $"Skipping row: numeric parse failed for STEP={step}."));
				return null;
			}

			return (step, u, i, p1, t, n);
		}

		private static IReadOnlyList<NoLoadAveragedPoint> ComputeNoLoadFromParsed(List<(int Step, double U0, double I0, double P0)> parsed)
		{
			var groups = parsed
				.Where(r => NoLoadSteps.Contains(r.Step))
				.GroupBy(r => r.Step)
				.ToDictionary(g => g.Key, g => g.ToList());

			var result = new List<NoLoadAveragedPoint>(NoLoadSteps.Length);
			foreach (var step in NoLoadSteps)
			{
				if (!groups.TryGetValue(step, out var list) || list.Count == 0)
				{
					result.Add(new NoLoadAveragedPoint(step, double.NaN, double.NaN, double.NaN));
					continue;
				}

				result.Add(new NoLoadAveragedPoint(step,
					U0: list.Average(x => x.U0),
					I0: list.Average(x => x.I0),
					P0: list.Average(x => x.P0)));
			}
			return result;
		}

		private static IReadOnlyList<LoadAveragedPoint> ComputeLoadFromParsed(List<(int Step, double U, double I, double P1, double T, double N)> parsed)
		{
			var groups = parsed
				.Where(r => LoadSteps.Contains(r.Step))
				.GroupBy(r => r.Step)
				.ToDictionary(g => g.Key, g => g.ToList());

			var result = new List<LoadAveragedPoint>(LoadSteps.Length);
			foreach (var step in LoadSteps)
			{
				if (!groups.TryGetValue(step, out var list) || list.Count == 0)
				{
					result.Add(new LoadAveragedPoint(step, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN));
					continue;
				}

				result.Add(new LoadAveragedPoint(step,
					U: list.Average(x => x.U),
					I: list.Average(x => x.I),
					P1: list.Average(x => x.P1),
					T: list.Average(x => x.T),
					N: list.Average(x => x.N)));
			}
			return result;
		}

        private static bool TryGetDouble(string[] parts, int index, out double value)
        {
            value = 0;
            if (index < 0 || index >= parts.Length)
            {
                return false;
            }

            var token = parts[index];
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            return double.TryParse(token.AsSpan().Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryGetInt(string[] parts, int index, out int value)
        {
            value = 0;
            if (index < 0 || index >= parts.Length)
            {
                return false;
            }

            var token = parts[index];
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            var span = token.AsSpan().Trim().TrimEnd('%');

            if (int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }

            if (double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl))
            {
                value = (int)Math.Round(dbl);
                return true;
            }

            return false;
        }
	}
}
