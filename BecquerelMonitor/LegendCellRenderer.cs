using System;
using System.Drawing;
using System.Windows.Forms;
using System.Windows.Forms.VisualStyles;
using XPTable.Events;
using XPTable.Models;
using XPTable.Renderers;
using XPTable.Themes;

namespace BecquerelMonitor
{
	// Token: 0x020000A3 RID: 163
	public class LegendCellRenderer : CellRenderer
	{
		// Token: 0x06000805 RID: 2053 RVA: 0x0002CE04 File Offset: 0x0002B004
		public LegendCellRenderer()
		{
			this.checkSize = new Size(13, 13);
			this.drawText = true;
		}

		// Token: 0x06000806 RID: 2054 RVA: 0x0002CE24 File Offset: 0x0002B024
		protected Rectangle CalcCheckRect(RowAlignment rowAlignment, ColumnAlignment columnAlignment)
		{
			Rectangle result = new Rectangle(this.ClientRectangle.Location, this.CheckSize);
			if (result.Height > this.ClientRectangle.Height)
			{
				result.Height = this.ClientRectangle.Height;
				result.Width = result.Height;
			}
			switch (rowAlignment)
			{
			case RowAlignment.Center:
				result.Y += (this.ClientRectangle.Height - result.Height) / 2;
				break;
			case RowAlignment.Bottom:
				result.Y = this.ClientRectangle.Bottom - result.Height;
				break;
			}
			if (!this.DrawText)
			{
				if (columnAlignment == ColumnAlignment.Center)
				{
					result.X += (this.ClientRectangle.Width - result.Width) / 2;
				}
				else if (columnAlignment == ColumnAlignment.Right)
				{
					result.X = this.ClientRectangle.Right - result.Width;
				}
			}
			return result;
		}

		// Token: 0x06000807 RID: 2055 RVA: 0x0002CF54 File Offset: 0x0002B154
		protected CheckBoxRendererData GetCheckBoxRendererData(Cell cell)
		{
			object obj = base.GetRendererData(cell);
			if (obj == null || !(obj is CheckBoxRendererData))
			{
				if (cell.CheckState == CheckState.Unchecked)
				{
					obj = new CheckBoxRendererData(CheckBoxState.UncheckedNormal);
				}
				else if (cell.CheckState == CheckState.Indeterminate && cell.ThreeState)
				{
					obj = new CheckBoxRendererData(CheckBoxState.MixedNormal);
				}
				else
				{
					obj = new CheckBoxRendererData(CheckBoxState.CheckedNormal);
				}
				base.SetRendererData(cell, obj);
			}
			this.ValidateCheckState(cell, (CheckBoxRendererData)obj);
			return (CheckBoxRendererData)obj;
		}

		// Token: 0x06000808 RID: 2056 RVA: 0x0002CFDC File Offset: 0x0002B1DC
		void ValidateCheckState(Cell cell, CheckBoxRendererData rendererData)
		{
			switch (cell.CheckState)
			{
			case CheckState.Checked:
				if (rendererData.CheckState <= CheckBoxState.UncheckedDisabled)
				{
					rendererData.CheckState |= CheckBoxState.UncheckedDisabled;
					return;
				}
				if (rendererData.CheckState >= CheckBoxState.MixedNormal)
				{
					rendererData.CheckState -= 4;
					return;
				}
				break;
			case CheckState.Indeterminate:
				if (rendererData.CheckState <= CheckBoxState.UncheckedDisabled)
				{
					rendererData.CheckState |= CheckBoxState.CheckedDisabled;
					return;
				}
				if (rendererData.CheckState <= CheckBoxState.CheckedDisabled)
				{
					rendererData.CheckState += 4;
					return;
				}
				break;
			default:
				if (rendererData.CheckState >= CheckBoxState.MixedNormal)
				{
					rendererData.CheckState -= 8;
					return;
				}
				if (rendererData.CheckState >= CheckBoxState.CheckedNormal)
				{
					rendererData.CheckState -= 4;
				}
				break;
			}
		}

		// Token: 0x1700024C RID: 588
		// (get) Token: 0x06000809 RID: 2057 RVA: 0x0002D0AC File Offset: 0x0002B2AC
		protected Size CheckSize
		{
			get
			{
				return this.checkSize;
			}
		}

		// Token: 0x1700024D RID: 589
		// (get) Token: 0x0600080A RID: 2058 RVA: 0x0002D0B4 File Offset: 0x0002B2B4
		public bool DrawText
		{
			get
			{
				return this.drawText;
			}
		}

