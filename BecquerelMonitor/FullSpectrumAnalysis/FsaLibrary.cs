using System;
using System.Collections.Generic;
using System.Linq;

namespace BecquerelMonitor.FullSpectrumAnalysis
{
    /// <summary>
    /// Сборка библиотеки образов для полноспектральной декомпозиции.
    ///
    /// Состав задаёт поиск пиков: какими нуклидами он подписал спектр, те и
    /// раскладываются. Фиксированный список брать неоткуда, а лишний компонент
    /// в нём немедленно становится фантомом — он ведь свободен и обязательно
    /// что-нибудь себе заберёт.
    ///
    /// Линии компонента берутся из базы нуклидов по имени, веса — из
    /// NuclideDefinition.Intencity. Если у нуклида в базе нет интенсивностей
    /// (характеристический рентген, да и вся поставочная база), берётся
    /// встроенный образ. Пики вылета добавляются всегда: они не принадлежат
    /// нуклиду, финдер их подписать не может, а без образа NNLS вешает их на
    /// ближайшую линию.
    /// </summary>
    public static class FsaLibrary
    {
        /// <summary>
        /// Образы, которые в набор не приходят из поиска пиков: пики вылета не
        /// принадлежат ни одному нуклиду, финдер подписать их не может, а без
        /// образа NNLS вешает их на ближайшую линию.
        /// </summary>
        static readonly string[] AlwaysPresent = { "SE-2614", "DE-2614" };

