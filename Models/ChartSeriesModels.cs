using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace MGK_Analyzer.Models
{
    /// <summary>
    /// Represents a single data series that can be displayed on the chart.
    /// This is the model for the list in the side panel.
    /// </summary>
    public partial class ChartSeriesViewModel : ObservableObject
    {
        [ObservableProperty]
        private bool _isSelected;

        [ObservableProperty]
        private bool _isSelectable = true;

        public SeriesData SeriesData { get; set; }
    }

    /// <summary>
    /// Represents a single point in a chart series.
    /// </summary>
    public class ChartDataPoint
    {
        // Use DateTime for X axis values to match DateTimeAxis expectations
        public System.DateTime Time { get; set; }
        public double Value { get; set; }

        public string? SeriesName { get; set; }

        public ChartDataPoint(System.DateTime x, double y)
        {
            Time = x;
            Value = y;
        }
    }
}

