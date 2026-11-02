// `AMBER269` дефект 5 (полоса `phys29b`, 02.11.2026): ПРИЧИНА РАСХОЖДЕНИЯ ОТПЕЧАТКА КРИВОЙ.
//
// Кривая точечной сцены, посчитанная до 30.10.2026, несла отпечаток геометрии без строки
// сдвига точки в капсуле (`srcoff`, Ф6), и вкладка «Эффективность» говорила «геометрию
// правили после расчёта» — а её не правили. Теперь `EfficiencyConfigData.CurveBeforeSourceOffset`
// узнаёт этот случай, и подпись (`DeviceConfigForm.CurveSceneNotes`) называет сдвиг точки.
//
//     SrcoffCauseProbeA269 [--devices=<каталог копии config\device>]
//
// Раздел 1 — синтетика (положительные контроли): точечная сцена со сдвигом 1.5 мм и отпечатком
// БЕЗ строки `srcoff` (обязана дать «до сдвига точки»), та же сцена с правкой расстояния до точки (обязана
// дать прежнее «геометрию правили»), свежий отпечаток (расхождения нет), не точечная сцена
// (отпечатка «до сдвига» нет вовсе). Раздел 2 — конфигурации приборов из `--devices=` (копия;
// проба только читает): по каждой кривой с геометрией — отпечатки и подпись.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Xml.Serialization;
using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor.Probes
{
    static class SrcoffCauseProbeA269
    {
        static int failures;

        static readonly MethodInfo SceneNotes = typeof(DeviceConfigForm).GetMethod(
            "CurveSceneNotes", BindingFlags.NonPublic | BindingFlags.Static);

        static void Ok(bool condition, string what)
        {
            Console.WriteLine("   {0} {1}", condition ? "да " : "НЕТ", what);
            if (!condition)
            {
                failures++;
            }
        }

        static List<string> Notes(EfficiencyConfigData config)
        {
            return (List<string>)SceneNotes.Invoke(null, new object[] { config });
        }

        static EfficiencyConfigData WithCurve(GeometryModel g, string fingerprint)
        {
            var c = new EfficiencyConfigData();
            c.Geometry = g;
            c.Curve = new List<ROIEfficiencyData>   // HasCurve — от двух точек
            {
                new ROIEfficiencyData { Energy = 100.0, Efficiency = 0.3 },
                new ROIEfficiencyData { Energy = 662.0, Efficiency = 0.1 },
            };
            c.GeometryFingerprint = fingerprint;
            return c;
        }

        [STAThread]
        static int Main(string[] args)
        {
            string devices = null;
            foreach (string a in args)
            {
                if (a.StartsWith("--devices=", StringComparison.Ordinal)) devices = a.Substring(10);
                else { Console.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            Console.WriteLine("== раздел 1: синтетика");
            GeometryModel point = new GeometryModel();
            GeometryPresets.Items[0].Apply(point);
            point.SourceType = GeometrySourceType.Point;
            Console.WriteLine("   заготовка «{0}», точечный источник: {1}; сдвиг точки {2} мм", GeometryPresets.Items[0].Name,
                              point.Describe(), point.SourceCapsuleOffset.ToString("R", CultureInfo.InvariantCulture));
            // ⛔ Откат Ф6 (`critpt30`, 02.11.2026): сдвига точки нет ни у одной сцены — отпечатка
            // «до сдвига» нет, и кривая прежней версии (без строки `srcoff`) сходится с нынешней.
            Ok(point.SourceCapsuleOffset == 0.0, "откат Ф6: у точечной сцены с капсулой ОСГИ сдвига точки нет");

            string now = ResponseMatrix.GeometryFingerprint(point);
            string before = ResponseMatrix.GeometryFingerprintBeforeSourceOffset(point);
            Console.WriteLine("   отпечаток нынешний {0}, без строки srcoff {1}", now, before == "" ? "(нет)" : before);
            Ok(before == "", "откат Ф6: отпечатка «до сдвига» нет");

            GeometryModel edited = point.Clone();
            edited.PointDistance = edited.PointDistance + 10.0;   // точку отодвинули — правка геометрии
            EfficiencyConfigData editedCurve = WithCurve(edited, now);
            List<string> editedNotes = Notes(editedCurve);
            Ok(editedCurve.CurveGeometryMismatch && !editedCurve.CurveBeforeSourceOffset,
               "правка расстояния до точки после счёта: расхождение есть, причина — НЕ сдвиг точки");
            Ok(editedNotes.Count == 1 && editedNotes[0] == BecquerelMonitor.Properties.Resources.EfficiencyCurveOtherGeometry,
               "подпись — прежнее «геометрию правили» (" + (editedNotes.Count > 0 ? editedNotes[0] : "—") + ")");

            EfficiencyConfigData fresh = WithCurve(point, now);
            Ok(!fresh.CurveGeometryMismatch && !fresh.CurveBeforeSourceOffset && Notes(fresh).Count == 0,
               "свежий отпечаток: расхождения и подписи нет");

            GeometryModel bare = point.Clone();
            bare.SourceType = GeometrySourceType.Cylinder;
            Ok(ResponseMatrix.GeometryFingerprintBeforeSourceOffset(bare) == "",
               "сцена без сдвига: отпечатка «до сдвига» нет (тексты совпадают)");

            if (devices != null)
            {
                Console.WriteLine("== раздел 2: конфигурации приборов {0} (только чтение)", devices);
                var ser = new XmlSerializer(typeof(DeviceConfigInfo));
                foreach (string path in Directory.GetFiles(devices, "*.xml"))
                {
                    DeviceConfigInfo info;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        info = (DeviceConfigInfo)ser.Deserialize(fs);
                    }

                    if (info.EfficiencyConfigs == null)
                    {
                        continue;
                    }

                    foreach (EfficiencyConfigData c in info.EfficiencyConfigs)
                    {
                        if (!c.HasGeometry || string.IsNullOrEmpty(c.GeometryFingerprint))
                        {
                            continue;
                        }

                        List<string> notes = Notes(c);
                        Console.WriteLine("   {0} | {1} | сдвиг {2} мм | при кривой {3} | нынешний {4} | без srcoff {5} | расхождение {6} | причина сдвиг {7}",
                                          Path.GetFileNameWithoutExtension(path), c.Name,
                                          c.Geometry.SourceCapsuleOffset.ToString("R", CultureInfo.InvariantCulture),
                                          c.GeometryFingerprint, ResponseMatrix.GeometryFingerprint(c.Geometry),
                                          ResponseMatrix.GeometryFingerprintBeforeSourceOffset(c.Geometry),
                                          c.CurveGeometryMismatch ? "да" : "нет", c.CurveBeforeSourceOffset ? "да" : "нет");
                        foreach (string n in notes)
                        {
                            Console.WriteLine("      подпись: " + n);
                        }
                    }
                }
            }

            Console.WriteLine(failures == 0 ? "ИТОГ: все проверки выполнены" : "ИТОГ: НЕ СОШЛОСЬ " + failures);
            return failures == 0 ? 0 : 1;
        }
    }
}
