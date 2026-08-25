using ArdisCVDCore.modules_hw;
using ArdisCVDCore.trends;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
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

        private GasTrendForm _gasTrendForm;
        private PressureTrendForm _pressureTrendForm;
        private MWPowerTrendForm _mwPowerTrendForm;
        private TemperatureTrendForm _temperatureTrendForm;
        private PidViewerForm _pidViewerForm;
        private StatusForm _statusForm;
        private ProcessParametersForm _processParametersForm;

        public MainForm()
        {
            InitializeComponent();
            BindChannels();
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

            PLC210PidClient.Stop();
            PLC210ThyracontClient.Stop();
            PLC210GasFlowClient.Stop();
            PLC210PyrometerClient.Stop();
            PLC210MicrowaveClient.Stop();
            PLC210GasValveClient.Stop();
            PLC210VacuumClient.Stop();
            PLC210CoolingClient.Stop();

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
        // Eight parallel Modbus TCP connections to the same PLC, one per
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

            // The regulators used to be enabled by opening the Gas Section
            // window. There is no such window now -- the gas controls are always
            // on screen, so the subsystem comes up with the application.
            PLC210PidClient.SetGasSubsystemEnabled(true);
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
            UpdateCoolingSection();
            UpdateStatusPlate();

            // Nothing is written to the PID until SET has been pressed at least
            // once -- otherwise the defaults (zero gains, zero limits) would get
            // pushed the moment the application connects and immediately collapse
            // the PLC's output to its lower limit.
            if (ChamberPid.Committed)
                PushChamberChannel();
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
            if (index >= 0)
                PLC210VacuumClient.RequestValve(index, wanted);
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
            //
            // G3, not a fixed number of decimals: the VSM79 spans 1000..5e-9
            // mbar, and "0.0" would be its reading over most of that range.
            PLC210ThyracontClient.State hiVac = PLC210ThyracontClient.GetState();
            HiVacPressure.Text = hiVac.HasValidValue
                ? hiVac.PressureTorr.ToString("G3", CultureInfo.InvariantCulture)
                : "---";
            HiVacPressureMbar.Text = hiVac.HasValidValue
                ? hiVac.PressureMbar.ToString("G3", CultureInfo.InvariantCulture)
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

            // The Status plate deliberately says nothing about a generator that
            // is merely switched off (see SystemStatus.AddMicrowave), so this is
            // the only place the operator is told -- quietly, in grey, next to
            // the countdown it explains the absence of.
            MWNotConnected.Visible = !generatorAlive;

            StartMW.BackColor = generatorAlive && state.PreheatOn ? Color.LightGreen : SystemColors.Control;
            button1.BackColor = generatorAlive && state.MicrowaveOn ? Color.LightGreen : SystemColors.Control;

            // Matches the generator's own touch screen, where Microwave greys out
            // while Fault is lit -- RESET (or STOP) is the way forward from
            // there. Also blocked below 9 Torr: PRG_Microwave.st refuses the coil
            // write regardless, and a button that visibly does nothing is worse
            // than a disabled one.
            StartMW.Enabled = generatorAlive;
            button1.Enabled = generatorAlive && !state.FaultActive && !state.ChamberPressureLow;

            bool preheating = generatorAlive && state.PreheatOn && !state.FilamentPreheatDone;
            int remaining = Math.Max(0, PreheatSeconds - state.PreheatElapsedSeconds);

            if (preheating)
                TimeToStart.Text = remaining.ToString(CultureInfo.InvariantCulture) + "s";
            else if (generatorAlive && state.FilamentPreheatDone)
                TimeToStart.Text = "0s";
            else
                // Nothing is preheating, so the whole nominal run is still ahead;
                // "0s" here used to read as "ready to fire" on a cold filament.
                TimeToStart.Text = PreheatSeconds.ToString(CultureInfo.InvariantCulture) + "s";

            PreheatProgress.Visible = preheating;
            // Capped, so a preheat that runs longer than nominal shows a full bar
            // instead of throwing on an out-of-range Value. Reset while idle so a
            // counter the PLC kept running with the generator off cannot make the
            // next preheat start from a full bar.
            PreheatProgress.Value = preheating
                ? Math.Max(0, Math.Min(state.PreheatElapsedSeconds, PreheatSeconds))
                : 0;
        }

        private void StartMW_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestPreheat(!_microwaveState.PreheatOn);
        }

        private void button1_Click_1(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestMicrowave(!_microwaveState.MicrowaveOn);
        }

        private void ResetMW_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestReset();
        }

        // No state check, no toggle -- always forces both outputs off.
        private void StopMW_Click(object sender, EventArgs e)
        {
            PLC210MicrowaveClient.RequestMicrowave(false);
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
        }

        private static void ShowPump(Button button, bool operable, bool running)
        {
            // Disabled until the PLC answers and a Manual Mode session is open:
            // with no confirmed state there is nothing meaningful for a click to
            // toggle off, and outside a session nothing should start at all.
            button.Enabled = operable;
            button.Text = running ? "PUMP ON" : "PUMP OFF";
            button.BackColor = running ? Color.LightGreen : Color.LightSalmon;
        }

        private void ForeVacPump_Click(object sender, EventArgs e)
        {
            PLC210VacuumClient.RequestForeVacPump(!PLC210VacuumClient.GetState().ForeVacPumpOn);
        }

        private void Water_Btn_Click(object sender, EventArgs e)
        {
            PLC210VacuumClient.RequestWaterPump(!PLC210VacuumClient.GetState().WaterPumpOn);
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
        private void UpdateStatusPlate()
        {
            StatusLevel level = SystemStatus.Worst(SystemStatus.Collect());

            StatusLabel.Text = SystemStatus.Describe(level);
            StatusLabel.BackColor = PlateColor(level);
            // The design leaves ForeColor transparent, which is unreadable on
            // the red and green plates.
            StatusLabel.ForeColor = Color.Black;

            // The signal tower on the panel shows the same verdict, so someone
            // standing at the reactor sees it without looking at the screen.
            PLC210PidClient.SetTrafficLight(TowerLamp(level));
        }

        private static TrafficLight TowerLamp(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Error: return TrafficLight.Red;
                case StatusLevel.Warning: return TrafficLight.Yellow;
                default: return TrafficLight.Green;
            }
        }

        private static Color PlateColor(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Error: return Color.Red;
                case StatusLevel.Warning: return Color.Gold;
                default: return Color.Green;
            }
        }

        private void StatusLabel_Click(object sender, EventArgs e)
        {
            _statusForm = ShowSingleton(_statusForm);
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

        private void TimeToStart_Click(object sender, EventArgs e) { }

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
