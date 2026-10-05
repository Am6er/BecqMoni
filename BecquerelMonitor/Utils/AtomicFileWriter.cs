using System;
using System.IO;

namespace BecquerelMonitor.Utils
{
    // All XML files in the project used to be written with FileMode.Create directly into
    // the target file, so any failure mid-serialization (crash, power loss, full disk,
    // serializer exception) truncated and destroyed the previous good copy. This helper
    // writes to a temp file in the same directory, flushes it to disk, then atomically
    // swaps it into place - the old file stays intact until the new one is complete.
    public static class AtomicFileWriter
    {
        public static void Write(string path, Action<Stream> writeAction)
        {
            string tempPath = path + ".tmp";
            try
            {
                using (FileStream fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    writeAction(fileStream);
                    fileStream.Flush(true);
                }
                if (File.Exists(path))
                {
                    File.Replace(tempPath, path, null);
                }
                else
                {
                    File.Move(tempPath, path);
                }
            }
            catch
            {
                // (`AMBER201`, мелочь 2.13, 05.10.2026) Отказ записи — исключение
                // вызывающему, как и прежде, но недописанный `.tmp` больше не
                // остаётся рядом с файлом человека (в каталоге конфигурации их
                // копилось по одному на каждый отказ). Прежний файл цел: замена
                // идёт только после полной записи.
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch (Exception)
                {
                    // Снять не вышло (файл держит чужой процесс) — главное здесь
                    // исходный отказ, он и уходит наверх.
                }
                throw;
            }
        }
    }
}
