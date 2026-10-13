using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// (`AMBER213`, П241) ПОЛНЫЙ СНИМОК РЕЗУЛЬТАТА РАЗБОРА — побитовая мерка «до/после».
///
/// Довесок (без <c>Main</c>): кладётся всем пробам. Объект обходится отражением:
/// все поля экземпляра (открытые и закрытые, по имени объявившего класса и поля),
/// коллекции — по порядку перечисления, словари — парами; числа с плавающей
/// точкой — `R` и восемь байт битов, то есть совпадение снимков = совпадение
/// каждого числа до последнего бита. Повторная ссылка печатается путём первой
/// встречи (циклы). Объекты чужих сборок (не `BecquerelMonitor*`) — именем типа.
/// Читатели: <c>FsaSpeedProbeP241</c>, ключ <c>--dump-bits=</c> у
/// <c>CorpusFsaProbe</c>.
/// </summary>
static class FsaResultDump
{
    public static string Of(object result)
    {
        var sb = new StringBuilder();
        new Dumper(sb).Dump("result", result, 0);
        return sb.ToString();
    }

    public static string Sha(string text)
    {
        using (SHA256 h = SHA256.Create())
        {
            byte[] b = h.ComputeHash(Encoding.UTF8.GetBytes(text));
            return BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
        }
    }

    /// <summary>Число снимка: `R` и биты (16 шестнадцатеричных знаков).</summary>
    public static string D(double v)
    {
        return v.ToString("R", CultureInfo.InvariantCulture) + " | d:"
               + BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
    }

    sealed class Dumper
    {
        readonly StringBuilder sb;
        readonly Dictionary<object, string> seen = new Dictionary<object, string>(new RefEq());

        public Dumper(StringBuilder sb) { this.sb = sb; }

        sealed class RefEq : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return RuntimeHelpers.GetHashCode(o); }
        }

        void Line(string path, string value)
        {
            this.sb.Append(path).Append(" = ").Append(value).Append('\n');
        }

        public void Dump(string path, object o, int depth)
        {
            if (o == null) { Line(path, "null"); return; }
            Type t = o.GetType();
            if (o is double) { Line(path, D((double)o)); return; }
            if (o is float) { Line(path, D((float)o)); return; }
            if (o is string) { Line(path, "\"" + (string)o + "\""); return; }
            if (t.IsPrimitive || t.IsEnum || o is decimal || o is DateTime || o is TimeSpan || o is Guid)
            {
                Line(path, Convert.ToString(o, CultureInfo.InvariantCulture));
                return;
            }
            if (o is Delegate || o is Type || o is MemberInfo || o is System.Threading.WaitHandle) { Line(path, "<" + t.Name + ">"); return; }
            if (!t.IsValueType)
            {
                string first;
                if (this.seen.TryGetValue(o, out first)) { Line(path, "@" + first); return; }
                this.seen[o] = path;
            }
            if (depth > 40) { Line(path, "<глубже 40>"); return; }
            IDictionary dict = o as IDictionary;
            if (dict != null)
            {
                Line(path, t.Name + " count=" + dict.Count);
                int k = 0;
                foreach (DictionaryEntry e in dict)
                {
                    Dump(path + "{" + k + "}.key", e.Key, depth + 1);
                    Dump(path + "{" + k + "}.value", e.Value, depth + 1);
                    k++;
                }
                return;
            }
            IEnumerable seq = o as IEnumerable;
            if (seq != null)
            {
                var items = new List<object>();
                foreach (object item in seq) items.Add(item);
                Line(path, t.Name + " count=" + items.Count);
                for (int k = 0; k < items.Count; k++)
                {
                    Dump(path + "[" + k + "]", items[k], depth + 1);
                }
                return;
            }
            string ns = t.Namespace ?? "";
            if (!ns.StartsWith("BecquerelMonitor", StringComparison.Ordinal) && !t.IsValueType)
            {
                Line(path, "<" + t.FullName + ">");
                return;
            }
            Line(path, "{" + t.Name + "}");
            var fields = new List<FieldInfo>();
            for (Type c = t; c != null && c != typeof(object); c = c.BaseType)
            {
                fields.AddRange(c.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly));
            }
            foreach (FieldInfo f in fields.OrderBy(f => f.DeclaringType.Name + "." + f.Name, StringComparer.Ordinal))
            {
                if (typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                Dump(path + "." + f.Name, f.GetValue(o), depth + 1);
            }
        }
    }
}
