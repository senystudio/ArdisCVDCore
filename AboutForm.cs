using System.Drawing;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class AboutForm : Form
    {
        private const string Version = "21";
        private const string ReleaseDate = "22.09.2026";

        private const string Authors = "Sizov Y. E., Chernyavskiy S. V., Pavlov A. Y.";

        public AboutForm()
        {
            Text = "About";
            ClientSize = new Size(392, 169);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.LightGray;
            Icon = Res.AppIcon;

            AddLine("Ardis CVDCore " + Version, 8);
            AddLine(Authors, 34);
            AddLine(ReleaseDate, 60);

            Controls.Add(new PictureBox
            {
                Image = Res.Qr,
                Location = new Point(287, 61),
                Size = new Size(91, 91),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false
            });

            Controls.Add(new PictureBox
            {
                Image = Res.Logo,
                Location = new Point(7, 124),
                Size = new Size(153, 35),
                SizeMode = PictureBoxSizeMode.Zoom,
                BackColor = Color.Transparent,
                TabStop = false
            });
        }

        private void AddLine(string text, int y)
        {
            Controls.Add(new Label
            {
                AutoSize = true,
                Location = new Point(10, y),
                Font = new Font("Microsoft Sans Serif", 10F, FontStyle.Regular, GraphicsUnit.Point, 204),
                BackColor = Color.Transparent,
                Text = text
            });
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                Close();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }
    }
}
