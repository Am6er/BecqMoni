using System.Collections.Generic;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>Вид компонента в разложении.</summary>
    public enum FsaComponentKind
    {
        /// <summary>Цепочка из нуклидного сета (жёсткая связка интенсивностей).</summary>
        Chain,
        /// <summary>Одиночный нуклид из встроенной таблицы.</summary>
        Single,
        /// <summary>Мешающий образ: рентген, пики вылета. В «пирог» не входит.</summary>
        Nuisance
    }

    /// <summary>Линия компонента: энергия и выход на распад родителя цепочки, %.</summary>
    public sealed class FsaLine
    {
        public FsaLine(string nuclide, double energy, double intensity)
        {
            this.Nuclide = nuclide ?? "";
            this.Energy = energy;
            this.Intensity = intensity;
        }

        public string Nuclide { get; private set; }

        public double Energy { get; private set; }

        public double Intensity { get; private set; }
    }

    /// <summary>
    /// Образ компонента: набор линий, из которых строится «спектр пиков»
    /// (сумма гауссиан единичной площади с весами по выходу и эффективности).
    /// </summary>
    public sealed class FsaComponent
    {
        public FsaComponent(string name, FsaComponentKind kind)
        {
            this.Name = name;
            this.Kind = kind;
            this.Lines = new List<FsaLine>();
        }

        public string Name { get; private set; }

        public FsaComponentKind Kind { get; private set; }

        public List<FsaLine> Lines { get; private set; }

        /// <summary>
        /// Вес линии посчитан целиком, эффективность в него уже входит и второй
        /// раз применяться не должна. Так устроен образ обратного рассеяния:
        /// вес берётся на энергии исходного фотона, а стоит линия на энергии
        /// рассеянного.
        /// </summary>
        public bool WeightsAreFinal { get; set; }

        /// <summary>
        /// Образ выведен из состава предыдущего прохода, а не задан библиотекой:
        /// перед пересборкой такие колонки выбрасываются, иначе они накапливались
        /// бы от прохода к проходу.
        /// </summary>
        public bool Derived { get; set; }
    }
}
