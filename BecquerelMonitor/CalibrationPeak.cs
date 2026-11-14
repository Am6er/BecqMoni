using System;
using System.Collections.Generic;
using System.Xml.Serialization;

namespace BecquerelMonitor
{
    public class CalibrationPeak : IComparable
    {
        int channel;
        double energy;
        double fwhm;
        bool energyLine;
        int count;
        int peak_type = 0;
        double left_tail = 1.0;
        double right_tail = 1.0;
        double voigt_sigma = 1.0;
        double voigt_gamma = 1.0;
        double chi2pndp = -1.0;
        double gaussian_chi2 = -1.0;
        int gaussian_ndp = -1;
        double exp_gauss_exp_chi2 = -1.0;
        int exp_gauss_exp_ndp = -1;
        double exp_gauss_exp_left_tail = 1.0;
        double exp_gauss_exp_right_tail = 1.0;
        double[] exp_gauss_exp_candidate_chi2;
        int[] exp_gauss_exp_candidate_ndp;
        double voigt_chi2 = -1.0;
        int voigt_ndp = -1;
        double voigt_best_sigma = 1.0;
        double voigt_best_gamma = 1.0;
        double[] voigt_candidate_chi2;
        int[] voigt_candidate_ndp;

        public int Channel { get => channel; set => channel = value; }
        public double Energy { get => energy; set => energy = value; }
        public double FWHM { get => fwhm; set => fwhm = value; }

        /// <summary>
        /// ⛔ (`AMBER276`, 29.10.2026) СТРОКА ЕДИНОЙ КАЛИБРОВКИ — ЛИНИЯ ДЛЯ ПОДГОНКИ
        /// ЭНЕРГИИ. Amber, консоль, дословно: «Объединить калибровки по энергии и FWHM в
        /// один процесс на одной вкладке … Таблица калибровки - единая. В файл спектра
        /// помимо калибровки fwhm теперь попадает и калибровка по энергии.»; вопросником
        /// того же дня: «Строка идёт в обе подгонки».
        ///
        /// true — строка заведена ЕДИНОЙ калибровкой этого спектра (щелчок по пику или
        /// «Добавить в калибровку» из таблицы найденных пиков) либо человек ввёл её
        /// энергию: такая строка идёт и в подгонку энергии, и в подгонку FWHM. false —
        /// опора FWHM, пришедшая иначе: из прибора (его кривая FWHM с опорами того
        /// спектра, по которому её сохраняли), из умолчания по трём числам настроек
        /// (каналы 0 и <c>Ch_Fwhm</c>) или из файла прежней версии. Её «энергия» — оценка
        /// по калибровке того времени, а не энергия линии, и в подгонку энергии такая
        /// строка не идёт: две умолчательные опоры прибили бы шкалу к прежнему смещению
        /// на канале 0, а опоры прибора — к каналам ЧУЖОГО спектра.
        ///
        /// ЗАПИСЬ — элементом <c>&lt;EnergyLine&gt;true&lt;/EnergyLine&gt;</c> внутри
        /// <c>&lt;Peak&gt;</c> секции FWHM и только у линии (<see cref="ShouldSerializeEnergyLine"/>);
        /// прежняя версия незнакомый элемент пропускает и читает канал, энергию и FWHM,
        /// как читала. Конфигурация прибора линий не получает: сохранение в прибор
        /// снимает признак (<c>DCFwhmCalibrationView.ForDevice</c>).
        /// </summary>
        public bool EnergyLine { get => energyLine; set => energyLine = value; }

        /// <summary>(`AMBER276`) Писать ли <see cref="EnergyLine"/> — соглашение <c>XmlSerializer</c>: только у линии.</summary>
        public bool ShouldSerializeEnergyLine()
        {
            return energyLine;
        }

        /// <summary>
        /// (`AMBER276`) Отсчёты в канале строки — вес точки в подгонке энергии «с весами»
        /// (прежде — столбец «Вес» таблицы энергии, <c>CalibrationPoint.Count</c>). Пишется
        /// только у линии: опоре FWHM вес не нужен.
        /// </summary>
        public int Count { get => count; set => count = value; }

        /// <summary>(`AMBER276`) Писать ли <see cref="Count"/> — только у линии.</summary>
        public bool ShouldSerializeCount()
        {
            return energyLine;
        }

        double spectrumCentroid = double.NaN;
        int spectrumCentroidChannel = -1;

