using ArdisCVDCore.modules_hw;
using System.Drawing;
using System.Windows.Forms;

namespace ArdisCVDCore.trends
{
    /// <summary>
    /// View -> Temperature Trend: the active pyrometer's two channels and its
    /// two-colour ratio temperature.
    /// </summary>
    public class TemperatureTrendForm : TrendForm
    {
        public TemperatureTrendForm()
            : base("TemperatureTrend", "Temperature Trend", 0, 1500)
        {
            AddSeries("Ch1", "Ch1, °C", Color.Blue, 3);
            AddSeries("Ch2", "Ch2, °C", Color.SeaGreen, 3);
            AddSeries("Ratio", "1-2, °C", Color.Red, 3);
        }

        protected override void AppendPoints(string xTime)
        {
            // Only one physical pyrometer is ever wired at a time -- the same
            // active-reading selection MainForm uses for its Ch1/Ch2/½ boxes.
            PLC210PyrometerClient.PyrometerReading active = MainForm.SelectActivePyrometer(
                PLC210PyrometerClient.GetState());

            bool gap = active == null;
            Plot("Ch1", xTime, gap ? 0 : active.Ch1Temp, gap);
            Plot("Ch2", xTime, gap ? 0 : active.Ch2Temp, gap);
            Plot("Ratio", xTime, gap ? 0 : active.RatioTemp, gap);
        }

        // Ported as-is from the old Temperature Section window: a °C axis wants
        // coarse steps at the top of its range and fine ones near zero, which is
        // a different shape from the shared decade zoom.
        protected override void OnChartMouseWheel(MouseEventArgs e)
        {
            double d = Area.AxisY.Maximum;
            double min = Area.AxisY.Minimum + 0.000001;

            if (e.Delta > 0)
            {
                if (d >= 20 + min) d += 10;
                else if (d >= 2 + min) { d += 1; Area.AxisY.LabelStyle.Format = "0.#"; }
                else if (d >= 1 + min) { d += 0.5; Area.AxisY.LabelStyle.Format = "0.##"; }
                else if (d >= 0.1 + min) d += 0.2;
                else d += 0.05;
                Area.AxisY.Maximum = d;
            }
            else if (e.Delta < 0)
            {
                if (d > 20 + min) d -= 10;
                else if (d < 2 + min) d -= 0.1;
                else if (d < 1 + min) { d -= 0.01; Area.AxisY.LabelStyle.Format = "0.###"; }
                else d -= 1;
                if (d <= 0 + min) { d = 0.01 + min; Area.AxisY.LabelStyle.Format = "0.####"; }
                Area.AxisY.Maximum = d;
            }
        }
    }
}
