# -*- coding: utf-8 -*-
# П94: кэш луча забывать вокруг переноса электрона в слоях (находка §7.1 журнала).
import io
p = 'BecquerelMonitor/EfficiencyMaker/ElectronTransport.cs'
s = io.open(p, 'rb').read().decode('utf-8-sig')
old = ("            double advance = toEdge + 1e-7;\r\n"
       "            x += ux * advance;\r\n"
       "            y += uy * advance;\r\n"
       "            z += uz * advance;\r\n"
       "\r\n"
       "            this.CountLayerEscapes++;\r\n")
new = ("            double advance = toEdge + 1e-7;\r\n"
       "            x += ux * advance;\r\n"
       "            y += uy * advance;\r\n"
       "            z += uz * advance;\r\n"
       "\r\n"
       "            // ⛔ КЭШ ЛУЧА — ЗАБЫТЬ (П94 §7.1). `At` доверяет разобранному лучу,\r\n"
       "            // если точка лежит на нём в 10 нм и `along > 1e-7`, — верно, пока\r\n"
       "            // луч разбирал сам обход, идущий по нему. Точка выхода электрона к\r\n"
       "            // лучу кванта не относится, а совпасть с ним в 10 нм на 20 млн\r\n"
       "            // историй успевает: измерено на голом кристалле — 6 бинов по\r\n"
       "            // одному отсчёту, ключ там обязан быть инертен. Разбор заново —\r\n"
       "            // один поиск области на вылет.\r\n"
       "            this.rayValid = false;\r\n"
       "            this.CountLayerEscapes++;\r\n")
assert s.count(old) == 1, 'old'
s = s.replace(old, new)
old2 = ("        bool TransportInLayers(ref double x, ref double y, ref double z,\r\n"
        "                               ref double ux, ref double uy, ref double uz,\r\n"
        "                               ref double t, int depth)\r\n"
        "        {\r\n"
        "            double fraction = this.ElectronStepFraction;\r\n"
        "            if (!(fraction > 0.0) || fraction > 1.0)\r\n"
        "            {\r\n"
        "                fraction = 1.0;\r\n"
        "            }\r\n"
        "\r\n"
        "            for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)\r\n")
new2 = ("        bool TransportInLayers(ref double x, ref double y, ref double z,\r\n"
        "                               ref double ux, ref double uy, ref double uz,\r\n"
        "                               ref double t, int depth)\r\n"
        "        {\r\n"
        "            bool entered = this.WalkInLayers(ref x, ref y, ref z, ref ux, ref uy, ref uz, ref t);\r\n"
        "            // ⛔ КЭШ ЛУЧА разобран ЗДЕСЬ лучами электрона — обходу кванта он\r\n"
        "            // чужой (П94 §7.1): забыть, чтобы следующий `At`/`StepToBoundary`\r\n"
        "            // кванта разобрал свой луч заново, а не поверил чужому на 10 нм.\r\n"
        "            this.rayValid = false;\r\n"
        "            return entered;\r\n"
        "        }\r\n"
        "\r\n"
        '        /// <summary>Тело <see cref="TransportInLayers"/>; кэш луча снимает обёртка.</summary>\r\n'
        "        bool WalkInLayers(ref double x, ref double y, ref double z,\r\n"
        "                          ref double ux, ref double uy, ref double uz, ref double t)\r\n"
        "        {\r\n"
        "            // Точка старта — на луче кванта (занос) или у грани (возврат): кэш\r\n"
        "            // кванта ей не судья — искать область перебором.\r\n"
        "            this.rayValid = false;\r\n"
        "            double fraction = this.ElectronStepFraction;\r\n"
        "            if (!(fraction > 0.0) || fraction > 1.0)\r\n"
        "            {\r\n"
        "                fraction = 1.0;\r\n"
        "            }\r\n"
        "\r\n"
        "            for (int step = 0; step < TransportMaxSteps && t > TransportCutKev; step++)\r\n")
assert s.count(old2) == 1, 'old2'
s = s.replace(old2, new2)
io.open(p, 'wb').write(b'\xef\xbb\xbf' + s.encode('utf-8'))
print('ok')
