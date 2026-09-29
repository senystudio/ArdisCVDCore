using System;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class CustomNumericUpDown : NumericUpDown
    {
        public decimal WheelIncrement { get; set; } = 5;

        public event EventHandler CustomMouseWheel;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            decimal step = WheelIncrement * Math.Sign(e.Delta);
            decimal newValue = this.Value + step;
            if (newValue < this.Minimum)
                newValue = this.Minimum;
            else if (newValue > this.Maximum)
                newValue = this.Maximum;
            this.Value = newValue;

            EventHandler handler = CustomMouseWheel;
            if (handler != null)
                handler(this, EventArgs.Empty);
        }
    }
}
