using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace BecquerelMonitor
{
    /// <summary>
    /// Пользовательский гид BecqMoni: сайт и копия в дистрибутиве (`AMBER249`, `AMBER250`).
    ///
    /// Копия — каталог <c>docs\</c> рядом с exe: страницы гида из <c>..\docs</c> репозитория
    /// входят в сборку и в публикацию ClickOnce как Content (см. BecquerelMonitor.csproj).
    /// Язык гида — язык интерфейса: <c>?lang=ru</c>, если он русский, иначе <c>?lang=en</c>;
    /// его ставит <c>Program.ApplyLanguage</c> из настройки «Язык» через CurrentUICulture.
    /// </summary>
    internal static class GuideLinks
    {
        public const string SiteUrl = "https://amba.cloud/becqmoni/help/";

        public static string Lang
        {
            get { return Thread.CurrentThread.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en"; }
        }

        public static string SiteWithLang
        {
            get { return SiteUrl + "?lang=" + Lang; }
        }

        public static string LocalIndex
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "index.html"); }
        }

        /// <summary>
        /// Открыть локальный гид в браузере по умолчанию, на языке интерфейса; если копии
        /// нет (сборка без docs\) — сайт. Возвращает адрес, который открывался.
        ///
        /// ⚠ Оболочка Windows открывает <c>file://…/index.html?lang=ru</c> как путь к файлу
        /// и ТЕРЯЕТ <c>?lang=</c>. Поэтому открывается короткий переход из %TEMP%, а уже он
        /// ведёт браузер на страницу с языком в адресе.
        /// </summary>
        public static string OpenLocal()
        {
            string index = LocalIndex;
            if (!File.Exists(index))
            {
                Process.Start(new ProcessStartInfo(SiteWithLang) { UseShellExecute = true });
                return SiteWithLang;
            }
            string target = new Uri(index).AbsoluteUri + "?lang=" + Lang;
            string dir = Path.Combine(Path.GetTempPath(), "BecqMoni");
            Directory.CreateDirectory(dir);
            string launcher = Path.Combine(dir, "guide-" + Lang + ".html");
            string js = target.Replace("\\", "\\\\").Replace("\"", "\\\"");
            string html = "<!doctype html><html><head><meta charset=\"utf-8\"><title>BecqMoni</title>"
                + "<meta http-equiv=\"refresh\" content=\"0;url=" + target.Replace("\"", "%22") + "\">"
                + "<script>location.replace(\"" + js + "\");</script></head><body></body></html>";
            File.WriteAllText(launcher, html, new UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(launcher) { UseShellExecute = true });
            return target;
        }
    }
}
