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
        private const int AnimationInterval = 15;
        private const float AnimationStep = 0.35f;
        private const float SwapOverlap = 0.5f;
        private const int DirectionDeadBand = 2;
        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 2;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private readonly List<TrendForm> _forms = new List<TrendForm>();
        private readonly Dictionary<TrendForm, Panel> _slots = new Dictionary<TrendForm, Panel>();
        private readonly Dictionary<TrendForm, Panel> _headers = new Dictionary<TrendForm, Panel>();
        private readonly Dictionary<TrendForm, float> _weights = new Dictionary<TrendForm, float>();
        private readonly Dictionary<TrendForm, Form> _floating = new Dictionary<TrendForm, Form>();
        private readonly List<Panel> _bars = new List<Panel>();
        private readonly Timer _animation;
        private Panel _dragBar;
        private int _barGrab;
        private TrendForm _dragForm;
        private Point _dragStart;
        private Point _dragGrab;
        private int _dragOffset;
        private int _liftPosition;
        private int _liftDirection;
        private bool _dragging;
        private List<TrendForm> _preview;
        private bool _sideBySide;

        public TrendDock()
        {
            _animation = new Timer { Interval = AnimationInterval };
            _animation.Tick += Animation_Tick;
        }

        public event EventHandler TrendsChanged;

        public Func<Rectangle> DropZone { get; set; }

        public bool HasTrends
        {
            get { return _forms.Count > 0; }
        }

        public bool SideBySide
        {
            get { return _sideBySide; }
            set
            {
                if (_sideBySide == value)
                    return;
                _sideBySide = value;
                ArrangeSlots();
            }
        }

        public void ShowTrend(TrendForm form)
        {
            if (_slots.ContainsKey(form) || _floating.ContainsKey(form))
                return;

            form.Disposed += (s, e) => CloseTrend(form);
            AddDocked(form, _forms.Count);
            form.Show();

            OnTrendsChanged();
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

            Panel slot;
            if (!_slots.TryGetValue(form, out slot))
                return;

            if (_dragForm == form)
                CancelDrag();

            RemoveDocked(form);
            slot.Dispose();

            ArrangeSlots();
            OnTrendsChanged();
        }

        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);
            if (_dragBar == null && !_dragging && !_animation.Enabled)
                ArrangeSlots();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _animation.Dispose();
            base.Dispose(disposing);
        }

        private void AddDocked(TrendForm form, int index)
        {
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
            close.MouseLeave += (s, e) => PaintHeader(form, _dragging && form == _dragForm);
            close.Click += (s, e) => BeginInvoke(new Action(() => CloseTrend(form)));

            header.Controls.Add(title);
            header.Controls.Add(close);

            slot.Controls.Add(form);
            slot.Controls.Add(header);

            _weights.Add(form, AverageWeight());
            _forms.Insert(Math.Max(0, Math.Min(_forms.Count, index)), form);
            _slots.Add(form, slot);
            _headers.Add(form, header);
            PaintHeader(form, false);

            SyncBars();
            Controls.Add(slot);
            ArrangeSlots();
        }

        private void RemoveDocked(TrendForm form)
        {
            Panel slot = _slots[form];

            _forms.Remove(form);
            _slots.Remove(form);
            _headers.Remove(form);
            _weights.Remove(form);

            Controls.Remove(slot);
            SyncBars();
        }

        private void PaintHeader(TrendForm form, bool lifted)
        {
            Panel header;
            if (!_headers.TryGetValue(form, out header))
                return;

            header.BackColor = lifted ? SystemColors.Highlight : SystemColors.InactiveCaption;
            Color text = lifted ? SystemColors.HighlightText : SystemColors.ControlText;
            foreach (Control control in header.Controls)
                control.ForeColor = text;
        }

        private Rectangle CurrentDropZone()
        {
            Func<Rectangle> zone = DropZone;
            return zone != null ? zone() : RectangleToScreen(ClientRectangle);
        }

        private TrendForm DockedFormAt(Point screen)
        {
            Point local = PointToClient(screen);
            foreach (TrendForm form in _forms)
                if (_slots[form].Bounds.Contains(local))
                    return form;
            return null;
        }

        private void Header_MouseDown(TrendForm form, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _dragForm != null)
                return;

            _dragForm = form;
            _dragStart = Cursor.Position;
            _dragGrab = e.Location;
            _dragging = false;
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
                BeginReorder(form);
            }

            if (!CurrentDropZone().Contains(cursor))
            {
                Point grab = _dragGrab;
                CancelDrag();
                BeginInvoke(new Action(() => Detach(form, grab)));
                return;
            }

            MoveLifted(cursor);
        }

        private void BeginReorder(TrendForm form)
        {
            _dragging = true;
            _preview = new List<TrendForm>(_forms);

            Panel slot = _slots[form];
            Point local = PointToClient(_dragStart);
            _dragOffset = _sideBySide ? local.X - slot.Left : local.Y - slot.Top;
            _liftPosition = _sideBySide ? slot.Left : slot.Top;
            _liftDirection = 0;

            foreach (Panel bar in _bars)
                bar.Visible = false;
            slot.BringToFront();
            PaintHeader(form, true);
        }

        private void MoveLifted(Point cursor)
        {
            Panel slot = _slots[_dragForm];
            Rectangle area = LayoutArea();
            Point local = PointToClient(cursor);

            if (_sideBySide)
                slot.Left = Math.Max(area.Left, Math.Min(area.Right - slot.Width, local.X - _dragOffset));
            else
                slot.Top = Math.Max(area.Top, Math.Min(area.Bottom - slot.Height, local.Y - _dragOffset));

            int start = _sideBySide ? slot.Left : slot.Top;
            int end = start + (_sideBySide ? slot.Width : slot.Height);

            if (Math.Abs(start - _liftPosition) >= DirectionDeadBand)
            {
                _liftDirection = Math.Sign(start - _liftPosition);
                _liftPosition = start;
            }

            for (int guard = 0; guard < _preview.Count; guard++)
            {
                List<Rectangle> targets = ComputeSlotBounds(_preview, null);
                if (targets == null)
                    return;

                int index = _preview.IndexOf(_dragForm);
                int neighbour = index + _liftDirection;
                if (_liftDirection == 0 || neighbour < 0 || neighbour >= _preview.Count)
                    break;

                Rectangle other = targets[neighbour];
                int otherStart = _sideBySide ? other.Left : other.Top;
                int otherSize = _sideBySide ? other.Width : other.Height;

                bool overlapped = _liftDirection > 0
                    ? end > otherStart + otherSize * SwapOverlap
                    : start < otherStart + otherSize * (1f - SwapOverlap);
                if (!overlapped)
                    break;

                _preview[index] = _preview[neighbour];
                _preview[neighbour] = _dragForm;
            }

            _animation.Start();
        }

        private void FinishHeaderDrag(bool commit)
        {
            if (_dragForm == null)
                return;

            TrendForm form = _dragForm;
            bool dragging = _dragging;
            List<TrendForm> preview = _preview;

            _dragForm = null;
            _dragging = false;
            _preview = null;

            if (!dragging)
                return;

            PaintHeader(form, false);

            if (commit && preview != null && preview.Count == _forms.Count)
            {
                _forms.Clear();
                _forms.AddRange(preview);
            }

            _animation.Start();
        }

        private void CancelDrag()
        {
            TrendForm form = _dragForm;
            bool dragging = _dragging;

            _dragForm = null;
            _dragging = false;
            _preview = null;
            _animation.Stop();

            if (form != null && dragging)
            {
                PaintHeader(form, false);
                ArrangeSlots();
            }
        }

        private void Animation_Tick(object sender, EventArgs e)
        {
            List<TrendForm> order = _dragging && _preview != null ? _preview : _forms;
            List<Rectangle> targets = ComputeSlotBounds(order, null);
            if (targets == null)
            {
                _animation.Stop();
                ArrangeSlots();
                return;
            }

            bool moving = false;
            SuspendLayout();
            for (int i = 0; i < order.Count; i++)
            {
                TrendForm form = order[i];
                if (_dragging && form == _dragForm)
                    continue;

                Panel slot;
                if (!_slots.TryGetValue(form, out slot))
                    continue;

                Rectangle next = StepTowards(slot.Bounds, targets[i]);
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
            if (!_dragging)
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
            Panel slot;
            if (!_slots.TryGetValue(form, out slot) || form.IsDisposed)
                return;

            Size size = form.Size;

            RemoveDocked(form);
            slot.Controls.Remove(form);
            slot.Dispose();
            ArrangeSlots();

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

            Point cursor = Cursor.Position;
            int grabX = Math.Max(0, Math.Min(host.Width - 1, grab.X));
            int grabY = SystemInformation.CaptionHeight / 2 + SystemInformation.FrameBorderSize.Height;
            host.Location = new Point(cursor.X - grabX, cursor.Y - grabY);

            Size sizeAtStart = Size.Empty;
            host.ResizeBegin += (s, e) => sizeAtStart = host.Size;
            host.Move += (s, e) => host.Opacity = CurrentDropZone().Contains(Cursor.Position) ? DropOpacity : 1.0;
            host.ResizeEnd += (s, e) =>
            {
                host.Opacity = 1.0;
                if (host.Size == sizeAtStart && CurrentDropZone().Contains(Cursor.Position))
                    Redock(form, Cursor.Position);
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

        private void Redock(TrendForm form, Point screen)
        {
            Form host;
            if (!_floating.TryGetValue(form, out host) || form.IsDisposed)
                return;

            _floating.Remove(form);
            host.Hide();
            host.Controls.Remove(form);

            int index = _forms.Count;
            TrendForm target = DockedFormAt(screen);
            if (target != null)
            {
                Rectangle bounds = _slots[target].Bounds;
                Point local = PointToClient(screen);
                bool after = _sideBySide
                    ? local.X > bounds.Left + bounds.Width / 2
                    : local.Y > bounds.Top + bounds.Height / 2;
                index = _forms.IndexOf(target) + (after ? 1 : 0);
            }

            AddDocked(form, index);
            OnTrendsChanged();

            BeginInvoke(new Action(host.Dispose));
        }

        private float AverageWeight()
        {
            if (_weights.Count == 0)
                return 1f;

            float total = 0f;
            foreach (float weight in _weights.Values)
                total += weight;
            return total / _weights.Count;
        }

        private void SyncBars()
        {
            int needed = Math.Max(0, _forms.Count - 1);

            while (_bars.Count < needed)
            {
                Panel bar = new Panel { BackColor = SystemColors.ControlDark };
                bar.MouseDown += Bar_MouseDown;
                bar.MouseMove += Bar_MouseMove;
                bar.MouseUp += (s, e) => FinishBarDrag();
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

        private void Bar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || _dragging || _animation.Enabled)
                return;

            _dragBar = (Panel)sender;
            _barGrab = _sideBySide ? e.X : e.Y;
            _dragBar.BringToFront();
        }

        private void Bar_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragBar == null || _dragBar != sender)
                return;

            int index = _bars.IndexOf(_dragBar);
            if (index < 0 || index + 1 >= _forms.Count)
                return;

            Panel first = _slots[_forms[index]];
            Panel second = _slots[_forms[index + 1]];

            if (_sideBySide)
            {
                int min = first.Left + MinSlotSize;
                int max = second.Right - MinSlotSize - SplitterSize;
                if (min <= max)
                    _dragBar.Left = Math.Max(min, Math.Min(max, _dragBar.Left + e.X - _barGrab));
            }
            else
            {
                int min = first.Top + MinSlotSize;
                int max = second.Bottom - MinSlotSize - SplitterSize;
                if (min <= max)
                    _dragBar.Top = Math.Max(min, Math.Min(max, _dragBar.Top + e.Y - _barGrab));
            }
        }

        private void FinishBarDrag()
        {
            if (_dragBar == null)
                return;

            Panel bar = _dragBar;
            _dragBar = null;

            int index = _bars.IndexOf(bar);
            if (index < 0 || index + 1 >= _forms.Count)
                return;

            TrendForm firstForm = _forms[index];
            TrendForm secondForm = _forms[index + 1];
            Panel first = _slots[firstForm];
            Panel second = _slots[secondForm];

            int firstLength = _sideBySide ? bar.Left - first.Left : bar.Top - first.Top;
            int pairLength = (_sideBySide ? second.Right - first.Left : second.Bottom - first.Top) - SplitterSize;
            if (pairLength <= 0)
                return;

            float pairWeight = _weights[firstForm] + _weights[secondForm];
            _weights[firstForm] = pairWeight * firstLength / pairLength;
            _weights[secondForm] = pairWeight - _weights[firstForm];

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

        private List<Rectangle> ComputeSlotBounds(List<TrendForm> order, List<Rectangle> bars)
        {
            int count = order.Count;
            if (count == 0)
                return null;

            Rectangle area = LayoutArea();
            int length = _sideBySide ? area.Width : area.Height;
            int free = Math.Max(0, length - SplitterSize * (count - 1));

            float total = 0f;
            foreach (TrendForm form in order)
            {
                float weight;
                if (!_weights.TryGetValue(form, out weight))
                    return null;
                total += weight;
            }
            if (total <= 0f)
                return null;

            List<Rectangle> slots = new List<Rectangle>(count);
            float accumulated = 0f;
            int position = 0;
            for (int i = 0; i < count; i++)
            {
                accumulated += _weights[order[i]];
                int end = i == count - 1 ? free : (int)Math.Round(free * accumulated / total);
                int size = Math.Max(1, end - position);
                int offset = position + SplitterSize * i;

                if (_sideBySide)
                    slots.Add(new Rectangle(area.X + offset, area.Y, size, area.Height));
                else
                    slots.Add(new Rectangle(area.X, area.Y + offset, area.Width, size));

                if (bars != null && i < count - 1)
                {
                    if (_sideBySide)
                        bars.Add(new Rectangle(area.X + offset + size, area.Y, SplitterSize, area.Height));
                    else
                        bars.Add(new Rectangle(area.X, area.Y + offset + size, area.Width, SplitterSize));
                }

                position = end;
            }
            return slots;
        }

        private void ArrangeSlots()
        {
            List<Rectangle> bars = new List<Rectangle>();
            List<Rectangle> slots = ComputeSlotBounds(_forms, bars);
            if (slots == null)
                return;

            SuspendLayout();
            for (int i = 0; i < _forms.Count; i++)
                _slots[_forms[i]].Bounds = slots[i];

            for (int i = 0; i < bars.Count && i < _bars.Count; i++)
            {
                Panel bar = _bars[i];
                bar.Cursor = _sideBySide ? Cursors.VSplit : Cursors.HSplit;
                bar.Bounds = bars[i];
                bar.Visible = true;
            }
            ResumeLayout(false);
        }

        private void OnTrendsChanged()
        {
            EventHandler handler = TrendsChanged;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
