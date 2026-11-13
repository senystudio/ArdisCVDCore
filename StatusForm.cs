using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class StatusForm : Form
    {
        private readonly ListView _list;
        private readonly Timer _timer;

        public StatusForm()
        {
            Text = "Connection Status";
            ClientSize = new Size(680, 260);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;
            Icon = Res.AppIcon;

            _list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false
            };
            _list.Columns.Add("Section", 190);
            _list.Columns.Add("State", 80);
            _list.Columns.Add("Detail", 390);

            Controls.Add(_list);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += Timer_Tick;

            Refresh(SystemStatus.CollectConnections());
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            base.OnFormClosing(e);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            Refresh(SystemStatus.CollectConnections());
        }

        private void Refresh(List<StatusLine> lines)
        {
            _list.BeginUpdate();
            try
            {
                while (_list.Items.Count > lines.Count)
                    _list.Items.RemoveAt(_list.Items.Count - 1);

                for (int i = 0; i < lines.Count; i++)
                {
                    StatusLine line = lines[i];

                    ListViewItem item;
                    if (i < _list.Items.Count)
                        item = _list.Items[i];
                    else
                    {
                        item = new ListViewItem(new[] { "", "", "" });
                        _list.Items.Add(item);
                    }

                    item.SubItems[0].Text = line.Section;
                    item.SubItems[1].Text = line.Level.ToString();
                    item.SubItems[2].Text = line.Text;
                    item.ForeColor = LevelColor(line.Level);
                }
            }
            finally
            {
                _list.EndUpdate();
            }
        }

        internal static Color LevelColor(StatusLevel level)
        {
            switch (level)
            {
                case StatusLevel.Error: return Color.Red;
                case StatusLevel.Warning: return Color.DarkGoldenrod;
                default: return Res.OnGreen;
            }
        }
    }
}
