using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    /// <summary>
    /// The per-section fault text behind the Status plate on the main window.
    /// </summary>
    /// <remarks>
    /// The section windows used to print these next to their own controls. The
    /// controls are all on one screen now, so the detail moved here and the main
    /// window only carries the OK / Warning / Error verdict.
    /// </remarks>
    public class StatusForm : Form
    {
        private const string IniSection = "StatusWindow";

        private readonly ListView _list;
        private readonly Timer _timer;

        public StatusForm()
        {
            Text = "System Status";
            ClientSize = new Size(680, 260);
            StartPosition = FormStartPosition.Manual;
            MinimumSize = new Size(420, 200);
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

            Refresh(SystemStatus.Collect());
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowPlacement();
            _timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _timer.Stop();
            IniWriter.INI.Write(IniSection, "X", Location.X.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(IniSection, "Y", Location.Y.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(IniSection, "Width", Width.ToString(CultureInfo.InvariantCulture));
            IniWriter.INI.Write(IniSection, "Height", Height.ToString(CultureInfo.InvariantCulture));
            base.OnFormClosing(e);
        }

        private void RestoreWindowPlacement()
        {
            if (IniWriter.INI.KeyExists("X", IniSection) && IniWriter.INI.KeyExists("Y", IniSection))
                Location = new Point(
                    int.Parse(IniWriter.INI.ReadINI(IniSection, "X")),
                    int.Parse(IniWriter.INI.ReadINI(IniSection, "Y")));

            if (IniWriter.INI.KeyExists("Width", IniSection) && IniWriter.INI.KeyExists("Height", IniSection))
                Size = new Size(
                    int.Parse(IniWriter.INI.ReadINI(IniSection, "Width")),
                    int.Parse(IniWriter.INI.ReadINI(IniSection, "Height")));

            if (Location.X < 0 || Location.Y < 0)
                Location = new Point(0, 0);
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            Refresh(SystemStatus.Collect());
        }

        // Rows are rewritten in place rather than cleared and rebuilt: a full
        // rebuild once a second makes the list flicker and drops the scroll
        // position while the operator is reading it.
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
                // Not Color.Yellow: unreadable on the default white list
                // background. Same hue, dark enough to read.
                case StatusLevel.Warning: return Color.DarkGoldenrod;
                default: return Color.Green;
            }
        }
    }
}
