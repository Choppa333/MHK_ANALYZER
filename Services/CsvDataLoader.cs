using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using MGK_Analyzer.Models;
using MGK_Analyzer.Services;

namespace MGK_Analyzer.Services
{
    public class CsvDataLoader
    {
        private static readonly Brush[] DefaultColors = 
        {
            Brushes.Blue, Brushes.Red, Brushes.Green, Brushes.Orange, Brushes.Purple,
            Brushes.Brown, Brushes.Pink, Brushes.Gray, Brushes.Olive, Brushes.Navy
        };

		private static Brush GetDeterministicSeriesColor(string seriesName)
		{
			if (string.IsNullOrWhiteSpace(seriesName))
			{
				return DefaultColors[0];
			}

			// Stable across runs: avoid string.GetHashCode() (randomized)
			unchecked
			{
				uint hash = 2166136261;
				foreach (var ch in seriesName.Trim())
				{
					hash ^= ch;
					hash *= 16777619;
				}

				var index = (int)(hash % (uint)DefaultColors.Length);
				return DefaultColors[index];
			}
		}

        static CsvDataLoader()
        {
			// EUC-KR 등 코드 페이지 기반 인코딩을 사용할 수 있도록 등록.
			Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
		}

		public async Task<MemoryOptimizedDataSet> LoadCsvDataAsync(
			string filePath,
			IProgress<int>? progress = null,
			int? expectedMetaType = 0,
			bool requireMetaTypeMatch = false)
        {
            using var overallTimer = PerformanceLogger.Instance.StartTimer($"전체 CSV 로딩: {Path.GetFileName(filePath)}", "CSV_Loading");
            
            try
            {
                PerformanceLogger.Instance.LogInfo($"CSV 파일 로딩 시작: {filePath}", "CSV_Loading");
                
                var dataSet = new MemoryOptimizedDataSet
                {
                    FileName = Path.GetFileName(filePath)
                };

                // 파일 크기 확인
                var fileInfo = new FileInfo(filePath);
                var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                
                PerformanceLogger.Instance.LogInfo($"파일 크기: {fileSizeMB:F2}MB", "CSV_Loading");
                progress?.Report(5);

                Encoding detectedEncoding;
                using (var encodingTimer = PerformanceLogger.Instance.StartTimer("인코딩 감지 및 파일 열기", "CSV_Loading"))
                {
                    detectedEncoding = DetectEncoding(filePath);
                    PerformanceLogger.Instance.LogInfo($"선택된 인코딩: {detectedEncoding.WebName}", "CSV_Loading");
                }

                using (var reader = new StreamReader(filePath, detectedEncoding, true, 1024 * 1024))
                {
                    // 헤더 및 메타 정보 읽기 (# 로 시작하는 메타 라인 스킵)
                    using var headerTimer = PerformanceLogger.Instance.StartTimer("헤더 정보 읽기", "CSV_Loading");

                    var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    string? headersLine = null;
                    string? dataTypesLine = null;
                    string? unitsLine = null;
                    string? line;

                    while ((line = await reader.ReadLineAsync()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line))
                            continue;

                        if (line.StartsWith("#"))
                        {
                            // 메타 라인 파싱 (#KEY:VALUE 또는 #KEY: VALUE)
                            var content = line.Substring(1).Trim();
                            var parts = content.Split(new[] { ':', ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 1)
                            {
                                var key = parts[0].Trim().ToUpperInvariant();
                                var value = parts.Length > 1 ? parts[1].Trim() : string.Empty;
                                meta[key] = value;
                            }
                            continue;
                        }

                        var trimmed = line.TrimStart();

                        if (dataTypesLine == null && trimmed.StartsWith("DATA_TYPE", StringComparison.OrdinalIgnoreCase))
                        {
                            dataTypesLine = line;
                        }
                        else if (unitsLine == null && trimmed.StartsWith("UNIT", StringComparison.OrdinalIgnoreCase))
                        {
                            unitsLine = line;
                        }
                        else if (headersLine == null)
                        {
                            headersLine = line;
                        }

                        if (headersLine != null && dataTypesLine != null && unitsLine != null)
                        {
                            break;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(headersLine) || string.IsNullOrWhiteSpace(dataTypesLine) || string.IsNullOrWhiteSpace(unitsLine))
                    {
                        throw new InvalidDataException("CSV 파일의 헤더/DATA_TYPE/UNIT 정보가 올바르지 않습니다.");
                    }

                    var headers = headersLine.Split(',');
                    var dataTypes = dataTypesLine.Split(',');
                    var units = unitsLine.Split(',');

                    PerformanceLogger.Instance.LogInfo($"헤더 및 메타 파싱 완료 - 컬럼 수: {headers.Length}", "CSV_Loading");

                    if (meta.TryGetValue("TYPE", out var metaTypeVal) && int.TryParse(metaTypeVal.Trim(), out var parsedMetaType))
                    {
                        dataSet.MetaType = parsedMetaType;
                    }

                    if (expectedMetaType.HasValue)
                    {
                        if (meta.TryGetValue("TYPE", out var expectedTypeText))
                        {
                            if (!int.TryParse(expectedTypeText.Trim(), out var parsedType) || parsedType != expectedMetaType.Value)
                            {
                                var errorMessage = $"CSV META TYPE 불일치: {expectedTypeText} (expected {expectedMetaType.Value})";
                                PerformanceLogger.Instance.LogError(errorMessage, "CSV_Loading");
                                throw new InvalidDataException(errorMessage);
                            }
                            else
                            {
                                PerformanceLogger.Instance.LogInfo($"CSV META TYPE 확인: {parsedType}", "CSV_Loading");
                            }
                        }
                        else if (requireMetaTypeMatch)
                        {
                            var errorMessage = $"CSV META TYPE 정보가 없습니다. (expected {expectedMetaType.Value})";
                            PerformanceLogger.Instance.LogError(errorMessage, "CSV_Loading");
                            throw new InvalidDataException(errorMessage);
                        }
                    }

                    // DATE 메타가 있으면 기본 시간으로 설정
                    if (meta.TryGetValue("DATE", out var dateVal) && !string.IsNullOrWhiteSpace(dateVal))
                    {
                        dataSet.MetaDateRaw = dateVal.Trim();
                        if (DateTime.TryParse(dateVal, out var parsedDate))
                        {
                            dataSet.BaseTime = parsedDate;
                            PerformanceLogger.Instance.LogInfo($"CSV META DATE 파싱 성공: {parsedDate}", "CSV_Loading");
                        }
                        else
                        {
                            PerformanceLogger.Instance.LogInfo($"CSV META DATE 파싱 실패 (무시): {dateVal}", "CSV_Loading");
                        }
                    }

                    headerTimer.Dispose();

                    progress?.Report(10);

                    // 빠른 라인 수 계산 (샘플링 기반)
                    int estimatedRows;
                    using (var estimateTimer = PerformanceLogger.Instance.StartTimer("행 수 추정", "CSV_Loading"))
                    {
                        // Use a separate reader for sampling so we don't disturb the main reader's buffer/position
                        estimatedRows = await EstimateRowCountAsync(filePath, fileSizeMB, detectedEncoding);
                        PerformanceLogger.Instance.LogInfo($"추정 행 수: {estimatedRows:N0}", "CSV_Loading");
                    }
                    progress?.Report(15);

                    // 시리즈 정보 설정 (메모리 할당 최적화)
                    Dictionary<string, SeriesData> seriesDict;
                    using (var seriesTimer = PerformanceLogger.Instance.StartTimer("시리즈 정보 설정", "CSV_Loading"))
                    {
                        seriesDict = new Dictionary<string, SeriesData>(headers.Length);
                        int colorIndex = 0;
                        
                        for (int i = 2; i < headers.Length; i++) // 시간 컬럼 제외
                        {
                            var headerName = headers[i]?.Trim();
                            if (string.IsNullOrEmpty(headerName)) continue;
                            
                            var unitName = units[i]?.Trim() ?? "";
                            var typeName = dataTypes[i]?.Trim()?.ToLower() ?? "double";

                            var series = new SeriesData
                            {
                                Name = headerName,
                                Unit = unitName,
                                DataType = typeName == "bit" ? typeof(bool) : typeof(double),
                                Values = new float[estimatedRows],
                                BitValues = typeName == "bit" ? new System.Collections.BitArray(estimatedRows) : null,
								Color = GetDeterministicSeriesColor(headerName),
                                IsVisible = false
                            };
                            seriesDict[series.Name] = series;
                            colorIndex++;
                        }
                        
                        PerformanceLogger.Instance.LogInfo($"시리즈 생성 완료 - 시리즈 수: {seriesDict.Count}", "CSV_Loading");
                    }

                    progress?.Report(20);

                    // 스트리밍 방식으로 데이터 로드 (청크 단위)
                    int lineCount;
                    using (var loadTimer = PerformanceLogger.Instance.StartTimer("데이터 스트리밍 로드", "CSV_Loading"))
                    {
                        var columnMap = CreateColumnMap(headers, seriesDict);
                        lineCount = await LoadDataStreamingAsync(reader, columnMap, dataSet, estimatedRows, progress);
                        PerformanceLogger.Instance.LogInfo($"실제 데이터 행 수: {lineCount:N0}", "CSV_Loading");
                    }

                    progress?.Report(90);

                    // 실제 크기로 배열 조정
                    if (lineCount != estimatedRows)
                    {
                        using var resizeTimer = PerformanceLogger.Instance.StartTimer("배열 크기 조정", "CSV_Loading");
                        await Task.Run(() => ResizeArrays(seriesDict, lineCount));
                    }

                    dataSet.TotalSamples = lineCount;
                    dataSet.SeriesData = seriesDict;
                    
                    // 통계 정보 계산 (백그라운드)
                    _ = Task.Run(() => 
                    {
                        using var statsTimer = PerformanceLogger.Instance.StartTimer("통계 정보 계산 (백그라운드)", "CSV_Loading");
                        CalculateStatistics(dataSet);
                    });
                    
                    progress?.Report(100);
                    
                    PerformanceLogger.Instance.LogInfo($"CSV 로딩 완료 - 파일: {dataSet.FileName}, 행: {lineCount:N0}, 시리즈: {seriesDict.Count}", "CSV_Loading");
                    return dataSet;
                }
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"CSV 파일 로딩 실패: {ex.Message}", "CSV_Loading");
                throw new InvalidDataException($"CSV 파일 로딩 중 오류 발생: {ex.Message}", ex);
            }
        }