		// Token: 0x0600080B RID: 2059 RVA: 0x0002D0BC File Offset: 0x0002B2BC
		public override void OnKeyDown(CellKeyEventArgs e)
		{
			base.OnKeyDown(e);
			if (e.KeyData == Keys.Space && e.Table.IsCellEditable(e.CellPos))
			{
				CheckBoxRendererData checkBoxRendererData = this.GetCheckBoxRendererData(e.Cell);
				if (e.Cell.CheckState == CheckState.Checked)
				{
					checkBoxRendererData.CheckState = CheckBoxState.CheckedPressed;
				}
				else if (e.Cell.CheckState == CheckState.Indeterminate)
				{
					checkBoxRendererData.CheckState = CheckBoxState.MixedPressed;
				}
				else
				{
					checkBoxRendererData.CheckState = CheckBoxState.UncheckedPressed;
				}
				e.Table.Invalidate(e.CellRect);
			}
		}

		// Token: 0x0600080C RID: 2060 RVA: 0x0002D158 File Offset: 0x0002B358
		public override void OnKeyUp(CellKeyEventArgs e)
		{
			base.OnKeyUp(e);
			if (e.KeyData == Keys.Space && e.Table.IsCellEditable(e.CellPos))
			{
				CheckBoxRendererData checkBoxRendererData = this.GetCheckBoxRendererData(e.Cell);
				if (e.Cell.CheckState == CheckState.Checked)
				{
					if (!e.Cell.ThreeState || !(e.Table.ColumnModel.Columns[e.Column] is CheckBoxColumn) || ((CheckBoxColumn)e.Table.ColumnModel.Columns[e.Column]).CheckStyle == CheckBoxColumnStyle.RadioButton)
					{
						checkBoxRendererData.CheckState = CheckBoxState.UncheckedNormal;
						e.Cell.CheckState = CheckState.Unchecked;
					}
					else
					{
						checkBoxRendererData.CheckState = CheckBoxState.MixedNormal;
						e.Cell.CheckState = CheckState.Indeterminate;
					}
				}
				else if (e.Cell.CheckState == CheckState.Indeterminate)
				{
					checkBoxRendererData.CheckState = CheckBoxState.UncheckedNormal;
					e.Cell.CheckState = CheckState.Unchecked;
				}
				else
				{
					checkBoxRendererData.CheckState = CheckBoxState.CheckedNormal;
					e.Cell.CheckState = CheckState.Checked;
				}
				e.Table.Invalidate(e.CellRect);
			}
		}

		// Token: 0x0600080D RID: 2061 RVA: 0x0002D290 File Offset: 0x0002B490
		public override void OnMouseLeave(CellMouseEventArgs e)
		{
			base.OnMouseLeave(e);
			if (e.Table.IsCellEditable(e.CellPos))
			{
				CheckBoxRendererData checkBoxRendererData = this.GetCheckBoxRendererData(e.Cell);
				if (e.Cell.CheckState == CheckState.Checked)
				{
					if (checkBoxRendererData.CheckState != CheckBoxState.CheckedNormal)
					{
						checkBoxRendererData.CheckState = CheckBoxState.CheckedNormal;
						e.Table.Invalidate(e.CellRect);
						return;
					}
				}
				else if (e.Cell.CheckState == CheckState.Indeterminate)
				{
					if (checkBoxRendererData.CheckState != CheckBoxState.MixedNormal)
					{
						checkBoxRendererData.CheckState = CheckBoxState.MixedNormal;
						e.Table.Invalidate(e.CellRect);
						return;
					}
				}
				else if (checkBoxRendererData.CheckState != CheckBoxState.UncheckedNormal)
				{
					checkBoxRendererData.CheckState = CheckBoxState.UncheckedNormal;
					e.Table.Invalidate(e.CellRect);
				}
			}
		}

