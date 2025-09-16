using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace MGK_Analyzer.Models
{
    /// <summary>
    /// �޸� ����ȭ�� �����ͼ�
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

        public Dictionary<string, SeriesData> SeriesData { get; set; } = new Dictionary<string, SeriesData>();

        // ������ �ð� ��� (overflow ����)
        public DateTime GetTimeAt(int index) 
        {
            try
            {
                // ���� üũ
                if (index < 0 || index >= TotalSamples)
                {
                    Console.WriteLine($"WARNING: GetTimeAt - �ε��� ���� �ʰ�: {index} (TotalSamples: {TotalSamples})");
                    return BaseTime;
                }
                
                // TimeInterval�� �ʹ� ũ�� ����
                var safeTimeInterval = Math.Min(Math.Max(TimeInterval, 0.001f), 3600f); // 0.001�� ~ 1�ð����� ����
                
                // ����� �� �� �� üũ
                var totalSeconds = (double)index * safeTimeInterval;
                if (totalSeconds > TimeSpan.MaxValue.TotalSeconds || totalSeconds < TimeSpan.MinValue.TotalSeconds)
                {
                    Console.WriteLine($"WARNING: GetTimeAt - �ð� ���� �ʰ�: totalSeconds={totalSeconds}");
                    return BaseTime.AddHours(1); // �⺻������ 1�ð� �� ��ȯ
                }
                
                return BaseTime.AddSeconds(totalSeconds);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"ERROR: GetTimeAt ���� - index={index}, TimeInterval={TimeInterval}, BaseTime={BaseTime}: {ex.Message}");
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
    /// �ø��� ������
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

        // ����� ������ ����
        public float[] Values { get; set; }
        public byte[] BitValues { get; set; }

        public string DisplayName => $"{Name} ({Unit})";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// ��Ʈ ������ ����Ʈ
    /// </summary>
    public class ChartDataPoint
    {
        public DateTime Time { get; set; }
        public double Value { get; set; }
        public string SeriesName { get; set; }
    }

    /// <summary>
    /// ��Ʈ�� �ø���
    /// </summary>
    public class ChartSeriesViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        private SeriesData _seriesData;

        public SeriesData SeriesData
        {
            get => _seriesData;
            set { _seriesData = value; OnPropertyChanged(); OnPropertyChanged(nameof(DisplayName)); }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set 
            { 
                _isSelected = value;
                if (SeriesData != null)
                    SeriesData.IsVisible = value;
                OnPropertyChanged();
            }
        }

        public string DisplayName => SeriesData?.DisplayName ?? "";

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}