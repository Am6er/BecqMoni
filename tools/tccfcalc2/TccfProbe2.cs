// Проба: дёрнуть НОВУЮ tccfcalc.dll ЛСРМ (NuclideMasterPlus 2.10) напрямую.
//
// Отличия от старой пробы (tools/tccfcalc/TccfProbe.cs) — всё, кроме
// разрядности:
//
//   * старая DLL — Borland C++ Builder, 187 КБ, четыре экспорта, имена
//     искажены по-борландовски (`@имя$qтипы`), соглашение cdecl;
//   * новая — MinGW/GCC (импортирует msvcrt.dll), 3.9 МБ, ДЕСЯТЬ экспортов,
//     имена искажены по-stdcall'овски (`имя@размерАргументов`), соглашение
//     stdcall. Внутри — C++ с полными именами классов в таблице символов
//     и DWARF от рантайма.
//
// Обе 32-битные (PE32, i386) — харнесс обязан быть x86: csc /platform:x86.
//
// Экспорты новой DLL (после @ — размер аргументов в байтах, отсюда их число):
//
//   TCCFCALC_Prepare@24            Prepare(int A, int Z, int M,
//                                          char* baseDir, char* libPrefix, int)
//   TCCFCALC_Prepare_Json@8        Prepare_Json(char*, char*) — открывает файл
//   TCCFCALC_Calculate@4           Calculate(int числоРаспадов)
//   TCCFCALC_Calculate_n_sec@12    Calculate_n_sec(int распадов, double секунд)
//   TCCFCALC_Reset@0               Reset()
//   TCCFCALC_CalculateSpectrum@8   CalculateSpectrum(double секунд)
//   TCCFCALC_CalcSpectrum@12       CalcSpectrum(char*, double)
//   TCCFCALC_CalcSpectrumFile@12   CalcSpectrumFile(char* путь, double секунд)
//   TCCFCALC_Reset_Spectrum@0      Reset_Spectrum()
//   DllMain@12                     ­
//
// Смысл параметров `Prepare` вычитан из самой DLL (документации нет) и
// проверен прогоном:
//
//   * пролог кладёт шесть слов со стека; первое сравнивается с 0x122 = 290 —
//     это A поддельного набора «Scale», значит первые три слова A, Z, изомер,
//     как и в старой;
//   * четвёртое слово — строка, из неё DLL строит `<baseDir>tccfcalc.in`
//     (входной файл), `<baseDir>tccfcalc.out` (отчёт) и `<baseDir>Lib\`
//     (библиотека), поэтому строка обязана кончаться обратной косой. В СТАРОЙ
//     DLL входной файл брался из ТЕКУЩЕГО каталога, а baseDir указывал только
//     на библиотеку — это разные вещи;
//   * пятое слово — тоже строка, приписывается к пути библиотеки спереди;
//     пустая строка означает «библиотека в <baseDir>Lib»;
//   * шестое слово — целое, внутри `prepare_internal` идёт через `fild` в
//     double; на прогон не влияет, оставлено нулём (см. --p6).
//
// Возврат `Prepare` — номер сообщения в таблице `errstr` внутри DLL:
//
//   0 всё хорошо           5 TCCFCALC.IN file not found
//   1 Memory allocation    6 Incorrect input geometry or material data
//   2 Attenuation library  7 No ENSDF data found for the specified A, Z and M
//   3 No GLECS database    8 There is no valid record in ENSDF data library
//   4 ECCBINDX.BIN         9 Normalization record is not complete
//                         10 ICC.BIN   11 No X- or gamma-rays emitted
//                         12 No EPDL97  13 No Ttb  14 No Elib
//                         15..21 разбор входного json   22 Respapprox init
//
// Геометрия и вещества — `tccfcalc.in` в baseDir, отчёт — `tccfcalc.out` там
// же. Работать только в своей копии каталога, установку ЛСРМ не трогать.
//
// Сборка:
//   csc /platform:x86 /out:TccfProbe2.exe TccfProbe2.cs
using System;
using System.IO;
using System.Runtime.InteropServices;

static class TccfProbe2
{
    const string Dll = "tccfcalc.dll";

