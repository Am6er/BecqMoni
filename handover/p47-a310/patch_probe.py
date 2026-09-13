# -*- coding: utf-8 -*-
"""П47 13.09.2026 (A310) — ключ `--weights=data|model` у CorpusFsaProbe (Options.Weights, разбор, применение к
анализатору, строка шапки) и у FsaP42ArmProbe (хук ProbeSetup для сцены Amber). Однократно."""
import io

NL = '\r\n'


def patch(p, pairs):
    s = io.open(p, encoding='utf-8', newline='').read()
    for old, new in pairs:
        old = old.replace('\n', NL)
        new = new.replace('\n', NL)
        assert s.count(old) == 1, (p, s.count(old), old[:80])
        s = s.replace(old, new)
    io.open(p, 'w', encoding='utf-8', newline='').write(s)


patch('tools/effmaker/probes/CorpusFsaProbe.cs', [
    # шапка ключей
    ("""    ///                  [--limits-mc=N [--mc-component=Имя]] [--huber=M] [--refit-z=Z]
""",
     """    ///                  [--limits-mc=N [--mc-component=Имя]] [--huber=M] [--refit-z=Z]
    ///                  [--weights=data|model]   (`A310`, П47: веса решателя по данным 1/max(N,1) или по модели, Пирсон)
"""),
    # Options
    ("""            public int LossJoint = -1;           // (S166, П18) вынос из пика с κ: 1 вкл, 0 выкл; -1 — умолчание анализатора
""",
     """            public int LossJoint = -1;           // (S166, П18) вынос из пика с κ: 1 вкл, 0 выкл; -1 — умолчание анализатора
            public string Weights = null;        // (A310, П47) веса решателя: "data" | "model"; null — умолчание анализатора
"""),
    # разбор
    ("""                else if (a.StartsWith("--huber=", StringComparison.Ordinal))
                {
                    o.HuberM = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
""",
     """                else if (a.StartsWith("--huber=", StringComparison.Ordinal))
                {
                    o.HuberM = double.Parse(a.Substring(8), CultureInfo.InvariantCulture);
                }
                else if (a.StartsWith("--weights=", StringComparison.Ordinal))
                {
                    // (`A310`, П47 13.09.2026) Веса решателя: `data` — по
                    // отсчёту, `model` — по модели (Пирсон). Уходит в
                    // `FsaAnalyzer.ModelWeights`; читатель — `SETUP`
                    // отражением.
                    string v = a.Substring(10);
                    if (v != "data" && v != "model")
                    {
                        Console.Error.WriteLine("--weights= знает data и model; дано: {0}", v);
                        return 2;
                    }

                    o.Weights = v;
                }
"""),
    # применение
    ("""            // (`S166`, П18) совместная эффективность в выносе из пика
            if (o.LossJoint >= 0)
            {
                analyzer.CascadeLossJointFactor = o.LossJoint == 1;
            }

            return analyzer;
""",
     """            // (`S166`, П18) совместная эффективность в выносе из пика
            if (o.LossJoint >= 0)
            {
                analyzer.CascadeLossJointFactor = o.LossJoint == 1;
            }

            // (`A310`, П47) веса решателя; ключ обязан ДОЕХАТЬ до анализатора,
            // читатель — `SETUP` отражением (`ModelWeights`)
            if (o.Weights != null)
            {
                analyzer.ModelWeights = o.Weights == "model";
            }

            return analyzer;
"""),
])

patch('tools/effmaker/probes/FsaP42ArmProbe.cs', [
    ("""    ///     fsap42armprobe [--cascade=off] [--channels=peak|<битовая маска>] -- <ключи FsaStackShot>
""",
     """    ///     fsap42armprobe [--cascade=off] [--channels=peak|<битовая маска>] [--weights=data|model] -- <ключи FsaStackShot>
"""),
    ("""    /// матрицы и суммирование остаются). Без ключей — умолчания анализатора, и
""",
     """    /// матрицы и суммирование остаются); `--weights=data|model` — веса решателя
    /// по данным или по модели (`A310`, П47: `FsaAnalyzer.ModelWeights`). Без ключей — умолчания анализатора, и
"""),
    ("""            bool cascade = true;
            int mask = -1;
""",
     """            bool cascade = true;
            int mask = -1;
            string weights = null;
"""),
    ("""                else if (a.StartsWith("--channels=", StringComparison.Ordinal))
                {
                    string v = a.Substring(11);
""",
     """                else if (a.StartsWith("--weights=", StringComparison.Ordinal))
                {
                    weights = a.Substring(10);
                    if (weights != "data" && weights != "model")
                    {
                        Console.Error.WriteLine("--weights= знает data и model; дано: " + weights);
                        return 2;
                    }
                }
                else if (a.StartsWith("--channels=", StringComparison.Ordinal))
                {
                    string v = a.Substring(11);
"""),
    ("""                analyzer.MatrixChannelMask = mask;
            };
""",
     """                analyzer.MatrixChannelMask = mask;
                if (weights != null)
                {
                    analyzer.ModelWeights = weights == "model";
                }
            };
"""),
    ("""            Console.WriteLine("p42-arm: анализаторов создано {0}, суммирование {1}, маска каналов {2}, код снимка {3}",
                              created, cascade ? "вкл" : "ВЫКЛ", mask == -1 ? "все" : mask.ToString(CultureInfo.InvariantCulture), code);
""",
     """            Console.WriteLine("p42-arm: анализаторов создано {0}, суммирование {1}, маска каналов {2}, веса {3}, код снимка {4}",
                              created, cascade ? "вкл" : "ВЫКЛ", mask == -1 ? "все" : mask.ToString(CultureInfo.InvariantCulture),
                              weights ?? "умолчание анализатора", code);
"""),
])
print('ok')
