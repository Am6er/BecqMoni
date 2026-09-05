// ⛔ ОБЩИЙ ДОВЕСОК: ЦЕЛЕВАЯ ПЛАТФОРМА КАЖДОЙ ПРОБЫ (`T237`, полоса G4, 06.09.2026).
//
// Файл БЕЗ `Main` — по правилу `build_all.ps1` (`T57`) он идёт довеском в
// КАЖДУЮ пробу каталога, и объявленная здесь целевая платформа становится
// платформой ВХОДНОЙ сборки каждой пробы.
//
// ЗАЧЕМ. Начиная с .NET 4.6 платформа выбирает СОВМЕСТИМОСТНЫЕ переключатели
// (`AppContext`: течение культуры по задачам и потокам, разбор путей, крипто,
// таймеры, …) по целевой платформе входной сборки процесса. У приложения она
// объявлена проектом (`.NETFramework,Version=v4.8`); у пробы, собранной голым
// `csc`, её не было ВОВСЕ — и процесс пробы жил по правилам до .NET 4.6.
// Цена, измеренная 05.09.2026: `CultureProbeO14` дала «дефект у 6 стартеров
// из 6» вместо 1 из 6 — замер СОВПАЛ с посылкой строки и подозрений не вызвал.
//
// ⛔ Атрибут `TargetFramework` в сборке может быть ТОЛЬКО ОДИН (`AllowMultiple
//    = false`): проба, несущая свой собственный, не соберётся с этим довеском
//    (CS0579 «повторяющийся атрибут»). Свой атрибут пробе больше НЕ НУЖЕН —
//    сними его, довесок объявляет платформу за всех. Девять проб, которые
//    завели его порознь 05–06.09.2026, приведены к этому же 06.09.2026.
//
// ⚠ Проба обязана ПЕЧАТАТЬ платформу в шапке — иначе «собрана иначе» снова
//   неотличимо от «дефект воспроизведён». Для этого здесь помощник
//   `ProbeTargetFramework`: `Console.WriteLine(ProbeTargetFramework.Describe())`
//   даёт строку вида
//     процесс пробы: целевая платформа входной сборки=.NETFramework,Version=v4.8,
//       NoAsyncCurrentCulture=по умолчанию платформы
//   `Assert()` — то же, но с отказом (исключением), если платформа не та, что
//   объявлена здесь: для проб, у которых замер БЕЗ неё лишён смысла.
//
// ⚠ Что ЧИТАЕТ платформа: `AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName`
//   берётся из атрибута ВХОДНОЙ сборки при старте процесса; атрибут на
//   `BecquerelMonitor.exe`, которое проба лишь ЗАГРУЖАЕТ, на процесс пробы не
//   действует. Именно поэтому объявлять надо в самой пробе.
using System;
using System.Reflection;
using System.Runtime.Versioning;

[assembly: TargetFramework(ProbeTargetFramework.Expected, FrameworkDisplayName = ".NET Framework 4.8")]

/// <summary>
/// Целевая платформа процесса пробы: та, что объявлена общим довеском
/// (`_TargetFramework.cs`), против той, по которой процесс живёт на деле.
/// </summary>
static class ProbeTargetFramework
{
    /// <summary>Что объявляет довесок — то же, что у приложения (`.csproj`).</summary>
    public const string Expected = ".NETFramework,Version=v4.8";

    /// <summary>
    /// Платформа, по которой процесс ВЫБРАЛ правила совместимости, или
    /// <c>null</c>, если входная сборка её не объявляет (правила до .NET 4.6).
    /// </summary>
    public static string Declared
    {
        get { return AppDomain.CurrentDomain.SetupInformation.TargetFrameworkName; }
    }

    /// <summary>Совпадает ли платформа процесса с объявленной довеском.</summary>
    public static bool Matches
    {
        get { return string.Equals(Declared, Expected, StringComparison.Ordinal); }
    }

    /// <summary>
    /// Строка для шапки пробы: платформа процесса и главный из переключателей,
    /// который она выбирает, — течение культуры по задачам.
    /// </summary>
    public static string Describe()
    {
        bool noFlow;
        bool known = AppContext.TryGetSwitch("Switch.System.Globalization.NoAsyncCurrentCulture", out noFlow);
        string declared = Declared ?? "⛔ НЕ ОБЪЯВЛЕНА (правила до .NET 4.6)";
        string attr = null;
        try
        {
            Assembly entry = Assembly.GetEntryAssembly();
            if (entry != null)
            {
                TargetFrameworkAttribute a = (TargetFrameworkAttribute)Attribute.GetCustomAttribute(entry, typeof(TargetFrameworkAttribute));
                attr = a == null ? "нет" : a.FrameworkName;
            }
        }
        catch (Exception ex) { attr = "не прочитан: " + ex.GetType().Name; }
        return "процесс пробы:     целевая платформа входной сборки=" + declared
             + (attr != null && attr != Declared ? " (атрибут входной сборки: " + attr + ")" : "")
             + ", NoAsyncCurrentCulture=" + (known ? noFlow.ToString() : "по умолчанию платформы");
    }

    /// <summary>
    /// Печатает <see cref="Describe"/> и ОСТАНАВЛИВАЕТ пробу, если платформа
    /// процесса не та, что объявлена: замер по чужим правилам хуже отсутствия
    /// замера — он подтверждает посылку.
    /// </summary>
    public static void Assert()
    {
        Console.WriteLine(Describe());
        if (!Matches)
        {
            throw new InvalidOperationException(
                "проба живёт не по правилам приложения: целевая платформа входной сборки «"
                + (Declared ?? "не объявлена") + "», ожидалась «" + Expected
                + "» — собрана без довеска `_TargetFramework.cs`? (T237)");
        }
    }
}
