using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Forms.DataVisualization.Charting;

namespace ArdisCVDCore
{
    internal sealed class Palette
    {
        public string Key { get; set; }
        public string Name { get; set; }
        public bool Classic { get; set; }
        public bool Dark { get; set; }
        public Color Window { get; set; }
        public Color Section { get; set; }
        public Color Inset { get; set; }
        public Color Border { get; set; }
        public Color Text { get; set; }
        public Color Title { get; set; }
        public Color ValueBack { get; set; }
        public Color ValueText { get; set; }
        public Color InputBack { get; set; }
        public Color InputText { get; set; }
        public Color ButtonBack { get; set; }
        public Color ButtonHover { get; set; }
        public Color ButtonPressed { get; set; }
        public Color ButtonBorder { get; set; }
        public Color Pipe { get; set; }
        public Color Toolbar { get; set; }
        public Color ToolbarHover { get; set; }
        public Color ToolbarPressed { get; set; }
        public Color ToolbarChecked { get; set; }
        public Color Menu { get; set; }
        public Color Accent { get; set; }
        public Color AccentText { get; set; }
        public Color Splitter { get; set; }
        public Color ChartBack { get; set; }
        public Color ChartGrid { get; set; }
        public Color ChartAxis { get; set; }
        public Color Body { get; set; }
        public Color BodyShade { get; set; }
        public Color BodyLight { get; set; }
        public Color BodyText { get; set; }
    }

    internal static class Theme
    {
        private const string IniSection = "MainForm";
        private const string IniKey = "Theme";
        private const int GroupTextLeft = 6;
        private const TextFormatFlags GroupTextFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;
        private const int DwmUseImmersiveDarkMode = 20;
        private const double MinChartLuminance = 0.18;

        public static readonly Palette Old = new Palette
        {
            Key = "Old",
            Name = "Default",
            Classic = true,
            Window = SystemColors.Control,
            Section = SystemColors.GradientActiveCaption,
            Inset = SystemColors.InactiveCaption,
            Border = SystemColors.ControlDark,
            Text = SystemColors.ControlText,
            Title = SystemColors.ControlText,
            ValueBack = Color.Blue,
            ValueText = Color.White,
            InputBack = SystemColors.Window,
            InputText = SystemColors.WindowText,
            ButtonBack = SystemColors.Control,
            ButtonHover = SystemColors.ControlLight,
            ButtonPressed = SystemColors.ControlDark,
            ButtonBorder = SystemColors.ControlDark,
            Pipe = Color.Black,
            Toolbar = SystemColors.Control,
            ToolbarHover = SystemColors.ControlLight,
            ToolbarPressed = SystemColors.ControlDark,
            ToolbarChecked = SystemColors.GradientActiveCaption,
            Menu = SystemColors.Menu,
            Accent = SystemColors.Highlight,
            AccentText = SystemColors.HighlightText,
            Splitter = SystemColors.ControlDark,
            ChartBack = Color.White,
            ChartGrid = Color.Silver,
            ChartAxis = Color.Black
        };

        public static readonly Palette Isa = new Palette
        {
            Key = "ISA101",
            Name = "Light Mode",
            Window = Color.FromArgb(214, 216, 219),
            Section = Color.FromArgb(232, 233, 235),
            Inset = Color.FromArgb(220, 222, 225),
            Border = Color.FromArgb(160, 164, 170),
            Text = Color.FromArgb(28, 30, 33),
            Title = Color.FromArgb(28, 30, 33),
            ValueBack = Color.White,
            ValueText = Color.FromArgb(16, 16, 16),
            InputBack = Color.FromArgb(204, 208, 214),
            InputText = Color.FromArgb(28, 30, 33),
            ButtonBack = Color.FromArgb(246, 247, 248),
            ButtonHover = Color.FromArgb(226, 229, 233),
            ButtonPressed = Color.FromArgb(206, 210, 216),
            ButtonBorder = Color.FromArgb(150, 154, 160),
            Pipe = Color.FromArgb(58, 60, 64),
            Toolbar = Color.FromArgb(244, 245, 246),
            ToolbarHover = Color.FromArgb(226, 229, 233),
            ToolbarPressed = Color.FromArgb(206, 210, 216),
            ToolbarChecked = Color.FromArgb(210, 213, 218),
            Menu = Color.FromArgb(250, 250, 251),
            Accent = Color.FromArgb(88, 94, 104),
            AccentText = Color.White,
            Splitter = Color.FromArgb(178, 182, 188),
            ChartBack = Color.White,
            ChartGrid = Color.FromArgb(222, 224, 228),
            ChartAxis = Color.FromArgb(120, 124, 130)
        };

