using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class SchemeScaler
    {
        private const int WM_SETREDRAW = 0x000B;
        private const int MaxLineThickness = 6;
        private const int TouchTolerance = 1;

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private enum Kind
        {
            Plain,
            HorizontalLine,
            VerticalLine,
            Picture
        }

        private class Entry
        {
            public Control Control;
            public int Parent = -1;
            public Rectangle Design;
            public Font Font;
            public Kind Kind;
            public int StartTarget = -1;
            public int EndTarget = -1;
            public Rectangle Scaled;
            public Rectangle Local;
        }

        private readonly Control _canvas;
        private readonly Size _designSize;
        private readonly List<Entry> _entries = new List<Entry>();
        private readonly Dictionary<Control, Entry> _byControl = new Dictionary<Control, Entry>();
        private readonly Dictionary<Font, Font> _fontCache = new Dictionary<Font, Font>();

        private float _scaleX = 1f;
        private float _scaleY = 1f;
        private int _centerLeft = -1;
        private int _centerRight = -1;
        private float _leftScaleX = 1f;
        private float _centerScaleX = 1f;
        private float _rightScaleX = 1f;

        public SchemeScaler(Control canvas, Size designSize)
        {
            _canvas = canvas;
            _designSize = designSize;

            Record(canvas, -1, Point.Empty);
            FindLineTargets();

            foreach (Entry entry in _entries)
                entry.Local = entry.Control.Bounds;
        }

        public Size DesignSize
        {
            get { return _designSize; }
        }

        public float FontFactor
        {
            get { return Math.Min(_scaleX, _scaleY); }
        }

        public void SetCenterColumn(int designLeft, int designRight)
        {
            _centerLeft = designLeft;
            _centerRight = designRight;
        }

        public Rectangle ScaledBounds(Control control)
        {
            Entry entry;
            return _byControl.TryGetValue(control, out entry) ? entry.Local : control.Bounds;
        }

        public Font ScaledFont(Font original)
        {
            Font scaled;
            if (_fontCache.TryGetValue(original, out scaled))
                return scaled;

            float size = original.Size * FontFactor;
            if (Math.Abs(size - original.Size) < 0.05f)
                scaled = original;
            else
                scaled = new Font(original.FontFamily, Math.Max(1f, size), original.Style,
                    original.Unit, original.GdiCharSet, original.GdiVerticalFont);

            _fontCache[original] = scaled;
            return scaled;
        }

        private void Record(Control parent, int parentIndex, Point origin)
        {
            foreach (Control control in parent.Controls)
            {
                Rectangle design = control.Bounds;
                design.Offset(origin);

                Entry entry = new Entry
                {
                    Control = control,
                    Parent = parentIndex,
                    Design = design,
                    Font = control.Font,
                    Kind = Classify(control)
                };

                int index = _entries.Count;
                _entries.Add(entry);
                _byControl[control] = entry;

                if (control is GroupBox || control is Panel)
                    Record(control, index, design.Location);
            }
        }

        private static Kind Classify(Control control)
        {
            PictureBox picture = control as PictureBox;
            if (picture == null)
                return Kind.Plain;

            if (picture.Image != null || picture.BackgroundImage != null)
            {
                if (picture.SizeMode == PictureBoxSizeMode.AutoSize)
                    picture.SizeMode = picture.Image != null ? PictureBoxSizeMode.Zoom : PictureBoxSizeMode.Normal;
                if (picture.BackgroundImageLayout == ImageLayout.Tile)
                    picture.BackgroundImageLayout = ImageLayout.Stretch;
                return Kind.Picture;
            }

            if (picture.SizeMode == PictureBoxSizeMode.AutoSize)
                picture.SizeMode = PictureBoxSizeMode.Normal;

            if (picture.BackColor.ToArgb() != Color.Black.ToArgb())
                return Kind.Plain;

            int w = picture.Width;
            int h = picture.Height;
            if (h <= MaxLineThickness && w > h)
                return Kind.HorizontalLine;
            if (w <= MaxLineThickness && h > w)
                return Kind.VerticalLine;
            return Kind.Plain;
        }

        private void FindLineTargets()
        {
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry line = _entries[i];
                if (line.Kind == Kind.HorizontalLine)
                {
                    line.StartTarget = FindTarget(i, line.Design.Left, true);
                    line.EndTarget = FindTarget(i, line.Design.Right, true);
                }
                else if (line.Kind == Kind.VerticalLine)
                {
                    line.StartTarget = FindTarget(i, line.Design.Top, false);
                    line.EndTarget = FindTarget(i, line.Design.Bottom, false);
                }
            }
        }

        private int FindTarget(int lineIndex, int edge, bool horizontal)
        {
            Rectangle line = _entries[lineIndex].Design;
            int found = -1;

            for (int j = 0; j < _entries.Count; j++)
            {
                if (j == lineIndex)
                    continue;

                Entry target = _entries[j];
                if (target.Kind == Kind.Plain)
                    continue;

                Rectangle t = target.Design;
                bool touches = horizontal
                    ? edge >= t.Left - TouchTolerance && edge <= t.Right + TouchTolerance
                        && line.Top <= t.Bottom + TouchTolerance && line.Bottom >= t.Top - TouchTolerance
                    : edge >= t.Top - TouchTolerance && edge <= t.Bottom + TouchTolerance
                        && line.Left <= t.Right + TouchTolerance && line.Right >= t.Left - TouchTolerance;

                if (!touches)
                    continue;

                if (found < 0 || (_entries[found].Kind == Kind.Picture && target.Kind != Kind.Picture))
                    found = j;
            }

            return found;
        }

        public void Apply(float scaleX, float scaleY)
        {
            if (Math.Abs(scaleX - _scaleX) < 0.0005f && Math.Abs(scaleY - _scaleY) < 0.0005f)
                return;

            bool fontChanged = Math.Abs(Math.Min(scaleX, scaleY) - FontFactor) >= 0.0005f;
            _scaleX = scaleX;
            _scaleY = scaleY;
            if (fontChanged)
                _fontCache.Clear();

            _leftScaleX = _scaleX;
            _centerScaleX = _scaleX;
            _rightScaleX = _scaleX;
            if (HasCenterColumn)
                CenterMiddleColumn();

            foreach (Entry entry in _entries)
                entry.Scaled = ScaleEntry(entry);

            Rectangle[] snapped = new Rectangle[_entries.Count];
            for (int i = 0; i < _entries.Count; i++)
                snapped[i] = SnapLineEnds(_entries[i]);
            for (int i = 0; i < _entries.Count; i++)
                _entries[i].Scaled = snapped[i];

            bool redraw = _canvas.IsHandleCreated;
            if (redraw)
                SendMessage(_canvas.Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);

            _canvas.SuspendLayout();
            try
            {
                foreach (Entry entry in _entries)
                    entry.Control.SuspendLayout();

                foreach (Entry entry in _entries)
                    Place(entry);

                for (int i = _entries.Count - 1; i >= 0; i--)
                    _entries[i].Control.ResumeLayout(true);
            }
            finally
            {
                _canvas.ResumeLayout(false);
                if (redraw)
                {
                    SendMessage(_canvas.Handle, WM_SETREDRAW, new IntPtr(1), IntPtr.Zero);
                    _canvas.Invalidate(true);
                }
            }
        }

        private Rectangle ScaleEntry(Entry entry)
        {
            Rectangle d = entry.Design;
            float f = FontFactor;

            switch (entry.Kind)
            {
                case Kind.HorizontalLine:
                {
                    int left = Round(MapX(d.Left));
                    int right = Round(MapX(d.Right));
                    int thickness = Math.Max(1, Round(d.Height * f));
                    int top = Round((d.Top + d.Height / 2f) * _scaleY - thickness / 2f);
                    return new Rectangle(left, top, Math.Max(1, right - left), thickness);
                }
                case Kind.VerticalLine:
                {
                    int top = Round(d.Top * _scaleY);
                    int bottom = Round(d.Bottom * _scaleY);
                    int thickness = Math.Max(1, Round(d.Width * f));
                    int left = Round(MapX(d.Left + d.Width / 2f) - thickness / 2f);
                    return new Rectangle(left, top, thickness, Math.Max(1, bottom - top));
                }
                case Kind.Picture:
                {
                    int width = Math.Max(1, Round(d.Width * f));
                    int height = Math.Max(1, Round(d.Height * f));
                    int left = Round(MapX(d.Left + d.Width / 2f) - width / 2f);
                    int top = Round((d.Top + d.Height / 2f) * _scaleY - height / 2f);
                    return new Rectangle(left, top, width, height);
                }
                default:
                {
                    int left = Round(MapX(d.Left));
                    int top = Round(d.Top * _scaleY);
                    int right = Round(MapX(d.Right));
                    int bottom = Round(d.Bottom * _scaleY);
                    return new Rectangle(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
                }
            }
        }

        private Rectangle SnapLineEnds(Entry line)
        {
            if (line.Kind == Kind.HorizontalLine)
            {
                int left = line.Scaled.Left;
                int right = line.Scaled.Right;
                if (line.StartTarget >= 0)
                    left = MapEdge(line.Design.Left, _entries[line.StartTarget], true);
                if (line.EndTarget >= 0)
                    right = MapEdge(line.Design.Right, _entries[line.EndTarget], true);
                if (right > left)
                    return new Rectangle(left, line.Scaled.Top, right - left, line.Scaled.Height);
            }
            else if (line.Kind == Kind.VerticalLine)
            {
                int top = line.Scaled.Top;
                int bottom = line.Scaled.Bottom;
                if (line.StartTarget >= 0)
                    top = MapEdge(line.Design.Top, _entries[line.StartTarget], false);
                if (line.EndTarget >= 0)
                    bottom = MapEdge(line.Design.Bottom, _entries[line.EndTarget], false);
                if (bottom > top)
                    return new Rectangle(line.Scaled.Left, top, line.Scaled.Width, bottom - top);
            }

            return line.Scaled;
        }

        private bool HasCenterColumn
        {
            get { return _centerLeft > 0 && _centerRight > _centerLeft && _centerRight < _designSize.Width; }
        }

        private void CenterMiddleColumn()
        {
            float total = _designSize.Width * _scaleX;
            float leftDesign = _centerLeft;
            float middleDesign = _centerRight - _centerLeft;
            float rightDesign = _designSize.Width - _centerRight;
            float font = FontFactor;

            float middle = _scaleX;
            float side = (total - middleDesign * middle) / 2f;
            float minSide = Math.Max(leftDesign, rightDesign) * font;
            if (side < minSide)
            {
                side = minSide;
                middle = (total - 2f * side) / middleDesign;
            }

            if (middle >= font)
            {
                _leftScaleX = side / leftDesign;
                _centerScaleX = middle;
                _rightScaleX = side / rightDesign;
                return;
            }

            _leftScaleX = font;
            _centerScaleX = font;
            _rightScaleX = (total - (leftDesign + middleDesign) * font) / rightDesign;
        }

        private float MapX(float x)
        {
            if (!HasCenterColumn)
                return x * _scaleX;
            if (x <= _centerLeft)
                return x * _leftScaleX;

            float left = _centerLeft * _leftScaleX;
            if (x <= _centerRight)
                return left + (x - _centerLeft) * _centerScaleX;

            return left + (_centerRight - _centerLeft) * _centerScaleX + (x - _centerRight) * _rightScaleX;
        }

        private static int MapEdge(int edge, Entry target, bool horizontal)
        {
            Rectangle d = target.Design;
            Rectangle s = target.Scaled;

            int start = horizontal ? d.Left : d.Top;
            int length = horizontal ? d.Width : d.Height;
            float t = length > 0 ? (float)(edge - start) / length : 0f;
            t = Math.Max(0f, Math.Min(1f, t));

            return horizontal
                ? Round(s.Left + t * s.Width)
                : Round(s.Top + t * s.Height);
        }

        private void Place(Entry entry)
        {
            Rectangle local = entry.Scaled;
            if (entry.Parent >= 0)
            {
                Rectangle parent = _entries[entry.Parent].Scaled;
                local.Offset(-parent.Left, -parent.Top);
            }

            Control control = entry.Control;
            control.Font = ScaledFont(entry.Font);
            control.Bounds = local;

            if (control.Height != local.Height)
                control.Top = local.Top + (local.Height - control.Height) / 2;

            entry.Local = control.Bounds;
        }

        private static int Round(float value)
        {
            return (int)Math.Round(value);
        }
    }
}
