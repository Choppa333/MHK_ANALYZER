using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Media;

namespace MGK_Analyzer.Models
{
    /// <summary>
    /// 3D Surface 차트를 위한 3D 포인트 데이터
    /// </summary>
    public class Surface3DPoint : INotifyPropertyChanged
    {
        private double _x;
        private double _y;
        private double _z;

        public double X
        {
            get => _x;
            set
            {
                _x = value;
                OnPropertyChanged(nameof(X));
            }
        }

        public double Y
        {
            get => _y;
            set
            {
                _y = value;
                OnPropertyChanged(nameof(Y));
            }
        }

        public double Z
        {
            get => _z;
            set
            {
                _z = value;
                OnPropertyChanged(nameof(Z));
            }
        }

        // Surface 색상 그라데이션을 위한 추가 속성
        public double Value => Z; // Z 값을 색상 매핑에 사용

        public Surface3DPoint()
        {
        }

        public Surface3DPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 3D Surface 시리즈 데이터
    /// </summary>
    public class Surface3DSeriesData : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _unit = string.Empty;
        private Color _color = Colors.Blue;
        private ObservableCollection<Surface3DPoint> _dataPoints = new();
        private bool _isVisible = true;
        private string _surfaceType = "Surface";

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public string Unit
        {
            get => _unit;
            set
            {
                _unit = value;
                OnPropertyChanged(nameof(Unit));
            }
        }

        public Color Color
        {
            get => _color;
            set
            {
                _color = value;
                OnPropertyChanged(nameof(Color));
            }
        }

        public ObservableCollection<Surface3DPoint> DataPoints
        {
            get => _dataPoints;
            set
            {
                _dataPoints = value;
                OnPropertyChanged(nameof(DataPoints));
            }
        }

        public bool IsVisible
        {
            get => _isVisible;
            set
            {
                _isVisible = value;
                OnPropertyChanged(nameof(IsVisible));
            }
        }

        public string SurfaceType
        {
            get => _surfaceType;
            set
            {
                _surfaceType = value;
                OnPropertyChanged(nameof(SurfaceType));
            }
        }

        public Surface3DSeriesData()
        {
            Name = $"Surface Series {DateTime.Now.Ticks % 1000}";
            Unit = "Value";
            Color = GetRandomColor();
        }

        public Surface3DSeriesData(string name, string unit)
        {
            Name = name;
            Unit = unit;
            Color = GetRandomColor();
        }

