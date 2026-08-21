
namespace ArdisCVDCore
{
    partial class GraphScaler
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.XLastPoints = new System.Windows.Forms.Label();
            this.PointsView = new System.Windows.Forms.ComboBox();
            this.OK = new System.Windows.Forms.Button();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.label1 = new System.Windows.Forms.Label();
            this.label2 = new System.Windows.Forms.Label();
            this.YMinimum = new System.Windows.Forms.NumericUpDown();
            this.YMaximum = new System.Windows.Forms.NumericUpDown();
            this.groupBox1.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.YMinimum)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.YMaximum)).BeginInit();
            this.SuspendLayout();
            //
            // XLastPoints
            //
            this.XLastPoints.AutoSize = true;
            this.XLastPoints.Location = new System.Drawing.Point(179, 23);
            this.XLastPoints.Name = "XLastPoints";
            this.XLastPoints.Size = new System.Drawing.Size(69, 13);
            this.XLastPoints.TabIndex = 32;
            this.XLastPoints.Text = "X Last Points";
            //
            // PointsView
            //
            this.PointsView.FormattingEnabled = true;
            this.PointsView.Items.AddRange(new object[] {
            "100",
            "200",
            "300",
            "400",
            "500",
            "600",
            "700",
            "800",
            "900",
            "1000",
            "All (View Only)"});
            this.PointsView.Location = new System.Drawing.Point(169, 41);
            this.PointsView.Name = "PointsView";
            this.PointsView.Size = new System.Drawing.Size(94, 21);
            this.PointsView.TabIndex = 31;
            //
            // OK
            //
            this.OK.DialogResult = System.Windows.Forms.DialogResult.OK;
            this.OK.Location = new System.Drawing.Point(181, 93);
            this.OK.Name = "OK";
            this.OK.Size = new System.Drawing.Size(62, 22);
            this.OK.TabIndex = 33;
            this.OK.Text = "OK";
            this.OK.UseVisualStyleBackColor = true;
            this.OK.Click += new System.EventHandler(this.OK_Click_1);
            //
            // groupBox1
            //
            this.groupBox1.Controls.Add(this.label1);
            this.groupBox1.Controls.Add(this.label2);
            this.groupBox1.Controls.Add(this.YMinimum);
            this.groupBox1.Controls.Add(this.YMaximum);
            this.groupBox1.Location = new System.Drawing.Point(2, 3);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(151, 148);
            this.groupBox1.TabIndex = 34;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Y Axis";
            //
            // label1
            //
            this.label1.AutoSize = true;
            this.label1.Location = new System.Drawing.Point(16, 20);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(51, 13);
            this.label1.TabIndex = 18;
            this.label1.Text = "Maximum";
            //
            // label2
            //
            this.label2.AutoSize = true;
            this.label2.Location = new System.Drawing.Point(16, 72);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(48, 13);
            this.label2.TabIndex = 19;
            this.label2.Text = "Minimum";
            //
            // YMinimum
            //
            this.YMinimum.DecimalPlaces = 1;
            this.YMinimum.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.YMinimum.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.YMinimum.Location = new System.Drawing.Point(8, 90);
            this.YMinimum.Maximum = new decimal(new int[] {
            1500,
            0,
            0,
            0});
            this.YMinimum.Minimum = new decimal(new int[] {
            1500,
            0,
            0,
            -2147483648});
            this.YMinimum.Name = "YMinimum";
            this.YMinimum.Size = new System.Drawing.Size(137, 26);
            this.YMinimum.TabIndex = 15;
            //
            // YMaximum
            //
            this.YMaximum.DecimalPlaces = 1;
            this.YMaximum.Font = new System.Drawing.Font("Microsoft Sans Serif", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            this.YMaximum.Increment = new decimal(new int[] {
            10,
            0,
            0,
            0});
            this.YMaximum.Location = new System.Drawing.Point(10, 36);
            this.YMaximum.Maximum = new decimal(new int[] {
            1000,
            0,
            0,
            0});
            this.YMaximum.Name = "YMaximum";
            this.YMaximum.Size = new System.Drawing.Size(135, 26);
            this.YMaximum.TabIndex = 14;
            this.YMaximum.Value = new decimal(new int[] {
            200,
            0,
            0,
            0});
            //
            // GraphScaler
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(275, 157);
            this.Controls.Add(this.XLastPoints);
            this.Controls.Add(this.PointsView);
            this.Controls.Add(this.OK);
            this.Controls.Add(this.groupBox1);
            this.Name = "GraphScaler";
            this.Text = "GraphScaler";
            this.groupBox1.ResumeLayout(false);
            this.groupBox1.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.YMinimum)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.YMaximum)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.Label XLastPoints;
        private System.Windows.Forms.ComboBox PointsView;
        private System.Windows.Forms.Button OK;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.NumericUpDown YMinimum;
        private System.Windows.Forms.NumericUpDown YMaximum;
    }
}