        /// <summary>
        /// ⛔ (`AMBER270`, решение Amber вопросником 30.10.2026, дословно: «Центр масс пика
        /// (Рекомендую)»; для κ заменено гауссом 14.11.2026, см. ниже) ДРОБНЫЙ ЦЕНТР ПИКА СТРОКИ ПО ЕЁ СОБСТВЕННОМУ СПЕКТРУ — для
        /// подгонки кривизны тракта (<c>DCFwhmCalibrationView.CentroidOf</c>). Нужен строке,
        /// пришедшей «Собрать всё» из ДРУГОГО документа: её пик лежит в чужом спектре, и центр
        /// масс по спектру, где её считают, был бы центром чужого пика. Мерится, когда строку
        /// заводят и когда её собирают (по спектру документа, откуда строка), и действует,
        /// пока канал строки тот же (<see cref="SpectrumCentroidChannel"/>): правка канала его
        /// отменяет. <c>NaN</c> — не измерен.
        ///
        /// ⛔ (`AMBER295`, решения Amber вопросником 14.11.2026, дословно: «Гаусс для κ
        /// (Рекомендую)» и «Писать центр в файл (Рекомендую)») Центр — гаусс на прямой
        /// (<c>DCFwhmCalibrationView.LineCentroid</c>), а не центр масс, и он ПИШЕТСЯ В ФАЙЛ
        /// элементами <c>&lt;Centroid&gt;</c> и <c>&lt;CentroidChannel&gt;</c> внутри
        /// <c>&lt;Peak&gt;</c> — только у линии энергии с измеренным центром
        /// (<see cref="ShouldSerializeSpectrumCentroid"/>). Прежде центр жил только в памяти, и
        /// после повторного открытия файла строка, собранная «Собрать всё» из другого спектра,
        /// и своя строка, заведённая во время набора (отсчёты канала с тех пор выросли), шли в
        /// κ без центра — отбрасывались (находка 4 критика `crit270k`). Старый файл без
        /// элементов читается как прежде (центр не измерен); прежняя версия программы
        /// незнакомые элементы пропускает. Число — инвариантной культурой (`XmlSerializer`).
        /// </summary>
        [XmlElement("Centroid")]
        public double SpectrumCentroid { get => spectrumCentroid; set => spectrumCentroid = value; }

        /// <summary>(`AMBER295`) Писать ли <see cref="SpectrumCentroid"/>: линия энергии с центром, измеренным у её канала.</summary>
        public bool ShouldSerializeSpectrumCentroid()
        {
            return energyLine && !double.IsNaN(spectrumCentroid) && !double.IsInfinity(spectrumCentroid) && spectrumCentroidChannel == channel;
        }

        /// <summary>(`AMBER270`) Канал строки, у которого измерен <see cref="SpectrumCentroid"/>; −1 — не измерен.</summary>
        [XmlElement("CentroidChannel")]
        public int SpectrumCentroidChannel { get => spectrumCentroidChannel; set => spectrumCentroidChannel = value; }

        /// <summary>(`AMBER295`) Писать ли <see cref="SpectrumCentroidChannel"/> — вместе с центром.</summary>
        public bool ShouldSerializeSpectrumCentroidChannel()
        {
            return ShouldSerializeSpectrumCentroid();
        }

        string source;

        /// <summary>
        /// ⛔ (`AMBER295`, решение Amber вопросником 14.11.2026, дословно: «κ только по строкам
        /// спектра (Рекомендую)») ОТКУДА СТРОКА. Пусто — строка своего спектра (заведена щелчком,
        /// «Добавить в калибровку» или собрана «Собрать всё» из него же), даже если спектр после
        /// этого набирался дальше. Иначе — имя документа, из которого её принесло «Собрать всё»:
        /// такая строка идёт в шкалу энергии и FWHM, а в кривизну тракта κ — нет (другая съёмка,
        /// другое усиление). Пишется в файл элементом <c>&lt;Source&gt;</c> только у такой линии;
        /// старый файл без элемента читается как прежде (строка своя).
        /// </summary>
        [XmlElement("Source")]
        public string Source { get => source; set => source = value; }

        /// <summary>(`AMBER295`) Писать ли <see cref="Source"/>: линия энергии другой съёмки.</summary>
        public bool ShouldSerializeSource()
        {
            return energyLine && !string.IsNullOrEmpty(source);
        }

        [XmlIgnore]
        public int PeakType { get => peak_type; set => peak_type = value; }

        [XmlIgnore]
        public double ExpGaussExpLeftTail { get => this.left_tail; set => this.left_tail = value; }

        [XmlIgnore]
        public double ExpGaussExpRightTail { get => this.right_tail; set => this.right_tail = value; }

        [XmlIgnore]
        public double VoigtSigma { get => this.voigt_sigma; set => this.voigt_sigma = value; }

        [XmlIgnore]
        public double VoigtGamma { get => this.voigt_gamma; set => this.voigt_gamma = value; }

        [XmlIgnore]
        public double Chi2pNdp { get => this.chi2pndp; set => this.chi2pndp = value; }

        [XmlIgnore]
        public double GaussianChi2 { get => this.gaussian_chi2; set => this.gaussian_chi2 = value; }

