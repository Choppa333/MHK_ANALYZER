using System.Collections.ObjectModel;
using System.Windows.Controls;
using MGK_Analyzer.Models;

namespace MGK_Analyzer.Samples.Syncfusion
{
    public partial class SfChartSample : UserControl
    {
        public ObservableCollection<ChartDataPoint> SeriesData { get; set; } = new ObservableCollection<ChartDataPoint>();

        public SfChartSample()
        {
            InitializeComponent();

            // sample data
            for (int i = 0; i < 10; i++)
            {
                SeriesData.Add(new ChartDataPoint { Time = System.DateTime.Now.AddDays(i), Value = i });
            }

            DataContext = this;
        }
    }
}