using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    /// <summary>
    /// Settings -&gt; Process Parameters: the chamber PID gains and limits, plus
    /// the alarm/abort thresholds and gas correction factors the previous machine
    /// carried.
    /// </summary>
    /// <remarks>
    /// Only the Chamber PID group has anywhere to write today -- it edits the
    /// same <see cref="ChamberPid"/> values as View -&gt; PID Viewer, and MainForm
    /// pushes them to the PLC once a second.
    ///
    /// The other four groups are switched off on purpose, see
    /// <see cref="DisableGroupsWithNoBackend"/>. They belong to machinery this
    /// application does not have yet: an alarm/abort engine that compares every
    /// signal against a percentage window and either warns or trips the
    /// microwave, gas correction factors applied to the РРГ setpoints, and the
    /// logic inputs for the cooling flow and pressure switches. Leaving them
    /// clickable would be worse than leaving them out -- an operator would enter
    /// an abort threshold, press Apply, and believe the reactor was protected.
    /// </remarks>
    public partial class ProcessParametersForm : Form
    {
        private const string IniSection = "ProcessParameters";

        public ProcessParametersForm()
        {
            InitializeComponent();
            Icon = Res.AppIcon;
            StartPosition = FormStartPosition.Manual;
        }

        private void ProcessParametersForm_Load(object sender, EventArgs e)
        {
            RestoreWindowPlacement();
            DisableGroupsWithNoBackend();
            LoadChamberPid();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
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

        /// <summary>
        /// Greys out every group whose values have nowhere to go, and says why in
        /// the group's own caption so it is obvious on screen rather than only in
        /// this file.
        /// </summary>
        private void DisableGroupsWithNoBackend()
        {
            MarkNotImplemented(ParametersGroup);
            MarkNotImplemented(TemperatureGroupBox);
            MarkNotImplemented(LogicInputGroupBox);
            MarkNotImplemented(groupBox1);
        }

        private static void MarkNotImplemented(GroupBox box)
        {
            box.Enabled = false;
            box.Text = box.Text.TrimEnd() + "  — not implemented yet";
        }

        // --- Chamber PID ------------------------------------------------------
        private void LoadChamberPid()
        {
            Chamber_pid_P.Value = Clamp(Chamber_pid_P, (decimal)ChamberPid.Kp);
            Chamber_pid_I.Value = Clamp(Chamber_pid_I, (decimal)ChamberPid.Ki);
            Chamber_pid_D.Value = Clamp(Chamber_pid_D, (decimal)ChamberPid.Kd);
            Chamber_UpperLimit.Value = Clamp(Chamber_UpperLimit, (decimal)ChamberPid.UpperLimit);
            Chamber_LowerLimit.Value = Clamp(Chamber_LowerLimit, (decimal)ChamberPid.LowerLimit);
        }

        private static decimal Clamp(NumericUpDown numeric, decimal value)
        {
            return Math.Max(numeric.Minimum, Math.Min(numeric.Maximum, value));
        }

        private void OK_Click(object sender, EventArgs e)
        {
            ChamberPid.Kp = (double)Chamber_pid_P.Value;
            ChamberPid.Ki = (double)Chamber_pid_I.Value;
            ChamberPid.Kd = (double)Chamber_pid_D.Value;
            ChamberPid.UpperLimit = (double)Chamber_UpperLimit.Value;
            ChamberPid.LowerLimit = (double)Chamber_LowerLimit.Value;
            ChamberPid.Committed = true;

            // The button says Apply, so it applies and stays open -- the same as
            // the window it came from, and it lets the operator watch the effect
            // in PID Viewer before closing.
        }

        /// <summary>
        /// Puts the factory gains back in the fields without applying them --
        /// nothing reaches the PLC until Apply is pressed, so a misclick here
        /// cannot disturb a running process.
        /// </summary>
        private void Reset_Click(object sender, EventArgs e)
        {
            Chamber_pid_P.Value = Clamp(Chamber_pid_P, (decimal)ChamberPid.DefaultKp);
            Chamber_pid_I.Value = Clamp(Chamber_pid_I, (decimal)ChamberPid.DefaultKi);
            Chamber_pid_D.Value = Clamp(Chamber_pid_D, (decimal)ChamberPid.DefaultKd);
            Chamber_UpperLimit.Value = Clamp(Chamber_UpperLimit, (decimal)ChamberPid.DefaultUpperLimit);
            Chamber_LowerLimit.Value = Clamp(Chamber_LowerLimit, (decimal)ChamberPid.DefaultLowerLimit);
        }

        // The design wires these, but in the window it came from they only fed the
        // interpolated-PID quadrants selected by a DomainUpDown and an "Interp
        // PID" checkbox -- neither of which exists in this layout. Apply reads the
        // three spinners directly, so there is nothing for them to do.
        private void Chamber_pid_P_ValueChanged(object sender, EventArgs e) { }

        private void Chamber_pid_I_ValueChanged(object sender, EventArgs e) { }

        private void Chamber_pid_D_ValueChanged(object sender, EventArgs e) { }

        // Part of the Inputs group, which is disabled above.
        private void PressureSwitch_comboBox_SelectedIndexChanged(object sender, EventArgs e) { }
    }
}