        [XmlIgnore]
        public int GaussianNdp { get => this.gaussian_ndp; set => this.gaussian_ndp = value; }

        [XmlIgnore]
        public double ExpGaussExpChi2 { get => this.exp_gauss_exp_chi2; set => this.exp_gauss_exp_chi2 = value; }

        [XmlIgnore]
        public int ExpGaussExpNdp { get => this.exp_gauss_exp_ndp; set => this.exp_gauss_exp_ndp = value; }

        [XmlIgnore]
        public double ExpGaussExpBestLeftTail { get => this.exp_gauss_exp_left_tail; set => this.exp_gauss_exp_left_tail = value; }

        [XmlIgnore]
        public double ExpGaussExpBestRightTail { get => this.exp_gauss_exp_right_tail; set => this.exp_gauss_exp_right_tail = value; }

        [XmlIgnore]
        public double[] ExpGaussExpCandidateChi2 { get => this.exp_gauss_exp_candidate_chi2; set => this.exp_gauss_exp_candidate_chi2 = value; }

        [XmlIgnore]
        public int[] ExpGaussExpCandidateNdp { get => this.exp_gauss_exp_candidate_ndp; set => this.exp_gauss_exp_candidate_ndp = value; }

        [XmlIgnore]
        public double VoigtChi2 { get => this.voigt_chi2; set => this.voigt_chi2 = value; }

        [XmlIgnore]
        public int VoigtNdp { get => this.voigt_ndp; set => this.voigt_ndp = value; }

        [XmlIgnore]
        public double VoigtBestSigma { get => this.voigt_best_sigma; set => this.voigt_best_sigma = value; }

        [XmlIgnore]
        public double VoigtBestGamma { get => this.voigt_best_gamma; set => this.voigt_best_gamma = value; }

        [XmlIgnore]
        public double[] VoigtCandidateChi2 { get => this.voigt_candidate_chi2; set => this.voigt_candidate_chi2 = value; }

        [XmlIgnore]
        public int[] VoigtCandidateNdp { get => this.voigt_candidate_ndp; set => this.voigt_candidate_ndp = value; }

        public int CompareTo(object obj)
        {
            return channel.CompareTo(((CalibrationPeak)obj).Channel);
        }

        public bool Equals(CalibrationPeak other)
        {
            if (other.fwhm == fwhm && other.channel == channel) return true;
            return false;
        }

        public static List<CalibrationPeak> ClonePeaks(List<CalibrationPeak> pts)
        {
            List<CalibrationPeak> result = new List<CalibrationPeak>();
            foreach (CalibrationPeak point in pts)
            {
                CalibrationPeak p = new CalibrationPeak
                {
                    Channel = point.Channel,
                    Energy = point.Energy,
                    FWHM = point.FWHM,
                    EnergyLine = point.EnergyLine,
                    Count = point.Count,
                    SpectrumCentroid = point.SpectrumCentroid,
                    SpectrumCentroidChannel = point.SpectrumCentroidChannel,
                    Source = point.Source,
                    PeakType = point.PeakType,
                    ExpGaussExpLeftTail = point.ExpGaussExpLeftTail,
                    ExpGaussExpRightTail = point.ExpGaussExpRightTail,
                    VoigtSigma = point.VoigtSigma,
                    VoigtGamma = point.VoigtGamma,
                    Chi2pNdp = point.Chi2pNdp,
                    GaussianChi2 = point.GaussianChi2,
                    GaussianNdp = point.GaussianNdp,
                    ExpGaussExpChi2 = point.ExpGaussExpChi2,
                    ExpGaussExpNdp = point.ExpGaussExpNdp,
                    ExpGaussExpBestLeftTail = point.ExpGaussExpBestLeftTail,
                    ExpGaussExpBestRightTail = point.ExpGaussExpBestRightTail,
                    ExpGaussExpCandidateChi2 = point.ExpGaussExpCandidateChi2 == null ? null : (double[])point.ExpGaussExpCandidateChi2.Clone(),
                    ExpGaussExpCandidateNdp = point.ExpGaussExpCandidateNdp == null ? null : (int[])point.ExpGaussExpCandidateNdp.Clone(),
                    VoigtChi2 = point.VoigtChi2,
                    VoigtNdp = point.VoigtNdp,
                    VoigtBestSigma = point.VoigtBestSigma,
                    VoigtBestGamma = point.VoigtBestGamma,
                    VoigtCandidateChi2 = point.VoigtCandidateChi2 == null ? null : (double[])point.VoigtCandidateChi2.Clone(),
                    VoigtCandidateNdp = point.VoigtCandidateNdp == null ? null : (int[])point.VoigtCandidateNdp.Clone()
                };
                result.Add(p);
            }
            return result;
        }
    }
}
