using System;
using System.Drawing;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class PasswordForm : Form
    {
        private const string Password = "ardis2027";

        private readonly TextBox _input;
        private readonly Label _error;

        public PasswordForm()
        {
            Text = "System Settings";
            Icon = Res.AppIcon;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Segoe UI", 9F);
            ClientSize = new Size(300, 128);

            Label caption = new Label
            {
                Text = "Enter password:",
                AutoSize = true,
                Location = new Point(14, 14)
            };

            _input = new TextBox
            {
                UseSystemPasswordChar = true,
                Location = new Point(16, 38),
                Size = new Size(268, 23)
            };
            _input.TextChanged += (s, e) => _error.Visible = false;

            _error = new Label
            {
                Text = "Wrong password",
                AutoSize = true,
                ForeColor = Color.FromArgb(220, 40, 60),
                Location = new Point(14, 66),
                Visible = false
            };

            Button ok = new Button
            {
                Text = "OK",
                Size = new Size(80, 28),
                Location = new Point(114, 90)
            };
            ok.Click += Ok_Click;

            Button cancel = new Button
            {
                Text = "Cancel",
                Size = new Size(80, 28),
                Location = new Point(204, 90),
                DialogResult = DialogResult.Cancel
            };

            AcceptButton = ok;
            CancelButton = cancel;

            Controls.Add(caption);
            Controls.Add(_input);
            Controls.Add(_error);
            Controls.Add(ok);
            Controls.Add(cancel);

            Theme.Apply(this);
        }

        public static bool Ask(IWin32Window owner)
        {
            using (PasswordForm form = new PasswordForm())
                return form.ShowDialog(owner) == DialogResult.OK;
        }

        private void Ok_Click(object sender, EventArgs e)
        {
            if (string.Equals(_input.Text, Password, StringComparison.Ordinal))
            {
                DialogResult = DialogResult.OK;
                return;
            }

            _error.Visible = true;
            _input.SelectAll();
            _input.Focus();
        }
    }
}