		// Token: 0x0600080E RID: 2062 RVA: 0x0002D360 File Offset: 0x0002B560
		public override void OnMouseUp(CellMouseEventArgs e)
		{
			base.OnMouseUp(e);
			if (e.Table.IsCellEditable(e.CellPos))
			{
				CheckBoxRendererData checkBoxRendererData = this.GetCheckBoxRendererData(e.Cell);
				if (this.CalcCheckRect(e.Table.TableModel.Rows[e.Row].Alignment, e.Table.ColumnModel.Columns[e.Column].Alignment).Contains(e.X, e.Y) && e.Button == MouseButtons.Left && e.Table.LastMouseDownCell.Row == e.Row && e.Table.LastMouseDownCell.Column == e.Column)
				{
					if (e.Cell.CheckState == CheckState.Checked)
					{
						if (!e.Cell.ThreeState || !(e.Table.ColumnModel.Columns[e.Column] is CheckBoxColumn) || ((CheckBoxColumn)e.Table.ColumnModel.Columns[e.Column]).CheckStyle == CheckBoxColumnStyle.RadioButton)
						{
							checkBoxRendererData.CheckState = CheckBoxState.UncheckedHot;
							e.Cell.CheckState = CheckState.Unchecked;
						}
						else
						{
							checkBoxRendererData.CheckState = CheckBoxState.MixedHot;
							e.Cell.CheckState = CheckState.Indeterminate;
						}
					}
					else if (e.Cell.CheckState == CheckState.Indeterminate)
					{
						checkBoxRendererData.CheckState = CheckBoxState.UncheckedHot;
						e.Cell.CheckState = CheckState.Unchecked;
					}
					else
					{
						checkBoxRendererData.CheckState = CheckBoxState.CheckedHot;
						e.Cell.CheckState = CheckState.Checked;
					}
					e.Table.Invalidate(e.CellRect);
				}
			}
		}

		// Token: 0x0600080F RID: 2063 RVA: 0x0002D538 File Offset: 0x0002B738
		public override void OnMouseDown(CellMouseEventArgs e)
		{
			base.OnMouseDown(e);
			if (e.Table.IsCellEditable(e.CellPos))
			{
				CheckBoxRendererData checkBoxRendererData = this.GetCheckBoxRendererData(e.Cell);
				if (this.CalcCheckRect(e.Table.TableModel.Rows[e.Row].Alignment, e.Table.ColumnModel.Columns[e.Column].Alignment).Contains(e.X, e.Y))
				{
					if (e.Cell.CheckState == CheckState.Checked)
					{
						checkBoxRendererData.CheckState = CheckBoxState.CheckedPressed;
					}
					else if (e.Cell.CheckState == CheckState.Indeterminate)
					{
						checkBoxRendererData.CheckState = CheckBoxState.MixedPressed;
					}
					else
					{
						checkBoxRendererData.CheckState = CheckBoxState.UncheckedPressed;
					}
					e.Table.Invalidate(e.CellRect);
				}
			}
		}

		// Token: 0x06000810 RID: 2064 RVA: 0x0002D628 File Offset: 0x0002B828
		public override void OnMouseMove(CellMouseEventArgs e)
		{
			base.OnMouseMove(e);
			if (e.Table.IsCellEditable(e.CellPos))
			{
				CheckBoxRendererData checkBoxRendererData = this.GetCheckBoxRendererData(e.Cell);
				if (this.CalcCheckRect(e.Table.TableModel.Rows[e.Row].Alignment, e.Table.ColumnModel.Columns[e.Column].Alignment).Contains(e.X, e.Y))
				{
					if (e.Cell.CheckState == CheckState.Checked)
					{
						if (checkBoxRendererData.CheckState == CheckBoxState.CheckedNormal)
						{
							if (e.Button == MouseButtons.Left && e.Row == e.Table.LastMouseDownCell.Row && e.Column == e.Table.LastMouseDownCell.Column)
							{
								checkBoxRendererData.CheckState = CheckBoxState.CheckedPressed;
							}
							else
							{
								checkBoxRendererData.CheckState = CheckBoxState.CheckedHot;
							}
							e.Table.Invalidate(e.CellRect);
							return;
						}
					}
					else if (e.Cell.CheckState == CheckState.Indeterminate)
					{
						if (checkBoxRendererData.CheckState == CheckBoxState.MixedNormal)
						{
							if (e.Button == MouseButtons.Left && e.Row == e.Table.LastMouseDownCell.Row && e.Column == e.Table.LastMouseDownCell.Column)
							{
								checkBoxRendererData.CheckState = CheckBoxState.MixedPressed;
							}
							else
							{
								checkBoxRendererData.CheckState = CheckBoxState.MixedHot;
							}
							e.Table.Invalidate(e.CellRect);
							return;
						}
					}
					else if (checkBoxRendererData.CheckState == CheckBoxState.UncheckedNormal)
					{
						if (e.Button == MouseButtons.Left && e.Row == e.Table.LastMouseDownCell.Row && e.Column == e.Table.LastMouseDownCell.Column)
						{
							checkBoxRendererData.CheckState = CheckBoxState.UncheckedPressed;
						}
						else
						{
							checkBoxRendererData.CheckState = CheckBoxState.UncheckedHot;
						}
						e.Table.Invalidate(e.CellRect);
						return;
					}
				}
				else
				{
					if (e.Cell.CheckState == CheckState.Checked)
					{
						checkBoxRendererData.CheckState = CheckBoxState.CheckedNormal;
					}
					else if (e.Cell.CheckState == CheckState.Indeterminate)
					{
						checkBoxRendererData.CheckState = CheckBoxState.MixedNormal;
					}
					else
					{
						checkBoxRendererData.CheckState = CheckBoxState.UncheckedNormal;
					}
					e.Table.Invalidate(e.CellRect);
				}
			}
		}

