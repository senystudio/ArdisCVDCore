using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArdisCVDCore.trends
{
    public class TrendDock : Panel
    {
        private const int SplitterSize = 6;
        private const int MinSlotSize = 80;
        private const int HeaderHeight = 26;
        private const double DropOpacity = 0.6;
        private const double HintOpacity = 0.35;
        private const int AnimationInterval = 15;
        private const float AnimationStep = 0.35f;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private sealed class BarInfo
        {
            public LayoutNode Split;
            public bool Convert;
            public LayoutNode First;
            public LayoutNode Second;
            public Rectangle FirstBounds;
            public Rectangle SecondBounds;
            public Rectangle Bounds;
            public bool Vertical;
        }

        private sealed class DropHint : Form
        {
            private const int WS_EX_TRANSPARENT = 0x20;
            private const int WS_EX_TOOLWINDOW = 0x80;
            private const int WS_EX_NOACTIVATE = 0x08000000;

            public DropHint()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Opacity = HintOpacity;
                BackColor = Theme.Current.Accent;
            }

            protected override bool ShowWithoutActivation
            {
                get { return true; }
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams parameters = base.CreateParams;
                    parameters.ExStyle |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
                    return parameters;
                }
            }
        }

        private readonly Dictionary<string, TrendForm> _docked = new Dictionary<string, TrendForm>();
        private readonly Dictionary<string, Panel> _slots = new Dictionary<string, Panel>();
        private readonly Dictionary<string, Panel> _headers = new Dictionary<string, Panel>();
        private readonly Dictionary<string, Button> _cycleButtons = new Dictionary<string, Button>();
        private readonly Dictionary<TrendForm, Form> _floating = new Dictionary<TrendForm, Form>();
        private readonly List<string> _order = new List<string>();
        private readonly List<Panel> _bars = new List<Panel>();
        private readonly List<BarInfo> _barInfo = new List<BarInfo>();
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly Timer _animation;
        private TrendLayout _layout = new TrendLayout();
        private TrendArrange _arrange = TrendArrange.Vertical;
        private bool _loneFirst;
        private bool _arranging;
        private DropHint _hint;
        private string _hintFor;
        private Panel _dragBar;
        private int _barGrab;
        private int _barStart;
        private TrendForm _dragForm;
        private Point _dragStart;
        private Point _dragGrab;
        private bool _dragging;
        private string _dropKey;

        public TrendDock()
        {
            _animation = new Timer { Interval = AnimationInterval };
            _animation.Tick += Animation_Tick;
            Theme.Changed += (s, e) => RepaintTheme();
        }

        public event EventHandler TrendsChanged;

        public event EventHandler LayoutChanged;

        public Func<Rectangle> DropZone { get; set; }

        public bool HasTrends
        {
            get { return _docked.Count > 0; }
        }

        public TrendArrange Arrange
        {
            get { return _arrange; }
        }

        public string OrderText
        {
            get { return string.Join(",", _order); }
        }

        public bool LoneFirst
        {
            get { return _loneFirst; }
        }

        public void LoadLayout(TrendArrange arrange, bool loneFirst, string order)
        {
            if (_docked.Count > 0 || _floating.Count > 0)
                return;

            _arrange = arrange;
            _loneFirst = loneFirst;
            _order.Clear();
            foreach (string part in (order ?? "").Split(','))
            {
                string key = part.Trim();
                if (key.Length > 0 && !_order.Contains(key))
                    _order.Add(key);
            }
        }

        public void ShowTrend(TrendForm form)
        {
            if (_docked.ContainsValue(form) || _floating.ContainsKey(form))
                return;

            string key = KeyOf(form);
            if (_docked.ContainsKey(key))
                return;

            form.Disposed += (s, e) => CloseTrend(form);
            AddDocked(form);
            if (!_order.Contains(key))
                _order.Add(key);
            Rebuild();
            ArrangeSlots();
            form.Show();

            OnTrendsChanged();
            OnLayoutChanged();
        }

        public void CloseTrend(TrendForm form)
        {
            if (Disposing || IsDisposed)
                return;

            Form host;
            if (_floating.TryGetValue(form, out host))
            {
                _floating.Remove(form);
                if (!host.IsDisposed && !host.Disposing)
                    host.Dispose();
                OnTrendsChanged();
                return;
            }

            string key = KeyOf(form);
            TrendForm docked;
            if (!_docked.TryGetValue(key, out docked) || docked != form)
                return;

            if (_dragForm == form)
                CancelDrag();

            Panel slot = _slots[key];
            RemoveDocked(key);
            slot.Dispose();

            Rebuild();
            ArrangeSlots();
            OnTrendsChanged();
            OnLayoutChanged();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_dragBar == null && !_animation.Enabled)
                ArrangeSlots();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _animation.Dispose();
                _toolTip.Dispose();
                if (_hint != null)
                    _hint.Dispose();
            }
            base.Dispose(disposing);
        }

        private static string KeyOf(TrendForm form)
        {
            return form.GetType().Name;
        }

        private List<string> VisibleOrder(List<string> order, string extra)
        {
            List<string> keys = new List<string>();
            foreach (string key in order)
                if (_docked.ContainsKey(key) || key == extra)
                    keys.Add(key);
            return keys;
        }

        private void Rebuild()
        {
            _layout = TrendLayout.Build(VisibleOrder(_order, null), _arrange, _loneFirst);
        }

        private void SyncOrder()
        {
            List<string> reading = _layout.ReadingOrder();
            int next = 0;
            for (int i = 0; i < _order.Count && next < reading.Count; i++)
                if (_docked.ContainsKey(_order[i]))
                    _order[i] = reading[next++];
        }

        private static bool PlanDrop(TrendLayout layout, string key, string target)
        {
            if (layout.IsLone(key))
            {
                layout.FlipRows();
                return true;
            }

            layout.Swap(key, target);
            return false;
        }

        private static void MoveInOrder(List<string> order, string key, string before)
        {
            order.Remove(key);
            int index = before != null ? order.IndexOf(before) : -1;
            if (index < 0)
                order.Add(key);
            else
                order.Insert(index, key);
        }

        private void AddDocked(TrendForm form)
        {
            string key = KeyOf(form);
            Panel slot = new Panel { BorderStyle = BorderStyle.FixedSingle };

            Panel header = new Panel { Dock = DockStyle.Top, Height = HeaderHeight };

            Label title = new Label
            {
                Dock = DockStyle.Fill,
                Text = form.Text,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(6, 0, 0, 0),
                Font = new Font("Segoe UI", 9.75F, FontStyle.Bold)
            };
            title.MouseDown += (s, e) => Header_MouseDown(form, e);
            title.MouseMove += (s, e) => Header_MouseMove(form);
            title.MouseUp += (s, e) => FinishHeaderDrag(true);
            title.MouseCaptureChanged += (s, e) => FinishHeaderDrag(false);

            Button cycle = new Button
            {
                Dock = DockStyle.Right,
                Width = HeaderHeight + 6,
                Text = "",
                Font = new Font("Segoe MDL2 Assets", 9F),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand,
                Visible = false
            };
            cycle.FlatAppearance.BorderSize = 0;
            cycle.Click += (s, e) => CycleArrange();
            _toolTip.SetToolTip(cycle, "Change Layout");

            Button close = new Button
            {
                Dock = DockStyle.Right,
                Width = HeaderHeight + 6,
                Text = "",
                Font = new Font("Segoe MDL2 Assets", 9F),
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand
            };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = Res.CloseHot;
            close.FlatAppearance.MouseDownBackColor = Res.ClosePressed;
            close.MouseEnter += (s, e) => close.ForeColor = Color.White;
            close.MouseLeave += (s, e) => PaintHeader(key, _dragging && form == _dragForm);
            close.Click += (s, e) => BeginInvoke(new Action(() => CloseTrend(form)));

            header.Controls.Add(title);
            header.Controls.Add(cycle);
            header.Controls.Add(close);

            slot.Controls.Add(form);
            slot.Controls.Add(header);

            _docked.Add(key, form);
            _slots.Add(key, slot);
            _headers.Add(key, header);
            _cycleButtons.Add(key, cycle);
            PaintHeader(key, false);

            Controls.Add(slot);
        }

        private void RemoveDocked(string key)
        {
            Panel slot = _slots[key];

            _docked.Remove(key);
            _slots.Remove(key);
            _headers.Remove(key);
            _cycleButtons.Remove(key);

            Controls.Remove(slot);
        }

        private void RepaintTheme()
        {
            foreach (string key in _headers.Keys)
                PaintHeader(key, false);
            foreach (Panel bar in _bars)
                bar.BackColor = Theme.Current.Splitter;
            if (_hint != null)
                _hint.BackColor = Theme.Current.Accent;
        }

        private void PaintHeader(string key, bool lifted)
        {
            Panel header;
            if (!_headers.TryGetValue(key, out header))
                return;

            header.BackColor = lifted ? Theme.Current.Accent : Theme.Current.Inset;
            Color text = lifted ? Theme.Current.AccentText : Theme.Current.Text;
            foreach (Control control in header.Controls)
                control.ForeColor = text;

            Button cycle;
            if (_cycleButtons.TryGetValue(key, out cycle))
            {
                cycle.FlatAppearance.MouseOverBackColor = Theme.Current.ToolbarHover;
                cycle.FlatAppearance.MouseDownBackColor = Theme.Current.ToolbarPressed;
            }
        }

        private void CycleArrange()
        {
            if (_docked.Count < 2)
                return;

            _arrange = TrendLayout.Next(_arrange, _docked.Count);
            Rebuild();
            Animate();
            OnLayoutChanged();
        }

        private Rectangle CurrentDropZone()
        {
            Func<Rectangle> zone = DropZone;
            return zone != null ? zone() : RectangleToScreen(ClientRectangle);
        }

        private void Header_MouseDown(TrendForm form, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _dragForm != null)
                return;

            _dragForm = form;
            _dragStart = Cursor.Position;
            _dragGrab = e.Location;
            _dragging = false;
            _dropKey = null;
        }

        private void Header_MouseMove(TrendForm form)
        {
            if (_dragForm != form)
                return;

            Point cursor = Cursor.Position;
            if (!_dragging)
            {
                Size slop = SystemInformation.DragSize;
                if (Math.Abs(cursor.X - _dragStart.X) < slop.Width && Math.Abs(cursor.Y - _dragStart.Y) < slop.Height)
                    return;
                _dragging = true;
                PaintHeader(KeyOf(form), true);
            }

            if (!CurrentDropZone().Contains(cursor))
            {
                Point grab = _dragGrab;
                CancelDrag();
                BeginInvoke(new Action(() => Detach(form, grab)));
                return;
            }

            string key = KeyOf(form);
            _dropKey = GraphAt(cursor, key);
            if (_dropKey == null)
            {
                HideHint();
                return;
            }

            TrendLayout preview = _layout.Clone();
            PlanDrop(preview, key, _dropKey);
            Rectangle bounds;
            ShowHint(ComputeSlots(preview, null).TryGetValue(key, out bounds) ? bounds : Rectangle.Empty, _dropKey);
        }

        private void FinishHeaderDrag(bool commit)
        {
            if (_dragForm == null)
                return;

            TrendForm form = _dragForm;
            bool dragging = _dragging;
            string target = _dropKey;

            _dragForm = null;
            _dragging = false;
            _dropKey = null;
            HideHint();

            if (!dragging)
                return;

            string key = KeyOf(form);
            PaintHeader(key, false);

            if (commit && target != null && _docked.ContainsKey(key) && _docked.ContainsKey(target))
                SwapGraphs(key, target);
        }

        private void CancelDrag()
        {
            TrendForm form = _dragForm;
            bool dragging = _dragging;

            _dragForm = null;
            _dragging = false;
            _dropKey = null;
            HideHint();

            if (form != null && dragging)
                PaintHeader(KeyOf(form), false);
        }

        private void SwapGraphs(string key, string target)
        {
            if (PlanDrop(_layout, key, target))
                _loneFirst = !_loneFirst;
            SyncOrder();

            Animate();
            OnLayoutChanged();
        }

        private string GraphAt(Point screen, string except)
        {
            Point local = PointToClient(screen);
            foreach (KeyValuePair<string, Panel> pair in _slots)
                if (pair.Key != except && pair.Value.Visible && pair.Value.Bounds.Contains(local))
                    return pair.Key;
            return null;
        }

        private void ShowHint(Rectangle bounds, string marker)
        {
            if (bounds.IsEmpty)
            {
                HideHint();
                return;
            }

            Rectangle screen = RectangleToScreen(bounds);
            if (_hint != null && !_hint.IsDisposed && _hint.Visible && _hintFor == marker && _hint.Bounds == screen)
                return;

            if (_hint == null || _hint.IsDisposed)
                _hint = new DropHint();

            _hintFor = marker;
            _hint.BackColor = Theme.Current.Accent;
            _hint.Bounds = screen;
            if (!_hint.Visible)
                _hint.Show(FindForm());
        }

        private void HideHint()
        {
            _hintFor = null;
            if (_hint != null && !_hint.IsDisposed && _hint.Visible)
                _hint.Hide();
        }

        private Rectangle RedockBounds(string key, string before)
        {
            List<string> order = new List<string>(_order);
            MoveInOrder(order, key, before);
            TrendLayout preview = TrendLayout.Build(VisibleOrder(order, key), _arrange, _loneFirst);

            Rectangle bounds;
            return ComputeSlots(preview, null).TryGetValue(key, out bounds) ? bounds : Rectangle.Empty;
        }

        private void Animate()
        {
            Dictionary<string, Rectangle> targets = ComputeSlots(_layout, null);

            bool differs = false;
            foreach (KeyValuePair<string, Rectangle> pair in targets)
            {
                Panel slot;
                if (_slots.TryGetValue(pair.Key, out slot) && slot.Bounds != pair.Value)
                    differs = true;
            }

            if (!differs)
            {
                ArrangeSlots();
                return;
            }

            _animation.Start();
            foreach (Panel bar in _bars.ToArray())
                bar.Visible = false;
        }

        private void Animation_Tick(object sender, EventArgs e)
        {
            Dictionary<string, Rectangle> targets = ComputeSlots(_layout, null);

            bool moving = false;
            SuspendLayout();
            foreach (KeyValuePair<string, Rectangle> pair in targets)
            {
                Panel slot;
                if (!_slots.TryGetValue(pair.Key, out slot))
                    continue;

                Rectangle next = StepTowards(slot.Bounds, pair.Value);
                if (next != slot.Bounds)
                {
                    slot.Bounds = next;
                    moving = true;
                }
            }
            ResumeLayout(false);

            if (moving)
                return;

            _animation.Stop();
            ArrangeSlots();
        }

        private static Rectangle StepTowards(Rectangle current, Rectangle target)
        {
            return new Rectangle(
                StepTowards(current.X, target.X),
                StepTowards(current.Y, target.Y),
                StepTowards(current.Width, target.Width),
                StepTowards(current.Height, target.Height));
        }

        private static int StepTowards(int current, int target)
        {
            int delta = target - current;
            if (Math.Abs(delta) <= 1)
                return target;
            int step = (int)Math.Round(delta * AnimationStep);
            if (step == 0)
                step = Math.Sign(delta);
            return current + step;
        }

        private void Detach(TrendForm form, Point grab)
        {
            string key = KeyOf(form);
            Panel slot;
            if (!_slots.TryGetValue(key, out slot) || form.IsDisposed)
                return;

            Size size = form.Size;

            RemoveDocked(key);
            slot.Controls.Remove(form);
            slot.Dispose();

            Rebuild();
            ArrangeSlots();
            OnLayoutChanged();

            Form host = new Form
            {
                Text = form.Text,
                Icon = Res.AppIcon,
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.Manual,
                ShowInTaskbar = false,
                AllowTransparency = true,
                MinimumSize = new Size(300, 200),
                ClientSize = size
            };
            host.Controls.Add(form);
            Theme.Apply(host);

            Point cursor = Cursor.Position;
            int grabX = Math.Max(0, Math.Min(host.Width - 1, grab.X));
            int grabY = SystemInformation.CaptionHeight / 2 + SystemInformation.FrameBorderSize.Height;
            host.Location = new Point(cursor.X - grabX, cursor.Y - grabY);

            Size sizeAtStart = Size.Empty;
            host.ResizeBegin += (s, e) => sizeAtStart = host.Size;
            host.Move += (s, e) =>
            {
                Point position = Cursor.Position;
                bool over = CurrentDropZone().Contains(position);
                host.Opacity = over ? DropOpacity : 1.0;
                if (over && host.Size == sizeAtStart)
                {
                    string before = GraphAt(position, key);
                    ShowHint(RedockBounds(key, before), key + ">" + before);
                }
                else
                {
                    HideHint();
                }
            };
            host.ResizeEnd += (s, e) =>
            {
                host.Opacity = 1.0;
                HideHint();
                Point position = Cursor.Position;
                if (host.Size == sizeAtStart && CurrentDropZone().Contains(position))
                    Redock(form, GraphAt(position, key));
            };

            _floating.Add(form, host);
            host.Show(FindForm());
            OnTrendsChanged();

            if (Control.MouseButtons == MouseButtons.Left)
            {
                host.Update();
                ReleaseCapture();
                SendMessage(host.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
        }

        private void Redock(TrendForm form, string before)
        {
            Form host;
            if (!_floating.TryGetValue(form, out host) || form.IsDisposed)
                return;

            string key = KeyOf(form);
            if (_docked.ContainsKey(key))
                return;

            _floating.Remove(form);
            host.Hide();
            host.Controls.Remove(form);

            AddDocked(form);
            MoveInOrder(_order, key, before);
            Rebuild();

            ArrangeSlots();
            OnTrendsChanged();
            OnLayoutChanged();

            BeginInvoke(new Action(host.Dispose));
        }

        private void Bar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _dragging || _animation.Enabled)
                return;

            Panel bar = (Panel)sender;
            int index = _bars.IndexOf(bar);
            if (index < 0 || index >= _barInfo.Count)
                return;

            BarInfo info = _barInfo[index];
            if (info.Convert)
            {
                _layout.Transpose();
                BarInfo real = null;
                foreach (BarInfo candidate in ComputeBars(_layout))
                    if (!candidate.Convert && candidate.Vertical == info.Vertical && candidate.Bounds.IntersectsWith(bar.Bounds))
                        real = candidate;
                if (real == null)
                {
                    _layout.Transpose();
                    return;
                }
                _barInfo[index] = real;
                info = real;
            }

            _dragBar = bar;
            _barGrab = info.Vertical ? e.Y : e.X;
            _barStart = info.Vertical ? bar.Top : bar.Left;
            bar.BringToFront();
        }

        private void Bar_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _dragging || _animation.Enabled)
                return;

            int index = _bars.IndexOf((Panel)sender);
            if (index < 0 || index >= _barInfo.Count)
                return;

            BarInfo info = _barInfo[index];
            if (info.Split == null || info.Split == _layout.Root)
                return;

            _layout.AlignWith(info.Split);
            ArrangeSlots();
        }

        private void Bar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragBar == null || _dragBar != sender)
                return;

            int index = _bars.IndexOf(_dragBar);
            if (index < 0 || index >= _barInfo.Count)
                return;

            BarInfo info = _barInfo[index];
            if (info.Vertical)
            {
                int min = info.FirstBounds.Top + MinSlotSize;
                int max = info.SecondBounds.Bottom - MinSlotSize - SplitterSize;
                if (min <= max)
                    _dragBar.Top = Math.Max(min, Math.Min(max, _dragBar.Top + e.Y - _barGrab));
            }
            else
            {
                int min = info.FirstBounds.Left + MinSlotSize;
                int max = info.SecondBounds.Right - MinSlotSize - SplitterSize;
                if (min <= max)
                    _dragBar.Left = Math.Max(min, Math.Min(max, _dragBar.Left + e.X - _barGrab));
            }
        }

        private void FinishBarDrag()
        {
            if (_dragBar == null)
                return;

            Panel bar = _dragBar;
            _dragBar = null;

            int index = _bars.IndexOf(bar);
            if (index < 0 || index >= _barInfo.Count)
                return;

            BarInfo info = _barInfo[index];
            int position = info.Vertical ? bar.Top : bar.Left;
            if (position == _barStart)
            {
                ArrangeSlots();
                return;
            }

            int firstLength = info.Vertical ? position - info.FirstBounds.Top : position - info.FirstBounds.Left;
            int pairLength = (info.Vertical
                ? info.SecondBounds.Bottom - info.FirstBounds.Top
                : info.SecondBounds.Right - info.FirstBounds.Left) - SplitterSize;
            if (pairLength <= 0)
                return;

            float pairWeight = info.First.Weight + info.Second.Weight;
            info.First.Weight = pairWeight * firstLength / pairLength;
            info.Second.Weight = pairWeight - info.First.Weight;

            ArrangeSlots();
        }

        private Rectangle LayoutArea()
        {
            Rectangle area = DisplayRectangle;
            return new Rectangle(
                area.X + Padding.Left,
                area.Y + Padding.Top,
                area.Width - Padding.Horizontal,
                area.Height - Padding.Vertical);
        }

        private Dictionary<string, Rectangle> ComputeSlots(TrendLayout layout, List<BarInfo> bars)
        {
            Dictionary<string, Rectangle> slots = new Dictionary<string, Rectangle>();
            if (layout.Root != null)
                Place(layout.Root, LayoutArea(), slots, bars);
            return slots;
        }

        private static void Place(LayoutNode node, Rectangle area, Dictionary<string, Rectangle> slots, List<BarInfo> bars)
        {
            if (node.IsLeaf)
            {
                slots[node.Key] = area;
                return;
            }

            int count = node.Children.Count;
            float total = 0f;
            foreach (LayoutNode child in node.Children)
                total += child.Weight;
            if (count == 0 || total <= 0f)
                return;

            int length = node.Vertical ? area.Height : area.Width;
            int free = Math.Max(0, length - SplitterSize * (count - 1));

            List<Rectangle> parts = new List<Rectangle>(count);
            float accumulated = 0f;
            int position = 0;
            for (int i = 0; i < count; i++)
            {
                accumulated += node.Children[i].Weight;
                int end = i == count - 1 ? free : (int)Math.Round(free * accumulated / total);
                int size = Math.Max(1, end - position);
                int offset = position + SplitterSize * i;

                parts.Add(node.Vertical
                    ? new Rectangle(area.X, area.Y + offset, area.Width, size)
                    : new Rectangle(area.X + offset, area.Y, size, area.Height));

                position = end;
            }

            for (int i = 0; i < count; i++)
            {
                Place(node.Children[i], parts[i], slots, bars);

                if (bars == null || i == count - 1)
                    continue;

                Rectangle part = parts[i];
                bars.Add(new BarInfo
                {
                    Split = node,
                    First = node.Children[i],
                    Second = node.Children[i + 1],
                    FirstBounds = part,
                    SecondBounds = parts[i + 1],
                    Vertical = node.Vertical,
                    Bounds = node.Vertical
                        ? new Rectangle(area.X, part.Bottom, area.Width, SplitterSize)
                        : new Rectangle(part.Right, area.Y, SplitterSize, area.Height)
                });
            }
        }

        private List<BarInfo> ComputeBars(TrendLayout layout)
        {
            List<BarInfo> bars = new List<BarInfo>();
            ComputeSlots(layout, bars);
            if (!layout.IsQuad)
                return bars;

            LayoutNode root = layout.Root;
            BarInfo main = bars.Find(bar => bar.Split == root);
            BarInfo first = bars.Find(bar => bar.Split == root.Children[0]);
            BarInfo second = bars.Find(bar => bar.Split == root.Children[1]);
            if (main == null || first == null || second == null)
                return bars;

            Rectangle line = main.Bounds;
            Rectangle a;
            Rectangle b;
            if (root.Vertical)
            {
                if (first.Bounds.X != second.Bounds.X)
                    return bars;
                int cut = first.Bounds.Right;
                a = new Rectangle(line.Left, line.Top, cut - line.Left, line.Height);
                b = new Rectangle(cut, line.Top, line.Right - cut, line.Height);
            }
            else
            {
                if (first.Bounds.Y != second.Bounds.Y)
                    return bars;
                int cut = first.Bounds.Bottom;
                a = new Rectangle(line.Left, line.Top, line.Width, cut - line.Top);
                b = new Rectangle(line.Left, cut, line.Width, line.Bottom - cut);
            }

            bars.Remove(main);
            bars.Add(new BarInfo { Convert = true, Vertical = main.Vertical, Bounds = a });
            bars.Add(new BarInfo { Convert = true, Vertical = main.Vertical, Bounds = b });
            return bars;
        }

        private void SyncBars(int needed)
        {
            while (_bars.Count < needed)
            {
                Panel bar = new Panel { BackColor = Theme.Current.Splitter };
                bar.MouseDown += Bar_MouseDown;
                bar.MouseMove += Bar_MouseMove;
                bar.MouseUp += (s, e) => FinishBarDrag();
                bar.MouseDoubleClick += Bar_MouseDoubleClick;
                bar.MouseCaptureChanged += (s, e) => FinishBarDrag();
                _bars.Add(bar);
                Controls.Add(bar);
            }

            while (_bars.Count > needed)
            {
                Panel bar = _bars[_bars.Count - 1];
                _bars.RemoveAt(_bars.Count - 1);
                if (bar == _dragBar)
                    _dragBar = null;
                Controls.Remove(bar);
                bar.Dispose();
            }
        }

        private void ArrangeSlots()
        {
            if (_arranging)
                return;

            _arranging = true;
            try
            {
                PlaceSlots();
            }
            finally
            {
                _arranging = false;
            }
        }

        private void PlaceSlots()
        {
            Dictionary<string, Rectangle> slots = ComputeSlots(_layout, null);
            List<BarInfo> bars = ComputeBars(_layout);

            _barInfo.Clear();
            _barInfo.AddRange(bars);
            SyncBars(bars.Count);

            SuspendLayout();
            foreach (KeyValuePair<string, Panel> pair in _slots)
            {
                Rectangle bounds;
                if (slots.TryGetValue(pair.Key, out bounds))
                {
                    pair.Value.Bounds = bounds;
                    pair.Value.Visible = true;
                }
            }

            for (int i = 0; i < bars.Count; i++)
            {
                Panel bar = _bars[i];
                bar.Cursor = bars[i].Vertical ? Cursors.HSplit : Cursors.VSplit;
                bar.Bounds = bars[i].Bounds;
                bar.Visible = true;
                bar.BringToFront();
            }
            ResumeLayout(false);

            bool cycle = _docked.Count >= 2;
            foreach (Button button in _cycleButtons.Values)
                button.Visible = cycle;
        }

        private void OnTrendsChanged()
        {
            EventHandler handler = TrendsChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void OnLayoutChanged()
        {
            EventHandler handler = LayoutChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
