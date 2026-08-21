using System;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public class CustomNumericUpDown : NumericUpDown
    {
        public decimal WheelIncrement { get; set; } = 5;

        /// <summary>
        /// Raised after the wheel has changed <see cref="NumericUpDown.Value"/>,
        /// so a caller can react to a wheel scroll specifically rather than to
        /// every ValueChanged (which also fires while the operator is typing).
        /// </summary>
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
