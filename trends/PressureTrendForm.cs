using ArdisCVDCore.modules_hw;
using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArdisCVDCore.trends
{
    /// <summary>
    /// View -> Pressure Trend: chamber pressure against its setpoint, plus the
    /// Thyracont high-vacuum gauge on the same axis.
    /// </summary>
    public class PressureTrendForm : TrendForm
    {
        // Cache of the ChamberPressure/ChamberPressureSetPoint colours, captured
        // just before the wheel zoom blanks them out down in Hi-Vac territory --
        // Color.Empty means "not currently hidden, nothing to restore".
        private Color _chamberPressureColor = Color.Empty;
        private Color _chamberPressureSetPointColor = Color.Empty;

        public PressureTrendForm()
            : base("PressureTrend", "Pressure Trend", 0, 800)
        {
            AddSeries("ChamberPressure", "ChamberPressure (Torr)", Color.RoyalBlue, 3);
            AddSetPointSeries("ChamberPressureSetPoint", "ChamberPressureSetPoint (Torr)", Color.Black);
            AddSeries("HiVac", "HiVac (Torr)", Color.Fuchsia, 1);
        }

        protected override void AppendPoints(string xTime)
        {
            PLC210PidClient.State pidState = PLC210PidClient.GetState();
            PLC210PidClient.Channel chamber = PLC210PidClient.GetChamberChannel();
            PLC210ThyracontClient.State thyracontState = PLC210ThyracontClient.GetState();

            double measured = pidState.PlcPressureAvailable ? Math.Max(0, pidState.PlcPressureTorr) : 0;

            // Zero, not a gap, before the first SET -- same as the old window:
            // no setpoint has been committed, so the PLC is holding at nothing.
            Plot("ChamberPressure", xTime, measured);
            Plot("ChamberPressureSetPoint", xTime, chamber == null ? 0 : chamber.Setpoint);
            Plot("HiVac", xTime, thyracontState.PressureTorr, !thyracontState.HasValidValue);
        }

        // A Torr axis on this rig spans from 800 down to ~1e-8, so the shared
        // one-decade-at-a-time zoom is not enough here.
        protected override void OnChartMouseWheel(MouseEventArgs e)
        {
            double max = Area.AxisY.Maximum;
            double min = Area.AxisY.Minimum;

            if (e.Delta > 0)
            {
                if (max >= 10) max += 10;
                else if (max >= 1 & max < 10) max += 1;
                else if (max >= 0.1 & max < 1) max += 0.1;
                else if (max >= 0.01 & max < 0.1) max += 0.01;
                else if (max >= 0.001 & max < 0.01) max += 0.001;
                else if (max >= 0.0001 & max < 0.001) max += 0.0001;
                else if (max >= 0.00001 & max < 0.0001) max += 0.00001;
                else if (max >= 0.000001 & max < 0.00001) max += 0.000001;
                else if (max >= 0.0000001 & max < 0.000001) max += 0.0000001;
                else if (max > 0.00000001 & max <= 0.0000001) max = 0.0000002;
            }
            else if (e.Delta < 0)
            {
                if (max > 10 + min) max -= 10;
                else if (max > 1 & max <= 10) max -= 1;
                else if (max > 0.1 & max <= 1) max -= 0.1;
                else if (max > 0.01 & max <= 0.1) max -= 0.01;
                else if (max > 0.001 & max <= 0.01) max -= 0.001;
                else if (max > 0.0001 & max <= 0.001) max -= 0.0001;
                else if (max > 0.00001 & max <= 0.0001) max -= 0.00001;
                else if (max > 0.000001 & max <= 0.00001) max -= 0.000001;
                else if (max > 0.0000001 & max <= 0.000001) max -= 0.0000001;
            }

            if (max > 10 + min)
            {
                max = Math.Round(max);
                min = Math.Round(min);
                Area.AxisY.LabelStyle.Format = "";
            }
            else if (max >= 1 & max < 10)
            {
                max = Math.Round(max, 1);
                min = Math.Round(min, 1);
                Area.AxisY.LabelStyle.Format = "";
            }
            else if (max >= 0.1 & max < 1)
            {
                max = Math.Round(max, 2);
                min = Math.Round(min, 2);
                Area.AxisY.LabelStyle.Format = "";
                RestoreChamberPressureVisibility();
            }
            else if (max >= 0.01 & max < 0.1)
            {
                max = Math.Round(max, 3);
                min = Math.Round(min, 3);
                Area.AxisY.LabelStyle.Format = "0.###E-0";
                HideChamberPressure();
            }
            else if (max >= 0.001 & max < 0.01) { max = Math.Round(max, 4); min = Math.Round(min, 4); }
            else if (max >= 0.0001 & max < 0.001) { max = Math.Round(max, 5); min = Math.Round(min, 5); }
            else if (max >= 0.00001 & max < 0.0001) { max = Math.Round(max, 6); min = Math.Round(min, 6); }
            else if (max >= 0.000001 & max < 0.00001) { max = Math.Round(max, 7); min = Math.Round(min, 7); }
            else if (max >= 0.0000001 & max < 0.000001) { max = Math.Round(max, 8); min = Math.Round(min, 8); }
            else if (max >= 0.00000001 & max < 0.0000001) { max = Math.Round(max, 9); min = Math.Round(min, 9); }

            if (max <= min) max += (min - max) * 2 + 0.00000001;

            Area.AxisY.Maximum = max;
            Area.AxisY.Minimum = min;
        }

        // Blank the line and its legend entry out rather than Series.Enabled =
        // false: a disabled series stops counting as chart data entirely, and if
        // it is the only source of data left (HiVac may have no valid reading
        // yet) the ChartArea's axis recalculation on the next repaint throws
        // ("the minimum and maximum axis values have not been specified").
        // Color.Transparent keeps the series a normal, counted data source.
        private void HideChamberPressure()
        {
            if (chart.Series["ChamberPressure"].Color != Color.Transparent)
            {
                _chamberPressureColor = chart.Series["ChamberPressure"].Color;
                _chamberPressureSetPointColor = chart.Series["ChamberPressureSetPoint"].Color;
            }

            chart.Series["ChamberPressure"].Color = Color.Transparent;
            chart.Series["ChamberPressureSetPoint"].Color = Color.Transparent;
            chart.Series["ChamberPressure"].IsVisibleInLegend = false;
            chart.Series["ChamberPressureSetPoint"].IsVisibleInLegend = false;
        }

        private void RestoreChamberPressureVisibility()
        {
            if (_chamberPressureColor == Color.Empty)
                return;

            chart.Series["ChamberPressure"].Color = _chamberPressureColor;
            chart.Series["ChamberPressureSetPoint"].Color = _chamberPressureSetPointColor;
            chart.Series["ChamberPressure"].IsVisibleInLegend = true;
            chart.Series["ChamberPressureSetPoint"].IsVisibleInLegend = true;
        }
    }
}
