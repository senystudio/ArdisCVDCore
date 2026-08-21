using System;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    /// <summary>
    /// A trend window whose Y axis range and visible point count this dialog edits.
    /// </summary>
    /// <remarks>
    /// Replaces the four near-identical constructor overloads this dialog used to
    /// carry (one per section window). Every trend window keeps these three values
    /// as fields and re-applies them to its chart after the dialog closes with OK.
    /// </remarks>
    public interface ITrendChartHost
    {
        double MaximumYAxis { get; set; }
        double MinimumYAxis { get; set; }
        int PointsViewIndex { get; set; }
    }

    public partial class GraphScaler : Form
    {
        private readonly ITrendChartHost _host;

        public GraphScaler(ITrendChartHost host)
        {
            InitializeComponent();
            _host = host;

            double maximum = _host.MaximumYAxis;
            if ((int)maximum > YMaximum.Maximum)
                maximum = (int)YMaximum.Maximum;

            YMaximum.Value = (decimal)maximum;
            YMinimum.Value = (decimal)_host.MinimumYAxis;
            PointsView.SelectedIndex = _host.PointsViewIndex;

            YMaximum.DecimalPlaces = 6;
            YMinimum.DecimalPlaces = 6;
        }

        private void OK_Click_1(object sender, EventArgs e)
        {
            if (YMinimum.Value >= YMaximum.Value)
                YMaximum.Value = YMinimum.Value + 1;

            _host.MinimumYAxis = (double)YMinimum.Value;
            _host.MaximumYAxis = (double)YMaximum.Value;
            _host.PointsViewIndex = PointsView.SelectedIndex;
            Close();
        }
    }
}