    [DllImport(Dll, EntryPoint = "TCCFCALC_Prepare@24", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    static extern int Prepare(int a, int z, int m, string baseDir, string libDir, int p6);

    [DllImport(Dll, EntryPoint = "TCCFCALC_Calculate@4", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall)]
    static extern int Calculate(int decays);

    [DllImport(Dll, EntryPoint = "TCCFCALC_Calculate_n_sec@12", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall)]
    static extern int CalculateNSec(int decays, double seconds);

    [DllImport(Dll, EntryPoint = "TCCFCALC_Reset@0", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall)]
    static extern int Reset();

    [DllImport(Dll, EntryPoint = "TCCFCALC_Reset_Spectrum@0", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall)]
    static extern int ResetSpectrum();

    [DllImport(Dll, EntryPoint = "TCCFCALC_CalcSpectrumFile@12", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Ansi)]
    static extern int CalcSpectrumFile(string path, double seconds);

    [DllImport(Dll, EntryPoint = "TCCFCALC_CalculateSpectrum@8", ExactSpelling = true,
               CallingConvention = CallingConvention.StdCall)]
    static extern int CalculateSpectrum(double seconds);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    static extern IntPtr LoadLibrary(string path);

    static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine(
                "TccfProbe2 <каталог> <A> <Z> <M> [распадов] [--lib=путь] [--p6=N]"
                + " [--sec=S] [--spectrum=секунд[:файл]]");
            return 2;
        }

        string dir = Path.GetFullPath(args[0]);
        int a = int.Parse(args[1]), z = int.Parse(args[2]), m = int.Parse(args[3]);
        int decays = 100000;
        string libDir = "";
        int p6 = 0;
        double seconds = -1.0;
        double spectrumSeconds = -1.0;
        string spectrumPath = "spectrum.spe";

        for (int i = 4; i < args.Length; i++)
        {
            string s = args[i];
            if (s.StartsWith("--lib=")) libDir = s.Substring(6);
            else if (s.StartsWith("--p6=")) p6 = int.Parse(s.Substring(5));
            else if (s.StartsWith("--sec=")) seconds = double.Parse(s.Substring(6),
                System.Globalization.CultureInfo.InvariantCulture);
            else if (s.StartsWith("--spectrum="))
            {
                string v = s.Substring(11);
                int colon = v.IndexOf(':');
                if (colon > 0)
                {
                    spectrumPath = v.Substring(colon + 1);
                    v = v.Substring(0, colon);
                }
                spectrumSeconds = double.Parse(v,
                    System.Globalization.CultureInfo.InvariantCulture);
            }
            else decays = int.Parse(s);
        }

        Directory.SetCurrentDirectory(dir);
        string baseDir = dir.EndsWith("\\") ? dir : dir + "\\";
        string outPath = Path.Combine(dir, "tccfcalc.out");
        if (File.Exists(outPath))
        {
            File.Delete(outPath);
        }

        // Грузим по явному пути: DLL лежит в рабочем каталоге, а не рядом с exe.
        if (LoadLibrary(Path.Combine(dir, Dll)) == IntPtr.Zero)
        {
            Console.Error.WriteLine("LoadLibrary не смог: " + Marshal.GetLastWin32Error());
            return 1;
        }

        Console.WriteLine("каталог: " + baseDir);
        Console.WriteLine("библиотека: " + (libDir == "" ? "<baseDir>Lib" : libDir));
        Console.WriteLine("нуклид: A=" + a + " Z=" + z + " M=" + m
                          + ", распадов " + decays + ", p6=" + p6);

        int rc;
        try
        {
            rc = Reset();
            Console.WriteLine("Reset -> " + rc);
            rc = Prepare(a, z, m, baseDir, libDir, p6);
            Console.WriteLine("Prepare -> " + rc);
            if (rc != 0)
            {
                Console.WriteLine("Prepare отказал, считать нечего");
            }
            else if (seconds >= 0)
            {
                rc = CalculateNSec(decays, seconds);
                Console.WriteLine("Calculate_n_sec -> " + rc);
            }
            else
            {
                rc = Calculate(decays);
                Console.WriteLine("Calculate -> " + rc);
            }

            if (rc == 0 && spectrumSeconds >= 0)
            {
                // Спектр складывается отдельным вызовом: `calc_spectrum = true`
                // в файле сам по себе ничего не пишет. Коды спектральных
                // вызовов в rc НЕ попадают: CalcSpectrumFile заведомо отвечает
                // 7 (TODO T5), и он затирал бы успешный код самого расчёта.
                int src = ResetSpectrum();
                Console.WriteLine("Reset_Spectrum -> " + src);
                src = CalculateSpectrum(spectrumSeconds);
                Console.WriteLine("CalculateSpectrum -> " + src);
                // `CalcSpectrumFile` отвечает 7 и ничего не пишет — что она
                // ждёт первым словом, не разобрано (TODO T5). Вызов оставлен,
                // чтобы это было видно, а не забыто.
                src = CalcSpectrumFile(Path.Combine(dir, spectrumPath), spectrumSeconds);
                Console.WriteLine("CalcSpectrumFile -> " + src
                                  + " (спектр пишет CalculateSpectrum: test_spectr.spe)");
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("сорвалось: " + e.GetType().Name + ": " + e.Message);
            return 1;
        }

        if (File.Exists(outPath))
        {
            Console.WriteLine("--- tccfcalc.out (" + new FileInfo(outPath).Length + " байт)");
            Console.WriteLine(File.ReadAllText(outPath));
        }
        else
        {
            Console.WriteLine("отчёта нет");
        }

        // Код DLL наружу: возврат 0 при отказе Prepare/Calculate заставлял
        // вызывающих ловить отказ числом строк отчёта. run_tccf2.py проверяет
        // ещё и печать «... -> 0» — она работает и со старыми копиями exe.
        return rc;
    }
}