        private async Task<int> EstimateRowCountAsync(string filePath, double fileSizeMB, Encoding encoding)
        {
            // Create a short-lived reader for sampling so the main reader state is not disturbed.
            var sampleLines = 0;
            var totalBytes = 0;
            const int SAMPLE_SIZE = 50;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sampleReader = new StreamReader(fs, encoding, true, 1024 * 1024))
            {
                for (int i = 0; i < SAMPLE_SIZE && !sampleReader.EndOfStream; i++)
                {
                    var line = await sampleReader.ReadLineAsync();
                    if (!string.IsNullOrEmpty(line))
                    {
                        totalBytes += Encoding.UTF8.GetByteCount(line) + 2; // rough CRLF
                        sampleLines++;
                    }
                }
            }

            if (sampleLines > 0)
            {
                var avgLineBytes = (double)totalBytes / sampleLines;
                var estimatedRows = (int)(fileSizeMB * 1024 * 1024 / avgLineBytes);
                return (int)(estimatedRows * 1.1);
            }

            return (int)(fileSizeMB * 1000);
        }

        private Encoding DetectEncoding(string filePath)
        {
            var eucKr = Encoding.GetEncoding("EUC-KR");
            var utf8Bom = new UTF8Encoding(true);
            var utf8 = new UTF8Encoding(false);

            var candidates = new (Encoding encoding, bool detectBom)[]
            {
                (utf8Bom, true),
                (utf8, true),
                (eucKr, false)
            };

            foreach (var candidate in candidates)
            {
                if (TrySampleWithEncoding(filePath, candidate.encoding, candidate.detectBom, out var sample) && !LooksCorrupted(sample))
                {
                    return candidate.encoding;
                }
            }

            return Encoding.UTF8;
        }

