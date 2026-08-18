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

        /// <summary>
        /// Готовый образ по каналам, если он не строится из линий вовсе.
        /// Заведено для случайных наложений (pile-up): их форма — автосвёртка
        /// САМОГО спектра, линий у неё нет.
        ///
        /// Такой образ НЕ двигается сеткой дрейфа, и это не упущение: он выведен
        /// из измеренного спектра и уже стоит в его шкале. Двигать его вслед за
        /// библиотечными образами значило бы сдвинуть спектр относительно
        /// самого себя.
        /// </summary>
        public double[] FixedTemplate { get; set; }

        /// <summary>
        /// Суммарный выход ВСЕХ излучений нуклида (γ и X) на его СОБСТВЕННЫЙ
        /// распад, % (`S69`). NaN — неизвестен, и тогда правило отсева не
        /// применяется вовсе.
        ///
        /// ⛔ Это АПРИОРНОЕ свойство нуклида, а не образа. Считать его по
        /// <see cref="Lines"/> нельзя по двум причинам сразу: у члена ряда веса
        /// линий домножены на долю ветвления от корня, а из образа выброшено
        /// всё, что не попало в рабочее окно прибора, — тогда как случай
        /// «линии есть и выход приличный, но они ВНЕ шкалы прибора» (Ra-228,
        /// 13.52 и 16.2 кэВ) обязан отличаться от «излучения нет вовсе»
        /// (Po-212) и от «выход пренебрежим» (Po-216, 0.0019 %).
        ///
        /// ⚠ Заполняется только сборкой из баз
        /// (<see cref="FsaSampleLibrary"/>): у <see cref="FsaLibrary"/> линии
        /// приходят из пользовательского конфига и встроенной таблицы, их сумма
        /// — итог КОНФИГА, а не нуклида, и выдавать её за априорное свойство
        /// значило бы соврать тихо.
        /// </summary>
        public double TotalYieldPercent { get; set; } = double.NaN;
    }
}