        public static readonly Palette Dark = new Palette
        {
            Key = "Dark",
            Name = "Dark Mode",
            Dark = true,
            Window = Color.FromArgb(11, 15, 30),
            Section = Color.FromArgb(19, 25, 45),
            Inset = Color.FromArgb(27, 35, 62),
            Border = Color.FromArgb(45, 56, 92),
            Text = Color.FromArgb(230, 233, 244),
            Title = Color.FromArgb(169, 177, 200),
            ValueBack = Color.FromArgb(6, 9, 19),
            ValueText = Color.FromArgb(236, 239, 248),
            InputBack = Color.FromArgb(24, 38, 86),
            InputText = Color.FromArgb(150, 184, 255),
            ButtonBack = Color.FromArgb(31, 40, 72),
            ButtonHover = Color.FromArgb(42, 54, 96),
            ButtonPressed = Color.FromArgb(54, 70, 124),
            ButtonBorder = Color.FromArgb(58, 72, 118),
            Pipe = Color.FromArgb(125, 135, 160),
            Toolbar = Color.FromArgb(14, 19, 36),
            ToolbarHover = Color.FromArgb(31, 40, 72),
            ToolbarPressed = Color.FromArgb(42, 54, 96),
            ToolbarChecked = Color.FromArgb(44, 60, 112),
            Menu = Color.FromArgb(19, 25, 45),
            Accent = Color.FromArgb(76, 110, 245),
            AccentText = Color.White,
            Splitter = Color.FromArgb(34, 44, 78),
            ChartBack = Color.FromArgb(12, 16, 32),
            ChartGrid = Color.FromArgb(32, 40, 66),
            ChartAxis = Color.FromArgb(90, 100, 130),
            Body = Color.FromArgb(82, 100, 156),
            BodyShade = Color.FromArgb(58, 74, 126),
            BodyLight = Color.FromArgb(196, 202, 220),
            BodyText = Color.FromArgb(230, 233, 244)
        };

        public static readonly Palette[] All = { Old, Isa, Dark };

        private enum Role
        {
            None,
            Form,
            Section,
            Inset,
            Plain,
            ButtonNeutral,
            ButtonColored,
            Measured,
            Setpoint,
            Combo,
            Pipe,
            Track,
            Logo,
            List,
            Chart,
            Hardware
        }

        private sealed class Snapshot
        {
            public Role Role;
            public Color OwnBack;
            public Color OwnFore;
            public FlatStyle Flat;
            public bool VisualBack;
            public BorderStyle Border;
            public bool Grid;
            public ChartSnapshot Chart;
        }

        private sealed class ChartSnapshot
        {
            public Color Back;
            public readonly Dictionary<ChartArea, Color> AreaBack = new Dictionary<ChartArea, Color>();
            public readonly Dictionary<Axis, Color[]> Axes = new Dictionary<Axis, Color[]>();
            public readonly Dictionary<Legend, Color[]> Legends = new Dictionary<Legend, Color[]>();
            public readonly Dictionary<Series, Color> Series = new Dictionary<Series, Color>();
        }

        private sealed class GlyphEntry
        {
            public ToolStripItem Item;
            public char Glyph;
            public int Size;
        }

        private static readonly ConditionalWeakTable<Control, Snapshot> _snapshots = new ConditionalWeakTable<Control, Snapshot>();
        private static readonly List<GlyphEntry> _glyphs = new List<GlyphEntry>();
        private static readonly MethodInfo _ownBack = typeof(Control).GetMethod("ShouldSerializeBackColor", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo _ownFore = typeof(Control).GetMethod("ShouldSerializeForeColor", BindingFlags.Instance | BindingFlags.NonPublic);
        private static Image _logoLight;

        public static Palette Current { get; private set; } = Isa;

        public static event EventHandler Changed;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string subIdList);

        public static void Load()
        {
            string key = IniWriter.INI.ReadINI(IniSection, IniKey);
            foreach (Palette palette in All)
                if (string.Equals(palette.Key, key, StringComparison.OrdinalIgnoreCase))
                    Current = palette;
        }

        public static void Select(Palette palette)
        {
            if (palette == null || palette == Current)
                return;

            Palette previous = Current;
            Current = palette;
            IniWriter.INI.Write(IniSection, IniKey, palette.Key);

            List<Form> forms = new List<Form>();
            foreach (Form form in Application.OpenForms)
                forms.Add(form);
            foreach (Form form in forms)
                Restyle(form, previous);

            RefreshGlyphs();

            EventHandler handler = Changed;
            if (handler != null)
                handler(null, EventArgs.Empty);

            foreach (Form form in forms)
                form.Invalidate(true);
        }

        public static void Apply(Control root)
        {
            Snapshot snapshot;
            if (!_snapshots.TryGetValue(root, out snapshot))
            {
                snapshot = Capture(root);
                _snapshots.Add(root, snapshot);
                Hook(root, snapshot);
                Paint(root, snapshot, Current, null);
                root.ControlAdded += (s, e) => Apply(e.Control);
            }

            if (root is NumericUpDown)
                return;
            foreach (Control child in root.Controls)
                Apply(child);
        }

