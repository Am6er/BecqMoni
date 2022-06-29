using System;
using System.Xml.Serialization;
using MathNet.Numerics;

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
				Func<double, double> f1 = x => this.coefficients[4]*x*x*x*x + this.coefficients[3]*x*x*x + this.coefficients[2]*x*x + this.coefficients[1]*x + this.coefficients[0] - enrg;
				try
                {
					double roots = FindRoots.OfFunction(f1, 0, 10000, accuracy: 0.1, maxIterations: 10000);
					return roots;
				} catch
                {
					throw new Exception(String.Format("Calibration coefficients are incorrect channels for Energy: " + enrg));
				}
			}

			if (this.polynomialOrder == 3)
			{

				Func<double, double> f1 = x => this.coefficients[3] * x * x * x + this.coefficients[2] * x * x + this.coefficients[1] * x + this.coefficients[0] - enrg;
				double roots = FindRoots.OfFunction(f1, 0, 10000);
				return roots;

			}

			throw new NotImplementedException("Four point calibration not implemented yet. Only 2,3,4,5 points exist.");
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
