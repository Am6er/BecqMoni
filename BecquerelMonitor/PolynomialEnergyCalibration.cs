using BecquerelMonitor.Properties;
using MathNet.Numerics;
using System;
using System.Collections.Generic;
using System.Xml.Serialization;
using WinMM;

namespace BecquerelMonitor
{
    // Token: 0x02000094 RID: 148
    public class PolynomialEnergyCalibration : EnergyCalibration
    {
        // Token: 0x17000218 RID: 536
        // (get) Token: 0x0600072C RID: 1836 RVA: 0x00029CF0 File Offset: 0x00027EF0
        // (set) Token: 0x0600072D RID: 1837 RVA: 0x00029CF8 File Offset: 0x00027EF8
        public int PolynomialOrder
        {
            get
            {
                return this.polynomialOrder;
            }
            set
            {
                this.polynomialOrder = value;
                this.dirty = true;
            }
        }

        // Token: 0x17000219 RID: 537
        // (get) Token: 0x0600072E RID: 1838 RVA: 0x00029D04 File Offset: 0x00027F04
        // (set) Token: 0x0600072F RID: 1839 RVA: 0x00029D0C File Offset: 0x00027F0C
        [XmlArrayItem("Coefficient")]
        public double[] Coefficients
        {
            get
            {
                return this.coefficients;
            }
            set
            {
                this.coefficients = value;
                this.dirty = true;
            }
        }

        public PolynomialEnergyCalibration()
        {
            this.polynomialOrder = 2;
            double[] array = new double[3];
            array[1] = 1.0;
            this.coefficients = array;
        }

        // Token: 0x06000731 RID: 1841 RVA: 0x00029D50 File Offset: 0x00027F50
        public PolynomialEnergyCalibration(PolynomialEnergyCalibration calib)
        {
            this.polynomialOrder = calib.polynomialOrder;
            this.coefficients = (double[])calib.coefficients.Clone();
        }

        // Token: 0x06000732 RID: 1842 RVA: 0x00029D7C File Offset: 0x00027F7C
        public override bool Equals(EnergyCalibration calib)
        {
            PolynomialEnergyCalibration polynomialEnergyCalibration = (PolynomialEnergyCalibration)calib;
            if (this.polynomialOrder != polynomialEnergyCalibration.polynomialOrder)
            {
                return false;
            }
            for (int i = 0; i <= this.polynomialOrder; i++)
            {
                if (this.coefficients[i] != polynomialEnergyCalibration.coefficients[i])
                {
                    return false;
                }
            }
            return true;
        }

        public bool CheckCalibration(int channels = 8192)
        {
            if (this.polynomialOrder == 1)
            {
                if (this.Coefficients[1] == 0)
                {
                    return false;
                }
            }
            if (this.polynomialOrder == 2)
            {
                double c = this.Coefficients[0];
                double b = this.Coefficients[1];
                double a = this.Coefficients[2];
                double discriminant = Math.Pow(b, 2.0) - 4.0 * a * c;

                if (discriminant < 0)
                {
                    return false;
                }
            }
            for (int i = 1; i <= channels; i++)
            {
                double prevEnrg = this.ChannelToEnergy(i - 1);
                if (prevEnrg <= -100.0 || prevEnrg >= 12000.0) return false;
                if (prevEnrg > this.ChannelToEnergy(i))
                {
                    return false;
                }
            }
            return true;
        }

        // Token: 0x06000733 RID: 1843 RVA: 0x00029DD4 File Offset: 0x00027FD4
        public override double ChannelToEnergy(double n)
        {
            if (this.polynomialOrder == 4)
            {
                return this.coefficients[4] * Math.Pow(n, 4) + this.coefficients[3] * Math.Pow(n, 3) + this.coefficients[2] * Math.Pow(n, 2) + this.coefficients[1] * n + this.coefficients[0];
            }
            if (this.polynomialOrder == 3)
            {
                return this.coefficients[3] * Math.Pow(n, 3) + this.coefficients[2] * Math.Pow(n, 2) + this.coefficients[1] * n + this.coefficients[0];
            }
            if (this.polynomialOrder == 2)
            {
                return this.coefficients[2] * Math.Pow(n, 2) + this.coefficients[1] * n + this.coefficients[0];
            }
            return this.coefficients[1] * n + this.coefficients[0];
        }

