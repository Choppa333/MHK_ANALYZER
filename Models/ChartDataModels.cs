using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace MGK_Analyzer.Models
{
    /// <summary>
    /// 메모리 최적화된 데이터셋
    /// </summary>
    public class MemoryOptimizedDataSet : INotifyPropertyChanged
    {
        private string _fileName;
        private DateTime _baseTime;
        private float _timeInterval;
        private int _totalSamples;

        public string FileName
        {
            get => _fileName;
            set { _fileName = value; OnPropertyChanged(); }
        }

        public DateTime BaseTime
        {
            get => _baseTime;
            set { _baseTime = value; OnPropertyChanged(); }
        }

        public float TimeInterval
        {
            get => _timeInterval;
            set { _timeInterval = value; OnPropertyChanged(); }
        }

        public int TotalSamples
        {
            get => _totalSamples;
            set { _totalSamples = value; OnPropertyChanged(); }
        }

        public int? MetaType { get; set; }

        public string? MetaDateRaw { get; set; }

        public Dictionary<string, SeriesData> SeriesData { get; set; } = new Dictionary<string, SeriesData>();

        // 안전한 시간 계산 (overflow 방지)
        public DateTime GetTimeAt(int index) 
        {
            try
            {
                // 범위 체크
                if (index < 0 || index >= TotalSamples)
                {
                    Console.WriteLine($"WARNING: GetTimeAt - 인덱스 범위 초과: {index} (TotalSamples: {TotalSamples})");
                    return BaseTime;
                }
                
                // TimeInterval이 너무 크면 제한
                var safeTimeInterval = Math.Min(Math.Max(TimeInterval, 0.001f), 3600f); // 0.001초 ~ 1시간으로 제한
                
                // 계산할 총 초 수 체크
                var totalSeconds = (double)index * safeTimeInterval;
                if (totalSeconds > TimeSpan.MaxValue.TotalSeconds || totalSeconds < TimeSpan.MinValue.TotalSeconds)
                {
                    Console.WriteLine($"WARNING: GetTimeAt - 시간 범위 초과: totalSeconds={totalSeconds}");
                    return BaseTime.AddHours(1); // 기본값으로 1시간 후 반환
                }
                
                return BaseTime.AddSeconds(totalSeconds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: GetTimeAt 오류 - index={index}, TimeInterval={TimeInterval}, BaseTime={BaseTime}: {ex.Message}");
                return BaseTime.AddSeconds(index); // fallback
            }
        }
        
        public double GetRelativeTimeAt(int index) 
        {
            if (index < 0 || index >= TotalSamples)
                return 0.0;
                
            return index * Math.Max(TimeInterval, 0.001f);
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 시리즈 데이터
    /// </summary>
    public class SeriesData : INotifyPropertyChanged
    {
        private string _name;
        private string _unit;
        private Type _dataType;
        private Brush _color;
        private bool _isVisible;
        private float _minValue;
        private float _maxValue;
        private float _avgValue;

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Unit
        {
            get => _unit;
            set { _unit = value; OnPropertyChanged(); }
        }

        public Type DataType
        {
            get => _dataType;
            set { _dataType = value; OnPropertyChanged(); }
        }

        public Brush Color
        {
            get => _color;
            set { _color = value; OnPropertyChanged(); }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set { _isVisible = value; OnPropertyChanged(); }
        }

        public float MinValue
        {
            get => _minValue;
            set { _minValue = value; OnPropertyChanged(); }
        }

        public float MaxValue
        {
            get => _maxValue;
            set { _maxValue = value; OnPropertyChanged(); }
        }

        public float AvgValue
        {
            get => _avgValue;
            set { _avgValue = value; OnPropertyChanged(); }
        }

        // 압축된 데이터 저장
        public float[] Values { get; set; }
        public System.Collections.BitArray BitValues { get; set; }

        public string DisplayName => $"{Name} ({Unit})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public IEnumerable<ChartDataPoint> GetCoupledData(SeriesData timeData, DateTime baseTime)
        {
            var dataPoints = new List<ChartDataPoint>();
            if (timeData == null || timeData.Values == null || Values == null)
            {
                return dataPoints;
            }

            int count = Math.Min(Values.Length, timeData.Values.Length);

            for (int i = 0; i < count; i++)
            {
                DateTime xValue = baseTime.AddSeconds(timeData.Values[i]);
                dataPoints.Add(new ChartDataPoint(xValue, Values[i]) { SeriesName = Name });
            }
            return dataPoints;
        }
    }
}