        private static bool TrySampleWithEncoding(string filePath, Encoding encoding, bool detectBom, out string sample)
        {
            sample = string.Empty;
            try
            {
                const int SAMPLE_CHAR_COUNT = 2048;
                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                using var reader = new StreamReader(fs, encoding, detectBom, 4096, leaveOpen: false);
                var buffer = new char[SAMPLE_CHAR_COUNT];
                var read = reader.Read(buffer, 0, SAMPLE_CHAR_COUNT);
                if (read <= 0)
                {
                    return false;
                }

                sample = new string(buffer, 0, read);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool LooksCorrupted(string sample)
        {
            if (string.IsNullOrEmpty(sample))
            {
                return false;
            }

            var replacementCount = sample.Count(c => c == '\uFFFD');
            if (replacementCount == 0)
            {
                return false;
            }

            var ratio = (double)replacementCount / sample.Length;
            return replacementCount > 2 || ratio > 0.001;
        }

        private static SeriesData?[] CreateColumnMap(string[] headers, Dictionary<string, SeriesData> seriesDict)
        {
            var map = new SeriesData?[headers.Length];
            for (int i = 2; i < headers.Length; i++)
            {
                var header = headers[i]?.Trim();
                if (string.IsNullOrEmpty(header)) continue;
                if (seriesDict.TryGetValue(header, out var series))
                {
                    map[i] = series;
                }
            }
            return map;
        }

        private async Task<int> LoadDataStreamingAsync(StreamReader reader,
            SeriesData?[] columnMap, MemoryOptimizedDataSet dataSet,
            int estimatedRows, IProgress<int> progress)
        {
            Console.WriteLine("=== LoadDataStreamingAsync 시작 (블록 기반) ===");

            const int BUFFER_CHAR_CAPACITY = 64 * 1024;
            const int PROGRESS_UPDATE_INTERVAL = 50_000;

            var buffers = new[]
            {
                new char[BUFFER_CHAR_CAPACITY],
                new char[BUFFER_CHAR_CAPACITY]
            };
            var lineBuilder = new StringBuilder(BUFFER_CHAR_CAPACITY);
            var lineCount = 0;

            // 첫 번째 및 두 번째 라인에서 시간 정보 설정 (메타 BaseTime 우선)
            var firstLine = await reader.ReadLineAsync();
            if (!string.IsNullOrEmpty(firstLine))
            {
                PerformanceLogger.Instance.LogInfo($"첫 데이터 라인(원시): {firstLine?.Length} chars - {firstLine?.Substring(0, Math.Min(200, firstLine.Length))}", "CSV_Loading");

                var firstValues = firstLine.Split(',');

                // Try parse first field as DateTime
                if (DateTime.TryParse(firstValues[0]?.Trim(), out var parsedDateTime))
                {
                    dataSet.BaseTime = parsedDateTime;
                }
                else
                {
                    // If BaseTime already set from META, keep it. Otherwise default to today.
                    if (dataSet.BaseTime == default)
                        dataSet.BaseTime = DateTime.Today;
                }

                // Attempt to read second line to compute interval when relative numeric times are used
                var secondLine = await reader.ReadLineAsync();
                PerformanceLogger.Instance.LogInfo($"두번째 데이터 라인(원시): {(secondLine == null ? "<null>" : secondLine.Length + " chars")}", "CSV_Loading");
                if (!string.IsNullOrEmpty(secondLine))
                {
                    PerformanceLogger.Instance.LogInfo($"두번째 데이터 라인 샘플: {secondLine.Substring(0, Math.Min(200, secondLine.Length))}", "CSV_Loading");
                    var secondValues = secondLine.Split(',');

                    // If both first fields are numeric, compute interval
                    if (double.TryParse(firstValues[0]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var t1) &&
                        double.TryParse(secondValues[0]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var t2))
                    {
                        var interval = Math.Abs(t2 - t1);
                        var safeInterval = Math.Max(0.001, Math.Min(3600.0, interval));
                        dataSet.TimeInterval = (float)safeInterval;
                    }
                    else
                    {
                        // fallback: try parse a possible interval field if available
                        if (firstValues.Length > 1 && double.TryParse(firstValues[1]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var maybeInterval))
                        {
                            var safeInterval = Math.Max(0.001, Math.Min(3600.0, Math.Abs(maybeInterval)));
                            dataSet.TimeInterval = (float)safeInterval;
                        }
                        else
                        {
                            dataSet.TimeInterval = 1.0f;
                        }
                    }

                    // Process both lines
                    ProcessSingleLineSpan(firstLine.AsSpan(), columnMap, 0);
                    ProcessSingleLineSpan(secondLine.AsSpan(), columnMap, 1);
                    lineCount = 2;
                }
                else
                {
                    // Only one line present
                    if (firstValues.Length > 1 && double.TryParse(firstValues[1]?.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var maybeInterval))
                    {
                        dataSet.TimeInterval = (float)Math.Max(0.001, Math.Min(3600.0, Math.Abs(maybeInterval)));
                    }
                    else
                    {
                        dataSet.TimeInterval = 1.0f;
                    }

                    ProcessSingleLineSpan(firstLine.AsSpan(), columnMap, 0);
                    lineCount = 1;
                }
            }

            var currentBufferIndex = 0;
            var charsRead = await reader.ReadAsync(buffers[currentBufferIndex], 0, BUFFER_CHAR_CAPACITY);
            if (charsRead == 0)
            {
                return lineCount;
            }

            while (charsRead > 0)
            {
                var nextBufferIndex = 1 - currentBufferIndex;
                var nextReadTask = reader.ReadAsync(buffers[nextBufferIndex], 0, BUFFER_CHAR_CAPACITY);

                ProcessBuffer(buffers[currentBufferIndex].AsSpan(0, charsRead));

                charsRead = await nextReadTask;
                currentBufferIndex = nextBufferIndex;
            }

            // 남은 줄 처리
            FlushPendingLine();

            Console.WriteLine($"스트리밍 데이터 로딩 완료 - 총 {lineCount:N0}개 라인");
            return lineCount;

            void ProcessBuffer(ReadOnlySpan<char> chunk)
            {
                if (chunk.IsEmpty)
                {
                    return;
                }

                var segmentStart = 0;
                for (int i = 0; i < chunk.Length; i++)
                {
                    var ch = chunk[i];
                    if (ch == '\n' || ch == '\r')
                    {
                        var segmentLength = i - segmentStart;
                        var hasData = segmentLength > 0 || lineBuilder.Length > 0;
                        if (hasData)
                        {
                            var segment = chunk.Slice(segmentStart, Math.Max(0, segmentLength));
                            CompleteLine(segment);
                        }

                        if (ch == '\r' && i + 1 < chunk.Length && chunk[i + 1] == '\n')
                        {
                            i++;
                        }
                        segmentStart = i + 1;
                    }
                }

                if (segmentStart < chunk.Length)
                {
                    lineBuilder.Append(chunk.Slice(segmentStart));
                }
            }

            void CompleteLine(ReadOnlySpan<char> segment)
            {
                ReadOnlySpan<char> lineSpan;
                if (lineBuilder.Length > 0)
                {
                    lineBuilder.Append(segment);
                    var composed = lineBuilder.ToString();
                    lineBuilder.Clear();
                    lineSpan = composed.AsSpan();
                    ProcessSingleLineSpan(lineSpan, columnMap, lineCount);
                }
                else if (!segment.IsEmpty)
                {
                    ProcessSingleLineSpan(segment, columnMap, lineCount);
                }
                else
                {
                    return;
                }

                lineCount++;
                if (lineCount % PROGRESS_UPDATE_INTERVAL == 0)
                {
                    var percent = 20 + (int)((double)lineCount / Math.Max(1, estimatedRows) * 60);
                    progress?.Report(Math.Min(85, percent));
                }
            }

            void FlushPendingLine()
            {
                if (lineBuilder.Length == 0)
                {
                    return;
                }

                CompleteLine(ReadOnlySpan<char>.Empty);
            }
        }

        private void ProcessSingleLineSpan(ReadOnlySpan<char> line, SeriesData?[] columnMap, int rowIndex)
        {
            if (columnMap.Length < 3 || line.IsEmpty) return;

            var start = 0;
            var colIndex = 0;

            while (start <= line.Length && colIndex < columnMap.Length)
            {
                int nextComma = -1;
                if (start < line.Length)
                {
                    nextComma = line.Slice(start).IndexOf(',');
                }

                ReadOnlySpan<char> token;
                if (nextComma >= 0)
                {
                    token = line.Slice(start, nextComma);
                    start += nextComma + 1;
                }
                else
                {
                    token = line.Slice(start);
                    start = line.Length + 1;
                }

                if (colIndex >= 2)
                {
                    var series = columnMap[colIndex];
                    if (series != null && rowIndex < series.Values.Length)
                    {
                        var trimmed = token.Trim();
                        if (!trimmed.IsEmpty)
                        {
                            if (series.DataType == typeof(bool))
                            {
                                if (IsTrueToken(trimmed) && series.BitValues != null && rowIndex < series.BitValues.Length)
                                {
                                    series.BitValues[rowIndex] = true;
                                }
                            }
                            else if (float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
                            {
                                series.Values[rowIndex] = floatVal;
                            }
                        }
                    }
                }

                colIndex++;
                if (nextComma < 0)
                {
                    break;
                }
            }
        }

        private static bool IsTrueToken(ReadOnlySpan<char> value)
        {
            if (value.Length == 0) return false;

            if (value.Length == 1)
            {
                var ch = value[0];
                return ch == '1' || ch == 'T' || ch == 't';
            }

            return value.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase);
        }

        private void ResizeArrays(Dictionary<string, SeriesData> seriesDict, int actualSize)
        {
            foreach (var series in seriesDict.Values)
            {
                if (series.Values.Length != actualSize)
                {
                    var newValues = new float[actualSize];
                    Array.Copy(series.Values, newValues, Math.Min(series.Values.Length, actualSize));
                    series.Values = newValues;
                    
                    if (series.BitValues != null)
                    {
                        var newBitValues = new System.Collections.BitArray(actualSize);
                        for (int i = 0; i < Math.Min(series.BitValues.Length, newBitValues.Length); i++)
                        {
                            newBitValues[i] = series.BitValues[i];
                        }
                        series.BitValues = newBitValues;
                    }
                }
            }
        }

        private void CalculateStatistics(MemoryOptimizedDataSet dataSet)
        {
            var seriesArray = dataSet.SeriesData.Values.Where(s => s.DataType != typeof(bool) && s.Values != null).ToArray();
            
            Parallel.ForEach(seriesArray, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, series =>
            {
                var values = series.Values;
                var count = values.Length;
                
                if (count == 0) return;
                
                // 한 번의 순회로 Min, Max, Sum 계산
                var min = float.MaxValue;
                var max = float.MinValue;
                var sum = 0.0;
                var validCount = 0;
                
                unsafe
                {
                    fixed (float* ptr = values)
                    {
                        for (int i = 0; i < count; i++)
                        {
                            var value = ptr[i];
                            if (!float.IsNaN(value) && !float.IsInfinity(value) && value != 0)
                            {
                                if (value < min) min = value;
                                if (value > max) max = value;
                                sum += value;
                                validCount++;
                            }
                        }
                    }
                }
                
                if (validCount > 0)
                {
                    series.MinValue = min;
                    series.MaxValue = max;
                    series.AvgValue = (float)(sum / validCount);
                }
            });
        }
    }
}