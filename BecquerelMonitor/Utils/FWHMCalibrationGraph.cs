using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    public partial class FWHMCalibrationGraph : Form
    {
        public FWHMCalibrationGraph(MainForm mainForm)
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            this.mainForm = mainForm;
            this.Icon = Resources.becqmoni;
            this.Size = new Size(this.mainForm.Width * 3 / 4, this.mainForm.Height * 3 / 4);
        }

        MainForm mainForm;
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();
        int width;
        int height;
        int startwidth = 10;
        int startheight = 10;
        int maxChannels;
        CalibrationPeak glowPoint;
        int mouseX = 0;
        int mouseY = 0;
        bool recalcCurve = true;
        bool polycorrect = false;
        bool formloading = true;
        double maxFWHM;
        List<CalibrationPeak> points, originalpoints;
        FwhmCalibration fwhmCalibration, originalfwhmCalibration;

        public void Init(FwhmCalibration fwhmCalibration, int maxchannel)
        {
            this.maxChannels = maxchannel;
            this.maxFWHM = fwhmCalibration.ChannelToFwhm(maxchannel);
            this.fwhmCalibration = fwhmCalibration.Clone();
            if (this.fwhmCalibration.NotCalibrated()) this.fwhmCalibration.PerformCalibration(this.maxChannels);
            this.points = new List<CalibrationPeak>(fwhmCalibration.CalibrationPeaks);
            this.originalpoints = new List<CalibrationPeak>(fwhmCalibration.CalibrationPeaks);
        }

        void PrepareData()
        {
            this.width = base.ClientSize.Width - startwidth;
            this.height = base.ClientSize.Height - startheight;
        }

        void PaintBackground(Graphics g)
        {
            Color bgcolor = this.globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color;
            using (Brush brush = new SolidBrush(bgcolor))
            {
                g.FillRectangle(brush, this.startwidth, this.startheight, this.width - this.startwidth, this.height - this.startheight);
                using (Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color))
                {
                    g.DrawLine(pen, this.startwidth, this.startheight, this.width, this.startheight);
                    g.DrawLine(pen, this.width, this.startheight, this.width, this.height);
                    g.DrawLine(pen, this.width, this.height, this.startwidth, this.height);
                    g.DrawLine(pen, this.startwidth, this.height, this.startwidth, this.startheight);
                }
            }
        }

        void PaintAxis(Graphics g)
        {
            int ch_step = this.maxChannels / 6;
            double fwhm_step = this.maxFWHM / 6;
            int x_points = this.maxChannels / ch_step;
            int y_points = (int)(this.maxFWHM / fwhm_step);

            Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);
            Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color);
            for (int i = 1; i < x_points; i++)
            {
                int x_high = this.ChanToPx(i * ch_step);
                int y_high = this.startheight;
                int x_low = x_high;
                int y_low = this.height;
                g.DrawLine(pen, x_high, y_high, x_low, y_low);
                Rectangle r = new Rectangle(x_low, y_low - 16, 32, 32);
                g.DrawString((i * ch_step).ToString(), this.Font, brush, r);
            }

            for (int i = 1; i < y_points; i++)
            {
                int x_left = this.startwidth;
                int y_left = this.height - this.FWHMToPx(i * fwhm_step);
                int x_right = this.width;
                int y_right = y_left;
                g.DrawLine(pen, x_left, y_left, x_right, y_right);
                Rectangle r = new Rectangle(x_left, y_left - 16, 32, 32);
                g.DrawString((i * (int)fwhm_step).ToString(), this.Font, brush, r);
            }

            Rectangle rlabel = new Rectangle(this.startwidth, this.startheight, 120, 32);
            g.DrawString(Resources.ChartHeaderFWHM, this.Font, brush, rlabel);
            rlabel = new Rectangle(this.width - 60, this.height - 32, 120, 32);
            g.DrawString(Resources.ChartHeaderChannel, this.Font, brush, rlabel);

        }

        void PaintChart(Graphics g)
        {
            if (this.recalcCurve)
            {
                this.polycorrect = fwhmCalibration.PerformCalibration(this.maxChannels);
                this.recalcCurve = false;
            }
            if (this.formloading)
            {
                this.originalfwhmCalibration = this.fwhmCalibration.Clone();
                this.formloading = false;
            }
            Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.ActiveSpectrumColor.Color);
            if (!this.polycorrect)
            {
                pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.BgDiffColor.Color);
            }
            for (int i = 0; i < this.maxChannels - 1; i++)
            {
                if (this.height - FWHMToPx(i + 1) <= this.startheight)
                {
                    break;
                }
                if (FWHMToPx(i) > 0)
                {
                    g.DrawLine(pen, ChanToPx(i), this.height - FWHMToPx(i), ChanToPx(i + 1), this.height - FWHMToPx(i + 1));
                }
            }
        }

        void PaintPoints(Graphics g)
        {
            Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.SelectionNetColor.Color);
            Brush glowbrush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.ROIBorderColor.Color);
            Brush textbrush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);
            int r = 10;
            foreach (CalibrationPeak point in this.points)
            {
                if (this.glowPoint != null && this.glowPoint.Channel == point.Channel && this.glowPoint.FWHM == point.FWHM)
                {
                    g.FillEllipse(glowbrush, ChanToPx(point.Channel) - r / 2, this.height - FWHMToPx(point.FWHM) - r / 2, r, r);
                    Rectangle label;
                    if (this.mouseX - 110 < this.startwidth)
                    {
                        label = new Rectangle(this.mouseX + 15, this.mouseY - 48, 120, 48);
                    }
                    else
                    {
                        label = new Rectangle(this.mouseX - 120, this.mouseY - 48, 120, 48);
                    }


                    string labeltext = string.Concat(
                            Resources.ChartHeaderChannel,
                            " ",
                            point.Channel,
                            "\n",
                            Resources.ChartHeaderEnergy,
                            " ",
                            point.Energy.ToString("f2"),
                            "\n",
                            Resources.ChartHeaderFWHM,
                            " ",
                            point.FWHM,
                            " ch\n",
                            Resources.Delta,
                            Resources.ChartHeaderFWHM,
                            " ",
                            (point.FWHM - this.fwhmCalibration.ChannelToFwhm(point.Channel)).ToString("f1")
                         );
                    g.DrawString(labeltext, this.Font, textbrush, label);
                    continue;
                }
                g.FillEllipse(brush, ChanToPx(point.Channel) - r / 2, this.height - FWHMToPx(point.FWHM) - r / 2, r, r);
            }
        }

        void WriteCalibration(Graphics g)
        {
            Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);
            Rectangle label = new Rectangle(100, this.startheight, 1000, 62);
            string functiontext = this.fwhmCalibration.ToString();
            functiontext += "\n" + Resources.MSGMSE + ":" + "\n"
            + "\t" + Resources.Default + ": " + Utils.CalibrationSolver.MSE(this.originalfwhmCalibration, this.originalpoints).ToString("f4") + "\n"
            + "\t" + Resources.Current + ": " + Utils.CalibrationSolver.MSE(this.fwhmCalibration, this.points).ToString("f4");
            if (!this.polycorrect)
            {
                functiontext += "\n" + Resources.CalibrationFunctionError;
            }
            g.DrawString(functiontext, this.Font, brush, label);
        }

        private void FWHMCalibrationGraph_MouseMove(object sender, MouseEventArgs e)
        {
            int X = e.X;
            int Y = e.Y;
            int r = 10;
            if (this.glowPoint != null && e.Button == MouseButtons.Left)
            {
                if (X <= this.startwidth || X >= this.width) return;
                if (Y <= this.startheight || Y >= this.height) return;
                int newChannel = PxToChannel(X);
                if (newChannel < 0)
                {
                    newChannel = 0;
                    X = ChanToPx(0);
                }
                else if (newChannel > this.maxChannels)
                {
                    newChannel = this.maxChannels;
                    X = ChanToPx(this.maxChannels);
                }
                this.glowPoint.Channel = newChannel;
                this.mouseX = X;
                this.mouseY = Y;
                this.recalcCurve = true;
                base.Invalidate();
                return;
            }
            foreach (CalibrationPeak point in this.points)
            {
                if (X >= ChanToPx(point.Channel) - r / 2 && X <= ChanToPx(point.Channel) + r / 2)
                {
                    if (Y >= this.height - FWHMToPx(point.FWHM) - r / 2 && Y <= this.height - FWHMToPx(point.FWHM) + r / 2)
                    {
                        this.glowPoint = point;
                        this.mouseX = X;
                        this.mouseY = Y;
                        base.Invalidate();
                        return;
                    }
                }
            }
            this.glowPoint = null;
            base.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs g)
        {
            Graphics graphics = g.Graphics;
            this.PrepareData();
            this.PaintBackground(graphics);
            this.PaintAxis(graphics);
            this.PaintChart(graphics);
            this.WriteCalibration(graphics);
            this.PaintPoints(graphics);
        }

        int ChanToPx(int ch)
        {
            return (int)((double)(this.width - this.startwidth) * (double)ch / (double)this.maxChannels + this.startwidth);
        }

        int FWHMToPx(int ch)
        {
            return (int)((double)(this.height - this.startheight) * this.fwhmCalibration.ChannelToFwhm(ch) / (double)this.maxFWHM);
        }

        int PxToChannel(int px)
        {
            return (int)((double)(this.maxChannels * (px - this.startwidth)) / (double)(this.width - this.startwidth));
        }

        private void updateCalibrationPointsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks = this.points;
            this.originalfwhmCalibration = this.fwhmCalibration.Clone();
            this.originalpoints = new List<CalibrationPeak>(this.points);
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.glowPoint = null;
            this.points = new List<CalibrationPeak>(this.originalpoints);
            this.fwhmCalibration = this.originalfwhmCalibration.Clone();
            base.Invalidate();
        }

        private void FWHMCalibrationGraph_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.contextMenuStrip1.Show(Cursor.Position);
            }
        }

        int FWHMToPx(double fwhm)
        {
            return (int)((double)(this.height - this.startheight) * fwhm / (double)this.maxFWHM);
        }
    }
}
