using ArdisCVDCore.modules_hw;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class SystemSettingsForm : Form
    {
        private static readonly Color ErrorFore = Color.FromArgb(220, 40, 60);
        private static readonly TimeSpan EchoTimeout = TimeSpan.FromSeconds(3);

        private sealed class DeviceRow
        {
            public string Name;
            public string Bus;
            public Func<string> Probe;
            public Action Reconnect;
            public ListViewItem Item;
        }

        private readonly MainForm _main;
        private readonly List<DeviceRow> _rows = new List<DeviceRow>();
        private readonly ListView _devices;
        private readonly Button _connect;
        private readonly Button _disconnect;
        private readonly Button _startLogging;
        private readonly Button _stopLogging;
        private readonly Button _reconnectSelected;
        private readonly Label _status;
        private readonly Timer _timer;
        private RegisterViewerForm _registers;

        public SystemSettingsForm(MainForm main)
        {
            _main = main;

            Text = "System Settings";
            Icon = Res.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(600, 560);

            GroupBox hmi = new GroupBox
            {
                Text = "HMI",
                Location = new Point(12, 8),
                Size = new Size(576, 64)
            };
            _connect = AddButton(hmi, "Connect", 12, 24, 120, (s, e) => _main.ConnectPlc());
            _disconnect = AddButton(hmi, "Disconnect", 140, 24, 120, Disconnect_Click);
            _startLogging = AddButton(hmi, "Start logging", 300, 24, 120, (s, e) => _main.StartLogging());
            _stopLogging = AddButton(hmi, "Stop logging", 428, 24, 120, (s, e) => _main.StopLogging());

            GroupBox modules = new GroupBox
            {
                Text = "PLC modules",
                Location = new Point(12, 80),
                Size = new Size(576, 276)
            };

            _devices = new ListView
            {
                Location = new Point(12, 22),
                Size = new Size(552, 206),
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false,
                HideSelection = false
            };
            _devices.Columns.Add("Device", 170);
            _devices.Columns.Add("Bus", 190);
            _devices.Columns.Add("State", 170);
            _devices.SelectedIndexChanged += (s, e) => UpdateButtons();
            modules.Controls.Add(_devices);

            _reconnectSelected = AddButton(modules, "Reconnect selected", 12, 236, 150, ReconnectSelected_Click);
            AddButton(modules, "Reconnect all", 170, 236, 120, ReconnectAll_Click);

            GroupBox resets = new GroupBox
            {
                Text = "Resets",
                Location = new Point(12, 364),
                Size = new Size(576, 100)
            };
            AddButton(resets, "Reset alarms", 12, 24, 150, (s, e) => SystemStatus.ResetFaults());
            AddButton(resets, "Reset chamber PID", 170, 24, 150, ResetPid_Click);
            AddButton(resets, "Reinit gas regulators", 328, 24, 170, ReinitGas_Click);
            AddButton(resets, "Soft restart", 12, 60, 150, SoftRestart_Click);
            AddButton(resets, "Reboot PLC", 170, 60, 150, RebootPlc_Click);

            AddButton(this, "Registers...", 12, 476, 120, (s, e) => OpenRegisters());
            Button close = AddButton(this, "Close", 468, 476, 120, (s, e) => Close());
            CancelButton = close;

            _status = new Label
            {
                Location = new Point(12, 514),
                Size = new Size(576, 40),
                TextAlign = ContentAlignment.TopLeft
            };

            Controls.Add(hmi);
            Controls.Add(modules);
            Controls.Add(resets);
            Controls.Add(_status);

            BuildRows();

            _timer = new Timer { Interval = 500 };
            _timer.Tick += (s, e) => RefreshView();

            Theme.Apply(this);
        }

        private static Button AddButton(Control parent, string text, int x, int y, int width, EventHandler click)
        {
            Button button = new Button
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(width, 28)
            };
            button.Click += click;
            parent.Controls.Add(button);
            return button;
        }

        private void BuildRows()
        {
            for (int i = 0; i < PLC210DebugClient.MfcDevices.Length; i++)
            {
                int index = i;
                string name = PLC210DebugClient.MfcDevices[i];
                AddRow(name, "PLC RS-485 port 1", () => ProbeMfc(index),
                    () => SendCommand((byte)(PLC210DebugClient.CmdResetMfcFirst + index), "Reconnect " + name));
            }

            AddRow("PYRO_RXT", "MKON gateway", ProbePyrometer, PLC210PyrometerClient.RequestRxtReconnect);
            AddRow("MW_GEN", "MKON gateway", ProbeMicrowave, PLC210MicrowaveClient.RequestGeneratorReconnect);
            AddRow("TURBO_PUMP", "-", ProbeTurboPump, null);
            AddRow("Thyracont", "PLC RS-485 port 2", ProbeThyracont, null);
            AddRow("Cooling module U8", "Ethernet 192.168.1.13", () => ProbeCooling(true), null);
            AddRow("Cooling module U9", "Ethernet 192.168.1.11", () => ProbeCooling(false), null);
        }

        private void AddRow(string name, string bus, Func<string> probe, Action reconnect)
        {
            DeviceRow row = new DeviceRow
            {
                Name = name,
                Bus = bus,
                Probe = probe,
                Reconnect = reconnect,
                Item = new ListViewItem(new[] { name, bus, string.Empty })
            };
            row.Item.Tag = row;
            _rows.Add(row);
            _devices.Items.Add(row.Item);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PLC210DebugClient.Start(_main.PlcHost, _main.PlcPort);
            RefreshView();
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            if (_registers != null && !_registers.IsDisposed)
                _registers.Close();
            PLC210DebugClient.Stop();
            base.OnFormClosed(e);
        }

        private void OpenRegisters()
        {
            if (_registers == null || _registers.IsDisposed)
            {
                _registers = new RegisterViewerForm(_main.PlcHost, _main.PlcPort)
                {
                    StartPosition = FormStartPosition.Manual,
                    Location = new Point(Right + 8, Top)
                };
                _registers.Show(this);
                return;
            }

            if (_registers.WindowState == FormWindowState.Minimized)
                _registers.WindowState = FormWindowState.Normal;
            _registers.Activate();
        }

        private void RefreshView()
        {
            _connect.Enabled = !_main.PlcLinkStarted;
            _disconnect.Enabled = _main.PlcLinkStarted;
            _startLogging.Enabled = !_main.LoggingActive;
            _stopLogging.Enabled = _main.LoggingActive;

            foreach (DeviceRow row in _rows)
            {
                string state = row.Probe();
                if (row.Item.SubItems[2].Text != state)
                    row.Item.SubItems[2].Text = state;

                Color fore = state == "OK" ? _devices.ForeColor : ErrorFore;
                if (row.Item.ForeColor != fore)
                    row.Item.ForeColor = fore;
            }

            UpdateButtons();
            _status.Text = DescribeDebugLink(PLC210DebugClient.GetState());
        }

        private void UpdateButtons()
        {
            DeviceRow row = SelectedRow();
            _reconnectSelected.Enabled = row != null && row.Reconnect != null;
        }

        private DeviceRow SelectedRow()
        {
            return _devices.SelectedItems.Count == 1 ? _devices.SelectedItems[0].Tag as DeviceRow : null;
        }

        private static string DescribeDebugLink(PLC210DebugClient.State state)
        {
            string text = state.StatusText;
            if (string.IsNullOrEmpty(state.LastCommandName))
                return text;

            string result;
            if (state.LastCommandDone)
                result = "done";
            else if (DateTime.Now - state.LastCommandAt > EchoTimeout)
                result = "no answer from the PLC (PLC program without PRG_Debug?)";
            else
                result = "waiting";

            return text + "\r\nLast command: " + state.LastCommandName
                + " at " + state.LastCommandAt.ToString("HH:mm:ss") + " - " + result;
        }

        private static void SendCommand(byte code, string name)
        {
            PLC210DebugClient.Send(code, name);
        }

        private void Disconnect_Click(object sender, EventArgs e)
        {
            Cursor = Cursors.WaitCursor;
            try
            {
                _main.DisconnectPlc();
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void ReconnectSelected_Click(object sender, EventArgs e)
        {
            DeviceRow row = SelectedRow();
            if (row != null && row.Reconnect != null)
                row.Reconnect();
        }

        private void ReconnectAll_Click(object sender, EventArgs e)
        {
            SendCommand(PLC210DebugClient.CmdResetAllMfc, "Reconnect all MFC");
            PLC210PyrometerClient.RequestRxtReconnect();
            PLC210MicrowaveClient.RequestGeneratorReconnect();
        }

        private void ResetPid_Click(object sender, EventArgs e)
        {
            if (Confirm("Reset the chamber pressure PID?\r\n\r\nThe PID output drops to its lower limit."))
                PLC210PidClient.RequestReset();
        }

        private void ReinitGas_Click(object sender, EventArgs e)
        {
            if (Confirm("Re-initialize all gas regulators?\r\n\r\nThe PLC writes ramp and control mode to every regulating MFC again."))
                SendCommand(PLC210DebugClient.CmdReinitGas, "Reinit gas regulators");
        }

        private void SoftRestart_Click(object sender, EventArgs e)
        {
            if (!Confirm("Soft restart?\r\n\r\nReconnects every module, re-initializes the gas regulators, "
                + "resets the chamber PID (output drops to its lower limit) and resets alarms. The PLC itself keeps running."))
                return;

            ReconnectAll_Click(sender, e);
            SendCommand(PLC210DebugClient.CmdReinitGas, "Reinit gas regulators");
            PLC210PidClient.RequestReset();
            SystemStatus.ResetFaults();
        }

        private void RebootPlc_Click(object sender, EventArgs e)
        {
            if (!Confirm("Reboot the PLC?\r\n\r\nAll PLC outputs switch off: gas, valves, pumps, microwave. "
                + "The HMI loses the PLC for about a minute until the controller starts again."))
                return;

            if (Confirm("Are you sure? This stops the running process."))
                SendCommand(PLC210DebugClient.CmdReboot, "Reboot PLC");
        }

        private bool Confirm(string text)
        {
            return MessageBox.Show(this, text, Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        private static string ProbeMfc(int index)
        {
            PLC210GasFlowClient.State state = PLC210GasFlowClient.GetState();
            if (!state.Connected)
                return "No PLC link";
            return (state.DiagSlaveErrorMask & (1 << index)) != 0 ? "Error" : "OK";
        }

        private static string ProbePyrometer()
        {
            PLC210PyrometerClient.State state = PLC210PyrometerClient.GetState();
            if (!state.Connected)
                return "No PLC link";
            return state.RxtAnswering ? "OK" : "Error";
        }

        private static string ProbeMicrowave()
        {
            PLC210MicrowaveClient.State state = PLC210MicrowaveClient.GetState();
            if (!state.Connected)
                return "No PLC link";
            return state.GeneratorAnswering ? "OK" : "Error";
        }

        private static string ProbeTurboPump()
        {
            PLC210TurboPumpClient.State state = PLC210TurboPumpClient.GetState();
            if (!state.Connected)
                return "No PLC link";
            return state.DriveAnswering ? "OK" : "Error";
        }

        private static string ProbeThyracont()
        {
            PLC210ThyracontClient.State state = PLC210ThyracontClient.GetState();
            if (!state.Enabled)
                return "Disabled";
            if (!state.Connected)
                return "No PLC link";
            return state.HasValidValue ? "OK" : "Error";
        }

        private static string ProbeCooling(bool first)
        {
            PLC210CoolingClient.State state = PLC210CoolingClient.GetState();
            if (!state.Connected)
                return "No PLC link";

            bool any = false;
            int from = first ? 0 : 4;
            int to = first ? 4 : PLC210CoolingClient.CircuitCount;
            for (int i = from; i < to; i++)
                any |= state.TempValid[i] || state.FlowValid[i];
            if (!first)
                any |= state.WaterPressureValid || state.CdaPressureValid;

            return any ? "OK" : "Error";
        }
    }
}