		// Token: 0x06000811 RID: 2065 RVA: 0x0002D8B8 File Offset: 0x0002BAB8
		public override void OnPaintCell(PaintCellEventArgs e)
		{
			if (e.Table.ColumnModel.Columns[e.Column] is CheckBoxColumn)
			{
				CheckBoxColumn checkBoxColumn = (CheckBoxColumn)e.Table.ColumnModel.Columns[e.Column];
				this.checkSize = checkBoxColumn.CheckSize;
				this.drawText = checkBoxColumn.DrawText;
			}
			else
			{
				this.checkSize = new Size(13, 13);
				this.drawText = true;
			}
			base.OnPaintCell(e);
		}

		// Token: 0x06000812 RID: 2066 RVA: 0x0002D94C File Offset: 0x0002BB4C
		protected override void OnPaint(PaintCellEventArgs e)
		{
			base.OnPaint(e);
			if (e.Cell == null)
			{
				return;
			}
			Rectangle checkRect = this.CalcCheckRect(base.LineAlignment, base.Alignment);
			CheckBoxState state = this.GetCheckBoxRendererData(e.Cell).CheckState;
			if (!e.Enabled)
			{
				if (e.Cell.CheckState == CheckState.Checked)
				{
					state = CheckBoxState.CheckedDisabled;
				}
				else if (e.Cell.CheckState == CheckState.Indeterminate)
				{
					state = CheckBoxState.MixedDisabled;
				}
				else
				{
					state = CheckBoxState.UncheckedDisabled;
				}
			}
			if (e.Table.ColumnModel.Columns[e.Column] is CheckBoxColumn && ((CheckBoxColumn)e.Table.ColumnModel.Columns[e.Column]).CheckStyle != CheckBoxColumnStyle.CheckBox)
			{
				switch (state)
				{
				case CheckBoxState.MixedNormal:
					state = CheckBoxState.CheckedNormal;
					break;
				case CheckBoxState.MixedHot:
					state = CheckBoxState.CheckedHot;
					break;
				case CheckBoxState.MixedPressed:
					state = CheckBoxState.CheckedPressed;
					break;
				case CheckBoxState.MixedDisabled:
					state = CheckBoxState.CheckedDisabled;
					break;
				}
				ThemeManager.DrawRadioButton(e.Graphics, checkRect, (RadioButtonState)state);
			}
			else
			{
				ThemeManager.DrawCheck(e.Graphics, checkRect, state);
			}
			if (this.DrawText)
			{
				string text = e.Cell.Text;
				if (text != null && text.Length != 0)
				{
					Rectangle clientRectangle = this.ClientRectangle;
					clientRectangle.X += checkRect.Width + 1;
					clientRectangle.Width -= checkRect.Width + 1;
					if (e.Enabled)
					{
						e.Graphics.DrawString(e.Cell.Text, base.Font, base.ForeBrush, clientRectangle, base.StringFormat);
					}
					else
					{
						e.Graphics.DrawString(e.Cell.Text, base.Font, base.GrayTextBrush, clientRectangle, base.StringFormat);
					}
				}
				if (e.Cell.WidthNotSet)
				{
					SizeF sizeF = e.Graphics.MeasureString(e.Cell.Text, base.Font);
					e.Cell.ContentWidth = this.checkSize.Width + (int)Math.Ceiling((double)sizeF.Width);
				}
			}
			else
			{
				Color color = (Color)e.Cell.Data;
				Graphics graphics = e.Graphics;
				Rectangle clientRectangle2 = this.ClientRectangle;
				clientRectangle2.X += checkRect.Width + 2;
				clientRectangle2.Width -= checkRect.Width + 4;
				clientRectangle2.Y += 5;
				clientRectangle2.Height = 2;
				using (Brush brush = new SolidBrush(color))
				{
					graphics.FillRectangle(brush, clientRectangle2);
				}
			}
			if (e.Focused && e.Enabled && e.Table.ShowSelectionRectangle)
			{
				ControlPaint.DrawFocusRectangle(e.Graphics, this.ClientRectangle);
			}
		}

		// Token: 0x04000413 RID: 1043
		Size checkSize;

		// Token: 0x04000414 RID: 1044
		bool drawText;
	}
}
