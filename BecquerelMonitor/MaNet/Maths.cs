using System;


namespace MaNet
{
    /// <summary>
    /// Minor staic class to hold hypot method
    /// </summary>
   static  class Maths
    {

       


       /// <summary>
       ///  sqrt(a^2 + b^2) without under/overflow.
       /// </summary>
       /// <param name="a">a</param>
       /// <param name="b">a</param>
       /// <returns>the length of the radius defined by a and b</returns>
        public static double Hypot(double a, double b)
        {  

            double r;

            if (Math.Abs(a) > Math.Abs(b))
            {

                r = b / a;

                r = Math.Abs(a) * Math.Sqrt(1 + r * r);

            }
            else if (b != 0)
            {

                r = a / b;

                r = Math.Abs(b) * Math.Sqrt(1 + r * r);

            }
            else
            {

                r = 0.0;

            }

            return r;

         }

    }
}