        public static void Glyph(ToolStripItem item, char glyph, int size)
        {
            _glyphs.RemoveAll(entry => entry.Item == item);
            _glyphs.Add(new GlyphEntry { Item = item, Glyph = glyph, Size = size });
            item.Image = Res.Glyph(glyph, Current.Text, size);
        }

        private static void RefreshGlyphs()
        {
            _glyphs.RemoveAll(entry => entry.Item.IsDisposed);
            foreach (GlyphEntry entry in _glyphs)
            {
                Image old = entry.Item.Image;
                entry.Item.Image = Res.Glyph(entry.Glyph, Current.Text, entry.Size);
                if (old != null)
                    old.Dispose();
            }
        }

        private static void Restyle(Control control, Palette previous)
        {
            Snapshot snapshot;
            if (_snapshots.TryGetValue(control, out snapshot))
                Paint(control, snapshot, Current, previous);
            else
                Apply(control);

            if (control is NumericUpDown)
                return;
            foreach (Control child in control.Controls)
                Restyle(child, previous);
        }

        private static Color Own(Control control, MethodInfo probe, Color value)
        {
            if (probe == null)
                return value;
            return (bool)probe.Invoke(control, null) ? value : Color.Empty;
        }

        private static bool IsNeutral(Color color)
        {
            return color == SystemColors.Control
                || color == SystemColors.ButtonFace
                || color == SystemColors.Window
                || color == SystemColors.ControlLight;
        }

        private static bool IsHardwareShade(Color color)
        {
            return color == SystemColors.ControlDark
                || color == SystemColors.ControlDarkDark
                || color == SystemColors.ControlLight;
        }

        private static bool IsTextColor(Color color)
        {
            return color == SystemColors.ControlText
                || color == SystemColors.WindowText
                || color.ToArgb() == Color.Black.ToArgb();
        }

        private static Snapshot Capture(Control control)
        {
            Snapshot s = new Snapshot
            {
                OwnBack = Own(control, _ownBack, control.BackColor),
                OwnFore = Own(control, _ownFore, control.ForeColor),
                Role = Role.Plain
            };

            if (control is Form)
                s.Role = Role.Form;
            else if (control is GroupBox)
                s.Role = GroupRole((GroupBox)control, s);
            else if (control is Button)
            {
                Button button = (Button)control;
                s.Flat = button.FlatStyle;
                s.VisualBack = button.UseVisualStyleBackColor;
                if (button.FlatStyle == FlatStyle.Flat)
                    s.Role = Role.None;
                else if (button.UseVisualStyleBackColor || s.OwnBack.IsEmpty || IsNeutral(s.OwnBack))
                    s.Role = Role.ButtonNeutral;
                else
                    s.Role = Role.ButtonColored;
            }
            else if (control is TextBox)
            {
                TextBox box = (TextBox)control;
                s.Border = box.BorderStyle;
                s.Role = box.ReadOnly || box.BackColor == Color.Blue ? Role.Measured : Role.Setpoint;
            }
            else if (control is NumericUpDown)
            {
                s.Border = ((NumericUpDown)control).BorderStyle;
                s.Role = Role.Setpoint;
            }
            else if (control is ComboBox)
            {
                s.Flat = ((ComboBox)control).FlatStyle;
                s.Role = Role.Combo;
            }
            else if (control is PictureBox)
                s.Role = PictureRole((PictureBox)control, s);
            else if (control is Chart)
            {
                s.Role = Role.Chart;
                s.Chart = CaptureChart((Chart)control);
            }
            else if (control is ListBox)
            {
                s.Border = ((ListBox)control).BorderStyle;
                s.Role = Role.List;
            }
            else if (control is ListView)
            {
                s.Border = ((ListView)control).BorderStyle;
                s.Grid = ((ListView)control).GridLines;
                s.Role = Role.List;
            }
            else if (control is Label)
            {
                s.Border = ((Label)control).BorderStyle;
                if (s.OwnBack == SystemColors.ControlDark)
                    s.Role = Role.Hardware;
            }
            else if (control is RichTextBox)
            {
                s.Border = ((RichTextBox)control).BorderStyle;
                s.Role = IsHardwareShade(s.OwnBack) ? Role.Hardware : Role.None;
            }
            else if (control is ToolStrip || control is ScrollBar)
                s.Role = Role.None;

            return s;
        }

        private static Role GroupRole(GroupBox box, Snapshot s)
        {
            Color back = s.OwnBack;
            if (back == SystemColors.InactiveCaption || back == SystemColors.ControlLight)
                return Role.Inset;
            if (back == SystemColors.GradientActiveCaption || back == SystemColors.ActiveCaption)
                return Role.Section;
            if (!(box.Parent is GroupBox) && (s.OwnBack.IsEmpty || IsNeutral(s.OwnBack)))
                return Role.Section;
            return Role.Plain;
        }

