using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace ArdisCVDCore.trends
{
    /// <summary>
    /// Everything the four trend windows have in common: the chart, the
    /// horizontal scroll-back bar, the once-a-second sampling tick, the
    /// right-click GraphScaler dialog and the window-position INI round trip.
    /// </summary>
    /// <remarks>
    /// Before the redesign each section had one window that mixed its controls
    /// and its chart, and the chart half was copy-pasted four times. The controls
    /// now live on MainForm, so what is left is only the chart -- and there is no
    /// reason to keep four copies of it. A derived window supplies its series in
    /// its constructor and fills them in <see cref="AppendPoints"/>; everything
    /// else happens here.
    /// </remarks>
    public abstract class TrendForm : Form, ITrendChartHost
    {
        // A point a second, so: an hour of history, 100 points (~1.5 min) on
        // screen by default. Both carried over from the old section windows.
        protected int numberOfPointsInChart = 3600;
        protected int numberOfPointsInArea = 100;

        public double MaximumYAxis { get; set; }
        public double MinimumYAxis { get; set; }
        public int PointsViewIndex { get; set; }

        private readonly string _iniSection;
        private readonly Timer _timer;
        private readonly HScrollBar _scrollBar;

        private int _leftLimit;
        private bool _valChangingOn;
        private int _tickCount;

        protected Chart chart;

        protected TrendForm(string iniSection, string title, double yMinimum, double yMaximum)
        {
            _iniSection = iniSection;
            MinimumYAxis = yMinimum;
            MaximumYAxis = yMaximum;

            Text = title;
            ClientSize = new Size(900, 600);
            StartPosition = FormStartPosition.Manual;
            Icon = Res.AppIcon;

            ChartArea area = new ChartArea("ChartArea1");
            area.AxisX.IsLabelAutoFit = false;
            area.AxisX.LabelStyle.Angle = -90;
            area.AxisX.MajorGrid.LineColor = Color.Silver;
            area.AxisY.MajorGrid.LineColor = Color.Silver;
            area.AxisY.Minimum = yMinimum;
            area.AxisY.Maximum = yMaximum;

            Legend legend = new Legend("Legend1");
            legend.BackColor = Color.Transparent;
            legend.DockedToChartArea = "ChartArea1";
            legend.Docking = Docking.Left;

            chart = new Chart { Dock = DockStyle.Fill };
            chart.ChartAreas.Add(area);
            chart.Legends.Add(legend);
            chart.MouseClick += Chart_MouseClick;
            chart.MouseWheel += Chart_MouseWheel;

            _scrollBar = new HScrollBar
            {
                Dock = DockStyle.Bottom,
                MaximumSize = new Size(0, 16),
                Maximum = 1009,
                Visible = false,
                Enabled = false
            };
            _scrollBar.ValueChanged += ScrollBar_ValueChanged;
            _scrollBar.MouseEnter += delegate { _valChangingOn = true; };
            _scrollBar.MouseLeave += delegate { _valChangingOn = false; };

            Controls.Add(chart);
            Controls.Add(_scrollBar);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += Timer_Tick;
        }

        /// <summary>Adds a line series; call from the derived constructor.</summary>
        protected Series AddSeries(string name, string legendText, Color color, int borderWidth)
        {
            Series series = new Series(name)
            {
                ChartArea = "ChartArea1",
                ChartType = SeriesChartType.Line,
                Legend = "Legend1",
                LegendText = legendText,
                Color = color,
                BorderWidth = borderWidth
            };
            chart.Series.Add(series);
            return series;
        }

        /// <summary>Adds the dashed companion line a setpoint series is drawn with.</summary>
        protected Series AddSetPointSeries(string name, string legendText, Color color)
        {
            Series series = AddSeries(name, legendText, color, 2);
            series.BorderDashStyle = ChartDashStyle.DashDot;
            return series;
        }

        /// <summary>One point per series, every second. Use <see cref="Plot"/>.</summary>
        protected abstract void AppendPoints(string xTime);

        /// <summary>
        /// Appends one point. <paramref name="gap"/> leaves a hole in the line
        /// instead of plotting a misleading zero when the reading is not valid --
        /// MSChart's own axis auto-scaling throws on a raw NaN, IsEmpty is its
        /// supported way of saying "no data here".
        /// </summary>
        protected void Plot(string seriesName, string xTime, double y, bool gap)
        {
            int index = chart.Series[seriesName].Points.AddXY(xTime, y);
            if (gap)
                chart.Series[seriesName].Points[index].IsEmpty = true;
        }

        protected void Plot(string seriesName, string xTime, double y)
        {
            chart.Series[seriesName].Points.AddXY(xTime, y);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowPlacement();
            _timer.Start();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            chart.ChartAreas["ChartArea1"].AxisX.ScaleView.Zoomable = true;
            chart.ChartAreas["ChartArea1"].AxisY.ScaleView.Zoomable = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            IniWriter.INI.Write(_iniSection, "X", Location.X.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(_iniSection, "Y", Location.Y.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(_iniSection, "Width", Width.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(_iniSection, "Height", Height.ToString(CultureInfo.InvariantCulture));
            base.OnFormClosing(e);
        }

        private void RestoreWindowPlacement()
        {
            if (IniWriter.INI.KeyExists("X", _iniSection) && IniWriter.INI.KeyExists("Y", _iniSection))
                Location = new Point(
                    int.Parse(IniWriter.INI.ReadINI(_iniSection, "X")),
                    int.Parse(IniWriter.INI.ReadINI(_iniSection, "Y")));

            if (IniWriter.INI.KeyExists("Width", _iniSection) && IniWriter.INI.KeyExists("Height", _iniSection))
                Size = new Size(
                    int.Parse(IniWriter.INI.ReadINI(_iniSection, "Width")),
                    int.Parse(IniWriter.INI.ReadINI(_iniSection, "Height")));

            if (Location.X < 0 || Location.Y < 0)
                Location = new Point(0, 0);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            _tickCount++;
            int count = _tickCount;
            string xTime = DateTime.Now.ToLongTimeString();

            TrimPoints();

            if (count > numberOfPointsInChart)
                _leftLimit = count - numberOfPointsInChart;

            if (count > numberOfPointsInArea)
            {
                _scrollBar.Enabled = true;
                _scrollBar.Visible = true;
                if (!_valChangingOn)
                {
                    _scrollBar.Maximum = count;
                    _scrollBar.LargeChange = numberOfPointsInArea / count + numberOfPointsInArea;
                    _scrollBar.Value = _scrollBar.Maximum - numberOfPointsInArea;
                }
            }
            else if (!_valChangingOn)
            {
                _scrollBar.Enabled = false;
                _scrollBar.Visible = false;
                chart.ChartAreas["ChartArea1"].AxisX.Maximum = count;
            }

            AppendPoints(xTime);

            if (!_valChangingOn && count <= numberOfPointsInArea)
                chart.ChartAreas["ChartArea1"].AxisX.Minimum = count - numberOfPointsInArea;
        }

        // Every series gets exactly one point per tick, so they all reach the
        // cap together and the first series is a good enough bellwether.
        private void TrimPoints()
        {
            if (chart.Series.Count == 0 || chart.Series[0].Points.Count < numberOfPointsInChart)
                return;

            foreach (Series series in chart.Series)
                series.Points.RemoveAt(0);
        }

        private void ScrollBar_ValueChanged(object sender, EventArgs e)
        {
            chart.ChartAreas["ChartArea1"].AxisX.Minimum = _leftLimit + (double)_scrollBar.Value;
            chart.ChartAreas["ChartArea1"].AxisX.Maximum = _leftLimit + _scrollBar.Value + numberOfPointsInArea;
        }

        private void Chart_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right)
                return;

            MinimumYAxis = chart.ChartAreas["ChartArea1"].AxisY.Minimum;
            MaximumYAxis = chart.ChartAreas["ChartArea1"].AxisY.Maximum;

            GraphScaler graphScaler = new GraphScaler(this);
            graphScaler.StartPosition = FormStartPosition.CenterParent;
            if (graphScaler.ShowDialog() != DialogResult.OK)
                return;

            chart.ChartAreas["ChartArea1"].AxisY.Minimum = MinimumYAxis;
            chart.ChartAreas["ChartArea1"].AxisY.Maximum = MaximumYAxis;

            if (_tickCount > numberOfPointsInChart)
                _leftLimit = _tickCount - numberOfPointsInChart;

            // "Last N points" -- entry 10 means "everything recorded so far",
            // and a fixed N is refused until that many points actually exist.
            if (PointsViewIndex == 10)
                numberOfPointsInArea = _tickCount;
            else
            {
                int requested = (PointsViewIndex + 1) * 100;
                if (PointsViewIndex == 0 || _tickCount >= requested)
                    numberOfPointsInArea = requested;
            }
        }

        private void Chart_MouseWheel(object sender, MouseEventArgs e)
        {
            OnChartMouseWheel(e);
        }

        /// <summary>
        /// Wheel zoom of the Y axis. The default steps one decade at a time down
        /// to 0.001, which suits every axis that spans a single order of
        /// magnitude (sccm, kW). Pressure overrides it -- a Torr axis has to
        /// reach 1e-8.
        /// </summary>
        protected virtual void OnChartMouseWheel(MouseEventArgs e)
        {
            double max = chart.ChartAreas["ChartArea1"].AxisY.Maximum;
            double min = chart.ChartAreas["ChartArea1"].AxisY.Minimum;

            if (e.Delta > 0)
            {
                if (max >= 10) max += 10;
                else if (max >= 1 & max < 10) max += 1;
                else if (max >= 0.1 & max < 1) max += 0.1;
                else if (max >= 0.01 & max < 0.1) max += 0.01;
                else if (max >= 0.001 & max < 0.01) max += 0.001;
            }
            else if (e.Delta < 0)
            {
                if (max > 10 + min) max -= 10;
                else if (max > 1 & max <= 10) max -= 1;
                else if (max > 0.1 & max <= 1) max -= 0.1;
                else if (max > 0.01 & max <= 0.1) max -= 0.01;
                else if (max > 0.001 & max <= 0.01) max -= 0.001;
            }

            if (max > 10 + min)
            {
                max = Math.Round(max);
                min = Math.Round(min);
            }
            else if (max >= 1 & max < 10)
            {
                max = Math.Round(max, 1);
                min = Math.Round(min, 1);
            }
            else if (max >= 0.1 & max < 1)
            {
                max = Math.Round(max, 2);
                min = Math.Round(min, 2);
            }
            else
            {
                max = Math.Round(max, 3);
                min = Math.Round(min, 3);
            }

            if (max <= min) max += (min - max) * 2 + 0.001;

            chart.ChartAreas["ChartArea1"].AxisY.Maximum = max;
            chart.ChartAreas["ChartArea1"].AxisY.Minimum = min;
        }

        protected ChartArea Area
        {
            get { return chart.ChartAreas["ChartArea1"]; }
        }
    }
}
