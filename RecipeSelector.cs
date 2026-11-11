using System;
using System.IO;
using System.Windows.Forms;

namespace ArdisCVDCore
{
    public partial class RecipeSelector : Form
    {
        public string fname = "";

        public RecipeSelector()
        {
            InitializeComponent();
        }

        private void RecipeSelector_Load(object sender, EventArgs e)
        {
            updComboBox();
        }

        void updComboBox()
        {
            Directory.CreateDirectory(RecipeEditor.RecipeFolder);
            foreach (string filename in Directory.GetFiles(RecipeEditor.RecipeFolder, "*.xml"))
                comboBox1.Items.Add(Path.GetFileName(filename));
        }

        private void button1_Click(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void button5_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void comboBox1_SelectedValueChanged(object sender, EventArgs e)
        {
            if (comboBox1.SelectedItem == null)
                return;

            fname = comboBox1.SelectedItem.ToString();
            try
            {
                textBox1.Text = DataSerializer.DeserializatedRecipeComments(
                    Path.Combine(RecipeEditor.RecipeFolder, fname));
            }
            catch (Exception)
            {
                textBox1.Text = "";
            }
        }
    }
}
