using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace BecquerelMonitor
{
    internal sealed class SpectrumReportSheet
    {
        public string Name { get; }
        public List<object[]> Rows { get; } = new List<object[]>();

        public SpectrumReportSheet(string name)
        {
            Name = name;
        }

        public void Add(params object[] cells)
        {
            Rows.Add(cells);
        }
    }

    internal static class SpectrumReportWriter
    {
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly XNamespace Spreadsheet = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        private static readonly XNamespace OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static readonly XNamespace PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
        private static readonly XNamespace ContentType = "http://schemas.openxmlformats.org/package/2006/content-types";
        private static readonly XNamespace Drawing = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        private static readonly XNamespace Art = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private const string MimePrefix = "application/vnd.openxmlformats-officedocument.";

        public static void Write(string path, bool xlsx, IList<SpectrumReportSheet> sheets, byte[] spectrumPng, byte[] fsaPng)
        {
            if (spectrumPng == null) throw new ArgumentNullException(nameof(spectrumPng));
            string destination = Path.GetFullPath(path);
            string temporary = Path.Combine(Path.GetDirectoryName(destination), Path.GetRandomFileName());
            var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            try
            {
                using (stream)
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, false, Encoding.UTF8))
                {
                    if (xlsx) WriteWorkbook(archive, sheets, spectrumPng, fsaPng);
                    else WriteCsvArchive(archive, sheets, spectrumPng, fsaPng);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        private static string Text(object value)
        {
            if (value is DateTime date) return date.ToString("yyyy-MM-dd HH:mm:ss.fff", Invariant);
            if (value is double number) return double.IsNaN(number) || double.IsInfinity(number) ? "" : number.ToString("R", Invariant);
            if (value is float single) return float.IsNaN(single) || float.IsInfinity(single) ? "" : single.ToString("R", Invariant);
            return Convert.ToString(value, Invariant);
        }

        private static void WriteCsvArchive(ZipArchive archive, IList<SpectrumReportSheet> sheets, byte[] spectrumPng, byte[] fsaPng)
        {
            for (int i = 0; i < sheets.Count; i++)
            {
                SpectrumReportSheet sheet = sheets[i];
                string basename = (i + 1).ToString("00", Invariant) + " " + sheet.Name;
                using (var writer = new StreamWriter(archive.CreateEntry(basename + ".csv").Open(), new UTF8Encoding(true)))
                {
                    writer.NewLine = "\r\n";
                    int columns = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(row => row.Length);
                    foreach (object[] row in sheet.Rows)
                    {
                        var fields = new string[columns];
                        for (int c = 0; c < columns; c++)
                            fields[c] = CsvField(c < row.Length ? row[c] : null);
                        writer.WriteLine(string.Join(",", fields));
                    }
                }
                byte[] image = ImageForSheet(i, spectrumPng, fsaPng);
                if (image != null) WriteImage(archive, basename + ".png", image);
            }
        }

        private static string CsvField(object value)
        {
            string text = Text(value);
            if (value is string)
            {
                // CSV has no cell types. Escape spreadsheet formula prefixes in imported
                // labels and notes. Escape an existing leading apostrophe as well, so
                // a CSV reader can recover the exact text by removing one apostrophe.
                string first = text.TrimStart();
                if (text.StartsWith("'", StringComparison.Ordinal) ||
                    (first.Length > 0 && "=+-@".IndexOf(first[0]) >= 0))
                    text = "'" + text;
            }
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static byte[] ImageForSheet(int index, byte[] spectrumPng, byte[] fsaPng)
        {
            if (index == 0) return spectrumPng;
            return index == 7 ? fsaPng : null;
        }

        private static void WriteWorkbook(ZipArchive archive, IList<SpectrumReportSheet> sheets, byte[] spectrumPng, byte[] fsaPng)
        {
            var types = new XElement(ContentType + "Types",
                DefaultType("rels", "application/vnd.openxmlformats-package.relationships+xml"),
                DefaultType("png", "image/png"),
                OverrideType("xl/workbook.xml", "spreadsheetml.sheet.main+xml"),
                OverrideType("xl/styles.xml", "spreadsheetml.styles+xml"));
            SaveXml(archive, "_rels/.rels", Relationships(Relation("book", "officeDocument", "xl/workbook.xml")));
            SaveXml(archive, "xl/styles.xml", XElement.Parse(Styles));
            var links = Relationships(Relation("styles", "styles", "styles.xml"));
            var tabs = new XElement(Spreadsheet + "sheets");

            for (int i = 0; i < sheets.Count; i++)
            {
                SpectrumReportSheet sheet = sheets[i];
                string id = (i + 1).ToString(Invariant);
                string xml = "sheet" + id + ".xml";
                string name = sheet.Name.Length > 31 ? sheet.Name.Substring(0, 31) : sheet.Name;
                tabs.Add(new XElement(Spreadsheet + "sheet", new XAttribute("name", name),
                    new XAttribute("sheetId", id), new XAttribute(OfficeRel + "id", "s" + id)));
                links.Add(Relation("s" + id, "worksheet", "worksheets/" + xml));
                types.Add(OverrideType("xl/worksheets/" + xml, "spreadsheetml.worksheet+xml"));
                byte[] png = ImageForSheet(i, spectrumPng, fsaPng);
                SaveXml(archive, "xl/worksheets/" + xml, Worksheet(sheet, png != null, i == 1));
                if (png == null) continue;

                string drawing = "drawing" + id + ".xml";
                string image = "image" + id + ".png";
                types.Add(OverrideType("xl/drawings/" + drawing, "drawing+xml"));
                SaveXml(archive, "xl/worksheets/_rels/" + xml + ".rels",
                    Relationships(Relation("plot", "drawing", "../drawings/" + drawing)));
                SaveXml(archive, "xl/drawings/_rels/" + drawing + ".rels",
                    Relationships(Relation("image", "image", "../media/" + image)));
                SaveXml(archive, "xl/drawings/" + drawing, Picture(sheet, png));
                WriteImage(archive, "xl/media/" + image, png);
            }

            SaveXml(archive, "xl/workbook.xml", new XElement(Spreadsheet + "workbook",
                new XAttribute(XNamespace.Xmlns + "r", OfficeRel), tabs));
            SaveXml(archive, "xl/_rels/workbook.xml.rels", links);
            SaveXml(archive, "[Content_Types].xml", types);
        }

        private static XElement Worksheet(SpectrumReportSheet sheet, bool hasImage, bool filterResults)
        {
            var table = new XElement(Spreadsheet + "sheetData");
            int columns = sheet.Rows.Count == 0 ? 0 : sheet.Rows.Max(row => row.Length);
            var widths = new int[columns];
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                object[] values = sheet.Rows[r];
                var row = new XElement(Spreadsheet + "row", new XAttribute("r", r + 1));
                for (int c = 0; c < values.Length; c++)
                {
                    object value = values[c];
                    string text = Text(value);
                    if (text.Length == 0) continue;
                    widths[c] = Math.Max(widths[c], Math.Min(64, text.Split('\n').Max(line => line.Length) + 3));
                    row.Add(Cell(ColumnName(c) + (r + 1).ToString(Invariant), value, text, r == 0));
                }
                table.Add(row);
            }
            var sheetView = new XElement(Spreadsheet + "sheetView", new XAttribute("workbookViewId", 0),
                new XElement(Spreadsheet + "pane", new XAttribute("ySplit", 1), new XAttribute("topLeftCell", "A2"),
                    new XAttribute("activePane", "bottomLeft"), new XAttribute("state", "frozen")));
            var layout = new XElement(Spreadsheet + "worksheet", new XAttribute(XNamespace.Xmlns + "r", OfficeRel),
                new XElement(Spreadsheet + "sheetViews", sheetView));
            if (columns > 0)
            {
                layout.Add(new XElement(Spreadsheet + "cols", widths.Select((width, c) =>
                    new XElement(Spreadsheet + "col", new XAttribute("min", c + 1), new XAttribute("max", c + 1),
                        new XAttribute("width", Math.Max(12, width)), new XAttribute("customWidth", 1)))));
            }
            layout.Add(table);
            if (filterResults && sheet.Rows.Count > 1 && sheet.Rows.All(row => row.Length == columns))
                layout.Add(new XElement(Spreadsheet + "autoFilter",
                    new XAttribute("ref", "A1:" + ColumnName(columns - 1) + sheet.Rows.Count.ToString(Invariant))));
            if (hasImage) layout.Add(new XElement(Spreadsheet + "drawing", new XAttribute(OfficeRel + "id", "plot")));
            return layout;
        }

        private static string ColumnName(int column)
        {
            string name = "";
            for (int n = column + 1; n > 0; n = (n - 1) / 26)
                name = (char)('A' + (n - 1) % 26) + name;
            return name;
        }

        // SpreadsheetML's escaped-string notation also needs to protect literal "_xHHHH_" text.
        private static readonly Regex ExcelEscape = new Regex(@"_x[\da-fA-F]{4}_|[\x00-\x08\x0b-\x0d\x0e-\x1f\ufffe\uffff]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static XElement Cell(string address, object value, string text, bool header)
        {
            var cell = new XElement(Spreadsheet + "c", new XAttribute("r", address));
            if (value is DateTime date && date >= new DateTime(1900, 3, 1))
            {
                cell.Add(new XAttribute("s", 2), new XElement(Spreadsheet + "v", date.ToOADate().ToString("R", Invariant)));
            }
            else if (value is bool boolean)
            {
                cell.Add(new XAttribute("t", "b"), new XElement(Spreadsheet + "v", boolean ? "1" : "0"));
            }
            else if (Type.GetTypeCode(value.GetType()) >= TypeCode.SByte && Type.GetTypeCode(value.GetType()) <= TypeCode.Decimal)
            {
                cell.Add(new XElement(Spreadsheet + "v", text));
            }
            else
            {
                string escaped = ExcelEscape.Replace(text, m => m.Length > 1 ? "_x005F_" + m.Value.Substring(1) :
                    "_x" + ((int)m.Value[0]).ToString("X4", Invariant) + "_");
                cell.Add(new XAttribute("t", "inlineStr"), new XAttribute("s", header ? 1 : 3),
                    new XElement(Spreadsheet + "is", new XElement(Spreadsheet + "t",
                        new XAttribute(XNamespace.Xml + "space", "preserve"), escaped)));
            }
            return cell;
        }

        // Place the graphic after the table. Pixel-to-EMU conversion at 96 dpi preserves its aspect ratio.
        private static XElement Picture(SpectrumReportSheet sheet, byte[] png)
        {
            long cx, cy;
            using (var input = new MemoryStream(png, false))
            using (var bitmap = Image.FromStream(input))
            {
                cx = bitmap.Width * 9525L;
                cy = bitmap.Height * 9525L;
            }
            var picture = new XElement(Drawing + "pic",
                new XElement(Drawing + "nvPicPr",
                    new XElement(Drawing + "cNvPr", new XAttribute("id", 1), new XAttribute("name", sheet.Name)),
                    new XElement(Drawing + "cNvPicPr", new XElement(Art + "picLocks", new XAttribute("noChangeAspect", 1)))),
                new XElement(Drawing + "blipFill", new XElement(Art + "blip", new XAttribute(OfficeRel + "embed", "image")),
                    new XElement(Art + "stretch", new XElement(Art + "fillRect"))),
                new XElement(Drawing + "spPr",
                    new XElement(Art + "xfrm", new XElement(Art + "off", new XAttribute("x", 0), new XAttribute("y", 0)),
                        new XElement(Art + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy))),
                    new XElement(Art + "prstGeom", new XAttribute("prst", "rect"), new XElement(Art + "avLst"))));
            return new XElement(Drawing + "wsDr",
                new XAttribute(XNamespace.Xmlns + "xdr", Drawing), new XAttribute(XNamespace.Xmlns + "a", Art),
                new XAttribute(XNamespace.Xmlns + "r", OfficeRel),
                new XElement(Drawing + "oneCellAnchor",
                    new XElement(Drawing + "from", new XElement(Drawing + "col", 0), new XElement(Drawing + "colOff", 0),
                        new XElement(Drawing + "row", sheet.Rows.Count + 1), new XElement(Drawing + "rowOff", 0)),
                    new XElement(Drawing + "ext", new XAttribute("cx", cx), new XAttribute("cy", cy)),
                    picture, new XElement(Drawing + "clientData")));
        }

        private static XElement DefaultType(string extension, string mime)
        {
            return new XElement(ContentType + "Default", new XAttribute("Extension", extension), new XAttribute("ContentType", mime));
        }

        private static XElement OverrideType(string part, string type)
        {
            return new XElement(ContentType + "Override", new XAttribute("PartName", "/" + part), new XAttribute("ContentType", MimePrefix + type));
        }

        private static XElement Relationships(XElement relation)
        {
            return new XElement(PackageRel + "Relationships", relation);
        }

        private static XElement Relation(string id, string type, string target)
        {
            return new XElement(PackageRel + "Relationship", new XAttribute("Id", id),
                new XAttribute("Type", OfficeRel.NamespaceName + "/" + type), new XAttribute("Target", target));
        }

        private static void SaveXml(ZipArchive archive, string path, XElement root)
        {
            using (Stream entry = archive.CreateEntry(path).Open()) root.Save(entry, SaveOptions.DisableFormatting);
        }

        private static void WriteImage(ZipArchive archive, string path, byte[] png)
        {
            using (Stream entry = archive.CreateEntry(path, CompressionLevel.NoCompression).Open())
                entry.Write(png, 0, png.Length);
        }

        private const string Styles = @"<styleSheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'>
  <numFmts count='1'><numFmt numFmtId='164' formatCode='yyyy-mm-dd hh:mm:ss.000'/></numFmts>
  <fonts count='2'><font><sz val='11'/><name val='Calibri'/></font><font><b/><color rgb='FFFFFFFF'/><sz val='11'/><name val='Calibri'/></font></fonts>
  <fills count='3'><fill><patternFill patternType='none'/></fill><fill><patternFill patternType='gray125'/></fill><fill><patternFill patternType='solid'><fgColor rgb='FF264961'/><bgColor indexed='64'/></patternFill></fill></fills>
  <borders count='1'><border><left/><right/><top/><bottom/><diagonal/></border></borders>
  <cellStyleXfs count='1'><xf numFmtId='0' fontId='0' fillId='0' borderId='0'/></cellStyleXfs>
  <cellXfs count='4'>
    <xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0'/>
    <xf numFmtId='0' fontId='1' fillId='2' borderId='0' xfId='0' applyFont='1' applyFill='1' applyAlignment='1'><alignment vertical='center' wrapText='1'/></xf>
    <xf numFmtId='164' fontId='0' fillId='0' borderId='0' xfId='0' applyNumberFormat='1'/>
    <xf numFmtId='0' fontId='0' fillId='0' borderId='0' xfId='0' applyAlignment='1'><alignment vertical='top' wrapText='1'/></xf>
  </cellXfs>
  <cellStyles count='1'><cellStyle name='Normal' xfId='0' builtinId='0'/></cellStyles>
</styleSheet>";
    }
}