        private static Role PictureRole(PictureBox picture, Snapshot s)
        {
            if (picture.Image != null && picture.Image == Res.Logo)
                return Role.Logo;
            if (picture.Image != null || picture.BackgroundImage != null)
                return Role.None;
            if (!s.OwnBack.IsEmpty && s.OwnBack.ToArgb() == Color.Black.ToArgb())
                return Role.Pipe;
            if (s.OwnBack == SystemColors.Control)
                return Role.Track;
            return Role.None;
        }

        private static ChartSnapshot CaptureChart(Chart chart)
        {
            ChartSnapshot c = new ChartSnapshot { Back = chart.BackColor };
            foreach (ChartArea area in chart.ChartAreas)
            {
                c.AreaBack[area] = area.BackColor;
                foreach (Axis axis in area.Axes)
                    c.Axes[axis] = new[]
                    {
                        axis.LineColor, axis.MajorGrid.LineColor, axis.MajorTickMark.LineColor,
                        axis.LabelStyle.ForeColor, axis.TitleForeColor
                    };
            }
            foreach (Legend legend in chart.Legends)
                c.Legends[legend] = new[] { legend.ForeColor, legend.BackColor };
            foreach (Series series in chart.Series)
                c.Series[series] = series.Color;
            return c;
        }

        private static void Hook(Control control, Snapshot s)
        {
            switch (s.Role)
            {
                case Role.Form:
                    control.HandleCreated += (sender, e) => SetDarkTitle((Form)control);
                    break;
                case Role.Section:
                case Role.Inset:
                case Role.Plain:
                    if (control is GroupBox)
                        control.Paint += PaintGroupBox;
                    break;
                case Role.ButtonNeutral:
                case Role.ButtonColored:
                    control.BackColorChanged += (sender, e) => UpdateButtonStates((Button)control);
                    break;
                case Role.Setpoint:
                    NumericUpDown numeric = control as NumericUpDown;
                    if (numeric != null)
                        foreach (Control part in numeric.Controls)
                            if (!(part is TextBoxBase))
                                part.Paint += PaintSpinner;
                    break;
                case Role.List:
                    ListView list = control as ListView;
                    if (list != null)
                    {
                        list.DrawColumnHeader += DrawListHeader;
                        list.DrawItem += (sender, e) => e.DrawDefault = !Current.Dark;
                        list.DrawSubItem += DrawListCell;
                    }
                    break;
            }

            if (s.Role == Role.List || control is ScrollBar || (control is TextBox && ((TextBox)control).Multiline))
                control.HandleCreated += (sender, e) => SetScrollTheme(control);
        }

        private static void Paint(Control control, Snapshot s, Palette p, Palette previous)
        {
            switch (s.Role)
            {
                case Role.None:
                    if (control is ScrollBar)
                        SetScrollTheme(control);
                    return;
                case Role.Form:
                    if (p.Classic || !(s.OwnBack.IsEmpty || IsNeutral(s.OwnBack) || s.OwnBack.ToArgb() == Color.LightGray.ToArgb()))
                        RestoreBack(control, s);
                    else
                        control.BackColor = p.Window;
                    if (p.Classic || !(s.OwnFore.IsEmpty || IsTextColor(s.OwnFore)))
                        RestoreFore(control, s);
                    else
                        control.ForeColor = p.Text;
                    SetDarkTitle((Form)control);
                    return;
                case Role.Section:
                case Role.Inset:
                    if (p.Classic)
                        RestoreBack(control, s);
                    else
                        control.BackColor = s.Role == Role.Section ? p.Section : p.Inset;
                    PaintFore(control, s, p);
                    control.Invalidate();
                    return;
                case Role.Plain:
                    PaintPlain(control, s, p);
                    return;
                case Role.ButtonNeutral:
                case Role.ButtonColored:
                    PaintButton((Button)control, s, p, previous);
                    return;
                case Role.Measured:
                case Role.Setpoint:
                    PaintField(control, s, p);
                    return;
                case Role.Combo:
                    ComboBox combo = (ComboBox)control;
                    if (p.Classic)
                    {
                        RestoreBack(combo, s);
                        RestoreFore(combo, s);
                        combo.FlatStyle = s.Flat;
                    }
                    else
                    {
                        combo.BackColor = p.InputBack;
                        combo.ForeColor = p.InputText;
                        combo.FlatStyle = FlatStyle.Flat;
                    }
                    return;
                case Role.Pipe:
                    control.BackColor = p.Classic ? Color.Black : p.Pipe;
                    return;
                case Role.Track:
                    if (p.Classic)
                        RestoreBack(control, s);
                    else
                        control.BackColor = p.Inset;
                    return;
                case Role.Logo:
                    ((PictureBox)control).Image = p.Dark ? LightLogo(p) : Res.Logo;
                    return;
                case Role.List:
                    PaintList(control, s, p);
                    return;
                case Role.Chart:
                    PaintChart((Chart)control, s.Chart, p);
                    return;
                case Role.Hardware:
                    PaintHardware(control, s, p);
                    return;
            }
        }

