using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    public partial class FWHMCalibrationGraph : Form
    {
        public FWHMCalibrationGraph()
        {
            InitializeComponent();
            this.DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint, true);
            SetStyle(ControlStyles.UserPaint, true);
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

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
        bool drawInfoPanel = false;
        double maxFWHM;
        List<CalibrationPeak> points, originalpoints;
        FwhmCalibration fwhmCalibration, originalfwhmCalibration;
        PolynomialEnergyCalibration energyCalibration;
        DialogResult result = DialogResult.No;

        public void Init(FwhmCalibration fwhmCalibration, int maxchannel)
        {
            this.energyCalibration = (PolynomialEnergyCalibration)this.mainForm.ActiveDocument.ActiveResultData.EnergySpectrum.EnergyCalibration.Clone();

            this.maxChannels = maxchannel;
            this.fwhmCalibration = fwhmCalibration.Clone();
            this.points = CalibrationPeak.ClonePeaks(this.fwhmCalibration.CalibrationPeaks);
            this.fwhmCalibration.CalibrationPeaks = this.points;
            this.RecalculateCalibration();
            // oroginal calibration for reset
            this.originalfwhmCalibration = this.fwhmCalibration.Clone();
            this.originalpoints = CalibrationPeak.ClonePeaks(this.points);
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
            int ch_step = Math.Max(1, this.maxChannels / 6);
            double fwhm_step = this.maxFWHM / 6;
            int x_points = this.maxChannels > 0 ? this.maxChannels / ch_step : 0;
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
                int y_left = this.height - this.FWHMToPy(i * fwhm_step);
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

            // Dispose GDI objects: this runs on every paint and used to leak handles.
            brush.Dispose();
            pen.Dispose();
        }

        void PaintChart(Graphics g)
        {
            Color chartColor = this.polycorrect
                ? this.globalConfigManager.GlobalConfig.ColorConfig.ActiveSpectrumColor.Color
                : this.globalConfigManager.GlobalConfig.ColorConfig.BgDiffColor.Color;
            using (Pen pen = new Pen(chartColor))
            {
                for (int i = 0; i < this.maxChannels - 1; i++)
                {
                    if (this.height - ChanToPy(i + 1) <= this.startheight)
                    {
                        break;
                    }
                    if (ChanToPy(i) > 0)
                    {
                        g.DrawLine(pen, ChanToPx(i), this.height - ChanToPy(i), ChanToPx(i + 1), this.height - ChanToPy(i + 1));
                    }
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
                    g.FillEllipse(glowbrush, ChanToPx(point.Channel) - r / 2, this.height - FWHMToPy(point.FWHM) - r / 2, r, r);
                    Rectangle label;
                    int panelWidth = 120;
                    int panelHeight = 48;
                    int recX, recY;

                    if (this.mouseX - (panelWidth - 10) < this.startwidth)
                    {
                        recX = this.mouseX;
                    }
                    else
                    {
                        recX = this.mouseX - panelWidth;
                    }

                    if (this.mouseY - (panelHeight - 10) < this.startheight)
                    {
                        recY = this.mouseY;
                    }
                    else
                    {
                        recY = this.mouseY - panelHeight;
                    }

                    label = new Rectangle(recX, recY, panelWidth, panelHeight);

                    // draw panel bg
                    Color bgcolor = this.globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color;
                    using (Brush brush2 = new SolidBrush(bgcolor))
                    {
                        g.FillRectangle(brush2, label.Left, label.Top, panelWidth, panelHeight);
                        using (Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color))
                        {
                            g.DrawLine(pen, label.Left, label.Bottom, label.Right, label.Bottom);
                            g.DrawLine(pen, label.Right, label.Bottom, label.Right, label.Top);
                            g.DrawLine(pen, label.Right, label.Top, label.Left, label.Top);
                            g.DrawLine(pen, label.Left, label.Top, label.Left, label.Bottom);
                        }
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
                g.FillEllipse(brush, ChanToPx(point.Channel) - r / 2, this.height - FWHMToPy(point.FWHM) - r / 2, r, r);
            }
            brush.Dispose();
            glowbrush.Dispose();
            textbrush.Dispose();
        }

        void DrawInfoPanel(Graphics g)
        {
            if (!drawInfoPanel) return;
            Rectangle label;
            int panelWidth = 120;
            int panelHeight = 36;
            int recX, recY;

            if (this.mouseX - (panelWidth - 10) < this.startwidth)
            {
                recX = this.mouseX;
            }
            else
            {
                recX = this.mouseX - panelWidth;
            }

            if (this.mouseY - (panelHeight - 10) < this.startheight)
            {
                recY = this.mouseY;
            }
            else
            {
                recY = this.mouseY - panelHeight;
            }

            label = new Rectangle(recX, recY, panelWidth, panelHeight);

            // draw panel bg
            Color bgcolor = this.globalConfigManager.GlobalConfig.ColorConfig.BackgroundColor.Color;
            using (Brush brush = new SolidBrush(bgcolor))
            {
                g.FillRectangle(brush, label.Left, label.Top, panelWidth, panelHeight);
                using (Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color))
                {
                    g.DrawLine(pen, label.Left, label.Bottom, label.Right, label.Bottom);
                    g.DrawLine(pen, label.Right, label.Bottom, label.Right, label.Top);
                    g.DrawLine(pen, label.Right, label.Top, label.Left, label.Top);
                    g.DrawLine(pen, label.Left, label.Top, label.Left, label.Bottom);
                }
            }

            // draw text
            Brush textbrush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);

            int channel = PxToChannel(this.mouseX);
            double energy = energyCalibration.ChannelToEnergy(channel);
            int fwhm = ChanToPy(channel);

            string labeltext = string.Concat(
                    Resources.ChartHeaderChannel,
                    " ",
                    channel,
                    "\n",
                    Resources.ChartHeaderEnergy,
                    " ",
                    energy.ToString("f2"),
                    "\n",
                    Resources.ChartHeaderFWHM,
                    " ",
                    PyToChan(this.height - this.mouseY),
                    " ch\n"
                 );
            g.DrawString(labeltext, this.Font, textbrush, label);
            textbrush.Dispose();
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
            brush.Dispose();
        }

        private void FWHMCalibrationGraph_MouseMove(object sender, MouseEventArgs e)
        {
            int X = e.X;
            int Y = e.Y;
            int r = 10;
            if (e.Button == MouseButtons.None)
            {
                if (X <= this.startwidth || X >= this.width) return;
                if (Y <= this.startheight || Y >= this.height) return;
                this.mouseX = X;
                this.mouseY = Y;
                drawInfoPanel = true;
            } else if (this.glowPoint != null && e.Button == MouseButtons.Left)
            {
                if (X <= this.startwidth || X >= this.width) return;
                if (Y <= this.startheight || Y >= this.height) return;
                drawInfoPanel = false;
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
                this.glowPoint.Energy = this.energyCalibration.ChannelToEnergy(newChannel);
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
                    if (Y >= this.height - FWHMToPy(point.FWHM) - r / 2 && Y <= this.height - FWHMToPy(point.FWHM) + r / 2)
                    {
                        drawInfoPanel = false;
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
            if (this.recalcCurve)
            {
                this.RecalculateCalibration();
            }
            this.PaintBackground(graphics);
            this.PaintAxis(graphics);
            this.PaintChart(graphics);
            this.WriteCalibration(graphics);
            this.PaintPoints(graphics);
            this.DrawInfoPanel(graphics);
        }

        private void updateCalibrationPointsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.mainForm.ActiveDocument.ActiveResultData.FwhmCalibration.CalibrationPeaks = this.points;
            this.originalfwhmCalibration = this.fwhmCalibration.Clone();
            this.originalpoints = CalibrationPeak.ClonePeaks(this.points);
            this.result = DialogResult.Yes;
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.glowPoint = null;
            this.points = CalibrationPeak.ClonePeaks(this.originalpoints);
            this.fwhmCalibration = this.originalfwhmCalibration.Clone();
            this.fwhmCalibration.CalibrationPeaks = this.points;
            this.recalcCurve = true;
            base.Invalidate();
        }

        private void FWHMCalibrationGraph_FormClosing(object sender, FormClosingEventArgs e)
        {
            this.DialogResult = result;
        }

        private void FWHMCalibrationGraph_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.contextMenuStrip1.Show(Cursor.Position);
            }
        }

        int ChanToPx(int ch)
        {
            int plotWidth = this.width - this.startwidth;
            if (this.maxChannels <= 0 || plotWidth <= 0)
            {
                return this.startwidth;
            }

            double ratio = (double)ch / this.maxChannels;
            if (ratio <= 0.0) return this.startwidth;
            if (ratio >= 1.0) return this.width;
            return this.startwidth + (int)(plotWidth * ratio);
        }

        int PxToChannel(int px)
        {
            int plotWidth = this.width - this.startwidth;
            if (this.maxChannels <= 0 || plotWidth <= 0)
            {
                return 0;
            }

            double ratio = (double)(px - this.startwidth) / plotWidth;
            if (ratio <= 0.0) return 0;
            if (ratio >= 1.0) return this.maxChannels;
            return (int)(this.maxChannels * ratio);
        }

        int ChanToPy(int ch)
        {
            return this.FWHMToPy(this.fwhmCalibration.ChannelToFwhm(ch));
        }

        int PyToChan(int py)
        {
            int plotHeight = this.height - this.startheight;
            if (plotHeight <= 0 || this.maxFWHM <= 0.0)
            {
                return 0;
            }

            return ToInt32((this.maxFWHM * py) / plotHeight);
        }

        int FWHMToPy(double fwhm)
        {
            int plotHeight = this.height - this.startheight;
            if (plotHeight <= 0 || !IsFinitePositive(fwhm) || !IsFinitePositive(this.maxFWHM))
            {
                return 0;
            }

            double ratio = fwhm / this.maxFWHM;
            if (ratio <= 0.0) return 0;
            if (ratio >= 1.0) return plotHeight;
            return (int)(plotHeight * ratio);
        }

        private void RecalculateCalibration()
        {
            this.polycorrect = this.fwhmCalibration.PerformCalibration(this.maxChannels);
            this.maxFWHM = this.GetMaxFwhm();
            this.recalcCurve = false;
        }

        private double GetMaxFwhm()
        {
            double maximum = 0.0;

            foreach (CalibrationPeak point in this.points)
            {
                if (IsFinitePositive(point.FWHM))
                {
                    maximum = Math.Max(maximum, point.FWHM);
                }
            }

            for (int channel = 0; channel <= this.maxChannels; channel++)
            {
                double fwhm = this.fwhmCalibration.ChannelToFwhm(channel);
                if (IsFinitePositive(fwhm))
                {
                    maximum = Math.Max(maximum, fwhm);
                }
            }

            if (maximum <= 0.0)
            {
                return 1.0;
            }

            return maximum <= double.MaxValue / 1.1 ? maximum * 1.1 : maximum;
        }

        private static bool IsFinitePositive(double value)
        {
            return value > 0.0 && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static int ToInt32(double value)
        {
            if (double.IsNaN(value) || value <= int.MinValue) return int.MinValue;
            if (double.IsInfinity(value) || value >= int.MaxValue) return int.MaxValue;
            return (int)value;
        }
    }
}
