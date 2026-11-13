using ArdisCVDCore.modules_hw;
using ArdisCVDCore.trends;
using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public partial class MainForm
    {
        private const string LayoutIniSection = "MainForm";
        private const int ToolbarIconSize = 20;
        private const int WindowGlyphSize = 14;
        private const int WindowButtonWidth = 46;
        private const int SchemeMargin = 8;
        private static readonly Font ToolbarFont = new Font("Tahoma", 9F, FontStyle.Regular, GraphicsUnit.Point, 204);
        private const float DefaultTrendShare = 0.4f;
        private const float MinTrendShare = 0.15f;
        private const float MaxTrendShare = 0.75f;
        private const int SplitterWidth = 6;
        private const float MinSchemeFactor = 0.3f;
        private const int DWMWA_CLOAK = 13;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        private const int SplashMinimumMs = 2000;
        private const int SplashMaximumMs = 4000;
        private const int RevealCheckMs = 100;

        private readonly Stopwatch _startupClock = Stopwatch.StartNew();
        private bool _startupCloaked = true;
        private bool _startupDataShown;
        private Timer _revealTimer;

        public event EventHandler Revealed;

        private Panel _workspace;
        private Panel _canvas;
        private TrendDock _trendDock;
        private Panel _splitter;
        private float _trendShare = DefaultTrendShare;
        private int _splitterGrab = -1;
        private SchemeScaler _scaler;
        private bool _fullScreen;
        private ToolStripMenuItem _fullScreenMenu;
        private ToolStripMenuItem _sideBySideMenu;
        private ToolStripMenuItem _minimizeMenu;
        private ToolStripMenuItem _closeMenu;

        private void BuildWorkspace()
        {
            AutoScaleMode = AutoScaleMode.None;

            SuspendLayout();

            _workspace = new Panel { Dock = DockStyle.Fill, BackColor = BackColor };
            _canvas = new Panel { BackColor = BackColor };

            Control[] scheme = new Control[Controls.Count];
            Controls.CopyTo(scheme, 0);

            Rectangle content = Rectangle.Empty;
            foreach (Control control in scheme)
                if (control != menuStrip1)
                    content = content.IsEmpty ? control.Bounds : Rectangle.Union(content, control.Bounds);

            foreach (Control control in scheme)
            {
                if (control == menuStrip1)
                    continue;
                Controls.Remove(control);
                control.Location = new Point(control.Left - content.Left, control.Top - content.Top);
                _canvas.Controls.Add(control);
            }

            Size design = content.Size;
            _canvas.Size = design;

            _trendDock = new TrendDock { Visible = false, BackColor = BackColor };
            _trendDock.DropZone = TrendDropZone;
            _trendDock.TrendsChanged += (s, e) =>
            {
                LayoutWorkspace();
                UpdateTrendButtons();
            };

            _splitter = new Panel
            {
                Visible = false,
                Width = SplitterWidth,
                Cursor = Cursors.VSplit,
                BackColor = SystemColors.ControlDark
            };
            _splitter.MouseDown += Splitter_MouseDown;
            _splitter.MouseMove += Splitter_MouseMove;
            _splitter.MouseUp += (s, e) => FinishSplitterDrag();
            _splitter.MouseCaptureChanged += (s, e) => FinishSplitterDrag();

            _workspace.Controls.Add(_splitter);
            _workspace.Controls.Add(_canvas);
            _workspace.Controls.Add(_trendDock);
            _workspace.Resize += (s, e) => LayoutWorkspace();

            Controls.Add(_workspace);
            _workspace.BringToFront();

            ResumeLayout(false);

            _scaler = new SchemeScaler(_canvas, design);
            _scaler.SetCenterColumn(Gas_groupBox.Right, Microwave_groupBox.Left);

            MaximizeBox = true;
            MinimumSize = new Size(800, 560);
            StartPosition = FormStartPosition.CenterScreen;

            AddLayoutMenu();
        }

        private void AddLayoutMenu()
        {
            _fullScreenMenu = new ToolStripMenuItem("Full Screen")
            {
                ShortcutKeys = Keys.F11,
                Image = Res.Glyph('', SystemColors.ControlText, 20)
            };
            _fullScreenMenu.Click += (s, e) =>
            {
                SetFullScreen(!_fullScreen);
                IniWriter.INI.Write(LayoutIniSection, "FullScreen", _fullScreen ? "1" : "0");
            };

            _sideBySideMenu = new ToolStripMenuItem("Graphs Side by Side")
            {
                Image = Res.Glyph('', SystemColors.ControlText, 20)
            };
            _sideBySideMenu.Click += (s, e) =>
            {
                _trendDock.SideBySide = !_trendDock.SideBySide;
                _sideBySideMenu.Checked = _trendDock.SideBySide;
                IniWriter.INI.Write(LayoutIniSection, "TrendsSideBySide", _trendDock.SideBySide ? "1" : "0");
            };

            viewToolStripMenuItem.DropDownItems.Add(new ToolStripSeparator());
            viewToolStripMenuItem.DropDownItems.Add(_sideBySideMenu);
            viewToolStripMenuItem.DropDownItems.Add(_fullScreenMenu);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (_startupCloaked)
                SetCloaked(true);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            SuperCycle.Tick += (s, a) =>
            {
                if (_startupCloaked && AllPlcClientsConnected())
                    _startupDataShown = true;
            };

            _revealTimer = new Timer { Interval = RevealCheckMs };
            _revealTimer.Tick += (s, a) =>
            {
                long elapsed = _startupClock.ElapsedMilliseconds;
                if (elapsed < SplashMinimumMs || (!_startupDataShown && elapsed < SplashMaximumMs))
                    return;

                _revealTimer.Stop();
                _revealTimer.Dispose();
                Reveal();
            };
            _revealTimer.Start();
        }

        private static bool AllPlcClientsConnected()
        {
            return PLC210PidClient.GetState().Connected
                && PLC210ThyracontClient.GetState().Connected
                && PLC210GasFlowClient.GetState().Connected
                && PLC210PyrometerClient.GetState().Connected
                && PLC210MicrowaveClient.GetState().Connected
                && PLC210GasValveClient.GetState().Connected
                && PLC210VacuumClient.GetState().Connected
                && PLC210CoolingClient.GetState().Connected
                && PLC210TurboPumpClient.GetState().Connected
                && PLC210AlarmClient.GetState().Connected;
        }

        private void Reveal()
        {
            if (!_startupCloaked)
                return;

            Refresh();
            _startupCloaked = false;
            SetCloaked(false);

            EventHandler handler = Revealed;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }

        private void SetCloaked(bool cloaked)
        {
            int value = cloaked ? 1 : 0;
            try
            {
                DwmSetWindowAttribute(Handle, DWMWA_CLOAK, ref value, sizeof(int));
            }
            catch (DllNotFoundException)
            {
            }
            catch (EntryPointNotFoundException)
            {
            }
        }

        private void ApplySavedWindowMode()
        {
            _trendDock.SideBySide = ReadIniString(LayoutIniSection, "TrendsSideBySide", "0") == "1";
            _sideBySideMenu.Checked = _trendDock.SideBySide;

            float share;
            if (float.TryParse(ReadIniString(LayoutIniSection, "TrendShare", ""), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out share))
                _trendShare = ClampTrendShare(share);

            SetFullScreen(ReadIniString(LayoutIniSection, "FullScreen", "1") == "1");
        }

        private void SetFullScreen(bool fullScreen)
        {
            _fullScreen = fullScreen;
            _fullScreenMenu.Checked = fullScreen;
            _minimizeMenu.Visible = fullScreen;
            _closeMenu.Visible = fullScreen;

            WindowState = FormWindowState.Normal;
            FormBorderStyle = fullScreen ? FormBorderStyle.None : FormBorderStyle.Sizable;
            WindowState = FormWindowState.Maximized;
        }

        private void LayoutWorkspace()
        {
            if (_workspace == null)
                return;

            Rectangle area = _workspace.ClientRectangle;
            if (area.Width <= 0 || area.Height <= 0)
                return;

            if (_trendDock.HasTrends)
            {
                int width = (int)(area.Width * _trendShare);
                _trendDock.Bounds = new Rectangle(area.Right - width, area.Top, width, area.Height);
                _splitter.Bounds = new Rectangle(area.Right - width - SplitterWidth, area.Top, SplitterWidth, area.Height);
                _trendDock.Visible = true;
                _splitter.Visible = true;
                area.Width -= width + SplitterWidth;
            }
            else
            {
                _trendDock.Visible = false;
                _splitter.Visible = false;
            }

            area.Inflate(-SchemeMargin, -SchemeMargin);
            if (area.Width <= 0 || area.Height <= 0)
                return;

            Size design = _scaler.DesignSize;
            float scaleX = Math.Max(MinSchemeFactor, (float)area.Width / design.Width);
            float scaleY = Math.Max(MinSchemeFactor, (float)area.Height / design.Height);
            _scaler.Apply(scaleX, scaleY);

            _canvas.Bounds = area;
        }

        private Rectangle TrendDropZone()
        {
            Rectangle area = _workspace.ClientRectangle;
            int width = (int)(area.Width * _trendShare);
            return _workspace.RectangleToScreen(new Rectangle(area.Right - width, area.Top, width, area.Height));
        }

        private static float ClampTrendShare(float share)
        {
            return Math.Max(MinTrendShare, Math.Min(MaxTrendShare, share));
        }

        private void Splitter_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            _splitterGrab = e.X;
            _splitter.BringToFront();
        }

        private void Splitter_MouseMove(object sender, MouseEventArgs e)
        {
            if (_splitterGrab < 0)
                return;

            int width = _workspace.ClientSize.Width;
            int left = _splitter.Left + e.X - _splitterGrab;
            int min = (int)(width * (1 - MaxTrendShare)) - SplitterWidth;
            int max = (int)(width * (1 - MinTrendShare)) - SplitterWidth;
            _splitter.Left = Math.Max(min, Math.Min(max, left));
        }

        private void FinishSplitterDrag()
        {
            if (_splitterGrab < 0)
                return;

            _splitterGrab = -1;

            int width = _workspace.ClientSize.Width;
            if (width <= 0)
                return;

            _trendShare = ClampTrendShare((float)(width - _splitter.Right) / width);
            IniWriter.INI.Write(LayoutIniSection, "TrendShare",
                _trendShare.ToString("0.###", CultureInfo.InvariantCulture));
            LayoutWorkspace();
        }

        private void BuildToolbar()
        {
            settingsToolStripMenuItem.DropDownItems.Remove(ProcessParametersToolMenu);
            settingsToolStripMenuItem.Visible = false;

            ToolStripMenuItem[] trends =
            {
                GasTrendToolMenu, PressureTrendToolMenu, MWPowerToolStripMenuItem, temperatureTrendToolStripMenuItem
            };
            foreach (ToolStripMenuItem item in trends)
                viewToolStripMenuItem.DropDownItems.Remove(item);
            viewToolStripMenuItem.DropDownItems.Remove(PIDToolStripMenuItem);
            viewToolStripMenuItem.DropDownItems.Remove(StatusTrendSeparator);

            menuStrip1.ImageScalingSize = new Size(ToolbarIconSize, ToolbarIconSize);

            StyleToolbarItem(fileToolStripMenuItem, '\uE8B7');
            StyleToolbarItem(viewToolStripMenuItem, '\uE890');

            menuStrip1.Items.Remove(aboutToolStripMenuItem);

            menuStrip1.Items.Add(new ToolStripSeparator());
            menuStrip1.Items.Add(ProcessParametersToolMenu);
            StyleToolbarItem(ProcessParametersToolMenu, '\uE9E9');
            menuStrip1.Items.Add(PIDToolStripMenuItem);
            StyleToolbarItem(PIDToolStripMenuItem, '\uE9D9');

            menuStrip1.Items.Add(new ToolStripSeparator());
            menuStrip1.Items.Add(GasTrendToolMenu);
            StyleToolbarItem(GasTrendToolMenu, '\uE9D2');
            menuStrip1.Items.Add(PressureTrendToolMenu);
            StyleToolbarItem(PressureTrendToolMenu, '\uEC4A');
            menuStrip1.Items.Add(MWPowerToolStripMenuItem);
            StyleToolbarItem(MWPowerToolStripMenuItem, '\uE945');
            menuStrip1.Items.Add(temperatureTrendToolStripMenuItem);
            StyleToolbarItem(temperatureTrendToolStripMenuItem, '\uE9CA');

            menuStrip1.Items.Add(new ToolStripSeparator());
            menuStrip1.Items.Add(aboutToolStripMenuItem);
            StyleToolbarItem(aboutToolStripMenuItem, '\uE946');

            menuStrip1.Padding = new Padding(SchemeMargin, 0, 0, 0);

            _closeMenu = AddWindowButton('\uE8BB', (s, e) => Close());
            _minimizeMenu = AddWindowButton('\uE921', (s, e) => WindowState = FormWindowState.Minimized);
            menuStrip1.Renderer = new ToolbarRenderer(_closeMenu, Res.Glyph('\uE8BB', Color.White, WindowGlyphSize));

            SizeWindowButtons();
        }

        private void SizeWindowButtons()
        {
            int height = fileToolStripMenuItem.GetPreferredSize(Size.Empty).Height;
            if (height <= 0)
                return;

            foreach (ToolStripMenuItem item in new[] { _minimizeMenu, _closeMenu })
            {
                item.AutoSize = false;
                item.Size = new Size(WindowButtonWidth, height);
            }
        }

        private sealed class ToolbarRenderer : ToolStripProfessionalRenderer
        {
            private readonly ToolStripItem _close;
            private readonly Image _closeHotGlyph;

            public ToolbarRenderer(ToolStripItem close, Image closeHotGlyph)
            {
                _close = close;
                _closeHotGlyph = closeHotGlyph;
            }

            private bool IsHotClose(ToolStripItem item)
            {
                return item == _close && (item.Selected || item.Pressed);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!IsHotClose(e.Item))
                {
                    base.OnRenderMenuItemBackground(e);
                    return;
                }

                using (SolidBrush brush = new SolidBrush(e.Item.Pressed ? Res.ClosePressed : Res.CloseHot))
                    e.Graphics.FillRectangle(brush, new Rectangle(Point.Empty, e.Item.Size));
            }

            protected override void OnRenderItemImage(ToolStripItemImageRenderEventArgs e)
            {
                if (IsHotClose(e.Item) && _closeHotGlyph != null)
                {
                    e.Graphics.DrawImage(_closeHotGlyph, e.ImageRectangle);
                    return;
                }

                base.OnRenderItemImage(e);
            }
        }

        private ToolStripMenuItem AddWindowButton(char glyph, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem
            {
                Alignment = ToolStripItemAlignment.Right,
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                Image = Res.Glyph(glyph, SystemColors.ControlText, WindowGlyphSize),
                ImageScaling = ToolStripItemImageScaling.None,
                Visible = _fullScreen
            };
            item.Click += click;
            menuStrip1.Items.Add(item);
            return item;
        }

        private void StyleToolbarItem(ToolStripMenuItem item, char glyph)
        {
            item.Font = ToolbarFont;
            item.Image = Res.Glyph(glyph, SystemColors.ControlText, ToolbarIconSize);
            item.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            item.TextImageRelation = TextImageRelation.ImageAboveText;
            item.ImageScaling = ToolStripItemImageScaling.SizeToFit;
            item.Padding = new Padding(8, 4, 8, 3);
        }

        private void UpdateTrendButtons()
        {
            MarkOpen(GasTrendToolMenu, _gasTrendForm);
            MarkOpen(PressureTrendToolMenu, _pressureTrendForm);
            MarkOpen(MWPowerToolStripMenuItem, _mwPowerTrendForm);
            MarkOpen(temperatureTrendToolStripMenuItem, _temperatureTrendForm);
        }

        private static void MarkOpen(ToolStripMenuItem item, Form form)
        {
            bool open = form != null && !form.IsDisposed;
            item.BackColor = open ? SystemColors.GradientActiveCaption : Color.Empty;
        }

        private T ToggleTrend<T>(T existing) where T : TrendForm, new()
        {
            if (existing != null && !existing.IsDisposed)
            {
                _trendDock.CloseTrend(existing);
                return null;
            }

            T created = new T();
            _trendDock.ShowTrend(created);
            return created;
        }

        private T ShowSingleton<T>(T existing) where T : Form, new()
        {
            if (existing == null || existing.IsDisposed)
            {
                T created = new T();
                CenterOverMainWindow(created);
                created.Show(this);
                return created;
            }

            if (existing.WindowState == FormWindowState.Minimized)
                existing.WindowState = FormWindowState.Normal;

            existing.Activate();
            return existing;
        }

        private void CenterOverMainWindow(Form form)
        {
            Rectangle area = _fullScreen
                ? Screen.FromControl(this).Bounds
                : Screen.FromControl(this).WorkingArea;
            Rectangle owner = WindowState == FormWindowState.Minimized ? area : Bounds;

            int x = owner.X + (owner.Width - form.Width) / 2;
            int y = owner.Y + (owner.Height - form.Height) / 2;
            x = Math.Max(area.Left, Math.Min(x, area.Right - form.Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - form.Height));

            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(x, y);
        }
    }
}
