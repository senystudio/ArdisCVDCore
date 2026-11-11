using ArdisCVDCore.modules_hw;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public partial class MainForm
    {
        internal const decimal MaxRecipeChamberTorr = 500;

        private static readonly bool[] VacuumValveFitted = { true, true, false, true, true, true, true, false };

        private List<StepItemData> _recipe;
        private TimeSpan _recipeSpan;
        private bool _autoActive;
        private bool _autoPaused;
        private bool _autoInterlocked;
        private bool _autoStepStarted;
        private bool _autoOutputsApplied;
        private bool _autoStepPauseDone;
        private bool _autoPresetWanted;
        private bool _autoMicrowaveRequested;
        private int _autoStepIndex;
        private int _autoStepTicks;
        private int _autoStepLength;
        private StepItemData _autoStep;
        private StepItemData _autoPreviousStep;
        private string _autoWaiting;
        private RecipeEditor _recipeEditor;

        private void AddRecipeEditorMenu()
        {
            ToolStripMenuItem item = new ToolStripMenuItem("Recipe Editor");
            item.Image = Res.Glyph('', SystemColors.ControlText, 20);
            item.Click += (s, e) => _recipeEditor = ShowSingleton(_recipeEditor);

            int exitIndex = fileToolStripMenuItem.DropDownItems.IndexOf(ExitToolMenu);
            if (exitIndex < 0)
                exitIndex = fileToolStripMenuItem.DropDownItems.Count;

            fileToolStripMenuItem.DropDownItems.Insert(exitIndex, new ToolStripSeparator());
            fileToolStripMenuItem.DropDownItems.Insert(exitIndex, item);
        }

        private void RecipeOpen_Click(object sender, EventArgs e)
        {
            if (_autoActive)
                return;

            string path;
            using (RecipeSelector form = new RecipeSelector())
            {
                if (form.ShowDialog(this) != DialogResult.OK)
                    return;
                path = form.fname;
            }

            if (string.IsNullOrEmpty(path) || !path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                return;

            List<StepItemData> loaded;
            try
            {
                Cursor.Current = Cursors.WaitCursor;
                loaded = (List<StepItemData>)DataSerializer.Deserialization(
                    Path.Combine(RecipeEditor.RecipeFolder, path));
            }
            catch (Exception ex)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(this, "Can't read the recipe:\r\n\r\n" + ex.Message, "Automatic Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor.Current = Cursors.Default;

            string problem = DescribeRecipeProblem(loaded);
            if (problem != null)
            {
                MessageBox.Show(this, "This recipe can't run on this machine:\r\n\r\n" + problem,
                    "Automatic Mode", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _recipe = loaded;
            _recipeSpan = TimeSpan.Zero;
            foreach (StepItemData step in _recipe)
                _recipeSpan += StepSpan(step);

            StepQuntity.Text = _recipe.Count.ToString(CultureInfo.InvariantCulture);
            RecipeTotalTime.Text = _recipeSpan.ToString(@"dd\.hh\:mm\:ss", CultureInfo.InvariantCulture);
            RecipeName.Text = Path.GetFileNameWithoutExtension(path);
        }

        private static string DescribeRecipeProblem(List<StepItemData> recipe)
        {
            if (recipe == null || recipe.Count == 0)
                return "the recipe has no steps";

            List<string> problems = new List<string>();
            for (int s = 0; s < recipe.Count; s++)
            {
                StepItemData step = recipe[s];
                string name = "step " + (s + 1).ToString(CultureInfo.InvariantCulture) + " \"" + step.StepName + "\"";

                double mwMax = Math.Max(step.MWPower.IncidentInitialValue, step.MWPower.IncidentEndValue);
                if (mwMax > PLC210MicrowaveClient.MaxSetpointKw)
                    problems.Add(name + ": MW Inc " + mwMax.ToString("0.##", CultureInfo.InvariantCulture)
                        + " is over " + PLC210MicrowaveClient.MaxSetpointKw.ToString(CultureInfo.InvariantCulture)
                        + " kW (a recipe from ArdisCVDMaster stores Watts; re-enter it in kW in the Recipe Editor)");

                StepItemData.MFCStruct[] gases = StepGases(step);
                for (int i = 0; i < gases.Length; i++)
                {
                    double gasMax = Math.Max(gases[i].InitialValue, gases[i].EndValue);
                    if (gasMax > PLC210GasFlowClient.FullScaleSccm[i])
                        problems.Add(name + ": " + PLC210GasFlowClient.GasNames[i] + " "
                            + gasMax.ToString("0.##", CultureInfo.InvariantCulture) + " sccm is over the "
                            + PLC210GasFlowClient.FullScaleSccm[i].ToString(CultureInfo.InvariantCulture)
                            + " sccm full scale");
                }

                double chamberMax = Math.Max(step.Chamber.InitialValue, step.Chamber.EndValue);
                if (chamberMax > (double)MaxRecipeChamberTorr)
                    problems.Add(name + ": chamber " + chamberMax.ToString("0.#", CultureInfo.InvariantCulture)
                        + " Torr is over " + MaxRecipeChamberTorr.ToString(CultureInfo.InvariantCulture) + " Torr");
            }

            return problems.Count == 0 ? null : string.Join("\r\n", problems.ToArray());
        }

        private static StepItemData.MFCStruct[] StepGases(StepItemData step)
        {
            return new[] { step.H2, step.CH4, step.N2, step.O2, step.AR, step.H2_2 };
        }

        private static TimeSpan StepSpan(StepItemData step)
        {
            return TimeSpan.FromSeconds(StepLengthSeconds(step));
        }

        private static int StepLengthSeconds(StepItemData step)
        {
            return Math.Max(1, (int)Math.Round(step.StepDuration.TotalSeconds));
        }

        private void AutomaticRun_Click(object sender, EventArgs e)
        {
            if (!_autoActive)
            {
                StartRecipe();
                return;
            }

            string question = ContinueManualMode.Checked
                ? "Do you want to Stop the Recipe and continue manually? "
                : "Do you want to Stop the Recipe without continuing manually? ";
            if (MessageBox.Show(this, question, "Question", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;

            StopRecipe(ContinueManualMode.Checked, false);
        }

        private void AutomaticPause_Click(object sender, EventArgs e)
        {
            if (!_autoActive)
                return;

            SetAutoPause(!_autoPaused);
        }

        private void StartRecipe()
        {
            if (_recipe == null)
            {
                MessageBox.Show(this, "Recipe File isn't Loaded", "Automatic Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (_manualRunActive)
                return;

            if (!SystemStatus.PlcConnected())
            {
                MessageBox.Show(this, "The PLC is not connected.", "Automatic Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            PLC210AlarmClient.State alarms = PLC210AlarmClient.GetState();
            if (alarms.Connected && alarms.AbortActive)
            {
                MessageBox.Show(this, "An abort is active. Clear it before starting a recipe.", "Automatic Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _autoActive = true;
            _autoPaused = false;
            _autoInterlocked = false;
            _autoStepIndex = 0;
            _autoStepStarted = false;
            _autoStep = null;
            _autoPreviousStep = null;
            _autoWaiting = null;

            ProcessLogger.BeginSession();

            AutoStartTimeValue.Text = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
            AutomaticRun.Text = "Stop";
            AutomaticPause.BackColor = SystemColors.Control;
            ApplyManualRunGate();
        }

        private void RecipeTick()
        {
            if (!_autoActive)
                return;

            if (!_autoStepStarted)
            {
                BeginRecipeStep();
                if (!_autoActive)
                    return;
            }

            if (_autoPaused)
            {
                ShowAutoProgress();
                return;
            }

            if (!_autoOutputsApplied)
            {
                string blocker = ApplyStepOutputs();
                if (blocker != null)
                {
                    PauseForInterlock(blocker);
                    ShowAutoProgress();
                    return;
                }
                _autoOutputsApplied = true;
            }

            WriteStepRamps();

            if (_autoPresetWanted && TryArmRecipePreset())
                _autoPresetWanted = false;

            if (_autoStep.bStepPause && !_autoStepPauseDone)
            {
                _autoStepPauseDone = true;
                SetAutoPause(true);
                ShowAutoProgress();
                return;
            }

            string hardStop;
            _autoWaiting = DescribeStepWait(out hardStop);
            if (hardStop != null)
            {
                PauseForInterlock(hardStop);
                ShowAutoProgress();
                return;
            }

            if (_autoWaiting == null)
            {
                _autoStepTicks++;
                if (_autoStepTicks >= _autoStepLength)
                {
                    _autoStepIndex++;
                    _autoStepStarted = false;
                    if (_autoStepIndex >= _recipe.Count)
                    {
                        FinishRecipe();
                        return;
                    }
                }
            }

            ShowAutoProgress();
        }

        private void BeginRecipeStep()
        {
            if (_autoStepIndex >= _recipe.Count)
            {
                FinishRecipe();
                return;
            }

            _autoPreviousStep = _autoStep;
            _autoStep = (StepItemData)_recipe[_autoStepIndex].Clone();
            _autoStepTicks = 0;
            _autoStepLength = StepLengthSeconds(_autoStep);
            _autoStepStarted = true;
            _autoOutputsApplied = false;
            _autoStepPauseDone = false;
            _autoMicrowaveRequested = false;
            _autoWaiting = null;

            PushStepThresholds(_autoStep);

            ChamberPid.DirectMode = _autoStep.Chamber.DirectPIDEnable;
            if (_autoStep.Chamber.DirectPIDEnable)
                ChamberPid.DirectValue = Math.Max(0, Math.Min(5000, _autoStep.Chamber.DirectPIDValue));
            ChamberPid.Committed = true;

            _autoPresetWanted = StepChangesChamberFlow(_autoPreviousStep, _autoStep);

            CurrentStepName.Text = _autoStep.StepName;
            TimeStepDuration.Text = StepSpan(_autoStep).ToString(@"dd\.hh\:mm\:ss", CultureInfo.InvariantCulture);
        }

        private static bool StepChangesChamberFlow(StepItemData previous, StepItemData next)
        {
            if (previous == null)
                return true;

            if (previous.Chamber.InitialValue != next.Chamber.InitialValue)
                return true;

            StepItemData.MFCStruct[] a = StepGases(previous);
            StepItemData.MFCStruct[] b = StepGases(next);
            for (int i = 0; i < a.Length; i++)
                if (a[i].InitialValue != b[i].InitialValue)
                    return true;

            return false;
        }

        private bool TryArmRecipePreset()
        {
            PLC210GasValveClient.State valves = PLC210GasValveClient.GetState();
            if (!valves.Connected)
                return false;

            for (int i = 0; i < PLC210GasValveClient.ValveCount; i++)
                if (valves.ValveOn[i] != _autoStep.bDO[i])
                    return false;

            ArmChamberPreset();
            return true;
        }

        private static bool[] StepVacuumValves(StepItemData step)
        {
            bool[] want = new bool[PLC210VacuumClient.ValveCount];
            want[0] = step.bDO_2[0];
            want[1] = step.bDO_2[1];
            want[2] = step.bDO_2[2];
            want[3] = step.bDO_2[3];
            want[4] = step.bDO_2[4];
            want[5] = step.bDO_2[5] & !step.bDO_2[6];
            want[6] = step.bDO_2[7];

            for (int i = 0; i < want.Length; i++)
                if (i >= VacuumValveFitted.Length || !VacuumValveFitted[i])
                    want[i] = false;

            return want;
        }

        private static bool TurboTurning(PLC210TurboPumpClient.State turbo)
        {
            return turbo.Connected && turbo.DriveAnswering && (turbo.Working || turbo.SpeedHz > 0);
        }

        private string ApplyStepOutputs()
        {
            StepItemData step = _autoStep;
            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
            PLC210MicrowaveClient.State microwave = PLC210MicrowaveClient.GetState();

            bool[] wantVacuum = StepVacuumValves(step);
            bool wantFore = step.ForeVacPump.Enable;
            bool wantTurbo = step.HiVacPump.Enable;
            bool wantWater = step.Water.Enable;
            bool wantMicrowave = step.MWPower.Power_Enable;

            List<string> blockers = new List<string>();

            bool bothVpv = wantVacuum[Vpv4Index] && wantVacuum[Vpv5Index];
            if (bothVpv)
                blockers.Add("the step opens both VPV4 and VPV5");

            bool turboRunning = turbo.Working || turbo.RunLatched;
            if (wantTurbo && !turboRunning)
            {
                string turboBlocker = DescribeTurboHardBlocker(turbo, wantVacuum, wantFore);
                if (turboBlocker != null)
                    blockers.Add("turbo pump: " + turboBlocker);
            }

            bool foreOffBlocked = !wantFore && vacuum.Connected && vacuum.ForeVacPumpOn && TurboTurning(turbo);
            if (foreOffBlocked)
                blockers.Add("the forevacuum pump can't stop: the turbo pump is still turning at "
                    + turbo.SpeedHz.ToString(CultureInfo.InvariantCulture) + " Hz");

            bool wantPreheat = wantMicrowave || step.MWPower.Preheat_Enable;
            bool microwaveOn = microwave.GeneratorAnswering && microwave.MicrowaveOn;
            if (wantMicrowave && !microwaveOn)
            {
                string mwBlocker = DescribeMicrowaveHardBlocker(microwave, wantFore, wantWater);
                if (mwBlocker != null)
                    blockers.Add("microwave: " + mwBlocker);
            }
            else if (!wantMicrowave && wantPreheat && !(microwave.GeneratorAnswering && microwave.PreheatOn))
            {
                string preheatBlocker = DescribePreheatHardBlocker(microwave);
                if (preheatBlocker != null)
                    blockers.Add("preheat: " + preheatBlocker);
            }

            if (vacuum.WaterPumpOn != wantWater || !vacuum.Connected)
            {
                PLC210VacuumClient.RequestWaterPump(wantWater);
                _microwaveAutoResetTicks = wantWater ? MicrowaveAutoResetDelayTicks : 0;
                if (wantWater)
                    PLC210MicrowaveClient.ClearWaterFaultLatch();
            }

            if (wantFore || !foreOffBlocked)
                PLC210VacuumClient.RequestForeVacPump(wantFore);

            for (int i = 0; i < PLC210GasValveClient.ValveCount; i++)
                PLC210GasValveClient.RequestValve(i, step.bDO[i]);

            for (int i = 0; i < wantVacuum.Length; i++)
                if (!wantVacuum[i] && VacuumValveFitted[i])
                    PLC210VacuumClient.RequestValve(i, false);

            for (int i = 0; i < wantVacuum.Length; i++)
            {
                if (!wantVacuum[i])
                    continue;
                if ((i == Vpv4Index || i == Vpv5Index) && (bothVpv || VacuumPartnerOpen(vacuum, i)))
                    continue;
                PLC210VacuumClient.RequestValve(i, true);
            }

            if (!wantTurbo)
                PLC210TurboPumpClient.RequestRun(false);

            if (!wantMicrowave && microwave.MicrowaveOn)
            {
                PLC210MicrowaveClient.RequestMicrowave(false);
                ProcessLogger.OpTimeLogging(false, "MWPower");
            }

            if (!wantPreheat)
                PLC210MicrowaveClient.RequestPreheat(false);

            return blockers.Count == 0 ? null : string.Join("\r\n", blockers.ToArray());
        }

        private static bool VacuumPartnerOpen(PLC210VacuumClient.State vacuum, int index)
        {
            int partner = index == Vpv4Index ? Vpv5Index : Vpv4Index;
            return !vacuum.Connected || vacuum.ValveOn[partner];
        }

        private static string DescribeTurboHardBlocker(PLC210TurboPumpClient.State turbo, bool[] wantVacuum, bool wantFore)
        {
            if (!turbo.Connected)
                return "the PLC is not answering";
            if (!turbo.DriveAnswering)
                return "the pump drive is not answering the PLC";
            string fault = turbo.FaultText;
            if (fault != null)
                return "the drive is reporting a fault: " + fault;
            if (!wantVacuum[TurboBackingValveIndex])
                return "the step keeps VPV7 closed, so the pump has no backing line";
            if (!wantFore)
                return "the step keeps the forevacuum pump off";
            return null;
        }

        private static string DescribeMicrowaveHardBlocker(PLC210MicrowaveClient.State microwave, bool wantFore, bool wantWater)
        {
            string preheatBlocker = DescribePreheatHardBlocker(microwave);
            if (preheatBlocker != null)
                return preheatBlocker;
            if (microwave.FaultReportable)
                return "the generator reports a fault: " + SystemStatus.FaultReason(microwave);
            if (microwave.ChamberPressureLow)
                return "chamber pressure too low, microwave blocked";
            if (!wantFore)
                return "Forevacuum pump must be working!";
            if (!wantWater)
                return "Water pump must be working!";
            return null;
        }

        private static string DescribePreheatHardBlocker(PLC210MicrowaveClient.State microwave)
        {
            PLC210AlarmClient.State alarms = PLC210AlarmClient.GetState();
            if (!alarms.Connected)
                return "the alarm state of the PLC is not known";
            if (alarms.AbortActive)
                return "an abort is active; reset it in Fault Status";
            if (alarms.MicrowaveTripped)
                return "the microwave is tripped; reset it in Fault Status";
            if (!microwave.GeneratorAnswering)
                return "the generator is not answering";
            return null;
        }

        private string DescribeStepWait(out string hardStop)
        {
            hardStop = null;
            StepItemData step = _autoStep;
            List<string> waits = new List<string>();

            PLC210VacuumClient.State vacuum = PLC210VacuumClient.GetState();
            bool[] wantVacuum = StepVacuumValves(step);
            foreach (int i in new[] { Vpv4Index, Vpv5Index })
            {
                if (!wantVacuum[i])
                    continue;
                if (VacuumPartnerOpen(vacuum, i))
                {
                    int partner = i == Vpv4Index ? Vpv5Index : Vpv4Index;
                    waits.Add(VacuumValveName(partner) + " to close");
                }
                else
                {
                    PLC210VacuumClient.RequestValve(i, true);
                }
            }

            if (step.HiVacPump.Enable)
            {
                PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
                if (!(turbo.Working || turbo.RunLatched))
                {
                    string blocker = DescribeTurboHardBlocker(turbo, wantVacuum, step.ForeVacPump.Enable);
                    if (blocker != null)
                    {
                        hardStop = "turbo pump: " + blocker;
                        return null;
                    }

                    string soft = DescribeTurboSoftWait(vacuum);
                    if (soft == null)
                        PLC210TurboPumpClient.RequestRun(true);
                    else
                        waits.Add("turbo: " + soft);
                }
            }

            if (step.MWPower.Power_Enable)
            {
                PLC210MicrowaveClient.State microwave = PLC210MicrowaveClient.GetState();
                if (!(microwave.GeneratorAnswering && microwave.MicrowaveOn))
                {
                    string blocker = DescribeMicrowaveHardBlocker(microwave, step.ForeVacPump.Enable, step.Water.Enable);
                    if (blocker != null)
                    {
                        hardStop = "microwave: " + blocker;
                        return null;
                    }

                    if (!microwave.FilamentPreheatDone)
                    {
                        if (!microwave.PreheatOn)
                            PLC210MicrowaveClient.RequestPreheat(true);
                        int left = Math.Max(0, PreheatSeconds - microwave.PreheatElapsedSeconds);
                        waits.Add("preheat " + left.ToString(CultureInfo.InvariantCulture) + " s");
                    }
                    else
                    {
                        if (!_autoMicrowaveRequested)
                        {
                            _autoMicrowaveRequested = true;
                            PLC210MicrowaveClient.RequestMicrowave(true);
                            ProcessLogger.OpTimeLogging(true, "MWPower");
                        }
                        waits.Add("microwave start");
                    }
                }
            }
            else if (step.MWPower.Preheat_Enable)
            {
                PLC210MicrowaveClient.State microwave = PLC210MicrowaveClient.GetState();
                if (!(microwave.GeneratorAnswering && microwave.PreheatOn))
                {
                    string blocker = DescribePreheatHardBlocker(microwave);
                    if (blocker != null)
                    {
                        hardStop = "preheat: " + blocker;
                        return null;
                    }

                    if (!microwave.PreheatOn)
                        PLC210MicrowaveClient.RequestPreheat(true);
                }
            }

            return waits.Count == 0 ? null : string.Join(", ", waits.ToArray());
        }

        private static string DescribeTurboSoftWait(PLC210VacuumClient.State vacuum)
        {
            if (!vacuum.Connected)
                return "vacuum outputs not answering";
            if (!vacuum.ValveOn[TurboBackingValveIndex])
                return "VPV7 to open";
            if (!vacuum.ForeVacPumpOn)
                return "forevacuum pump";

            PLC210PidClient.State pid = PLC210PidClient.GetState();
            if (!pid.Connected || !pid.PlcPressureAvailable)
                return "no chamber pressure reading";
            if (pid.PlcPressureTorr > TurboMaxChamberPressureTorr)
                return "chamber " + pid.PlcPressureTorr.ToString("F1", CultureInfo.InvariantCulture) + " Torr > "
                    + TurboMaxChamberPressureTorr.ToString("0.#", CultureInfo.InvariantCulture) + " Torr";
            return null;
        }

        private void WriteStepRamps()
        {
            StepItemData step = _autoStep;
            double f = (double)_autoStepTicks / _autoStepLength;

            StepItemData.MFCStruct[] gases = StepGases(step);
            for (int i = 0; i < gases.Length && i < _gasSetpoint.Length; i++)
            {
                double value = Ramp(gases[i].InitialValue, gases[i].EndValue, f);
                value = Math.Max(0, Math.Min(PLC210GasFlowClient.FullScaleSccm[i], value));
                gases[i].CalculatedValue = value;
                _gasSetpoint[i].Value = ClampTo(_gasSetpoint[i], value);
                PLC210GasFlowClient.RequestSetpoint(i, value);
            }
            UpdateChamberGasFlow();

            double chamber = Ramp(step.Chamber.InitialValue, step.Chamber.EndValue, f);
            chamber = Math.Max(0, Math.Min((double)MaxRecipeChamberTorr, chamber));
            step.Chamber.CalculatedValue = chamber;
            ChamberPid.Setpoint = chamber;
            ChamberPid.Committed = true;
            ChamberPressureSetPoint.Value = ClampTo(ChamberPressureSetPoint, chamber);

            double kw = Ramp(step.MWPower.IncidentInitialValue, step.MWPower.IncidentEndValue, f);
            kw = Math.Max(PLC210MicrowaveClient.MinSetpointKw, Math.Min(PLC210MicrowaveClient.MaxSetpointKw, kw));
            step.MWPower.IncidentCalculatedValue = kw;
            MWPowerSetPoint.Value = ClampTo(MWPowerSetPoint, kw);
            PLC210MicrowaveClient.RequestSetpoint(kw);
        }

        private static double Ramp(double initial, double end, double fraction)
        {
            if (initial == end)
                return initial;
            return initial + (end - initial) * fraction;
        }

        private static decimal ClampTo(NumericUpDown n, double value)
        {
            decimal d = (decimal)Math.Round(value, 4);
            return Math.Max(n.Minimum, Math.Min(n.Maximum, d));
        }

        private void PushStepThresholds(StepItemData step)
        {
            AlarmSettings.ParamAlarmEnable[AlarmSettings.ParamChamber] = step.Chamber.AlarmEnable;
            AlarmSettings.ParamAbortEnable[AlarmSettings.ParamChamber] = step.Chamber.AbortEnable;
            AlarmSettings.ParamAlarmPct[AlarmSettings.ParamChamber] = step.Chamber.AlarmValue;
            AlarmSettings.ParamAbortPct[AlarmSettings.ParamChamber] = step.Chamber.AbortValue;

            StepItemData.MFCStruct[] gases = StepGases(step);
            for (int i = 0; i < gases.Length; i++)
            {
                int p = AlarmSettings.ParamGasFirst + i;
                AlarmSettings.ParamAlarmEnable[p] = gases[i].AlarmEnable;
                AlarmSettings.ParamAbortEnable[p] = gases[i].AbortEnable;
                AlarmSettings.ParamAlarmPct[p] = (int)Math.Round(gases[i].AlarmValue);
                AlarmSettings.ParamAbortPct[p] = (int)Math.Round(gases[i].AbortValue);
            }

            AlarmSettings.ParamAlarmEnable[AlarmSettings.ParamReflected] = step.MWPower.ReflectedAlarmEnable;
            AlarmSettings.ParamAbortEnable[AlarmSettings.ParamReflected] = step.MWPower.ReflectedAbortEnable;
            AlarmSettings.ReflectedAlarmWatt = (int)Math.Round(step.MWPower.ReflectedAlarmValue);
            AlarmSettings.ReflectedAbortWatt = (int)Math.Round(step.MWPower.ReflectedAbortValue);

            AlarmSettings.SampleAlarmEnable = step.TempSample.AlarmEnable;
            AlarmSettings.SampleTargetC = (int)step.TempSample.SetValue;
            AlarmSettings.SampleAlarmPct = (int)step.TempSample.AlarmValue;

            StepItemData.CheckStruct[] water =
            {
                step.TempInternalH2O, step.TempExternalH2O, step.TempStageH2O, step.TempChamberH2O
            };
            for (int i = 0; i < AlarmSettings.WaterCount; i++)
            {
                AlarmSettings.WaterAlarmEnable[i] = water[i].AlarmEnable;
                AlarmSettings.WaterAbortEnable[i] = water[i].AbortEnable;
                AlarmSettings.WaterTargetC[i] = (int)water[i].SetValue;
                AlarmSettings.WaterAlarmPct[i] = (int)water[i].AlarmValue;
                AlarmSettings.WaterAbortPct[i] = (int)water[i].AbortValue;
            }

            StepItemData.InputCheckStruct[] inputs =
            {
                step.Input_1, step.Input_2, step.Input_3, step.Input_4,
                step.Input_5, step.Input_6, step.Input_9, step.Input_10
            };
            for (int i = 0; i < AlarmSettings.InputCount; i++)
            {
                AlarmSettings.InputEnable[i] = inputs[i].Enable;
                AlarmSettings.InputAborts[i] = inputs[i].Reaction == 1;
            }

            PLC210AlarmClient.PushThresholds(AlarmSettings.Pack());
        }

        private static void RestoreProcessThresholds()
        {
            AlarmSettings.Load();
            PLC210AlarmClient.PushThresholds(AlarmSettings.Pack());
        }

        private void SetAutoPause(bool on)
        {
            _autoPaused = on;
            AutomaticPause.BackColor = on ? Color.IndianRed : SystemColors.Control;

            if (!on && _autoInterlocked)
            {
                _autoInterlocked = false;
                _autoOutputsApplied = false;
                _autoMicrowaveRequested = false;
            }

            ShowAutoProgress();
        }

        private void PauseForInterlock(string blocker)
        {
            _autoInterlocked = true;
            _autoPaused = true;
            AutomaticPause.BackColor = Color.IndianRed;

            string stepName = _autoStep != null ? _autoStep.StepName : "";
            BeginInvoke(new MethodInvoker(delegate
            {
                MessageBox.Show(
                    this,
                    "The recipe is paused at step \"" + stepName + "\":\r\n\r\n" + blocker
                        + "\r\n\r\nThat command was not sent. Fix the cause and press Pause to continue, "
                        + "or Stop the recipe.",
                    "Automatic Mode",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }));
        }

        private void FinishRecipe()
        {
            StopRecipe(ContinueManualMode.Checked, false);
            BeginInvoke(new MethodInvoker(delegate
            {
                MessageBox.Show(this, "Recipe Finished!", "Automatic Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
            }));
        }

        private void StopRecipeAfterAbort()
        {
            if (!_autoActive)
                return;

            StopRecipe(ContinueManualMode.Checked, true);
            BeginInvoke(new MethodInvoker(delegate
            {
                MessageBox.Show(this, "The recipe was stopped by a PLC abort.", "Automatic Mode",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }));
        }

        private void StopRecipe(bool continueManual, bool afterAbort)
        {
            _autoActive = false;
            _autoPaused = false;
            _autoInterlocked = false;
            _autoStepStarted = false;
            _autoWaiting = null;

            RestoreProcessThresholds();

            if (!continueManual && !afterAbort)
                ShutDownAfterRecipe();

            ProcessLogger.CreateLogFileEnded();

            AutomaticRun.Text = "Run";
            AutomaticPause.BackColor = SystemColors.Control;
            CurrentStepName.Text = "...";
            AutoStartTimeValue.Text = IdleTime;
            TimeEndStepValue.Text = IdleTime;
            TimeEndRecipeValue.Text = IdleTime;
            TimeStepDuration.Text = IdleTime;

            if (continueManual)
                StartManualRun();
            else
                ApplyManualRunGate();
        }

        private void ShutDownAfterRecipe()
        {
            PLC210MicrowaveClient.RequestMicrowave(false);
            ProcessLogger.OpTimeLogging(false, "MWPower");
            PLC210MicrowaveClient.RequestPreheat(false);

            for (int i = 0; i < PLC210GasFlowClient.GasNames.Length; i++)
            {
                PLC210GasFlowClient.RequestSetpoint(i, 0);
                _gasSetpoint[i].Value = ClampTo(_gasSetpoint[i], 0);
            }

            CloseAllValves_Click(null, EventArgs.Empty);

            PLC210TurboPumpClient.RequestRun(false);

            PLC210VacuumClient.RequestWaterPump(false);
            _microwaveAutoResetTicks = 0;

            PLC210TurboPumpClient.State turbo = PLC210TurboPumpClient.GetState();
            if (TurboTurning(turbo))
            {
                string speed = turbo.SpeedHz.ToString(CultureInfo.InvariantCulture);
                BeginInvoke(new MethodInvoker(delegate
                {
                    MessageBox.Show(
                        this,
                        "The forevacuum pump was left on: the turbo pump is still turning at " + speed
                            + " Hz.\r\n\r\nSwitch the forevacuum pump off once the turbo pump has stopped.",
                        "Automatic Mode",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }));
            }
            else
            {
                PLC210VacuumClient.RequestForeVacPump(false);
            }
        }

        private void ShowAutoProgress()
        {
            if (!_autoActive || _autoStep == null)
                return;

            string name = _autoStep.StepName;
            if (_autoPaused)
                name += "  (paused)";
            else if (_autoWaiting != null)
                name += "  (waiting: " + _autoWaiting + ")";
            CurrentStepName.Text = name;

            int stepLeft = Math.Max(0, _autoStepLength - _autoStepTicks);
            TimeSpan recipeLeft = TimeSpan.FromSeconds(stepLeft);
            for (int i = _autoStepIndex + 1; i < _recipe.Count; i++)
                recipeLeft += StepSpan(_recipe[i]);

            TimeEndStepValue.Text = TimeSpan.FromSeconds(stepLeft).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
            TimeEndRecipeValue.Text = recipeLeft.ToString(@"dd\.hh\:mm\:ss", CultureInfo.InvariantCulture);
        }
    }
}
