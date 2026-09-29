using ArdisCVDCore.modules_hw;
using ArdisCVDCore.trends;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public partial class MainForm : Form
    {
        private const int PreheatSeconds = 150;

        private NumericUpDown[] _gasSetpoint;
        private TextBox[] _gasMeasured;

        private PictureBox[] _gasValveBox;

        private TextBox[] _coolingTemp;
        private TextBox[] _coolingFlow;

        private PictureBox[] _vacuumValveBox;

        private PLC210MicrowaveClient.State _microwaveState = new PLC210MicrowaveClient.State
        {
            StatusText = "PLC210 microwave generator disabled"
        };

        private readonly Stopwatch _stopwatch = new Stopwatch();

        private bool _manualRunActive;
        private DateTime _manualRunStart;

        private const string IdleTime = "00:00:00";
        private const string IdleDuration = "00:00:00:00";

        private const int MicrowaveAutoResetDelayTicks = 5;
        private int _microwaveAutoResetTicks;
        private bool _waterFaultWarned;

        private int _turboBarSpeedHz;
        private bool _turboBarAtSpeed;

        private static readonly Font PreheatBarFont = new Font("Microsoft Sans Serif", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        private int _preheatBarShownSeconds = PreheatSeconds;
        private int _preheatBarFilledSeconds;

        private GasTrendForm _gasTrendForm;
        private PressureTrendForm _pressureTrendForm;
        private MWPowerTrendForm _mwPowerTrendForm;
        private TemperatureTrendForm _temperatureTrendForm;
        private PidViewerForm _pidViewerForm;
        private StatusForm _statusForm;
        private FaultStatusForm _faultStatusForm;
        private bool _abortHandled;
        private ProcessParametersForm _processParametersForm;

        public MainForm()
        {
            InitializeComponent();
            BindChannels();
            BindMenuIcons();
            SetLoggingMenu(Logger.Enabled);
            ProcessLogger.Init();
        }

        private void BindChannels()
        {
            _gasSetpoint = new NumericUpDown[] { H2_Set, CH4_Set, N2_Set, O2_Set, AR_Set, H22_Set };
            _gasMeasured = new TextBox[] { H2_FlowRate, CH4_FlowRate, N2_FlowRate, O2_FlowRate, AR_FlowRate, H22_FlowRate };

            for (int i = 0; i < _gasSetpoint.Length; i++)
            {
                _gasSetpoint[i].Minimum = 0;
                _gasSetpoint[i].Maximum = (decimal)PLC210GasFlowClient.FullScaleSccm[i];
            }

            H22_Set.Increment = H2_Set.Increment;

            _coolingTemp = new[] { StageTemp, ChamberTemp, MWHeadTemp, MWPowerTemp, TunerTemp, InternalTemp, ExternalTemp };
            _coolingFlow = new[] { StageFlow, ChamberFlow, MWHeadFlow, MWPowerFlow, TunerFlow, InternalFlow, ExternalFlow };

            _gasValveBox = new[] { Valve_1, Valve_2, Valve_3, Valve_4, Valve_5, Valve_6, Valve_7, Valve_8 };
            _vacuumValveBox = new[] { Valve_17, Valve_18, null, Valve_20, Valve_21, Valve_22, Valve_24, null };

            MWPowerSetPoint.Minimum = (decimal)PLC210MicrowaveClient.MinSetpointKw;
            MWPowerSetPoint.Maximum = (decimal)PLC210MicrowaveClient.MaxSetpointKw;

            ApplyManualRunGate();
        }

        private void BindMenuIcons()
        {
            const int size = 20;
            fileToolStripMenuItem.DropDown.ImageScalingSize = new Size(size, size);

            ConnectToolMenu.Image = Res.Glyph('\uE839', SystemColors.ControlText, size);
            DisconnectToolMenu.Image = Res.Glyph('\uEB55', SystemColors.ControlText, size);
            StartLoggingToolMenu.Image = Res.Glyph('\uE7C8', Color.Red, size);
            StopLoggingToolMenu.Image = Res.Glyph('\uE71A', SystemColors.ControlText, size);
            ExitToolMenu.Image = Res.Glyph('\uF3B1', SystemColors.ControlText, size);

            settingsToolStripMenuItem.DropDown.ImageScalingSize = new Size(size, size);
            ProcessParametersToolMenu.Image = Res.Glyph('\uE9E9', SystemColors.ControlText, size);

            viewToolStripMenuItem.DropDown.ImageScalingSize = new Size(size, size);
            FaultStatusToolMenu.Image = Res.Glyph('\uE7BA', SystemColors.ControlText, size);
            ConnectionStatusToolMenu.Image = Res.Glyph('\uE968', SystemColors.ControlText, size);
            GasTrendToolMenu.Image = Res.Glyph('\uE9D2', SystemColors.ControlText, size);
            PressureTrendToolMenu.Image = Res.Glyph('\uEC4A', SystemColors.ControlText, size);
            MWPowerToolStripMenuItem.Image = Res.Glyph('\uE945', SystemColors.ControlText, size);
            temperatureTrendToolStripMenuItem.Image = Res.Glyph('\uE9CA', SystemColors.ControlText, size);
            PIDToolStripMenuItem.Image = Res.Glyph('\uE9D9', SystemColors.ControlText, size);
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            RestoreWindowPlacement();
            StartPlcClients();
            SuperCycle.Start();
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmExit())
            {
                e.Cancel = true;
                return;
            }

            CloseOtherWindows();

            SuperCycle.Stop();

            ProcessLogger.OpTimeLogging(false, "MWPower");
            ProcessLogger.LogBinary();
            Task write = ProcessLogger.CreateLogFileEnded();
            if (write != null)
                write.Wait(5000);

            StopPlcClients();

            Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            IniWriter.INI.Write("MainForm", "X", bounds.X.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write("MainForm", "Y", bounds.Y.ToString(CultureInfo.InvariantCulture));
        }

        private void CloseOtherWindows()
        {
            List<Form> open = new List<Form>();
            foreach (Form form in Application.OpenForms)
                if (form != this)
                    open.Add(form);

            foreach (Form form in open)
                if (!form.IsDisposed)
                    form.Close();
        }

        private bool ConfirmExit()
        {
            string blocker = DescribeRunningEquipment();
            if (blocker != null)
                return MessageBox.Show(
                    this,
                    "Something is still switched on:\r\n\r\n" + blocker
                        + "\r\n\r\nClosing this window leaves it running with "
                        + "nothing watching it. Exit anyway?",
                    "Ardis CVDCore",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) == DialogResult.Yes;

            return MessageBox.Show(
                this,
                "Are you sure you want to exit?",
                "Ardis CVDCore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void RestoreWindowPlacement()
        {
            if (IniWriter.INI.KeyExists("X", "MainForm") && IniWriter.INI.KeyExists("Y", "MainForm"))
                Location = new Point(
                    int.Parse(IniWriter.INI.ReadINI("MainForm", "X")),
                    int.Parse(IniWriter.INI.ReadINI("MainForm", "Y")));

            if (Location.X < 0 || Location.Y < 0)
                Location = new Point(0, 0);
        }

        private void StartPlcClients()
        {
            string host = ReadIniString("PLC210", "IP", "192.168.1.10");
            int port = ReadIniInt("PLC210", "Port", 502);

            PLC210PidClient.SetLidInputChannel(
                ReadIniInt("PLC210", "LidInput", PLC210PidClient.DefaultLidInputChannel));

            PLC210PidClient.Start(host, port);
            PLC210ThyracontClient.Start(host, port);
            PLC210GasFlowClient.Start(host, port);
            PLC210PyrometerClient.Start(host, port);
            PLC210MicrowaveClient.Start(host, port);
            PLC210GasValveClient.Start(host, port);
            PLC210VacuumClient.Start(host, port);
            PLC210CoolingClient.Start(host, port);
            PLC210TurboPumpClient.Start(host, port);
            PLC210AlarmClient.Start(host, port);

            AlarmSettings.Load();
            AlarmSettings.LidInput = PLC210PidClient.LidInputChannel;
            PLC210AlarmClient.PushThresholds(AlarmSettings.Pack());

            PLC210PidClient.SetGasSubsystemEnabled(true);
        }

        private static void StopPlcClients()
        {
            Parallel.Invoke(
                PLC210PidClient.Stop,
                PLC210ThyracontClient.Stop,
                PLC210GasFlowClient.Stop,
                PLC210PyrometerClient.Stop,
                PLC210MicrowaveClient.Stop,
                PLC210GasValveClient.Stop,
                PLC210VacuumClient.Stop,
                PLC210CoolingClient.Stop,
                PLC210TurboPumpClient.Stop,
                PLC210AlarmClient.Stop);
        }

        private void SetPlcLinkMenu(bool connected)
        {
            ConnectToolMenu.Enabled = !connected;
            DisconnectToolMenu.Enabled = connected;
        }

        private static string ReadIniString(string section, string key, string defaultValue)
        {
            if (!IniWriter.INI.KeyExists(key, section))
                return defaultValue;

            string value = IniWriter.INI.ReadINI(section, key);
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static int ReadIniInt(string section, string key, int defaultValue)
        {
            int value;
            return int.TryParse(ReadIniString(section, key, defaultValue.ToString(CultureInfo.InvariantCulture)), out value) &&
                value > 0 && value <= 65535
                ? value
                : defaultValue;
        }

        private void SuperCycle_Tick(object sender, EventArgs e)
        {
            UpdateGasSection();
            UpdateValves();
            UpdateChamber();
            UpdateMicrowaveSection();
            UpdatePumps();
            UpdateTurboPump();
            UpdateCoolingSection();
            UpdateAlarms();
            UpdateStatusPlate();

            if (ChamberPid.Committed)
                PushChamberChannel();

            if (_manualRunActive && Logger.Enabled)
                ProcessLogger.Record(ProcessSample.Capture(DateTime.Now));
        }

        private void PushChamberChannel()
        {
            PLC210PidClient.State state = PLC210PidClient.GetState();
            double measured = state.PlcPressureAvailable ? Math.Max(0, state.PlcPressureTorr) : 0;

            PLC210PidClient.Channel chamber = new PLC210PidClient.Channel
            {
                Enabled = true,
                Setpoint = ChamberPid.Setpoint,
                Measured = measured,
                Kp = ChamberPid.Kp,
                Ki = ChamberPid.Ki,
                Kd = ChamberPid.Kd,
                SmartMode = ChamberPid.SmartMode,
                SmartKp = ChamberPid.SmartKp,
                LowerLimit = ChamberPid.DirectMode ? 0 : ChamberPid.LowerLimit,
                UpperLimit = ChamberPid.DirectMode ? 5000 : ChamberPid.UpperLimit,
                DirectMode = ChamberPid.DirectMode,
                DirectValue = Math.Max(0, Math.Min(5000, ChamberPid.DirectValue))
            };

            PLC210PidClient.Channel plenumDisabled = new PLC210PidClient.Channel
            {
                Enabled = false
            };

            PLC210PidClient.SetChannels(chamber, plenumDisabled, reset: false);
        }

        private void UpdateGasSection()
        {
            PLC210GasFlowClient.State state = PLC210GasFlowClient.GetState();

            for (int i = 0; i < _gasMeasured.Length; i++)
            {
                PLC210GasFlowClient.ChannelState channel = state.Channels[i];

                _gasMeasured[i].Text = state.Connected && !channel.SlaveError
                    ? channel.MeasuredSccm.ToString(
                        _gasSetpoint[i].DecimalPlaces == 0 ? "F0" : "F2", CultureInfo.InvariantCulture)
                    : "---";
            }
        }

        private void GasesSet_Click(object sender, EventArgs e)
        {
            int index = GasChannelOf(sender);
            if (index >= 0)
                PLC210GasFlowClient.RequestSetpoint(index, (double)_gasSetpoint[index].Value);
        }

        private int GasChannelOf(object setButton)
        {
            if (ReferenceEquals(setButton, H2_SetVal)) return 0;
            if (ReferenceEquals(setButton, CH4_SetVal)) return 1;
            if (ReferenceEquals(setButton, N2_SetVal)) return 2;
            if (ReferenceEquals(setButton, O2_SetVal)) return 3;
            if (ReferenceEquals(setButton, Ar_SetVal)) return 4;
            if (ReferenceEquals(setButton, H22_SetVal)) return 5;
            return -1;
        }

        private void Valves_Click(object sender, EventArgs e)
        {
            PictureBox box = sender as PictureBox;
            if (box == null)
                return;

            bool wanted = !IsShownOpen(box);

            int index = Array.IndexOf(_gasValveBox, box);
            if (index >= 0)
            {
                PLC210GasValveClient.RequestValve(index, wanted);
                return;
            }

            index = Array.IndexOf(_vacuumValveBox, box);
            if (index < 0)
                return;

            if (wanted)
            {
                string blocker = DescribeVacuumValveBlocker(index);
                if (blocker != null)
                {
                    MessageBox.Show(
                        this,
                        blocker,
                        "Vacuum valves",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            PLC210VacuumClient.RequestValve(index, wanted);
        }

        private const int Vpv4Index = 3;
        private const int Vpv5Index = 4;

        private string DescribeVacuumValveBlocker(int index)
        {
            int other;
            if (index == Vpv4Index)
                other = Vpv5Index;
            else if (index == Vpv5Index)
                other = Vpv4Index;
            else
                return null;

            string name = VacuumValveName(index);
            string otherName = VacuumValveName(other);

            if (IsShownOpen(_vacuumValveBox[other]))
                return name + " cannot be opened while " + otherName + " is open.\r\n\r\n"
                    + "Close " + otherName + " first.";

            if (PLC210VacuumClient.IsValveRequested(other))
                return name + " cannot be opened while " + otherName + " is opening.\r\n\r\n"
                    + "The PLC has not confirmed " + otherName + " yet. If it stays closed, press Close All Valves.";

            return null;
        }

        private static string VacuumValveName(int index)
        {
            return "VPV" + (index + 1).ToString(CultureInfo.InvariantCulture);
        }

        private void CloseAllValves_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < PLC210GasValveClient.ValveCount; i++)
                PLC210GasValveClient.RequestValve(i, false);

            for (int i = 0; i < PLC210VacuumClient.ValveCount; i++)
                PLC210VacuumClient.RequestValve(i, false);
        }

        private void UpdateValves()
        {
            PLC210GasValveClient.State gasValves = PLC210GasValveClient.GetState();
            for (int i = 0; i < _gasValveBox.Length; i++)
                ShowValve(_gasValveBox[i], gasValves.Connected && gasValves.ValveOn[i]);

            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            for (int i = 0; i < _vacuumValveBox.Length; i++)
                if (_vacuumValveBox[i] != null)
                    ShowValve(_vacuumValveBox[i], vacuum.Connected && vacuum.ValveOn[i]);
        }

        private static bool IsShownOpen(PictureBox box)
        {
            return ReferenceEquals(box.BackgroundImage, Res.ValveOpen);
        }

        private static void ShowValve(PictureBox box, bool open)
        {
            Image wanted = open ? Res.ValveOpen : Res.ValveClosed;
            if (!ReferenceEquals(box.BackgroundImage, wanted))
                box.BackgroundImage = wanted;
        }

        private void UpdateChamber()
        {
            PLC210PidClient.State pid = PLC210PidClient.GetState();
            ChamberPressure_textbox.Text = pid.PlcPressureAvailable
                ? Math.Max(0, pid.PlcPressureTorr).ToString("F1", CultureInfo.InvariantCulture)
                : "---";

            PLC210ThyracontClient.State hiVac = PLC210ThyracontClient.GetState();
            HiVacPressure.Text = hiVac.HasValidValue
                ? hiVac.PressureTorr.ToString("0.###E-0", CultureInfo.InvariantCulture)
                : "---";
            HiVacPressureMbar.Text = hiVac.HasValidValue
                ? hiVac.PressureMbar.ToString("0.###E-0", CultureInfo.InvariantCulture)
                : "---";

            UpdatePyrometers();
        }

        private void UpdatePyrometers()
        {
            PLC210PyrometerClient.State state = PLC210PyrometerClient.GetState();
            PLC210PyrometerClient.PyrometerReading active = SelectActivePyrometer(state);

            if (active == null)
            {
                SampleTemp_ch1.Text = "---";
                SampleTemp_ch2.Text = "---";
                SampleTemp_sum.Text = "---";
                return;
            }

            double lowLimit;
            string lowLimitLabel;
            if (ReferenceEquals(active, state.Rxt))
            {
                lowLimit = PLC210PyrometerClient.RxtLowLimit;
                lowLimitLabel = PLC210PyrometerClient.RxtLowLimitLabel;
            }
            else
            {
                lowLimit = PLC210PyrometerClient.SmartLowLimit;
                lowLimitLabel = PLC210PyrometerClient.SmartLowLimitLabel;
            }

            bool ch1Low = active.Ch1Temp <= lowLimit;
            bool ch2Low = active.Ch2Temp <= lowLimit;

            SampleTemp_ch1.Text = ch1Low ? lowLimitLabel : active.Ch1Temp.ToString("F0", CultureInfo.InvariantCulture);
            SampleTemp_ch2.Text = ch2Low ? lowLimitLabel : active.Ch2Temp.ToString("F0", CultureInfo.InvariantCulture);
            SampleTemp_sum.Text = (ch1Low || ch2Low)
                ? lowLimitLabel
                : active.RatioTemp.ToString("F0", CultureInfo.InvariantCulture);
        }

        public static PLC210PyrometerClient.PyrometerReading SelectActivePyrometer(PLC210PyrometerClient.State state)
        {
            if (state.Rxt.Valid && !state.Rxt.CommFault)
                return state.Rxt;
            if (state.Smart.Valid && !state.Smart.CommFault)
                return state.Smart;
            return null;
        }

        private void ChamberPressure_SetVal_Click(object sender, EventArgs e)
        {
            ChamberPid.Setpoint = (double)ChamberPressureSetPoint.Value;
            ChamberPid.Committed = true;
            PushChamberChannel();
        }

        private void UpdateMicrowaveSection()
        {
            PLC210MicrowaveClient.State state = PLC210MicrowaveClient.GetState();
            _microwaveState = state;

            bool generatorAlive = state.GeneratorAnswering;

            IncMWPower.Text = generatorAlive
                ? state.IncidentKw.ToString("F2", CultureInfo.InvariantCulture)
                : "---";
            ReflMWPower.Text = generatorAlive
                ? state.ReflectedKw.ToString("F2", CultureInfo.InvariantCulture)
                : "---";

            MWReconnect.Visible = !generatorAlive;

            StartMW.BackColor = generatorAlive && state.PreheatOn ? Res.OnGreen : SystemColors.Control;
            button1.BackColor = generatorAlive && state.MicrowaveOn ? Res.OnGreen : SystemColors.Control;

            StartMW.Enabled = generatorAlive;
            button1.Enabled = generatorAlive && !state.FaultReportable && !state.ChamberPressureLow
                && (state.FilamentPreheatDone || state.MicrowaveOn);

            bool preheating = generatorAlive && state.PreheatOn && !state.FilamentPreheatDone;
            int remaining = Math.Max(0, PreheatSeconds - state.PreheatElapsedSeconds);

            int shownSeconds;
            int filledSeconds;
            if (preheating)
            {
                shownSeconds = remaining;
                filledSeconds = Math.Max(0, Math.Min(state.PreheatElapsedSeconds, PreheatSeconds));
            }
            else if (generatorAlive && state.FilamentPreheatDone)
            {
                shownSeconds = 0;
                filledSeconds = PreheatSeconds;
            }
            else
            {
                shownSeconds = PreheatSeconds;
                filledSeconds = 0;
            }

            PreheatBar.Visible = generatorAlive;
            if (shownSeconds != _preheatBarShownSeconds || filledSeconds != _preheatBarFilledSeconds)
            {
                _preheatBarShownSeconds = shownSeconds;
                _preheatBarFilledSeconds = filledSeconds;
                PreheatBar.Invalidate();
            }

            CheckMicrowaveWater(state);
        }

        private void CheckMicrowaveWater(PLC210MicrowaveClient.State state)
        {
            if (state.GeneratorAnswering && state.WaterFlowFault && !state.Idle)
            {
                if (_waterFaultWarned)
                    return;

                _waterFaultWarned = true;
                PLC210MicrowaveClient.LatchWaterFault();
                PLC210MicrowaveClient.RequestMicrowave(false);
                ProcessLogger.OpTimeLogging(false, "MWPower");
                PLC210MicrowaveClient.RequestPreheat(false);

                BeginInvoke(new MethodInvoker(delegate
                {
                    MessageBox.Show(
                        this,
                        "No cooling water at the microwave generator.",
                        "Microwave",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }));

                return;
            }

            if (state.Idle)
                _waterFaultWarned = false;
        }

        private void StartMW_Click(object sender, EventArgs e)
        {
            bool turnOn = !_microwaveState.PreheatOn;
            if (!turnOn && !ConfirmSwitchOff("Preheat"))
                return;

            PLC210MicrowaveClient.RequestPreheat(turnOn);
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            bool turnOn = !_microwaveState.MicrowaveOn;
            if (turnOn && !_microwaveState.FilamentPreheatDone)
                return;

            if (!turnOn && !ConfirmSwitchOff("Microwave"))
                return;

            PLC210MicrowaveClient.RequestMicrowave(turnOn);
            ProcessLogger.OpTimeLogging(turnOn, "MWPower");
        }

        private bool ConfirmSwitchOff(string equipment)
        {
            return MessageBox.Show(
                this,
                "Are you sure you want to switch " + equipment + " off?",
                "Ardis CVDCore",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void ResetMW_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestReset();
        }

        private void MWReconnect_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestGeneratorReconnect();
        }

        private void StopMW_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestMicrowave(false);
            ProcessLogger.OpTimeLogging(false, "MWPower");
            PLC210MicrowaveClient.RequestPreheat(false);
        }

        private void MWPowerSet_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestSetpoint((double)MWPowerSetPoint.Value);
        }

        private void UpdatePumps()
        {
            PLC210VacuumClient.State state = PLC210VacuumClient.GetState();

            ShowPump(ForeVacPump, state.Connected && _manualRunActive, state.ForeVacPumpOn);
            ShowPump(Water_Btn, state.Connected && _manualRunActive, state.WaterPumpOn);

            if (_microwaveAutoResetTicks > 0)
            {
                _microwaveAutoResetTicks--;
                if (_microwaveAutoResetTicks == 0 && state.Connected && state.WaterPumpOn)
                    ClearMicrowaveFaultOnWater();
            }
        }

        private static void ShowPump(Button button, bool operable, bool running)
        {
            button.Enabled = operable;
            button.Text = running ? "PUMP ON" : "PUMP OFF";
            button.BackColor = running ? Res.OnGreen : Color.LightSalmon;
        }

        private void ForeVacPump_Click(object sender, EventArgs e)
        {
            bool turnOn = !PLC210VacuumClient.GetState().ForeVacPumpOn;

            if (!turnOn)
            {
                PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
                if (turbo.Connected && turbo.DriveAnswering && (turbo.Working || turbo.SpeedHz > 0))
                {
                    MessageBox.Show(
                        this,
                        "Can't stop the forevacuum pump: the turbo pump is still turning at "
                            + turbo.SpeedHz.ToString(CultureInfo.InvariantCulture)
                            + " Hz.\r\n\r\nStop the turbo pump and let it spin down first.",
                        "Forevacuum pump",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }
            }

            PLC210VacuumClient.RequestForeVacPump(turnOn);
        }

        private const double TurboMaxChamberPressureTorr = 2.0;

        private const int TurboBackingValveIndex = 6;

        private void UpdateTurboPump()
        {
            PLC210TurboPumpClient.State state = PLC210TurboPumpClient.GetState();
            bool live = state.Connected && state.DriveAnswering;

            TurboVacPump.Enabled = live && _manualRunActive;
            if (live)
            {
                TurboVacPump.Text = state.Working ? "TURBO PUMP ON" : "TURBO PUMP OFF";
                TurboVacPump.BackColor = TurboButtonColor(state);
            }
            else
            {
                TurboVacPump.Text = "NOT CONNECTED";
                TurboVacPump.BackColor = Color.LightGray;
            }

            TurboSpeedValue.Text = live
                ? state.SpeedHz.ToString(CultureInfo.InvariantCulture)
                : "---";
            TurboTempValue.Text = live
                ? state.TemperatureC.ToString(CultureInfo.InvariantCulture)
                : "---";

            int speed = live ? state.SpeedHz : 0;
            bool atSpeed = live && state.AtNormalSpeed;
            if (speed != _turboBarSpeedHz || atSpeed != _turboBarAtSpeed)
            {
                _turboBarSpeedHz = speed;
                _turboBarAtSpeed = atSpeed;
                TurboSpeedBar.Invalidate();
            }
        }

        private static Color TurboButtonColor(PLC210TurboPumpClient.State state)
        {
            if (state.Connected && state.DriveAnswering && state.FaultActive)
                return Color.Red;

            if (!state.Working)
                return Color.LightSalmon;

            return state.AtNormalSpeed ? Res.OnGreen : Color.YellowGreen;
        }

        private void PreheatBar_Paint(object sender, PaintEventArgs e)
        {
            Rectangle area = PreheatBar.ClientRectangle;
            e.Graphics.Clear(PreheatBar.BackColor);

            int filled = area.Width * _preheatBarFilledSeconds / PreheatSeconds;
            if (filled > 0)
            {
                using (SolidBrush brush = new SolidBrush(Res.OnGreen))
                    e.Graphics.FillRectangle(brush, 0, 0, filled, area.Height);
            }

            Rectangle text = Rectangle.Inflate(area, -6, 0);
            TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
            TextRenderer.DrawText(e.Graphics, "Preheat Status", PreheatBarFont, text,
                SystemColors.ControlText, flags | TextFormatFlags.Left);
            TextRenderer.DrawText(e.Graphics, _preheatBarShownSeconds.ToString(CultureInfo.InvariantCulture) + "s",
                PreheatBarFont, text, SystemColors.ControlText, flags | TextFormatFlags.Right);
        }

        private void TurboSpeedBar_Paint(object sender, PaintEventArgs e)
        {
            e.Graphics.Clear(TurboSpeedBar.BackColor);

            if (_turboBarSpeedHz <= 0)
                return;

            double fraction = Math.Min(
                1.0, (double)_turboBarSpeedHz / PLC210TurboPumpClient.RatedSpeedHz);

            using (SolidBrush brush = new SolidBrush(_turboBarAtSpeed ? Res.OnGreen : Color.GreenYellow))
                e.Graphics.FillRectangle(
                    brush,
                    0,
                    0,
                    (float)(TurboSpeedBar.ClientSize.Width * fraction),
                    TurboSpeedBar.ClientSize.Height);
        }

        private void TurboVacPump_Click(object sender, EventArgs e)
        {
            PLC210TurboPumpClient.State state = PLC210TurboPumpClient.GetState();

            if (state.Working || state.RunLatched)
            {
                PLC210TurboPumpClient.RequestRun(false);
                return;
            }

            string blocker = DescribeTurboStartBlockers(state);
            if (blocker != null)
            {
                MessageBox.Show(
                    this,
                    "Can't start the turbo pump:\r\n\r\n" + blocker,
                    "Turbo pump",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            PLC210TurboPumpClient.RequestRun(true);
        }

        private static string DescribeTurboStartBlockers(PLC210TurboPumpClient.State turbo)
        {
            if (!turbo.Connected)
                return "the PLC is not answering";

            if (!turbo.DriveAnswering)
                return "the pump drive is not answering the PLC";

            string fault = turbo.FaultText;
            if (fault != null)
                return "the drive is reporting a fault: " + fault;

            List<string> blockers = new List<string>();

            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            if (!vacuum.Connected)
            {
                blockers.Add("the vacuum outputs are not answering, so neither VPV7 "
                    + "nor the forevacuum pump can be confirmed");
            }
            else
            {
                if (!vacuum.ValveOn[TurboBackingValveIndex])
                    blockers.Add("VPV7 is closed, so the pump has no backing line");
                if (!vacuum.ForeVacPumpOn)
                    blockers.Add("the forevacuum pump is off");
            }

            string limit = TurboMaxChamberPressureTorr.ToString("0.#", CultureInfo.InvariantCulture);

            PLC210PidClient.State pid = PLC210PidClient.GetState();
            if (!pid.Connected || !pid.PlcPressureAvailable)
            {
                blockers.Add("there is no chamber pressure reading to check against the "
                    + limit + " Torr limit");
            }
            else if (pid.PlcPressureTorr > TurboMaxChamberPressureTorr)
            {
                blockers.Add("the chamber is at "
                    + pid.PlcPressureTorr.ToString("F1", CultureInfo.InvariantCulture)
                    + " Torr, above the " + limit + " Torr limit");
            }

            return blockers.Count == 0 ? null : string.Join("\r\n", blockers.ToArray());
        }

        private void Water_Btn_Click(object sender, EventArgs e)
        {
            bool turnOn = !PLC210VacuumClient.GetState().WaterPumpOn;
            PLC210VacuumClient.RequestWaterPump(turnOn);
            _microwaveAutoResetTicks = turnOn ? MicrowaveAutoResetDelayTicks : 0;

            if (turnOn)
                PLC210MicrowaveClient.ClearWaterFaultLatch();
        }

        private static void ClearMicrowaveFaultOnWater()
        {
            PLC210MicrowaveClient.State microwave = PLC210MicrowaveClient.GetState();
            if (!microwave.GeneratorAnswering || !microwave.FaultActive)
                return;

            PLC210MicrowaveClient.RequestMicrowave(false);
            ProcessLogger.OpTimeLogging(false, "MWPower");
            PLC210MicrowaveClient.RequestReset();
        }

        private void UpdateCoolingSection()
        {
            PLC210CoolingClient.State state = PLC210CoolingClient.GetState();

            for (int i = 0; i < _coolingTemp.Length; i++)
            {
                _coolingTemp[i].Text = Reading(state.Connected && state.TempValid[i], state.TempC[i]);
                _coolingFlow[i].Text = Reading(state.Connected && state.FlowValid[i], state.FlowLpm[i]);
            }

            WaterPressureTextBox.Text = Reading(
                state.Connected && state.WaterPressureValid, state.WaterPressureBar);

            CDAPressureTextBox.Text = Reading(
                state.Connected && state.CdaPressureValid, state.CdaPressureBar);
        }

        private static string Reading(bool valid, double value)
        {
            return valid ? value.ToString("F1", CultureInfo.InvariantCulture) : "---";
        }

        private void UpdateAlarms()
        {
            PLC210AlarmClient.State state = PLC210AlarmClient.GetState();
            AlarmJournal.Poll(state);

            if (!state.Connected)
                return;

            if (state.AbortActive)
            {
                if (!_abortHandled)
                {
                    _abortHandled = true;
                    DropRequestsAfterAbort();
                }
            }
            else
            {
                _abortHandled = false;
            }
        }

        private void DropRequestsAfterAbort()
        {
            PLC210MicrowaveClient.RequestMicrowave(false);
            ProcessLogger.OpTimeLogging(false, "MWPower");
            PLC210MicrowaveClient.RequestPreheat(false);

            for (int i = 0; i < PLC210GasFlowClient.GasNames.Length; i++)
                PLC210GasFlowClient.RequestSetpoint(i, 0);

            CloseAllValves_Click(null, EventArgs.Empty);

            PLC210VacuumClient.RequestForeVacPump(false);
            PLC210TurboPumpClient.RequestRun(false);
        }

        private void UpdateStatusPlate()
        {
            List<StatusLine> lines = SystemStatus.Collect();

            if (!_statusPlateLeft.HasValue)
                _statusPlateLeft = StatusLabel.Left;

            if (SystemStatus.PlcConnected())
            {
                StatusLabel.Left = _statusPlateLeft.Value;
                StatusLevel alarmLevel = SystemStatus.AlarmLevel();
                StatusLabel.Text = SystemStatus.Describe(alarmLevel);
                StatusLabel.BackColor = PlateColor(alarmLevel);
                StatusLabel.ForeColor = SystemStatus.AnyModuleLost(lines)
                    ? (alarmLevel == StatusLevel.Warning ? Color.Orange : Color.Yellow)
                    : Color.Black;
            }
            else
            {
                StatusLabel.Text = "Not connected";
                StatusLabel.BackColor = Color.Transparent;
                StatusLabel.ForeColor = Color.Red;
                StatusLabel.Left = ManualMode_groupBox.Left + (ManualMode_groupBox.Width - StatusLabel.Width) / 2;
            }

            PLC210PidClient.SetTrafficLight(SystemStatus.TowerLamp(lines));
        }

        private int? _statusPlateLeft;

        private static Color PlateColor(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Error: return Color.Red;
                case StatusLevel.Warning: return Color.Gold;
                default: return Res.OnGreen;
            }
        }

        private void StatusLabel_Click(object sender, EventArgs e)
        {
            _faultStatusForm = ShowSingleton(_faultStatusForm);
        }

        private void ConnectionStatusToolMenu_Click(object sender, EventArgs e)
        {
            _statusForm = ShowSingleton(_statusForm);
        }

        private void FaultStatusToolMenu_Click(object sender, EventArgs e)
        {
            _faultStatusForm = ShowSingleton(_faultStatusForm);
        }

        private void ConnectToolMenu_Click(object sender, EventArgs e)
        {
            StartPlcClients();
            SetPlcLinkMenu(true);
        }

        private void DisconnectToolMenu_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                StopPlcClients();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
            SetPlcLinkMenu(false);
        }

        private void StartLoggingToolMenu_Click(object sender, EventArgs e)
        {
            Logger.Enabled = true;
            if (_manualRunActive)
                ProcessLogger.BeginSession();
            SetLoggingMenu(true);
        }

        private void StopLoggingToolMenu_Click(object sender, EventArgs e)
        {
            ProcessLogger.CreateLogFileEnded();
            Logger.Enabled = false;
            SetLoggingMenu(false);
        }

        private void SetLoggingMenu(bool logging)
        {
            StartLoggingToolMenu.Enabled = !logging;
            StopLoggingToolMenu.Enabled = logging;
        }

        private void ExitToolMenu_Click(object sender, EventArgs e)
        {
            Close();
        }

        private void ProcessParametersToolMenu_Click(object sender, EventArgs e)
        {
            _processParametersForm = ShowSingleton(_processParametersForm);
        }

        private void GasTrendToolMenu_Click(object sender, EventArgs e)
        {
            _gasTrendForm = ShowSingleton(_gasTrendForm);
        }

        private void PressureTrendToolMenu_Click(object sender, EventArgs e)
        {
            _pressureTrendForm = ShowSingleton(_pressureTrendForm);
        }

        private void MWPowerToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _mwPowerTrendForm = ShowSingleton(_mwPowerTrendForm);
        }

        private void temperatureTrendToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _temperatureTrendForm = ShowSingleton(_temperatureTrendForm);
        }

        private void PIDToolStripMenuItem_Click(object sender, EventArgs e)
        {
            _pidViewerForm = ShowSingleton(_pidViewerForm);
        }

        private static T ShowSingleton<T>(T existing) where T : Form, new()
        {
            if (existing == null || existing.IsDisposed)
            {
                T created = new T();
                created.Show();
                return created;
            }

            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;

            existing.Activate();
            return existing;
        }

        private void aboutToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (AboutForm about = new AboutForm())
                about.ShowDialog(this);
        }

        private void ManualRun_Click(object sender, EventArgs e)
        {
            if (!_manualRunActive)
                StartManualRun();
            else
                StopManualRun();
        }

        private void StartManualRun()
        {
            _manualRunActive = true;
            _manualRunStart = DateTime.Now;
            ProcessLogger.BeginSession();

            StartTimeValue.Text = _manualRunStart.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            DurationValue.Text = IdleDuration;

            ManualRun.Text = "Stop";
            ApplyManualRunGate();
        }

        private void StopManualRun()
        {
            string blocker = DescribeRunningEquipment();
            if (blocker != null)
            {
                MessageBox.Show(
                    this,
                    "Can't stop the session while something is still running:\r\n\r\n" + blocker,
                    "Manual Mode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _manualRunActive = false;
            ProcessLogger.CreateLogFileEnded();

            StartTimeValue.Text = IdleTime;
            DurationValue.Text = IdleDuration;

            ManualRun.Text = "Run";
            ApplyManualRunGate();
        }

        private void ApplyManualRunGate()
        {
            Gas_groupBox.Enabled = _manualRunActive;
            Microwave_groupBox.Enabled = _manualRunActive;
            Vacuum_groupBox.Enabled = _manualRunActive;

            Water_Btn.Enabled = _manualRunActive;

            AutoMode_groupBox.Enabled = !_manualRunActive;
        }

        private static string DescribeRunningEquipment()
        {
            PLC210GasValveClient.State gasValves = PLC210GasValveClient.GetState();
            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            PLC210MicrowaveClient.State microwave = PLC210MicrowaveClient.GetState();

            List<string> running = new List<string>();

            if (gasValves.Connected)
                for (int i = 0; i < PLC210GasValveClient.ValveCount; i++)
                    if (gasValves.ValveOn[i])
                        running.Add("gas valve GPV" + (i + 1).ToString(CultureInfo.InvariantCulture) + " is open");

            if (vacuum.Connected)
            {
                for (int i = 0; i < PLC210VacuumClient.ValveCount; i++)
                    if (vacuum.ValveOn[i])
                        running.Add("vacuum valve VPV" + (i + 1).ToString(CultureInfo.InvariantCulture) + " is open");

                if (vacuum.ForeVacPumpOn)
                    running.Add("the forevacuum pump is on");
                if (vacuum.WaterPumpOn)
                    running.Add("the water pump is on");
            }

            PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
            if (turbo.Connected && turbo.DriveAnswering && (turbo.Working || turbo.SpeedHz > 0))
                running.Add("the turbo pump is turning at "
                    + turbo.SpeedHz.ToString(CultureInfo.InvariantCulture) + " Hz");

            if (microwave.GeneratorAnswering && (microwave.MicrowaveOn || microwave.PreheatOn))
                running.Add(microwave.MicrowaveOn
                    ? "the microwave generator is on"
                    : "the generator filament is preheating");

            return running.Count == 0 ? null : string.Join("\r\n", running);
        }

        private void timerUi_Tick(object sender, EventArgs e)
        {
            CurrentTimeValue.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            StopWatchText.Text = FormatStopwatch(_stopwatch.Elapsed);

            if (_manualRunActive)
                DurationValue.Text = (DateTime.Now - _manualRunStart).ToString(
                    @"dd\.hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private void SWStartStop_Click(object sender, EventArgs e)
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                SWStartStop.Text = "Start";
            }
            else
            {
                _stopwatch.Start();
                SWStartStop.Text = "Stop";
            }
        }

        private void SWErase_Click(object sender, EventArgs e)
        {
            if (_stopwatch.IsRunning)
                _stopwatch.Restart();
            else
                _stopwatch.Reset();

            StopWatchText.Text = FormatStopwatch(_stopwatch.Elapsed);
        }

        private static string FormatStopwatch(TimeSpan elapsed)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}",
                (int)elapsed.TotalHours,
                elapsed.Minutes,
                elapsed.Seconds);
        }

        private void AutomaticRun_Click(object sender, EventArgs e) { }

        private void AutomaticPause_Click(object sender, EventArgs e) { }

        private void RecipeOpen_Click(object sender, EventArgs e) { }


        private void TempLoopDelayTimer_Tick(object sender, EventArgs e) { }

        private void ChamberPressure_SetVal_MouseDown(object sender, MouseEventArgs e) { }

        private void ChamberPressure_SetVal_MouseUp(object sender, MouseEventArgs e) { }

        private void ChamberPressureSetPoint_ValueChanged(object sender, EventArgs e) { }

        private void MWPowerSetPoint_MouseWheel(object sender, EventArgs e) { }

        private void H2_Set_ValueChanged(object sender, EventArgs e) { }

        private void groupBox3_Enter(object sender, EventArgs e) { }

        private void Microwave_groupBox_Enter(object sender, EventArgs e) { }

        private void pictureBox10_Click(object sender, EventArgs e) { }
    }
}
