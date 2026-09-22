using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace BecquerelMonitor
{
    internal static class SpecUtilsNative
    {
        private const string DLL = "SpecUtilsNet.dll";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int GetShortPathNameW(string lpszLongPath, StringBuilder lpszShortPath, int cchBuffer);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr Open(string path, string file_ext);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern void Close(IntPtr ptr);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetMeasurementsCount(IntPtr ptr);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetChannelCount(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern double GetTotalCounts(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetSpectrum(IntPtr ptr, int m_num, out int size);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern double GetLiveTime(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern double GetRealTime(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int Test();

        /// <summary>
        /// Source types:
        /// Polynomial = 0,
        /// FullRangeFraction = 1,
        /// LowerChannelEdge = 2,
        /// UnspecifiedUsingDefaultPolynomial = 3,
        /// InvalidEquationType = 4
        /// </summary>
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetEnergyCalType(IntPtr ptr, int m_num);

        /// <summary>
        /// Source types:
        /// IntrinsicActivity = 0,
        /// Calibration = 1,
        /// Background = 2,
        /// Foreground = 3,
        /// Unknown = 4
        /// </summary>
        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern int GetSourceType(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern long GetStartTime(IntPtr ptr, int m_num);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetEnergyCalibrationCoefficients(IntPtr ptr, int m_num, out int size);

        [DllImport(DLL, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
        public static extern IntPtr GetEnergyCalibrationChannelEnergies(IntPtr ptr, int m, out int size);
    }

    /// <summary>
    /// `AMBER75` (полоса П138, 22.09.2026): ПУТЬ, КОТОРЫЙ МОЖНО ОТДАТЬ
    /// НАТИВНОЙ <c>SpecUtilsNet.dll</c>.
    ///
    /// ⛔ ЧТО СЛОМАНО НА ТОЙ СТОРОНЕ, И ЭТО ПРОЧИТАНО В КОДЕ DLL, А НЕ УГАДАНО.
    /// <c>SpecUtilsNet!Open</c> принимает путь ШИРОКИМИ символами и делает с ним
    /// ровно два действия (разбор 22.09.2026, <c>dumpbin /disasm</c>, RVA 0x2FB0):
    /// <list type="number">
    /// <item><c>GetShortPathNameW(путь, буфер, 260)</c> — попытка получить
    /// короткое имя 8.3; ноль в ответ обрывает <c>Open</c> с NULL;</item>
    /// <item><c>WideCharToMultiByte(CP_ACP, 0, …)</c> — полученное имя
    /// переводится в УЗКУЮ строку КОДОВОЙ СТРАНИЦЕЙ МАШИНЫ, и она уходит в
    /// <c>SpecUtils::SpecFile::load_file</c>.</item>
    /// </list>
    /// А сама SpecUtils на Windows понимает узкое имя как UTF-8 и переводит его
    /// обратно в UTF-16 перед открытием файла. Кодовая страница машины здесь
    /// 1251 (<c>GetACP()</c> = 1251, замерено), и байты кириллицы из неё
    /// правильным UTF-8 не являются — файл не находится, <c>Open</c> отдаёт
    /// NULL, а человек читает «Unable to load file using SpecUtils. Unknown file
    /// format.» о совершенно здоровом файле.
    ///
    /// ⚠ ОТСЮДА ГЛАВНОЕ: ВИНОВАТА НЕ КИРИЛЛИЦА САМА ПО СЕБЕ, А ОТСУТСТВИЕ
    /// КОРОТКОГО ИМЕНИ. Пока у тома включено порождение имён 8.3, шаг (1)
    /// отдаёт чистый ASCII, и путь с кириллицей открывается. Замерено
    /// 22.09.2026 на одном и том же файле:
    /// <c>C:\Users\moroz\YandexDisk\Спектры\Разное с канала\Подвал.N42</c> →
    /// короткое имя <c>C:\Users\moroz\YANDEX~1\11D6~1\E352~1\D07B~1.N42</c>,
    /// открывается; он же на <c>D:\</c> (порождение 8.3 выключено) → короткое
    /// имя равно длинному, NULL. То есть беда приходит с томом: съёмный диск,
    /// сетевая папка, любой том с выключенным 8.3 — и её нельзя объявить
    /// «у Amber не воспроизводится».
    ///
    /// ⛔ ПОЧЕМУ ОБХОД, А НЕ ПОЧИНКА DLL. Исходников <c>SpecUtilsNet.dll</c> в
    /// дереве нет (двоичный файл в <c>BecquerelMonitor\</c>, копируется в вывод
    /// как содержимое), и единственная точка, где мы можем на это повлиять, —
    /// имя, которое мы ей подаём. Подать «правильные UTF-8 байты, записанные
    /// символами 1251» нельзя: шаг (1) требует, чтобы путь СУЩЕСТВОВАЛ, а такой
    /// путь не существует. Короткое имя тоже не спасает — его берёт сама DLL, и
    /// там, где оно есть, всё и так работает. Остаётся одно: дать файлу путь из
    /// одних ASCII-символов, то есть временную копию.
    ///
    /// ⚠ Цена измерена и мала: спектры — килобайты и единицы мегабайт, копия
    /// делается ТОЛЬКО когда в пути есть не-ASCII, и снимается в <c>finally</c>
    /// того же ввоза. На пути из одних ASCII не делается ничего.
    /// </summary>
    internal static class SpecUtilsPath
    {
        /// <summary>
        /// Путь, который можно отдать <c>SpecUtilsNative.Open</c>. Если исходный
        /// путь весь из ASCII — он же и возвращается, <paramref name="tempCopy"/>
        /// остаётся null. Иначе кладётся временная копия по пути из одних
        /// ASCII-символов, и её имя возвращается ОБОИМИ значениями: вызывающий
        /// обязан снять её <see cref="Drop"/> в <c>finally</c>.
        ///
        /// ⚠ Не-ASCII во ВСЁМ пути, а не только в имени файла: узкую строку
        /// получает путь целиком, и каталог «Спектры» ломает ввоз ровно так же,
        /// как имя «Подвал.N42».
        ///
        /// ⚠ Обходить нечем (временный каталог сам не ASCII и короткого имени у
        /// него нет) — возвращается исходный путь, то есть прежнее поведение со
        /// прежним отказом. Выдумывать здесь второй отказ нельзя: он был бы
        /// новым, а беда старая.
        /// </summary>
        internal static string Prepare(string path, out string tempCopy)
        {
            tempCopy = null;
            if (string.IsNullOrEmpty(path) || IsAscii(path))
            {
                return path;
            }
            string dir = AsciiTempDirectory();
            if (dir == null)
            {
                return path;
            }
            string ext = Path.GetExtension(path);
            if (ext == null || !IsAscii(ext))
            {
                ext = "";
            }
            string copy = Path.Combine(dir, "becqmoni-specutils-" + Guid.NewGuid().ToString("N") + ext);
            File.Copy(path, copy, true);
            tempCopy = copy;
            return copy;
        }

        /// <summary>
        /// Снять временную копию. Молча: её отсутствие или занятость — не беда
        /// ввоза, а мусор в TEMP, и заслонять ею настоящую причину отказа
        /// (которая уже названа выше по стеку) нельзя.
        /// </summary>
        internal static void Drop(string tempCopy)
        {
            if (string.IsNullOrEmpty(tempCopy))
            {
                return;
            }
            try
            {
                File.Delete(tempCopy);
            }
            catch
            {
            }
        }

        /// <summary>
        /// Каталог для временной копии, весь из ASCII: сам <c>TEMP</c>, а если у
        /// пользователя он назван не по-английски — его короткое имя 8.3 (на
        /// системном томе оно, как правило, есть). Ни того, ни другого — null.
        /// </summary>
        private static string AsciiTempDirectory()
        {
            string temp;
            try
            {
                temp = Path.GetTempPath();
            }
            catch
            {
                return null;
            }
            if (IsAscii(temp))
            {
                return temp;
            }
            string shortened = ShortPath(temp);
            return shortened != null && IsAscii(shortened) ? shortened : null;
        }

        private static string ShortPath(string path)
        {
            try
            {
                StringBuilder sb = new StringBuilder(1024);
                int n = SpecUtilsNative.GetShortPathNameW(path, sb, sb.Capacity);
                return n > 0 && n < sb.Capacity ? sb.ToString() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Все символы строки — ASCII (меньше 0x80). Ровно это и есть условие
        /// того, что перевод кодовой страницей на той стороне ничего не испортит:
        /// в любой однобайтовой кодовой странице Windows нижние 128 позиций
        /// совпадают с ASCII, а ASCII есть правильный UTF-8.
        /// </summary>
        internal static bool IsAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] >= (char)0x80)
                {
                    return false;
                }
            }
            return true;
        }
    }
}