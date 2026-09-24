using ArdisCVDCore.modules_hw;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class FaultStatusForm : Form
    {
        private const string IniSection = "FaultStatus";

        private static readonly Color AlarmBack = Color.FromArgb(249, 239, 60);
        private static readonly Color AbortBack = Color.FromArgb(255, 32, 104);
        private static readonly Color ClearedFore = Color.FromArgb(110, 110, 110);

        private readonly SplitContainer _split;
        private readonly ListView _current;
        private readonly ListView _history;
        private readonly TextBox _alarmDump;
        private readonly TextBox _abortDump;
        private readonly Button _reset;
        private readonly Button _clearHistory;
        private readonly Timer _timer;

        private ulong _shownAlarm;
        private ulong _shownAbort;
        private bool _currentEverDrawn;
        private int _shownHistory;

        public FaultStatusForm()
        {
            Text = "Fault Status";
            ClientSize = new Size(1105, 358);
            MinimumSize = new Size(640, 300);
            StartPosition = FormStartPosition.Manual;
            Icon = Res.AppIcon;

            _split = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal
            };
            SplitContainer split = _split;

            Label currentCaption = new Label
            {
                Dock = DockStyle.Top,
                Height = 20,
                Text = "Current Errors/Warnings",
                TextAlign = ContentAlignment.MiddleCenter
            };

            _current = BuildList();
            split.Panel1.Controls.Add(_current);
            split.Panel1.Controls.Add(currentCaption);

            _history = BuildList();

            Panel buttons = new Panel { Dock = DockStyle.Top, Height = 27 };

            _reset = new Button
            {
                Text = "reset last warning",
                Width = 130,
                Height = 23,
                Left = 369,
                Top = 1
            };
            _reset.Click += Reset_Click;

            _clearHistory = new Button
            {
                Text = "clear history",
                Width = 90,
                Height = 23,
                Left = 647,
                Top = 1
            };
            _clearHistory.Click += ClearHistory_Click;

            Label historyCaption = new Label
            {
                Text = "Errors/Warnings history",
                AutoSize = true,
                Left = 511,
                Top = 6
            };

            buttons.Controls.Add(historyCaption);
            buttons.Controls.Add(_reset);
            buttons.Controls.Add(_clearHistory);

            Panel dumps = new Panel { Dock = DockStyle.Top, Height = 24 };

            _alarmDump = BuildDump();
            _abortDump = BuildDump();
            _alarmDump.Dock = DockStyle.Left;
            _alarmDump.Width = 552;
            _abortDump.Dock = DockStyle.Fill;

            dumps.Controls.Add(_abortDump);
            dumps.Controls.Add(_alarmDump);

            split.Panel2.Controls.Add(_history);
            split.Panel2.Controls.Add(buttons);
            split.Panel2.Controls.Add(dumps);

            Controls.Add(split);

            _timer = new Timer { Interval = 1000 };
            _timer.Tick += Timer_Tick;

            Refresh(PLC210AlarmClient.GetState());
        }

        private static ListView BuildList()
        {
            ListView list = new ListView
            {
                Dock = DockStyle.Fill,
                View = View.Details,
                FullRowSelect = true,
                GridLines = true,
                HeaderStyle = ColumnHeaderStyle.Nonclickable,
                MultiSelect = false
            };

            list.Columns.Add("Date", 155);
            list.Columns.Add("Error/Warning Code", 210);
            list.Columns.Add("Description", 350);
            list.Columns.Add("Recommendation", 510);
            return list;
        }

        private static TextBox BuildDump()
        {
            return new TextBox
            {
                ReadOnly = true,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font(FontFamily.GenericMonospace, 8.25f)
            };
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            RestoreWindowPlacement();

            int wanted = Math.Min(130, Math.Max(_split.Panel1MinSize,
                _split.Height - _split.Panel2MinSize - _split.SplitterWidth));
            if (wanted > 0)
                _split.SplitterDistance = wanted;

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
            Refresh(PLC210AlarmClient.GetState());
        }

        private void Reset_Click(object sender, EventArgs e)
        {
            PLC210PidClient.RequestReset();
        }

        private void ClearHistory_Click(object sender, EventArgs e)
        {
            AlarmJournal.ClearHistory();
            _history.Items.Clear();
            _shownHistory = 0;
        }

        private void Refresh(PLC210AlarmClient.State state)
        {
            _alarmDump.Text = "Alarm  " + FormatMask(state.AlarmMask);
            _abortDump.Text = "Abort  " + FormatMask(state.AbortOrLatched);

            _reset.Enabled = state.Connected && state.AbortActive;

            RefreshCurrent(state);
            RefreshHistory();
        }

        private void RefreshCurrent(PLC210AlarmClient.State state)
        {
            ulong abort = state.AbortOrLatched;
            if (_currentEverDrawn && state.AlarmMask == _shownAlarm && abort == _shownAbort)
                return;

            _shownAlarm = state.AlarmMask;
            _shownAbort = abort;
            _currentEverDrawn = true;

            _current.BeginUpdate();
            try
            {
                _current.Items.Clear();

                if (!state.Connected)
                    return;

                AddCurrentRows(abort, AlarmLevel.Abort);
                AddCurrentRows(state.AlarmMask & ~abort, AlarmLevel.Alarm);
            }
            finally
            {
                _current.EndUpdate();
            }
        }

        private void AddCurrentRows(ulong mask, AlarmLevel level)
        {
            for (int code = 0; code < AlarmCatalog.CodeCount; code++)
            {
                if ((mask & (1UL << code)) == 0)
                    continue;

                ListViewItem item = new ListViewItem(new[]
                {
                    DateTime.Now.ToString("g", CultureInfo.CurrentCulture),
                    CodeText(code, level),
                    DescriptionOf(code, level),
                    RecommendationOf(code, level)
                });

                PaintRow(item, level, true);
                _current.Items.Add(item);
            }
        }

        private void RefreshHistory()
        {
            List<AlarmEvent> events = AlarmJournal.Snapshot();
            if (events.Count == _shownHistory)
                return;

            if (events.Count < _shownHistory)
            {
                _history.Items.Clear();
                _shownHistory = 0;
            }

            _history.BeginUpdate();
            try
            {
                for (int i = _shownHistory; i < events.Count; i++)
                {
                    AlarmEvent entry = events[i];

                    ListViewItem item = new ListViewItem(new[]
                    {
                        entry.At.ToString("g", CultureInfo.CurrentCulture),
                        CodeText(entry.Code, entry.Level),
                        (entry.Raised ? string.Empty : "cleared: ") + DescriptionOf(entry.Code, entry.Level),
                        entry.Raised ? RecommendationOf(entry.Code, entry.Level) : string.Empty
                    });

                    PaintRow(item, entry.Level, entry.Raised);
                    _history.Items.Add(item);
                }

                _shownHistory = events.Count;

                if (_history.Items.Count > 0)
                    _history.EnsureVisible(_history.Items.Count - 1);
            }
            finally
            {
                _history.EndUpdate();
            }
        }

        private static void PaintRow(ListViewItem item, AlarmLevel level, bool raised)
        {
            if (!raised)
            {
                item.ForeColor = ClearedFore;
                return;
            }

            if (level == AlarmLevel.Abort)
            {
                item.BackColor = AbortBack;
                item.ForeColor = Color.White;
            }
            else
            {
                item.BackColor = AlarmBack;
            }
        }

        private static string CodeText(int code, AlarmLevel level)
        {
            AlarmEntry entry = AlarmCatalog.Get(code);
            string section = entry != null ? entry.Section : "Unassigned";
            return (level == AlarmLevel.Abort ? "Abort " : "Alarm ")
                + code.ToString(CultureInfo.InvariantCulture) + "  " + section;
        }

        private static string DescriptionOf(int code, AlarmLevel level)
        {
            return (level == AlarmLevel.Abort ? "Error : " : "Warning : ") + AlarmCatalog.Describe(code);
        }

        private static string RecommendationOf(int code, AlarmLevel level)
        {
            AlarmEntry entry = AlarmCatalog.Get(code);
            if (entry == null)
                return string.Empty;

            return level == AlarmLevel.Abort ? entry.AbortRecommendation : entry.AlarmRecommendation;
        }

        private static string FormatMask(ulong mask)
        {
            char[] text = new char[80];
            int at = 0;
            for (int i = 0; i < 8; i++)
            {
                byte piece = (byte)(mask >> (i * 8));
                text[at++] = Nibble(piece >> 4);
                text[at++] = Nibble(piece & 0x0F);
                text[at++] = ' ';
            }
            return new string(text, 0, at);
        }

        private static char Nibble(int value)
        {
            return (char)(value < 10 ? '0' + value : 'a' + value - 10);
        }
    }
}
