using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using MGK_Analyzer.Services.Analysis;

namespace MGK_Analyzer.ViewModels
{
    public sealed class AnalysisInputViewModel : INotifyPropertyChanged
    {
        private string _noLoadCsvPath = string.Empty;
        private string _loadCsvPath = string.Empty;

        private double _ratedVoltage = 380;
        private double _ratedCurrent = 10;
        private double _ratedSpeedRpm = 1500;
        private double _ratedTorqueNm = 50;
        private double _ratedFrequencyHz = 60;
        private int _poleCount = 4;
        private double _ratedPowerKw = 3.7;
        private double _r12;
        private double _r23;
        private double _r31;

        private DateTime? _testDate = DateTime.Today;
        private string _testerName = string.Empty;

        private double _temperatureC;
        private double _humidityPercent;
        private string _remarks = string.Empty;

        private double _conductorTempCoeff = 235;
        private double _referenceTemp = 25;
        private double _resistanceMeasureTemp = 25;
        private double _windingRatedTemp = 85;
        private double _coolantTemp = 25;

        private string _selectedWindingMaterial = "Copper";

        public AnalysisInputViewModel()
        {
            WindingMaterials = new ObservableCollection<string>
            {
                "Copper",
                "Aluminum",
                "Other"
            };

            BrowseNoLoadCsvCommand = new RelayCommand(_ => BrowseCsv(isNoLoad: true));
            BrowseLoadCsvCommand = new RelayCommand(_ => BrowseCsv(isNoLoad: false));
            ValidateCsvCommand = new RelayCommand(_ => ValidateCsv(), _ => !HasUploadValidationError);

            // Skeleton: Save logic intentionally not implemented yet.
            SaveCommand = new RelayCommand(_ => MessageBox.Show("저장 로직은 추후 구현 예정입니다.", "알림", MessageBoxButton.OK, MessageBoxImage.Information),
                _ => CanSave);
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand BrowseNoLoadCsvCommand { get; }
        public ICommand BrowseLoadCsvCommand { get; }
        public ICommand ValidateCsvCommand { get; }
        public ICommand SaveCommand { get; }

        public ObservableCollection<string> WindingMaterials { get; }

        public string NoLoadCsvPath
        {
            get => _noLoadCsvPath;
            set
            {
                if (_noLoadCsvPath != value)
                {
                    _noLoadCsvPath = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public string LoadCsvPath
        {
            get => _loadCsvPath;
            set
            {
                if (_loadCsvPath != value)
                {
                    _loadCsvPath = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double RatedVoltage
        {
            get => _ratedVoltage;
            set
            {
                if (Math.Abs(_ratedVoltage - value) > double.Epsilon)
                {
                    _ratedVoltage = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double RatedCurrent
        {
            get => _ratedCurrent;
            set
            {
                if (Math.Abs(_ratedCurrent - value) > double.Epsilon)
                {
                    _ratedCurrent = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double RatedSpeedRpm
        {
            get => _ratedSpeedRpm;
            set
            {
                if (Math.Abs(_ratedSpeedRpm - value) > double.Epsilon)
                {
                    _ratedSpeedRpm = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double RatedTorqueNm
        {
            get => _ratedTorqueNm;
            set
            {
                if (Math.Abs(_ratedTorqueNm - value) > double.Epsilon)
                {
                    _ratedTorqueNm = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double RatedFrequencyHz
        {
            get => _ratedFrequencyHz;
            set
            {
                if (Math.Abs(_ratedFrequencyHz - value) > double.Epsilon)
                {
                    _ratedFrequencyHz = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public int PoleCount
        {
            get => _poleCount;
            set
            {
                if (_poleCount != value)
                {
                    _poleCount = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double RatedPowerKw
        {
            get => _ratedPowerKw;
            set
            {
                if (Math.Abs(_ratedPowerKw - value) > double.Epsilon)
                {
                    _ratedPowerKw = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public string SelectedWindingMaterial
        {
            get => _selectedWindingMaterial;
            set
            {
                if (_selectedWindingMaterial != value)
                {
                    _selectedWindingMaterial = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double R12
        {
            get => _r12;
            set
            {
                if (Math.Abs(_r12 - value) > double.Epsilon)
                {
                    _r12 = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double R23
        {
            get => _r23;
            set
            {
                if (Math.Abs(_r23 - value) > double.Epsilon)
                {
                    _r23 = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double R31
        {
            get => _r31;
            set
            {
                if (Math.Abs(_r31 - value) > double.Epsilon)
                {
                    _r31 = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double ConductorTempCoeff
        {
            get => _conductorTempCoeff;
            set
            {
                if (Math.Abs(_conductorTempCoeff - value) > double.Epsilon)
                {
                    _conductorTempCoeff = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double ReferenceTemp
        {
            get => _referenceTemp;
            set
            {
                if (Math.Abs(_referenceTemp - value) > double.Epsilon)
                {
                    _referenceTemp = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double ResistanceMeasureTemp
        {
            get => _resistanceMeasureTemp;
            set
            {
                if (Math.Abs(_resistanceMeasureTemp - value) > double.Epsilon)
                {
                    _resistanceMeasureTemp = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double WindingRatedTemp
        {
            get => _windingRatedTemp;
            set
            {
                if (Math.Abs(_windingRatedTemp - value) > double.Epsilon)
                {
                    _windingRatedTemp = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public double CoolantTemp
        {
            get => _coolantTemp;
            set
            {
                if (Math.Abs(_coolantTemp - value) > double.Epsilon)
                {
                    _coolantTemp = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public DateTime? TestDate
        {
            get => _testDate;
            set
            {
                if (_testDate != value)
                {
                    _testDate = value;
                    OnPropertyChanged();
                    OnValidationChanged();
                }
            }
        }

        public string TesterName
        {
            get => _testerName;
            set
            {
                if (_testerName != value)
                {
                    _testerName = value;
                    OnPropertyChanged();
                }
            }
        }

        public double TemperatureC
        {
            get => _temperatureC;
            set
            {
                if (Math.Abs(_temperatureC - value) > double.Epsilon)
                {
                    _temperatureC = value;
                    OnPropertyChanged();
                }
            }
        }

        public double HumidityPercent
        {
            get => _humidityPercent;
            set
            {
                if (Math.Abs(_humidityPercent - value) > double.Epsilon)
                {
                    _humidityPercent = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Remarks
        {
            get => _remarks;
            set
            {
                if (_remarks != value)
                {
                    _remarks = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool HasUploadValidationError => string.IsNullOrWhiteSpace(NoLoadCsvPath) || string.IsNullOrWhiteSpace(LoadCsvPath);

        public bool CanSave
        {
            get
            {
                if (HasUploadValidationError)
                {
                    return false;
                }

                if (TestDate == null)
                {
                    return false;
                }

                // Required rated fields (minimal)
                if (RatedVoltage <= 0 || RatedCurrent <= 0 || RatedSpeedRpm <= 0 || RatedTorqueNm <= 0 || RatedFrequencyHz <= 0 || PoleCount <= 0 || RatedPowerKw <= 0)
                {
                    return false;
                }

                if (R12 <= 0 || R23 <= 0 || R31 <= 0)
                {
                    return false;
                }

                return true;
            }
        }

        public bool CanValidate => !HasUploadValidationError;

        private void BrowseCsv(bool isNoLoad)
        {
            var dialog = new OpenFileDialog
            {
                Filter = "CSV 파일 (*.csv)|*.csv",
                DefaultExt = ".csv",
                CheckFileExists = true,
                CheckPathExists = true
            };

            if (dialog.ShowDialog() != true)
            {
                return;
            }

            if (isNoLoad)
            {
                NoLoadCsvPath = dialog.FileName;
            }
            else
            {
                LoadCsvPath = dialog.FileName;
            }
        }

        private void ValidateCsv()
        {
            try
            {
                var service = new LossAnalysisCsvService();
                var noLoadResult = service.ValidateAndComputeNoLoad(NoLoadCsvPath);
                var loadResult = service.ValidateAndComputeLoad(LoadCsvPath);

                Console.WriteLine("=== CSV Validation (No-Load) ===");
                Console.WriteLine(LossAnalysisCsvService.FormatValidationMessages(noLoadResult.Messages));
                Console.WriteLine("=== CSV Validation (Load) ===");
                Console.WriteLine(LossAnalysisCsvService.FormatValidationMessages(loadResult.Messages));

                var hasError = noLoadResult.Messages.Any(m => m.Severity == LossAnalysisCsvService.ValidationSeverity.Error) ||
                               loadResult.Messages.Any(m => m.Severity == LossAnalysisCsvService.ValidationSeverity.Error);

                if (hasError)
                {
                    MessageBox.Show("CSV 검증 실패: 콘솔 출력의 [Error] 항목을 확인하세요.", "Validate CSV", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                Console.WriteLine(LossAnalysisCsvService.FormatNoLoadSummary(noLoadResult.Points));
                Console.WriteLine(LossAnalysisCsvService.FormatLoadSummary(loadResult.Points));

                MessageBox.Show("CSV STEP 평균 계산 결과를 콘솔에 출력했습니다.", "Validate CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"CSV 검증/가공 중 오류 발생: {ex.Message}", "Validate CSV", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnValidationChanged()
        {
            OnPropertyChanged(nameof(HasUploadValidationError));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(CanValidate));

            if (SaveCommand is RelayCommand relay)
            {
                relay.RaiseCanExecuteChanged();
            }

            if (ValidateCsvCommand is RelayCommand validateRelay)
            {
                validateRelay.RaiseCanExecuteChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private sealed class RelayCommand : ICommand
        {
            private readonly Action<object?> _execute;
            private readonly Func<object?, bool>? _canExecute;

            public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null)
            {
                _execute = execute;
                _canExecute = canExecute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

            public void Execute(object? parameter) => _execute(parameter);

            public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
