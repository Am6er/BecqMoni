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
