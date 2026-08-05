using System;
using System.Globalization;
using System.IO;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Где лежат матрицы отклика: `config\device\response\<guid>.rmx`, по файлу
    /// на геометрию, ключ — Guid кривой эффективности (у каждой своя геометрия).
    ///
    /// Отдельно от конфигурации, и это не вкусовщина. `ResultData.DeviceConfig`
    /// сериализуется в файл спектра целиком; матрица, положенная в
    /// конфигурацию, уезжала бы в каждый сохранённый спектр — сотни килобайт,
    /// бесполезных получателю. Здесь же файл остаётся на машине, где посчитан,
    /// а спектр уносит в лучшем случае корешок в несколько десятков байт.
    /// </summary>
    public static class ResponseMatrixStore
    {
        public static string Directory
        {
            get
            {
                return Path.Combine(Package.GetInstance().DeviceDir, "response");
            }
        }

        public static string PathOf(string efficiencyGuid)
        {
            string name = Sanitize(efficiencyGuid);
            return Path.Combine(Directory, name + ".rmx");
        }

        public static bool Exists(string efficiencyGuid)
        {
            return !string.IsNullOrEmpty(efficiencyGuid) && File.Exists(PathOf(efficiencyGuid));
        }

        public static ResponseMatrix Load(string efficiencyGuid)
        {
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return null;
            }

            return ResponseMatrix.Load(PathOf(efficiencyGuid));
        }

        public static void Save(string efficiencyGuid, ResponseMatrix matrix)
        {
            if (string.IsNullOrEmpty(efficiencyGuid) || matrix == null)
            {
                throw new ArgumentNullException("matrix");
            }

            matrix.Save(PathOf(efficiencyGuid));
        }

        public static void Delete(string efficiencyGuid)
        {
            if (string.IsNullOrEmpty(efficiencyGuid))
            {
                return;
            }

            string path = PathOf(efficiencyGuid);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        public static long FileSize(string efficiencyGuid)
        {
            if (!Exists(efficiencyGuid))
            {
                return 0;
            }

            return new FileInfo(PathOf(efficiencyGuid)).Length;
        }

        /// <summary>
        /// Guid приходит из конфигурации, а она правится руками. Всё, что не
        /// буква, цифра, дефис или подчёркивание, заменяется: имя файла не место
        /// для доверия чужой строке.
        /// </summary>
        static string Sanitize(string guid)
        {
            if (string.IsNullOrEmpty(guid))
            {
                return "none";
            }

            var chars = guid.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z')
                          || (c >= 'A' && c <= 'Z') || c == '-' || c == '_';
                if (!ok)
                {
                    chars[i] = '_';
                }
            }

            return new string(chars);
        }
    }
}
