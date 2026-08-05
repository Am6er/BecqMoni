// Проба: дёрнуть TCCFCALC.dll из поставки ЛСРМ напрямую.
//
// DLL 32-битная (PE32, i386), собрана Borland C++ Builder, тянет только
// KERNEL32 и USER32 — рантайм в ней статический. Значит харнесс обязан быть
// x86: csc /platform:x86.
//
// Три экспорта, имена искажены по-борландовски (`@имя$qтипы`):
//
//   @TCCFCALC_Prepare$qiiipc   -> Prepare(int, int, int, char*)
//   @TCCFCALC_Calculate$qi     -> Calculate(int)
//   @TCCFCALC_Reset$qv         -> Reset()
//
// Смысл параметров прочитан из самой DLL, документации к ней нет:
//
//   * пролог Prepare кладёт три int со СТЕКА ([ebp+8], [ebp+0xC], [ebp+0x10])
//     в глобальные переменные — значит соглашение стековое, не борландовский
//     __fastcall; из строк `No ENSDF data found for the specified A, Z and M`
//     следует, что это A, Z и номер изомера;
//   * четвёртый параметр — базовый каталог: в DLL лежат шаблоны
//     `%sLIB\ENSDF2\`, `%sLIB\ICC\`, `%sLIB\ECCBINDX\`, `%sLIB\XCOM\`,
//     поэтому строка должна кончаться обратной косой;
//   * пролог Calculate делает `fild [ebp+8]` и ПРИБАВЛЯЕТ значение к глобальному
//     double, а в отчёте есть строка `Number of simulated decays` — значит это
//     число разыгрываемых распадов, и вызовы накапливаются.
//
// Геометрия и вещества читаются из `TCCFCALC.in` в текущем каталоге, отчёт
// пишется в `tccfcalc.out` там же. Проба работает ТОЛЬКО в своей копии
// каталога: установку ЛСРМ трогать нельзя.
//
// Сборка:
//   csc /platform:x86 /out:TccfProbe.exe TccfProbe.cs
using System;
using System.IO;
using System.Runtime.InteropServices;

static class TccfProbe
{
    const string Dll = "TCCFCALC.dll";

    [DllImport(Dll, EntryPoint = "@TCCFCALC_Prepare$qiiipc",
               CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    static extern int Prepare(int a, int z, int m, string baseDir);

    [DllImport(Dll, EntryPoint = "@TCCFCALC_Calculate$qi",
               CallingConvention = CallingConvention.Cdecl)]
    static extern int Calculate(int decays);

    [DllImport(Dll, EntryPoint = "@TCCFCALC_Reset$qv",
               CallingConvention = CallingConvention.Cdecl)]
    static extern int Reset();

    static int Main(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("TccfProbe <каталог> <A> <Z> <M> [распадов]");
            return 2;
        }

        string dir = Path.GetFullPath(args[0]);
        int a = int.Parse(args[1]), z = int.Parse(args[2]), m = int.Parse(args[3]);
        int decays = args.Length > 4 ? int.Parse(args[4]) : 100000;

        Directory.SetCurrentDirectory(dir);
        string baseDir = dir.EndsWith("\\") ? dir : dir + "\\";
        string outPath = Path.Combine(dir, "tccfcalc.out");
        if (File.Exists(outPath))
        {
            File.Delete(outPath);
        }

        Console.WriteLine("каталог: " + baseDir);
        Console.WriteLine("нуклид: A=" + a + " Z=" + z + " M=" + m + ", распадов " + decays);

        int rc;
        try
        {
            rc = Reset();
            Console.WriteLine("Reset -> " + rc);
            rc = Prepare(a, z, m, baseDir);
            Console.WriteLine("Prepare -> " + rc);
            rc = Calculate(decays);
            Console.WriteLine("Calculate -> " + rc);
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

        return 0;
    }
}
