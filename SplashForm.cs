using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class SplashForm : Form
    {
        private const float ScreenShare = 1f / 3f;

        private readonly Image _picture;

        public SplashForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            Text = "Ardis CVDCore";
            Icon = Res.AppIcon;
            BackColor = Color.Black;
            DoubleBuffered = true;

            _picture = ScalePicture(Screen.FromPoint(Cursor.Position).WorkingArea);
            ClientSize = _picture != null ? _picture.Size : new Size(640, 337);
        }

        private static Image ScalePicture(Rectangle screen)
        {
            using (Image source = Res.LoadSplash())
            {
                if (source == null)
                    return null;

                int width = Math.Min(source.Width, (int)(screen.Width * ScreenShare));
                int height = (int)Math.Round((double)width * source.Height / source.Width);

                Bitmap scaled = new Bitmap(width, height);
                using (Graphics g = Graphics.FromImage(scaled))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.DrawImage(source, new Rectangle(0, 0, width, height));
                }
                return scaled;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (_picture != null)
                e.Graphics.DrawImageUnscaled(_picture, 0, 0);
            else
                base.OnPaint(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _picture != null)
                _picture.Dispose();
            base.Dispose(disposing);
        }
    }
}
