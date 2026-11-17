using ArdisCVDCore.modules_hw;
using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class RegisterViewerForm : Form
    {
        private static readonly Color ChangedFore = Color.FromArgb(220, 40, 60);
        private static readonly TimeSpan ChangedHold = TimeSpan.FromSeconds(2);

        private readonly string _host;
        private readonly int _port;
        private readonly ListView _list;
        private readonly CheckBox _freeze;
        private readonly Label _status;
        private readonly Timer _timer;
        private readonly ushort?[] _shown = new ushort?[PLC210DebugClient.RegisterTotal];
        private readonly DateTime[] _changedAt = new DateTime[PLC210DebugClient.RegisterTotal];

        public RegisterViewerForm(string host, int port)
        {
            _host = host;
            _port = port;

            Text = "PLC Registers";
            Icon = Res.AppIcon;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = false;
            MinimumSize = new Size(560, 300);
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(600, 640);

            Panel top = new Panel { Dock = DockStyle.Top, Height = 36 };

            _freeze = new CheckBox
            {
                Text = "Freeze",
                AutoSize = true,
                Location = new Point(12, 9)
            };
            top.Controls.Add(_freeze);

            _status = new Label
            {
                AutoSize = false,
                Location = new Point(90, 10),
                Size = new Size(496, 20),
                Anchor = AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right
            };
            top.Controls.Add(_status);

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false,
                HideSelection = false,
                Font = new Font(FontFamily.GenericMonospace, 9F)
            };
            _list.Columns.Add("Reg", 50, HorizontalAlignment.Right);
            _list.Columns.Add("Hex", 70, HorizontalAlignment.Right);
            _list.Columns.Add("Unsigned", 80, HorizontalAlignment.Right);
            _list.Columns.Add("Signed", 80, HorizontalAlignment.Right);
            _list.Columns.Add("Block", 280);

            _list.BeginUpdate();
            for (int i = 0; i < PLC210DebugClient.RegisterTotal; i++)
            {
                _list.Items.Add(new ListViewItem(new[]
                {
                    i.ToString(CultureInfo.InvariantCulture),
                    "-",
                    "-",
                    "-",
                    BlockOf(i)
                }));
            }
            _list.EndUpdate();

            Controls.Add(_list);
            Controls.Add(top);

            _timer = new Timer { Interval = 500 };
            _timer.Tick += (s, e) => RefreshView();

            Theme.Apply(this);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            PLC210DebugClient.Start(_host, _port);
            RefreshView();
            _timer.Start();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _timer.Stop();
            PLC210DebugClient.Stop();
            base.OnFormClosed(e);
        }

        private void RefreshView()
        {
            PLC210DebugClient.State state = PLC210DebugClient.GetState();
            _status.Text = state.Connected
                ? "Updated " + state.UpdatedAt.ToString("HH:mm:ss.f", CultureInfo.InvariantCulture)
                : state.StatusText;

            if (_freeze.Checked || state.Registers == null)
                return;

            DateTime now = DateTime.Now;
            _list.BeginUpdate();
            try
            {
                for (int i = 0; i < state.Registers.Length && i < _list.Items.Count; i++)
                {
                    ushort value = state.Registers[i];
                    ListViewItem item = _list.Items[i];

                    if (_shown[i] != value)
                    {
                        if (_shown[i].HasValue)
                            _changedAt[i] = now;
                        _shown[i] = value;
                        item.SubItems[1].Text = value.ToString("X4", CultureInfo.InvariantCulture);
                        item.SubItems[2].Text = value.ToString(CultureInfo.InvariantCulture);
                        item.SubItems[3].Text = ((short)value).ToString(CultureInfo.InvariantCulture);
                    }

                    Color fore = now - _changedAt[i] < ChangedHold ? ChangedFore : _list.ForeColor;
                    if (item.ForeColor != fore)
                        item.ForeColor = fore;
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        private static string BlockOf(int register)
        {
            if (register <= 63) return "HMI -> PLC: PID, flags";
            if (register <= 99) return "Gas flow";
            if (register <= 132) return "PID outputs";
            if (register <= 134) return "Gas valves";
            if (register <= 138) return "Vacuum outputs";
            if (register == 139) return "PLC discrete inputs";
            if (register <= 147) return "Thyracont";
            if (register == 148) return "Debug command (HMI -> PLC)";
            if (register == 149) return "Debug command echo";
            if (register <= 157) return "Bench levers";
            if (register <= 159) return "Free";
            if (register <= 191) return "Pyrometers";
            if (register <= 205) return "Microwave";
            if (register <= 239) return "Cooling";
            if (register <= 247) return "Turbo pump";
            if (register <= 279) return "Alarm thresholds (HMI -> PLC)";
            return "Alarm state";
        }
    }
}
