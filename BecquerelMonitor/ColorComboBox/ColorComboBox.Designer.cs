using BecquerelMonitor.Properties;
using System;
using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace ColorComboBox
{
	// Token: 0x020000A7 RID: 167
	public class ColorComboBox : UserControl
	{
		// Token: 0x06000840 RID: 2112 RVA: 0x00030094 File Offset: 0x0002E294
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000841 RID: 2113 RVA: 0x000300BC File Offset: 0x0002E2BC
		void InitializeComponent()
		{
			this.button = new ColorComboBox.ColorComboButton();
			base.SuspendLayout();
			this.button.Appearance = Appearance.Button;
			this.button.Extended = false;
			this.button.Location = new Point(0, 0);
			this.button.Name = "button";
			this.button.SelectedColor = Color.Black;
			this.button.Size = new Size(103, 22);
			this.button.TabIndex = 0;
			base.AutoScaleDimensions = new SizeF(6f, 12f);
			base.AutoScaleMode = AutoScaleMode.Font;
			base.Controls.Add(this.button);
			base.Name = "ColorComboBox";
			base.Size = new Size(103, 22);
			base.SizeChanged += this.ColorComboBox_SizeChanged;
			base.ResumeLayout(false);
		}

		// Token: 0x17000250 RID: 592
		// (get) Token: 0x06000842 RID: 2114 RVA: 0x000301A8 File Offset: 0x0002E3A8
		// (set) Token: 0x06000843 RID: 2115 RVA: 0x000301B8 File Offset: 0x0002E3B8
		public Color SelectedColor
		{
			get
			{
				return this.button.SelectedColor;
			}
			set
			{
				this.button.SelectedColor = value;
			}
		}

		// Token: 0x17000251 RID: 593
		// (get) Token: 0x06000845 RID: 2117 RVA: 0x000301D8 File Offset: 0x0002E3D8
		// (set) Token: 0x06000844 RID: 2116 RVA: 0x000301C8 File Offset: 0x0002E3C8
		public bool Extended
		{
			get
			{
				return this.button.Extended;
			}
			set
			{
				this.button.Extended = value;
			}
		}

		// Token: 0x1400001F RID: 31
		// (add) Token: 0x06000846 RID: 2118 RVA: 0x000301E8 File Offset: 0x0002E3E8
		// (remove) Token: 0x06000847 RID: 2119 RVA: 0x00030224 File Offset: 0x0002E424
		public event ColorChangedHandler ColorChanged;

		// Token: 0x06000848 RID: 2120 RVA: 0x00030260 File Offset: 0x0002E460
		public ColorComboBox()
		{
			this.InitializeComponent();
			this.button.ColorChanged += this.button_ColorChanged;
		}

		// Token: 0x06000849 RID: 2121 RVA: 0x00030288 File Offset: 0x0002E488
		public void button_ColorChanged(object sender, ColorChangeArgs e)
		{
			if (this.ColorChanged != null)
			{
				this.ColorChanged(this, e);
			}
		}

		// Token: 0x0600084A RID: 2122 RVA: 0x000302A4 File Offset: 0x0002E4A4
		void ColorComboBox_SizeChanged(object sender, EventArgs e)
		{
			this.button.Location = new Point(0, 0);
			this.button.Size = base.Size;
		}

		// Token: 0x0400044B RID: 1099
		IContainer components;

		// Token: 0x0400044C RID: 1100
		ColorComboBox.ColorComboButton button;

		// Token: 0x02000224 RID: 548
		class ColorComboButton : CheckBox
		{
			// Token: 0x14000078 RID: 120
			// (add) Token: 0x06001936 RID: 6454 RVA: 0x00076D30 File Offset: 0x00074F30
			// (remove) Token: 0x06001937 RID: 6455 RVA: 0x00076D6C File Offset: 0x00074F6C
			public event ColorChangedHandler ColorChanged;

			// Token: 0x1700075F RID: 1887
			// (get) Token: 0x06001938 RID: 6456 RVA: 0x00076DA8 File Offset: 0x00074FA8
			// (set) Token: 0x06001939 RID: 6457 RVA: 0x00076DB0 File Offset: 0x00074FB0
			public Color SelectedColor
			{
				get
				{
					return this.selectedColor;
				}
				set
				{
					this.selectedColor = value;
					this.colors.SelectedColor = value;
				}
			}

			// Token: 0x17000760 RID: 1888
			// (get) Token: 0x0600193B RID: 6459 RVA: 0x00076DD8 File Offset: 0x00074FD8
			// (set) Token: 0x0600193A RID: 6458 RVA: 0x00076DC8 File Offset: 0x00074FC8
			public bool Extended
			{
				get
				{
					return this.colors.ExtendedColors;
				}
				set
				{
					this.colors.ExtendedColors = value;
				}
			}

			// Token: 0x0600193C RID: 6460 RVA: 0x00076DE8 File Offset: 0x00074FE8
			public ColorComboButton() : this(false, Color.Black)
			{
			}

			// Token: 0x0600193D RID: 6461 RVA: 0x00076DF8 File Offset: 0x00074FF8
			public ColorComboButton(bool extended, Color selectedColor)
			{
				base.SuspendLayout();
				base.Appearance = Appearance.Button;
				this.AutoSize = false;
				base.Size = new Size(103, 23);
				this.Text = "";
				base.Paint += this.ColorCombo_Paint;
				base.Click += this.ColorCombo_Click;
				this.timer.Tick += this.OnCheckStatus;
				this.timer.Interval = 30;
				this.timer.Start();
				this.colors.ExtendedColors = extended;
				ColorComboBox.ColorComboButton.ColorPopup colorPopup = this.colors;
				this.selectedColor = selectedColor;
				colorPopup.SelectedColor = selectedColor;
				base.ResumeLayout(false);
			}

			// Token: 0x0600193E RID: 6462 RVA: 0x00076EDC File Offset: 0x000750DC
			void ColorCombo_Click(object sender, EventArgs e)
			{
				if (!base.Checked)
				{
					return;
				}
				this.popupWnd = new ColorComboBox.ColorComboButton.PopupWindow(this.colors);
				Rectangle r = base.Bounds;
				r = base.Parent.RectangleToScreen(r);
				Point screenLocation = new Point(r.Left, r.Bottom);
				this.popupWnd.ColorChanged += this.OnColorChanged;
				this.popupWnd.Show(screenLocation);
				base.Enabled = false;
			}

			// Token: 0x0600193F RID: 6463 RVA: 0x00076F60 File Offset: 0x00075160
			protected void OnColorChanged(object sender, ColorChangeArgs e)
			{
				if (this.ColorChanged != null && e.color != this.selectedColor)
				{
					this.selectedColor = e.color;
					this.ColorChanged(this, e);
					return;
				}
				this.selectedColor = e.color;
			}

			// Token: 0x06001940 RID: 6464 RVA: 0x00076FB8 File Offset: 0x000751B8
			void ColorCombo_Paint(object sender, PaintEventArgs e)
			{
				Rectangle rect = new Rectangle(base.ClientRectangle.Right - 18, base.ClientRectangle.Top, 18, base.ClientRectangle.Height);
				this.DrawArrow(e.Graphics, rect);
				Rectangle rect2 = new Rectangle(base.ClientRectangle.Left + 5, base.ClientRectangle.Top + 4, base.ClientRectangle.Width - 22, base.ClientRectangle.Height - 9);
				this.DrawColor(e.Graphics, rect2, this.selectedColor);
			}

			// Token: 0x06001941 RID: 6465 RVA: 0x00077070 File Offset: 0x00075270
			void DrawArrow(Graphics dc, Rectangle rect)
			{
				Point[] array = new Point[3];
				int num = rect.Left + (rect.Right - rect.Left) / 2;
				int num2 = rect.Top + (rect.Bottom - rect.Top) / 2;
				array[0].X = num - 4;
				array[0].Y = num2 - 2;
				array[1].X = num + 4;
				array[1].Y = num2 - 2;
				array[2].X = num;
				array[2].Y = num2 + 2;
				SolidBrush brush = new SolidBrush(Color.FromArgb(base.Enabled ? 255 : 100, Color.Black));
				dc.FillPolygon(brush, array);
			}

			// Token: 0x06001942 RID: 6466 RVA: 0x00077144 File Offset: 0x00075344
			void DrawColor(Graphics dc, Rectangle rect, Color color)
			{
				SolidBrush solidBrush = new SolidBrush(color);
				dc.FillRectangle(solidBrush, rect);
				dc.DrawRectangle(Pens.Black, rect);
				solidBrush.Dispose();
			}

			// Token: 0x06001943 RID: 6467 RVA: 0x00077178 File Offset: 0x00075378
			void OnCheckStatus(object myObject, EventArgs myEventArgs)
			{
				if (this.popupWnd != null && !this.popupWnd.Visible)
				{
					base.Checked = false;
					base.Enabled = true;
				}
			}

			// Token: 0x04000D8B RID: 3467
			static ColorDialog colorDialog;

			// Token: 0x04000D8C RID: 3468
			ColorComboBox.ColorComboButton.PopupWindow popupWnd;

			// Token: 0x04000D8D RID: 3469
			ColorComboBox.ColorComboButton.ColorPopup colors = new ColorComboBox.ColorComboButton.ColorPopup();

			// Token: 0x04000D8E RID: 3470
			Color selectedColor = Color.Black;

			// Token: 0x04000D8F RID: 3471
			Timer timer = new Timer();

			// Token: 0x02000262 RID: 610
			class ColorRadioButton : RadioButton
			{
				// Token: 0x06001A8A RID: 6794 RVA: 0x0007DEC4 File Offset: 0x0007C0C4
				public ColorRadioButton(Color color, Color backColor)
				{
					base.ClientSize = new Size(20, 20);
					base.Appearance = Appearance.Button;
					base.Name = "button1";
					base.Visible = true;
					this.ForeColor = color;
					base.FlatAppearance.BorderColor = backColor;
					base.FlatAppearance.BorderSize = 0;
					base.FlatStyle = FlatStyle.Flat;
					base.Paint += this.OnPaintButton;
				}

				// Token: 0x06001A8B RID: 6795 RVA: 0x0007DF3C File Offset: 0x0007C13C
				void OnPaintButton(object sender, PaintEventArgs e)
				{
					Rectangle rect = new Rectangle(base.ClientRectangle.Left + 4, base.ClientRectangle.Top + 4, base.ClientRectangle.Width - 9, base.ClientRectangle.Height - 9);
					e.Graphics.FillRectangle(new SolidBrush(this.ForeColor), rect);
					e.Graphics.DrawRectangle(new Pen(Color.Black), rect);
				}
			}

			// Token: 0x02000263 RID: 611
			class PopupWindow : ToolStripDropDown
			{
				// Token: 0x1400007F RID: 127
				// (add) Token: 0x06001A8C RID: 6796 RVA: 0x0007DFC4 File Offset: 0x0007C1C4
				// (remove) Token: 0x06001A8D RID: 6797 RVA: 0x0007E000 File Offset: 0x0007C200
				public event ColorChangedHandler ColorChanged;

				// Token: 0x170007B7 RID: 1975
				// (get) Token: 0x06001A8E RID: 6798 RVA: 0x0007E03C File Offset: 0x0007C23C
				public Color SelectedColor
				{
					get
					{
						return this.content.SelectedColor;
					}
				}

				// Token: 0x06001A8F RID: 6799 RVA: 0x0007E04C File Offset: 0x0007C24C
				public PopupWindow(ColorComboBox.ColorComboButton.ColorPopup content)
				{
					if (content == null)
					{
						throw new ArgumentNullException("content");
					}
					this.content = content;
					this.AutoSize = false;
					this.DoubleBuffered = true;
					base.ResizeRedraw = true;
					this.host = new ToolStripControlHost(content);
					base.Padding = (base.Margin = (this.host.Padding = (this.host.Margin = Padding.Empty)));
					this.MinimumSize = content.MinimumSize;
					content.MinimumSize = content.Size;
					this.MaximumSize = new Size(content.Size.Width + 1, content.Size.Height + 1);
					content.MaximumSize = new Size(content.Size.Width + 1, content.Size.Height + 1);
					base.Size = new Size(content.Size.Width + 1, content.Size.Height + 1);
					content.Location = Point.Empty;
					this.Items.Add(this.host);
				}

				// Token: 0x06001A90 RID: 6800 RVA: 0x0007E188 File Offset: 0x0007C388
				protected override void OnClosed(ToolStripDropDownClosedEventArgs e)
				{
					if (this.ColorChanged != null)
					{
						this.ColorChanged(this, new ColorChangeArgs(this.SelectedColor));
					}
				}

				// Token: 0x04000EF6 RID: 3830
				ToolStripControlHost host;

				// Token: 0x04000EF7 RID: 3831
				ColorComboBox.ColorComboButton.ColorPopup content;
			}

			// Token: 0x02000264 RID: 612
			class ColorPopup : UserControl
			{
				// Token: 0x170007B8 RID: 1976
				// (get) Token: 0x06001A92 RID: 6802 RVA: 0x0007E1BC File Offset: 0x0007C3BC
				// (set) Token: 0x06001A91 RID: 6801 RVA: 0x0007E1AC File Offset: 0x0007C3AC
				public bool ExtendedColors
				{
					get
					{
						return this.extended;
					}
					set
					{
						this.extended = value;
						this.SetupButtons();
					}
				}

				// Token: 0x170007B9 RID: 1977
				// (get) Token: 0x06001A93 RID: 6803 RVA: 0x0007E1C4 File Offset: 0x0007C3C4
				// (set) Token: 0x06001A94 RID: 6804 RVA: 0x0007E1CC File Offset: 0x0007C3CC
				public Color SelectedColor
				{
					get
					{
						return this.selectedColor;
					}
					set
					{
						this.selectedColor = value;
						Color[] array = this.extended ? this.extendedColors : this.colors;
						for (int i = 0; i < array.Length; i++)
						{
							this.buttons[i].Checked = (this.selectedColor == array[i]);
						}
					}
				}

				// Token: 0x06001A95 RID: 6805 RVA: 0x0007E23C File Offset: 0x0007C43C
				void InitializeComponent()
				{
					base.SuspendLayout();
					base.Name = "Color Popup";
					this.Text = "";
					base.ResumeLayout(false);
				}

				// Token: 0x06001A96 RID: 6806 RVA: 0x0007E270 File Offset: 0x0007C470
				public ColorPopup()
				{
					this.InitializeComponent();
					this.SetupButtons();
					base.Paint += this.OnPaintBorder;
				}

				// Token: 0x06001A97 RID: 6807 RVA: 0x0007E738 File Offset: 0x0007C938
				void SetupButtons()
				{
					base.Controls.Clear();
					int num = 3;
					int num2 = 3;
					int num3 = this.extended ? 8 : 4;
					Color[] array = this.extended ? this.extendedColors : this.colors;
					this.buttons = new ColorComboBox.ColorComboButton.ColorRadioButton[array.Length];
					if (this.extended)
					{
						base.ClientSize = new Size(166, 130);
					}
					else
					{
						base.ClientSize = new Size(86, 110);
					}
					for (int i = 0; i < array.Length; i++)
					{
						if (i > 0 && i % num3 == 0)
						{
							num2 += 20;
							num = 3;
						}
						this.buttons[i] = new ColorComboBox.ColorComboButton.ColorRadioButton(array[i], this.BackColor);
						this.buttons[i].Location = new Point(num, num2);
						base.Controls.Add(this.buttons[i]);
						this.buttons[i].Click += this.BtnClicked;
						if (this.selectedColor == array[i])
						{
							this.buttons[i].Checked = true;
						}
						num += 20;
					}
					this.moreColorsBtn = new Button();
					this.moreColorsBtn.FlatStyle = FlatStyle.Flat;
					this.moreColorsBtn.Text = Resources.ColorBTN;
					this.moreColorsBtn.Location = new Point(3, num2 + 20);
					this.moreColorsBtn.ClientSize = new Size(this.extended ? 160 : 80, 24);
					this.moreColorsBtn.Click += this.OnMoreClicked;
					base.Controls.Add(this.moreColorsBtn);
				}

				// Token: 0x06001A98 RID: 6808 RVA: 0x0007E930 File Offset: 0x0007CB30
				void OnPaintBorder(object sender, PaintEventArgs e)
				{
					e.Graphics.DrawRectangle(new Pen(Color.Black), base.ClientRectangle);
				}

				// Token: 0x06001A99 RID: 6809 RVA: 0x0007E950 File Offset: 0x0007CB50
				public void BtnClicked(object sender, EventArgs e)
				{
					this.selectedColor = ((ColorComboBox.ColorComboButton.ColorRadioButton)sender).ForeColor;
					((ToolStripDropDown)base.Parent).Close();
				}

				// Token: 0x06001A9A RID: 6810 RVA: 0x0007E974 File Offset: 0x0007CB74
				public void OnMoreClicked(object sender, EventArgs e)
				{
					if (ColorComboBox.ColorComboButton.colorDialog == null)
					{
						ColorComboBox.ColorComboButton.colorDialog = new ColorDialog();
					}
					ColorDialog colorDialog = ColorComboBox.ColorComboButton.colorDialog;
					colorDialog.Color = this.SelectedColor;
					if (colorDialog.ShowDialog(this) == DialogResult.OK)
					{
						this.selectedColor = colorDialog.Color;
					}
					((ToolStripDropDown)base.Parent).Close();
				}

				// Token: 0x04000EF8 RID: 3832
				Color[] colors = new Color[]
				{
					Color.Black,
					Color.Gray,
					Color.Maroon,
					Color.Olive,
					Color.Green,
					Color.Teal,
					Color.Navy,
					Color.Purple,
					Color.White,
					Color.Silver,
					Color.Red,
					Color.Yellow,
					Color.Lime,
					Color.Aqua,
					Color.Blue,
					Color.Fuchsia
				};

				// Token: 0x04000EF9 RID: 3833
				Color[] extendedColors = new Color[]
				{
					Color.Black,
					Color.Brown,
					Color.Olive,
					Color.DarkGreen,
					Color.FromArgb(0, 51, 102),
					Color.DarkBlue,
					Color.Indigo,
					Color.FromArgb(51, 51, 51),
					Color.DarkRed,
					Color.Orange,
					Color.FromArgb(128, 128, 0),
					Color.Green,
					Color.Teal,
					Color.Blue,
					Color.FromArgb(102, 102, 153),
					Color.FromArgb(128, 128, 128),
					Color.Red,
					Color.FromArgb(255, 153, 0),
					Color.Lime,
					Color.SeaGreen,
					Color.Aqua,
					Color.LightBlue,
					Color.Violet,
					Color.FromArgb(153, 153, 153),
					Color.Pink,
					Color.Gold,
					Color.Yellow,
					Color.FromArgb(0, 255, 0),
					Color.Turquoise,
					Color.SkyBlue,
					Color.Plum,
					Color.FromArgb(192, 192, 192),
					Color.FromArgb(255, 153, 204),
					Color.Tan,
					Color.LightYellow,
					Color.LightGreen,
					Color.FromArgb(204, 255, 255),
					Color.FromArgb(153, 204, 255),
					Color.Lavender,
					Color.White
				};

				// Token: 0x04000EFA RID: 3834
				ColorComboBox.ColorComboButton.ColorRadioButton[] buttons;

				// Token: 0x04000EFB RID: 3835
				Button moreColorsBtn;

				// Token: 0x04000EFC RID: 3836
				Color selectedColor = Color.Black;

				// Token: 0x04000EFD RID: 3837
				bool extended;
			}
		}
	}
}
