using ArdisCVDCore.modules_hw;
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
    public partial class ProcessParametersForm : Form
    {
        private const string IniSection = "ProcessParameters";

        private CheckBox[] _paramAlarmEnable;
        private CheckBox[] _paramAbortEnable;
        private NumericUpDown[] _paramAlarmVal;
        private NumericUpDown[] _paramAbortVal;

        private CheckBox[] _waterAlarmEnable;
        private CheckBox[] _waterAbortEnable;
        private NumericUpDown[] _waterTarget;
        private NumericUpDown[] _waterAlarmVal;
        private NumericUpDown[] _waterAbortVal;

        private CheckBox[] _inputEnable;
        private ComboBox[] _inputReaction;

        private const int ApplyBlinkIntervalMs = 250;
        private const int ApplyBlinkToggles = 8;

        private readonly Timer _applyBlinkTimer = new Timer();
        private int _applyBlinkLeft;

        public ProcessParametersForm()
        {
            InitializeComponent();
            Icon = Res.AppIcon;
            StartPosition = FormStartPosition.Manual;
            BindAlarmControls();

            _applyBlinkTimer.Interval = ApplyBlinkIntervalMs;
            _applyBlinkTimer.Tick += ApplyBlinkTimer_Tick;
            Disposed += (s, e) => _applyBlinkTimer.Dispose();
        }

        private void ProcessParametersForm_Load(object sender, EventArgs e)
        {
            RestoreWindowPlacement();
            DisableGroupsWithNoBackend();
            HideRowsWithNoHardware();
            LoadChamberPid();
            LoadAlarms();
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

        private void BindAlarmControls()
        {
            _paramAlarmEnable = new[]
            {
                Chamber_AlarmEnable, H2_AlarmEnable, CH4_AlarmEnable, N2_AlarmEnable,
                O2_AlarmEnable, AR_AlarmEnable, H22_AlarmEnable, MWRef_AlarmEnable
            };

            _paramAbortEnable = new[]
            {
                Chamber_AbortEnable, H2_AbortEnable, CH4_AbortEnable, N2_AbortEnable,
                O2_AbortEnable, AR_AbortEnable, H22_AbortEnable, MWRef_AbortEnable
            };

            _paramAlarmVal = new[]
            {
                ChamberPressure_AlarmVal, H2_AlarmVal, CH4_AlarmVal, N2_AlarmVal,
                O2_AlarmVal, AR_AlarmVal, H22_AlarmVal, MWRef_AlarmVal
            };

            _paramAbortVal = new[]
            {
                ChamberPressure_AbortVal, H2_AbortVal, CH4_AbortVal, N2_AbortVal,
                O2_AbortVal, AR_AbortVal, H22_AbortVal, MWRef_AbortVal
            };

            _waterAlarmEnable = new[]
            {
                TInternalH2O_AlarmEnable, TExternalH2O_AlarmEnable,
                TStageH2O_AlarmEnable, TChamberH2O_AlarmEnable
            };

            _waterAbortEnable = new[]
            {
                TInternalH2O_AbortEnable, TExternalH2O_AbortEnable,
                TStageH2O_AbortEnable, TChamberH2O_AbortEnable
            };

            _waterTarget = new[] { TInternalH2O, TExternalH2O, TStageH2O, TChamberH2O };

            _waterAlarmVal = new[]
            {
                TInternalH2O_AlarmVal, TExternalH2O_AlarmVal,
                TStageH2O_AlarmVal, TChamberH2O_AlarmVal
            };

            _waterAbortVal = new[]
            {
                TInternalH2O_AbortVal, TExternalH2O_AbortVal,
                TStageH2O_AbortVal, TChamberH2O_AbortVal
            };

            _inputEnable = new[]
            {
                StageFlow_CheckBox, ChamberFlow_CheckBox, MWHeadFlow_CheckBox,
                MWPowerFlow_CheckBox, InternalFlow_CheckBox, ExternalFlow_CheckBox,
                PressureSwitch_CheckBox, InputChamberOpen_CheckBox
            };

            _inputReaction = new[]
            {
                StageFlow_comboBox, ChamberFlow_comboBox, MWHeadFlow_comboBox,
                MWPowerFlow_comboBox, InternalFlow_comboBox, ExternalFlow_comboBox,
                PressureSwitch_comboBox, ChamberOpen_comboBox
            };

            foreach (ComboBox combo in _inputReaction)
                combo.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        /// <summary>
        /// Greys out every group whose values have nowhere to go, and says why in
        /// the group's own caption so it is obvious on screen rather than only in
        /// this file.
        /// </summary>
        private void DisableGroupsWithNoBackend()
        {
            MarkNotImplemented(groupBox1);
        }

        private static void MarkNotImplemented(GroupBox box)
        {
            box.Enabled = false;
            box.Text = box.Text.TrimEnd() + "  — not implemented yet";
        }

        private void HideRowsWithNoHardware()
        {
            PlasmaDrop_AlarmEnable.Visible = false;
            PlasmaDrop_AlarmVal.Visible = false;
            label88.Visible = false;
            label87.Visible = false;
            label90.Visible = false;
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

        // --- Alarm and abort --------------------------------------------------
        private void LoadAlarms()
        {
            for (int i = 0; i < AlarmSettings.ParamCount; i++)
            {
                _paramAlarmEnable[i].Checked = AlarmSettings.ParamAlarmEnable[i];
                _paramAbortEnable[i].Checked = AlarmSettings.ParamAbortEnable[i];
            }

            for (int i = 0; i < AlarmSettings.ParamPctCount; i++)
            {
                _paramAlarmVal[i].Value = Clamp(_paramAlarmVal[i], AlarmSettings.ParamAlarmPct[i]);
                _paramAbortVal[i].Value = Clamp(_paramAbortVal[i], AlarmSettings.ParamAbortPct[i]);
            }

            _paramAlarmVal[AlarmSettings.ParamReflected].Value =
                Clamp(_paramAlarmVal[AlarmSettings.ParamReflected], AlarmSettings.ReflectedAlarmWatt);
            _paramAbortVal[AlarmSettings.ParamReflected].Value =
                Clamp(_paramAbortVal[AlarmSettings.ParamReflected], AlarmSettings.ReflectedAbortWatt);

            TCenter_AlarmEnable.Checked = AlarmSettings.SampleAlarmEnable;
            TSampleCenter.Value = Clamp(TSampleCenter, AlarmSettings.SampleTargetC);
            TSample_AlarmVal.Value = Clamp(TSample_AlarmVal, AlarmSettings.SampleAlarmPct);

            for (int i = 0; i < AlarmSettings.WaterCount; i++)
            {
                _waterAlarmEnable[i].Checked = AlarmSettings.WaterAlarmEnable[i];
                _waterAbortEnable[i].Checked = AlarmSettings.WaterAbortEnable[i];
                _waterTarget[i].Value = Clamp(_waterTarget[i], AlarmSettings.WaterTargetC[i]);
                _waterAlarmVal[i].Value = Clamp(_waterAlarmVal[i], AlarmSettings.WaterAlarmPct[i]);
                _waterAbortVal[i].Value = Clamp(_waterAbortVal[i], AlarmSettings.WaterAbortPct[i]);
            }

            for (int i = 0; i < AlarmSettings.InputCount; i++)
            {
                _inputEnable[i].Checked = AlarmSettings.InputEnable[i];
                _inputReaction[i].SelectedIndex = AlarmSettings.InputAborts[i] ? 1 : 0;
            }
        }

        private void StoreAlarms()
        {
            for (int i = 0; i < AlarmSettings.ParamCount; i++)
            {
                AlarmSettings.ParamAlarmEnable[i] = _paramAlarmEnable[i].Checked;
                AlarmSettings.ParamAbortEnable[i] = _paramAbortEnable[i].Checked;
            }

            for (int i = 0; i < AlarmSettings.ParamPctCount; i++)
            {
                AlarmSettings.ParamAlarmPct[i] = (int)_paramAlarmVal[i].Value;
                AlarmSettings.ParamAbortPct[i] = (int)_paramAbortVal[i].Value;
            }

            AlarmSettings.ReflectedAlarmWatt = (int)_paramAlarmVal[AlarmSettings.ParamReflected].Value;
            AlarmSettings.ReflectedAbortWatt = (int)_paramAbortVal[AlarmSettings.ParamReflected].Value;

            AlarmSettings.SampleAlarmEnable = TCenter_AlarmEnable.Checked;
            AlarmSettings.SampleTargetC = (int)TSampleCenter.Value;
            AlarmSettings.SampleAlarmPct = (int)TSample_AlarmVal.Value;

            for (int i = 0; i < AlarmSettings.WaterCount; i++)
            {
                AlarmSettings.WaterAlarmEnable[i] = _waterAlarmEnable[i].Checked;
                AlarmSettings.WaterAbortEnable[i] = _waterAbortEnable[i].Checked;
                AlarmSettings.WaterTargetC[i] = (int)_waterTarget[i].Value;
                AlarmSettings.WaterAlarmPct[i] = (int)_waterAlarmVal[i].Value;
                AlarmSettings.WaterAbortPct[i] = (int)_waterAbortVal[i].Value;
            }

            for (int i = 0; i < AlarmSettings.InputCount; i++)
            {
                AlarmSettings.InputEnable[i] = _inputEnable[i].Checked;
                AlarmSettings.InputAborts[i] = _inputReaction[i].SelectedIndex == 1;
            }
        }

        private void OK_Click(object sender, EventArgs e)
        {
            ChamberPid.Kp = (double)Chamber_pid_P.Value;
            ChamberPid.Ki = (double)Chamber_pid_I.Value;
            ChamberPid.Kd = (double)Chamber_pid_D.Value;
            ChamberPid.UpperLimit = (double)Chamber_UpperLimit.Value;
            ChamberPid.LowerLimit = (double)Chamber_LowerLimit.Value;
            ChamberPid.Committed = true;

            StoreAlarms();
            AlarmSettings.Save();
            PLC210AlarmClient.PushThresholds(AlarmSettings.Pack());

            StartApplyBlink();

            // The button says Apply, so it applies and stays open -- the same as
            // the window it came from, and it lets the operator watch the effect
            // in PID Viewer before closing.
        }

        private void StartApplyBlink()
        {
            _applyBlinkLeft = ApplyBlinkToggles;
            OK.BackColor = Color.LightGreen;
            _applyBlinkTimer.Stop();
            _applyBlinkTimer.Start();
        }

        private void ApplyBlinkTimer_Tick(object sender, EventArgs e)
        {
            _applyBlinkLeft--;
            if (_applyBlinkLeft <= 0)
            {
                _applyBlinkTimer.Stop();
                OK.BackColor = SystemColors.Window;
                return;
            }
            OK.BackColor = _applyBlinkLeft % 2 == 0 ? Color.LightGreen : SystemColors.Window;
        }

        /// <summary>
        /// Puts the factory gains back in the fields without applying them --
        /// nothing reaches the PLC until Apply is pressed, so a misclick here
        /// cannot disturb a running process.
        /// </summary>
        private void Reset_Click(object sender, EventArgs e)
        {
            DialogResult answer = MessageBox.Show(this,
                "This puts every field on this screen back to its default and clears every alarm and abort check.\n\n"
                + "Nothing reaches the PLC until Apply is pressed.\n\nReset the values?",
                "Process Parameters",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
                return;

            Chamber_pid_P.Value = Clamp(Chamber_pid_P, (decimal)ChamberPid.DefaultKp);
            Chamber_pid_I.Value = Clamp(Chamber_pid_I, (decimal)ChamberPid.DefaultKi);
            Chamber_pid_D.Value = Clamp(Chamber_pid_D, (decimal)ChamberPid.DefaultKd);
            Chamber_UpperLimit.Value = Clamp(Chamber_UpperLimit, (decimal)ChamberPid.DefaultUpperLimit);
            Chamber_LowerLimit.Value = Clamp(Chamber_LowerLimit, (decimal)ChamberPid.DefaultLowerLimit);

            for (int i = 0; i < AlarmSettings.ParamCount; i++)
            {
                _paramAlarmEnable[i].Checked = false;
                _paramAbortEnable[i].Checked = false;
                _paramAlarmVal[i].Value = _paramAlarmVal[i].Minimum;
                _paramAbortVal[i].Value = _paramAbortVal[i].Minimum;
            }

            TCenter_AlarmEnable.Checked = false;
            TSampleCenter.Value = Clamp(TSampleCenter, AlarmSettings.DefaultSampleTargetC);
            TSample_AlarmVal.Value = TSample_AlarmVal.Minimum;

            for (int i = 0; i < AlarmSettings.WaterCount; i++)
            {
                _waterAlarmEnable[i].Checked = false;
                _waterAbortEnable[i].Checked = false;
                _waterTarget[i].Value = _waterTarget[i].Minimum;
                _waterAlarmVal[i].Value = _waterAlarmVal[i].Minimum;
                _waterAbortVal[i].Value = _waterAbortVal[i].Minimum;
            }

            for (int i = 0; i < AlarmSettings.InputCount; i++)
            {
                _inputEnable[i].Checked = false;
                _inputReaction[i].SelectedIndex = 0;
            }
        }

        // The design wires these, but in the window it came from they only fed the
        // interpolated-PID quadrants selected by a DomainUpDown and an "Interp
        // PID" checkbox -- neither of which exists in this layout. Apply reads the
        // three spinners directly, so there is nothing for them to do.
        private void Chamber_pid_P_ValueChanged(object sender, EventArgs e) { }

        private void Chamber_pid_I_ValueChanged(object sender, EventArgs e) { }

        private void Chamber_pid_D_ValueChanged(object sender, EventArgs e) { }

        private void PressureSwitch_comboBox_SelectedIndexChanged(object sender, EventArgs e) { }
    }
}
