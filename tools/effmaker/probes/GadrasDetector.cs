using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace GadrasShared
{
    /// <summary>
    /// Типовой детектор GADRAS из поставки InterSpec: параметры сцены плюс
    /// эталонные колонки отклика. Общий код двух проб — `GadrasProbe` (сверка
    /// эффективности в пике) и `ResponseProbe` (сверка комптона и вылетов), —
    /// поэтому у файла нет `Main` и он подкладывается в сборку обеих.
    ///
    /// Разбор поставки и оговорки о том, чем эти числа НЕ являются, — в
    /// `tools/interspec/README.md` и `tools/effmaker/probes/README.md`.
    /// </summary>
    public sealed class GadrasDetector
    {
        public string Name;
        public string CrystalName;
        public double CrystalLengthCm;    // строка 10 Detector.dat: det. length
        public double CrystalWidthCm;     // строка 11: det. width — это ДИАМЕТР
        public double WindowZ;            // строка 14: attenuator Z (бывает дробным)
        public double WindowArealDensity; // строка 15: attenuator g/cm2
        public double DistanceCm;         // строка 17: distance
        public double SetbackCm;          // строка 40: det setback

        /// <summary>Германий вне предмета — см. `tools/effmaker/handover-2026-08-05.md`, §1.</summary>
        public bool IsGermanium
        {
            get { return string.Equals(this.CrystalName, "HPGe", StringComparison.OrdinalIgnoreCase); }
        }

        /// <summary>Доля телесного угла диска кристалла с оси источника.</summary>
        public double SolidAngleFraction
        {
            get
            {
                double r = 0.5 * this.CrystalWidthCm;
                double d = this.DistanceCm + this.SetbackCm;
                return 0.5 * (1.0 - d / Math.Sqrt(d * d + r * r));
            }
        }

        readonly List<double> energies = new List<double>();
        readonly List<double[]> columns = new List<double[]>();

        /// <summary>Колонки `Efficiency.csv` после энергии, в процентах.</summary>
        public enum Column
        {
            Peak = 0,
            Compton = 1,
            Compton1 = 2,
            SingleEscape = 3,
            DoubleEscape = 4,
            Total = 5
        }

        public static GadrasDetector Read(string dir, string name)
        {
            var d = new GadrasDetector { Name = name };

            // Detector.dat: «номер значение флаг подпись», фиксированной ширины.
            // Разбираем по номеру строки — подписи в разных файлах обрезаны
            // по-разному.
            foreach (string line in File.ReadAllLines(Path.Combine(dir, "Detector.dat")))
            {
                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int idx;
                double val;
                if (parts.Length < 2
                    || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out idx)
                    || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out val))
                {
                    continue;
                }

                switch (idx)
                {
                    case 10: d.CrystalLengthCm = val; break;
                    case 11: d.CrystalWidthCm = val; break;
                    case 14: d.WindowZ = val; break;
                    case 15: d.WindowArealDensity = val; break;
                    case 17: d.DistanceCm = val; break;
                    case 40: d.SetbackCm = val; break;
                }
            }

            // Efficiency.csv: две строки шапки, дальше энергия и шесть колонок.
            string[] rows = File.ReadAllLines(Path.Combine(dir, "Efficiency.csv"));
            for (int i = 2; i < rows.Length; i++)
            {
                string[] cells = rows[i].Split(',');
                double e;
                if (cells.Length < 7
                    || !double.TryParse(cells[0], NumberStyles.Float, CultureInfo.InvariantCulture, out e))
                {
                    continue;
                }

                double[] values = new double[6];
                bool ok = true;
                for (int c = 0; c < 6 && ok; c++)
                {
                    ok = double.TryParse(cells[c + 1], NumberStyles.Float,
                                         CultureInfo.InvariantCulture, out values[c]);
                }

                if (!ok)
                {
                    continue;
                }

                d.energies.Add(e);
                d.columns.Add(values);
            }

            if (d.energies.Count == 0)
            {
                throw new InvalidDataException("пустая Efficiency.csv");
            }

            d.CrystalName = CrystalOf(name);
            return d;
        }

        /// <summary>Эталон в узле сетки: линейная вставка по логарифму энергии.</summary>
        public double Reference(Column column, double energyKev)
        {
            if (energyKev < this.energies[0] || energyKev > this.energies[this.energies.Count - 1])
            {
                return double.NaN;
            }

            for (int i = 1; i < this.energies.Count; i++)
            {
                if (energyKev > this.energies[i])
                {
                    continue;
                }

                double t = (Math.Log(energyKev) - Math.Log(this.energies[i - 1]))
                         / (Math.Log(this.energies[i]) - Math.Log(this.energies[i - 1]));
                double lo = this.columns[i - 1][(int)column];
                return lo + t * (this.columns[i][(int)column] - lo);
            }

            return this.columns[this.columns.Count - 1][(int)column];
        }

        static string CrystalOf(string name)
        {
            if (name.StartsWith("NaI", StringComparison.OrdinalIgnoreCase)) return "NaI";
            if (name.StartsWith("LaBr", StringComparison.OrdinalIgnoreCase)) return "LaBr3";
            if (name.StartsWith("HPGe", StringComparison.OrdinalIgnoreCase)) return "HPGe";
            throw new InvalidDataException("неизвестный кристалл в имени «" + name + "»");
        }

        /// <summary>
        /// Сцена: точечный источник на оси, цилиндр кристалла, входное окно по
        /// поверхностной плотности. Модель — в МИЛЛИМЕТРАХ.
        /// </summary>
        public GeometryModel ToModel()
        {
            var m = new GeometryModel
            {
                Name = "GADRAS " + this.Name,
                // Сплошной цилиндр всегда: коаксиальную ветвь модель не
                // разбирает и не будет (германий вне предмета).
                IsScintillator = true,
                SourceType = GeometrySourceType.Point,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = this.CrystalWidthCm * GeometryModel.MmPerCm,
                CrystalHeight = this.CrystalLengthCm * GeometryModel.MmPerCm,
                PointDistance = (this.DistanceCm + this.SetbackCm) * GeometryModel.MmPerCm
            };

            // ByName ищет по ПОЛНОМУ имени («Sodium iodide»), а у нас сокращение.
            GeometryMaterialLibrary.Entry crystal = null;
            foreach (GeometryMaterialLibrary.Entry entry in
                     GeometryMaterialLibrary.Of(GeometryMaterialLibrary.MaterialKind.Crystal))
            {
                if (string.Equals(entry.Abbr, this.CrystalName, StringComparison.OrdinalIgnoreCase))
                {
                    crystal = entry;
                    break;
                }
            }

            if (crystal == null)
            {
                throw new InvalidDataException("нет вещества «" + this.CrystalName + "» в библиотеке");
            }

            m.Crystal = GeometryMaterialLibrary.Make(crystal, crystal.Density);

            // Окно задано эффективным Z и поверхностной плотностью; целого
            // вещества за дробным Z нет. Плотность условная: ослабление зависит
            // только от произведения ρ·t, а оно и есть заданная поверхностная
            // плотность.
            int z = (int)Math.Round(this.WindowZ);
            if (z > 0 && this.WindowArealDensity > 0.0)
            {
                const double windowDensity = 2.7;
                var win = new GeometryMaterial
                {
                    Name = GeometryMaterialLibrary.SymbolOf(z) ?? ("Z" + z),
                    Density = windowDensity
                };
                win.Fractions[z] = 1.0;
                m.Cladding = win;
                m.FrontCladdingThickness =
                    this.WindowArealDensity / windowDensity * GeometryModel.MmPerCm;
            }

            return m;
        }
    }
}
