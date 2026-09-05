using System.Collections.Generic;

namespace BecquerelMonitor
{
    public enum PeakSearchOrigin
    {
        FWHMPeakFinder
    }

    public class Peak
    {
        public double Energy
        {
            get
            {
                return this.energy;
            }
            set
            {
                this.energy = value;
            }
        }

        public double SNR
        {
            get
            {
                return this.snr;
            }
            set
            {
                this.snr = value;
            }
        }

        public double FWHM
        {
            get
            {
                return this.fwhm;
            }
            set
            {
                this.fwhm = value;
            }
        }

        public double FWHM_DELTA
        {
            get
            {
                return this.fwhm_delta;
            }
            set
            {
                this.fwhm_delta = value;
            }
        }

        public int Channel
        {
            get
            {
                return this.channel;
            }
            set
            {
                this.channel = value;
            }
        }

        /// <summary>
        /// ПОБЕДИТЕЛЬ — первое имя списка кандидатов
        /// (<see cref="NuclideCandidates"/>), и всё, что считает по подписи
        /// числа, читает именно его: активность (`S96`), состав разбора
        /// (`FsaCompositionInference`), цвет пика.
        ///
        /// ⛔ Присвоение сюда СБРАСЫВАЕТ список кандидатов, и это не оплошность.
        /// Подпись со стороны — приборный образ поверх нуклидной (`A196`),
        /// снятие неподтверждённой (`A197`), развод неразрешимых близнецов
        /// (<c>PeakDetector.isNewPeak</c>) — объявляет НОВОГО победителя, а
        /// прежние соперники были соперниками ПРЕЖНЕМУ и к новому отношения не
        /// имеют. Список ставится одним движением с победителем —
        /// <see cref="SetNuclideCandidates"/>.
        /// </summary>
        public NuclideDefinition Nuclide
        {
            get
            {
                return this.nuclide;
            }
            set
            {
                this.nuclide = value;
                this.nuclideCandidates = null;
            }
        }

        /// <summary>
        /// (`S64`, решение Amber 05.09.2026) КТО ЕЩЁ МОГ БЫТЬ ЭТИМ ПИКОМ —
        /// список кандидатов, первый в котором победитель.
        ///
        /// Зачем список вообще: две линии разных родителей внутри одного пика
        /// НЕРАЗЛИЧИМЫ ПО ПОЛОЖЕНИЮ при разрешении прибора (`Xray-W` 59.318 и
        /// `Am-241` 59.541 при ПШПВ 10…22 кэВ), и подпись одним именем — это
        /// утверждение, которого измерение не содержит. Показ обоих имён —
        /// способ не врать, не теряя истины: победитель остаётся первым, и
        /// всякий счёт по подписи идёт по нему одному.
        ///
        /// ⛔ Список — это ЗАПИСИ БИБЛИОТЕКИ, а не готовая строка: строку
        /// собирает показ (<c>PeakDetector.PeakLabel</c>), и разделитель у неё
        /// из ресурсов приложения. Класть склеенное имя в
        /// <see cref="Nuclide"/> нельзя — по нему считают выход, ряд и лексему
        /// нуклида.
        ///
        /// Пустой список значит «подписи нет»; список из одного — «спор не с
        /// кем», и на экране всё как прежде.
        /// </summary>
        public IList<NuclideDefinition> NuclideCandidates
        {
            get
            {
                if (this.nuclideCandidates != null)
                {
                    return this.nuclideCandidates;
                }
                var single = new List<NuclideDefinition>();
                if (this.nuclide != null)
                {
                    single.Add(this.nuclide);
                }
                return single;
            }
        }

        /// <summary>
        /// Поставить победителя и его соперников ОДНИМ движением: первый
        /// элемент становится <see cref="Nuclide"/>. Пустой список (или null)
        /// снимает подпись целиком.
        /// </summary>
        public void SetNuclideCandidates(List<NuclideDefinition> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                this.Nuclide = null;
                return;
            }
            this.Nuclide = candidates[0];
            this.nuclideCandidates = candidates;
        }

        public int Count
        {
            get
            {
                return this.count;
            }
            set
            {
                this.count = value;
            }
        }

        public int LeftChannel
        {
            get
            {
                return this.leftChannel;
            }
            set
            {
                this.leftChannel = value;
            }
        }

        public int RightChannel
        {
            get
            {
                return this.rightChannel;
            }
            set
            {
                this.rightChannel = value;
            }
        }

        public PeakSearchOrigin PeakSearchOrigin
        {
            get
            {
                return this.peakSearchOrigin;
            }
            set
            {
                this.peakSearchOrigin = value;
            }
        }

        double energy;

        int channel;

        NuclideDefinition nuclide;

        // null значит «соперников не искали»; тогда список строится из одного
        // победителя, и старый путь подписи виден как прежде.
        List<NuclideDefinition> nuclideCandidates;

        int count;

        int leftChannel;

        int rightChannel;

        double snr;

        double fwhm;

        double fwhm_delta;

        PeakSearchOrigin peakSearchOrigin;
    }
}
