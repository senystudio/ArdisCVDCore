using ArdisCVDCore.modules_hw;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    /// <summary>
    /// View -> PID Viewer: the chamber PID panel that used to sit down the right
    /// hand side of the old main window.
    /// </summary>
    /// <remarks>
    /// What the controller is doing, and the direct-drive override. The gains and
    /// output limits are NOT here: they are edited in Settings -> Process
    /// Parameters, which is the one place that owns them -- having the same five
    /// numbers on two screens meant two chances to disagree about which set was
    /// live. The pressure setpoint likewise stays in the main window's Chamber
    /// Pressure box, where the design puts it.
    ///
    /// Direct mode still lives here because it is an operating decision rather
    /// than a tuning parameter, and it takes effect immediately.
    ///
    /// Deliberately no connection state and no measured pressure either: the main
    /// window already carries both -- the Status plate for the link and the
    /// Chamber Pressure box for the reading.
    /// </remarks>
    public class PidViewerForm : Form
    {
        private const string IniSection = "PidViewer";

        private readonly CheckBox _direct;
        private readonly CustomNumericUpDown _directValue;

        private readonly Label _error;
        private readonly Label _p;
        private readonly Label _i;
        private readonly Label _d;
        private readonly Label _output;

        private readonly Timer _timer;

        public PidViewerForm()
        {
            Text = "PID Viewer";
            ClientSize = new Size(232, 164);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Icon = Res.AppIcon;

            // Straight onto the form, with no group box around them: with the
            // caption gone the frame was a border round the whole window saying
            // nothing, and the title bar already says which window this is.
            _error = AddReadout(this, "Error", 12);
            _p = AddReadout(this, "P", 34);
            _i = AddReadout(this, "I", 56);
            _d = AddReadout(this, "D", 78);
            _output = AddReadout(this, "Output", 100);

            // Below the readouts, separated from them: this is the one control on
            // the window, and everything above it is just reporting.
            _direct = new CheckBox
            {
                AutoSize = true,
                Location = new Point(LabelX, 134),
                Text = "Direct Input",
                UseVisualStyleBackColor = true
            };
            _direct.CheckedChanged += Direct_CheckedChanged;
            Controls.Add(_direct);

            _directValue = new CustomNumericUpDown
            {
                Location = new Point(ValueX, 132),
                Size = new Size(ValueWidth, 20),
                Maximum = 5000,
                WheelIncrement = 5
            };
            _directValue.ValueChanged += DirectValue_ValueChanged;
            Controls.Add(_directValue);

            LoadFromSettings();

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += Timer_Tick;
        }


        // One column of captions, one of values, shared by the readouts and the
        // Direct Input row so they line up.
        private const int LabelX = 12;
        private const int ValueX = 120;
        private const int ValueWidth = 100;

        private static Label AddReadout(Control parent, string caption, int y)
        {
            parent.Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(LabelX, y),
                Text = caption
            });

            Label value = new Label
            {
                AutoSize = false,
                Location = new Point(ValueX, y),
                Size = new Size(ValueWidth, 16),
                Text = "0"
            };
            parent.Controls.Add(value);
            return value;
        }

        private void LoadFromSettings()
        {
            // Handlers attached after the initial fill so restoring the current state
            // does not itself count as an operator action.
            _direct.CheckedChanged -= Direct_CheckedChanged;
            _direct.Checked = ChamberPid.DirectMode;
            _direct.CheckedChanged += Direct_CheckedChanged;

            _directValue.ValueChanged -= DirectValue_ValueChanged;
            _directValue.Value = Clamp(_directValue, (decimal)ChamberPid.DirectValue);
            _directValue.ValueChanged += DirectValue_ValueChanged;
        }

        private static decimal Clamp(NumericUpDown numeric, decimal value)
        {
            return Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, value));
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowPlacement();
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            IniWriter.INI.Write(IniSection, "X", Location.X.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(IniSection, "Y", Location.Y.ToString(CultureInfo.InvariantCulture));
            base.OnFormClosing(e);
        }

        private void RestoreWindowPlacement()
        {
            if (IniWriter.INI.KeyExists("X", IniSection) && IniWriter.INI.KeyExists("Y", IniSection))
                Location = new Point(
                    int.Parse(IniWriter.INI.ReadINI(IniSection, "X")),
                    int.Parse(IniWriter.INI.ReadINI(IniSection, "Y")));

            if (Location.X < 0 || Location.Y < 0)
                Location = new Point(0, 0);
        }

        // No SET button here: the direct-mode switch and its voltage take effect
        // the moment they change, as they did before the redesign.
        private void Direct_CheckedChanged(object sender, EventArgs e)
        {
            ChamberPid.DirectMode = _direct.Checked;
            ChamberPid.Committed = true;
        }

        private void DirectValue_ValueChanged(object sender, EventArgs e)
        {
            ChamberPid.DirectValue = (double)_directValue.Value;
            ChamberPid.Committed = true;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            PLC210PidClient.State state = PLC210PidClient.GetState();

            _error.Text = state.Chamber.Error.ToString("F2", CultureInfo.InvariantCulture);
            _p.Text = state.Chamber.P.ToString("F2", CultureInfo.InvariantCulture);
            _i.Text = state.Chamber.I.ToString("F2", CultureInfo.InvariantCulture);
            _d.Text = state.Chamber.D.ToString("F2", CultureInfo.InvariantCulture);
            _output.Text = state.Chamber.Output.ToString("F2", CultureInfo.InvariantCulture);
        }
    }
}
