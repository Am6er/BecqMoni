using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace BecquerelMonitor.EfficiencyMaker
{
    // ⚡ GPU-путь матрицы отклика (`AMBER160`, полоса П221; решения Amber 02.10.2026
    // вопросником: «float + статистика (Рекомендую)», «Только оснастка (Рекомендую)»).
    //
    // Писатель упаковки и реестр объектов данных. Сами разделы упаковки —
    // `GpuPackTables.cs` (сечения, рассеяние, релаксация, электроны, тормозное, свет) и
    // `GpuPackScene.cs` (вещества, области, источник); нативная часть —
    // `tools/effmaker/gpu` (README там же).
    //
    // ⛔ ПРЯМОЙ ДОСТУП, без отражения (`AMBER219`, П245-R; решение Amber 07.10.2026
    // вопросником, дословно: «Перевести на прямой доступ сейчас»). Закрытые члены
    // `EfficiencySimulator` и классов данных, которые читает упаковщик, открыты как
    // `internal` — менялась ТОЛЬКО видимость, тела не тронуты; переименование члена теперь
    // ловит компилятор, а не отказ при счёте. Прежний помощник отражения (наследие решения
    // «Только оснастка» 02.10.2026) живёт только в пробе `tools/effmaker/probes/GpuMatrixCheck.cs`:
    // она вне сборки и внутренних членов не видит.
    //
    // Отражение здесь осталось ОДНО и нарочно — `Settings`: перебор ОТКРЫТЫХ простых полей
    // симулятора. Это не доступ к закрытому, а самообновляющийся список настроек: новое
    // открытое поле само уходит на устройство, и натив отказывает на незнакомом имени.
    // Явный список здесь потерял бы новую настройку молча.

    /// <summary>Писатель позиционного потока упаковки; читатель — `tools/effmaker/gpu/host.h`.</summary>
    public sealed class GpuWriter
    {
        readonly MemoryStream stream = new MemoryStream();
        readonly BinaryWriter w;

        public GpuWriter()
        {
            this.w = new BinaryWriter(this.stream, new UTF8Encoding(false));
        }

        public void Tag(string name, int count)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(name);
            this.w.Write(bytes.Length);
            this.w.Write(bytes);
            this.w.Write(count);
        }

        public void Real(double v) { this.w.Write(v); }
        public void Int(int v) { this.w.Write(v); }
        public void Long(long v) { this.w.Write(v); }
        public void Bool(bool v) { this.w.Write(v ? 1 : 0); }

        public void Reals(double[] a)
        {
            if (a == null) { this.w.Write(0); return; }
            this.w.Write(a.Length);
            foreach (double v in a) this.w.Write(v);
        }

        public void Ints(int[] a)
        {
            if (a == null) { this.w.Write(0); return; }
            this.w.Write(a.Length);
            foreach (int v in a) this.w.Write(v);
        }

        public void Bytes(bool[] a)
        {
            if (a == null) { this.w.Write(0); return; }
            this.w.Write(a.Length);
            foreach (bool v in a) this.w.Write((byte)(v ? 1 : 0));
        }

        public void Jagged(double[][] a)
        {
            if (a == null) { this.w.Write(0); return; }
            this.w.Write(a.Length);
            foreach (double[] row in a)
            {
                if (row == null) { this.w.Write(0); continue; }
                this.w.Write(row.Length);
                foreach (double v in row) this.w.Write(v);
            }
        }

        /// <summary>Прямоугольный `double[,]` — как зубчатый по строкам.</summary>
        public void Rect(double[,] a)
        {
            if (a == null) { this.w.Write(0); return; }
            int rows = a.GetLength(0), cols = a.GetLength(1);
            this.w.Write(rows);
            for (int i = 0; i < rows; i++)
            {
                this.w.Write(cols);
                for (int j = 0; j < cols; j++) this.w.Write(a[i, j]);
            }
        }

        public void JaggedInts(int[][] a)
        {
            if (a == null) { this.w.Write(0); return; }
            this.w.Write(a.Length);
            foreach (int[] row in a)
            {
                if (row == null) { this.w.Write(0); continue; }
                this.w.Write(row.Length);
                foreach (int v in row) this.w.Write(v);
            }
        }

        /// <summary>Контрольное слово конца записи (сверяет `BlobReader::End`).</summary>
        public void End() { this.w.Write(unchecked((int)0x5EC710EDu)); }

        public byte[] ToArray()
        {
            this.w.Flush();
            return this.stream.ToArray();
        }
    }

    /// <summary>
    /// Реестр объектов данных одной упаковки: объект C# → индекс в таблице устройства.
    /// Сравнение — по ССЫЛКЕ (два разных вещества с одинаковым составом — два индекса,
    /// как два ключа словаря `Dictionary&lt;GeometryMaterial, …&gt;` у симулятора).
    /// </summary>
    public sealed class GpuRegistry
    {
        sealed class ByRef : IEqualityComparer<object>
        {
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        readonly Dictionary<string, List<object>> lists = new Dictionary<string, List<object>>();
        readonly Dictionary<string, Dictionary<object, int>> index = new Dictionary<string, Dictionary<object, int>>();

        /// <summary>Индекс объекта в таблице <paramref name="table"/>; null → −1; новый — дописывается.</summary>
        public int Add(string table, object o)
        {
            if (o == null) return -1;
            List<object> list;
            Dictionary<object, int> map;
            if (!this.lists.TryGetValue(table, out list))
            {
                list = new List<object>();
                map = new Dictionary<object, int>(new ByRef());
                this.lists[table] = list;
                this.index[table] = map;
            }
            else
            {
                map = this.index[table];
            }

            int i;
            if (map.TryGetValue(o, out i)) return i;
            i = list.Count;
            list.Add(o);
            map[o] = i;
            return i;
        }

        /// <summary>Индекс УЖЕ зарегистрированного объекта; незарегистрированный — отказ.</summary>
        public int Of(string table, object o)
        {
            if (o == null) return -1;
            Dictionary<object, int> map;
            int i;
            if (this.index.TryGetValue(table, out map) && map.TryGetValue(o, out i)) return i;
            throw new InvalidOperationException("GPU-упаковка: объект " + o.GetType().Name
                + " не зарегистрирован в таблице «" + table + "» до записи");
        }

        public IList<object> List(string table)
        {
            List<object> list;
            return this.lists.TryGetValue(table, out list) ? (IList<object>)list : new List<object>();
        }
    }

    /// <summary>Контекст одной упаковки: симулятор узла, реестр, писатель.</summary>
    public sealed class GpuPackContext
    {
        public readonly EfficiencySimulator Sim;
        public readonly GpuRegistry Reg = new GpuRegistry();

        /// <summary>Все Z веществ сцены (и вещества пустоты/воды для электрона), по возрастанию.</summary>
        public readonly SortedSet<int> Zs = new SortedSet<int>();

        public GpuPackContext(EfficiencySimulator sim)
        {
            this.Sim = sim;
        }
    }

    /// <summary>
    /// Сборка упаковки. Разделы пишут модули (`partial`):
    ///   `GpuPackScene.cs`  — CollectScene (регистрирует вещества и их кэши, собирает Z),
    ///                        WriteScene (вещества, флуоресценты, рассеиватели, области, сцена);
    ///   `GpuPackTables.cs` — WriteTables (элементы, флуоресценция, оболочки, релаксация,
    ///                        атомы рассеяния, ESTAR, тормозное, кривая света).
    /// Порядок разделов в потоке — порядок, в котором их читает `api.cu`.
    /// </summary>
    public static partial class GpuPack
    {
        public static byte[] Pack(EfficiencySimulator sim)
        {
            sim.EnsureBuilt();
            var ctx = new GpuPackContext(sim);
            CollectScene(ctx);
            var w = new GpuWriter();
            WriteTables(ctx, w);
            WriteScene(ctx, w);
            return w.ToArray();
        }

        /// <summary>
        /// Настройки симулятора — ВСЕ открытые простые поля, кроме счётчиков-выходов;
        /// пара «имя, значение». Натив ставит их по имени (`rm_cfg_set`) и отказывает на
        /// незнакомом имени и на неустановленном поле — ключ, которого GPU не знает, не
        /// проходит молча.
        /// </summary>
        public static List<KeyValuePair<string, double>> Settings(EfficiencySimulator sim)
        {
            var list = new List<KeyValuePair<string, double>>();
            foreach (FieldInfo f in typeof(EfficiencySimulator).GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!f.FieldType.IsPrimitive) continue;
                string n = f.Name;
                if (n.StartsWith("Count") || n.StartsWith("Sum") || n.StartsWith("Last") || n.StartsWith("Weight")) continue;
                object v = f.GetValue(sim);
                double d = v is bool ? ((bool)v ? 1.0 : 0.0) : Convert.ToDouble(v);
                list.Add(new KeyValuePair<string, double>(n, d));
            }

            return list;
        }
    }
}
