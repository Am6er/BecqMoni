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
    }
}
