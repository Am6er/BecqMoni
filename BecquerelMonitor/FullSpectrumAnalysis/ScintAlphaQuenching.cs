using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// (`AMBER274` Э3, полоса `a274n`, 14.11.2026) ГАШЕНИЕ АЛЬФ В СЦИНТИЛЛЯТОРЕ — таблица
    /// `matdb.scint_alpha_quenching`: свет альфы с энергией E — как у электрона энергии E·QF(E).
    /// Решение Amber 14.11.2026 вопросником, дословно: «В matdb по замеру (Рекомендую)».
    ///
    /// Строка таблицы — ЗАКОН, а не точки: (вещество, источник) → QF на опорной энергии, наклон на МэВ,
    /// QF(E) = QF(E_ref) + наклон·(E − E_ref)/1000 (та же форма, что у рычагов
    /// <see cref="CrystalOwnParts.AlphaQuenching"/> / <see cref="CrystalOwnParts.AlphaQuenchingSlopePerMev"/>
    /// при E_ref = <see cref="CrystalOwnActivityBuilder.AlphaQuenchingReferenceKev"/>), и ОХВАТ замера — энергии,
    /// на которых закон держится данными (вне охвата — продолжение закона, <see cref="Law.Measured"/> = false).
    ///
    /// ⛔ Читатель лежит ВНЕ `EfficiencyMaker/` нарочно: таблицу не читает перенос, и в отпечаток склада
    /// (`MaterialDatabase.SimulatorTables`, `mdb=` клейма матриц и кривых) она не входит — как фотонные кривые
    /// света FSA (`AMBER272`). Сторож `tools/check_matdb_fingerprint.py` потребовал бы внести в список всякую
    /// таблицу, имя которой стоит в коде `EfficiencyMaker/*.cs`, и внесение объявило бы негодным весь склад
    /// без единого изменения физики. QF входит в клеймо РАЗДЕЛА слоя значением (`RecipeStamp`).
    ///
    /// ⚠ Без названного источника берётся только строка `is_default = 1` («по веществу»). Строка замера
    /// `a274a_bg_vertices` (LaBr3:Ce) лежит с `is_default = 0`: измерена на одном кристалле, и выбор её для
    /// всякого LaBr3 — решение Amber (у `LABR_BRIL` — «не включать»). Нет таблицы (база до 14.11.2026), нет
    /// строки — null, альф нет, как прежде. Прочий отказ чтения — null и строка в
    /// <see cref="FsaDatabaseFailures"/>.
    /// </summary>
    public static class ScintAlphaQuenching
    {
        public const string Table = "scint_alpha_quenching";

        /// <summary>Одна строка таблицы — закон QF(E) вещества по источнику.</summary>
        public sealed class Law
        {
            public string Material;
            public string Source;
            public bool IsDefault;
            public double ReferenceKev;
            public double QfAtReference;
            public double SlopePerMev;
            public double MeasuredMinKev;
            public double MeasuredMaxKev;
            public string Origin;
            public string Note;

            /// <summary>QF при энергии альфы <paramref name="alphaKev"/> (кэВ).</summary>
            public double At(double alphaKev)
            {
                return this.QfAtReference + this.SlopePerMev * (alphaKev - this.ReferenceKev) / 1000.0;
            }

            /// <summary>Энергия внутри охвата замера (иначе — продолжение закона).</summary>
            public bool Measured(double alphaKev)
            {
                return alphaKev >= this.MeasuredMinKev && alphaKev <= this.MeasuredMaxKev;
            }

            /// <summary>
            /// QF на опорной энергии рычага слоя (<see cref="CrystalOwnActivityBuilder.AlphaQuenchingReferenceKev"/>) —
            /// то, что кладётся в <see cref="CrystalOwnParts.AlphaQuenching"/>; наклон — как есть.
            /// </summary>
            public double QfAtBuilderReference
            {
                get { return this.At(CrystalOwnActivityBuilder.AlphaQuenchingReferenceKev); }
            }
        }

        static readonly object Gate = new object();
        static List<Law> rows;
        static string rowsPath;
        static string overridePath;

        /// <summary>
        /// Закон вещества <paramref name="material"/> (имя как в `scint_npsm_params`: `LaBr3:Ce`,
        /// <see cref="EfficiencyMaker.EfficiencySimulator.ScintillatorNameOf(EfficiencyMaker.GeometryModel)"/>) по
        /// источнику <paramref name="source"/>; источник null или пуст — строка `is_default = 1`. null — строки нет.
        /// </summary>
        public static Law Find(string material, string source)
        {
            foreach (Law law in All())
            {
                if (!string.Equals(law.Material, material ?? "", StringComparison.Ordinal))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(source) ? law.IsDefault : string.Equals(law.Source, source, StringComparison.Ordinal))
                {
                    return law;
                }
            }

            return null;
        }

        /// <summary>Все строки таблицы (пусто — таблицы нет или отказ). Кэш на процесс и файл.</summary>
        public static IList<Law> All()
        {
            string path = overridePath ?? EfficiencyMaker.MaterialDatabase.DatabasePath();
            lock (Gate)
            {
                if (rows != null && string.Equals(rowsPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    return rows.AsReadOnly();
                }

                List<Law> list;
                try
                {
                    list = Read(path);
                }
                catch (Exception ex)
                {
                    FsaDatabaseFailures.Note("matdb.sqlite", "гашение альф " + Table, ex);
                    list = new List<Law>();
                }

                rows = list;
                rowsPath = path;
                return rows.AsReadOnly();
            }
        }

        /// <summary>Только для проб: читать другой файл базы (положительный контроль); null — файл рядом с программой.</summary>
        public static void Reload(string path)
        {
            lock (Gate)
            {
                overridePath = path;
                rows = null;
                rowsPath = null;
            }
        }

        static List<Law> Read(string path)
        {
            var list = new List<Law>();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("matdb.sqlite не найдена рядом с программой: " + path, path);
            }

            using (var connection = new SqliteConnection(EfficiencyMaker.MaterialDatabase.ReadOnlyConnection(path, true)))
            {
                connection.Open();
                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select count(*) from sqlite_master where type = 'table' and name = $n";
                    command.Parameters.AddWithValue("$n", Table);
                    if (Convert.ToInt64(command.ExecuteScalar(), CultureInfo.InvariantCulture) == 0)
                    {
                        return list;                       // база до 14.11.2026: таблицы нет — альф нет
                    }
                }

                using (SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "select material, source, is_default, ref_kev, qf_ref, slope_per_mev,"
                                          + " measured_min_kev, measured_max_kev, origin, note from " + Table
                                          + " order by material, source";
                    using (SqliteDataReader reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var law = new Law
                            {
                                Material = reader.GetString(0),
                                Source = reader.GetString(1),
                                IsDefault = reader.GetInt64(2) != 0,
                                ReferenceKev = reader.GetDouble(3),
                                QfAtReference = reader.GetDouble(4),
                                SlopePerMev = reader.GetDouble(5),
                                MeasuredMinKev = reader.GetDouble(6),
                                MeasuredMaxKev = reader.GetDouble(7),
                                Origin = reader.GetString(8),
                                Note = reader.GetString(9),
                            };
                            if (!(law.QfAtReference > 0.0 && law.QfAtReference < 1.0) || !(law.ReferenceKev > 0.0)
                                || double.IsNaN(law.SlopePerMev) || double.IsInfinity(law.SlopePerMev))
                            {
                                throw new InvalidDataException(
                                    Table + ": строка «" + law.Material + " / " + law.Source + "» вне смысла — QF "
                                    + law.QfAtReference.ToString("R", CultureInfo.InvariantCulture) + " на "
                                    + law.ReferenceKev.ToString("R", CultureInfo.InvariantCulture) + " кэВ");
                            }

                            list.Add(law);
                        }
                    }
                }
            }

            return list;
        }
    }
}