        /// <summary>
        /// Нуклиды, у которых в базе есть линии, но нет интенсивностей
        /// (характеристический рентген): подписать пик база позволяет, а
        /// построить по ней образ — нет, поэтому берётся встроенный.
        /// </summary>
        static readonly Dictionary<string, string> BuiltinSubstitutes =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "W", "Xray-W" },
                { "X-ray", "Xray-Pb" }
            };

        /// <summary>
        /// Библиотека по найденным пикам: distinct по нуклидам, которыми поиск
        /// пиков подписал спектр. Это и есть состав, который надо разложить —
        /// брать фиксированный список неоткуда, а лишние компоненты в нём
        /// становятся фантомами. Побочно получается разрез цепочки: финдер
        /// подписывает пики дочерними (Ac-228, Pb-212, Tl-208), и каждый
        /// дочерний входит в модель своим образом со свободной амплитудой —
        /// жёсткая связка интенсивностей внутри цепочки не навязывается.
        /// </summary>
        public static List<FsaComponent> BuildFromPeaks(
            IEnumerable<Peak> peaks,
            IEnumerable<NuclideDefinition> nuclideDefinitions)
        {
            List<FsaComponent> result = new List<FsaComponent>();
            if (peaks == null || nuclideDefinitions == null)
            {
                return result;
            }

            List<NuclideDefinition> definitions = nuclideDefinitions
                .Where(n => n != null && n.Visible && n.Energy > 0.0)
                .ToList();

            // Порядок сохраняем по первому появлению: так состав читается в
            // том же порядке, в каком пики идут по спектру.
            List<string> order = new List<string>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Peak peak in peaks)
            {
                if (peak == null || peak.Nuclide == null)
                {
                    // «(unknown)»: подписать нечем, образа тоже нет
                    continue;
                }

                string nuclide = NuclideToken(peak.Nuclide.Name);
                if (nuclide.Length > 0 && seen.Add(nuclide))
                {
                    order.Add(nuclide);
                }
            }

            Dictionary<string, FsaComponent> builtin = BuiltinSingles();
            HashSet<string> taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string nuclide in order)
            {
                FsaComponent component = new FsaComponent(nuclide, FsaComponentKind.Single);
                foreach (NuclideDefinition definition in definitions)
                {
                    if (definition.Intencity <= 0.0
                        || !string.Equals(NuclideToken(definition.Name), nuclide, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    component.Lines.Add(new FsaLine(nuclide, definition.Energy, definition.Intencity));
                }

                if (component.Lines.Count > 0)
                {
                    if (taken.Add(component.Name))
                    {
                        result.Add(component);
                    }

                    continue;
                }

                // Линий с интенсивностями в базе нет — берём встроенный образ:
                // сначала по имени самого нуклида (в поставочной базе выходы не
                // заполнены вовсе), затем по подстановке для рентгена.
                FsaComponent replacement;
                string substitute;
                if (!builtin.TryGetValue(nuclide, out replacement)
                    && BuiltinSubstitutes.TryGetValue(nuclide, out substitute))
                {
                    builtin.TryGetValue(substitute, out replacement);
                }

                if (replacement != null && taken.Add(replacement.Name))
                {
                    result.Add(replacement);
                }
            }

            foreach (string name in AlwaysPresent)
            {
                FsaComponent component;
                if (builtin.TryGetValue(name, out component) && taken.Add(name))
                {
                    result.Add(component);
                }
            }

            return result;
        }

        /// <summary>Имя нуклида без хвоста вида «(Th-232)».</summary>
        static string NuclideToken(string name)
        {
            return NuclideDefinition.NuclideNameOf(name);
        }

        static Dictionary<string, FsaComponent> BuiltinSingles()
        {
            Dictionary<string, FsaComponent> table = new Dictionary<string, FsaComponent>(StringComparer.OrdinalIgnoreCase);
            Action<string, FsaComponentKind, double[,]> add = (name, kind, lines) =>
            {
                FsaComponent component = new FsaComponent(name, kind);
                for (int i = 0; i < lines.GetLength(0); i++)
                {
                    component.Lines.Add(new FsaLine(name, lines[i, 0], lines[i, 1]));
                }

                table[name] = component;
            };

            add("K-40", FsaComponentKind.Single, new double[,] { { 1460.822, 10.66 } });
            add("Cs-137", FsaComponentKind.Single, new double[,] { { 661.657, 85.10 } });
            add("Am-241", FsaComponentKind.Single, new double[,] { { 59.541, 35.92 }, { 26.345, 2.31 } });
            add("Co-60", FsaComponentKind.Single, new double[,] { { 1173.228, 99.85 }, { 1332.492, 99.9826 } });
            add("I-131", FsaComponentKind.Single, new double[,] {
                { 364.489, 81.5 }, { 636.989, 7.16 }, { 284.305, 6.12 },
                { 80.185, 2.62 }, { 722.911, 1.77 } });
            add("Eu-152", FsaComponentKind.Single, new double[,] {
                { 121.782, 28.53 }, { 244.697, 7.55 }, { 344.279, 26.59 }, { 411.116, 2.24 },
                { 443.965, 2.80 }, { 778.904, 12.93 }, { 867.380, 4.23 }, { 964.079, 14.51 },
                { 1085.837, 10.11 }, { 1089.737, 1.73 }, { 1112.076, 13.67 }, { 1212.948, 1.42 },
                { 1299.142, 1.62 }, { 1408.013, 20.87 } });
            add("Ba-133", FsaComponentKind.Single, new double[,] {
                { 80.998, 34.06 }, { 79.614, 2.65 }, { 276.399, 7.16 },
                { 302.851, 18.34 }, { 356.013, 62.05 }, { 383.848, 8.94 } });
            add("Lu-176", FsaComponentKind.Single, new double[,] {
                { 88.34, 14.5 }, { 201.83, 78.0 }, { 306.78, 93.6 } });

            // Характеристический рентген — не нуклиды, а мешающие образы:
            // флуоресценция вольфрама (ториевые WT-электроды) и свинца (домик).
            // Без них NNLS вешает пик 58-59 кэВ на Am-241 (59.5 кэВ).
            add("Xray-W", FsaComponentKind.Nuisance, new double[,] {
                { 59.318, 100.0 }, { 57.981, 57.6 }, { 67.244, 22.0 }, { 69.067, 8.0 } });
            add("Xray-Pb", FsaComponentKind.Nuisance, new double[,] {
                { 74.969, 100.0 }, { 72.804, 59.5 }, { 84.936, 23.0 }, { 87.300, 8.0 } });

            // Пики вылета от 2614.5 кэВ (Tl-208): одиночный (-511) и двойной
            // (-1022). Образы генерируются только для пиков полного поглощения,
            // а доли вылета зависят от кристалла — поэтому отдельные образы со
            // свободной амплитудой, а не строки внутри ториевого образа.
            add("SE-2614", FsaComponentKind.Nuisance, new double[,] { { 2103.5, 100.0 } });
            add("DE-2614", FsaComponentKind.Nuisance, new double[,] { { 1592.5, 100.0 } });

            return table;
        }
    }
}
