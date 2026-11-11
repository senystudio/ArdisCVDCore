using ArdisCVDCore.modules_hw;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public partial class RecipeEditor : Form
    {
        public const string RecipeFolderName = "recipes";
        public static bool AppExiting;

        public List<StepItemData> StepArray = new List<StepItemData>();
        static public StepItemData Step = new StepItemData();
        static public StepItemData CopyStep = new StepItemData();

        String sFileName = "";
        TimeSpan RecipeSpan;
        public DateTime StartTime;

        bool bIsModified;
        bool bIsFileName;
        bool bIsCopyStepValues;

        public static string RecipeFolder
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, RecipeFolderName); }
        }

        private CheckBox MWPreheat_CheckBox;

        public RecipeEditor()
        {
            InitializeComponent();
            Icon = Res.AppIcon;
            Step = new StepItemData();
            AdaptToMachine();
        }

        private void AdaptToMachine()
        {
            foreach (Control c in new Control[]
            {
                label34, label28, AR_Init, AR_End, AR_AlarmEn, AR_AlarmVal, AR_AbortEn, AR_AbortVal,
                label64, label63, H22_Init, H22_End, H22_AlarmEn, H22_AlarmVal, H22_AbortEn, H22_AbortVal,
                Valve_5, Valve_6, label75, GCFAR
            })
                c.Visible = true;

            foreach (Control c in new Control[]
            {
                label26, label24, BiasU_Init, BiasU_End,
                label25, label23, BiasI_Init, BiasI_End,
                DC_Enable_CheckBox,
                label52, label57, label58, label65, Tuner_leftPos, Tuner_rightPos, Autotune_En
            })
                c.Visible = false;

            NumericUpDown[] gasInit = { H2_Init, CH4_Init, N2_Init, O2_Init, AR_Init, H22_Init };
            NumericUpDown[] gasEnd = { H2_End, CH4_End, N2_End, O2_End, AR_End, H22_End };
            int[] gasDecimals = { 0, 0, 0, 2, 2, 2 };
            for (int i = 0; i < gasInit.Length; i++)
            {
                foreach (NumericUpDown n in new[] { gasInit[i], gasEnd[i] })
                {
                    n.Minimum = 0;
                    n.Maximum = (decimal)PLC210GasFlowClient.FullScaleSccm[i];
                    n.DecimalPlaces = gasDecimals[i];
                }
            }

            ChamberPressure_Init.Maximum = MainForm.MaxRecipeChamberTorr;
            ChamberPressure_End.Maximum = MainForm.MaxRecipeChamberTorr;

            MWPreheat_CheckBox = new CheckBox
            {
                Name = "MWPreheat_CheckBox",
                Text = "MW Preheat",
                AutoSize = true,
                CheckAlign = MWPower_CheckBox.CheckAlign,
                Font = MWPower_CheckBox.Font,
                UseVisualStyleBackColor = true
            };
            MWPreheat_CheckBox.Leave += UpdateStepSettings;
            LogicOutputGroupBox.Controls.Add(MWPreheat_CheckBox);
            MWPreheat_CheckBox.Location = new System.Drawing.Point(
                MWPower_CheckBox.Right - MWPreheat_CheckBox.PreferredSize.Width,
                DC_Enable_CheckBox.Top);

            label21.Text = "kW";
            foreach (NumericUpDown n in new[] { MWInc_Init, MWInc_End })
            {
                n.Minimum = 0;
                n.Maximum = (decimal)PLC210MicrowaveClient.MaxSetpointKw;
                n.DecimalPlaces = 2;
                n.Increment = 0.1m;
            }
        }

        private static void SetValue(NumericUpDown n, decimal value)
        {
            n.Value = Math.Max(n.Minimum, Math.Min(n.Maximum, value));
        }

        private static void SetValue(NumericUpDown n, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                value = 0;
            decimal d = value > (double)decimal.MaxValue ? decimal.MaxValue
                : value < (double)decimal.MinValue ? decimal.MinValue
                : (decimal)value;
            SetValue(n, d);
        }

        private static int ComboNumber(ComboBox combo, int max)
        {
            int value;
            if (!int.TryParse(combo.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
                value = Math.Max(0, combo.SelectedIndex);
            return Math.Max(0, Math.Min(max, value));
        }

        private static void SetIndex(ComboBox combo, int index)
        {
            combo.SelectedIndex = Math.Max(0, Math.Min(combo.Items.Count - 1, index));
        }

        private static OpenFileDialog CreateOpenDialog()
        {
            Directory.CreateDirectory(RecipeFolder);
            return new OpenFileDialog
            {
                Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*",
                Title = "Open a recipe",
                InitialDirectory = RecipeFolder
            };
        }

        private void RecipeEditor_Load(object sender, EventArgs e)
        {
            NewStepName.Enabled = false;
            StepItemlistBox.Enabled = false;
            InsertStepButton.Enabled = false;
            DeleteStepButton.Enabled = false;
            NewStepName.Enabled = false;
            RenameStepButton.Enabled = false;
            StepDurationGroupBox.Enabled = false;

            ParametersGroup.Enabled = false;
            TemperatureGroupBox.Enabled = false;
            LogicOutputGroupBox.Enabled = false;
            Input_groupBox.Enabled = false;
            Comments.Enabled = false;

            bIsFileName = false;
            bIsModified = false;
            bIsCopyStepValues = false;
            CopyStepValues.Enabled = false;
            PasteStepValues.Enabled = false;
            GCF_groupBox.Enabled = false;
            this.Text = "Recipe Editor   ";
        }

        private void newToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StepArray.Clear();
            StepItemlistBox.Items.Clear();
            NewStepName.Enabled = false;
            StepItemlistBox.Enabled = true;
            InsertStepButton.Enabled = true;
            DeleteStepButton.Enabled = true;
            NewStepName.Enabled = false;
            RenameStepButton.Enabled = true;

            StageFlow_comboBox.SelectedIndex = 0;
            ChamberFlow_comboBox.SelectedIndex = 0;
            MWHeadFlow_comboBox.SelectedIndex = 0;
            MWPowerFlow_comboBox.SelectedIndex = 0;
            InternalFlow_comboBox.SelectedIndex = 0;
            ExternalFlow_comboBox.SelectedIndex = 0;
            PS2_comboBox.SelectedIndex = 0;
            PS1_comboBox.SelectedIndex = 0;
            PressureSwitch_comboBox.SelectedIndex = 0;
            ChamberOpen_comboBox.SelectedIndex = 0;

            StageFlow_CheckBox.Checked = false;
            ChamberFlow_CheckBox.Checked = false;
            MWHeadFlow_CheckBox.Checked = false;
            MWPowerFlow_CheckBox.Checked = false;
            InternalFlow_CheckBox.Checked = false;
            ExternalFlow_CheckBox.Checked = false;
            PS2_CheckBox.Checked = false;
            PS1_CheckBox.Checked = false;
            PressureSwitch_CheckBox.Checked = false;
            ChamberOpen_CheckBox.Checked = false;
            Autotune_En.Checked = false;
            ChamberPID_DirectMode_En.Checked = false;

            for (int i = 0; i < 10; i++)
            {
                Step = NewStep(i.ToString() + " Step");
                StepItemlistBox.Items.Add(Step.StepName);
                StepArray.Add(Step);
            }
            StartTime = DateTime.Now;
            CalculateTotalTimeSpan();
            StepItemlistBox.SetSelected(0, true);
            StepItemlistBox.Focus();
            StepDurationGroupBox.Enabled = true;
            ParametersGroup.Enabled = true;
            TemperatureGroupBox.Enabled = true;
            LogicOutputGroupBox.Enabled = true;
            Input_groupBox.Enabled = true;
            CopyStepValues.Enabled = true;
            Comments.Enabled = true;
            GCF_groupBox.Enabled = true;
            CopyStepValues.Enabled = true;
            PasteStepValues.Enabled = true;

            this.Text = "Recipe Editor   ";
            string format = "dd MMM yyyy HH:mm ";
            string commText = "The recipe was created on " + StartTime.ToString(format, new CultureInfo("en-US"));
            Comments.Text = commText;
            StepArray[0].StepComments = commText;
            bIsModified = true;
            bIsFileName = false;
        }

        private static StepItemData NewStep(string name)
        {
            StepItemData step = new StepItemData();
            step.GCF.H2 = 1.0;
            step.GCF.N2 = 1.0;
            step.GCF.CH4 = 1.0;
            step.GCF.O2 = 1.0;
            step.GCF.AR = 1.0;
            step.GCF.H2_2 = 1.0;
            step.StepName = name;
            return step;
        }

        private void loadToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<StepItemData> loaded;
            using (OpenFileDialog openFileDialogRecipe = CreateOpenDialog())
            {
                if (openFileDialogRecipe.ShowDialog() != DialogResult.OK)
                    return;

                try
                {
                    Cursor.Current = Cursors.WaitCursor;
                    loaded = (List<StepItemData>)DataSerializer.Deserialization(openFileDialogRecipe.FileName);
                }
                catch (Exception ex)
                {
                    Cursor.Current = Cursors.Default;
                    MessageBox.Show(this, "Can't read the recipe:\r\n\r\n" + ex.Message, "Recipe Editor",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                Cursor.Current = Cursors.Default;

                if (loaded == null || loaded.Count == 0)
                {
                    MessageBox.Show(this, "The recipe has no steps.", "Recipe Editor",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                sFileName = openFileDialogRecipe.FileName;
            }

            StepArray = loaded;
            StepItemlistBox.Items.Clear();
            foreach (var s in StepArray)
                StepItemlistBox.Items.Add(s.StepName);

            bIsFileName = true;

            NewStepName.Enabled = false;
            StepItemlistBox.Enabled = true;
            InsertStepButton.Enabled = true;
            DeleteStepButton.Enabled = true;
            NewStepName.Enabled = false;
            RenameStepButton.Enabled = true;

            StepDurationGroupBox.Enabled = true;
            ParametersGroup.Enabled = true;
            TemperatureGroupBox.Enabled = true;
            LogicOutputGroupBox.Enabled = true;
            Input_groupBox.Enabled = true;
            Comments.Enabled = true;
            GCF_groupBox.Enabled = true;

            StepItemlistBox.SetSelected(0, true);
            StepItemlistBox.Focus();
            CopyStepValues.Enabled = true;
            bIsModified = true;
            this.Text = "Recipe Editor   " + sFileName;
            if (StepItemlistBox.SelectedIndex == 0)
                NewStepName.Text = StepArray[0].StepName;
        }

        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (!bIsFileName)
            {
                sFileName = saveFileDialog1.FileName;
                if (sFileName != "")
                {
                    UpdateStepSettings(null, null);
                    saveRecipe(sFileName);
                }
                else
                {
                    MessageBox.Show("file name is not defined. Use \"Save As\" ");
                }
            }
            else
            {
                if (sFileName != "")
                {
                    UpdateStepSettings(null, null);
                    saveRecipe(sFileName);
                }
                else
                {
                    MessageBox.Show("file name is not defined");
                }
            }
        }

        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (StepArray == null || StepArray.Count == 0)
            {
                MessageBox.Show("No data to save. Load recipe or create new one");
                return;
            }
            UpdateStepSettings(null, null);
            Directory.CreateDirectory(RecipeFolder);
            saveFileDialog1.Filter = "xml files (*.xml)|*.xml|All files (*.*)|*.*";
            if (string.IsNullOrEmpty(saveFileDialog1.InitialDirectory))
                saveFileDialog1.InitialDirectory = RecipeFolder;
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                sFileName = saveFileDialog1.FileName;
                bIsFileName = true;
                saveRecipe(sFileName);
            }
        }

        private void saveRecipe(string fname)
        {
            try
            {
                DataSerializer.Serialization(StepArray, fname);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Can't save the recipe:\r\n\r\n" + ex.Message, "Recipe Editor",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(sFileName + " is Saved");
            this.Text = "Recipe Editor   " + fname;
            bIsModified = false;
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (bIsModified) { MessageBox.Show("Recipe Modified!"); return; }
            Close();
        }

        private void StepItemlistBox_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (StepItemlistBox.SelectedIndex == -1) return;
            Step = StepArray[StepItemlistBox.SelectedIndex];
            CalculateTotalTimeSpan();
            UpdateSettings(StepItemlistBox.SelectedIndex);
            NewStepName.Text = StepArray[StepItemlistBox.SelectedIndex].StepName;
        }

        private void InsertStepButton_Click(object sender, EventArgs e)
        {
            if (StepItemlistBox.SelectedIndex < 0)
            {
                MessageBox.Show("Not selected Item");
                return;
            }
            if (InsertStepButton.Text != "Insert")
            {
                StepItemlistBox.Enabled = false;
                InsertStepButton.Enabled = true;
                DeleteStepButton.Enabled = false;
                NewStepName.Enabled = true;
                RenameStepButton.Enabled = false;
                NewStepName.Text = (StepItemlistBox.SelectedIndex + 1).ToString() + " Step";
                InsertStepButton.Text = "Insert";
            }
            else
            {
                int index = StepItemlistBox.SelectedIndex;
                Step = NewStep(NewStepName.Text);
                StepArray.Insert(index + 1, Step);
                StepItemlistBox.Items.Insert(index + 1, NewStepName.Text);

                StepItemlistBox.Enabled = true;
                InsertStepButton.Enabled = true;
                DeleteStepButton.Enabled = true;
                RenameStepButton.Enabled = true;
                NewStepName.Enabled = false;
                InsertStepButton.Text = "Insert After";
                bIsModified = true;
            }
        }

        private void RenameStep_Click(object sender, EventArgs e)
        {
            if (StepItemlistBox.SelectedIndex < 0)
            {
                MessageBox.Show("Not selected Item");
                return;
            }
            if (RenameStepButton.Text == "Rename Step")
            {
                StepItemlistBox.Enabled = false;
                InsertStepButton.Enabled = false;
                DeleteStepButton.Enabled = false;
                NewStepName.Enabled = true;
                NewStepName.Text = StepItemlistBox.SelectedItem.ToString();
                RenameStepButton.Text = "Rename";
            }
            else
            {
                int index = StepItemlistBox.SelectedIndex;
                Step.StepName = NewStepName.Text;
                StepArray[index] = Step;
                StepItemlistBox.Items[index] = NewStepName.Text;

                StepItemlistBox.Enabled = true;
                InsertStepButton.Enabled = true;
                DeleteStepButton.Enabled = true;
                NewStepName.Enabled = false;
                RenameStepButton.Text = "Rename Step";
                bIsModified = true;
            }
        }

        private void DeleteStep_Click(object sender, EventArgs e)
        {
            if (StepItemlistBox.SelectedIndex < 0)
            {
                MessageBox.Show("Not selected Item");
                return;
            }
            if (StepArray.Count <= 1)
            {
                MessageBox.Show("A recipe needs at least one step.");
                return;
            }
            DialogResult result = MessageBox.Show("Delete Step?", "Attention!",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (result == DialogResult.Yes)
            {
                int index = StepItemlistBox.SelectedIndex;
                string comments = StepArray[0].StepComments;
                StepArray.RemoveAt(index);
                StepArray[0].StepComments = comments;
                StepItemlistBox.Items.RemoveAt(index);
                StepItemlistBox.SelectedIndex = Math.Min(index, StepItemlistBox.Items.Count - 1);
                CalculateTotalTimeSpan();
                bIsModified = true;
            }
        }

        void UpdateSettings(int CurrentStep)
        {
            Comments.Text = StepArray[0].StepComments;
            SetValue(HourTextBox, (decimal)(int)Step.StepDuration.TotalHours);
            SetIndex(Minutes_ComboBox, Step.StepDuration.Minutes);
            SetIndex(Seconds_ComboBox, Step.StepDuration.Seconds);

            SetValue(H2_Init, Step.H2.InitialValue);
            SetValue(H2_End, Step.H2.EndValue);
            H2_AlarmEn.Checked = Step.H2.AlarmEnable;
            H2_AbortEn.Checked = Step.H2.AbortEnable;
            SetValue(H2_AlarmVal, Step.H2.AlarmValue);
            SetValue(H2_AbortVal, Step.H2.AbortValue);

            SetValue(H22_Init, Step.H2_2.InitialValue);
            SetValue(H22_End, Step.H2_2.EndValue);
            H22_AlarmEn.Checked = Step.H2_2.AlarmEnable;
            H22_AbortEn.Checked = Step.H2_2.AbortEnable;
            SetValue(H22_AlarmVal, Step.H2_2.AlarmValue);
            SetValue(H22_AbortVal, Step.H2_2.AbortValue);

            SetValue(CH4_Init, Step.CH4.InitialValue);
            SetValue(CH4_End, Step.CH4.EndValue);
            CH4_AlarmEn.Checked = Step.CH4.AlarmEnable;
            CH4_AbortEn.Checked = Step.CH4.AbortEnable;
            SetValue(CH4_AlarmVal, Step.CH4.AlarmValue);
            SetValue(CH4_AbortVal, Step.CH4.AbortValue);

            SetValue(O2_Init, Step.O2.InitialValue);
            SetValue(O2_End, Step.O2.EndValue);
            O2_AlarmEn.Checked = Step.O2.AlarmEnable;
            O2_AbortEn.Checked = Step.O2.AbortEnable;
            SetValue(O2_AlarmVal, Step.O2.AlarmValue);
            SetValue(O2_AbortVal, Step.O2.AbortValue);

            SetValue(N2_Init, Step.N2.InitialValue);
            SetValue(N2_End, Step.N2.EndValue);
            N2_AlarmEn.Checked = Step.N2.AlarmEnable;
            N2_AbortEn.Checked = Step.N2.AbortEnable;
            SetValue(N2_AlarmVal, Step.N2.AlarmValue);
            SetValue(N2_AbortVal, Step.N2.AbortValue);

            SetValue(AR_Init, Step.AR.InitialValue);
            SetValue(AR_End, Step.AR.EndValue);
            AR_AlarmEn.Checked = Step.AR.AlarmEnable;
            AR_AbortEn.Checked = Step.AR.AbortEnable;
            SetValue(AR_AlarmVal, Step.AR.AlarmValue);
            SetValue(AR_AbortVal, Step.AR.AbortValue);

            SetValue(ChamberPressure_Init, Step.Chamber.InitialValue);
            SetValue(ChamberPressure_End, Step.Chamber.EndValue);
            ChamberPressure_AlarmEn.Checked = Step.Chamber.AlarmEnable;
            ChamberPressure_AbortEn.Checked = Step.Chamber.AbortEnable;
            SetValue(ChamberPressure_AlarmVal, (decimal)Step.Chamber.AlarmValue);
            SetValue(ChamberPressure_AbortVal, (decimal)Step.Chamber.AbortValue);

            SetValue(PlenumPressure_Init, Step.Plenum.InitialValue);
            SetValue(PlenumPressure_End, Step.Plenum.EndValue);
            PlenumPressure_AlarmEn.Checked = Step.Plenum.AlarmEnable;
            PlenumPressure_AbortEn.Checked = Step.Plenum.AbortEnable;
            SetValue(PlenumPressure_AlarmVal, (decimal)Step.Plenum.AlarmValue);
            SetValue(PlenumPressure_AbortVal, (decimal)Step.Plenum.AbortValue);

            SetValue(MWInc_Init, Step.MWPower.IncidentInitialValue);
            SetValue(MWInc_End, Step.MWPower.IncidentEndValue);

            MWRef_AlarmEn.Checked = Step.MWPower.ReflectedAlarmEnable;
            MWRef_AbortEn.Checked = Step.MWPower.ReflectedAbortEnable;
            SetValue(MWRef_AlarmVal, Step.MWPower.ReflectedAlarmValue);
            SetValue(MWRef_AbortVal, Step.MWPower.ReflectedAbortValue);

            SetValue(BiasU_Init, Step.DCVoltage.InitialValue);
            SetValue(BiasU_End, Step.DCVoltage.EndValue);

            SetValue(BiasI_Init, Step.DCCurrent.InitialValue);
            SetValue(BiasI_End, Step.DCCurrent.EndValue);

            SetValue(Rod_Init, Step.ZPos.InitialValue);
            SetValue(Rod_End, Step.ZPos.EndValue);
            SetValue(Rod_Speed, (decimal)Step.ZPos.Speed);

            SetValue(TSampleCenter, Step.TempSample.SetValue);
            TCenter_AlarmEnable.Checked = Step.TempSample.AlarmEnable;
            TCenter_AbortEnable.Checked = Step.TempSample.AbortEnable;
            SetValue(TSampleCenter_AlarmVal, Step.TempSample.AlarmValue);
            SetValue(TSampleCenter_AbortVal, Step.TempSample.AbortValue);

            SetValue(TInternalH2O, Step.TempInternalH2O.SetValue);
            TInternalH2O_AlarmEnable.Checked = Step.TempInternalH2O.AlarmEnable;
            TInternalH2O_AbortEnable.Checked = Step.TempInternalH2O.AbortEnable;
            SetValue(TInternalH2O_AlarmVal, Step.TempInternalH2O.AlarmValue);
            SetValue(TInternalH2O_AbortVal, Step.TempInternalH2O.AbortValue);

            SetValue(TExternalH2O, Step.TempExternalH2O.SetValue);
            TExternalH2O_AlarmEnable.Checked = Step.TempExternalH2O.AlarmEnable;
            TExternalH2O_AbortEnable.Checked = Step.TempExternalH2O.AbortEnable;
            SetValue(TExternalH2O_AlarmVal, Step.TempExternalH2O.AlarmValue);
            SetValue(TExternalH2O_AbortVal, Step.TempExternalH2O.AbortValue);

            SetValue(TStageH2O, Step.TempStageH2O.SetValue);
            TStageH2O_AlarmEnable.Checked = Step.TempStageH2O.AlarmEnable;
            TStageH2O_AbortEnable.Checked = Step.TempStageH2O.AbortEnable;
            SetValue(TStageH2O_AlarmVal, Step.TempStageH2O.AlarmValue);
            SetValue(TStageH2O_AbortVal, Step.TempStageH2O.AbortValue);

            SetValue(TChamberH2O, Step.TempChamberH2O.SetValue);
            TChamberH2O_AlarmEnable.Checked = Step.TempChamberH2O.AlarmEnable;
            TChamberH2O_AbortEnable.Checked = Step.TempChamberH2O.AbortEnable;
            SetValue(TChamberH2O_AlarmVal, Step.TempChamberH2O.AlarmValue);
            SetValue(TChamberH2O_AbortVal, Step.TempChamberH2O.AbortValue);

            Valve_1.Checked = Step.bDO[0];
            Valve_2.Checked = Step.bDO[1];
            Valve_3.Checked = Step.bDO[2];
            Valve_4.Checked = Step.bDO[3];
            Valve_5.Checked = Step.bDO[4];
            Valve_6.Checked = Step.bDO[5];
            Valve_7.Checked = Step.bDO[6];
            Valve_8.Checked = Step.bDO[7];

            Valve_9.Checked = Step.bDO_1[0];
            Valve_10.Checked = Step.bDO_1[1];
            Valve_11.Checked = Step.bDO_1[2];
            Valve_12.Checked = Step.bDO_1[3];
            Valve_13.Checked = Step.bDO_1[4];
            Valve_14.Checked = Step.bDO_1[5];
            Valve_15.Checked = Step.bDO_1[6];
            Valve_16.Checked = Step.bDO_1[7];

            Valve_17.Checked = Step.bDO_2[0];
            Valve_18.Checked = Step.bDO_2[1];
            Valve_19.Checked = Step.bDO_2[2];
            Valve_20.Checked = Step.bDO_2[3];
            Valve_21.Checked = Step.bDO_2[4];
            Valve_22.Checked = Step.bDO_2[5] & !Step.bDO_2[6];
            Valve_23.Checked = Step.bDO_2[7];

            BasePump_CheckBox.Checked = Step.ForeVacPump.Enable;
            TurboPump_CheckBox.Checked = Step.HiVacPump.Enable;
            H2OPump_CheckBox.Checked = Step.Water.Enable;

            Pause_CheckBox.Checked = Step.bStepPause;

            MWPower_CheckBox.Checked = Step.MWPower.Power_Enable;
            MWPreheat_CheckBox.Checked = Step.MWPower.Preheat_Enable;
            DC_Enable_CheckBox.Checked = Step.DCVoltage.Power_Enable;

            StageFlow_CheckBox.Checked = Step.Input_1.Enable;
            ChamberFlow_CheckBox.Checked = Step.Input_2.Enable;
            MWHeadFlow_CheckBox.Checked = Step.Input_3.Enable;
            MWPowerFlow_CheckBox.Checked = Step.Input_4.Enable;
            InternalFlow_CheckBox.Checked = Step.Input_5.Enable;
            ExternalFlow_CheckBox.Checked = Step.Input_6.Enable;
            PS2_CheckBox.Checked = Step.Input_7.Enable;
            PS1_CheckBox.Checked = Step.Input_8.Enable;
            PressureSwitch_CheckBox.Checked = Step.Input_9.Enable;
            ChamberOpen_CheckBox.Checked = Step.Input_10.Enable;
            RodLine_CheckBox.Checked = Step.Input_11.Enable;

            SetIndex(StageFlow_comboBox, (int)Step.Input_1.Reaction);
            SetIndex(ChamberFlow_comboBox, (int)Step.Input_2.Reaction);
            SetIndex(MWHeadFlow_comboBox, (int)Step.Input_3.Reaction);
            SetIndex(MWPowerFlow_comboBox, (int)Step.Input_4.Reaction);
            SetIndex(InternalFlow_comboBox, (int)Step.Input_5.Reaction);
            SetIndex(ExternalFlow_comboBox, (int)Step.Input_6.Reaction);
            SetIndex(PS2_comboBox, (int)Step.Input_7.Reaction);
            SetIndex(PS1_comboBox, (int)Step.Input_8.Reaction);
            SetIndex(PressureSwitch_comboBox, (int)Step.Input_9.Reaction);
            SetIndex(ChamberOpen_comboBox, (int)Step.Input_10.Reaction);
            SetIndex(RodLine_comboBox, (int)Step.Input_11.Reaction);

            SetValue(GCFH2, Step.GCF.H2);
            SetValue(GCFN2, Step.GCF.N2);
            SetValue(GCFCH4, Step.GCF.CH4);
            SetValue(GCFO2, Step.GCF.O2);
            SetValue(GCFAR, Step.GCF.AR);
            SetValue(GCFH22, Step.GCF.H2_2);

            if (Step.Bubbler.SetTemperatureAutomode < 10) Step.Bubbler.SetTemperatureAutomode = 10;
            SetValue(BubblerTemperature, Step.Bubbler.SetTemperatureAutomode);
            SetValue(BubblerPressure, Step.Bubbler.SetPressureAutomode);

            SetValue(Tuner_leftPos, Step.Stub1.AutomodeStepPosition);
            SetValue(Tuner_rightPos, Step.Stub2.AutomodeStepPosition);
            Autotune_En.Checked = Step.Stub1.AutomodeAutotuneOn | Step.Stub2.AutomodeAutotuneOn;

            SetValue(ChamberPID_DirectMode_Value, (decimal)Step.Chamber.DirectPIDValue);
            ChamberPID_DirectMode_En.Checked = Step.Chamber.DirectPIDEnable;
        }

        private void UpdateStepSettings(object sender, EventArgs e)
        {
            if (StepArray == null || StepArray.Count == 0 || Step == null)
                return;

            bIsModified = true;
            StepArray[0].StepComments = Comments.Text;
            Step.StepDuration = new TimeSpan(
                (int)HourTextBox.Value,
                ComboNumber(Minutes_ComboBox, 59),
                ComboNumber(Seconds_ComboBox, 59));

            Step.H2.InitialValue = Convert.ToDouble(H2_Init.Value);
            Step.H2.EndValue = Convert.ToDouble(H2_End.Value);
            Step.H2.AlarmEnable = H2_AlarmEn.Checked;
            Step.H2.AbortEnable = H2_AbortEn.Checked;
            Step.H2.AlarmValue = Convert.ToInt16(H2_AlarmVal.Value);
            Step.H2.AbortValue = Convert.ToInt16(H2_AbortVal.Value);

            Step.H2_2.InitialValue = Convert.ToDouble(H22_Init.Value);
            Step.H2_2.EndValue = Convert.ToDouble(H22_End.Value);
            Step.H2_2.AlarmEnable = H22_AlarmEn.Checked;
            Step.H2_2.AbortEnable = H22_AbortEn.Checked;
            Step.H2_2.AlarmValue = Convert.ToInt16(H22_AlarmVal.Value);
            Step.H2_2.AbortValue = Convert.ToInt16(H22_AbortVal.Value);

            Step.CH4.InitialValue = Convert.ToDouble(CH4_Init.Value);
            Step.CH4.EndValue = Convert.ToDouble(CH4_End.Value);
            Step.CH4.AlarmEnable = CH4_AlarmEn.Checked;
            Step.CH4.AbortEnable = CH4_AbortEn.Checked;
            Step.CH4.AlarmValue = Convert.ToInt16(CH4_AlarmVal.Value);
            Step.CH4.AbortValue = Convert.ToInt16(CH4_AbortVal.Value);

            Step.O2.InitialValue = Convert.ToDouble(O2_Init.Value);
            Step.O2.EndValue = Convert.ToDouble(O2_End.Value);
            Step.O2.AlarmEnable = O2_AlarmEn.Checked;
            Step.O2.AbortEnable = O2_AbortEn.Checked;
            Step.O2.AlarmValue = Convert.ToInt16(O2_AlarmVal.Value);
            Step.O2.AbortValue = Convert.ToInt16(O2_AbortVal.Value);

            Step.N2.InitialValue = Convert.ToDouble(N2_Init.Value);
            Step.N2.EndValue = Convert.ToDouble(N2_End.Value);
            Step.N2.AlarmEnable = N2_AlarmEn.Checked;
            Step.N2.AbortEnable = N2_AbortEn.Checked;
            Step.N2.AlarmValue = Convert.ToInt16(N2_AlarmVal.Value);
            Step.N2.AbortValue = Convert.ToInt16(N2_AbortVal.Value);

            Step.AR.InitialValue = Convert.ToDouble(AR_Init.Value);
            Step.AR.EndValue = Convert.ToDouble(AR_End.Value);
            Step.AR.AlarmEnable = AR_AlarmEn.Checked;
            Step.AR.AbortEnable = AR_AbortEn.Checked;
            Step.AR.AlarmValue = Convert.ToInt16(AR_AlarmVal.Value);
            Step.AR.AbortValue = Convert.ToInt16(AR_AbortVal.Value);

            Step.Chamber.InitialValue = Convert.ToDouble(ChamberPressure_Init.Value);
            Step.Chamber.EndValue = Convert.ToDouble(ChamberPressure_End.Value);
            Step.Chamber.AlarmEnable = ChamberPressure_AlarmEn.Checked;
            Step.Chamber.AbortEnable = ChamberPressure_AbortEn.Checked;
            Step.Chamber.AlarmValue = Convert.ToInt16(ChamberPressure_AlarmVal.Value);
            Step.Chamber.AbortValue = Convert.ToInt16(ChamberPressure_AbortVal.Value);

            Step.Plenum.InitialValue = Convert.ToDouble(PlenumPressure_Init.Value);
            Step.Plenum.EndValue = Convert.ToDouble(PlenumPressure_End.Value);
            Step.Plenum.AlarmEnable = PlenumPressure_AlarmEn.Checked;
            Step.Plenum.AbortEnable = PlenumPressure_AbortEn.Checked;
            Step.Plenum.AlarmValue = Convert.ToInt16(PlenumPressure_AlarmVal.Value);
            Step.Plenum.AbortValue = Convert.ToInt16(PlenumPressure_AbortVal.Value);

            Step.MWPower.IncidentInitialValue = Convert.ToDouble(MWInc_Init.Value);
            Step.MWPower.IncidentEndValue = Convert.ToDouble(MWInc_End.Value);

            Step.MWPower.ReflectedAlarmEnable = MWRef_AlarmEn.Checked;
            Step.MWPower.ReflectedAbortEnable = MWRef_AbortEn.Checked;
            Step.MWPower.ReflectedAlarmValue = Convert.ToInt16(MWRef_AlarmVal.Value);
            Step.MWPower.ReflectedAbortValue = Convert.ToInt16(MWRef_AbortVal.Value);

            Step.DCVoltage.InitialValue = Convert.ToDouble(BiasU_Init.Value);
            Step.DCVoltage.EndValue = Convert.ToDouble(BiasU_End.Value);

            Step.DCCurrent.InitialValue = Convert.ToDouble(BiasI_Init.Value);
            Step.DCCurrent.EndValue = Convert.ToDouble(BiasI_End.Value);

            Step.TempSample.SetValue = TSampleCenter.Value;
            Step.TempSample.AlarmEnable = TCenter_AlarmEnable.Checked;
            Step.TempSample.AbortEnable = TCenter_AbortEnable.Checked;
            Step.TempSample.AlarmValue = TSampleCenter_AlarmVal.Value;
            Step.TempSample.AbortValue = TSampleCenter_AbortVal.Value;

            Step.TempInternalH2O.SetValue = TInternalH2O.Value;
            Step.TempInternalH2O.AlarmEnable = TInternalH2O_AlarmEnable.Checked;
            Step.TempInternalH2O.AbortEnable = TInternalH2O_AbortEnable.Checked;
            Step.TempInternalH2O.AlarmValue = TInternalH2O_AlarmVal.Value;
            Step.TempInternalH2O.AbortValue = TInternalH2O_AbortVal.Value;

            Step.TempExternalH2O.SetValue = TExternalH2O.Value;
            Step.TempExternalH2O.AlarmEnable = TExternalH2O_AlarmEnable.Checked;
            Step.TempExternalH2O.AbortEnable = TExternalH2O_AbortEnable.Checked;
            Step.TempExternalH2O.AlarmValue = TExternalH2O_AlarmVal.Value;
            Step.TempExternalH2O.AbortValue = TExternalH2O_AbortVal.Value;

            Step.TempStageH2O.SetValue = TStageH2O.Value;
            Step.TempStageH2O.AlarmEnable = TStageH2O_AlarmEnable.Checked;
            Step.TempStageH2O.AbortEnable = TStageH2O_AbortEnable.Checked;
            Step.TempStageH2O.AlarmValue = TStageH2O_AlarmVal.Value;
            Step.TempStageH2O.AbortValue = TStageH2O_AbortVal.Value;

            Step.TempChamberH2O.SetValue = TChamberH2O.Value;
            Step.TempChamberH2O.AlarmEnable = TChamberH2O_AlarmEnable.Checked;
            Step.TempChamberH2O.AbortEnable = TChamberH2O_AbortEnable.Checked;
            Step.TempChamberH2O.AlarmValue = TChamberH2O_AlarmVal.Value;
            Step.TempChamberH2O.AbortValue = TChamberH2O_AbortVal.Value;

            Step.ZPos.InitialValue = Convert.ToDouble(Rod_Init.Value);
            Step.ZPos.EndValue = Convert.ToDouble(Rod_End.Value);
            Step.ZPos.Speed = (int)Convert.ToDouble(Rod_Speed.Value);

            Step.GCF.H2 = Convert.ToDouble(GCFH2.Value);
            Step.GCF.N2 = Convert.ToDouble(GCFN2.Value);
            Step.GCF.CH4 = Convert.ToDouble(GCFCH4.Value);
            Step.GCF.O2 = Convert.ToDouble(GCFO2.Value);
            Step.GCF.AR = Convert.ToDouble(GCFAR.Value);
            Step.GCF.H2_2 = Convert.ToDouble(GCFH22.Value);

            Step.bDO[0] = Valve_1.Checked;
            Step.bDO[1] = Valve_2.Checked;
            Step.bDO[2] = Valve_3.Checked;
            Step.bDO[3] = Valve_4.Checked;
            Step.bDO[4] = Valve_5.Checked;
            Step.bDO[5] = Valve_6.Checked;
            Step.bDO[6] = Valve_7.Checked;
            Step.bDO[7] = Valve_8.Checked;

            Step.bDO_1[0] = Valve_9.Checked;
            Step.bDO_1[1] = Valve_10.Checked;
            Step.bDO_1[2] = Valve_11.Checked;
            Step.bDO_1[3] = Valve_12.Checked;
            Step.bDO_1[4] = Valve_13.Checked;
            Step.bDO_1[5] = Valve_14.Checked;
            Step.bDO_1[6] = Valve_15.Checked;
            Step.bDO_1[7] = Valve_16.Checked;

            Step.bDO_2[0] = Valve_17.Checked;
            Step.bDO_2[1] = Valve_18.Checked;
            Step.bDO_2[2] = Valve_19.Checked;
            Step.bDO_2[3] = Valve_20.Checked;
            Step.bDO_2[4] = Valve_21.Checked;
            Step.bDO_2[5] = Valve_22.Checked;
            Step.bDO_2[6] = !Valve_22.Checked;
            Step.bDO_2[7] = Valve_23.Checked;

            Step.ForeVacPump.Enable = BasePump_CheckBox.Checked;
            Step.HiVacPump.Enable = TurboPump_CheckBox.Checked;
            Step.Water.Enable = H2OPump_CheckBox.Checked;

            Step.MWPower.Power_Enable = MWPower_CheckBox.Checked;
            Step.MWPower.Preheat_Enable = MWPreheat_CheckBox.Checked;
            Step.DCVoltage.Power_Enable = DC_Enable_CheckBox.Checked;

            Step.Input_1.Enable = StageFlow_CheckBox.Checked;
            Step.Input_2.Enable = ChamberFlow_CheckBox.Checked;
            Step.Input_3.Enable = MWHeadFlow_CheckBox.Checked;
            Step.Input_4.Enable = MWPowerFlow_CheckBox.Checked;
            Step.Input_5.Enable = InternalFlow_CheckBox.Checked;
            Step.Input_6.Enable = ExternalFlow_CheckBox.Checked;
            Step.Input_7.Enable = PS2_CheckBox.Checked;
            Step.Input_8.Enable = PS1_CheckBox.Checked;
            Step.Input_9.Enable = PressureSwitch_CheckBox.Checked;
            Step.Input_10.Enable = ChamberOpen_CheckBox.Checked;
            Step.Input_11.Enable = RodLine_CheckBox.Checked;

            Step.Input_1.Reaction = StageFlow_comboBox.SelectedIndex;
            Step.Input_2.Reaction = ChamberFlow_comboBox.SelectedIndex;
            Step.Input_3.Reaction = MWHeadFlow_comboBox.SelectedIndex;
            Step.Input_4.Reaction = MWPowerFlow_comboBox.SelectedIndex;
            Step.Input_5.Reaction = InternalFlow_comboBox.SelectedIndex;
            Step.Input_6.Reaction = ExternalFlow_comboBox.SelectedIndex;
            Step.Input_7.Reaction = PS2_comboBox.SelectedIndex;
            Step.Input_8.Reaction = PS1_comboBox.SelectedIndex;
            Step.Input_9.Reaction = PressureSwitch_comboBox.SelectedIndex;
            Step.Input_10.Reaction = ChamberOpen_comboBox.SelectedIndex;
            Step.Input_11.Reaction = RodLine_comboBox.SelectedIndex;

            Step.Stub1.AutomodeStepPosition = Convert.ToDouble(Tuner_leftPos.Value);
            Step.Stub2.AutomodeStepPosition = Convert.ToDouble(Tuner_rightPos.Value);
            Step.Stub1.AutomodeAutotuneOn = Autotune_En.Checked;
            Step.Stub2.AutomodeAutotuneOn = Autotune_En.Checked;
            Step.Chamber.DirectPIDEnable = ChamberPID_DirectMode_En.Checked;
            Step.Chamber.DirectPIDValue = Convert.ToInt32(ChamberPID_DirectMode_Value.Value);

            Step.Bubbler.SetPressureAutomode = Convert.ToDouble(BubblerPressure.Value);
            Step.Bubbler.SetTemperatureAutomode = Convert.ToDouble(BubblerTemperature.Value);

            Step.bStepPause = Pause_CheckBox.Checked;
        }

        private void CalculateTotalTimeSpan()
        {
            TimeSpan TotalTimeSpan = new TimeSpan(0, 0, 0);
            for (int index = 0; index < StepArray.Count; index++)
                TotalTimeSpan += StepArray[index].StepDuration;
            RecipeSpan = TotalTimeSpan;
            TotalRecipeSpan.Text = TotalTimeSpan.ToString();
        }

        private void LeaveTimeCombo(object sender, EventArgs e)
        {
            UpdateStepSettings(null, null);
            CalculateTotalTimeSpan();
        }

        private void CopyStepValues_Click(object sender, EventArgs e)
        {
            UpdateStepSettings(null, null);
            CopyStep = (StepItemData)Step.Clone();
            bIsCopyStepValues = true;
            PasteStepValues.Enabled = true;
        }

        private void PasteStepValues_Click(object sender, EventArgs e)
        {
            if (!bIsCopyStepValues || StepItemlistBox.SelectedIndex < 0)
                return;

            string stepname = Step.StepName;
            string comments = StepArray[0].StepComments;
            Step = (StepItemData)CopyStep.Clone();
            Step.StepName = stepname;
            int ArrayIndex = StepItemlistBox.SelectedIndex;
            StepArray[ArrayIndex] = Step;
            StepArray[0].StepComments = comments;

            UpdateSettings(StepItemlistBox.SelectedIndex);
            UpdateStepSettings(null, null);
            CalculateTotalTimeSpan();
        }

        private void StepItemlistBox_Enter(object sender, EventArgs e)
        {
            if (StepItemlistBox.SelectedIndex < 0 && StepItemlistBox.Items.Count > 0)
                StepItemlistBox.SelectedIndex = 0;
        }

        private void RecipeEditor_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!AppExiting && e.CloseReason == CloseReason.UserClosing)
            {
                DialogResult dialogResult = MessageBox.Show("Do you want to Exit?", "Question", MessageBoxButtons.YesNo);
                if (dialogResult == DialogResult.No) { e.Cancel = true; return; }
            }
        }

        private void Autotune_En_CheckedChanged(object sender, EventArgs e)
        {
            Tuner_leftPos.Enabled = !Autotune_En.Checked;
            Tuner_rightPos.Enabled = !Autotune_En.Checked;
        }

        private void aboutToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            AboutForm form = new AboutForm();
            form.Show();
        }
    }
}
