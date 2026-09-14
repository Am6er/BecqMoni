import io
p='tools/effmaker/probes/CorpusFsaProbe.cs'
raw=io.open(p,'rb').read()
s=raw.decode('utf-8').replace('\r\n','\n')
a="""        /// <summary>Разбор строки CSV с кавычками (поле `library_note` несёт запятые).</summary>
        static string[] SplitCsv(string line)
        {
            var cells = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false;
            foreach (char c in line)
            {
                if (c == '"')
                {
                    quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    cells.Add(cur.ToString());
                    cur.Length = 0;
                }
                else
                {
                    cur.Append(c);
                }
            }

            cells.Add(cur.ToString());
            return cells.ToArray();
        }

"""
assert s.count(a)==1
s=s.replace(a,"")
a="""                    string[] cells = SplitCsv(lines[i]);
                    if (cells.Length <= Math.Max(iSpec, iEps))
"""
b="""                    List<string> cells = SplitCsv(lines[i]);
                    if (cells.Count <= Math.Max(iSpec, iEps))
"""
assert s.count(a)==1
s=s.replace(a,b)
io.open(p,'wb').write(s.replace('\n','\r\n').encode('utf-8'))
print('ok')
