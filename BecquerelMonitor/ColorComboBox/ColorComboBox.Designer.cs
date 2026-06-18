using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ColorComboBox
{
	partial class ColorComboBox
	{
		IContainer components = null;

		ColorComboButton button;

		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		void InitializeComponent()
		{
			this.button = new ColorComboButton();
			this.SuspendLayout();
			this.button.Appearance = Appearance.Button;
			this.button.Extended = false;
			this.button.Location = new Point(0, 0);
			this.button.Name = "button";
			this.button.SelectedColor = Color.Black;
			this.button.Size = new Size(103, 22);
			this.button.TabIndex = 0;
			this.AutoScaleDimensions = new SizeF(6f, 12f);
			this.AutoScaleMode = AutoScaleMode.Font;
			this.Controls.Add(this.button);
			this.Name = "ColorComboBox";
			this.Size = new Size(103, 22);
			this.SizeChanged += this.ColorComboBox_SizeChanged;
			this.ResumeLayout(false);
		}
	}
}
