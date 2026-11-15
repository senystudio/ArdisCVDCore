using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class ThemeForm : Form
    {
        private const int CardWidth = 210;
        private const int CardHeight = 176;
        private const int Gap = 14;
        private const int ButtonWidth = 92;
        private const int ButtonHeight = 30;

        private readonly ThemeCard[] _cards;

        public ThemeForm()
        {
            Text = "Theme";
            Icon = Res.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(Gap + Theme.All.Length * (CardWidth + Gap), Gap + CardHeight + Gap + ButtonHeight + Gap);

            _cards = new ThemeCard[Theme.All.Length];
            for (int i = 0; i < Theme.All.Length; i++)
            {
                ThemeCard card = new ThemeCard(Theme.All[i])
                {
                    Location = new Point(Gap + i * (CardWidth + Gap), Gap),
                    Size = new Size(CardWidth, CardHeight)
                };
                card.Click += (s, e) => Theme.Select(card.Palette);
                _cards[i] = card;
                Controls.Add(card);
            }

            Button close = new Button
            {
                Text = "Close",
                Size = new Size(ButtonWidth, ButtonHeight),
                Location = new Point(ClientSize.Width - Gap - ButtonWidth, Gap + CardHeight + Gap)
            };
            close.Click += (s, e) => Close();
            CancelButton = close;
            Controls.Add(close);

            Theme.Changed += Theme_Changed;
            FormClosed += (s, e) => Theme.Changed -= Theme_Changed;

            Theme.Apply(this);
        }

        private void Theme_Changed(object sender, EventArgs e)
        {
            foreach (ThemeCard card in _cards)
                card.Invalidate();
        }
    }

    internal sealed class ThemeCard : Control
    {
        private const int Pad = 8;
        private const int CaptionHeight = 34;

        private bool _hot;

        public ThemeCard(Palette palette)
        {
            Palette = palette;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public Palette Palette { get; private set; }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hot = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hot = false;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Palette ui = Theme.Current;
            Graphics g = e.Graphics;
            bool selected = Palette == ui;

            g.Clear(BackColor);

            Rectangle card = new Rectangle(0, 0, Width - 1, Height - 1);
            using (SolidBrush back = new SolidBrush(_hot || selected ? ui.Section : BackColor))
                g.FillRectangle(back, card);

            Rectangle preview = new Rectangle(Pad, Pad, Width - 2 * Pad, Height - 2 * Pad - CaptionHeight + Pad);
            DrawPreview(g, preview, Palette);

            Rectangle caption = new Rectangle(Pad, preview.Bottom, Width - 2 * Pad, Height - preview.Bottom);
            using (Font font = new Font(Font.FontFamily, 10F, selected ? FontStyle.Bold : FontStyle.Regular))
                TextRenderer.DrawText(g, Palette.Name, font, caption, ui.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

            if (selected)
            {
                using (Pen pen = new Pen(ui.Accent, 2f))
                    g.DrawRectangle(pen, 1, 1, Width - 2, Height - 2);
            }
            else if (_hot)
            {
                using (Pen pen = new Pen(ui.Border))
                    g.DrawRectangle(pen, card);
            }
        }

        private void DrawPreview(Graphics g, Rectangle area, Palette q)
        {
            using (SolidBrush window = new SolidBrush(q.Window))
                g.FillRectangle(window, area);

            Rectangle toolbar = new Rectangle(area.X, area.Y, area.Width, 14);
            using (SolidBrush bar = new SolidBrush(q.Toolbar))
                g.FillRectangle(bar, toolbar);

            using (Font small = new Font(Font.FontFamily, 7.5F))
            {
                Rectangle section = new Rectangle(area.X + 8, area.Y + 26, area.Width - 16, area.Height - 34);
                using (SolidBrush back = new SolidBrush(q.Section))
                    g.FillRectangle(back, section.X, section.Y - 6, section.Width, section.Height + 6);
                using (Pen border = new Pen(q.Border))
                    g.DrawRectangle(border, section);

                Size title = TextRenderer.MeasureText(g, "Gas Section", small, Size.Empty, TextFormatFlags.NoPadding);
                Rectangle titleRect = new Rectangle(section.X + 6, section.Y - title.Height / 2, title.Width + 4, title.Height);
                using (SolidBrush back = new SolidBrush(q.Section))
                    g.FillRectangle(back, titleRect);
                TextRenderer.DrawText(g, "Gas Section", small, new Point(titleRect.X + 2, titleRect.Y), q.Title, TextFormatFlags.NoPadding);

                int rowY = section.Y + 14;
                int fieldWidth = (section.Width - 4 * 6) * 3 / 8;
                int buttonWidth = section.Width - 4 * 6 - 2 * fieldWidth;

                Rectangle measured = new Rectangle(section.X + 6, rowY, fieldWidth, 18);
                Rectangle setpoint = new Rectangle(measured.Right + 6, rowY, fieldWidth, 18);
                Rectangle button = new Rectangle(setpoint.Right + 6, rowY, buttonWidth, 18);

                DrawField(g, measured, q.ValueBack, q.ValueText, q.Border, "12.50", small);
                DrawField(g, setpoint, q.InputBack, q.InputText, q.Border, "12.00", small);
                DrawField(g, button, q.ButtonBack, q.Text, q.ButtonBorder, "Set", small);

                int pipeY = rowY + 38;
                using (Pen pipe = new Pen(q.Pipe, 3f))
                    g.DrawLine(pipe, section.X + 6, pipeY, section.Right - 6, pipeY);

                Image valve = Res.ValveOpen;
                if (valve != null)
                {
                    int valveWidth = 30;
                    int valveHeight = 16;
                    Rectangle valveRect = new Rectangle(section.X + (section.Width - valveWidth) / 2, pipeY - valveHeight / 2, valveWidth, valveHeight);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(valve, valveRect);
                }
            }
        }

        private static void DrawField(Graphics g, Rectangle rect, Color back, Color fore, Color border, string text, Font font)
        {
            using (SolidBrush brush = new SolidBrush(back))
                g.FillRectangle(brush, rect);
            using (Pen pen = new Pen(border))
                g.DrawRectangle(pen, rect);
            TextRenderer.DrawText(g, text, font, rect, fore,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }
}
