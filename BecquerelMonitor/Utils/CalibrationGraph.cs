using System;
using System.Collections.Generic;
using System.Drawing;
using System.Security.Policy;
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
        bool polycorrect = false;
        bool formloading = true;
        double maxEnergy;
        List<CalibrationPoint> points, originalpoints;
        PolynomialEnergyCalibration calibration, originalcalibration;
        int polyorder;
        bool weights = false;

        public void SetCalibration(PolynomialEnergyCalibration calibration, int maxchannel, int order, bool useweight)
        {
            this.calibration = (PolynomialEnergyCalibration)calibration.Clone();
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
            int ch_step = 500;
            double en_step = 500.0;
            int x_points = this.maxChannels / ch_step;
            int y_points = (int)(this.maxEnergy / en_step);
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
                int y_left = this.height - this.EnergyToPx(i * en_step);
                int x_right = this.width;
                int y_right = y_left;
                g.DrawLine(pen, x_left, y_left, x_right, y_right);
                Rectangle r = new Rectangle(x_left, y_left - 16, 32, 32);
                g.DrawString((i * (int)en_step).ToString(), this.Font, brush, r);
            }

            Rectangle rlabel = new Rectangle(this.startwidth, this.startheight, 120, 32);
            g.DrawString(Resources.ChartHeaderEnergy, this.Font, brush, rlabel);
            rlabel = new Rectangle(this.width - 60, this.height - 32, 120, 32);
            g.DrawString(Resources.ChartHeaderChannel, this.Font, brush, rlabel);

        }

        void PaintChart(Graphics g)
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
                    if (this.calibration.CheckCalibration(channels: this.maxChannels))
                    {
                        this.polycorrect = true;
                    }
                    else
                    {
                        this.polycorrect = false;
                    }
                }
                this.recalcPoly = false;
            }
            if (this.formloading)
            {
                this.originalcalibration = (PolynomialEnergyCalibration)this.calibration.Clone();
                this.formloading = false;
            }
            Pen pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.ActiveSpectrumColor.Color);
            if (!this.polycorrect)
            {
                pen = new Pen(this.globalConfigManager.GlobalConfig.ColorConfig.BgDiffColor.Color);
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
                    Rectangle label;
                    if (this.mouseX - 110 < this.startwidth)
                    {
                        label = new Rectangle(this.mouseX + 15, this.mouseY - 48, 120, 48);
                    } else
                    {
                        label = new Rectangle(this.mouseX - 120, this.mouseY - 48, 120, 48);
                    }
                        
                    string labeltext;
                    if (!this.weights)
                    {
                        labeltext = string.Concat(
                            Resources.ChartHeaderChannel, 
                            " ", 
                            point.Channel, 
                            "\n", 
                            Resources.ChartHeaderEnergy, 
                            " ", 
                            point.Energy,
                            "\n",
                            Resources.Delta,
                            Resources.ChartHeaderChannel,
                            " ",
                            (int)(point.Channel - this.calibration.EnergyToChannel((double)point.Energy, maxChannels: this.maxChannels))
                         );
                    } else
                    {
                        labeltext = string.Concat(
                            Resources.ChartHeaderChannel,
                            " ",
                            point.Channel,
                            "\n",
                            Resources.ChartHeaderEnergy,
                            " ",
                            point.Energy,
                            "\n",
                            Resources.ChartHeaderWeight,
                            " ",
                            point.Count,
                            "\n",
                            Resources.Delta,
                            Resources.ChartHeaderChannel,
                            " ",
                            (int)(point.Channel - this.calibration.EnergyToChannel((double)point.Energy, maxChannels: this.maxChannels))
                         );
                    }
                    g.DrawString(labeltext, this.Font, textbrush, label);
                    continue;
                }
                g.FillEllipse(brush, ChanToPx(point.Channel) - r / 2, this.height - EnergyToPx((double)point.Energy) - r / 2, r, r);
            }
        }

        void WriteCalibration(Graphics g)
        {
            Brush brush = new SolidBrush(this.globalConfigManager.GlobalConfig.ColorConfig.AxisFigureColor.Color);
            Rectangle label = new Rectangle(100, this.startheight, 1000, 62);
            string functiontext = this.calibration.ToString();
            if (!this.weights)
            {
                functiontext += "\n" + Resources.MSGMSE + ":" + "\n"
                    + "\t" + Resources.Default + ": " + Utils.CalibrationSolver.WMSE(this.originalcalibration.Coefficients, this.originalpoints).ToString("f4") + "\n"
                    + "\t" + Resources.Current + ": " + Utils.CalibrationSolver.WMSE(this.calibration.Coefficients, this.points).ToString("f4");

            } else
            {
                functiontext += "\n" + Resources.MSGMSE + ":" + "\n"
                    + "\t" + Resources.Default + ": " + Utils.CalibrationSolver.MSE(this.originalcalibration.Coefficients, this.originalpoints).ToString("f4") + "\n"
                    + "\t" + Resources.Current + ": " + Utils.CalibrationSolver.MSE(this.calibration.Coefficients, this.points).ToString("f4");
            }
            if (!this.polycorrect)
            {
                functiontext += "\n" + Resources.CalibrationFunctionError;
            }
            g.DrawString(functiontext, this.Font, brush, label);
        }

        private void CalibrationGraph_MouseMove(object sender, MouseEventArgs e)
        {
            int X = e.X;
            int Y = e.Y;
            int r = 10;
            if (this.glowPoint != null && e.Button == MouseButtons.Left)
            {
                if (X <= this.startwidth || X >= this.width) return;
                this.glowPoint.Channel = PxToChannel(X);
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

        int PxToChannel(int px)
        {
            return (int)((double)(this.maxChannels * (px - this.startwidth)) / (double)(this.width - this.startwidth));
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
