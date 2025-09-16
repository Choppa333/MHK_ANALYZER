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
            using var overallTimer = PerformanceLogger.Instance.StartTimer($"��ü CSV �ε�: {Path.GetFileName(filePath)}", "CSV_Loading");
            
            try
            {
                PerformanceLogger.Instance.LogInfo($"CSV ���� �ε� ����: {filePath}", "CSV_Loading");
                
                var dataSet = new MemoryOptimizedDataSet
                {
                    FileName = Path.GetFileName(filePath)
                };

                // ���� ũ�� Ȯ��
                var fileInfo = new FileInfo(filePath);
                var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
                
                PerformanceLogger.Instance.LogInfo($"���� ũ��: {fileSizeMB:F2}MB", "CSV_Loading");
                progress?.Report(5);

                // ���� ���ڵ� �õ��Ͽ� �ѱ� ���� ����
                StreamReader reader = null;
                using var encodingTimer = PerformanceLogger.Instance.StartTimer("���ڵ� ���� �� ���� ����", "CSV_Loading");
                try
                {
                    reader = new StreamReader(filePath, new UTF8Encoding(true), true, 1024 * 1024); // 1MB ����
                    PerformanceLogger.Instance.LogInfo("UTF-8 BOM ���ڵ����� ���� ���� ����", "CSV_Loading");
                }
                catch
                {
                    try
                    {
                        reader = new StreamReader(filePath, Encoding.UTF8, true, 1024 * 1024);
                        PerformanceLogger.Instance.LogInfo("UTF-8 ���ڵ����� ���� ���� ����", "CSV_Loading");
                    }
                    catch
                    {
                        reader = new StreamReader(filePath, Encoding.GetEncoding("EUC-KR"), true, 1024 * 1024);
                        PerformanceLogger.Instance.LogInfo("EUC-KR ���ڵ����� ���� ���� ����", "CSV_Loading");
                    }
                }
                encodingTimer.Dispose();

                using (reader)
                {
                    // ��� ���� �б�
                    using var headerTimer = PerformanceLogger.Instance.StartTimer("��� ���� �б�", "CSV_Loading");
                    var headersLine = await reader.ReadLineAsync();
                    var dataTypesLine = await reader.ReadLineAsync();
                    var unitsLine = await reader.ReadLineAsync();

                    if (string.IsNullOrEmpty(headersLine) || string.IsNullOrEmpty(dataTypesLine) || string.IsNullOrEmpty(unitsLine))
                    {
                        throw new InvalidDataException("CSV ������ ��� ������ �ùٸ��� �ʽ��ϴ�. 3���� ����� �ʿ��մϴ�.");
                    }

                    var headers = headersLine.Split(',');
                    var dataTypes = dataTypesLine.Split(',');
                    var units = unitsLine.Split(',');
                    
                    PerformanceLogger.Instance.LogInfo($"��� �Ľ� �Ϸ� - �÷� ��: {headers.Length}", "CSV_Loading");
                    headerTimer.Dispose();

                    progress?.Report(10);

                    // ���� ���� �� ��� (���ø� ���)
                    int estimatedRows;
                    using (var estimateTimer = PerformanceLogger.Instance.StartTimer("�� �� ����", "CSV_Loading"))
                    {
                        estimatedRows = await EstimateRowCountAsync(reader, fileSizeMB);
                        PerformanceLogger.Instance.LogInfo($"���� �� ��: {estimatedRows:N0}", "CSV_Loading");
                    }
                    progress?.Report(15);

                    // �ø��� ���� ���� (�޸� �Ҵ� ����ȭ)
                    Dictionary<string, SeriesData> seriesDict;
                    using (var seriesTimer = PerformanceLogger.Instance.StartTimer("�ø��� ���� ����", "CSV_Loading"))
                    {
                        seriesDict = new Dictionary<string, SeriesData>(headers.Length);
                        int colorIndex = 0;
                        
                        for (int i = 2; i < headers.Length; i++) // �ð� �÷� ����
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
                                BitValues = typeName == "bit" ? new byte[(estimatedRows + 7) / 8] : null,
                                Color = DefaultColors[colorIndex % DefaultColors.Length],
                                IsVisible = false
                            };
                            seriesDict[series.Name] = series;
                            colorIndex++;
                        }
                        
                        PerformanceLogger.Instance.LogInfo($"�ø��� ���� �Ϸ� - �ø��� ��: {seriesDict.Count}", "CSV_Loading");
                    }

                    progress?.Report(20);

                    // ��Ʈ���� ������� ������ �ε� (ûũ ����)
                    int lineCount;
                    using (var loadTimer = PerformanceLogger.Instance.StartTimer("������ ��Ʈ���� �ε�", "CSV_Loading"))
                    {
                        lineCount = await LoadDataStreamingAsync(reader, headers, seriesDict, dataSet, estimatedRows, progress);
                        PerformanceLogger.Instance.LogInfo($"���� ������ �� ��: {lineCount:N0}", "CSV_Loading");
                    }

                    progress?.Report(90);

                    // ���� ũ��� �迭 ����
                    if (lineCount != estimatedRows)
                    {
                        using var resizeTimer = PerformanceLogger.Instance.StartTimer("�迭 ũ�� ����", "CSV_Loading");
                        await Task.Run(() => ResizeArrays(seriesDict, lineCount));
                    }

                    dataSet.TotalSamples = lineCount;
                    dataSet.SeriesData = seriesDict;
                    
                    // ��� ���� ��� (��׶���)
                    _ = Task.Run(() => 
                    {
                        using var statsTimer = PerformanceLogger.Instance.StartTimer("��� ���� ��� (��׶���)", "CSV_Loading");
                        CalculateStatistics(dataSet);
                    });
                    
                    progress?.Report(100);
                    
                    PerformanceLogger.Instance.LogInfo($"CSV �ε� �Ϸ� - ����: {dataSet.FileName}, ��: {lineCount:N0}, �ø���: {seriesDict.Count}", "CSV_Loading");
                    return dataSet;
                }
            }
            catch (Exception ex)
            {
                PerformanceLogger.Instance.LogError($"CSV ���� �ε� ����: {ex.Message}", "CSV_Loading");
                throw new InvalidDataException($"CSV ���� �ε� �� ���� �߻�: {ex.Message}", ex);
            }
        }

        private async Task<int> EstimateRowCountAsync(StreamReader reader, double fileSizeMB)
        {
            var currentPosition = reader.BaseStream.Position;
            
            // �� ���� ���÷� ��Ȯ�� ���
            var sampleLines = 0;
            var totalBytes = 0;
            const int SAMPLE_SIZE = 50; // 10 -> 50���� ����
            
            for (int i = 0; i < SAMPLE_SIZE && !reader.EndOfStream; i++)
            {
                var line = await reader.ReadLineAsync();
                if (!string.IsNullOrEmpty(line))
                {
                    totalBytes += Encoding.UTF8.GetByteCount(line) + 2; // \r\n
                    sampleLines++;
                }
            }
            
            // ���� ��ġ�� ����
            reader.BaseStream.Position = currentPosition;
            reader.DiscardBufferedData();
            
            if (sampleLines > 0)
            {
                var avgLineBytes = (double)totalBytes / sampleLines;
                var estimatedRows = (int)(fileSizeMB * 1024 * 1024 / avgLineBytes);
                
                // 10% ������ �߰� (�迭 ���Ҵ� ����)
                return (int)(estimatedRows * 1.1);
            }
            
            return (int)(fileSizeMB * 1000); // �⺻ ����ġ
        }

        private async Task<int> LoadDataStreamingAsync(StreamReader reader, string[] headers, 
            Dictionary<string, SeriesData> seriesDict, MemoryOptimizedDataSet dataSet, 
            int estimatedRows, IProgress<int> progress)
        {
            Console.WriteLine("=== LoadDataStreamingAsync ���� (����ȭ ����) ===");
            
            // �� ū ûũ ũ��� ���� ����
            const int CHUNK_SIZE = 50000; // 10,000 -> 50,000���� ����
            const int BUFFER_SIZE = CHUNK_SIZE * 200; // ��뷮 ���ڿ� ����
            
            var lineCount = 0;
            var charBuffer = new char[BUFFER_SIZE];
            var stringBuilder = new StringBuilder(BUFFER_SIZE);
            
            // ù ��° ���ο��� �ð� ���� ����
            var firstLine = await reader.ReadLineAsync();
            if (!string.IsNullOrEmpty(firstLine))
            {
                var firstValues = firstLine.Split(',');
                
                if (DateTime.TryParse(firstValues[0]?.Trim(), out var baseTime))
                {
                    dataSet.BaseTime = baseTime;
                }
                else
                {
                    dataSet.BaseTime = DateTime.Today;
                }
                
                if (firstValues.Length > 1 && double.TryParse(firstValues[1]?.Trim(), out var interval))
                {
                    var safeInterval = Math.Max(0.001, Math.Min(3600.0, Math.Abs(interval)));
                    dataSet.TimeInterval = (float)safeInterval;
                }
                else
                {
                    dataSet.TimeInterval = 1.0f;
                }
                
                // ù ��° ���� ó��
                ProcessSingleLine(firstLine, headers, seriesDict, 0);
                lineCount = 1;
            }

            // ��뷮 ûũ ������ �б�
            var lines = new List<string>(CHUNK_SIZE);
            string line;
            
            while ((line = await reader.ReadLineAsync()) != null)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    lines.Add(line);
                    lineCount++;
                    
                    // ûũ�� ���� ���� ���� ó��
                    if (lines.Count >= CHUNK_SIZE)
                    {
                        await ProcessChunkParallelAsync(lines, headers, seriesDict, lineCount - lines.Count);
                        lines.Clear();
                        
                        // ����� ������Ʈ (�� ����)
                        var progressPercent = 20 + (int)((double)lineCount / estimatedRows * 60);
                        progress?.Report(Math.Min(85, progressPercent));
                        
                        // UI �������� ���� ��� �纸 (�� ����)
                        if (lineCount % (CHUNK_SIZE * 2) == 0)
                        {
                            await Task.Delay(1);
                        }
                    }
                }
            }

            // ������ ûũ ó��
            if (lines.Count > 0)
            {
                await ProcessChunkParallelAsync(lines, headers, seriesDict, lineCount - lines.Count);
            }

            Console.WriteLine($"����ȭ�� ������ �ε� �Ϸ� - �� {lineCount:N0}�� ����");
            return lineCount;
        }

        private async Task ProcessChunkParallelAsync(List<string> lines, string[] headers, 
            Dictionary<string, SeriesData> seriesDict, int startIndex)
        {
            // CPU �ھ� ���� ���� ���� ó��
            var coreCount = Environment.ProcessorCount;
            var chunkSize = Math.Max(1000, lines.Count / coreCount);
            
            await Task.Run(() =>
            {
                Parallel.For(0, Math.Min(coreCount, (lines.Count + chunkSize - 1) / chunkSize), parallelIndex =>
                {
                    var start = parallelIndex * chunkSize;
                    var end = Math.Min(start + chunkSize, lines.Count);
                    
                    for (int lineIdx = start; lineIdx < end; lineIdx++)
                    {
                        ProcessSingleLine(lines[lineIdx], headers, seriesDict, startIndex + lineIdx);
                    }
                });
            });
        }

        private void ProcessSingleLine(string line, string[] headers, 
            Dictionary<string, SeriesData> seriesDict, int rowIndex)
        {
            var values = line.Split(',');
            
            // �÷� �� ���� üũ�� ���� ����
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
                        int byteIndex = rowIndex / 8;
                        int bitIndex = rowIndex % 8;
                        if (byteIndex < series.BitValues.Length)
                        {
                            lock (series.BitValues) // ���� ó���� ���� ����ȭ
                            {
                                series.BitValues[byteIndex] |= (byte)(1 << bitIndex);
                            }
                        }
                    }
                }
                else
                {
                    // ���� float �Ľ�
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
                        var newBitValues = new byte[(actualSize + 7) / 8];
                        Array.Copy(series.BitValues, newBitValues, Math.Min(series.BitValues.Length, newBitValues.Length));
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
                
                // �� ���� ��ȸ�� Min, Max, Sum ���
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