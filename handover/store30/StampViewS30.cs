using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

// store30 (AMBER274, 02.11.2026): состав клейма матрицы склада — ключи физики в настройках файла,
// отпечаток matdb, клеймо по настройкам файла, каждый ключ физики 28–30 перевёрнутым обязан менять клеймо;
// раздел электрона — его клеймо и годность для сцены.
//   StampViewS30 --scene=<X.in> --matrix=<X.rmx> [...ещё пары через --scene/--matrix по порядку]
static class StampViewS30
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        GlobalConfigManager.GetInstance();
        string scene = null;
        int bad = 0;
        Console.WriteLine("phys кода {0}; mdb базы {1}", ResponseMatrix.PhysicsVersion, MaterialDatabase.SimulatorDataFingerprint());
        foreach (string a in args)
        {
            if (a.StartsWith("--scene=", StringComparison.Ordinal)) { scene = a.Substring(8); continue; }
            if (!a.StartsWith("--matrix=", StringComparison.Ordinal)) { Console.Error.WriteLine("ключ? " + a); return 2; }
            string path = a.Substring(9);
            MatrixRefusal refusal; int format;
            ResponseMatrix m = ResponseMatrix.Load(path, out refusal, out format);
            GeometryModel g = GeometryModel.Load(scene);
            if (m == null || g == null) { Console.WriteLine("{0}: не прочитана ({1})", Path.GetFileName(path), refusal); bad++; continue; }
            ResponseMatrixOptions o = m.Options;
            string own = ResponseMatrix.ComputeStamp(g, o);
            string def = ResponseMatrix.ComputeStamp(g, new ResponseMatrixOptions { Histories = o.Histories });
            var sb = new StringBuilder();
            string[] keys = { "ElectronStraggling", "ElectronMixedTransport", "ElectronMottHeavy", "ElectronElasticRefine", "ElectronScreeningDpwa", "CascadeCkVacancy" };
            string[] toks = { "estr", "emix", "mott", "eref", "dpwa", "ckv" };
            bool allOn = true, allFlip = true;
            for (int i = 0; i < keys.Length; i++)
            {
                FieldInfo f = typeof(ResponseMatrixOptions).GetField(keys[i]);
                bool v = (bool)f.GetValue(o);
                f.SetValue(o, !v);
                bool differs = ResponseMatrix.ComputeStamp(g, o) != m.Stamp;
                f.SetValue(o, v);
                allOn &= v; allFlip &= differs;
                sb.Append(' ').Append(toks[i]).Append('=').Append(v ? 1 : 0).Append(differs ? "" : "(!клеймо не различает)");
            }
            ElectronSectionRefusal why;
            ResponseElectronSection s = m.ElectronSectionFor(g, out why);
            bool ok = own == m.Stamp && def == m.Stamp && allOn && allFlip && ResponseMatrix.PhysicsFromStamp(m.Stamp) == 30
                      && s != null && s.Stamp.StartsWith("esec=4;phys=30;", StringComparison.Ordinal) && s.Stamp.Contains(";br=");
            if (!ok) bad++;
            Console.WriteLine("{0}: формат {1}, {2} узлов, историй {3}; клеймо {4}; = по настройкам файла: {5}; = при умолчаниях кода: {6};{7}; тело {8}; раздел: {9} {10}; {11}",
                              Path.GetFileNameWithoutExtension(path), format, m.Energies.Length, o.Histories, m.Stamp, own == m.Stamp, def == m.Stamp,
                              sb.ToString(), m.BodyFingerprint == null ? "—" : m.BodyFingerprint.Substring(0, 16), why,
                              s == null ? "—" : s.Stamp, ok ? "ГОДНА" : "⛔ НЕ ГОДНА");
        }
        Console.WriteLine(bad == 0 ? "ИТОГ: все матрицы — физика 30 с ключами и разделом esec=4" : "ИТОГ: ОТКАЗ, матриц " + bad);
        return bad == 0 ? 0 : 1;
    }
}