        private static void PaintHardware(Control control, Snapshot s, Palette p)
        {
            RichTextBox box = control as RichTextBox;
            if (p.Classic || p.Body.IsEmpty)
            {
                RestoreBack(control, s);
                RestoreFore(control, s);
                if (box != null)
                    box.BorderStyle = s.Border;
                return;
            }

            if (s.OwnBack == SystemColors.ControlDarkDark)
                control.BackColor = p.BodyShade;
            else if (s.OwnBack == SystemColors.ControlLight)
                control.BackColor = p.BodyLight;
            else
                control.BackColor = p.Body;

            if (box != null)
                box.BorderStyle = BorderStyle.None;
            else
                control.ForeColor = p.BodyText;
        }

        private static void RestoreBack(Control control, Snapshot s)
        {
            if (s.OwnBack.IsEmpty)
                control.ResetBackColor();
            else
                control.BackColor = s.OwnBack;
        }

        private static void RestoreFore(Control control, Snapshot s)
        {
            if (s.OwnFore.IsEmpty)
                control.ResetForeColor();
            else
                control.ForeColor = s.OwnFore;
        }

        private static bool OwnsOpaqueBack(Snapshot s)
        {
            return !s.OwnBack.IsEmpty && s.OwnBack.A == 255 && !IsNeutral(s.OwnBack);
        }

        private static void PaintFore(Control control, Snapshot s, Palette p)
        {
            if (p.Classic || s.OwnFore.IsEmpty || OwnsOpaqueBack(s))
            {
                RestoreFore(control, s);
                return;
            }

            if (IsTextColor(s.OwnFore) || (p.Dark && Luminance(s.OwnFore) < 0.12))
                control.ForeColor = p.Text;
            else
                control.ForeColor = s.OwnFore;
        }

        private static void PaintPlain(Control control, Snapshot s, Palette p)
        {
            if (p.Classic || !(s.OwnBack.IsEmpty || IsNeutral(s.OwnBack)))
                RestoreBack(control, s);
            else
                control.ResetBackColor();
            PaintFore(control, s, p);

            Label label = control as Label;
            if (label != null)
                label.BorderStyle = !p.Classic && s.Border == BorderStyle.Fixed3D ? BorderStyle.FixedSingle : s.Border;

            if (control is GroupBox)
                control.Invalidate();
        }

        private static void PaintButton(Button button, Snapshot s, Palette p, Palette previous)
        {
            Color back = button.BackColor;
            bool neutralNow = button.UseVisualStyleBackColor || IsNeutral(back)
                || (previous != null && back == previous.ButtonBack)
                || (previous == null && s.Role == Role.ButtonNeutral);

            if (p.Classic)
            {
                button.FlatStyle = s.Flat;
                button.UseVisualStyleBackColor = s.VisualBack;
                if (s.Role == Role.ButtonNeutral && neutralNow)
                    RestoreBack(button, s);
                if (s.Role == Role.ButtonNeutral)
                    RestoreFore(button, s);
                return;
            }

            if (s.Role == Role.ButtonNeutral)
            {
                if (neutralNow)
                    button.BackColor = p.ButtonBack;
                if (s.OwnFore.IsEmpty || IsTextColor(s.OwnFore))
                    button.ForeColor = p.Text;
            }

            button.UseVisualStyleBackColor = false;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = p.ButtonBorder;
            UpdateButtonStates(button);
        }

        private static void UpdateButtonStates(Button button)
        {
            Palette p = Current;
            Color back = button.BackColor;
            if (back == p.ButtonBack)
            {
                button.FlatAppearance.MouseOverBackColor = p.ButtonHover;
                button.FlatAppearance.MouseDownBackColor = p.ButtonPressed;
            }
            else if (back.A == 255)
            {
                button.FlatAppearance.MouseOverBackColor = Shade(back, 0.9f);
                button.FlatAppearance.MouseDownBackColor = Shade(back, 0.8f);
            }
        }

        private static Color Shade(Color color, float factor)
        {
            return Color.FromArgb(color.A, (int)(color.R * factor), (int)(color.G * factor), (int)(color.B * factor));
        }

        private static void PaintField(Control control, Snapshot s, Palette p)
        {
            bool measured = s.Role == Role.Measured;
            if (p.Classic)
            {
                RestoreBack(control, s);
                RestoreFore(control, s);
            }
            else
            {
                control.BackColor = measured ? p.ValueBack : p.InputBack;
                control.ForeColor = measured ? p.ValueText : p.InputText;
            }

            BorderStyle border = !p.Classic && s.Border == BorderStyle.Fixed3D ? BorderStyle.FixedSingle : s.Border;
            TextBox box = control as TextBox;
            if (box != null)
            {
                box.BorderStyle = border;
                if (box.Multiline)
                    SetScrollTheme(box);
            }

            NumericUpDown numeric = control as NumericUpDown;
            if (numeric != null)
            {
                numeric.BorderStyle = border;
                foreach (Control part in numeric.Controls)
                    part.Invalidate();
            }
        }

