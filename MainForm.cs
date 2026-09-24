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
    /// <summary>
    /// The single operator screen: gas, chamber, microwave and vacuum in one
    /// mimic diagram. Trends and the PID panel moved out to their own windows
    /// under the View menu.
    /// </summary>
    /// <remarks>
    /// Manual Mode's Run is the gate on everything: Gas, Microwave and Vacuum
    /// stay locked out until a session is opened, and the Time Section reports
    /// that session's start and duration.
    ///
    /// Two blocks of the layout -- Automatic Mode with its recipe fields, and
    /// the File menu -- have no hardware or logic behind them yet and are
    /// deliberately left inert until they do. They are marked "not wired yet"
    /// below.
    /// </remarks>
    public partial class MainForm : Form
    {
        // The generator reports only a done/not-done flag, never a percentage or
        // a remaining time, so both "Time to preheat" and the bar under the
        // buttons run against this nominal duration rather than against anything
        // the device says. Nor does the PLC confirm anything: status bit 0x0020
        // is the last PREHEAT request echoed back, so the countdown is only
        // meaningful while the generator is actually answering -- which is what
        // UpdateMicrowaveSection gates it on.
        private const int PreheatSeconds = 150;

        // Index == PLC channel index in PLC210GasFlowClient (H2, CH4, N2, O2,
        // Ar, H2 second line) == aCfg[] order in FB_MfcModbusMaster.st.
        private NumericUpDown[] _gasSetpoint;
        private TextBox[] _gasMeasured;

        // Index == valve bit in PLC210GasValveClient (GPV1..GPV8).
        private PictureBox[] _gasValveBox;

        // Index == circuit index in PLC210CoolingClient (Stage, Chamber, MW
        // Head, MW Power, Tuner, Internal, External). Waveguide/Rod has no
        // sensor on this machine, so its two boxes stay hidden and out of
        // these arrays.
        private TextBox[] _coolingTemp;
        private TextBox[] _coolingFlow;

        // Index == valve bit in PLC210VacuumClient (VPV1..VPV8). VPV3 and VPV8
        // are null: the mimic diagram has no symbol for them, so they cannot be
        // operated from this screen.
        private PictureBox[] _vacuumValveBox;

        // Last confirmed generator state. The PREHEAT/MICROWAVE buttons toggle
        // off of this, never off a separately tracked "what did I last ask for"
        // field -- that is what used to make the Microwave button unreliable, as
        // it always started at false regardless of what was actually running.
        private PLC210MicrowaveClient.State _microwaveState = new PLC210MicrowaveClient.State
        {
            StatusText = "PLC210 microwave generator disabled"
        };

        // Operator's own stopwatch, unrelated to the process: Start/Stop and
        // Erase drive nothing but the label beside them.
        private readonly Stopwatch _stopwatch = new Stopwatch();

        // Manual Mode session. Until Run is pressed the plant sections are
        // locked out, so the Time Section's Start Time / Duration always refer to
        // a session somebody deliberately opened.
        private bool _manualRunActive;
        private DateTime _manualRunStart;

        private const string IdleTime = "00:00:00";
        private const string IdleDuration = "00:00:00:00";

        private const int MicrowaveAutoResetDelayTicks = 5;
        private int _microwaveAutoResetTicks;
        private bool _waterFaultWarned;

        // What TurboSpeedBar_Paint draws. Held here rather than read out of the
        // client inside the paint handler, so that a repaint Windows asks for --
        // the window being uncovered, say -- draws the same bar the last
        // exchange put there instead of whatever the polling thread has since
        // written.
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

            // The full scale of an РРГ-20 is a property of the regulator, not of
            // the drawing, so the spinner ranges come from the client rather than
            // from the design file (whose sixth channel still carried the 10 sccm
            // range of an older machine).
            for (int i = 0; i < _gasSetpoint.Length; i++)
            {
                _gasSetpoint[i].Minimum = 0;
                _gasSetpoint[i].Maximum = (decimal)PLC210GasFlowClient.FullScaleSccm[i];
            }

            // ...and with the sixth channel now spanning 0..1000 like the first,
            // its 0.01 step out of the design file would take 100000 clicks to
            // cross. It gets the first H2 line's step instead.
            H22_Set.Increment = H2_Set.Increment;

            _coolingTemp = new[] { StageTemp, ChamberTemp, MWHeadTemp, MWPowerTemp, TunerTemp, InternalTemp, ExternalTemp };
            _coolingFlow = new[] { StageFlow, ChamberFlow, MWHeadFlow, MWPowerFlow, TunerFlow, InternalFlow, ExternalFlow };

            _gasValveBox = new[] { Valve_1, Valve_2, Valve_3, Valve_4, Valve_5, Valve_6, Valve_7, Valve_8 };
            _vacuumValveBox = new[] { Valve_17, Valve_18, null, Valve_20, Valve_21, Valve_22, Valve_24, null };

            MWPowerSetPoint.Minimum = (decimal)PLC210MicrowaveClient.MinSetpointKw;
            MWPowerSetPoint.Maximum = (decimal)PLC210MicrowaveClient.MaxSetpointKw;

            // No session open yet, so the plant starts locked out.
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
            // Asked before anything is shut down, so answering No leaves every
            // client running and the window exactly as it was.
            //
            // Only when the operator is the one closing it: on a Windows shutdown
            // or a kill from Task Manager there is nobody to answer, and a dialog
            // would just delay the exit until the OS gave up waiting.
            if (e.CloseReason == CloseReason.UserClosing && !ConfirmExit())
            {
                e.Cancel = true;
                return;
            }

            SuperCycle.Stop();

            ProcessLogger.OpTimeLogging(false, "MWPower");
            ProcessLogger.LogBinary();
            Task write = ProcessLogger.CreateLogFileEnded();
            if (write != null)
                write.Wait(5000);

            StopPlcClients();

            IniWriter.INI.Write("MainForm", "X", Location.X.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write("MainForm", "Y", Location.Y.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Confirms closing the operator screen. Defaults to No: this window is
        /// the only view of a running reactor, and a stray Enter or double-click
        /// on the title bar should not be enough to lose it.
        /// </summary>
        /// <remarks>
        /// When something is still on, the dialog names it -- the same list, off
        /// the same DescribeRunningEquipment, that Manual Mode Stop refuses on.
        /// Stop refuses outright because ending a session with gas flowing is
        /// never what the operator meant; closing the window only warns, because
        /// there is nothing here that turns the plant off, so refusing would
        /// leave the operator unable to close the screen at all while pumping.
        /// </remarks>
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

        // The window is FixedSingle, so only the position is remembered.
        private void RestoreWindowPlacement()
        {
            if (IniWriter.INI.KeyExists("X", "MainForm") && IniWriter.INI.KeyExists("Y", "MainForm"))
                Location = new Point(
                    int.Parse(IniWriter.INI.ReadINI("MainForm", "X")),
                    int.Parse(IniWriter.INI.ReadINI("MainForm", "Y")));

            if (Location.X < 0 || Location.Y < 0)
                Location = new Point(0, 0);
        }

        // --- PLC210 connection (config.ini [PLC210]) ---
        // Nine parallel Modbus TCP connections to the same PLC, one per
        // register block: the PLC's own scan publishes them all into awHolding,
        // but a single client polling every block would make the slowest one
        // (the РРГ-20 serial sweep) set the update rate for all of them.
        private void StartPlcClients()
        {
            string host = ReadIniString("PLC210", "IP", "192.168.1.10");
            int port = ReadIniInt("PLC210", "Port", 502);

            // Which of the controller's own discrete inputs carries the chamber
            // lid switch (1..8 = FDI1..FDI8, 9..12 = DI9..DI12). A wiring fact,
            // so it is read from the config rather than fixed in the client --
            // see GVL_PlcIO.st for the other half of that argument.
            PLC210PidClient.SetLidInputChannel(
                ReadIniInt("PLC210", "LidInput", PLC210PidClient.DefaultLidInputChannel));

            PLC210PidClient.Start(host, port);          // 100..139, plus chamber pressure at 130..132 and the discrete inputs at 139
            PLC210ThyracontClient.Start(host, port);    // 140..147, the vacuum gauge
            PLC210GasFlowClient.Start(host, port);      // 64..99, the six РРГ-20 regulators
            PLC210PyrometerClient.Start(host, port);    // 160..191, the two Kelvin pyrometers
            PLC210MicrowaveClient.Start(host, port);    // 192..203, the generator on slave 9
            PLC210GasValveClient.Start(host, port);     // 133..134
            PLC210VacuumClient.Start(host, port);       // 135..138
            PLC210CoolingClient.Start(host, port);      // 206..239, the two МВ210-102 analogue modules
            PLC210TurboPumpClient.Start(host, port);    // 240..247, the KYKY TD turbo pump drive
            PLC210AlarmClient.Start(host, port);        // 248..295, the alarm thresholds and the alarm state

            AlarmSettings.Load();
            AlarmSettings.LidInput = PLC210PidClient.LidInputChannel;
            PLC210AlarmClient.PushThresholds(AlarmSettings.Pack());

            // The regulators used to be enabled by opening the Gas Section
            // window. There is no such window now -- the gas controls are always
            // on screen, so the subsystem comes up with the application.
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

        // --- The once-a-second exchange ---
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

            // Nothing is written to the PID until SET has been pressed at least
            // once -- otherwise the defaults (zero gains, zero limits) would get
            // pushed the moment the application connects and immediately collapse
            // the PLC's output to its lower limit.
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
                // Direct drive bypasses the controller, so the operator's own
                // clamp must not also apply -- open the limits to the full range.
                LowerLimit = ChamberPid.DirectMode ? 0 : ChamberPid.LowerLimit,
                UpperLimit = ChamberPid.DirectMode ? 5000 : ChamberPid.UpperLimit,
                DirectMode = ChamberPid.DirectMode,
                DirectValue = Math.Max(0, Math.Min(5000, ChamberPid.DirectValue))
            };

            PLC210PidClient.Channel plenumDisabled = new PLC210PidClient.Channel
            {
                Enabled = false
            };

            // No reset: a new setpoint or a new gain should be picked up
            // smoothly, not restart the PLC's integrator from zero.
            PLC210PidClient.SetChannels(chamber, plenumDisabled, reset: false);
        }

        // --- Gas Section ---
        private void UpdateGasSection()
        {
            PLC210GasFlowClient.State state = PLC210GasFlowClient.GetState();

            for (int i = 0; i < _gasMeasured.Length; i++)
            {
                PLC210GasFlowClient.ChannelState channel = state.Channels[i];

                // Same rule as the cooling readouts: a frozen number reads as gas
                // that is still flowing. Nothing clears the measured value at
                // either end -- PRG_GasFlow.st keeps the last word the regulator
                // sent, and the client hands the previous Channels array back when
                // the TCP link drops -- so the dashes have to come off the link,
                // not off the value.
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

        // --- Valves ---
        private void Valves_Click(object sender, EventArgs e)
        {
            PictureBox box = sender as PictureBox;
            if (box == null)
                return;

            // Toggling off the picture means toggling off the state the PLC has
            // confirmed, not off a locally remembered request -- the same
            // reasoning as the microwave buttons above.
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

            // Every vacuum bit, including the two with no symbol on the mimic
            // diagram: "close all" has to mean all of them.
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

        // Assigning BackgroundImage invalidates the control, so only assign when
        // the picture actually changes -- otherwise all fourteen valves repaint
        // every second for nothing.
        private static void ShowValve(PictureBox box, bool open)
        {
            Image wanted = open ? Res.ValveOpen : Res.ValveClosed;
            if (!ReferenceEquals(box.BackgroundImage, wanted))
                box.BackgroundImage = wanted;
        }

        // --- Chamber: pressure, high vacuum, pyrometers ---
        private void UpdateChamber()
        {
            PLC210PidClient.State pid = PLC210PidClient.GetState();
            ChamberPressure_textbox.Text = pid.PlcPressureAvailable
                ? Math.Max(0, pid.PlcPressureTorr).ToString("F1", CultureInfo.InvariantCulture)
                : "---";

            // Both units -- the gauge is specified in mbar but the rest of this
            // screen works in Torr, so neither one alone is enough -- in two
            // boxes of their own, in the order the caption names them.
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

        /// <summary>
        /// Only one physical pyrometer is ever wired at a time; this picks
        /// whichever is actually answering, RXT-PRO first. Null when neither is.
        /// </summary>
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

        // --- Microwave Section ---
        private void UpdateMicrowaveSection()
        {
            PLC210MicrowaveClient.State state = PLC210MicrowaveClient.GetState();
            _microwaveState = state;

            // Status bit 0x0020 is not a confirmation from the generator, it is
            // the PLC echoing stMw.xLastPreheatValue -- i.e. what this screen
            // last asked for -- and awHolding[205] counts up off that same
            // request. With the generator powered down or its RS485 link cut,
            // PRG_Microwave.st still echoes and still counts, so PREHEAT used to
            // go green and run a countdown against a filament that never got the
            // order. Nothing here is believed unless the generator is answering.
            bool generatorAlive = state.GeneratorAnswering;

            // Incident and reflected are the last words PRG_Microwave.st got out
            // of the generator and are never cleared either, so like the gas
            // flows they freeze on screen rather than fall to zero once it goes
            // quiet.
            IncMWPower.Text = generatorAlive
                ? state.IncidentKw.ToString("F2", CultureInfo.InvariantCulture)
                : "---";
            ReflMWPower.Text = generatorAlive
                ? state.ReflectedKw.ToString("F2", CultureInfo.InvariantCulture)
                : "---";

            MWReconnect.Visible = !generatorAlive;

            StartMW.BackColor = generatorAlive && state.PreheatOn ? Res.OnGreen : SystemColors.Control;
            button1.BackColor = generatorAlive && state.MicrowaveOn ? Res.OnGreen : SystemColors.Control;

            // Matches the generator's own touch screen, where Microwave greys out
            // while Fault is lit -- RESET (or STOP) is the way forward from
            // there. Also blocked below 9 Torr: PRG_Microwave.st refuses the coil
            // write regardless, and a button that visibly does nothing is worse
            // than a disabled one.
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
                // Nothing is preheating, so the whole nominal run is still ahead;
                // "0s" here used to read as "ready to fire" on a cold filament.
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

        // No state check, no toggle -- always forces both outputs off.
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

        // --- Pumps ---
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
            // Disabled until the PLC answers and a Manual Mode session is open:
            // with no confirmed state there is nothing meaningful for a click to
            // toggle off, and outside a session nothing should start at all.
            button.Enabled = operable;
            button.Text = running ? "PUMP ON" : "PUMP OFF";
            button.BackColor = running ? Res.OnGreen : Color.LightSalmon;
        }

        private void ForeVacPump_Click(object sender, EventArgs e)
        {
            bool turnOn = !PLC210VacuumClient.GetState().ForeVacPumpOn;

            // The turbo pump exhausts into the forevacuum line, so pulling the
            // backing pump out from under a rotor that is still turning is the
            // one sequence that damages it. ArdisCVDMaster refused this too
            // ("Can't turn off. HiVac Pump is working"), and it is refused off
            // the speed the drive reports rather than off the latched command:
            // a pump told to stop two seconds ago is still at full speed.
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

        // --- Turbo pump -------------------------------------------------------
        // A turbo pump may only be started once the chamber has been roughed
        // out: at anything much above a couple of Torr the rotor is working
        // against a gas load it was never sized for, and it overheats or trips
        // instead of spinning up. The three conditions below are the ones
        // ArdisCVDMaster refused on -- backing valve open, forevacuum pump
        // running, chamber under the pressure limit -- kept as they were, except
        // that this names the one that is actually in the way rather than
        // printing all three every time.

        private const double TurboMaxChamberPressureTorr = 2.0;

        // VPV7: valve bit 6 in PLC210VacuumClient, index 6 of _vacuumValveBox.
        // The valve between the turbo pump's exhaust and the forevacuum line, so
        // with it shut the pump has nothing backing it.
        private const int TurboBackingValveIndex = 6;

        private void UpdateTurboPump()
        {
            PLC210TurboPumpClient.State state = PLC210TurboPumpClient.GetState();
            bool live = state.Connected && state.DriveAnswering;

            // Same rule as the other pumps: nothing to toggle until the drive
            // has confirmed a state, and nothing starts outside a session.
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

            // Spin-up takes minutes, and a green button for all of it would say
            // the pump is ready when it is nowhere near.
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

            // Stopping is never refused, and it toggles off what the drive
            // reports rather than off a remembered request -- the same reasoning
            // as the valves and the microwave buttons. RunLatched is in there so
            // that the seconds between pressing start and the rotor actually
            // moving are still cancellable.
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

        /// <summary>
        /// What stands in the way of starting the turbo pump, or null if nothing
        /// does.
        /// </summary>
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

            // An unavailable reading blocks exactly as a high one does: the
            // condition is "the chamber is known to be below the limit", and a
            // gauge that is not answering does not establish that.
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

        // --- Cooling Section ---
        // Read-only throughout: the seven circuits, the water pressure and the
        // CDA pressure are measurements, and the only control in this group is
        // the water pump button, which belongs to the vacuum client above.
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

        /// <summary>
        /// One decimal, or "---" when the channel is not to be trusted.
        /// </summary>
        /// <remarks>
        /// A blank field is deliberate rather than a held last value: a frozen
        /// number on a cooling readout reads as "the water is still flowing",
        /// which is exactly the wrong thing to imply about a dead sensor.
        /// </remarks>
        private static string Reading(bool valid, double value)
        {
            return valid ? value.ToString("F1", CultureInfo.InvariantCulture) : "---";
        }

        // --- Status plate ---
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

            // The signal tower on the panel shows the same verdict, so someone
            // standing at the reactor sees it without looking at the screen.
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

        // --- Settings menu ---
        private void ProcessParametersToolMenu_Click(object sender, EventArgs e)
        {
            _processParametersForm = ShowSingleton(_processParametersForm);
        }

        // --- View menu ---
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

        /// <summary>
        /// Opens the window if it is not open, brings it to the front if it is.
        /// </summary>
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

        // --- Manual Mode session ---------------------------------------------
        // Run is the gate on the whole plant, carried over from the previous
        // machine: nothing in Gas, Microwave or Vacuum can be touched until an
        // operator has deliberately opened a session, and Stop refuses while
        // anything is still open or running, so a session cannot be closed with
        // gas flowing or a pump going.
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

            // Water_Btn lives in the Cooling group, which is not gated, so it
            // needs disabling in its own right. Its final Enabled state is
            // settled by UpdatePumps, which also requires a live PLC.
            Water_Btn.Enabled = _manualRunActive;

            // Automatic Mode and Manual Mode are alternatives, the same way they
            // were before: opening one locks the other out.
            AutoMode_groupBox.Enabled = !_manualRunActive;
        }

        /// <summary>
        /// What is still running and therefore blocks Stop, or null if nothing is.
        /// </summary>
        private static string DescribeRunningEquipment()
        {
            PLC210GasValveClient.State gasValves = PLC210GasValveClient.GetState();
            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            PLC210MicrowaveClient.State microwave = PLC210MicrowaveClient.GetState();

            List<string> running = new List<string>();

            // Only what the PLC has confirmed counts: with the link down there is
            // no confirmed state to hold the session open on.
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

            // Off the speed the drive reports, not the latched command: a rotor
            // coasting down after a stop is still spinning, and a session must
            // not close out from under it.
            PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
            if (turbo.Connected && turbo.DriveAnswering && (turbo.Working || turbo.SpeedHz > 0))
                running.Add("the turbo pump is turning at "
                    + turbo.SpeedHz.ToString(CultureInfo.InvariantCulture) + " Hz");

            // ...and for the microwave that means the generator answering, not
            // just the PLC: both flags below are the PLC echoing the last request
            // back, so with the generator off they would hold the session open on
            // a filament that is stone cold.
            if (microwave.GeneratorAnswering && (microwave.MicrowaveOn || microwave.PreheatOn))
                running.Add(microwave.MicrowaveOn
                    ? "the microwave generator is on"
                    : "the generator filament is preheating");

            return running.Count == 0 ? null : string.Join("\r\n", running);
        }

        // --- Time Section and Stopwatch --------------------------------------
        // timerUi is the only thing on this screen that has to move faster than
        // the one-second exchange: a clock that updates once a second lands its
        // tick anywhere inside the second and looks like it stutters, so the two
        // labels are redrawn four times a second instead.
        private void timerUi_Tick(object sender, EventArgs e)
        {
            CurrentTimeValue.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            StopWatchText.Text = FormatStopwatch(_stopwatch.Elapsed);

            // Left frozen at 00:00:00:00 outside a session -- Start Time is blank
            // then too, so a duration counting up from nothing would be a lie.
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
            // Erase while running restarts from zero rather than stopping --
            // carried over from the previous machine, where this was how you
            // timed one stage after another without losing the count.
            if (_stopwatch.IsRunning)
                _stopwatch.Restart();
            else
                _stopwatch.Reset();

            // Repainted here as well as on the tick, so the click is reflected at
            // once instead of up to 250 ms later.
            StopWatchText.Text = FormatStopwatch(_stopwatch.Elapsed);
        }

        /// <summary>
        /// Hours:minutes:seconds, hours not wrapped at 24 and never truncated --
        /// a run can outlast a day and the total is what matters.
        /// </summary>
        private static string FormatStopwatch(TimeSpan elapsed)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:00}:{1:00}:{2:00}",
                (int)elapsed.TotalHours,
                elapsed.Minutes,
                elapsed.Seconds);
        }

        // --- Not wired yet ---------------------------------------------------
        // The handlers below exist because the design references them. The blocks
        // they belong to (recipes and automatic mode, the press-and-hold
        // pressure ramp) have no hardware or logic behind them yet; they are
        // drawn so the screen matches the intended layout, and they do nothing
        // until that logic exists.

        private void AutomaticRun_Click(object sender, EventArgs e) { }

        private void AutomaticPause_Click(object sender, EventArgs e) { }

        private void RecipeOpen_Click(object sender, EventArgs e) { }


        private void TempLoopDelayTimer_Tick(object sender, EventArgs e) { }

        // Press and hold on Set was a pressure ramp on the previous machine.
        // Click commits the setpoint; holding does nothing for now.
        private void ChamberPressure_SetVal_MouseDown(object sender, MouseEventArgs e) { }

        private void ChamberPressure_SetVal_MouseUp(object sender, MouseEventArgs e) { }

        // Wheel-scrolling a setpoint only moves the number; it is applied by Set.
        private void ChamberPressureSetPoint_ValueChanged(object sender, EventArgs e) { }

        private void MWPowerSetPoint_MouseWheel(object sender, EventArgs e) { }

        private void H2_Set_ValueChanged(object sender, EventArgs e) { }

        // Designer-generated GroupBox.Enter and decorative PictureBox.Click
        // handlers, kept only because the design file wires them.
        private void groupBox3_Enter(object sender, EventArgs e) { }

        private void Microwave_groupBox_Enter(object sender, EventArgs e) { }

        private void pictureBox10_Click(object sender, EventArgs e) { }
    }
}