        // Token: 0x06000734 RID: 1844 RVA: 0x00029E1C File Offset: 0x0002801C
        public override double EnergyToChannel(double enrg, int maxChannels = 10000)
        {
            
            if (this.dirty || this.energytochanel == null)
            {
                this.energytochanel = new Dictionary<double, double>();
                this.dirty = false;
                double value = EnrgToChannel(enrg, maxCh: maxChannels);
                this.energytochanel.Add(enrg, value);
                return value;
            } else
            {
                if (this.energytochanel.TryGetValue(enrg, out double value))
                {
                    return value;
                } else
                {
                    value = EnrgToChannel(enrg, maxCh: maxChannels);
                    this.energytochanel.Add(enrg, value);
                    return value;
                }
            }
        }

        double EnrgToChannel(double enrg, int maxCh = 10000)
        {
            if (enrg < 0 || enrg < this.coefficients[0])
            {
                return 0;
            }

            if (this.polynomialOrder == 1)
            {
                double k = this.coefficients[1];
                double b = this.coefficients[0];
                if (k == 0) k = 1;
                return (enrg - b) / k;
            }

            if (this.polynomialOrder == 2)
            {
                double a = this.coefficients[2];
                double b = this.coefficients[1];
                double c = this.coefficients[0] - enrg;
                if (a == 0.0)
                {
                    if (b == 0.0)
                    {
                        throw new Exception(Resources.CalibrationFunctionError);
                    }
                    return -c / b;
                }
                else
                {
                    double discriminant = Math.Pow(b, 2.0) - 4.0 * a * c;
                    if (discriminant < 0.0)
                    {
                        throw new Exception(Resources.CalibrationFunctionError);
                    }
                    return (-b + Math.Sqrt(discriminant)) / (2.0 * a);
                }
            }
            if (this.polynomialOrder == 4)
            {
                Func<double, double> f1 = x => this.coefficients[4] * x * x * x * x + this.coefficients[3] * x * x * x + this.coefficients[2] * x * x + this.coefficients[1] * x + this.coefficients[0] - enrg;
                try
                {
                    double roots = FindRoots.OfFunction(f1, 0, maxCh);
                    //System.Windows.Forms.MessageBox.Show("Calibration coefficients are incorrect channels for Energy: " + enrg + " roots = " + roots);
                    return Math.Round(roots, 2);
                }
                catch
                {
                    //throw new Exception(String.Format("Calibration coefficients are incorrect channels for Energy: " + enrg));
                    //System.Windows.Forms.MessageBox.Show("Calibration coefficients are incorrect channels for Energy: " + enrg);
                    return 0;
                }
            }

            if (this.polynomialOrder == 3)
            {

                Func<double, double> f1 = x => this.coefficients[3] * x * x * x + this.coefficients[2] * x * x + this.coefficients[1] * x + this.coefficients[0] - enrg;
                try
                {
                    double roots = FindRoots.OfFunction(f1, 0, maxCh);
                    return Math.Round(roots, 2);
                }
                catch
                {
                    //System.Windows.Forms.MessageBox.Show("Calibration coefficients are incorrect channels for Energy: " + enrg);
                    //throw new Exception(String.Format("Calibration coefficients are incorrect channels for Energy: " + enrg));
                    return 0;
                }
            }

            throw new NotImplementedException(String.Format(Resources.ERRUnsupportedCalibrationMethod, this.polynomialOrder));
        }

        // Token: 0x06000735 RID: 1845 RVA: 0x00029EF8 File Offset: 0x000280F8
        public override EnergyCalibration Clone()
        {
            return new PolynomialEnergyCalibration(this);
        }

        public override string ToString()
        {
            string result = "";
            string xpow = "";
            string sign = "";
            for(int i = 0; i < this.coefficients.Length; i++)
            {
                if (this.coefficients[i] != 0)
                {
                    if (i == 0)
                    {
                        xpow = "";
                        if (this.coefficients[i] > 0)
                        {
                            sign = "+";
                        }
                        else
                        {
                            sign = "";
                        }
                    }
                    if (i == 1)
                    {
                        xpow = "*x";
                        if (this.coefficients[i] > 0 && i != this.coefficients.Length - 1)
                        {
                            sign = "+";
                        }
                        else
                        {
                            sign = "";
                        }
                    }
                    if (i > 1)
                    {
                        xpow = "*x^" + i.ToString();
                        if (this.coefficients[i] > 0 && i != this.coefficients.Length - 1)
                        {
                            sign = "+";
                        }
                        else
                        {
                            sign = "";
                        }
                    }

                    result = sign + this.coefficients[i].ToString() + xpow + result;
                }
            }
            return "y = " + result;
        }

        // Token: 0x040003A4 RID: 932
        int polynomialOrder;

        // Token: 0x040003A5 RID: 933
        double[] coefficients;

        bool dirty = false;

        Dictionary<double, double> energytochanel = null;
    }
}
