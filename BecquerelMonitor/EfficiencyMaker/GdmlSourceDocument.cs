using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Xml;

namespace BecquerelMonitor.EfficiencyMaker
{
    // Keep locations, not a second copy of the CAD mesh. Resolve the actual declarations
    // selected by the importer, including unnamed placements and duplicate stripped names.
    internal sealed class GdmlSourceDocument
    {
        readonly string path;
        readonly DateTime modified;
        readonly long length;

        GdmlSourceDocument(FileInfo file)
        {
            path = file.FullName;
            modified = file.LastWriteTimeUtc;
            length = file.Length;
        }

        internal static GdmlSourceDocument ForFile(string path)
        {
            if (!File.Exists(path)) return null;
            try { return new GdmlSourceDocument(new FileInfo(path)); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static long Position(IXmlLineInfo node)
        {
            return node.HasLineInfo() ? ((long)node.LineNumber << 32) | (uint)node.LinePosition : 0;
        }

        void CheckVersion()
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length != length || file.LastWriteTimeUtc != modified)
                throw new IOException(GdmlText.T("GdmlInfoSourceChanged", "The imported file has changed or is no longer available."));
        }

        internal string Read(GdmlVolumeInstance part, CancellationToken cancel)
        {
            CheckVersion();
            long[] locations = { part.VolumeSourcePosition, part.PlacementSourcePosition,
                part.Material == null ? 0 : part.Material.SourcePosition, part.Solid.SourcePosition };
            var wanted = new HashSet<long>(locations);
            wanted.Remove(0);
            var found = new Dictionary<long, string>();
            var settings = new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit };
            using (var stream = File.OpenRead(path))
            using (var xml = XmlReader.Create(stream, settings))
            {
                xml.MoveToContent();
                while (!xml.EOF && wanted.Count != 0)
                {
                    cancel.ThrowIfCancellationRequested();
                    long location = Position((IXmlLineInfo)xml);
                    if (xml.NodeType == XmlNodeType.Element && wanted.Remove(location))
                        found.Add(location, xml.ReadOuterXml());
                    else
                        xml.Read();
                }
            }
            CheckVersion();
            if (wanted.Count != 0)
                throw new IOException(GdmlText.T("GdmlInfoSourceChanged", "The imported file has changed or is no longer available."));
            var declarations = new List<string>();
            foreach (long location in locations)
                if (location != 0) declarations.Add(found[location]);
            return string.Join(Environment.NewLine + Environment.NewLine, declarations);
        }
    }
}
