using System;
using System.Xml.Serialization;

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
			}
		}

		// Token: 0x06000730 RID: 1840 RVA: 0x00029D18 File Offset: 0x00027F18
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
			for (int i = 0; i < this.polynomialOrder; i++)
			{
				if (this.coefficients[i] != polynomialEnergyCalibration.coefficients[i])
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
				return this.coefficients[4] * n * n * n * n + this.coefficients[3] * n * n * n + this.coefficients[2] * n * n + this.coefficients[1] * n + this.coefficients[0];
			}
			if (this.polynomialOrder == 3)
			{
				return this.coefficients[3] * n * n * n + this.coefficients[2] * n * n + this.coefficients[1] * n + this.coefficients[0];
			}
			return this.coefficients[2] * n * n + this.coefficients[1] * n + this.coefficients[0];
		}

		// Token: 0x06000734 RID: 1844 RVA: 0x00029E1C File Offset: 0x0002801C
		public override double EnergyToChannel(double enrg)
		{
			if (this.polynomialOrder == 2)
			{
				enrg += 1E-07;
				double num = this.coefficients[2];
				double num2 = this.coefficients[1];
				double num3 = this.coefficients[0] - enrg;
				if (Math.Abs(num) < 1E-07)
				{
					num = 0.0;
				}
				if (num == 0.0)
				{
					if (num2 == 0.0)
					{
						throw new Exception();
					}
					return -num3 / num2;
				}
				else
				{
					double num4 = Math.Pow(num2, 2.0) - 4.0 * num * num3;
					if (num4 < 0.0)
					{
						throw new OutofChannelException();
					}
					return (-num2 + Math.Sqrt(num4)) / (2.0 * num);
				}
			}
			if (this.polynomialOrder == 4)
            {
				double e = this.coefficients[0] - enrg;
				double d = this.coefficients[1];
				double c = this.coefficients[2];
				double b = this.coefficients[3];
				double a = this.coefficients[4];
					

				// https://en.wikipedia.org/wiki/Quartic_function
				double p = (8.0 * a * c - 3.0 * b * b) / (8.0 * a * a);
				double q = (b * b * b - 4.0 * a * b * c + 8.0 * a * a * d) / (8.0 * a * a * a);
				double delta0 = c * c - 3.0 * b * d + 12.0 * a * e;
				double delta1 = 2.0 * c * c * c - 9.0 * b * c * d + 27.0 * b * b * e + 27.0 * a * d * d - 72.0 * a * c * e;
				double Q = Math.Pow((delta1 + Math.Sqrt(delta1 * delta1 - 4.0 * delta0 * delta0 * delta0)) / 2.0, 1.0 / 3.0);
				double S = 0.5 * Math.Sqrt(-2.0 * p / 3.0 + (Q + delta0 / Q) / (3.0 * a));
				double x1 = -b / (4.0 * a) - S + Math.Sqrt(-4.0 * S * S - 2.0 * p + q / S) / 2.0;
				double x2 = -b / (4.0 * a) - S - Math.Sqrt(-4.0 * S * S - 2.0 * p + q / S) / 2.0;
				double x3 = -b / (4.0 * a) - S + Math.Sqrt(-4.0 * S * S - 2.0 * p - q / S) / 2.0;
				double x4 = -b / (4.0 * a) - S - Math.Sqrt(-4.0 * S * S - 2.0 * p - q / S) / 2.0;

				if (x1 > 0 && x1 < 100000.0)
                {
					return x1;
                }

				if (x2 > 0 && x2 < 100000.0)
				{
					return x2;
				}

				if (x3 > 0 && x3 < 100000.0)
				{
					return x3;
				}

				if (x4 > 0 && x4 < 100000.0)
				{
					return x4;
				}

				return 0;

			}

			throw new NotImplementedException("Four point calibration not implemented yet. Only 2,3,5 points exist.");
		}

		// Token: 0x06000735 RID: 1845 RVA: 0x00029EF8 File Offset: 0x000280F8
		public override EnergyCalibration Clone()
		{
			return new PolynomialEnergyCalibration(this);
		}

		// Token: 0x06000736 RID: 1846 RVA: 0x00029F00 File Offset: 0x00028100
		public override double MaximumChannel()
		{
			if (this.coefficients[2] >= 0.0)
			{
				return double.PositiveInfinity;
			}
			return -this.coefficients[1] / (2.0 * this.coefficients[2]);
		}

		// Token: 0x040003A4 RID: 932
		int polynomialOrder;

		// Token: 0x040003A5 RID: 933
		double[] coefficients;
	}
}