        private static void PaintList(Control control, Snapshot s, Palette p)
        {
            if (p.Classic)
            {
                RestoreBack(control, s);
                RestoreFore(control, s);
            }
            else
            {
                control.BackColor = p.ValueBack;
                control.ForeColor = p.Text;
            }

            BorderStyle border = !p.Classic && s.Border == BorderStyle.Fixed3D ? BorderStyle.FixedSingle : s.Border;
            ListBox listBox = control as ListBox;
            if (listBox != null)
                listBox.BorderStyle = border;

            ListView listView = control as ListView;
            if (listView != null)
            {
                listView.BorderStyle = border;
                listView.OwnerDraw = !p.Classic && listView.View == View.Details;
                listView.GridLines = s.Grid && !p.Dark;
            }

            SetScrollTheme(control);
        }

        private static void PaintChart(Chart chart, ChartSnapshot c, Palette p)
        {
            foreach (Series series in chart.Series)
                if (!c.Series.ContainsKey(series))
                    c.Series[series] = series.Color;

            if (p.Classic)
            {
                chart.BackColor = c.Back;
                foreach (KeyValuePair<ChartArea, Color> area in c.AreaBack)
                    area.Key.BackColor = area.Value;
                foreach (KeyValuePair<Axis, Color[]> axis in c.Axes)
                {
                    axis.Key.LineColor = axis.Value[0];
                    axis.Key.MajorGrid.LineColor = axis.Value[1];
                    axis.Key.MajorTickMark.LineColor = axis.Value[2];
                    axis.Key.LabelStyle.ForeColor = axis.Value[3];
                    axis.Key.TitleForeColor = axis.Value[4];
                }
                foreach (KeyValuePair<Legend, Color[]> legend in c.Legends)
                {
                    legend.Key.ForeColor = legend.Value[0];
                    legend.Key.BackColor = legend.Value[1];
                }
            }
            else
            {
                chart.BackColor = p.ChartBack;
                foreach (ChartArea area in chart.ChartAreas)
                {
                    area.BackColor = p.ChartBack;
                    foreach (Axis axis in area.Axes)
                    {
                        axis.LineColor = p.ChartAxis;
                        axis.MajorGrid.LineColor = p.ChartGrid;
                        axis.MajorTickMark.LineColor = p.ChartAxis;
                        axis.LabelStyle.ForeColor = p.Text;
                        axis.TitleForeColor = p.Text;
                    }
                }
                foreach (Legend legend in chart.Legends)
                {
                    legend.ForeColor = p.Text;
                    legend.BackColor = Color.Transparent;
                }
            }

            foreach (KeyValuePair<Series, Color> series in c.Series)
            {
                if (series.Key.Color.A == 0 || series.Value.A == 0)
                    continue;
                series.Key.Color = p.Dark ? Readable(series.Value) : series.Value;
            }
        }

