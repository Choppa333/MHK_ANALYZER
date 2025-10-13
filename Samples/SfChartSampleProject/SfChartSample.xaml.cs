using System.Collections.ObjectModel;
using System.Windows.Controls;

namespace Samples.SfChartSampleProject
{
    public partial class SfChartSample : UserControl
    {
        public ObservableCollection<object> SeriesData { get; set; } = new ObservableCollection<object>();

        public SfChartSample()
        {
            InitializeComponent();

            // sample data
            for (int i = 0; i < 10; i++)
            {
                SeriesData.Add(new { Time = System.DateTime.Now.AddDays(i), Value = i });
            }

            DataContext = this;
        }
    }
}