using System;

namespace BecquerelMonitor
{
    public class NuclideSet
    {
        public Guid Id
        {
            get
            {
                return this.id;
            }
            set
            {
                this.id = value;
            }
        }

        public string Name
        {
            get
            {
                return this.name;
            }
            set
            {
                this.name = value;
            }
        }

        public bool HideUnknownPeaks
        {
            get
            {
                return this.hideUnknownPeaks;
            }
            set
            {
                this.hideUnknownPeaks = value;
            }
        }

        /// <summary>
        /// Рисовать ли линии этого набора поверх спектра: энергия даёт
        /// положение, выход — высоту, цвет нуклида — цвет.
        ///
        /// Признак у НАБОРА, а не общая настройка: набор выбирают в панели
        /// поиска пиков, и без этой галки линии включались бы вместе с поиском
        /// по набору — выключить их, не сменив набор, было бы нечем.
        ///
        /// По умолчанию выключено, в том числе у наборов, заведённых до этой
        /// галки: частокол в тридцать линий поверх спектра — это то, что просят
        /// показать, а не то, что показывают сами.
        /// </summary>
        public bool ShowIntensityLines
        {
            get
            {
                return this.showIntensityLines;
            }
            set
            {
                this.showIntensityLines = value;
            }
        }

        Guid id = Guid.Empty;
        string name = "";
        bool hideUnknownPeaks = false;
        bool showIntensityLines = false;
    }
}
