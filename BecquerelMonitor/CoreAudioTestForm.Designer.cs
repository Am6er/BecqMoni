namespace BecquerelMonitor
{
	// Token: 0x020000C2 RID: 194
	public partial class CoreAudioTestForm : global::System.Windows.Forms.Form
	{
		// Token: 0x06000965 RID: 2405 RVA: 0x00036E74 File Offset: 0x00035074
		protected override void Dispose(bool disposing)
		{
			if (disposing && this.components != null)
			{
				this.components.Dispose();
			}
			base.Dispose(disposing);
		}

		// Token: 0x06000966 RID: 2406 RVA: 0x00036E9C File Offset: 0x0003509C
		void InitializeComponent()
		{
			global::XPTable.Models.DataSourceColumnBinder dataSourceColumnBinder = new global::XPTable.Models.DataSourceColumnBinder();
			global::XPTable.Renderers.DragDropRenderer dragDropRenderer = new global::XPTable.Renderers.DragDropRenderer();
			this.table1 = new global::XPTable.Models.Table();
			this.columnModel1 = new global::XPTable.Models.ColumnModel();
			this.textColumn1 = new global::XPTable.Models.TextColumn();
			this.textColumn2 = new global::XPTable.Models.TextColumn();
			this.textColumn3 = new global::XPTable.Models.TextColumn();
			this.tableModel1 = new global::XPTable.Models.TableModel();
			this.label1 = new global::System.Windows.Forms.Label();
			this.trackBar1 = new global::System.Windows.Forms.TrackBar();
			((global::System.ComponentModel.ISupportInitialize)this.table1).BeginInit();
			((global::System.ComponentModel.ISupportInitialize)this.trackBar1).BeginInit();
			base.SuspendLayout();
			this.table1.BorderColor = global::System.Drawing.Color.Black;
			this.table1.ColumnModel = this.columnModel1;
			this.table1.DataMember = null;
			this.table1.DataSourceColumnBinder = dataSourceColumnBinder;
			dragDropRenderer.ForeColor = global::System.Drawing.Color.Red;
			this.table1.DragDropRenderer = dragDropRenderer;
			this.table1.GridLines = global::XPTable.Models.GridLines.Both;
			this.table1.GridLinesContrainedToData = false;
			this.table1.Location = new global::System.Drawing.Point(-1, 12);
			this.table1.Name = "table1";
			this.table1.Size = new global::System.Drawing.Size(545, 214);
			this.table1.TabIndex = 0;
			this.table1.TableModel = this.tableModel1;
			this.table1.Text = "table1";
			this.table1.UnfocusedBorderColor = global::System.Drawing.Color.Black;
			this.columnModel1.Columns.AddRange(new global::XPTable.Models.Column[]
			{
				this.textColumn1,
				this.textColumn2,
				this.textColumn3
			});
			this.textColumn1.IsTextTrimmed = false;
			this.textColumn1.Width = 120;
			this.textColumn2.IsTextTrimmed = false;
			this.textColumn2.Width = 120;
			this.textColumn3.IsTextTrimmed = false;
			this.label1.AutoSize = true;
			this.label1.Location = new global::System.Drawing.Point(23, 265);
			this.label1.Name = "label1";
			this.label1.Size = new global::System.Drawing.Size(35, 12);
			this.label1.TabIndex = 1;
			this.label1.Text = "label1";
			this.trackBar1.Location = new global::System.Drawing.Point(281, 232);
			this.trackBar1.Maximum = 100;
			this.trackBar1.Name = "trackBar1";
			this.trackBar1.Size = new global::System.Drawing.Size(216, 45);
			this.trackBar1.TabIndex = 2;
			this.trackBar1.TickFrequency = 10;
			this.trackBar1.Scroll += new global::System.EventHandler(this.trackBar1_Scroll);
			base.AutoScaleDimensions = new global::System.Drawing.SizeF(6f, 12f);
			base.AutoScaleMode = global::System.Windows.Forms.AutoScaleMode.Font;
			base.ClientSize = new global::System.Drawing.Size(556, 286);
			base.Controls.Add(this.trackBar1);
			base.Controls.Add(this.label1);
			base.Controls.Add(this.table1);
			base.Name = "CoreAudioTestForm";
			this.Text = "CoreAudioTestForm";
			((global::System.ComponentModel.ISupportInitialize)this.table1).EndInit();
			((global::System.ComponentModel.ISupportInitialize)this.trackBar1).EndInit();
			base.ResumeLayout(false);
			base.PerformLayout();
		}

		// Token: 0x04000538 RID: 1336
		global::System.ComponentModel.IContainer components;

		// Token: 0x04000539 RID: 1337
		global::XPTable.Models.Table table1;

		// Token: 0x0400053A RID: 1338
		global::XPTable.Models.ColumnModel columnModel1;

		// Token: 0x0400053B RID: 1339
		global::XPTable.Models.TextColumn textColumn1;

		// Token: 0x0400053C RID: 1340
		global::XPTable.Models.TextColumn textColumn2;

		// Token: 0x0400053D RID: 1341
		global::XPTable.Models.TableModel tableModel1;

		// Token: 0x0400053E RID: 1342
		global::XPTable.Models.TextColumn textColumn3;

		// Token: 0x0400053F RID: 1343
		global::System.Windows.Forms.Label label1;

		// Token: 0x04000540 RID: 1344
		global::System.Windows.Forms.TrackBar trackBar1;
	}
}
