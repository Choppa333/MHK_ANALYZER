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

        public async Task<MemoryOptimizedDataSet> LoadCsvDataAsync(string filePath, IProgress<int> progress = null)
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

                // 여러 인코딩 시도하여 한글 깨짐 방지
                StreamReader reader = null;
                using var encodingTimer = PerformanceLogger.Instance.StartTimer("인코딩 감지 및 파일 열기", "CSV_Loading");
                try
                {
                    reader = new StreamReader(filePath, new UTF8Encoding(true), true, 1024 * 1024); // 1MB 버퍼
                    PerformanceLogger.Instance.LogInfo("UTF-8 BOM 인코딩으로 파일 열기 성공", "CSV_Loading");
                }
                catch
                {
                    try
                    {
                        reader = new StreamReader(filePath, Encoding.UTF8, true, 1024 * 1024);
                        PerformanceLogger.Instance.LogInfo("UTF-8 인코딩으로 파일 열기 성공", "CSV_Loading");
                    }
                    catch
                    {
                        reader = new StreamReader(filePath, Encoding.GetEncoding("EUC-KR"), true, 1024 * 1024);
                        PerformanceLogger.Instance.LogInfo("EUC-KR 인코딩으로 파일 열기 성공", "CSV_Loading");
                    }
                }
                encodingTimer.Dispose();

                using (reader)
                {
                    // 헤더 및 메타 정보 읽기 (# 로 시작하는 메타 라인 스킵)
                    using var headerTimer = PerformanceLogger.Instance.StartTimer("헤더 정보 읽기", "CSV_Loading");

                    var meta = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    var nonMetaLines = new List<string>(3);
                    string line;

                    while (nonMetaLines.Count < 3 && (line = await reader.ReadLineAsync()) != null)
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

                        nonMetaLines.Add(line);
                    }

                    if (nonMetaLines.Count < 3)
                    {
                        throw new InvalidDataException("CSV 파일의 헤더 정보가 올바르지 않습니다. 메타라인 후 3행의 헤더가 필요합니다.");
                    }

                    var headersLine = nonMetaLines[0];
                    var dataTypesLine = nonMetaLines[1];
                    var unitsLine = nonMetaLines[2];

                    var headers = headersLine.Split(',');
                    var dataTypes = dataTypesLine.Split(',');
                    var units = unitsLine.Split(',');

                    PerformanceLogger.Instance.LogInfo($"헤더 및 메타 파싱 완료 - 컬럼 수: {headers.Length}", "CSV_Loading");

                    // TYPE 메타 확인 (정격시험은 0 이어야 함)
                    if (meta.TryGetValue("TYPE", out var typeVal))
                    {
                        if (!string.Equals(typeVal.Trim(), "0", StringComparison.Ordinal))
                        {
                            PerformanceLogger.Instance.LogError($"CSV META TYPE 불일치: {typeVal} (정격시험은 0이어야 합니다)", "CSV_Loading");
                            throw new InvalidDataException($"CSV META TYPE 불일치: {typeVal} (정격시험은 0이어야 합니다)");
                        }
                        else
                        {
                            PerformanceLogger.Instance.LogInfo("CSV META TYPE 확인: 0 (정격시험)", "CSV_Loading");
                        }
                    }

                    // DATE 메타가 있으면 기본 시간으로 설정
                    if (meta.TryGetValue("DATE", out var dateVal) && !string.IsNullOrWhiteSpace(dateVal))
                    {
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
                        estimatedRows = await EstimateRowCountAsync(filePath, fileSizeMB);
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
                                Color = DefaultColors[colorIndex % DefaultColors.Length],
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
                        lineCount = await LoadDataStreamingAsync(reader, headers, seriesDict, dataSet, estimatedRows, progress);
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

        private async Task<int> EstimateRowCountAsync(string filePath, double fileSizeMB)
        {
            // Create a short-lived reader for sampling so the main reader state is not disturbed.
            var sampleLines = 0;
            var totalBytes = 0;
            const int SAMPLE_SIZE = 50;

            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var sampleReader = new StreamReader(fs, Encoding.UTF8, true, 1024 * 1024))
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

        private async Task<int> LoadDataStreamingAsync(StreamReader reader, string[] headers, 
            Dictionary<string, SeriesData> seriesDict, MemoryOptimizedDataSet dataSet, 
            int estimatedRows, IProgress<int> progress)
        {
            Console.WriteLine("=== LoadDataStreamingAsync 시작 (최적화 버전) ===");
            
            // 더 큰 청크 크기로 성능 개선
            const int CHUNK_SIZE = 50000; // 10,000 -> 50,000으로 증가
            const int BUFFER_SIZE = CHUNK_SIZE * 200; // 대용량 문자열 버퍼
            
            var lineCount = 0;
            var charBuffer = new char[BUFFER_SIZE];
            var stringBuilder = new StringBuilder(BUFFER_SIZE);
            
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
                    ProcessSingleLine(firstLine, headers, seriesDict, 0);
                    ProcessSingleLine(secondLine, headers, seriesDict, 1);
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

                    ProcessSingleLine(firstLine, headers, seriesDict, 0);
                    lineCount = 1;
                }
            }

            // 대용량 청크 단위로 읽기
            var lines = new List<string>(CHUNK_SIZE);
            string line;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                    lineCount++;
                    
                    // 청크가 가득 차면 병렬 처리
                    if (lines.Count >= CHUNK_SIZE)
                    {
                        await ProcessChunkParallelAsync(lines, headers, seriesDict, lineCount - lines.Count);
                        lines.Clear();
                        
                        // 진행률 업데이트 (빈도 줄임)
                        var progressPercent = 20 + (int)((double)lineCount / estimatedRows * 60);
                        progress?.Report(Math.Min(85, progressPercent));
                        
                        // UI 반응성을 위한 잠시 양보 (빈도 줄임)
                        if (lineCount % (CHUNK_SIZE * 2) == 0)
                        {
                            await Task.Delay(1);
                        }
                    }
                }

            // 디버그: 남아있는 스트림 여부 로그
            PerformanceLogger.Instance.LogInfo($"LoadDataStreamingAsync 종료 - reader.EndOfStream: {reader.EndOfStream}", "CSV_Loading");
            }

            // 마지막 청크 처리
            if (lines.Count > 0)
            {
                await ProcessChunkParallelAsync(lines, headers, seriesDict, lineCount - lines.Count);
            }

            Console.WriteLine($"최적화된 데이터 로딩 완료 - 총 {lineCount:N0}개 라인");
            return lineCount;
        }

        private async Task ProcessChunkParallelAsync(List<string> lines, string[] headers, 
            Dictionary<string, SeriesData> seriesDict, int startIndex)
        {
            // 단순 순차 처리로 안정성 확보 (병렬 처리에서 인덱스 불일치로 데이터 누락 가능성 있음)
            await Task.Run(() =>
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    ProcessSingleLine(lines[i], headers, seriesDict, startIndex + i);
                }
            });
        }

        private void ProcessSingleLine(string line, string[] headers, 
            Dictionary<string, SeriesData> seriesDict, int rowIndex)
        {
            var values = line.Split(',');
            
            // 컬럼 수 사전 체크로 성능 개선
            var maxColumns = Math.Min(values.Length, headers.Length);
            
            for (int colIndex = 2; colIndex < maxColumns; colIndex++)
            {
                var seriesName = headers[colIndex]?.Trim();
                if (string.IsNullOrEmpty(seriesName) || !seriesDict.TryGetValue(seriesName, out var series)) 
                    continue;
                
                if (rowIndex >= series.Values.Length) continue;
                
                var cellValue = values[colIndex]?.Trim();
                if (string.IsNullOrEmpty(cellValue)) continue;
                
                if (series.DataType == typeof(bool))
                {
                    if (cellValue == "1" || cellValue.Equals("true", StringComparison.OrdinalIgnoreCase))
                    {
                        if (series.BitValues != null && rowIndex < series.BitValues.Length)
                        {
                            lock (series.BitValues) // 병렬 처리를 위한 동기화
                            {
                                series.BitValues[rowIndex] = true;
                            }
                        }
                    }
                }
                else
                {
                    // 빠른 float 파싱
                    if (float.TryParse(cellValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatVal))
                    {
                        series.Values[rowIndex] = floatVal;
                    }
                }
            }
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