using System;
using System.Globalization;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// ⛔ (`AMBER321`, 11.11.2026) ПРЕСЕТ РАЗРЕШЕНИЯ ПО ВЕЩЕСТВУ КРИСТАЛЛА: FWHM на 662 кэВ, %,
    /// которым заполняется поле «FWHM на 662 кэВ» редактора геометрии, пока числа нет ни от
    /// человека, ни от калибровки прибора. Постановка Amber 07.11.2026, дословно: «На нулевом
    /// этапе (нет ничего) нужно знать разрешение - нужно иметь пресет готовых разрешений из
    /// химии кристалла.» Решение вопросником того же дня, дословно: «Таблица в коде + корпус
    /// (Рекомендую)» — у групп корпуса их измеренные числа, у прочих веществ — паспортные
    /// изготовителей со ссылкой; база `*.sqlite` не меняется; германия здесь нет.
    ///
    /// Число — ТИПИЧНОЕ для вещества, а не паспорт экземпляра: у своего прибора разрешение
    /// приходит из его FWHM-калибровки (кнопка «Из калибровки прибора» и вопрос при открытии
    /// конструктора, <c>EfficiencyMakerForm.OfferFwhmFromCalibration</c>). На разбор FSA число не
    /// действует; оно задаёт допуск пика кривой эффективности для однократного рассеяния
    /// (<see cref="GeometryModel.FwhmAt662Percent"/>).
    ///
    /// ⚠ Вещество ищется по ИМЕНИ (<see cref="GeometryMaterial.Name"/>, как в библиотеке
    /// веществ, без учёта регистра). Своё вещество, заведённое под другим именем, пресета не
    /// получает — честный ноль, а не чужое число.
    ///
    /// Чего в таблице нет и почему:
    ///   * германий (`Germanium`) — вне работы по решению Amber;
    ///   * `Cadmium telluride` — у изготовителя числа на 662 кэВ нет: Amptek XR-100T-CdTe даёт
    ///     разрешение только до 150 кэВ (https://www.amptek.com/internal-products/xr-100cdte-x-ray-and-gamma-ray-detector,
    ///     открыт 11.11.2026);
    ///   * `Gadolinium oxyorthosilicate` — паспорта изготовителя не найдено (Hitachi, Epic,
    ///     Scionix, Luxium, Berkeley Nucleonics — 11.11.2026); есть только статьи, а статьи — не
    ///     паспорт.
    /// </summary>
    public static class CrystalResolutionPresets
    {
        /// <summary>Строка таблицы: вещество, FWHM на 662 кэВ в %, откуда число.</summary>
        public sealed class Entry
        {
            public string Material;
            public double FwhmAt662Percent;
            public string Source;
        }

        static readonly Entry[] Table =
        {
            // ⛔ КАК СЧИТАЕТСЯ ЧИСЛО КОРПУСА (решение Amber 11.11.2026 вопросником, дословно: «7.10 —
            // один прибор один раз (Рекомендую)», правило — ко всем веществам): у каждого ПРИБОРА —
            // одно число, среднее его групп `tools/CORPUS/corpus/detectors.csv` (колонка
            // `fwhm_662_pct`, модель разрешения группы на 662 кэВ); число вещества — медиана приборов.
            //
            // NaI(Tl): прибор Gamma-1S (группы G1S16 6.61 и G1S24 7.34 — один прибор в двух поверках)
            // → 6.975; Atom Spectra Pro 80x80 (AS80x80) 7.22 → медиана (6.975 + 7.22)/2 = 7.0975 ≈ 7.10.
            // Паспорт Luxium (https://luxiumsolutions.com/applications/detection-applications/imaging,
            // открыт 11.11.2026): 7.
            new Entry { Material = "Sodium iodide", FwhmAt662Percent = 7.10, Source = "corpus: NaI(Tl) devices Gamma-1S (G1S16+G1S24), AS80x80 (median, one device once)" },

            // CsI(Tl), восемь приборов: ASN16 6.30, GS4000 6.39, Nano 8 (группы ASN8_1024/2048/3000/
            // 4096/8192: 6.24, 7.04, 6.98, 6.89, 6.74) → 6.778, ASN3 7, AS1PRO 7.13, RC103 7.95, OBS 13,
            // RC101 13.12 → медиана (7 + 7.13)/2 = 7.065 ≈ 7.07. ⚠ ASN3 и OBS — числа Amber 11.11.2026
            // (вопросником, дословно: «Asn3 -7%, OBS -13%»), а не модели `detectors.csv` (3.54 и 4.91):
            // модель ASN3 опровергнута пиками самого спектра (замер `AMBER321`, журнал
            // `handover/handover-2026-11-11-a321.md`), модель OBS проверить нечем. Сам корпус не правится
            // (её же решение: «Не трогать корпус»).
            new Entry { Material = "Cesium iodide", FwhmAt662Percent = 7.07, Source = "corpus: CsI(Tl) devices (median, one device once; ASN3 and OBS - values of Amber 11.11.2026)" },

            // Корпус: группа LABR_BRIL (BrilLanCe 380, 2 спектра) — 2.94. Паспорт Luxium
            // (https://luxiumsolutions.com/radiation-detection-scintillators/crystal-scintillators/lanthanum-bromide-labr3):
            // 2.6 измерено на 2"×2", гарантировано 3.0…3.5.
            new Entry { Material = "Lanthanum bromide", FwhmAt662Percent = 2.94, Source = "corpus: group LABR_BRIL" },

            // Паспорт Luxium, таблица «Eres @ 662 keV (%)»:
            // https://luxiumsolutions.com/applications/detection-applications/imaging (открыт 11.11.2026) — BGO 10.0.
            // ⚠ На странице LYSO того же изготовителя стоит 12.0; Berkeley Nucleonics — «Typically 8–12%».
            new Entry { Material = "Bismuth germanate", FwhmAt662Percent = 10.0, Source = "datasheet: Luxium (Saint-Gobain), Eres @ 662 keV" },

            // Паспорт Scionix: «Typical energy resolutions are 4% FWHM for 662 keV.»
            // https://scionix.nl/?p=75185 (открыт 11.11.2026); Berkeley Nucleonics — тоже 4 %.
            new Entry { Material = "Cerium bromide", FwhmAt662Percent = 4.0, Source = "datasheet: Scionix, CeBr3" },

            // Паспорт Scionix: «Typical energy resolutions are 3.5% @ 662 keV»
            // https://scionix.nl/?p=75185 (открыт 11.11.2026); RMD — «< 4 %» на 25×25 и 38×38 мм.
            new Entry { Material = "Strontium iodide", FwhmAt662Percent = 3.5, Source = "datasheet: Scionix, SrI2(Eu)" },

            // Паспорт Kromek GR1: «Energy resolution: < 2.5% FWHM @ 662 KeV», куб 1 см³.
            // https://www.kromek.com/product/gr-family/ (открыт 11.11.2026). Предел паспорта, а не типичное.
            new Entry { Material = "Cadmium zinc telluride", FwhmAt662Percent = 2.5, Source = "datasheet: Kromek GR1 (upper limit)" },
        };

        /// <summary>Вся таблица — для пробы и для показа.</summary>
        public static Entry[] Items
        {
            get { return (Entry[])Table.Clone(); }
        }

        /// <summary>Строка таблицы для вещества кристалла; null — пресета нет.</summary>
        public static Entry Find(string materialName)
        {
            string name = (materialName ?? "").Trim();
            if (name.Length == 0)
            {
                return null;
            }

            foreach (Entry entry in Table)
            {
                if (string.Equals(entry.Material, name, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        /// <summary>FWHM на 662 кэВ, % для вещества кристалла; 0 — пресета нет.</summary>
        public static double For(GeometryMaterial crystal)
        {
            Entry entry = Find(crystal == null ? null : crystal.Name);
            return entry == null ? 0.0 : entry.FwhmAt662Percent;
        }

        /// <summary>Число для поля — так же, как его ставит кнопка «Из калибровки прибора».</summary>
        public static string FieldText(double percent)
        {
            return percent > 0.0 ? percent.ToString("0.###", CultureInfo.InvariantCulture) : "0";
        }
    }
}
