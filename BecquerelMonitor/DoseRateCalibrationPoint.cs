using System;

namespace BecquerelMonitor
{
    /// <summary>
    /// ⛔ `IComparable` ЗДЕСЬ ОБЯЗАТЕЛЕН, и его не было (найдено 05.09.2026,
    /// `C4`). `DoseRateConfig.DoseRateCalibrationPoints` в сеттере зовёт
    /// `List.Sort()` без сравнителя — а без `IComparable` это
    /// `InvalidOperationException` на любом списке длиннее одного элемента.
    /// Дефект жил незамеченным потому, что все живые пути обходили сеттер:
    /// форма присваивала ПУСТОЙ список и дописывала точки по одной (пустой
    /// список сортируется молча), копирующий конструктор пишет прямо в поле, а
    /// разбор XML наполняет коллекцию через геттер. Первое же присваивание
    /// готового списка валило программу — и заодно выяснилось, что заявленная
    /// сеттером сортировка не выполнялась НИКОГДА.
    ///
    /// Порядок — по нижней границе: диапазоны идут встык, и это их
    /// естественный порядок на шкале.
    /// </summary>
    public class DoseRateCalibrationPoint : IComparable<DoseRateCalibrationPoint>, IComparable
    {
        public DoseRateCalibrationPoint()
        {
        }

        public int CompareTo(DoseRateCalibrationPoint other)
        {
            if (other == null)
            {
                return 1;
            }

            int byLower = this.lowerbound.CompareTo(other.lowerbound);
            return byLower != 0 ? byLower : this.upperbound.CompareTo(other.upperbound);
        }

        public int CompareTo(object obj)
        {
            if (obj == null)
            {
                return 1;
            }

            DoseRateCalibrationPoint other = obj as DoseRateCalibrationPoint;
            if (other == null)
            {
                throw new ArgumentException("DoseRateCalibrationPoint ожидался", "obj");
            }

            return this.CompareTo(other);
        }

        public double LowerBound
        {
            get
            {
                return this.lowerbound;
            }
            set
            {
                this.lowerbound = value;
            }
        }

        public double UpperBound
        {
            get
            {
                return this.upperbound;
            }
            set
            {
                this.upperbound = value;
            }
        }

        public double CPS
        {
            get
            {
                return this.cps;
            }
            set
            {
                this.cps = value;
                if (this.etalondoseratevalue > 0 && this.cps > 0)
                {
                    this.sensitivity = this.etalondoseratevalue / this.cps;
                }
                else
                {
                    this.sensitivity = 0;
                }
            }
        }

        public double EtalonDoseRateValue
        {
            get
            {
                return this.etalondoseratevalue;
            }
            set
            {
                this.etalondoseratevalue = value;
                if (this.etalondoseratevalue > 0 && this.cps > 0)
                {
                    this.sensitivity = this.etalondoseratevalue / this.cps;
                }
                else
                {
                    this.sensitivity = 0;
                }
            }
        }

        public double Sensitivity
        {
            get
            {
                return this.sensitivity;
            }
        }

        public bool Equals(DoseRateCalibrationPoint point)
        {
            if (this.upperbound == point.UpperBound && this.lowerbound == point.LowerBound && this.cps == point.CPS && this.etalondoseratevalue == point.EtalonDoseRateValue)
            {
                return true;
            }
            return false;
        }

        double lowerbound;
        double upperbound;
        double cps;
        double etalondoseratevalue;
        double sensitivity;
    }
}