        private static Color GetRandomColor()
        {
            Random random = new();
            return Color.FromRgb(
                (byte)random.Next(50, 255),
                (byte)random.Next(50, 255),
                (byte)random.Next(50, 255)
            );
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 3D Surface 차트 시리즈 뷰모델
    /// </summary>
    public class Surface3DSeriesViewModel : INotifyPropertyChanged
    {
        private Surface3DSeriesData _seriesData;
        private bool _isSelected = true;

        public Surface3DSeriesData SeriesData
        {
            get => _seriesData;
            set
            {
                _seriesData = value;
                OnPropertyChanged(nameof(SeriesData));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                if (_seriesData != null)
                {
                    _seriesData.IsVisible = value;
                }
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public Surface3DSeriesViewModel(Surface3DSeriesData seriesData)
        {
            _seriesData = seriesData ?? throw new ArgumentNullException(nameof(seriesData));
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// 3D Surface 데이터 생성기
    /// </summary>
    public static class Surface3DDataGenerator
    {
        /// <summary>
        /// 수학 함수 기반 3D Surface 데이터 생성
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateMathFunction(
            Func<double, double, double> function,
            double xMin, double xMax, int xSteps,
            double yMin, double yMax, int ySteps)
        {
            var points = new ObservableCollection<Surface3DPoint>();
            
            double xStep = (xMax - xMin) / (xSteps - 1);
            double yStep = (yMax - yMin) / (ySteps - 1);

            for (int i = 0; i < xSteps; i++)
            {
                for (int j = 0; j < ySteps; j++)
                {
                    double x = xMin + i * xStep;
                    double y = yMin + j * yStep;
                    double z = function(x, y);
                    
                    points.Add(new Surface3DPoint(x, y, z));
                }
            }

            return points;
        }

        /// <summary>
        /// 사인파 기반 Surface 생성
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateSinWave(
            double amplitude = 5.0, 
            double frequency = 1.0,
            int xSteps = 50, 
            int ySteps = 50)
        {
            return GenerateMathFunction(
                (x, y) => amplitude * Math.Sin(frequency * Math.Sqrt(x * x + y * y)),
                -10, 10, xSteps,
                -10, 10, ySteps
            );
        }

        /// <summary>
        /// 코사인파 기반 Surface 생성
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateCosWave(
            double amplitude = 3.0,
            double frequency = 0.5,
            int xSteps = 40,
            int ySteps = 40)
        {
            return GenerateMathFunction(
                (x, y) => amplitude * Math.Cos(frequency * x) * Math.Sin(frequency * y),
                -5, 5, xSteps,
                -5, 5, ySteps
            );
        }

        /// <summary>
        /// 가우시안 Surface 생성
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateGaussian(
            double amplitude = 10.0,
            double sigmaX = 2.0,
            double sigmaY = 2.0,
            int xSteps = 30,
            int ySteps = 30)
        {
            return GenerateMathFunction(
                (x, y) => amplitude * Math.Exp(-(x * x / (2 * sigmaX * sigmaX) + y * y / (2 * sigmaY * sigmaY))),
                -8, 8, xSteps,
                -8, 8, ySteps
            );
        }

        /// <summary>
        /// 랜덤 노이즈 Surface 생성
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateRandomNoise(
            double baseValue = 0.0,
            double noiseLevel = 2.0,
            int xSteps = 25,
            int ySteps = 25)
        {
            Random random = new();
            return GenerateMathFunction(
                (x, y) => baseValue + (random.NextDouble() - 0.5) * 2 * noiseLevel,
                0, 10, xSteps,
                0, 10, ySteps
            );
        }

        /// <summary>
        /// 복합 함수 Surface 생성 (실제 데이터와 유사한 패턴)
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateComplexSurface(
            int xSteps = 35,
            int ySteps = 35)
        {
            return GenerateMathFunction(
                (x, y) => 5 * Math.Sin(0.5 * x) * Math.Cos(0.3 * y) + 
                         2 * Math.Exp(-(x * x + y * y) / 50) +
                         0.5 * (x + y),
                -10, 10, xSteps,
                -10, 10, ySteps
            );
        }

        /// <summary>
        /// 간단한 효율맵 Surface 생성 (이미지 참조)
        /// </summary>
        public static ObservableCollection<Surface3DPoint> GenerateSimpleSurface(
            int xSteps = 20,
            int ySteps = 20)
        {
            var points = new ObservableCollection<Surface3DPoint>();
            
            // 좌표 범위를 이미지와 유사하게 설정
            double xMin = 0, xMax = 80;
            double yMin = 0, yMax = 80;
            double xStep = (xMax - xMin) / (xSteps - 1);
            double yStep = (yMax - yMin) / (ySteps - 1);

            for (int i = 0; i < xSteps; i++)
            {
                for (int j = 0; j < ySteps; j++)
                {
                    double x = xMin + i * xStep;
                    double y = yMin + j * yStep;
                    
                    // 이미지와 유사한 효율맵 함수
                    double normX = (x - 40) / 20.0; // 중심점을 40으로 하여 정규화
                    double normY = (y - 40) / 20.0; // 중심점을 40으로 하여 정규화
                    
                    // 첨부 이미지와 유사한 두 봉우리 Surface
                    double peak1 = 0.1 * Math.Exp(-((normX + 0.7)*(normX + 0.7) + (normY + 0.7)*(normY + 0.7)) / 0.4);  // 왼쪽 위 봉우리 (낮음)
                    double peak2 = 0.2 * Math.Exp(-((normX - 0.3)*(normX - 0.3) + (normY - 0.3)*(normY - 0.3)) / 0.6);   // 오른쪽 아래 봉우리 (높음)
                    double valley = -0.3 * Math.Exp(-((normX - 0.8)*(normX - 0.8) + (normY + 1.0)*(normY + 1.0)) / 1.0); // 골짜기
                    
                    // 부드러운 기본 곡면 추가
                    double baseWave = 0.05 * Math.Sin(normX * 2) * Math.Cos(normY * 1.5);
                    
                    double z = peak1 + peak2 + valley + baseWave;
                    
                    points.Add(new Surface3DPoint(x, y, z));
                }
            }

            return points;
        }
    }
}