        private static double Linear(int channel)
        {
            double c = channel / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        private static double Luminance(Color color)
        {
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }

        private static Color Blend(Color color, Color with, double amount)
        {
            return Color.FromArgb(
                color.A,
                (int)Math.Round(color.R + (with.R - color.R) * amount),
                (int)Math.Round(color.G + (with.G - color.G) * amount),
                (int)Math.Round(color.B + (with.B - color.B) * amount));
        }

        private static Color Readable(Color color)
        {
            Color result = color;
            for (double amount = 0.1; Luminance(result) < MinChartLuminance && amount <= 1.0; amount += 0.1)
                result = Blend(color, Color.White, amount);
            return result;
        }

        private static Image LightLogo(Palette p)
        {
            if (_logoLight != null || Res.Logo == null)
                return _logoLight ?? Res.Logo;

            Bitmap source = new Bitmap(Res.Logo);
            Bitmap light = new Bitmap(source.Width, source.Height);
            for (int y = 0; y < source.Height; y++)
                for (int x = 0; x < source.Width; x++)
                {
                    Color pixel = source.GetPixel(x, y);
                    light.SetPixel(x, y, Color.FromArgb(pixel.A, p.Title));
                }
            source.Dispose();
            _logoLight = light;
            return _logoLight;
        }

        private static void SetDarkTitle(Form form)
        {
            if (!form.IsHandleCreated || !form.TopLevel)
                return;
            int value = Current.Dark ? 1 : 0;
            try
            {
                DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref value, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static void SetScrollTheme(Control control)
        {
            if (!control.IsHandleCreated)
                return;
            try
            {
                SetWindowTheme(control.Handle, Current.Dark ? "DarkMode_Explorer" : null, null);
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private static void PaintGroupBox(object sender, PaintEventArgs e)
        {
            Palette p = Current;
            if (p.Classic)
                return;

            GroupBox box = (GroupBox)sender;
            Graphics g = e.Graphics;
            Rectangle client = box.ClientRectangle;

            using (SolidBrush back = new SolidBrush(box.BackColor))
                g.FillRectangle(back, client);

            string text = box.Text ?? string.Empty;
            string trimmed = text.TrimStart();
            Size full = text.Length == 0 ? Size.Empty : TextRenderer.MeasureText(g, text, box.Font, Size.Empty, GroupTextFlags);
            Size shown = trimmed.Length == 0 ? Size.Empty : TextRenderer.MeasureText(g, trimmed, box.Font, Size.Empty, GroupTextFlags);

            int lineY = full.Height / 2;
            using (Pen pen = new Pen(p.Border))
                g.DrawRectangle(pen, 0, lineY, client.Width - 1, client.Height - lineY - 1);

            if (trimmed.Length == 0)
                return;

            int x = GroupTextLeft + full.Width - shown.Width;
            using (SolidBrush back = new SolidBrush(box.BackColor))
                g.FillRectangle(back, x - 3, 0, shown.Width + 6, full.Height);

            Color fore;
            if (!box.Enabled)
                fore = SystemColors.GrayText;
            else if (box.ForeColor == p.Text)
                fore = p.Title;
            else
                fore = box.ForeColor;
            TextRenderer.DrawText(g, trimmed, box.Font, new Point(x, 0), fore, GroupTextFlags);
        }

        private static void PaintSpinner(object sender, PaintEventArgs e)
        {
            Palette p = Current;
            if (!p.Dark)
                return;

            Control spinner = (Control)sender;
            Rectangle area = spinner.ClientRectangle;
            Graphics g = e.Graphics;

            using (SolidBrush back = new SolidBrush(p.ButtonBack))
                g.FillRectangle(back, area);

            int half = area.Height / 2;
            using (Pen line = new Pen(p.Border))
            {
                g.DrawLine(line, 0, 0, 0, area.Height);
                g.DrawLine(line, 0, half, area.Width, half);
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush arrow = new SolidBrush(spinner.Enabled ? p.Text : p.Border))
            {
                float cx = area.Width / 2f + 0.5f;
                float w = Math.Max(3f, area.Width / 4f);
                float h = Math.Max(2f, half / 4f);
                float upY = half / 2f;
                float downY = half + (area.Height - half) / 2f;
                g.FillPolygon(arrow, new[] { new PointF(cx - w, upY + h), new PointF(cx + w, upY + h), new PointF(cx, upY - h) });
                g.FillPolygon(arrow, new[] { new PointF(cx - w, downY - h), new PointF(cx + w, downY - h), new PointF(cx, downY + h) });
            }
        }

        private static void DrawListHeader(object sender, DrawListViewColumnHeaderEventArgs e)
        {
            Palette p = Current;
            if (p.Classic)
            {
                e.DrawDefault = true;
                return;
            }

            ListView list = (ListView)sender;
            using (SolidBrush back = new SolidBrush(p.Inset))
                e.Graphics.FillRectangle(back, e.Bounds);
            using (Pen line = new Pen(p.Border))
            {
                e.Graphics.DrawLine(line, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom);
                e.Graphics.DrawLine(line, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
            }

            Rectangle text = Rectangle.Inflate(e.Bounds, -4, 0);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, list.Font, text, p.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);
        }

        private static void DrawListCell(object sender, DrawListViewSubItemEventArgs e)
        {
            Palette p = Current;
            if (!p.Dark)
            {
                e.DrawDefault = true;
                return;
            }

            ListView list = (ListView)sender;
            bool useItem = e.Item.UseItemStyleForSubItems || e.SubItem == null;
            Color back = useItem ? e.Item.BackColor : e.SubItem.BackColor;
            Color fore = useItem ? e.Item.ForeColor : e.SubItem.ForeColor;
            if (e.Item.Selected && (list.Focused || !list.HideSelection))
            {
                back = p.Accent;
                fore = p.AccentText;
            }

            using (SolidBrush brush = new SolidBrush(back))
                e.Graphics.FillRectangle(brush, e.Bounds);

            TextFormatFlags align = TextFormatFlags.Left;
            if (e.Header != null && e.Header.TextAlign == HorizontalAlignment.Center)
                align = TextFormatFlags.HorizontalCenter;
            else if (e.Header != null && e.Header.TextAlign == HorizontalAlignment.Right)
                align = TextFormatFlags.Right;

            string text = e.SubItem != null ? e.SubItem.Text : e.Item.Text;
            TextRenderer.DrawText(e.Graphics, text, list.Font, Rectangle.Inflate(e.Bounds, -4, 0), fore,
                align | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            Snapshot s;
            if (_snapshots.TryGetValue(list, out s) && s.Grid)
            {
                using (Pen pen = new Pen(Blend(p.Border, list.BackColor, 0.35)))
                {
                    e.Graphics.DrawLine(pen, e.Bounds.Right - 1, e.Bounds.Top, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
                    e.Graphics.DrawLine(pen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right - 1, e.Bounds.Bottom - 1);
                }
            }
        }

        public class Renderer : ToolStripProfessionalRenderer
        {
            public Renderer()
                : base(new ToolColors())
            {
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                if (!Current.Classic)
                    e.TextColor = e.Item.Enabled ? Current.Text : Blend(Current.Text, Current.Menu, 0.55);
                base.OnRenderItemText(e);
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                if (!Current.Classic)
                    e.ArrowColor = Current.Text;
                base.OnRenderArrow(e);
            }
        }

        private sealed class ToolColors : ProfessionalColorTable
        {
            private static bool Classic { get { return Current.Classic; } }

            public override Color MenuStripGradientBegin { get { return Classic ? base.MenuStripGradientBegin : Current.Toolbar; } }
            public override Color MenuStripGradientEnd { get { return Classic ? base.MenuStripGradientEnd : Current.Toolbar; } }
            public override Color ToolStripGradientBegin { get { return Classic ? base.ToolStripGradientBegin : Current.Toolbar; } }
            public override Color ToolStripGradientMiddle { get { return Classic ? base.ToolStripGradientMiddle : Current.Toolbar; } }
            public override Color ToolStripGradientEnd { get { return Classic ? base.ToolStripGradientEnd : Current.Toolbar; } }
            public override Color ToolStripBorder { get { return Classic ? base.ToolStripBorder : Current.Border; } }
            public override Color ToolStripDropDownBackground { get { return Classic ? base.ToolStripDropDownBackground : Current.Menu; } }
            public override Color ImageMarginGradientBegin { get { return Classic ? base.ImageMarginGradientBegin : Current.Menu; } }
            public override Color ImageMarginGradientMiddle { get { return Classic ? base.ImageMarginGradientMiddle : Current.Menu; } }
            public override Color ImageMarginGradientEnd { get { return Classic ? base.ImageMarginGradientEnd : Current.Menu; } }
            public override Color MenuBorder { get { return Classic ? base.MenuBorder : Current.Border; } }
            public override Color MenuItemBorder { get { return Classic ? base.MenuItemBorder : Current.ToolbarHover; } }
            public override Color MenuItemSelected { get { return Classic ? base.MenuItemSelected : Current.ToolbarHover; } }
            public override Color MenuItemSelectedGradientBegin { get { return Classic ? base.MenuItemSelectedGradientBegin : Current.ToolbarHover; } }
            public override Color MenuItemSelectedGradientEnd { get { return Classic ? base.MenuItemSelectedGradientEnd : Current.ToolbarHover; } }
            public override Color MenuItemPressedGradientBegin { get { return Classic ? base.MenuItemPressedGradientBegin : Current.ToolbarPressed; } }
            public override Color MenuItemPressedGradientMiddle { get { return Classic ? base.MenuItemPressedGradientMiddle : Current.ToolbarPressed; } }
            public override Color MenuItemPressedGradientEnd { get { return Classic ? base.MenuItemPressedGradientEnd : Current.ToolbarPressed; } }
            public override Color ButtonSelectedHighlight { get { return Classic ? base.ButtonSelectedHighlight : Current.ToolbarHover; } }
            public override Color ButtonSelectedGradientBegin { get { return Classic ? base.ButtonSelectedGradientBegin : Current.ToolbarHover; } }
            public override Color ButtonSelectedGradientMiddle { get { return Classic ? base.ButtonSelectedGradientMiddle : Current.ToolbarHover; } }
            public override Color ButtonSelectedGradientEnd { get { return Classic ? base.ButtonSelectedGradientEnd : Current.ToolbarHover; } }
            public override Color ButtonPressedGradientBegin { get { return Classic ? base.ButtonPressedGradientBegin : Current.ToolbarPressed; } }
            public override Color ButtonPressedGradientMiddle { get { return Classic ? base.ButtonPressedGradientMiddle : Current.ToolbarPressed; } }
            public override Color ButtonPressedGradientEnd { get { return Classic ? base.ButtonPressedGradientEnd : Current.ToolbarPressed; } }
            public override Color CheckBackground { get { return Classic ? base.CheckBackground : Current.ToolbarChecked; } }
            public override Color CheckSelectedBackground { get { return Classic ? base.CheckSelectedBackground : Current.ToolbarChecked; } }
            public override Color CheckPressedBackground { get { return Classic ? base.CheckPressedBackground : Current.ToolbarChecked; } }
            public override Color SeparatorDark { get { return Classic ? base.SeparatorDark : Current.Border; } }
            public override Color SeparatorLight { get { return Classic ? base.SeparatorLight : Current.Toolbar; } }
        }
    }
}
