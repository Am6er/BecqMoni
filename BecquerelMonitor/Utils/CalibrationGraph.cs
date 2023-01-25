using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.Utils
{
    public partial class CalibrationGraph : Form
    {
        public CalibrationGraph(MainForm mainForm)
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
        CalibrationPoint glowPoint;
        int mouseX = 0;
        int mouseY = 0;
        bool recalcPoly = true;
        double maxEnergy;
        List<CalibrationPoint> points, originalpoints;
        PolynomialEnergyCalibration calibration, originalcalibration;
        int polyorder;
        bool weights = false;

        public void SetCalibration(PolynomialEnergyCalibration calibration, int maxchannel, int order, bool useweight)
        {
            this.calibration = (PolynomialEnergyCalibration)calibration.Clone();
            this.originalcalibration = calibration;
            this.maxChannels = maxchannel;
            this.maxEnergy = 3500.0;
            this.polyorder = order;
            this.weights = useweight;
        }

        public void SetCalibrationPoints(List<CalibrationPoint> pts)
        {
            this.points = ClonePoints(pts);
            this.originalpoints = pts;
        }

        void PrepareData()
        {
            this.width = base.ClientSize.Width - startwidth;
            this.height = base.ClientSize.Height - startheight;
            this.maxEnergy = 3500.0;
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

            int x_points = this.maxChannels / 1000;
            int y_points = (int)(this.maxEnergy / 500);
            int chan_pitch = this.ChanToPx(1000);
            int energy_pitch = this.EnergyToPx(500.0);
            Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);
            Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.GridColor1.Color);
            for (int i = 1; i < x_points; i++)
            {
                int x_high = i * chan_pitch;
                int y_high = this.startheight;
                int x_low = x_high;
                int y_low = this.height;
                g.DrawLine(pen, x_high, y_high, x_low, y_low);
                Rectangle r = new Rectangle(x_low, y_low - 16, 32, 32);
                g.DrawString((i * 1000).ToString(), this.Font, brush, r);
            }

            for (int i = 1; i < y_points; i++)
            {
                int x_left = this.startwidth;
                int y_left = this.height - i * energy_pitch;
                int x_right = this.width;
                int y_right = y_left;
                g.DrawLine(pen, x_left, y_left, x_right, y_right);
                Rectangle r = new Rectangle(x_left, y_left - 16, 32, 32);
                g.DrawString((i * 500).ToString(), this.Font, brush, r);
            }

            Rectangle rlabel = new Rectangle(this.startwidth, this.startheight, 120, 32);
            g.DrawString(Resources.ChartHeaderEnergy, this.Font, brush, rlabel);
            rlabel = new Rectangle(this.width - 60, this.height - 32, 120, 32);
            g.DrawString(Resources.ChartHeaderChannel, this.Font, brush, rlabel);

        }

        void PaintChart(Graphics g)
        {
            using (Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.ActiveSpectrumColor.Color))
            {
                if (this.recalcPoly)
                {
                    double[] matrix;
                    if (this.weights)
                    {
                        matrix = Utils.CalibrationSolver.SolveWeighted(points, this.polyorder);
                    }
                    else
                    {
                        matrix = Utils.CalibrationSolver.Solve(points, this.polyorder);
                    }
                    if (matrix != null)
                    {
                        this.calibration.Coefficients = new double[matrix.Length];
                        this.calibration.PolynomialOrder = matrix.Length - 1;
                        this.calibration.Coefficients = matrix;
                    }
                    this.recalcPoly = false;
                }
                for (int i = 0; i < this.maxChannels - 1; i++)
                {
                    if (this.height - EnergyToPx(i + 1) <= this.startheight)
                    {
                        break;
                    }
                    if (EnergyToPx(i) > 0)
                    {
                        g.DrawLine(pen, ChanToPx(i), this.height - EnergyToPx(i), ChanToPx(i + 1), this.height - EnergyToPx(i + 1));
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
            foreach (CalibrationPoint point in this.points)
            {
                if (this.glowPoint != null && this.glowPoint.Channel == point.Channel && this.glowPoint.Energy == point.Energy)
                {
                    g.FillEllipse(glowbrush, ChanToPx(point.Channel) - r / 2, this.height - EnergyToPx((double)point.Energy) - r / 2, r, r);
                    Rectangle label = new Rectangle(this.mouseX - 120, this.mouseY - 32, 120, 36);
                    string labeltext = string.Concat(Resources.ChartHeaderChannel, " ", point.Channel, "\n", Resources.ChartHeaderEnergy, " ", point.Energy);
                    g.DrawString(labeltext, this.Font, textbrush, label);
                    continue;
                }
                g.FillEllipse(brush, ChanToPx(point.Channel) - r / 2, this.height - EnergyToPx((double)point.Energy) - r / 2, r, r);
            }
        }

        void WriteCalibration(Graphics g)
        {
            Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);
            Rectangle label = new Rectangle(100, this.startheight, 1000, 32);
            g.DrawString(this.calibration.ToString(), this.Font, brush, label);
        }

        private void CalibrationGraph_MouseMove(object sender, MouseEventArgs e)
        {
            int X = e.X;
            int Y = e.Y;
            int r = 10;
            if (this.glowPoint != null && e.Button == MouseButtons.Left)
            {
                if (Y <= this.startheight || Y >= this.height) return;
                this.glowPoint.Energy = decimal.Parse(PxToEnergy(this.height - Y).ToString("0.00"));
                this.mouseX = X;
                this.mouseY = Y;
                this.recalcPoly = true;
                base.Invalidate();
                return;
            }
            foreach (CalibrationPoint point in this.points)
            {
                if (X >= ChanToPx(point.Channel) - r / 2 && X <= ChanToPx(point.Channel) + r / 2)
                {
                    if (Y >= this.height - EnergyToPx((double)point.Energy) - r / 2 && Y <= this.height - EnergyToPx((double)point.Energy) + r / 2)
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

        int EnergyToPx(int ch)
        {
            return (int)((double)(this.height - this.startheight) * this.calibration.ChannelToEnergy(ch) / (double)this.maxEnergy);
        }

        double PxToEnergy(int px)
        {
            return (double)this.maxEnergy * (double)px / (double)(this.height - this.startheight);
        }

        private void updateCalibrationPointsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints = this.points;
            this.originalcalibration = (PolynomialEnergyCalibration)this.calibration.Clone();
            this.originalpoints = ClonePoints(this.points);
        }

        private void resetToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.glowPoint = null;
            this.points = ClonePoints(this.originalpoints);
            this.calibration = (PolynomialEnergyCalibration)this.originalcalibration.Clone();
            base.Invalidate();
        }

        private void CalibrationGraph_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                this.contextMenuStrip1.Show(Cursor.Position);
            }
        }

        int EnergyToPx(double energy)
        {
            return (int)((double)(this.height - this.startheight) * energy / (double)this.maxEnergy);
        }

        List<CalibrationPoint> ClonePoints(List<CalibrationPoint> pts)
        {
            List <CalibrationPoint> result = new List<CalibrationPoint>();
            foreach (CalibrationPoint point in pts)
            {
                CalibrationPoint p = new CalibrationPoint(point.Channel, point.Energy, point.Count);
                result.Add(p);
            }
            return result;
        }
    }
}
