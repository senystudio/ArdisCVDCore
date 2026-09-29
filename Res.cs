using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Reflection;

namespace ArdisCVDCore
{
    internal static class Res
    {
        private const string Prefix = "ArdisCVDCore.res.";

        private static readonly Image _valveClosed = LoadImage("Valve_red.png");
        private static readonly Image _valveOpen = LoadImage("Valve_green.png");
        private static readonly Color _onGreen = Color.FromArgb(0, 145, 14);
        private static readonly Image _logo = LoadImage("logo.png");
        private static readonly Image _qr = LoadImage("QR.png");
        private static readonly Icon _appIcon = LoadIcon("ardis.ico");
        private static readonly string _glyphFont = FindGlyphFont("Segoe Fluent Icons", "Segoe MDL2 Assets");

        public static Image ValveClosed { get { return _valveClosed; } }

        public static Image ValveOpen { get { return _valveOpen; } }

        public static Color OnGreen { get { return _onGreen; } }

        public static Image Logo { get { return _logo; } }

        public static Image Qr { get { return _qr; } }

        public static Icon AppIcon { get { return _appIcon; } }

        public static Image Glyph(char glyph, Color color, int size)
        {
            if (_glyphFont == null)
                return null;

            Bitmap bitmap = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (Font font = new Font(_glyphFont, size * 0.8f, GraphicsUnit.Pixel))
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.DrawString(glyph.ToString(), font, brush, new RectangleF(0, 0, size, size), format);
            }
            return bitmap;
        }

        private static string FindGlyphFont(params string[] names)
        {
            using (InstalledFontCollection installed = new InstalledFontCollection())
            {
                foreach (string name in names)
                    foreach (FontFamily family in installed.Families)
                        if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                            return family.Name;
            }
            return null;
        }

        private static Image LoadImage(string fileName)
        {
            Stream stream = Open(fileName);
            return stream == null ? null : Image.FromStream(stream);
        }

        private static Icon LoadIcon(string fileName)
        {
            using (Stream stream = Open(fileName))
                return stream == null ? null : new Icon(stream);
        }

        private static Stream Open(string fileName)
        {
            return Assembly.GetExecutingAssembly().GetManifestResourceStream(Prefix + fileName);
        }
    }
}
