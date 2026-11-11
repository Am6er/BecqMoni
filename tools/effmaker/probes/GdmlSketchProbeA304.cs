using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Resources;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace GdmlSketchProbeA304
{
    /// <summary>
    /// ПРИЁМКА `AMBER292` (чертёж) и `AMBER304` (чертёж, форма, панель, ключи), 05.11.2026.
    /// (`AMBER324`, 08.11.2026) Все числа горизонтальны: заслон ГОСТ отказывает повёрнутому числу размера
    /// (рамка выше своей ширины), у вертикального размера число стоит слева от линии; контроль — прошлая раскладка.
    ///
    ///     gdmlsketchprobea304 [--data=&lt;каталог&gt;] [--real=&lt;каталог&gt;] [--more=&lt;каталог&gt;]...
    ///                         [--tmp=&lt;каталог&gt;] [--shots=&lt;каталог&gt;]
    ///
    ///   --data  входы ревизии, по умолчанию `..\data\gdml_a303` от каталога пробы;
    ///   --real  настоящие файлы (12 МБ, в git их нет): пять профилей GS2020;
    ///   --more  ещё каталоги *.gdml, только на падение;
    ///   --heavy ещё каталоги *.gdml: каждый файл отдельным процессом, код и время отрисовки;
    ///   --tmp   куда писать свои входы, по умолчанию `tmp_a304` рядом с пробой;
    ///   --shots снимки 1400×700, на диск, не в git;
    ///
    /// ⛔ ОКОН НЕТ: `OnPaint` зовётся отражением на `Graphics` от `Bitmap`, форма и панель не
    /// показываются, `TryCommit` не зовётся (он показал бы окно сообщения).
    ///
    /// (1) Падения. Все входы × 16 размеров окна: исключения нет, и ПОСЛЕДНИЙ ЗАСЛОН НЕ СРАБОТАЛ:
    /// защита — данные, а не `try`. Положительный контроль заслона — null среди томов.
    /// (2) Сетка: хорды нет. (3) Профиль в порядке файла, площадь. (4) Частные случаи: ось y,
    /// поворот, смещение, неполный оборот, незнакомые тела, вычитание. (5) Форма: битый файл, импорт
    /// при счёте. (6) Панель и график. (7) Ключи: у каждого `Gdml*` есть пара, плейсхолдеры те же.
    ///
    /// (`AMBER301`) Оформление числом: подписи без наложения и вне чертежа, выноска в своё тело, разные
    /// цвета веществ, легенда и сводка против чисел читателя, шов и контур составного тела (Н-А критика
    /// `critGdml5`), заливка замкнутых сеток. Сверка заливки с настоящим Geant4 — `--locate`, журнал
    /// `handover-2026-11-05-design-gdml.md`.
    ///
    /// (8) Глубина и работа: цепочки вычитаний и общие тела — ОТДЕЛЬНЫМ ПРОЦЕССОМ: переполнение стека
    /// `try` не ловит и уносит весь процесс. Положительный контроль — заведомое переполнение в
    /// дочернем процессе, родитель обязан его увидеть.
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ», код 0; есть расхождение — код 1; ключи и файлы — код 2.
    /// Положительный контроль всей пробы — мутанты, журнал `handover-2026-11-05-fixgdml-b.md`.
    /// </summary>
    static class Program
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Color Canvas = Color.FromArgb(0xD6, 0xD2, 0xC4);

        static readonly Size[] Sizes =
        {
            new Size(0, 0), new Size(1, 1), new Size(8, 8), new Size(16, 16), new Size(17, 17), new Size(24, 24),
            new Size(32, 32), new Size(33, 33), new Size(40, 40), new Size(49, 49), new Size(50, 20), new Size(20, 300),
            new Size(50, 50), new Size(100, 50), new Size(300, 200), new Size(1400, 700),
        };

        static int bad;
        static string data, real, tmp, shots;
        static readonly List<string> more = new List<string>();
        static readonly List<string> heavy = new List<string>();

        [DllImport("kernel32.dll")] static extern uint SetErrorMode(uint mode);
        [DllImport("kernel32.dll")] static extern int WerSetFlags(uint flags);

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            if (args.Length >= 1 && args[0].StartsWith("--paint-one=", StringComparison.Ordinal))
            {
                return PaintOne(args[0].Substring(12));
            }

            if (args.Length >= 1 && args[0].StartsWith("--collect-one=", StringComparison.Ordinal))
            {
                return CollectOne(args[0].Substring(14));
            }

            if (args.Length >= 1 && args[0] == "--crash-control")
            {
                SetErrorMode(0x8003);
                WerSetFlags(0x20);
                return Overflow(0);
            }

            string here = AppDomain.CurrentDomain.BaseDirectory;
            data = Path.Combine(here, "..", "data", "gdml_a303");
            tmp = Path.Combine(here, "tmp_a304");
            foreach (string a in args)
            {
                if (a.StartsWith("--data=", StringComparison.Ordinal)) data = a.Substring(7);
                else if (a.StartsWith("--real=", StringComparison.Ordinal)) real = a.Substring(7);
                else if (a.StartsWith("--column=", StringComparison.Ordinal)) column = a.Substring(9);
                else if (a.StartsWith("--more=", StringComparison.Ordinal)) more.Add(a.Substring(7));
                else if (a.StartsWith("--heavy=", StringComparison.Ordinal)) heavy.Add(a.Substring(8));
                else if (a.StartsWith("--tmp=", StringComparison.Ordinal)) tmp = a.Substring(6);
                else if (a.StartsWith("--shots=", StringComparison.Ordinal)) shots = a.Substring(8);
                else { Console.Error.WriteLine("неизвестный ключ: " + a); return 2; }
            }

            if (!Directory.Exists(data))
            {
                Console.Error.WriteLine("нет каталога входов: " + data);
                return 2;
            }

            Directory.CreateDirectory(tmp);
            if (shots != null) Directory.CreateDirectory(shots);
            Console.WriteLine("сборка приложения: {0}", typeof(GdmlSketch).Assembly.Location);

            WriteInlineCases();
            GostControl();
            Crash();
            Barrier();
            Mesh();
            Profiles();
            SpecialCases();
            Subtraction();
            NewBodies();
            Seams();
            Look();
            Form();
            Panel();
            Keys();
            Deep();

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЕ СОШЛИСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------
        //  Свои входы: чего нет в наборе чтения и что читатель пропускает
        // ------------------------------------------------------------------

        static string Inline(string name)
        {
            return Path.Combine(tmp, name + ".gdml");
        }

        /// <summary>Файл с миром и томами: solids — свои тела, volumes — свои тома, physvol — их размещение.</summary>
        static void Case(string name, string define, string solids, string volumes, string physvol)
        {
            File.WriteAllText(Inline(name),
                "<?xml version=\"1.0\"?><gdml><define>" + define + "</define><solids>"
                + "<box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/>" + solids + "</solids><structure>" + volumes
                + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>" + physvol + "</volume></structure></gdml>");
        }

        static string Vol(string name, string solid, string material)
        {
            return "<volume name=\"" + name + "\"><materialref ref=\"" + material + "\"/><solidref ref=\"" + solid + "\"/></volume>";
        }

        static string Phys(string name, string inner)
        {
            return "<physvol name=\"pv_" + name + "\"><volumeref ref=\"" + name + "\"/>" + inner + "</physvol>";
        }

        const string Cylinder = "<polycone name=\"Pc\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"-50\" rmin=\"0\" rmax=\"5\"/><zplane z=\"50\" rmin=\"0\" rmax=\"5\"/></polycone>";

        static string Corners(string prefix, double half)
        {
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < 8; i++)
            {
                text.AppendFormat(CultureInfo.InvariantCulture, "<position name=\"{0}{1}\" x=\"{2}\" y=\"{3}\" z=\"{4}\"/>", prefix, i, (i & 1) == 0 ? -half : half, (i & 2) == 0 ? -half : half, (i & 4) == 0 ? -half : half);
            }

            return text.ToString();
        }

        static string Tri(string a, string b, string c)
        {
            return "<triangular vertex1=\"" + a + "\" vertex2=\"" + b + "\" vertex3=\"" + c + "\"/>";
        }

        /// <summary>Грани куба наружу; inward — внутрь: полость в теле.</summary>
        static string CubeFaces(string p, bool inward)
        {
            int[][] quads =
            {
                new[] { 0, 2, 3, 1 }, new[] { 4, 5, 7, 6 }, new[] { 0, 1, 5, 4 },
                new[] { 2, 6, 7, 3 }, new[] { 0, 4, 6, 2 }, new[] { 1, 3, 7, 5 },
            };
            StringBuilder text = new StringBuilder();
            foreach (int[] q in quads)
            {
                if (inward)
                {
                    text.Append(Tri(p + q[0], p + q[2], p + q[1])).Append(Tri(p + q[0], p + q[3], p + q[2]));
                }
                else
                {
                    text.Append(Tri(p + q[0], p + q[1], p + q[2])).Append(Tri(p + q[0], p + q[2], p + q[3]));
                }
            }

            return text.ToString();
        }

        static void WriteInlineCases()
        {
            // Входы критика, у которых числа конечны или читатель их теперь не пропускает.
            string a = Vol("A", "Pc", "G4_Al"), pa = Phys("A", "");
            Case("c1_nan_vertex", "<position name=\"v1\" x=\"NaN\" y=\"0\" z=\"0\"/><position name=\"v2\" x=\"1\" y=\"0\" z=\"0\"/><position name=\"v3\" x=\"0\" y=\"1\" z=\"0\"/><position name=\"v4\" x=\"0\" y=\"0\" z=\"1\"/>",
                 "<tessellated name=\"Pc\"><triangular vertex1=\"v1\" vertex2=\"v2\" vertex3=\"v3\"/><triangular vertex1=\"v1\" vertex2=\"v3\" vertex3=\"v4\"/><triangular vertex1=\"v1\" vertex2=\"v4\" vertex3=\"v2\"/><triangular vertex1=\"v2\" vertex2=\"v4\" vertex3=\"v3\"/></tessellated>", a, pa);
            Case("c1b_1e39_vertex", "<position name=\"v1\" x=\"1e39\" y=\"0\" z=\"0\"/><position name=\"v2\" x=\"0\" y=\"1e39\" z=\"0\"/><position name=\"v3\" x=\"0\" y=\"0\" z=\"1e39\"/><position name=\"v4\" x=\"0\" y=\"0\" z=\"0\"/>",
                 "<tessellated name=\"Pc\"><triangular vertex1=\"v1\" vertex2=\"v2\" vertex3=\"v3\"/><triangular vertex1=\"v1\" vertex2=\"v3\" vertex3=\"v4\"/><triangular vertex1=\"v1\" vertex2=\"v4\" vertex3=\"v2\"/><triangular vertex1=\"v2\" vertex2=\"v4\" vertex3=\"v3\"/></tessellated>", a, pa);
            Case("c2_rot_nan", "", Cylinder, a, Phys("A", "<rotation name=\"r\" x=\"NaN\" y=\"0\" z=\"0\" unit=\"deg\"/>"));
            Case("c2b_rot_big", "", Cylinder, a, Phys("A", "<rotation name=\"r\" x=\"1e300\" y=\"0\" z=\"0\" unit=\"deg\"/>"));
            Case("c3_gp_nan", "", "<genericPolycone name=\"Pc\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><rzpoint r=\"NaN\" z=\"0\"/><rzpoint r=\"5\" z=\"0\"/><rzpoint r=\"5\" z=\"5\"/></genericPolycone>", a, pa);
            Case("16f_pc_plus_hugebox", "", Cylinder + "<box name=\"Hb\" x=\"1e300\" y=\"1e300\" z=\"1e300\" lunit=\"mm\"/>", a + Vol("H", "Hb", "G4_Fe"), pa + Phys("H", ""));
            Case("16g_box_1e38", "", "<box name=\"Pc\" x=\"1e38\" y=\"10\" z=\"10\" lunit=\"mm\"/>", a, pa);
            Case("16h_box_1e39", "", "<box name=\"Pc\" x=\"1e39\" y=\"10\" z=\"10\" lunit=\"mm\"/>", a, pa);
            Case("pos_1e308", "<position name=\"far\" x=\"1.7e308\" y=\"1.7e308\" z=\"1.7e308\"/><position name=\"near\" x=\"-1.7e308\" y=\"-1.7e308\" z=\"-1.7e308\"/>",
                 Cylinder, a + Vol("B", "Pc", "G4_Fe"), Phys("A", "<positionref ref=\"far\"/>") + Phys("B", "<positionref ref=\"near\"/>"));
            Case("tiny_alone", "", "<box name=\"Pc\" x=\"1e-5\" y=\"1e-5\" z=\"1e-5\" lunit=\"mm\"/>", a, pa);
            Case("no_placement", "", Cylinder, a, Phys("A", "<positionref ref=\"missing\"/>"));
            Case("no_placement_mix", "", "<box name=\"Pc\" x=\"100\" y=\"100\" z=\"100\" lunit=\"mm\"/><box name=\"Core\" x=\"4\" y=\"4\" z=\"4\" lunit=\"mm\"/>",
                 a + Vol("B", "Core", "G4_Pb"), pa + Phys("B", "<positionref ref=\"missing\"/>"));

            // Предел томов: 2^17 копий, читатель не строит ничего, причина в сводке.
            StringBuilder levels = new StringBuilder();
            string inner = "A";
            for (int level = 0; level < 17; level++)
            {
                string next = "L" + level.ToString(CultureInfo.InvariantCulture);
                levels.Append("<volume name=\"" + next + "\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>" + Phys(inner, "") + Phys(inner, "").Replace("pv_", "pw_") + "</volume>");
                inner = next;
            }

            Case("too_many", "", Cylinder, a + levels, Phys(inner, ""));

            // Тело вращения: ось вдоль y, косо, вдали от нуля.
            Case("pc_rot_x", "", Cylinder, a, Phys("A", "<rotation name=\"r\" x=\"90\" y=\"0\" z=\"0\" unit=\"deg\"/>"));
            Case("pc_oblique", "", Cylinder, a, Phys("A", "<rotation name=\"r\" x=\"0\" y=\"45\" z=\"0\" unit=\"deg\"/>"));
            Case("pc_far_y", "<position name=\"far\" x=\"0\" y=\"40\" z=\"0\"/>", Cylinder, a, Phys("A", "<positionref ref=\"far\"/>"));
            Case("partial_offset", "<position name=\"far\" x=\"0\" y=\"10\" z=\"0\"/>",
                 "<polycone name=\"Pc\" startphi=\"0\" deltaphi=\"90\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"0\" rmin=\"0\" rmax=\"20\"/><zplane z=\"50\" rmin=\"0\" rmax=\"20\"/></polycone>",
                 a, Phys("A", "<positionref ref=\"far\"/>"));

            // Вершина профиля ровно на плоскости торца: два равных радиуса сбивали пары колец.
            Case("gp_vertex_on_plane", "",
                 "<genericPolycone name=\"Pc\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><rzpoint r=\"10\" z=\"-10\"/><rzpoint r=\"20\" z=\"-10\"/><rzpoint r=\"30\" z=\"0\"/><rzpoint r=\"20\" z=\"10\"/><rzpoint r=\"10\" z=\"10\"/><rzpoint r=\"5\" z=\"0\"/></genericPolycone>",
                 a, pa);

            // Объединение: бюджет вершин и глубина. Каждое тело на 1000 плоскостей даёт 8000 вершин.
            System.Text.StringBuilder bigPlanes = new System.Text.StringBuilder();
            for (int i = 0; i <= 1000; i++)
            {
                bigPlanes.Append("<zplane z=\"" + i.ToString(CultureInfo.InvariantCulture) + "\" rmin=\"0\" rmax=\"10\"/>");
            }

            Case("union_big", "",
                 "<polycone name=\"U1\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\">" + bigPlanes + "</polycone>"
                 + "<polycone name=\"U2\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\">" + bigPlanes + "</polycone>"
                 + "<union name=\"Pc\"><first ref=\"U1\"/><second ref=\"U2\"/><position name=\"q\" x=\"5\"/></union>", a, pa);
            System.Text.StringBuilder unionChain = new System.Text.StringBuilder("<box name=\"C0\" x=\"10\" y=\"10\" z=\"10\"/>");
            for (int i = 1; i <= 300; i++)
            {
                string me = i == 300 ? "Pc" : "C" + i.ToString(CultureInfo.InvariantCulture);
                unionChain.Append("<union name=\"" + me + "\"><first ref=\"C" + (i - 1).ToString(CultureInfo.InvariantCulture) + "\"/><second ref=\"C0\"/></union>");
            }

            Case("union_chain", "", unionChain.ToString(), a, pa);

            // Вычитание.
            string b1 = "<box name=\"B1\" x=\"20\" y=\"20\" z=\"20\" lunit=\"mm\"/>";
            Case("sub_offset", "<position name=\"off\" x=\"5\" y=\"0\" z=\"0\"/>",
                 b1 + "<box name=\"B2\" x=\"4\" y=\"4\" z=\"40\" lunit=\"mm\"/><subtraction name=\"Pc\"><first ref=\"B1\"/><second ref=\"B2\"/><positionref ref=\"off\"/></subtraction>", a, pa);
            Case("sub_protrude", "",
                 b1 + "<box name=\"B2\" x=\"40\" y=\"4\" z=\"4\" lunit=\"mm\"/><subtraction name=\"Pc\"><first ref=\"B1\"/><second ref=\"B2\"/></subtraction><box name=\"Wide\" x=\"60\" y=\"60\" z=\"60\" lunit=\"mm\"/>",
                 a + Vol("W", "Wide", "G4_AIR"), Phys("W", "") + pa);
            Case("sub_sibling", "",
                 b1 + "<box name=\"B2\" x=\"10\" y=\"10\" z=\"40\" lunit=\"mm\"/><subtraction name=\"Pc\"><first ref=\"B1\"/><second ref=\"B2\"/></subtraction><box name=\"Core\" x=\"4\" y=\"4\" z=\"4\" lunit=\"mm\"/>",
                 a + Vol("S", "Core", "G4_Pb"), Phys("S", "") + pa);
            File.WriteAllText(Inline("broken"), "<?xml version=\"1.0\"?><gdml><solids>");

            // Положение первого тела, сетка вторым телом, знак оси, грань в плоскости, допуск на поворот.
            Case("sub_first", "", "<box name=\"A0\" x=\"100\" y=\"100\" z=\"100\" lunit=\"mm\"/><box name=\"B0\" x=\"20\" y=\"20\" z=\"200\" lunit=\"mm\"/><subtraction name=\"Pc\"><first ref=\"A0\"/><second ref=\"B0\"/><firstposition name=\"fp\" x=\"10\" y=\"0\" z=\"0\" unit=\"mm\"/></subtraction>", a, pa);
            Case("sub_mesh", "<position name=\"v1\" x=\"-5\" y=\"-5\" z=\"-5\"/><position name=\"v2\" x=\"5\" y=\"-5\" z=\"-5\"/><position name=\"v3\" x=\"0\" y=\"5\" z=\"-5\"/><position name=\"v4\" x=\"0\" y=\"0\" z=\"5\"/>",
                 "<box name=\"A0\" x=\"40\" y=\"40\" z=\"40\" lunit=\"mm\"/><tessellated name=\"B0\"><triangular vertex1=\"v1\" vertex2=\"v2\" vertex3=\"v3\"/><triangular vertex1=\"v1\" vertex2=\"v3\" vertex3=\"v4\"/><triangular vertex1=\"v1\" vertex2=\"v4\" vertex3=\"v2\"/><triangular vertex1=\"v2\" vertex2=\"v4\" vertex3=\"v3\"/></tessellated><subtraction name=\"Pc\"><first ref=\"A0\"/><second ref=\"B0\"/></subtraction>", a, pa);
            string stepped = "<polycone name=\"Pc\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"-10\" rmin=\"0\" rmax=\"20\"/><zplane z=\"0\" rmin=\"0\" rmax=\"20\"/><zplane z=\"1\" rmin=\"0\" rmax=\"5\"/><zplane z=\"30\" rmin=\"0\" rmax=\"5\"/></polycone>";
            Case("pc_flip", "", stepped, a, Phys("A", "<rotation name=\"r\" x=\"180\" y=\"0\" z=\"0\" unit=\"deg\"/>"));
            Case("face_in_plane", "<position name=\"v1\" x=\"0\" y=\"0\" z=\"0\"/><position name=\"v2\" x=\"20\" y=\"0\" z=\"0\"/><position name=\"v3\" x=\"0\" y=\"0\" z=\"20\"/>",
                 "<tessellated name=\"Pc\"><triangular vertex1=\"v1\" vertex2=\"v2\" vertex3=\"v3\"/></tessellated>", a, pa);
            Case("pc_rad", "", Cylinder, a, Phys("A", "<rotation name=\"r\" x=\"1.5707963\" y=\"0\" z=\"0\"/>"));
            Case("pc_rad_oblique", "", Cylinder, a, Phys("A", "<rotation name=\"r\" x=\"1.57\" y=\"0\" z=\"0\"/>"));

            // Зажим координат: длинный вырез под углом.
            foreach (string length in new[] { "1e3", "1e5" })
            {
                Case("band_" + length, "", b1 + "<box name=\"B2\" x=\"" + length + "\" y=\"4\" z=\"4\" lunit=\"mm\"/><subtraction name=\"Pc\"><first ref=\"B1\"/><second ref=\"B2\"/><rotation name=\"r\" x=\"0\" y=\"30\" z=\"0\" unit=\"deg\"/></subtraction>", a, pa);
            }

            Case("sub_ring", "", "<box name=\"A0\" x=\"100\" y=\"100\" z=\"200\" lunit=\"mm\"/><polycone name=\"Ring\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"-300\" rmin=\"10\" rmax=\"20\"/><zplane z=\"300\" rmin=\"10\" rmax=\"20\"/></polycone><subtraction name=\"Pc\"><first ref=\"A0\"/><second ref=\"Ring\"/></subtraction>", a, pa);

            Case("order_fold", "", "<box name=\"B1\" x=\"1000\" y=\"1000\" z=\"1000\" lunit=\"mm\"/><polycone name=\"C\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"0\" rmin=\"0\" rmax=\"300\"/><zplane z=\"400\" rmin=\"0\" rmax=\"300\"/><zplane z=\"200\" rmin=\"0\" rmax=\"100\"/></polycone><subtraction name=\"Pc\"><first ref=\"B1\"/><second ref=\"C\"/></subtraction>", a, pa);

            // Кольцо меньше половины точки в плоскости торца: плоскость тело задела.
            Case("tiny_ring", "<position name=\"far\" x=\"0\" y=\"0\" z=\"2000\"/>",
                 "<polycone name=\"Ring\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"-1\" rmin=\"0\" rmax=\"0.05\"/><zplane z=\"1\" rmin=\"0\" rmax=\"0.05\"/></polycone><box name=\"Far\" x=\"1000\" y=\"1000\" z=\"1000\" lunit=\"mm\"/>",
                 Vol("Crystal", "Ring", "G4_Al") + Vol("F", "Far", "G4_Fe"), Phys("Crystal", "") + Phys("F", "<positionref ref=\"far\"/>"));

            Case("tiny_box", "<position name=\"far\" x=\"0\" y=\"0\" z=\"2000\"/>",
                 "<box name=\"Small\" x=\"0.001\" y=\"0.001\" z=\"0.001\" lunit=\"mm\"/><box name=\"Far\" x=\"1000\" y=\"1000\" z=\"1000\" lunit=\"mm\"/>",
                 Vol("Crystal", "Small", "G4_Al") + Vol("F", "Far", "G4_Fe"), Phys("Crystal", "") + Phys("F", "<positionref ref=\"far\"/>"));

            // Общая работа на вид: десять тел по 255 частей.
            StringBuilder shared = new StringBuilder("<box name=\"L0\" x=\"100\" y=\"100\" z=\"100\" lunit=\"mm\"/>");
            for (int i = 1; i <= 7; i++)
            {
                shared.Append(string.Format(CultureInfo.InvariantCulture, "<subtraction name=\"L{0}\"><first ref=\"L{1}\"/><second ref=\"L{1}\"/></subtraction>", i, i - 1));
            }

            StringBuilder many = new StringBuilder(), placed = new StringBuilder();
            for (int i = 0; i < 10; i++)
            {
                string name = "V" + i.ToString(CultureInfo.InvariantCulture);
                many.Append(Vol(name, "L7", "G4_Al"));
                placed.Append(Phys(name, ""));
            }

            Case("view_budget", "", shared.ToString(), many.ToString(), placed.ToString());

            // Сетки: куб, куб с полостью, октаэдр с вершинами ровно на плоскости y = 0.
            Case("mesh_cube", Corners("o", 20.0), "<tessellated name=\"Pc\">" + CubeFaces("o", false) + "</tessellated>", a, pa);
            Case("mesh_shell", Corners("o", 20.0) + Corners("i", 10.0), "<tessellated name=\"Pc\">" + CubeFaces("o", false) + CubeFaces("i", true) + "</tessellated>", a, pa);
            Case("mesh_octa", "<position name=\"xp\" x=\"20\" y=\"0\" z=\"0\"/><position name=\"xm\" x=\"-20\" y=\"0\" z=\"0\"/><position name=\"yp\" x=\"0\" y=\"20\" z=\"0\"/><position name=\"ym\" x=\"0\" y=\"-20\" z=\"0\"/><position name=\"zp\" x=\"0\" y=\"0\" z=\"20\"/><position name=\"zm\" x=\"0\" y=\"0\" z=\"-20\"/>",
                 "<tessellated name=\"Pc\">" + Tri("xp", "yp", "zp") + Tri("yp", "xm", "zp") + Tri("xm", "ym", "zp") + Tri("ym", "xp", "zp") + Tri("yp", "xp", "zm") + Tri("xm", "yp", "zm") + Tri("ym", "xm", "zm") + Tri("xp", "ym", "zm") + "</tessellated>", a, pa);

            // Шов: цилиндр из трёх плоскостей, на оси и на смещённой хорде. Ось — z, разрез y = 0.
            string three = "<polycone name=\"Pc\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"-60\" rmin=\"0\" rmax=\"40\"/><zplane z=\"-20\" rmin=\"0\" rmax=\"40\"/><zplane z=\"60\" rmin=\"0\" rmax=\"40\"/></polycone>";
            Case("pc_three_planes", "", three, a, pa);
            Case("chord_three_planes", "<position name=\"far\" x=\"0\" y=\"10\" z=\"0\"/>", three, a, Phys("A", "<positionref ref=\"far\"/>"));

            // Объединение, в котором вырез выступает за тело: x 20…40 вне коробки 20×20.
            // Роли: незнакомые имена, свинцовый домик, источник в корпусе и проба поодаль.
            string crystal = "<tube name=\"C\" rmax=\"25\" z=\"50\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>";
            string housing = "<tube name=\"H\" rmin=\"26\" rmax=\"28\" z=\"56\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>";
            string cry = Vol("Crystal", "C", "G4_SODIUM_IODIDE"), hou = Vol("Housing", "H", "G4_Al");
            string both = Phys("Crystal", "") + Phys("Housing", "");
            Case("role_castle", "", crystal + housing + "<box name=\"OUT\" lunit=\"mm\" x=\"300\" y=\"300\" z=\"300\"/><box name=\"IN\" lunit=\"mm\" x=\"100\" y=\"100\" z=\"100\"/><subtraction name=\"PB\"><first ref=\"OUT\"/><second ref=\"IN\"/></subtraction>",
                 cry + hou + Vol("Castle", "PB", "G4_Pb"), both + Phys("Castle", ""));
            Case("role_castle_only", "", crystal + "<box name=\"OUT\" lunit=\"mm\" x=\"300\" y=\"300\" z=\"300\"/><box name=\"IN\" lunit=\"mm\" x=\"100\" y=\"100\" z=\"100\"/><subtraction name=\"PB\"><first ref=\"OUT\"/><second ref=\"IN\"/></subtraction>",
                 cry + Vol("Castle", "PB", "G4_Pb"), Phys("Crystal", "") + Phys("Castle", ""));
            Case("role_lone_box", "", "<box name=\"B\" lunit=\"mm\" x=\"50\" y=\"50\" z=\"50\"/>", Vol("Thing", "B", "G4_Al"), Phys("Thing", ""));
            Case("role_source_inside", "", crystal + housing + "<box name=\"S\" lunit=\"mm\" x=\"2\" y=\"2\" z=\"2\"/>", cry + hou + Vol("Am241_source", "S", "G4_Fe"),
                 both + Phys("Am241_source", "<position name=\"sp\" x=\"0\" y=\"0\" z=\"27\"/>"));
            Case("role_source_apart", "", crystal + housing + "<box name=\"S\" lunit=\"mm\" x=\"20\" y=\"20\" z=\"2\"/>", cry + hou + Vol("Point_source", "S", "G4_Fe"),
                 both + Phys("Point_source", "<position name=\"sp\" x=\"0\" y=\"0\" z=\"100\"/>"));

            Case("role_resource_apart", "", crystal + housing + "<box name=\"S\" lunit=\"mm\" x=\"20\" y=\"20\" z=\"2\"/>", cry + hou + Vol("Resource_box", "S", "G4_Fe"),
                 both + Phys("Resource_box", "<position name=\"sp\" x=\"0\" y=\"0\" z=\"100\"/>"));

            // Порог рамки: 50 м оставить, 150 м при детекторе 100 мм убрать, 200 м при соседе 500 м оставить.
            string det = "<box name=\"B\" lunit=\"mm\" x=\"10\" y=\"10\" z=\"10\"/>";
            Case("keep_50m", "<position name=\"q\" x=\"0\" y=\"2000\" z=\"0\"/>", det + "<box name=\"L\" lunit=\"mm\" x=\"50000\" y=\"1000\" z=\"1000\"/>",
                 Vol("Small", "B", "G4_Al") + Vol("Big", "L", "G4_Al"), Phys("Big", "") + Phys("Small", "<positionref ref=\"q\"/>"));
            Case("trim_wall", "<position name=\"q\" x=\"0\" y=\"2000\" z=\"0\"/>", "<box name=\"B\" lunit=\"mm\" x=\"100\" y=\"100\" z=\"100\"/><box name=\"L\" lunit=\"mm\" x=\"150000\" y=\"1000\" z=\"3000\"/>",
                 Vol("Det", "B", "G4_Al") + Vol("Wall", "L", "G4_Fe"), Phys("Det", "") + Phys("Wall", "<positionref ref=\"q\"/>"));
            Case("keep_gap", "<position name=\"q\" x=\"0\" y=\"2000\" z=\"0\"/>", "<box name=\"B\" lunit=\"mm\" x=\"500\" y=\"500\" z=\"500\"/><box name=\"L\" lunit=\"mm\" x=\"200000\" y=\"1000\" z=\"1000\"/>",
                 Vol("Det", "B", "G4_Al") + Vol("Long", "L", "G4_Fe"), Phys("Det", "") + Phys("Long", "<positionref ref=\"q\"/>"));

            // Плоскость в зазоре между частями объединения.
            Case("un_gap", "<position name=\"p1\" x=\"-50\" y=\"30\" z=\"0\"/><position name=\"p2\" x=\"50\" y=\"-30\" z=\"0\"/>",
                 "<box name=\"B1\" lunit=\"mm\" x=\"20\" y=\"20\" z=\"20\"/><union name=\"Pc\"><first ref=\"B1\"/><second ref=\"B1\"/><positionref ref=\"p2\"/><firstpositionref ref=\"p1\"/></union>", a, pa);

            Case("union_protrude", "<position name=\"q\" x=\"25\" y=\"0\" z=\"0\"/><position name=\"p2\" x=\"60\" y=\"0\" z=\"0\"/>",
                 "<box name=\"B\" x=\"40\" y=\"40\" z=\"40\" lunit=\"mm\"/><box name=\"Cut\" x=\"30\" y=\"60\" z=\"40\" lunit=\"mm\"/><box name=\"Far\" x=\"10\" y=\"10\" z=\"10\" lunit=\"mm\"/><subtraction name=\"Sb\"><first ref=\"B\"/><second ref=\"Cut\"/><positionref ref=\"q\"/></subtraction><union name=\"Pc\"><first ref=\"Sb\"/><second ref=\"Far\"/><positionref ref=\"p2\"/></union>", a, pa);
        }

        // ------------------------------------------------------------------
        //  Рисование
        // ------------------------------------------------------------------

        sealed class Drawing : IDisposable
        {
            public GdmlSketch Sketch;
            public Bitmap Bitmap;
            public GdmlGeometry Geometry;
            public List<string> Notes;
            public string Failure;
            public Rectangle Left, Right;

            public void Dispose()
            {
                this.Bitmap.Dispose();
                this.Sketch.Dispose();
            }
        }

        static object Field(object owner, string name)
        {
            return owner.GetType().GetField(name, Any).GetValue(owner);
        }

        static Drawing Draw(GdmlGeometry g, Size size)
        {
            Drawing d = new Drawing { Geometry = g, Sketch = new GdmlSketch(), Bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppArgb) };
            d.Sketch.Size = size;
            d.Sketch.SetGeometry(g);
            int leftWidth = (size.Width - 16) / 2;
            d.Left = new Rectangle(0, 0, leftWidth, size.Height);
            d.Right = new Rectangle(leftWidth + 16, 0, size.Width - leftWidth - 16, size.Height);
            using (Graphics graphics = Graphics.FromImage(d.Bitmap))
            {
                typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(d.Sketch, new object[]
                {
                    new PaintEventArgs(graphics, new Rectangle(0, 0, d.Bitmap.Width, d.Bitmap.Height)),
                });
            }

            // Места видов считает сам чертёж: заголовок, подписи и выноски стоят вне поля чертежа.
            IList slots = (IList)Field(d.Sketch, "paintSlots");
            if (slots.Count == 2)
            {
                d.Left = (Rectangle)slots[0].GetType().GetField("Plot").GetValue(slots[0]);
                d.Right = (Rectangle)slots[1].GetType().GetField("Plot").GetValue(slots[1]);
            }

            d.Notes = new List<string>((List<string>)Field(d.Sketch, "paintNotes"));
            d.Failure = (string)Field(d.Sketch, "paintFailure");
            return d;
        }

        static Drawing DrawFile(string path)
        {
            Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            return Draw(GdmlGeometry.Read(path), new Size(1400, 700));
        }

        static void Shot(Drawing d, string name)
        {
            if (shots != null)
            {
                d.Bitmap.Save(Path.Combine(shots, name + ".png"), ImageFormat.Png);
            }
        }

        /// <summary>Масштаб вида: рамка строится тем же кодом, что и чертёж.</summary>
        static object Frame(Drawing d, bool axial, out int normal)
        {
            object[] axes = { axial, d.Geometry.Axis, 0, 0, 0 };
            typeof(GdmlSketch).GetMethod("ViewAxes", Any).Invoke(null, axes);
            normal = (int)axes[2];
            return typeof(GdmlSketch).GetMethod("MakeFrame", Any).Invoke(d.Sketch, new object[] { axial ? d.Left : d.Right, axes[3], axes[4] });
        }

        static double Scale(Drawing d, bool axial)
        {
            int normal;
            object frame = Frame(d, axial, out normal);
            return frame == null ? 0.0 : Convert.ToDouble(frame.GetType().GetField("Scale").GetValue(frame), CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Строка оси на просвете штриха. Настоящий шов двух заливок встык виден в сторону светлее заливки: фон просвечивает
        /// (синий заливки 108, шва 115). Штрих оси только темнит (просвет 96). Поэтому мера — направление: не светлее заливки,
        /// и не темнее штриха, иначе сплошная линия по оси тоже прошла бы.
        /// </summary>
        static bool NearFill(Color c, Color fill)
        {
            const int Tolerance = 2;
            return c.R <= fill.R + Tolerance && c.G <= fill.G + Tolerance && c.B <= fill.B + Tolerance
                && Math.Abs(c.R - fill.R) + Math.Abs(c.G - fill.G) + Math.Abs(c.B - fill.B) <= 80;
        }

        static PointF Screen(Drawing d, bool axial, double u, double v)
        {
            int normal;
            object frame = Frame(d, axial, out normal);
            return (PointF)frame.GetType().GetMethod("ToScreen").Invoke(frame, new object[] { u, v });
        }

        static Color Fill(Drawing d, string material)
        {
            return (Color)typeof(GdmlSketch).GetMethod("ColorOf", Any).Invoke(d.Sketch, new object[] { material });
        }

        static List<string> Titles(Drawing d)
        {
            return (List<string>)Field(d.Sketch, "paintTitles");
        }

        static double FillMm2(Drawing d, bool axial, string material)
        {
            int color = Fill(d, material).ToArgb();
            Rectangle rect = axial ? d.Left : d.Right;
            int count = 0;
            for (int y = rect.Top; y < rect.Bottom; y++)
            {
                for (int x = rect.Left; x < rect.Right; x++)
                {
                    if (d.Bitmap.GetPixel(x, y).ToArgb() == color) count++;
                }
            }

            double scale = Scale(d, axial);
            return scale > 0.0 ? count / (scale * scale) : 0.0;
        }

        static RectangleF Drawn(Drawing d, bool axial)
        {
            int normal;
            object frame = Frame(d, axial, out normal);
            return frame == null ? RectangleF.Empty : (RectangleF)frame.GetType().GetProperty("Drawn").GetValue(frame);
        }

        static string Num(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        static void Check(string name, bool ok, string detail)
        {
            Console.WriteLine("  {0} {1}: {2}", ok ? "ок" : "⛔", name, detail);
            if (!ok) bad++;
        }

        /// <summary>Закрашено столько мм², сколько в сечении: контур и штрих оси отнимают проценты, поэтому снизу запас.</summary>
        static void Area(string name, Drawing d, bool axial, string material, double expected)
        {
            double area = FillMm2(d, axial, material);
            double ratio = area / expected;
            Check(name, ratio > 0.88 && ratio < 1.02,
                  "закрашено " + Num(area) + " мм², сечение " + Num(expected) + " мм², ×" + Num(ratio));
        }

        // ------------------------------------------------------------------
        //  (1) падения
        // ------------------------------------------------------------------

        static void Crash()
        {
            Console.WriteLine("=== (1) падения: все входы × {0} размеров ===", Sizes.Length);
            List<string> files = new List<string>(Directory.GetFiles(data, "*.gdml"));
            // Цепочки и тяжёлые входы — только отдельным процессом: падение стека унесло бы пробу без приговора.
            foreach (string own in Directory.GetFiles(tmp, "*.gdml"))
            {
                string shortName = Path.GetFileName(own);
                if (!shortName.StartsWith("deep_", StringComparison.Ordinal) && !shortName.StartsWith("heavy_", StringComparison.Ordinal))
                {
                    files.Add(own);
                }
            }

            foreach (string dir in more)
            {
                files.AddRange(Directory.GetFiles(dir, "*.gdml"));
            }

            int runs = 0;
            foreach (string file in files)
            {
                GdmlGeometry g;
                try
                {
                    g = GdmlGeometry.Read(file);
                }
                catch (Exception ex)
                {
                    Check(Path.GetFileName(file), false, "читатель бросил " + ex.GetType().Name);
                    continue;
                }

                runs += PaintAllSizes(Path.GetFileName(file), g);
                if (shots != null)
                {
                    using (Drawing d = Draw(g, new Size(1400, 700)))
                    {
                        Shot(d, Path.GetFileNameWithoutExtension(file));
                    }
                }
            }

            foreach (KeyValuePair<string, GdmlGeometry> hand in HandBuilt())
            {
                runs += PaintAllSizes(hand.Key, hand.Value);
                if (hand.Key.IndexOf("конечное", StringComparison.Ordinal) >= 0)
                {
                    continue; // конечное: рисовать можно, зажим координат, текста не нужно
                }

                using (Drawing d = Draw(hand.Value, new Size(1400, 700)))
                {
                    bool bothViews = hand.Key.IndexOf("EndPlane", StringComparison.Ordinal) < 0;
                    bool refused = d.Notes.Count > 0 && (!bothViews || d.Notes.Count == 2)
                        && d.Notes.TrueForAll(n => n.StartsWith("No drawing", StringComparison.Ordinal) || n.StartsWith("No exact", StringComparison.Ordinal));
                    Check(hand.Key + ": текст вместо чертежа", refused, string.Join(" | ", d.Notes));
                }
            }

            Console.WriteLine("  входов {0}, отрисовок {1}", files.Count + HandBuilt().Count, runs);

            // Конечное и огромное — чертёж есть, а не отказ: тело в рамке, и метка масштаба своего порядка.
            using (Drawing d = DrawFile(Inline("16f_pc_plus_hugebox")))
            {
                // Деталь в 1e300 мм больше не сводит прибор в точку: цилиндр 100×10 крупно, деталь названа подписью.
                Check("16f: чертёж, деталь вне рамки и названа", Scale(d, true) > 1.0 && d.Notes.Count == 2
                      && d.Notes[0].Contains("larger than the rest") && d.Notes[0].Contains("H"),
                      "масштаб " + Scale(d, true).ToString("E2", CultureInfo.InvariantCulture) + "; " + string.Join(" | ", d.Notes));
                Area("16f: цилиндр на месте", d, true, "G4_Al", 100.0 * 10.0);
            }

            foreach (Size small in new[] { new Size(20, 300), new Size(40, 40), new Size(49, 49) })
            {
                using (Drawing d = Draw(Hand(GdmlTransform.Identity(), -5.0, 5.0, 0.0), small))
                {
                    Check("окно " + small.Width + "x" + small.Height + ": места нет, подписи нет", d.Notes.Count == 0, string.Join(" | ", d.Notes));
                }
            }

            foreach (double[] pair in new[] { new[] { 100.0, 20.0 }, new[] { 8000.0, 2000.0 }, new[] { 200.0, 50.0 }, new[] { 1e-5, 2e-6 }, new[] { 1e300, 2e299 } })
            {
                double length = (double)typeof(GdmlSketch).GetMethod("ScaleLength", Any).Invoke(null, new object[] { pair[0] });
                Check("метка масштаба при ширине " + pair[0].ToString("G4", CultureInfo.InvariantCulture), Math.Abs(length / pair[1] - 1.0) < 1e-9, length.ToString("G4", CultureInfo.InvariantCulture));
            }

            using (Drawing d = DrawFile(Inline("pos_1e308")))
            {
                Check("сумма 1.7e308 переполняет рамку: текст", d.Notes.Count > 0, string.Join(" | ", d.Notes));
            }
        }

        static int PaintAllSizes(string name, GdmlGeometry g)
        {
            int runs = 0;
            foreach (Size size in Sizes)
            {
                try
                {
                    using (Drawing d = Draw(g, size))
                    {
                        Gost(name + " " + size.Width + "x" + size.Height, d);
                        if (d.Failure != null)
                        {
                            Check(name + " " + size.Width + "x" + size.Height, false, "сработал последний заслон: " + d.Failure);
                        }
                    }
                }
                catch (TargetInvocationException ex)
                {
                    Check(name + " " + size.Width + "x" + size.Height, false, "из OnPaint вышло " + ex.InnerException.GetType().Name);
                }

                runs++;
            }

            return runs;
        }

        /// <summary>Заслон простановки: те же правила ГОСТ 2.307-2011, что у чертежа редактора (`GeometryTopViewProbeA307`).
        /// Всего читает рамки подписей, линии и контуры, поэтому его можно поставить на настоящую раскладку прошлой версии.</summary>
        static List<string> GostFound(List<KeyValuePair<string, RectangleF>> marks, GeometrySketch.DimLine[] lines, RectangleF[] contours)
        {
            List<string> found = new List<string>();
            foreach (KeyValuePair<string, RectangleF> mark in marks)
            {
                RectangleF ink = mark.Value;
                ink.Inflate(-1f, -1f);
                foreach (GeometrySketch.DimLine line in lines)
                {
                    bool hit = false;
                    for (int i = 0; i <= 200 && !hit; i++)
                    {
                        float t = i / 200f;
                        hit = ink.Contains(line.A.X + t * (line.B.X - line.A.X), line.A.Y + t * (line.B.Y - line.A.Y));
                    }

                    if (hit)
                    {
                        found.Add("линия через число: " + mark.Key);
                    }
                }

                // (`AMBER324`) Решение Amber 08.11.2026 вопросником, дословно: «Все числа горизонтально (Рекомендую)».
                // Число размера в рамке выше своей ширины — повёрнутое: однознаковое не судится, у него рамка и так узкая.
                if (mark.Key.Length >= 2 && mark.Value.Height > mark.Value.Width && lines.Any(x => x.Key == mark.Key && x.Kind == GeometrySketch.DimLineKind.Dimension))
                {
                    found.Add("число повёрнуто: " + mark.Key);
                }

                if (lines.Any(x => x.Key == mark.Key))
                {
                    foreach (RectangleF contour in contours)
                    {
                        RectangleF o = RectangleF.Intersect(ink, contour);
                        if (o.Width > 0f && o.Height > 0f)
                        {
                            found.Add("число на контуре: " + mark.Key);
                        }
                    }
                }
            }

            return found;
        }

        static void Gost(string name, Drawing d)
        {
            List<GdmlSketch.SketchMark> list = (List<GdmlSketch.SketchMark>)Field(d.Sketch, "paintMarks");
            List<KeyValuePair<string, RectangleF>> marks = new List<KeyValuePair<string, RectangleF>>();
            IList slots = (IList)Field(d.Sketch, "paintSlots");
            foreach (GdmlSketch.SketchMark mark in list)
            {
                // Длинный заголовок чертёж обрезает по своей рамке вида: за ней текста нет, хотя мера его длиннее.
                RectangleF bounds = mark.Bounds;
                foreach (object slot in slots)
                {
                    Rectangle panel = (Rectangle)slot.GetType().GetField("Panel").GetValue(slot);
                    if (panel.Contains(Point.Round(bounds.Location)))
                    {
                        bounds = RectangleF.Intersect(bounds, panel);
                        break;
                    }
                }

                marks.Add(new KeyValuePair<string, RectangleF>(mark.Text, bounds));
            }

            GeometrySketch.DimLine[] lines = d.Sketch.DimensionLines;
            RectangleF[] contours = d.Sketch.ViewContours;
            foreach (string line in GostFound(marks, lines, contours).Distinct().Take(2))
            {
                Check(name, false, "ГОСТ: " + line);
            }

            foreach (KeyValuePair<RectangleF, double> bar in (List<KeyValuePair<RectangleF, double>>)Field(d.Sketch, "paintScales"))
            {
                foreach (GeometrySketch.DimLine line in lines)
                {
                    RectangleF extent = RectangleF.FromLTRB(Math.Min(line.A.X, line.B.X), Math.Min(line.A.Y, line.B.Y), Math.Max(line.A.X, line.B.X) + 0.5f, Math.Max(line.A.Y, line.B.Y) + 0.5f);
                    RectangleF o = RectangleF.Intersect(extent, bar.Key);
                    if (o.Width > 0f && o.Height > 0f)
                    {
                        Check(name, false, "ГОСТ: линия упирается в масштабную черту: " + line.Key);
                    }
                }
            }

            // Ряд и вылет: те же числа, что у чертежа редактора.
            foreach (GeometrySketch.DimLine line in lines)
            {
                bool bottom = line.Side == GeometrySketch.DimSide.Bottom;
                if (line.Kind == GeometrySketch.DimLineKind.Dimension)
                {
                    float at = bottom ? line.A.Y : line.A.X;
                    bool onRow = contours.Any(c => new[] { 0f, 1f, 2f }.Any(row => Math.Abs((bottom ? at - c.Bottom : c.Left - at) - GeometrySketch.DimFirstPx - row * GeometrySketch.DimPitchPx) < 0.1f));
                    if (!onRow)
                    {
                        Check(name, false, "ГОСТ: размер не на ряду: " + line.Key);
                    }
                }
                else
                {
                    GeometrySketch.DimLine own = lines.First(x => x.Group == line.Group);
                    float tip = bottom ? line.B.Y : line.B.X, row = bottom ? own.A.Y : own.A.X;
                    if (Math.Abs(Math.Abs(tip - row) - GeometrySketch.DimOverPx) > 0.1f)
                    {
                        Check(name, false, "ГОСТ: вылет выносной: " + line.Key);
                    }
                }
            }

            // Над линией, а не в её разрыве. (`AMBER324`) У вертикальной — горизонтальное число рядом с ней, в 4 точках: слева
            // или, в тесноте, справа, со стороны тела.
            foreach (GeometrySketch.DimLine line in lines.Where(x => x.Kind == GeometrySketch.DimLineKind.Dimension))
            {
                bool bottom = line.Side == GeometrySketch.DimSide.Bottom;
                float at = bottom ? line.A.Y : line.A.X;
                bool above = marks.Any(m => m.Key == line.Key && (bottom ? Math.Abs(m.Value.Bottom + 1f - at) < 0.1f : (Math.Abs(m.Value.Right + 4f - at) < 0.1f || Math.Abs(m.Value.Left - 4f - at) < 0.1f)));
                if (!above)
                {
                    Check(name, false, "ГОСТ: число не у линии: " + line.Key);
                }
            }
        }

        /// <summary>Положительный контроль заслона: настоящая раскладка прошлой версии, GS2020_Capescint51x51, поле 584×646.
        /// Рамки чисел сняты прежним кодом (`after\\GS2020_Capescint51x51.*.584x646.png`), линии по его формуле: число в разрыве линии.</summary>
        static void GostControl()
        {
            List<KeyValuePair<string, RectangleF>> marks = new List<KeyValuePair<string, RectangleF>>
            {
                new KeyValuePair<string, RectangleF>("83 mm", new RectangleF(128.8f, 434.1f, 35.5f, 13.8f)),
                new KeyValuePair<string, RectangleF>("63 mm", new RectangleF(7.2f, 295.8f, 13.8f, 35.5f)),
            };
            GeometrySketch.DimLine[] lines =
            {
                new GeometrySketch.DimLine { A = new PointF(28.1f, 441f), B = new PointF(265f, 441f), Kind = GeometrySketch.DimLineKind.Dimension, Key = "83 mm" },
                new GeometrySketch.DimLine { A = new PointF(14.1f, 210f), B = new PointF(14.1f, 417f), Kind = GeometrySketch.DimLineKind.Dimension, Key = "63 mm" },
            };
            List<string> found = GostFound(marks, lines, new[] { RectangleF.FromLTRB(28.1f, 210f, 265f, 417f) });
            Check("заслон ГОСТ на прошлой раскладке", found.Contains("линия через число: 83 mm") && found.Contains("линия через число: 63 mm"), "отказов " + found.Count);
            // (`AMBER324`) Тот же положительный контроль для поворота: вертикальное «63 mm» прошлой раскладки повёрнуто, горизонтальное «83 mm» нет.
            Check("заслон ГОСТ видит повёрнутое число прошлой раскладки", found.Contains("число повёрнуто: 63 mm") && !found.Contains("число повёрнуто: 83 mm"), string.Join("; ", found));
        }

        /// <summary>Читатель неконечного больше не отдаёт; чертёж не должен на него полагаться, поэтому геометрия руками.</summary>
        static Dictionary<string, GdmlGeometry> HandBuilt()
        {
            Dictionary<string, GdmlGeometry> all = new Dictionary<string, GdmlGeometry>();
            all["рукой: NaN в границах"] = Hand(GdmlTransform.Identity(), double.NaN, 10.0, 0.0);
            all["рукой: +Inf в границах"] = Hand(GdmlTransform.Identity(), -1.0, double.PositiveInfinity, 0.0);
            all["рукой: размах переполняет double"] = Hand(GdmlTransform.Identity(), -1.7e308, 1.7e308, 0.0);
            all["рукой: EndPlane NaN"] = Hand(GdmlTransform.Identity(), -5.0, 5.0, double.NaN);
            all["рукой: EndPlane +Inf"] = Hand(GdmlTransform.Identity(), -5.0, 5.0, double.PositiveInfinity);
            GdmlGeometry bounded = Hand(GdmlTransform.Identity(), -5.0, 5.0, 0.0);
            bounded.Volumes[0].Min = null;
            bounded.Volumes[0].Max = null;
            all["рукой: границ нет ни у кого"] = bounded;
            all["рукой: NaN в положении при конечных границах"] = Hand(GdmlTransform.FromEuler(0.0, 0.0, 0.0, double.NaN, 0.0, 0.0), -5.0, 5.0, 0.0);
            all["рукой: конечное 1e300 в положении при конечных границах"] = Hand(GdmlTransform.FromEuler(0.0, 0.0, 0.0, 1e300, 0.0, 0.0), -5.0, 5.0, 0.0);
            all["рукой: конечное 1e9 в положении при конечных границах"] = Hand(GdmlTransform.FromEuler(0.0, 0.0, 0.0, 1e9, 1e9, 0.0), -5.0, 5.0, 0.0);
            GdmlGeometry normalNan = Hand(GdmlTransform.Identity(), -5.0, 5.0, 0.0);
            normalNan.Volumes[0].Min[2] = double.NaN;
            all["рукой: NaN только по нормали торца"] = normalNan;
            return all;
        }

        static GdmlGeometry Hand(GdmlTransform transform, double low, double high, double endPlane)
        {
            GdmlSolid solid = new GdmlSolid { Name = "S", Tag = "polycone", Kind = GdmlSolidKind.Polycone };
            solid.Planes.Add(new[] { -5.0, 0.0, 5.0 });
            solid.Planes.Add(new[] { 5.0, 0.0, 5.0 });
            GdmlGeometry g = new GdmlGeometry { EndPlane = endPlane };
            g.Volumes.Add(new GdmlVolumeInstance
            {
                VolumeName = "V",
                MaterialName = "G4_Al",
                Solid = solid,
                Transform = transform,
                Min = new[] { low, low, low },
                Max = new[] { high, high, high },
            });
            return g;
        }

        /// <summary>Цепочка вычитаний глубиной depth без вторых тел: частей меньше предела, судит только глубина.</summary>
        static GdmlGeometry Hand(GdmlTransform transform, double low, double high, double endPlane, int depth)
        {
            GdmlGeometry g = Hand(transform, low, high, endPlane);
            GdmlSolid inner = new GdmlSolid { Name = "S0", Tag = "box", Kind = GdmlSolidKind.Box, SizeX = 100.0, SizeY = 100.0, SizeZ = 100.0 };
            for (int i = 1; i < depth; i++)
            {
                inner = new GdmlSolid { Name = "S" + i.ToString(CultureInfo.InvariantCulture), Tag = "subtraction", Kind = GdmlSolidKind.Subtraction, First = inner };
            }

            g.Volumes[0].Solid = inner;
            return g;
        }

        static void Barrier()
        {
            Console.WriteLine();
            Console.WriteLine("=== положительный контроль последнего заслона ===");
            GdmlGeometry g = Hand(GdmlTransform.Identity(), -5.0, 5.0, 0.0);
            g.Volumes.Add(null);
            try
            {
                using (Drawing d = Draw(g, new Size(400, 200)))
                {
                    Check("null среди томов: заслон сработал, текст есть", d.Failure != null && d.Notes.Count > 0,
                          (d.Failure ?? "заслон молчал") + " | " + string.Join(" | ", d.Notes));
                }
            }
            catch (TargetInvocationException ex)
            {
                Check("null среди томов", false, "из OnPaint вышло " + ex.InnerException.GetType().Name);
            }

            using (Drawing d = Draw(Hand(GdmlTransform.Identity(), -1.7e308, 1.7e308, 0.0), new Size(400, 200)))
            {
                Check("обычный отказ идёт мимо заслона", d.Failure == null && d.Notes.Count > 0, d.Failure ?? string.Join(" | ", d.Notes));
            }
        }

        // ------------------------------------------------------------------
        //  (2) сетка
        // ------------------------------------------------------------------

        static void Mesh()
        {
            Console.WriteLine();
            Console.WriteLine("=== (2) сетка: отрезки сечения без хорд ===");
            using (Drawing d = DrawFile(Path.Combine(data, "29_mesh_chord.gdml")))
            {
                Shot(d, "a304-29_mesh_chord");
                // Две грани: отрезки у краёв рамки. Хорда между ними шла бы через центр вида.
                RectangleF drawn = Drawn(d, true);
                PointF center = Screen(d, true, 0.0, 0.0);
                float cut = Screen(d, true, d.Geometry.EndPlane, 0.0).X;
                int ink = 0;
                for (int y = (int)drawn.Top + 2; y < (int)drawn.Bottom - 2; y++)
                {
                    // Строку штриха оси и линию A-A не считаем: это другие линии.
                    if (Math.Abs(y - center.Y) < 3.0f) continue;
                    for (int x = (int)drawn.Left + 6; x < (int)drawn.Right - 6; x++)
                    {
                        if (Math.Abs(x - cut) < 3.0f) continue;
                        if (d.Bitmap.GetPixel(x, y).ToArgb() != Canvas.ToArgb()) ink++;
                    }
                }

                Check("29_mesh_chord: точек вне строки оси", ink < 120, ink.ToString(CultureInfo.InvariantCulture));
                Check("29_mesh_chord: торец x = 0 мимо граней, и это сказано", d.Notes.Contains("This plane crosses no body"), string.Join(" | ", d.Notes));
                Check("29_mesh_chord: незамкнутая сетка названа, заливки нет", d.Notes.Exists(n => n.StartsWith("Outline only", StringComparison.Ordinal)) && FillMm2(d, true, "G4_Al") == 0.0, string.Join(" | ", d.Notes));
            }

            // Замкнутая, но вывернутая сетка: подпись не говорит «не замкнута», причина — в сводке.
            foreach (string flipped in new[] { "o05_tess_inverted", "e02_flipped_face" })
            {
                using (Drawing d = DrawFile(Path.Combine(data, flipped + ".gdml")))
                {
                    string summary = GdmlOverview.Of(d.Geometry).SummaryText();
                    Check(flipped + ": подпись не называет сетку незамкнутой", !d.Notes.Exists(n => n.Contains("not closed")) && d.Notes.Exists(n => n.StartsWith("Outline only", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
                    Check(flipped + ": причина в сводке", summary.Contains("inside out"), "");
                }
            }

            MeshFill();
        }

        /// <summary>Заливка сечения замкнутой сетки: площадь по формуле, полость пуста, вершины на самой плоскости.</summary>
        static void MeshFill()
        {
            using (Drawing d = DrawFile(Inline("mesh_cube")))
            {
                Area("mesh_cube: сечение закрашено", d, true, "G4_Al", 40.0 * 40.0);
                Check("mesh_cube: подписи нет", d.Notes.Count == 0, string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("mesh_shell")))
            {
                Area("mesh_shell: стенка закрашена, полость нет", d, true, "G4_Al", 40.0 * 40.0 - 20.0 * 20.0);
                PointF wall = Screen(d, true, 15.0, 5.0);
                PointF hole = Screen(d, true, 3.0, 5.0);
                Check("mesh_shell: стенка цветом вещества", d.Bitmap.GetPixel((int)wall.X, (int)wall.Y).ToArgb() == Fill(d, "G4_Al").ToArgb(), "точка в стенке");
                Check("mesh_shell: в полости холст", d.Bitmap.GetPixel((int)hole.X, (int)hole.Y).ToArgb() == Canvas.ToArgb(), "точка в полости");
            }

            using (Drawing d = DrawFile(Inline("mesh_octa")))
            {
                // Плоскость y = 0 идёт через четыре вершины: квадрат с диагональю 40, 800 мм².
                Area("mesh_octa: вершины на самой плоскости, контур сошёлся", d, true, "G4_Al", 800.0);
                Check("mesh_octa: подписи нет", d.Notes.Count == 0, string.Join(" | ", d.Notes));
            }
        }

        // ------------------------------------------------------------------
        //  (3) профиль
        // ------------------------------------------------------------------

        static void Profiles()
        {
            Console.WriteLine();
            Console.WriteLine("=== (3) профиль genericPolycone в порядке файла ===");
            using (Drawing d = DrawFile(Path.Combine(data, "27_cup.gdml")))
            {
                Shot(d, "a304-27_cup");
                // Стакан: дно 30×2 и стенка 2×38, половина; зеркало вторая.
                Area("27_cup: площадь разреза", d, true, "G4_Al", 2.0 * (30.0 * 2.0 + 2.0 * 38.0));
                PointF inside = Screen(d, true, 25.0, 14.0);
                Check("27_cup: внутри стакана пусто", d.Bitmap.GetPixel((int)inside.X, (int)inside.Y).ToArgb() == Canvas.ToArgb(), "точка z=25 r=14");
            }

            using (Drawing d = DrawFile(Path.Combine(data, "37_ctrl_gp.gdml")))
            {
                Area("37_ctrl_gp: выпуклый профиль, разрез", d, true, "G4_Al", 2.0 * 10.0 * 10.0);
                Area("37_ctrl_gp: выпуклый профиль, торец", d, false, "G4_Al", Math.PI * (20.0 * 20.0 - 10.0 * 10.0));
            }

            if (real == null)
            {
                Console.WriteLine("  (пять профилей GS2020 — ключом --real)");
                return;
            }

            GdmlGeometry th = GdmlGeometry.Read(Path.Combine(real, "GS2020_marinelli_th232.gdml"));
            foreach (string name in new[] { "S_Al_housing_1", "S_Reflector", "S_Al_housing_2", "S_Epoxy_seal_mid", "S_Epoxy_seal_top" })
            {
                GdmlVolumeInstance volume = th.Volumes.Find(v => v.Solid != null && v.Solid.Name == name);
                if (volume == null)
                {
                    Check(name, false, "нет такого тела");
                    continue;
                }

                double truth = 2.0 * Math.Abs(Shoelace(volume.Solid.Profile));
                double drawn = CollectedMm2(th, volume, true);
                Check(name + ": площадь сечения", Math.Abs(drawn / truth - 1.0) < 0.02,
                      "нарисовано " + Num(drawn) + " мм², профиль " + Num(truth) + " мм², ×" + (drawn / truth).ToString("0.00", CultureInfo.InvariantCulture) + ", вершин " + volume.Solid.Profile.Count);
            }
        }

        static double Shoelace(List<double[]> profile)
        {
            double sum = 0.0;
            for (int i = 0; i < profile.Count; i++)
            {
                double[] a = profile[i];
                double[] b = profile[(i + 1) % profile.Count];
                sum += a[0] * b[1] - b[0] * a[1];
            }

            return sum / 2.0;
        }

        /// <summary>Площадь сечения одного тома тем же кодом, что и чертёж, без контуров и подписей: область в мм².</summary>
        static double CollectedMm2(GdmlGeometry g, GdmlVolumeInstance volume, bool axial)
        {
            // Размер большой: область растрируется по целым точкам, и тонкий профиль иначе
            // терял бы проценты на округлении. Поверхности такого размера нет и не нужно.
            Size size = new Size(60000, 30000);
            using (Drawing d = new Drawing { Geometry = g, Sketch = new GdmlSketch(), Bitmap = new Bitmap(1, 1) })
            using (Bitmap surface = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(surface))
            using (Region region = new Region())
            using (GraphicsPath lines = new GraphicsPath())
            using (Matrix identity = new Matrix())
            {
                d.Sketch.Size = size;
                d.Sketch.SetGeometry(g);
                d.Left = new Rectangle(0, 0, (size.Width - 16) / 2, size.Height);
                d.Right = new Rectangle((size.Width - 16) / 2 + 16, 0, size.Width - (size.Width - 16) / 2 - 16, size.Height);
                int normal;
                object frame = Frame(d, axial, out normal);
                object[] axes = { axial, g.Axis, 0, 0, 0 };
                typeof(GdmlSketch).GetMethod("ViewAxes", Any).Invoke(null, axes);
                Type contextType = typeof(GdmlSketch).GetNestedType("ViewContext", Any);
                object context = Activator.CreateInstance(contextType, true);
                contextType.GetField("Graphics").SetValue(context, graphics);
                contextType.GetField("Frame").SetValue(context, frame);
                contextType.GetField("Volume").SetValue(context, volume);
                contextType.GetField("Normal").SetValue(context, axes[2]);
                contextType.GetField("U").SetValue(context, axes[3]);
                contextType.GetField("V").SetValue(context, axes[4]);
                contextType.GetField("Plane").SetValue(context, 0.0);
                region.MakeEmpty();
                typeof(GdmlSketch).GetMethod("CollectShape", Any).Invoke(d.Sketch, new[] { context, volume.Solid, volume.Transform, region, lines });
                double pixels = 0.0;
                foreach (RectangleF scan in region.GetRegionScans(identity))
                {
                    pixels += scan.Width * scan.Height;
                }

                double scale = Scale(d, axial);
                return pixels / (scale * scale);
            }
        }

        // ------------------------------------------------------------------
        //  (4) частные случаи
        // ------------------------------------------------------------------

        static void SpecialCases()
        {
            Console.WriteLine();
            Console.WriteLine("=== (4) ось y, поворот, смещение, неполный оборот, незнакомые тела ===");

            using (Drawing d = DrawFile(Path.Combine(data, "28_ylong_box.gdml")))
            {
                Shot(d, "a304-28_ylong_box");
                Check("28_ylong_box: ось", d.Geometry.Axis == 'y', d.Geometry.Axis.ToString());
                Area("28_ylong_box: разрез вдоль оси y (плоскость z = 0)", d, true, "G4_Al", 100.0 * 20.0);
                Area("28_ylong_box: торец", d, false, "G4_Al", 20.0 * 10.0);
                Check("28_ylong_box: заголовок разреза про z", Titles(d)[0] == "Axial section (z = 0)", Titles(d)[0]);
            }

            // Читатель берёт ось у самого большого тела вращения (`DetectAxis`): здесь ось x, торец поперёк неё.
            using (Drawing d = DrawFile(Path.Combine(data, "34_pc_rot_end.gdml")))
            {
                Shot(d, "a304-34_pc_rot_end");
                Check("34_pc_rot_end: ось", d.Geometry.Axis == 'x', d.Geometry.Axis.ToString());
                Area("34_pc_rot_end: разрез 100×10", d, true, "G4_Al", 100.0 * 10.0);
                Area("34_pc_rot_end: торец — круг r=5", d, false, "G4_Al", Math.PI * 25.0);
                Check("34_pc_rot_end: заголовок торца про ось x", Titles(d)[1].Contains("x = 0"), Titles(d)[1]);
            }

            using (Drawing d = DrawFile(Inline("pc_rot_x")))
            {
                Check("поворот вокруг x: ось y", d.Geometry.Axis == 'y', d.Geometry.Axis.ToString());
                Area("поворот вокруг x: разрез 100×10", d, true, "G4_Al", 100.0 * 10.0);
                Area("поворот вокруг x: торец — круг r=5", d, false, "G4_Al", Math.PI * 25.0);
            }

            using (Drawing d = DrawFile(Inline("pc_rad")))
            {
                Check("поворот 1.5707963 рад: обычная запись, подписи нет", d.Notes.Count == 0, string.Join(" | ", d.Notes));
                Area("поворот 1.5707963 рад: разрез", d, true, "G4_Al", 100.0 * 10.0);
            }

            using (Drawing d = DrawFile(Inline("pc_rad_oblique")))
            {
                Check("поворот 1.57 рад: уже косо, подпись", d.Notes.Exists(n => n.StartsWith("No exact", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("pc_flip")))
            {
                // Ось наоборот: на z = −10 мира лежит локальная z = +10, это r = 5, а не r = 20.
                Area("ось наоборот: кольца с верным знаком", d, false, "G4_Al", Math.PI * 25.0);
            }

            using (Drawing d = DrawFile(Inline("face_in_plane")))
            {
                bool ink = false;
                PointF middle = Screen(d, true, 10.0, 10.0);
                for (int dy = -2; dy <= 2; dy++)
                {
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        ink |= d.Bitmap.GetPixel((int)middle.X + dx, (int)middle.Y + dy).ToArgb() != Canvas.ToArgb();
                    }
                }

                Check("грань в плоскости: контур целиком", ink, "середина гипотенузы");
            }

            foreach (string length in new[] { "1e3", "1e5" })
            {
                using (Drawing d = DrawFile(Inline("band_" + length)))
                {
                    bool note = d.Notes.Exists(n => n.StartsWith("No exact", StringComparison.Ordinal));
                    Check("вырез длиной " + length + (length == "1e5" ? ": зажим сменил фигуру, подпись" : ": зажима нет, подписи нет"), note == (length == "1e5"),
                          "закрашено " + Num(FillMm2(d, true, "G4_Al")) + " мм²; " + string.Join(" | ", d.Notes));
                }
            }

            using (Drawing d = DrawFile(Inline("tiny_ring")))
            {
                Check("кольцо меньше точки в плоскости: «не пересекает» не говорится", !d.Notes.Exists(n => n.Contains("crosses no body")), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("tiny_box")))
            {
                Check("коробка меньше точки в плоскости: «не пересекает» не говорится", !d.Notes.Exists(n => n.Contains("crosses no body")), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "29_mesh_chord.gdml")))
            {
                // Метка от ширины вида: высокое тонкое тело давало метку в три точки.
                List<KeyValuePair<RectangleF, double>> scales = (List<KeyValuePair<RectangleF, double>>)Field(d.Sketch, "paintScales");
                RectangleF bar = scales[scales.Count - 1].Key;
                Check("метка масштаба: не меньше десятой части вида", bar.Width >= d.Right.Width / 10f && bar.Width <= d.Right.Width / 4f + 2f, bar.Width + " точек при ширине вида " + d.Right.Width);
                int inked = 0;
                for (int x = (int)bar.Left; x <= (int)bar.Right; x++)
                {
                    if (d.Bitmap.GetPixel(x, (int)(bar.Top + bar.Height / 2f)).R < 120) inked++;
                }

                Check("метка масштаба: нарисована той длины", Math.Abs(inked - bar.Width) < 3f, inked + " точек, ждали " + bar.Width);
            }

            using (Drawing d = DrawFile(Inline("pc_oblique")))
            {
                Check("косо к плоскости торца: подпись, а не другая фигура", d.Notes.Count == 1 && d.Notes[0].StartsWith("No exact section", StringComparison.Ordinal) && d.Notes[0].Contains("A"), string.Join(" | ", d.Notes));
                Area("косо: разрез с осью в плоскости есть", d, true, "G4_Al", 100.0 * 10.0);
                Check("косо: фигуры в торце нет", FillMm2(d, false, "G4_Al") == 0.0, Num(FillMm2(d, false, "G4_Al")));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "33_pc_offset_y.gdml")))
            {
                Shot(d, "a304-33_pc_offset_y");
                // r = 30, ось в y = 20: на плоскости y = 0 полухорда sqrt(900 − 400).
                Area("33_pc_offset_y: хорда 44.72×100", d, true, "G4_Al", 2.0 * Math.Sqrt(500.0) * 100.0);
                Check("33_pc_offset_y: хорда подписана", d.Notes.Count == 1 && d.Notes[0].StartsWith("The plane misses the axis", StringComparison.Ordinal) && d.Notes[0].EndsWith("A", StringComparison.Ordinal), string.Join(" | ", d.Notes));
                Check("33_pc_offset_y: заголовок", Titles(d)[0] == "Axial section (y = 0)", Titles(d)[0]);
                Area("33_pc_offset_y: торец — круг r=30 со смещением", d, false, "G4_Al", Math.PI * 900.0);
                PointF center = Screen(d, false, 0.0, 20.0);
                Check("33_pc_offset_y: центр круга в y=20", d.Bitmap.GetPixel((int)center.X + 3, (int)center.Y + 3).ToArgb() == Fill(d, "G4_Al").ToArgb(), "точка вида");
            }

            using (Drawing d = DrawFile(Inline("pc_far_y")))
            {
                Area("прибор вдали от y=0: разрез через середину габарита", d, true, "G4_Al", 100.0 * 10.0);
                Check("прибор вдали от y=0: плоскость в заголовке", Titles(d)[0] == "Axial section (y = 40 mm)", Titles(d)[0]);
                Check("прибор вдали от y=0: правило названо", d.Notes.Count == 1 && d.Notes[0].StartsWith("The plane y = 0 misses the device", StringComparison.Ordinal), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "35_partial_phi.gdml")))
            {
                Shot(d, "a304-35_partial_phi");
                Area("35_partial_phi: разрез — одна половина", d, true, "G4_Al", 20.0 * 50.0);
                Area("35_partial_phi: торец — четверть круга", d, false, "G4_Al", Math.PI * 400.0 / 4.0);
            }

            using (Drawing d = DrawFile(Inline("partial_offset")))
            {
                Check("неполный оборот на хорде: подпись", d.Notes.Exists(n => n.StartsWith("No exact section", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("gp_vertex_on_plane")))
            {
                // Плоскость торца z = 0 проходит через вершины r = 30 и r = 5.
                Area("вершина профиля на плоскости торца: кольцо 5…30", d, false, "G4_Al", Math.PI * (900.0 - 25.0));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "22_union.gdml")))
            {
                Shot(d, "a304-22_union");
                Check("22_union: union и tube рисуются, подпись объединения", d.Notes.Count >= 2 && d.Notes[0].StartsWith("Union", StringComparison.Ordinal), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("no_placement")))
            {
                Check("положение не прочитано: тело не в начале координат", FillMm2(d, true, "G4_Al") == 0.0 && d.Notes.Count > 0, string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("no_placement_mix")))
            {
                Check("положение одного тела не прочитано: его нет в начале координат", FillMm2(d, true, "G4_Pb") == 0.0 && d.Notes.Count == 2 && d.Notes[0].Contains("B"), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("too_many")))
            {
                string text = d.Geometry.Describe();
                Check("предел томов: чертежа нет, причина в сводке", d.Geometry.Volumes.Count == 0 && d.Failure == null && text.Contains("more than"), text.Trim().Replace(Environment.NewLine, " / "));
            }

            using (Drawing d = Draw(Hand(GdmlTransform.Identity(), -5.0, 5.0, 0.0), new Size(1400, 700)))
            {
                Check("пересекает, подписей нет", d.Notes.Count == 0, string.Join(" | ", d.Notes));
            }

            // Единицы вида: подпись масштаба зовёт ключ, а не литерал.
            using (Drawing en = DrawFile(Path.Combine(data, "37_ctrl_gp.gdml")))
            {
                Thread.CurrentThread.CurrentUICulture = new CultureInfo("ru");
                using (Drawing ru = Draw(en.Geometry, new Size(1400, 700)))
                {
                    int differ = 0;
                    List<KeyValuePair<RectangleF, double>> scales = (List<KeyValuePair<RectangleF, double>>)Field(ru.Sketch, "paintScales");
                    RectangleF bar = scales[0].Key;
                    for (int y = (int)bar.Top - 8; y < (int)bar.Bottom + 8; y++)
                    {
                        for (int x = (int)bar.Right + 2; x < (int)bar.Right + 80; x++)
                        {
                            if (en.Bitmap.GetPixel(x, y).ToArgb() != ru.Bitmap.GetPixel(x, y).ToArgb()) differ++;
                        }
                    }

                    Check("подпись масштаба на языке интерфейса", differ > 0, "отличий точек ru от en: " + differ);
                }

                Thread.CurrentThread.CurrentUICulture = CultureInfo.InvariantCulture;
            }
        }

        // ------------------------------------------------------------------
        //  вычитание
        // ------------------------------------------------------------------

        /// <summary>`tube`, `cone`, `union`: площади сечений считаются по формулам, контур объединения общий.</summary>
        static void NewBodies()
        {
            Console.WriteLine();
            Console.WriteLine("=== tube, cone, union ===");
            using (Drawing d = DrawFile(Path.Combine(data, "a_tube.gdml")))
            {
                Area("a_tube: разрез — две полосы", d, true, "G4_Al", 2.0 * 5.0 * 20.0);
                Area("a_tube: торец — кольцо", d, false, "G4_Al", Math.PI * (100.0 - 25.0));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_tube_phi.gdml")))
            {
                Area("a_tube_phi: торец — сектор 90°", d, false, "G4_Al", Math.PI * (900.0 - 100.0) / 4.0);
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_cone.gdml")))
            {
                Area("a_cone: разрез — трапеция", d, true, "G4_Al", (2.0 * 10.0 + 2.0 * 20.0) / 2.0 * 30.0);
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_cone_hole_phi.gdml")))
            {
                Check("a_cone_hole_phi: чертёж без отказа", d.Notes.Count == 0, string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_union_boxes.gdml")))
            {
                Area("a_union_boxes: общая площадь, не сумма", d, true, "G4_Al", 15.0 * 10.0);
                Check("a_union_boxes: подпись объединения", d.Notes.Exists(n => n.StartsWith("Union", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_union_overlap.gdml")))
            {
                // Объём не измерен, а сечение известно: разрез y = 0, x −10…15, z −10…10.
                Area("a_union_overlap: сечение есть и без объёма", d, true, "G4_Al", 25.0 * 20.0);
                PointF inner = Screen(d, true, -5.0, 3.0);
                Check("a_union_overlap: граница второго тела внутри первого не нарисована",
                      d.Bitmap.GetPixel((int)Math.Round(inner.X), (int)Math.Round(inner.Y)).ToArgb() == Fill(d, "G4_Al").ToArgb(), "контур внутри");
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_union_nested.gdml")))
            {
                Check("a_union_nested: чертёж без отказа", d.Notes.Exists(n => n.StartsWith("Union", StringComparison.Ordinal))
                      && !d.Notes.Exists(n => n.StartsWith("No exact", StringComparison.Ordinal)), string.Join(" | ", d.Notes));

                // Три части в ряд: куб −5…5, куб 95…105, труба −105…−95, все три сечения по 10×10.
                Area("a_union_nested: все три части закрашены", d, true, "G4_Al", 300.0);
                foreach (double x in new[] { -100.0, 0.0, 100.0 })
                {
                    Check("a_union_nested: контур части x = " + Num(x), IsInk(d, Screen(d, true, x, 5.0)) && IsInk(d, Screen(d, true, x + 5.0, 2.5)), "верх и правый край");
                }
            }

            Contours();

            using (Drawing d = DrawFile(Path.Combine(data, "a_union_unknown.gdml")))
            {
                Check("a_union_unknown: габарита нет, фигуры нет, подпись", d.Notes.Exists(n => n.StartsWith("No drawing", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            }

            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            using (Drawing d = DrawFile(Inline("union_big")))
            {
                Check("union_big: за бюджетом вершин — подпись вместо чертежа", d.Notes.Exists(n => n.StartsWith("Not drawn, too complex", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            }

            Check("union_big: быстро", watch.ElapsedMilliseconds < 20000, watch.ElapsedMilliseconds.ToString(CultureInfo.InvariantCulture) + " мс");
            using (Drawing d = DrawFile(Inline("union_chain")))
            {
                Check("union_chain: глубже предела — подпись, процесс жив", d.Notes.Count > 0, string.Join(" | ", d.Notes));
            }
        }

        static bool Near(Color a, Color b)
        {
            return Math.Abs(a.R - b.R) <= 45 && Math.Abs(a.G - b.G) <= 45 && Math.Abs(a.B - b.B) <= 45;
        }

        /// <summary>Точка контура: не холст и не заливка, рядом темнее заливки.</summary>
        static bool IsInk(Drawing d, PointF point)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    Color c = d.Bitmap.GetPixel((int)Math.Round(point.X) + dx, (int)Math.Round(point.Y) + dy);
                    if ((c.R + c.G + c.B) / 3 < 160) return true;
                }
            }

            return false;
        }

        /// <summary>Вокруг точки только этот цвет: ни контура, ни шва.</summary>
        static bool IsPure(Drawing d, PointF point, Color color)
        {
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    if (d.Bitmap.GetPixel((int)Math.Round(point.X) + dx, (int)Math.Round(point.Y) + dy).ToArgb() != color.ToArgb()) return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Контур составного тела — край самой области (`critGdml5`, Н-А): при трёх и более частях
        /// и при объединении с вычитанием. Ось и линия A-A на этих точках не лежат: точки выбраны в стороне.
        /// </summary>
        static void Contours()
        {
            using (Drawing d = DrawFile(Path.Combine(data, "n1_nested_overlap.gdml")))
            {
                Color fill = Fill(d, "Al");
                // Коробки −20…20, 0…40, 20…60 по x, z −20…20. Шов на x = 0, 20, 40 был бы внутри тела.
                Area("n1: общая площадь", d, true, "Al", 80.0 * 40.0);
                // Шов на x = 20 закрыла бы линия A-A: она там и стоит.
                foreach (double x in new[] { 0.0, 40.0 })
                {
                    Check("n1: шва нет на x = " + Num(x), IsPure(d, Screen(d, true, x, 10.0), fill), "середина тела");
                }

                foreach (double x in new[] { -10.0, 10.0, 30.0, 50.0 })
                {
                    Check("n1: верхний край есть на x = " + Num(x), IsInk(d, Screen(d, true, x, 20.0)), "край области");
                    Check("n1: нижний край есть на x = " + Num(x), IsInk(d, Screen(d, true, x, -20.0)), "край области");
                }
            }

            using (Drawing d = DrawFile(Path.Combine(data, "n2_union_of_sub.gdml")))
            {
                // Коробка −20…20 без угла x 10…20, z 10…20; рядом отдельная коробка −45…−35.
                Area("n2: площадь с вырезанным углом", d, true, "Al", 40.0 * 40.0 - 100.0 + 100.0);
                Check("n2: в вырезе холст", IsPure(d, Screen(d, true, 15.0, 15.0), Canvas), "точка в углу");
                Check("n2: контур не идёт по вырезанному углу, правый край", IsPure(d, Screen(d, true, 20.0, 15.0), Canvas), "x = 20");
                Check("n2: контур не идёт по вырезанному углу, верхний край", IsPure(d, Screen(d, true, 15.0, 20.0), Canvas), "z = 20");
                Check("n2: край выреза обведён, стенка", IsInk(d, Screen(d, true, 10.0, 15.0)), "x = 10");
                Check("n2: край выреза обведён, дно", IsInk(d, Screen(d, true, 15.0, 10.0)), "z = 10");
                Check("n2: вторая часть обведена", IsInk(d, Screen(d, true, -40.0, 5.0)), "z = 5");
            }

            using (Drawing d = DrawFile(Inline("union_protrude")))
            {
                // Объединение (вычитание с выступающим вырезом) и коробки: ребро выреза вне тела не рисуется.
                PointF outside = Screen(d, true, 30.0, 10.0);
                Check("вырез выступает за тело: его ребра вне тела нет", IsPure(d, outside, Canvas), "точка x = 30, z = 10");
            }
        }

        /// <summary>
        /// Шов: половины сечения и части полиакона в одной заливке, контур не идёт по оси. Штрих оси
        /// лежит поверх с разрывами: в разрывах на строке оси чистая заливка; со швом таких точек нет.
        /// </summary>
        static void Seams()
        {
            Console.WriteLine();
            Console.WriteLine("=== шов по оси и между плоскостями ===");
            foreach (string[] item in new[]
            {
                new[] { "pc_three_planes", "G4_Al", "тело вращения" },
                new[] { "chord_three_planes", "G4_Al", "хорда" },
            })
            {
                using (Drawing d = DrawFile(Inline(item[0])))
                {
                    Color fill = Fill(d, item[1]);
                    PointF left = Screen(d, true, -40.0, 0.0);
                    PointF right = Screen(d, true, 40.0, 0.0);
                    int pure = 0, all = 0;
                    // Обе строки, которых касается перо шириной 0.9 на этой высоте: шов закрасил бы их на всём протяжении.
                    int rowA = (int)Math.Floor(left.Y - 0.45f), rowB = (int)Math.Floor(left.Y + 0.45f);
                    for (int x = (int)left.X; x < (int)right.X; x++)
                    {
                        all++;
                        if (NearFill(d.Bitmap.GetPixel(x, rowA), fill) && NearFill(d.Bitmap.GetPixel(x, rowB), fill)) pure++;
                    }

                    Check(item[2] + ": на оси нет шва", pure > all / 5, "чистых точек на строке оси " + pure + " из " + all);
                    Check(item[2] + ": нет шва между плоскостями", IsPure(d, Screen(d, true, -20.0, 5.0), fill), "z = -20, плоскость посередине");
                }
            }

            using (Drawing d = DrawFile(Path.Combine(data, "35_partial_phi.gdml")))
            {
                // Половина одна: ребро на оси — настоящий край, его срезала плоскость сектора. Штрих оси идёт с
                // разрывами, поэтому край виден в разрывах: чистых столбцов, где темнее заливки ничего нет, почти нет.
                GdmlVolumeInstance body = d.Geometry.Volumes[0];
                int along = d.Geometry.Axis == 'x' ? 0 : 2;
                PointF left = Screen(d, true, body.Min[along] + 2.0, 0.0);
                PointF right = Screen(d, true, body.Max[along] - 2.0, 0.0);
                int rowA = (int)Math.Floor(left.Y - 0.45f), rowB = (int)Math.Floor(left.Y + 0.45f);
                Color fill = Fill(d, body.MaterialName);
                int fillLight = (fill.R + fill.G + fill.B) / 3;
                int clean = 0, all = 0;
                for (int x = (int)left.X; x < (int)right.X; x++)
                {
                    all++;
                    Color a = d.Bitmap.GetPixel(x, rowA), b = d.Bitmap.GetPixel(x, rowB);
                    if ((a.R + a.G + a.B) / 3 >= fillLight - 8 && (b.R + b.G + b.B) / 3 >= fillLight - 8) clean++;
                }

                Check("неполный оборот: край на оси обведён", clean < all / 10, "чистых столбцов " + clean + " из " + all);
            }
        }

        static bool SegmentHits(PointF a, PointF b, RectangleF box)
        {
            object[] args = { box, a, b };
            return (bool)typeof(GdmlSketch).GetMethod("ClipSegment", Any).Invoke(null, args);
        }

        static GdmlOverview OverviewOf(string file)
        {
            return GdmlOverview.Of(GdmlGeometry.Read(file));
        }

        static string RoleText(GdmlOverview o, string material)
        {
            GdmlMaterialRow row = o.Materials.Find(r => r.Name == material);
            return row == null ? "нет вещества" : row.Role.ToString();
        }

        /// <summary>«Не узнано — роли нет»: незнакомые имена, домик, подстрока в имени (`critDesign`).</summary>
        /// <summary>Настоящий файл столба без говорящих имён: `gamma1s_*` в каталоге ключа `--column`.</summary>
        static string column;

        static string Tube(string name, double rmin, double rmax, double z)
        {
            return string.Format(CultureInfo.InvariantCulture, "<tube name=\"{0}\" rmin=\"{1}\" rmax=\"{2}\" z=\"{3}\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"/>", name, rmin, rmax, z);
        }

        static string At(double z)
        {
            return string.Format(CultureInfo.InvariantCulture, "<position name=\"p{0}\" x=\"0\" y=\"0\" z=\"{1}\" unit=\"mm\"/>", Guid.NewGuid().ToString("N"), z);
        }

        /// <summary>
        /// (`AMBER317`) Столб без говорящих имён: кристалл узнаётся по веществу, отражатель по веществу и месту, корпус — наружная
        /// оболочка плотного столба, хотя он много длиннее кристалла; вид с торца идёт через кристалл.
        /// </summary>
        static void ColumnRoles()
        {
            Console.WriteLine();
            Console.WriteLine("=== столб без говорящих имён ===");
            string solids = Tube("Sc", 0.0, 31.5, 63.0) + Tube("Pw", 31.5, 35.0, 63.0) + Tube("Can", 35.0, 36.0, 70.0) + Tube("Sl", 36.0, 38.0, 190.0)
                + Tube("Tb", 38.0, 40.0, 310.0) + Tube("Tail", 0.0, 38.0, 120.0) + Tube("Far", 0.0, 30.0, 10.0) + Tube("Pb", 150.0, 200.0, 500.0);
            string vols = "</solids><structure>" + Vol("Scint", "Sc", "G4_SODIUM_IODIDE") + Vol("Powder", "Pw", "MgO_powder") + Vol("Can1", "Can", "G4_Al")
                + Vol("Sleeve", "Sl", "G4_RUBBER_NATURAL") + Vol("Tube1", "Tb", "G4_Al") + Vol("Boards", "Tail", "G4_Fe") + Vol("Spare", "Far", "MgO_powder") + Vol("Castle", "Pb", "G4_Pb")
                + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>"
                + Phys("Scint", At(0.0)) + Phys("Powder", At(0.0)) + Phys("Can1", At(2.0)) + Phys("Sleeve", At(-50.0)) + Phys("Tube1", At(-110.0)) + Phys("Boards", At(-200.0))
                + Phys("Spare", At(-400.0)) + Phys("Castle", At(-100.0));
            string file = Inline("column_roles");
            File.WriteAllText(file, "<?xml version=\"1.0\"?><gdml><define/><materials>"
                + "<element name=\"Mg\" Z=\"12\" formula=\"Mg\"><atom value=\"24.3\"/></element><element name=\"O\" Z=\"8\" formula=\"O\"><atom value=\"16\"/></element>"
                + "<material name=\"MgO_powder\" state=\"solid\"><D unit=\"g/cm3\" value=\"0.8\"/><fraction n=\"0.603\" ref=\"Mg\"/><fraction n=\"0.397\" ref=\"O\"/></material>"
                + "</materials><solids><box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/>" + solids + vols
                + "</volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup></gdml>");
            GdmlGeometry g = GdmlGeometry.Read(file);
            GdmlOverview o = GdmlOverview.Of(g);
            Check("столб: кристалл по веществу в чтении", g.CrystalName == "Scint", g.CrystalName);
            Check("столб: кристалл в ролях тот же", o.Crystal != null && o.Crystal.VolumeName == "Scint", o.Crystal == null ? "?" : o.Crystal.VolumeName);
            Check("столб: вид с торца через кристалл", Math.Abs(g.EndPlane) < 1e-6, g.EndPlane.ToString(CultureInfo.InvariantCulture));
            Check("столб: корпус — наружная оболочка", o.DeviceBody != null && o.DeviceBody.VolumeName == "Tube1", o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName);
            Check("столб: порошок — отражатель", RoleText(o, "MgO_powder") == "Reflector", RoleText(o, "MgO_powder"));
            Check("столб: алюминий — корпус", RoleText(o, "G4_Al") == "Housing", RoleText(o, "G4_Al"));
            Check("столб: свинец домика без роли", RoleText(o, "G4_Pb") == "None", RoleText(o, "G4_Pb"));
            Check("столб: прибор целиком", o.DeviceSizeText().StartsWith("80 × 80 × ", StringComparison.Ordinal), o.DeviceSizeText());

            // Вещество отражателя вдали от кристалла — не отражатель.
            string apart = Inline("column_apart");
            string text = File.ReadAllText(file);
            int start = text.IndexOf("<physvol name=\"pv_Powder\">", StringComparison.Ordinal);
            int stop = text.IndexOf("</physvol>", start, StringComparison.Ordinal) + 10;
            File.WriteAllText(apart, text.Remove(start, stop - start));
            o = GdmlOverview.Of(GdmlGeometry.Read(apart));
            Check("столб без порошка у кристалла: вдали это не отражатель", RoleText(o, "MgO_powder") == "None", RoleText(o, "MgO_powder"));

            // За зазором уже не столб: колодец сосуда вокруг кристалла корпусом не становится.
            string gap = Inline("column_gap");
            File.WriteAllText(gap, "<?xml version=\"1.0\"?><gdml><define/><solids><box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/>"
                + Tube("Sc", 0.0, 31.5, 63.0) + Tube("Wl", 48.0, 50.0, 80.0) + "</solids><structure>" + Vol("Scint", "Sc", "G4_SODIUM_IODIDE") + Vol("Well", "Wl", "G4_POLYPROPYLENE")
                + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>" + Phys("Scint", At(0.0)) + Phys("Well", At(0.0))
                + "</volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup></gdml>");
            o = GdmlOverview.Of(GdmlGeometry.Read(gap));
            Check("колодец за зазором не корпус", o.DeviceBody == null, o.DeviceBody == null ? "" : o.DeviceBody.VolumeName);

            // Тесные оболочки, которые корпусом не являются, и проба из вещества кристалла.
            foreach (string[] extra in new[]
            {
                new[] { "колодец маринелли PP вплотную", Tube("Wl", 40.0, 41.5, 314.0), Vol("Well", "Wl", "G4_POLYPROPYLENE"), "Well", "-110" },
                new[] { "стакан фторопласта вплотную", Tube("Cp", 40.0, 42.0, 320.0), Vol("Cup", "Cp", "G4_TEFLON"), "Cup", "-110" },
                new[] { "медная гильза домика вплотную", Tube("Ln", 40.0, 41.0, 400.0), Vol("Sleeve2", "Ln", "G4_Cu"), "Sleeve2", "-110" },
                new[] { "стальная защита вплотную, по имени", Tube("Sh", 40.0, 41.0, 330.0), Vol("Shield_inner", "Sh", "G4_STAINLESS-STEEL"), "Shield_inner", "-110" },
                new[] { "алюминий за зазором", Tube("Gp", 45.0, 46.0, 330.0), Vol("Outer_tube", "Gp", "G4_Al"), "Outer_tube", "-110" },
                new[] { "проба NaI крупнее кристалла", "<box name=\"Sb\" x=\"200\" y=\"200\" z=\"60\" lunit=\"mm\"/>", Vol("Salt_sample", "Sb", "G4_SODIUM_IODIDE"), "Salt_sample", "120" },
            })
            {
                string extraFile = Inline("column_extra");
                File.WriteAllText(extraFile, "<?xml version=\"1.0\"?><gdml><define/><materials>"
                    + "<element name=\"Mg\" Z=\"12\" formula=\"Mg\"><atom value=\"24.3\"/></element><element name=\"O\" Z=\"8\" formula=\"O\"><atom value=\"16\"/></element>"
                    + "<material name=\"MgO_powder\" state=\"solid\"><D unit=\"g/cm3\" value=\"0.8\"/><fraction n=\"0.603\" ref=\"Mg\"/><fraction n=\"0.397\" ref=\"O\"/></material>"
                    + "</materials><solids><box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/>"
                    + Tube("Sc", 0.0, 31.5, 63.0) + Tube("Pw", 31.5, 35.0, 63.0) + Tube("Can", 35.0, 36.0, 70.0) + Tube("Sl", 36.0, 38.0, 190.0) + Tube("Tb", 38.0, 40.0, 310.0) + extra[1]
                    + "</solids><structure>" + Vol("Scint", "Sc", "G4_SODIUM_IODIDE") + Vol("Powder", "Pw", "MgO_powder") + Vol("Can1", "Can", "G4_Al")
                    + Vol("Sleeve", "Sl", "G4_RUBBER_NATURAL") + Vol("Tube1", "Tb", "G4_Al") + extra[2]
                    + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>"
                    + Phys("Scint", At(0.0)) + Phys("Powder", At(0.0)) + Phys("Can1", At(2.0)) + Phys("Sleeve", At(-50.0)) + Phys("Tube1", At(-110.0))
                    + Phys(extra[3], At(double.Parse(extra[4], CultureInfo.InvariantCulture)))
                    + "</volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup></gdml>");
                g = GdmlGeometry.Read(extraFile);
                o = GdmlOverview.Of(g);
                Check("столб: " + extra[0] + ": корпус прежний", o.Crystal != null && o.Crystal.VolumeName == "Scint" && g.CrystalName == "Scint"
                      && o.DeviceBody != null && o.DeviceBody.VolumeName == "Tube1" && o.DeviceSizeText().StartsWith("80 × 80 × ", StringComparison.Ordinal),
                      (o.Crystal == null ? "?" : o.Crystal.VolumeName) + ", " + (o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName) + ", " + o.DeviceSizeText());
            }

            // Случаи `critTop`: кристалл, MgO, Al-банка ⌀71.3, Al-корпус ⌀78.3 над пустым слоем воздуха. До 3 мм на сторону —
            // корпус, алюминий за 5 мм — уже не корпус, прибор по банке.
            foreach (double air in new[] { 0.0, 1.0, 1.6, 2.0, 3.0, 5.0 })
            {
                bool housing = air < 4.0;
                double rmin = 35.65 + air, rmax = housing ? 39.15 : rmin + 0.5;
                string airFile = Inline("column_air");
                File.WriteAllText(airFile, "<?xml version=\"1.0\"?><gdml><define/><materials>"
                    + "<element name=\"Mg\" Z=\"12\" formula=\"Mg\"><atom value=\"24.3\"/></element><element name=\"O\" Z=\"8\" formula=\"O\"><atom value=\"16\"/></element>"
                    + "<material name=\"MgO_powder\" state=\"solid\"><D unit=\"g/cm3\" value=\"0.8\"/><fraction n=\"0.603\" ref=\"Mg\"/><fraction n=\"0.397\" ref=\"O\"/></material>"
                    + "</materials><solids><box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/>"
                    + Tube("Sc", 0.0, 31.5, 63.0) + Tube("Pw", 31.5, 35.0, 63.0) + Tube("Cn", 35.0, 35.65, 70.0) + Tube("Cs", rmin, rmax, 80.0)
                    + "</solids><structure>" + Vol("Scint", "Sc", "G4_SODIUM_IODIDE") + Vol("Powder", "Pw", "MgO_powder") + Vol("Can", "Cn", "G4_Al") + Vol("Case", "Cs", "G4_Al")
                    + "<volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/>"
                    + Phys("Scint", At(0.0)) + Phys("Powder", At(0.0)) + Phys("Can", At(0.0)) + Phys("Case", At(0.0))
                    + "</volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"World\"/></setup></gdml>");
                o = GdmlOverview.Of(GdmlGeometry.Read(airFile));
                string want = housing ? "Case" : "Can", size = housing ? "78.3 × 78.3 × " : "71.3 × 71.3 × ";
                Check(string.Format(CultureInfo.InvariantCulture, "столб: воздух {0:F1} мм под корпусом: прибор {1}", air, want),
                      o.DeviceBody != null && o.DeviceBody.VolumeName == want && o.DeviceSizeText().StartsWith(size, StringComparison.Ordinal),
                      (o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName) + ", " + o.DeviceSizeText());
            }

            if (column == null)
            {
                return;
            }

            foreach (string name in new[] { "gamma1s_detector", "gamma1s_geo1_marinelli", "gamma1s_geo1_shield" })
            {
                g = GdmlGeometry.Read(Path.Combine(column, name + ".gdml"));
                o = GdmlOverview.Of(g);
                string detail = (o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName) + ", " + o.DeviceSizeText();
                Check(name + ": прибор", o.DeviceBody != null && o.DeviceBody.VolumeName == "AlCase_side" && o.DeviceSizeText().StartsWith("78.3 × 78.3 × 315", StringComparison.Ordinal), detail);
                Check(name + ": MgO отражатель, Al корпус", RoleText(o, "MgO_powder") == "Reflector" && RoleText(o, "G4_Al") == "Housing", RoleText(o, "MgO_powder") + ", " + RoleText(o, "G4_Al"));
                Check(name + ": кристалл и плоскость торца", g.CrystalName == "NaI" && o.Crystal != null && Math.Abs(g.EndPlane - GdmlGeometry.AxisCenter(o.Crystal, g.Axis)) < 1e-6, g.CrystalName + ", " + g.EndPlane.ToString(CultureInfo.InvariantCulture));
                Check(name + ": проба", (o.Sample != null) == name.Contains("marinelli"), o.Sample == null ? "нет" : o.Sample.VolumeName);
            }
        }

        static void Roles()
        {
            ColumnRoles();
            GdmlOverview o = OverviewOf(Inline("role_castle"));
            Check("домик: прибор — корпус вокруг кристалла, а не домик", o.DeviceBody != null && o.DeviceBody.VolumeName == "Housing" && RoleText(o, "G4_Pb") == "None" && o.DeviceMax[0] - o.DeviceMin[0] < 60.0,
                  (o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName) + ", Pb " + RoleText(o, "G4_Pb") + ", " + o.DeviceSizeText());
            o = OverviewOf(Inline("role_castle_only"));
            Check("только домик вокруг кристалла: прибора нет", o.DeviceBody == null && RoleText(o, "G4_Pb") == "None", RoleText(o, "G4_Pb"));
            o = OverviewOf(Inline("role_lone_box"));
            Check("одна коробка: ни прибора, ни пробы", o.DeviceBody == null && o.Crystal == null && o.Sample == null && RoleText(o, "G4_Al") == "None", RoleText(o, "G4_Al"));
            o = OverviewOf(Inline("role_source_inside"));
            Check("источник в корпусе — не проба", o.Sample == null, o.Sample == null ? "" : o.Sample.VolumeName);
            o = OverviewOf(Inline("role_source_apart"));
            Check("источник поодаль — проба", o.Sample != null && o.Sample.VolumeName == "Point_source" && o.DeviceMax[2] < 50.0, o.DeviceSizeText());
            o = OverviewOf(Inline("role_resource_apart"));
            Check("Resource поодаль — не проба: слово, а не подстрока", o.Sample == null, o.Sample == null ? "" : o.Sample.VolumeName);
            if (real == null)
            {
                return;
            }

            // Имена настоящих файлов заменены: та же геометрия, другая подпись.
            string th = File.ReadAllText(Path.Combine(real, "GS2020_marinelli_th232.gdml"));
            string soil = Inline("role_soil");
            File.WriteAllText(soil, th.Replace("LV_SourceMatrix", "LV_Soil").Replace("LV_VesselWall", "LV_Wall"));
            o = OverviewOf(soil);
            Check("role_soil: пробы и сосуда нет, стенка не корпус", o.Sample == null && o.Vessel == null && RoleText(o, "Vessel_PP") == "None" && RoleText(o, "Epoxy_crumb") == "None", RoleText(o, "Vessel_PP"));
            Check("role_soil: прибор — столб у кристалла", o.DeviceBody != null && o.DeviceBody.VolumeName == "LV_Shell_1_00" && o.DeviceMax[0] - o.DeviceMin[0] < 64.0, o.DeviceSizeText());
            File.Delete(soil);
            string cape = File.ReadAllText(Path.Combine(real, "GS2020_Capescint51x51.gdml"));
            string board = Inline("role_matrix_board");
            File.WriteAllText(board, cape.Replace("LV_Component301_1_34", "LV_Matrix_board"));
            o = OverviewOf(board);
            Check("role_matrix_board: плата не проба", o.Sample == null && RoleText(o, "G4_Al") == "Housing", RoleText(o, "G4_Al"));
            File.Delete(board);
            string sourceless = Inline("role_sourceless");
            File.WriteAllText(sourceless, cape.Replace("LV_Shell_1_00", "LV_Opensource_shell"));
            o = OverviewOf(sourceless);
            Check("role_sourceless: Opensource не проба", o.Sample == null && o.DeviceBody != null && o.DeviceBody.VolumeName == "LV_Opensource_shell", o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName);
            File.Delete(sourceless);

            // Настоящие файлы как прежде.
            foreach (string file in Directory.GetFiles(real, "*.gdml"))
            {
                o = OverviewOf(file);
                string device = o.DeviceSizeText();
                bool rc = Path.GetFileName(file).StartsWith("RC", StringComparison.Ordinal);
                Check(Path.GetFileNameWithoutExtension(file) + ": роли как прежде", o.Crystal != null && o.DeviceBody != null
                      && (rc ? o.DeviceBody.VolumeName == "Case_shell_log" && o.Sample == null
                             : o.DeviceBody.VolumeName == "LV_Shell_1_00" && device.StartsWith("63 × 62.99 × 83", StringComparison.Ordinal)),
                      (o.Crystal == null ? "?" : o.Crystal.VolumeName) + ", " + (o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName) + ", " + (o.Sample == null ? "-" : o.Sample.VolumeName) + ", " + (o.Vessel == null ? "-" : o.Vessel.VolumeName) + ", " + device);
            }
        }

        /// <summary>Порог рамки: деталь не сводит прибор в точку, но большое тело сцены рамку не теряет.</summary>
        static void Thresholds()
        {
            using (Drawing d = DrawFile(Inline("keep_50m")))
            {
                Check("тело 50 м остаётся в рамке", !d.Notes.Exists(n => n.Contains("larger than the rest")) && Drawn(d, true).Width > 200f, string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("trim_wall")))
            {
                Check("стена 150 м при приборе 100 мм вне рамки и названа", d.Notes.Exists(n => n.Contains("larger than the rest") && n.Contains("Wall")), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("keep_gap")))
            {
                Check("200 м при соседе 500 мм: отношение меньше 1000, в рамке", !d.Notes.Exists(n => n.Contains("larger than the rest")), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("un_gap")))
            {
                Check("объединение, плоскость в зазоре: «нарисовано» не говорится", !d.Notes.Exists(n => n.StartsWith("Union", StringComparison.Ordinal)) || !d.Notes.Contains("This plane crosses no body"), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Path.Combine(data, "a_tube_phi.gdml")))
            {
                string hint = (string)typeof(GdmlSketch).GetMethod("HintOf", Any).Invoke(d.Sketch, new[] { ((IList)Field(d.Sketch, "paintSlots"))[0] });
                Check("разрез: подсказка не утверждает «через ось»", !hint.Contains("through the device axis"), hint);
            }

            using (Drawing d = DrawFile(Path.Combine(data, "37_ctrl_gp.gdml")))
            {
                string rtf = GdmlOverview.Of(d.Geometry).SummaryRtf("Tahoma", 8.25f);
                Check("сводка: доли подписаны, отступ свойством абзаца", rtf.Contains("mass fractions") && rtf.Contains("\\li240") && !rtf.Contains("    "), "");
            }
        }

        /// <summary>
        /// Оформление числом, как приёмка чертежа редактора: подписи не налезают друг на друга и на чертёж,
        /// не выходят за окно; выноска показывает в своё тело; у веществ файла разные цвета; легенда и сводка
        /// сходятся с числами читателя.
        /// </summary>
        static void Look()
        {
            Console.WriteLine();
            Console.WriteLine("=== подписи, выноски, легенда, сводка ===");
            List<string> files = new List<string> { Path.Combine(data, "37_ctrl_gp.gdml"), Path.Combine(data, "33_pc_offset_y.gdml"), Path.Combine(data, "35_partial_phi.gdml"), Inline("sub_sibling"), Inline("no_placement_mix") };
            if (real != null)
            {
                files.AddRange(Directory.GetFiles(real, "*.gdml"));
            }

            // Настоящий диапазон чертежа: окно от 900×560 (`MinimumSize`) до 1920×1040, раздел сводки двигается
            // от 240 до «чертежу 320»: замерено на скрытом столе, журнал `handover-2026-11-05-design-gdml.md`.
            Size[] sizes =
            {
                new Size(320, 367), new Size(532, 367), new Size(584, 427), new Size(584, 646), new Size(916, 807), new Size(1185, 847),
                new Size(1400, 700), new Size(1100, 600), new Size(590, 640), new Size(400, 500), new Size(700, 500), new Size(900, 700),
            };
            int leaderNumbers = 0, leaderMarks = 0, leaderDots = 0, leaders = 0;
            int layouts = 0, marks = 0, callouts = 0;
            foreach (string file in files)
            {
                GdmlGeometry g = GdmlGeometry.Read(file);
                string name = Path.GetFileNameWithoutExtension(file);
                foreach (Size size in sizes)
                {
                    using (Drawing d = Draw(g, size))
                    {
                        layouts++;

                        Gost(name + " " + size.Width + "x" + size.Height, d);

                        // Оба размера на обоих видах: при поле, съевшем вертикальный ряд, чертёж молча терял его.
                        if (size.Width >= 584 && size.Height >= 427 && file.StartsWith(real ?? "\0", StringComparison.Ordinal))
                        {
                            int rows = d.Sketch.DimensionLines.Count(x => x.Kind == GeometrySketch.DimLineKind.Dimension);
                            Check(name + " " + size.Width + "x" + size.Height + ": размеров на двух видах", rows == 2 * d.Sketch.ViewContours.Length, "размерных " + rows);
                        }

                        List<GdmlSketch.SketchMark> list = (List<GdmlSketch.SketchMark>)Field(d.Sketch, "paintMarks");
                        marks += list.Count;
                        string label = name + " " + size.Width + "x" + size.Height;
                        List<string> faults = new List<string>();
                        Check(label + ": оба вида есть и не крапинка", Math.Max(Drawn(d, true).Width, Drawn(d, true).Height) >= 60f && Math.Max(Drawn(d, false).Width, Drawn(d, false).Height) >= 40f,
                              Drawn(d, true).Size + ", " + Drawn(d, false).Size);
                        RectangleF body = RectangleF.Union(Drawn(d, true), RectangleF.Empty);
                        for (int i = 0; i < list.Count; i++)
                        {
                            RectangleF a = list[i].Bounds;
                            if (a.Left < -1f || a.Top < -1f || a.Right > size.Width + 2f || a.Bottom > size.Height + 2f)
                            {
                                faults.Add("за окном: " + list[i].Text);
                            }

                            for (int k = i + 1; k < list.Count; k++)
                            {
                                RectangleF c = list[k].Bounds;
                                RectangleF cross = RectangleF.Intersect(a, c);
                                // Запас MeasureString: 3 точки на край считаются касанием, а не наложением.
                                if (cross.Width > 3f && cross.Height > 3f)
                                {
                                    faults.Add("наложение: «" + list[i].Text + "» и «" + list[k].Text + "»");
                                }
                            }

                            foreach (RectangleF drawn in new[] { Drawn(d, true), Drawn(d, false) })
                            {
                                RectangleF cross = RectangleF.Intersect(a, drawn);
                                if (cross.Width > 3f && cross.Height > 3f)
                                {
                                    faults.Add("на чертеже: " + list[i].Text);
                                }
                            }
                        }

                        Check(label + ": подписи без наложения", faults.Count == 0, faults.Count == 0 ? list.Count + " подписей" : string.Join("; ", faults.GetRange(0, Math.Min(4, faults.Count))));

                        foreach (GdmlSketch.SketchCallout callout in (List<GdmlSketch.SketchCallout>)Field(d.Sketch, "paintCallouts"))
                        {
                            callouts++;
                            // Точка лежит в поле своего вида, и рядом с ней цвет вещества тела или его контур.
                            Color color = Fill(d, callout.MaterialName);
                            Color edge = Color.FromArgb(color.R * 2 / 3, color.G * 2 / 3, color.B * 2 / 3);
                            int own = 0;
                            for (int radius = 4; radius <= 6; radius++)
                            {
                                for (int a = 0; a < 16; a++)
                                {
                                    double angle = a * Math.PI / 8.0;
                                    Color seen = d.Bitmap.GetPixel((int)Math.Round(callout.Anchor.X + radius * Math.Cos(angle)), (int)Math.Round(callout.Anchor.Y + radius * Math.Sin(angle)));
                                    // Тонкая стенка в две точки вся из сглаженных краёв: допуск на канал.
                                    if (Near(seen, color) || Near(seen, edge)) own++;
                                }
                            }

                            RectangleF plot = callout.Axial ? d.Left : d.Right;
                            Check(label + ": выноска «" + callout.Role + "»", plot.Contains(callout.Anchor) && own >= 1, "точек цвета тела вокруг " + own + " из 48");

                            // Линия выноски: через число размера не идёт, через другую подпись и чужую точку — по счёту ниже.
                            leaders++;
                            foreach (GdmlSketch.SketchMark mark in list)
                            {
                                RectangleF box = mark.Bounds;
                                if (box.IntersectsWith(callout.Label)) continue;
                                box.Inflate(-1.5f, -1.5f);
                                if (SegmentHits(callout.From, callout.Anchor, box))
                                {
                                    leaderMarks++;
                                    if (mark.Text.Length > 0 && char.IsDigit(mark.Text[0]))
                                    {
                                        leaderNumbers++;
                                        Console.WriteLine("    ⛔ {0}: выноска «{1}» через число «{2}»", label, callout.Role, mark.Text);
                                    }
                                }
                            }

                            foreach (GdmlSketch.SketchCallout other in (List<GdmlSketch.SketchCallout>)Field(d.Sketch, "paintCallouts"))
                            {
                                if (other.Axial == callout.Axial && other.Role != callout.Role
                                    && SegmentHits(callout.From, callout.Anchor, new RectangleF(other.Anchor.X - 3f, other.Anchor.Y - 3f, 6f, 6f)))
                                {
                                    leaderDots++;
                                    Console.WriteLine("    чужая точка: {0}: линия «{1}» через точку «{2}»", label, callout.Role, other.Role);
                                }
                            }
                        }
                    }
                }

                GdmlOverview overview = GdmlOverview.Of(g);
                HashSet<int> colors = new HashSet<int>();
                double massRows = 0.0;
                foreach (GdmlMaterialRow row in overview.Materials)
                {
                    colors.Add(row.Color.ToArgb());
                    massRows += row.MassG;
                }

                Check(name + ": у веществ разные цвета", colors.Count == overview.Materials.Count, colors.Count + " цветов на " + overview.Materials.Count + " веществ");
                double massVolumes = 0.0;
                foreach (GdmlVolumeInstance volume in g.Volumes)
                {
                    if (!double.IsNaN(volume.MassG)) massVolumes += volume.MassG;
                }

                Check(name + ": масса в легенде = сумма томов", Math.Abs(massRows - massVolumes) <= 1e-9 * Math.Max(1.0, massVolumes), Num(massRows) + " г");

                string summary = overview.SummaryText();
                // Замечание либо стоит в сводке, либо дословно повторяет причину в «Не измерено» и стоит там.
                int missing = 0, repeated = 0;
                foreach (string warning in g.Warnings)
                {
                    bool repeat = g.Volumes.Exists(v => v.Solid != null && !v.Solid.VolumeKnown && v.Solid.Note.Length > 0 && warning.EndsWith(v.Solid.Note, StringComparison.Ordinal) && summary.Contains(v.Solid.Note));
                    if (summary.Contains(warning))
                    {
                        repeated += repeat ? 1 : 0;
                    }
                    else if (!repeat)
                    {
                        missing++;
                    }
                }

                Check(name + ": сводка: замечания не повторяют «Не измерено»", repeated == 0, "повторов " + repeated);

                int lost = 0;
                foreach (GdmlVolumeInstance volume in g.Volumes)
                {
                    bool unclear = volume.Solid == null || !volume.Solid.VolumeKnown || !volume.PlacementKnown;
                    if (unclear && !summary.Contains(volume.VolumeName)) lost++;
                }

                Check(name + ": сводка: все замечания и все неизмеренные тома на месте", missing == 0 && lost == 0, "замечаний потеряно " + missing + ", томов " + lost);
                Check(name + ": сводка начинается с имени файла", summary.StartsWith(Path.GetFileName(file), StringComparison.Ordinal), summary.Substring(0, Math.Min(40, summary.Length)));
            }

            Console.WriteLine("  раскладок {0}, подписей {1}, выносок {2}", layouts, marks, callouts);
            Check("выноски не идут через числа размеров", leaderNumbers == 0, "выносок " + leaders + ", через число " + leaderNumbers + ", через прочие подписи " + (leaderMarks - leaderNumbers));
            Check("выноски редко идут через чужую точку", leaderDots * 50 <= leaders, "через чужую точку " + leaderDots + " из " + leaders);
            Roles();
            Thresholds();

            if (real != null)
            {
                string th = Path.Combine(real, "GS2020_marinelli_th232.gdml");
                GdmlGeometry g = GdmlGeometry.Read(th);
                GdmlOverview o = GdmlOverview.Of(g);
                Check("th232: кристалл, проба, сосуд и прибор узнаны", o.Crystal != null && o.Crystal.VolumeName == "LV_NaI_crystal" && o.Sample != null && o.Sample.VolumeName == "LV_SourceMatrix" && o.Vessel != null && o.Vessel.VolumeName == "LV_VesselWall" && o.DeviceBody != null && o.DeviceBody.VolumeName == "LV_Shell_1_00",
                      (o.Crystal == null ? "?" : o.Crystal.VolumeName) + ", " + (o.Sample == null ? "?" : o.Sample.VolumeName) + ", " + (o.Vessel == null ? "?" : o.Vessel.VolumeName) + ", " + (o.DeviceBody == null ? "?" : o.DeviceBody.VolumeName));
                Check("th232: цвет кристалла — как в редакторе", o.ColorOf("G4_SODIUM_IODIDE").ToArgb() == Color.FromArgb(0x35, 0xA5, 0xAD).ToArgb(), "");
                Check("th232: цвет пробы — как в редакторе", o.ColorOf("Epoxy_crumb").ToArgb() == Color.FromArgb(0x78, 0x80, 0x8E).ToArgb(), "");
                using (Drawing d = Draw(g, new Size(1100, 600)))
                {
                    Check("th232: два вида одним масштабом", Math.Abs(Scale(d, true) / Scale(d, false) - 1.0) < 1e-9, Scale(d, true).ToString("0.000", CultureInfo.InvariantCulture) + " и " + Scale(d, false).ToString("0.000", CultureInfo.InvariantCulture));
                    Check("th232: выноски у всех четырёх частей в разрезе", ((List<GdmlSketch.SketchCallout>)Field(d.Sketch, "paintCallouts")).FindAll(c => c.Axial).Count == 4, "");
                    // Стенка корпуса-сетки закрашена: прежде она была пустым контуром.
                    GdmlVolumeInstance shell = g.Volumes.Find(v => v.VolumeName == "LV_Shell_1_00");
                    Check("th232: стенка корпуса-сетки закрашена", FillMm2(d, true, "G4_Al") > 300.0, Num(FillMm2(d, true, "G4_Al")) + " мм², том " + shell.VolumeName);
                }

                foreach (string file in Directory.GetFiles(real, "*.gdml"))
                {
                    using (Drawing d = Draw(GdmlGeometry.Read(file), new Size(1400, 700)))
                    {
                        long best = long.MaxValue;
                        for (int i = 0; i < 5; i++)
                        {
                            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                            using (Graphics graphics = Graphics.FromImage(d.Bitmap))
                            {
                                typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(d.Sketch, new object[] { new PaintEventArgs(graphics, new Rectangle(0, 0, 1400, 700)) });
                            }

                            best = Math.Min(best, watch.ElapsedMilliseconds);
                        }

                        Check(Path.GetFileNameWithoutExtension(file) + ": до 50 мс на вид", best <= 100, best + " мс на два вида, лучшая из пяти");
                    }
                }
            }
        }

        static void Subtraction()
        {
            Console.WriteLine();
            Console.WriteLine("=== вычитание: положение второго тела, вырез вместо заливки ===");
            using (Drawing d = DrawFile(Path.Combine(data, "36_ctrl_sub.gdml")))
            {
                Area("36_ctrl_sub: вырез по центру", d, true, "G4_Al", 100.0 - 16.0);
            }

            using (Drawing d = DrawFile(Path.Combine(data, "12_sub_second_pos.gdml")))
            {
                Area("12_sub_second_pos: второе тело далеко, выреза нет", d, true, "G4_Al", 100.0);
            }

            using (Drawing d = DrawFile(Inline("sub_first")))
            {
                // Первое тело смещено на +10, вырез на месте: тело x −40…60, вырез −10…10.
                Area("sub_first: положение первого тела", d, true, "G4_Al", 10000.0 - 2000.0);
                PointF right = Screen(d, true, 55.0, 5.0);
                PointF hole = Screen(d, true, 0.0, 5.0);
                Check("sub_first: тело смещено, вырез нет", d.Bitmap.GetPixel((int)right.X, (int)right.Y).ToArgb() == Fill(d, "G4_Al").ToArgb() && d.Bitmap.GetPixel((int)hole.X, (int)hole.Y).ToArgb() == Canvas.ToArgb(), "точки x=55, x=0");
            }

            using (Drawing d = DrawFile(Inline("sub_ring")))
            {
                // Второе тело — кольцо: при едином пути сечения внутренний контур обязан вырезать.
                Area("sub_ring: в торце вырезано кольцо", d, false, "G4_Al", 10000.0 - Math.PI * (400.0 - 100.0));
                PointF inside = Screen(d, false, 4.0, 4.0);
                Check("sub_ring: внутри кольца тело, вырезан не круг", d.Bitmap.GetPixel((int)inside.X, (int)inside.Y).ToArgb() == Fill(d, "G4_Al").ToArgb(), "точка x=4 y=4");
            }

            using (Drawing d = DrawFile(Inline("order_fold")))
            {
                // Плоскости не по порядку: читатель тело отверг, вырез не определён.
                Check("второе тело отвергнуто читателем: подпись, фигуры нет", d.Notes.Count == 2 && d.Notes[0].StartsWith("No exact", StringComparison.Ordinal) && FillMm2(d, true, "G4_Al") == 0.0, string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("sub_mesh")))
            {
                Check("сетка вторым телом: вырезать нечем, подпись", d.Notes.Exists(n => n.StartsWith("No exact", StringComparison.Ordinal)), string.Join(" | ", d.Notes));
            }

            using (Drawing d = DrawFile(Inline("sub_offset")))
            {
                Shot(d, "a304-sub_offset");
                // Ось x: u = y, v = z в торце; плоскость торца x = 0 мимо выреза, разрез y = 0: u = x, v = z.
                PointF hole = Screen(d, true, 5.0, 0.0);
                PointF body = Screen(d, true, -5.0, 0.0);
                Check("sub_offset: вырез на своём месте", d.Bitmap.GetPixel((int)hole.X, (int)hole.Y + 8).ToArgb() != Fill(d, "G4_Al").ToArgb(), "точка x=5");
                Check("sub_offset: на месте первого тела выреза нет", d.Bitmap.GetPixel((int)body.X, (int)body.Y + 8).ToArgb() == Fill(d, "G4_Al").ToArgb(), "точка x=-5");
            }

            using (Drawing d = DrawFile(Inline("sub_protrude")))
            {
                Shot(d, "a304-sub_protrude");
                // Второе тело выступает за первое: контура за пределами тела нет, там только материнский том.
                PointF outside = Screen(d, true, 15.0, 2.0);
                Check("sub_protrude: нет контура вне тела", d.Bitmap.GetPixel((int)outside.X, (int)outside.Y).ToArgb() == Fill(d, "G4_AIR").ToArgb(), "точка x=15 z=2");
            }

            using (Drawing d = DrawFile(Inline("sub_sibling")))
            {
                Shot(d, "a304-sub_sibling");
                PointF core = Screen(d, true, 0.0, 0.0);
                Check("sub_sibling: сосед в вырезе не затёрт", d.Bitmap.GetPixel((int)core.X + 3, (int)core.Y + 3).ToArgb() == Fill(d, "G4_Pb").ToArgb(), "материал соседа в центре выреза");
            }
        }

        // ------------------------------------------------------------------
        //  (5) форма
        // ------------------------------------------------------------------

        static object Member(EfficiencyMakerForm form, string name)
        {
            return typeof(EfficiencyMakerForm).GetField(name, Any).GetValue(form);
        }

        static bool StateEnabled(Control control)
        {
            return (bool)typeof(Control).GetMethod("GetState", Any, null, new[] { typeof(int) }, null).Invoke(control, new object[] { 4 });
        }

        static bool Import(EfficiencyMakerForm form, string path, out string problem)
        {
            object[] args = { path, null };
            bool ok = (bool)typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFile", Any).Invoke(form, args);
            problem = (string)args[1];
            return ok;
        }

        static void Form()
        {
            Console.WriteLine();
            Console.WriteLine("=== (5) форма: битый файл, импорт при счёте ===");
            // Конец счёта — в потоке формы, как в приложении: без этого BackgroundWorker зовёт его из пула
            // и гоняется с проверками и с закрытием формы.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            string good = Path.Combine(data, "37_ctrl_gp.gdml");
            string other = Path.Combine(data, "27_cup.gdml");
            string broken = Inline("broken");
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
                Button calc = (Button)Member(form, "calculateButton");
                TextBox log = (TextBox)Member(form, "logTextBox");
                Label status = (Label)Member(form, "statusLabel");
                GdmlSketch sketch = (GdmlSketch)Field(panel, "gdmlSketch");
                MethodInfo busy = typeof(EfficiencyMakerForm).GetMethod("Busy", Any);
                Func<string> state = () => "Busy=" + busy.Invoke(form, null) + " [" + calc.Text + "] Enabled=" + StateEnabled(calc) + " GdmlMode=" + panel.GdmlMode;

                string problem;
                bool imported = Import(form, broken, out problem);
                Check("битый файл без показанного импорта: режим не включён", !imported && problem != null && !panel.GdmlMode && StateEnabled(calc), state() + " | " + problem);
                Check("битый файл: причина в журнале", log.Text.Contains("broken.gdml"), log.Text.Trim());

                imported = Import(form, good, out problem);
                Check("исправный: режим включён", imported && panel.GdmlMode && !StateEnabled(calc), state());
                GdmlGeometry shown = sketch.Geometry;
                imported = Import(form, broken, out problem);
                Check("битый поверх показанного: прежний импорт на месте", !imported && problem != null && panel.GdmlMode && ReferenceEquals(shown, sketch.Geometry), state());

                panel.ClearGdml();
                Check("выход: строка в журнале и в статусе", log.Text.Contains("GDML view is closed") && status.Text.Contains("GDML view is closed"), status.Text);
                Check("выход: «Расчёт» вернулся", StateEnabled(calc), state());

                imported = Import(form, good, out problem);
                Check("импорт после выхода: статус про показ, а не про выход", imported && status.Text.Contains("37_ctrl_gp.gdml") && status.Text.Contains("GDML geometry is shown"), status.Text);

                // Заслон: причина в журнале и в Trace, по разу на геометрию.
                using (StringWriter traced = new StringWriter())
                using (System.Diagnostics.TextWriterTraceListener listener = new System.Diagnostics.TextWriterTraceListener(traced))
                {
                    System.Diagnostics.Trace.Listeners.Add(listener);
                    GdmlGeometry broken2 = Hand(GdmlTransform.Identity(), -5.0, 5.0, 0.0);
                    broken2.Volumes.Add(null);
                    sketch.SetGeometry(broken2);
                    sketch.Size = new Size(200, 100);
                    using (Bitmap surface = new Bitmap(200, 100))
                    using (Graphics g = Graphics.FromImage(surface))
                    {
                        typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 100)) });
                        typeof(GdmlSketch).GetMethod("OnPaint", Any).Invoke(sketch, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 200, 100)) });
                    }

                    listener.Flush();
                    System.Diagnostics.Trace.Listeners.Remove(listener);
                    int once = Regex.Matches(log.Text, "NullReferenceException").Count;
                    Check("заслон: причина в журнале, один раз на две отрисовки", once == 1, "вхождений " + once);
                    Check("заслон: причина в Trace", traced.ToString().Contains("NullReferenceException"), traced.ToString().Trim());
                    sketch.SetGeometry(shown);
                }

                panel.ClearGdml();

                // Счёт идёт: задача ждёт события, окна нет.
                MethodInfo start = typeof(EfficiencyMakerForm).GetMethod("Start", Any);
                using (ManualResetEvent go = new ManualResetEvent(false))
                {
                    Func<Action<string>, Func<bool>, EfficiencyFitResult> job = (say, cancel) => { go.WaitOne(); return null; };
                    start.Invoke(form, new object[] { "x", job });
                    string before = state();
                    Check("счёт идёт", before.StartsWith("Busy=True", StringComparison.Ordinal) && StateEnabled(calc), before);

                    imported = Import(form, other, out problem);
                    Check("импорт при счёте: отказ, «Стоп» жив", !imported && problem == null && !panel.GdmlMode && StateEnabled(calc) && calc.Text == before.Split('[', ']')[1], state());
                    Check("импорт при счёте: строка в журнале и в статусе", log.Text.Contains("Import is refused") && status.Text.Contains("Import is refused"), status.Text);
                    // Режим, включённый посреди счёта любым другим путём, «Стоп» тоже не гасит.
                    panel.ShowGdml(GdmlGeometry.Read(good));
                    Check("показ GDML посреди счёта: Busy=True [Stop] Enabled=True", StateEnabled(calc) && panel.GdmlMode, state());
                    go.Set();
                    for (int i = 0; i < 200 && (bool)busy.Invoke(form, null); i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(25);
                    }

                    Application.DoEvents();
                    Check("счёт кончился при показанном GDML: кнопка погашена", !StateEnabled(calc) && !(bool)busy.Invoke(form, null), state());
                    panel.ClearGdml();
                    Check("выход из GDML: кнопка вернулась", StateEnabled(calc), state());
                }

                // Удачный счёт, кончившийся при показанном GDML: кривая запомнена, но на графике её нет.
                EfficiencyCurveGraph graph = (EfficiencyCurveGraph)Member(form, "graph");
                EfficiencyFitResult done = new EfficiencyFitResult();
                done.Curve.Add(new ROIEfficiencyData { Energy = 100.0, Efficiency = 0.1, ErrorPercent = 1.0 });
                done.Curve.Add(new ROIEfficiencyData { Energy = 1000.0, Efficiency = 0.01, ErrorPercent = 1.0 });
                using (ManualResetEvent go2 = new ManualResetEvent(false))
                {
                    Func<Action<string>, Func<bool>, EfficiencyFitResult> job2 = (say, cancel) => { go2.WaitOne(); return done; };
                    // DoEvents снимает контекст потока: второй счёт ставит его заново.
                    SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
                    start.Invoke(form, new object[] { "x", job2 });
                    panel.ShowGdml(GdmlGeometry.Read(good));
                    go2.Set();
                    for (int i = 0; i < 200 && (bool)busy.Invoke(form, null); i++)
                    {
                        Application.DoEvents();
                        Thread.Sleep(25);
                    }

                    Application.DoEvents();
                    Check("счёт кончился при показе: кривая запомнена, на графике нет", ReferenceEquals(Member(form, "lastResult"), done) && Field(graph, "result") == null, "");
                    panel.ClearGdml();
                    Check("выход: кривая счёта на графике", ReferenceEquals(Field(graph, "result"), done), "");
                }
            }
        }

        // ------------------------------------------------------------------
        //  (6) панель и график
        // ------------------------------------------------------------------

        static TextBox FirstEditable(Control root)
        {
            foreach (Control child in root.Controls)
            {
                TextBox box = child as TextBox;
                if (box != null && !box.ReadOnly) return box;
                TextBox inner = FirstEditable(child);
                if (inner != null) return inner;
            }

            return null;
        }

        static void Panel()
        {
            Console.WriteLine();
            Console.WriteLine("=== (6) панель: правка поля, кривые и выгрузка при показе GDML, перенос сводки ===");
            string good = Path.Combine(data, "37_ctrl_gp.gdml");
            using (EfficiencyMakerForm form = new EfficiencyMakerForm())
            {
                GeometryEditorPanel panel = (GeometryEditorPanel)Member(form, "geometryPanel");
                Button export = (Button)Member(form, "exportButton");
                TextBox log = (TextBox)Member(form, "logTextBox");
                Label status = (Label)Member(form, "statusLabel");
                EfficiencyCurveGraph graph = (EfficiencyCurveGraph)Member(form, "graph");
                RichTextBox summary = (RichTextBox)Field(panel, "gdmlSummary");
                Geometry(form, panel, good);
                Check("сводка с переносом", summary.WordWrap && summary.ScrollBars == RichTextBoxScrollBars.Vertical, "WordWrap=" + summary.WordWrap + " ScrollBars=" + summary.ScrollBars);

                EfficiencyFitResult fit = new EfficiencyFitResult();
                fit.Curve.Add(new ROIEfficiencyData { Energy = 100.0, Efficiency = 0.1, ErrorPercent = 1.0 });
                fit.Curve.Add(new ROIEfficiencyData { Energy = 1000.0, Efficiency = 0.01, ErrorPercent = 1.0 });
                typeof(EfficiencyMakerForm).GetField("lastResult", Any).SetValue(form, fit);
                graph.SetData(null, fit);
                typeof(EfficiencyMakerForm).GetMethod("UpdateSaveState", Any).Invoke(form, null);
                Button save = (Button)Member(form, "saveButton");
                Check("до импорта: кривая на графике, выгрузка есть", ReferenceEquals(Field(graph, "result"), fit) && StateEnabled(export), "Export=" + StateEnabled(export));
                Check("до импорта: «Сохранить» есть", StateEnabled(save), "Save=" + StateEnabled(save));
                int before = GraphInk(graph);

                string problem;
                object[] args = { good, null };
                typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFile", Any).Invoke(form, args);
                Check("показ GDML: прежней кривой на графике нет", Field(graph, "result") == null, "");
                Check("показ GDML: `Экспорт CSV` погашен", !StateEnabled(export), "Export=" + StateEnabled(export));
                Check("показ GDML: «Сохранить» погашена", !StateEnabled(save), "Save=" + StateEnabled(save));
                string hidden = (string)Field(graph, "emptyNote");
                Check("показ GDML: на пустом графике причина", !string.IsNullOrEmpty(hidden) && hidden.Contains("GDML"), hidden);
                string plain = form.GetType().Assembly.GetType("BecquerelMonitor.Properties.Resources").GetProperty("EfficiencyMakerGraphEmpty", Any).GetValue(null) as string;
                int withNote = GraphInk(graph);
                Field(graph, "emptyNote");
                graph.SetEmptyNote(null);
                int withoutNote = GraphInk(graph);
                graph.SetEmptyNote(hidden);
                Check("показ GDML: подпись нарисована, а не только задана", withNote != withoutNote && withNote > 0, "точек с причиной " + withNote + ", с прежней надписью «" + plain + "» " + withoutNote + "; с кривой было " + before);

                TabControl tabs = (TabControl)Field(panel, "tabs");
                TextBox field = FirstEditable(tabs.TabPages[1]);
                field.Text = field.Text + "1";
                problem = status.Text;
                Check("правка поля «Источник»: импорт снят и это сказано", !panel.GdmlMode && log.Text.Contains("GDML view is closed") && problem.Contains("GDML view is closed"), problem);
                Check("после выхода: кривая и выгрузка вернулись", ReferenceEquals(Field(graph, "result"), fit) && StateEnabled(export), "Export=" + StateEnabled(export));
                Check("после выхода: «Сохранить» вернулась, причины на графике нет", StateEnabled(save) && Field(graph, "emptyNote") == null, "Save=" + StateEnabled(save));

                // Импорт кривой при показе GDML: кривая ложится с полями редактора, показ закрыт.
                args = new object[] { good, null };
                typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFile", Any).Invoke(form, args);
                string curve = Path.Combine(tmp, "curve_a304.txt");
                File.WriteAllText(curve, "Energy, keV\tEfficiency\tUncertainty, %\n100\t0.1\t1\n1000\t0.01\t1\n");
                object[] curveArgs = { curve, null, null };
                bool curveOk = (bool)typeof(EfficiencyMakerForm).GetMethod("ImportCurveFile", Any).Invoke(form, curveArgs);
                Check("импорт кривой при показе GDML: показ закрыт, кривая на графике", curveOk && !panel.GdmlMode && Field(graph, "result") != null && ReferenceEquals(Field(graph, "result"), Member(form, "lastResult")), "ok=" + curveOk + " GdmlMode=" + panel.GdmlMode + " " + curveArgs[2]);

                // Сводка в окне: RTF собран, текст прочитан окном, а не только построен.
                panel.ShowGdml(GdmlGeometry.Read(good));
                Check("сводка: текст в окне тот же, что собрал обзор", summary.Text.Contains("37_ctrl_gp.gdml") && summary.Text.Contains("Al"), summary.Text.Substring(0, Math.Min(60, summary.Text.Length)));
                panel.ClearGdml();
                Check("сводка пуста после выхода", summary.Text.Length == 0, "");
            }
        }

        /// <summary>
        /// Главная находка дизайна и раздел: сводка — доля вкладки, чертежу больше половины; при показе у раздела нет
        /// стыкованных соседей, кроме сводки (иначе предел отрицательный и он не двигается); выход возвращает колонку;
        /// высота вкладки идёт за окном, статус читается целиком. Мышью — `CritSplit.cs` на скрытом столе, журнал.
        /// </summary>
        static void Geometry(EfficiencyMakerForm form, GeometryEditorPanel panel, string good)
        {
            Panel column = (Panel)Field(panel, "detectorColumn");
            Panel sum = (Panel)Field(panel, "gdmlSummaryPanel");
            Splitter splitter = (Splitter)Field(panel, "gdmlSplitter");
            GdmlSketch sketch = (GdmlSketch)Field(panel, "gdmlSketch");
            TabControl tabControl = (TabControl)Member(form, "tabControl");
            Button saveButton = (Button)Member(form, "saveButton");
            Label status = (Label)Member(form, "statusLabel");
            TabPage tab = (TabPage)sum.Parent;
            Check("до показа: колонка стыкована", column.Dock == DockStyle.Left, column.Dock.ToString());
            string longName = Path.Combine(tmp, "GS2020_marinelli_1000ml_water_long_name.gdml");
            File.Copy(good, longName, true);
            object[] args = { longName, null };
            typeof(EfficiencyMakerForm).GetMethod("ImportGdmlFile", Any).Invoke(form, args);
            foreach (Size size in new[] { new Size(900, 560), new Size(982, 620), new Size(982, 839), new Size(1500, 1000), new Size(1920, 1040) })
            {
                form.Size = size;
                form.PerformLayout();
                string label = "окно " + size.Width + "x" + size.Height;
                Check(label + ": вкладка идёт за окном", saveButton.Top - tabControl.Bottom == 12, "вкладка до " + tabControl.Bottom + ", кнопки с " + saveButton.Top);

                // Окно не показано, вкладка сама размера не получит: ширина как на скрытом столе, 62 точки рамок.
                int width = size.Width - 62;
                tab.Size = new Size(width, 400);
                tab.PerformLayout();
                Check(label + ": сводка не больше 40 % вкладки (главная находка: было 640)", sum.Width <= width * 0.4 + 1, "сводка " + sum.Width + " из " + width);
                Check(label + ": чертёж занимает остаток, не меньше половины вкладки", sketch.Width >= width - sum.Width - splitter.Width - 1 && sketch.Width >= width / 2, "чертёж " + sketch.Width + " из " + width);
                int needed = TextRenderer.MeasureText(status.Text, status.Font).Width;
                Check(label + ": статус читается целиком", needed <= status.Width, status.Text + ": нужно " + needed + ", есть " + status.Width);
            }

            foreach (Control sibling in tab.Controls)
            {
                if (sibling != sum && sibling != splitter)
                {
                    Check("при показе у раздела нет стыкованных соседей: " + sibling.GetType().Name, sibling.Dock != DockStyle.Left, sibling.Dock.ToString());
                }
            }

            panel.ClearGdml();
            Check("выход: колонка стыкована", column.Dock == DockStyle.Left, column.Dock.ToString());
            form.Size = new Size(982, 839);
        }

        static int GraphInk(EfficiencyCurveGraph graph)
        {
            graph.Size = new Size(600, 300);
            using (Bitmap surface = new Bitmap(600, 300))
            using (Graphics g = Graphics.FromImage(surface))
            {
                typeof(EfficiencyCurveGraph).GetMethod("OnPaint", Any).Invoke(graph, new object[] { new PaintEventArgs(g, new Rectangle(0, 0, 600, 300)) });
                int ink = 0;
                for (int y = 0; y < 300; y++)
                {
                    for (int x = 0; x < 600; x++)
                    {
                        if (surface.GetPixel(x, y).ToArgb() != Color.White.ToArgb()) ink++;
                    }
                }

                return ink;
            }
        }

        // ------------------------------------------------------------------
        //  (7) ключи
        // ------------------------------------------------------------------

        static void Keys()
        {
            Console.WriteLine();
            Console.WriteLine("=== (7) ключи Gdml* ===");
            ResourceManager manager = BecquerelMonitor.Properties.Resources.ResourceManager;
            ResourceSet neutral = manager.GetResourceSet(CultureInfo.InvariantCulture, true, true);
            CultureInfo ru = new CultureInfo("ru");
            int count = 0, broken = 0;
            foreach (DictionaryEntry entry in neutral)
            {
                string key = (string)entry.Key;
                if (!key.StartsWith("Gdml", StringComparison.Ordinal) || !(entry.Value is string)) continue;
                count++;
                string english = (string)entry.Value;
                string russian = manager.GetString(key, ru);
                if (Holes(english) != Holes(russian) || (english == russian && Regex.IsMatch(russian, "[A-Za-z]{4,}") && !Regex.IsMatch(russian, "[А-Яа-я]")))
                {
                    broken++;
                    Console.WriteLine("    ⛔ {0}: [{1}] / [{2}]", key, english, russian);
                }
            }

            Check("у всех Gdml* русская пара и те же плейсхолдеры", broken == 0 && count >= 119, "ключей " + count + ", расхождений " + broken);
            foreach (string[] unit in new[] { new[] { "GdmlUnitMm", "mm", "мм" }, new[] { "GdmlUnitCm3", "cm3", "см³" }, new[] { "GdmlUnitG", "g", "г" } })
            {
                Check(unit[0], manager.GetString(unit[0], CultureInfo.InvariantCulture) == unit[1] && manager.GetString(unit[0], ru) == unit[2], unit[1] + " / " + unit[2]);
            }
        }

        // ------------------------------------------------------------------
        //  (8) глубина и работа: отдельным процессом
        // ------------------------------------------------------------------

        static int Overflow(int depth)
        {
            byte[] pad = new byte[64];
            return Overflow(depth + 1) + pad[depth % 64];
        }

        /// <summary>Дочерний процесс: прочитать и нарисовать в двух размерах; переполнение стека унесёт его целиком.</summary>
        static int PaintOne(string path)
        {
            SetErrorMode(0x8003);
            WerSetFlags(0x20);
            System.Diagnostics.Stopwatch reading = System.Diagnostics.Stopwatch.StartNew();
            GdmlGeometry g = GdmlGeometry.Read(path);
            long readMs = reading.ElapsedMilliseconds;
            long worst = 0;
            string notes = "", failure = null;
            long regions = 0;
            foreach (Size size in new[] { new Size(1400, 700), new Size(300, 200) })
            {
                System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
                using (Drawing d = Draw(g, size))
                {
                    worst = Math.Max(worst, watch.ElapsedMilliseconds);
                    notes = string.Join(" | ", d.Notes);
                    failure = d.Failure ?? failure;
                    regions = Math.Max(regions, (long)Field(d.Sketch, "regionsBuilt"));
                }
            }

            string summary = g.Describe();
            Console.WriteLine("read=" + readMs + " ms=" + worst + " regions=" + regions + " budget=" + (summary.Contains("not checked for self-crossing") ? "1" : "0") + " notes=[" + (notes.Length > 160 ? notes.Substring(0, 160) : notes) + "] failure=[" + failure + "]");
            return failure == null ? 0 : 3;
        }

        /// <summary>
        /// Сбор области мимо предела: чертёж за пределом такое тело не рисует, а путь сбора судить надо.
        /// Цепочка `Region.Union` на отрезок переполняла стек внутри GDI+ с 6000 плоскостей.
        /// </summary>
        static int CollectOne(string path)
        {
            SetErrorMode(0x8003);
            WerSetFlags(0x20);
            GdmlGeometry g = GdmlGeometry.Read(path);
            GdmlVolumeInstance volume = g.Volumes.Find(v => v.Solid != null && v.Solid.Kind == GdmlSolidKind.Subtraction);
            Console.WriteLine("мм²=" + CollectedMm2(g, volume, true).ToString("0", CultureInfo.InvariantCulture));
            return 0;
        }

        static string Chain(string kind, int n)
        {
            string path = Path.Combine(tmp, "deep_" + kind + "_" + n.ToString(CultureInfo.InvariantCulture) + ".gdml");
            StringBuilder s = new StringBuilder("<?xml version=\"1.0\"?><gdml><define/><solids><box name=\"WB\" x=\"2000\" y=\"2000\" z=\"2000\" lunit=\"mm\"/><box name=\"B0\" x=\"100\" y=\"100\" z=\"100\" lunit=\"mm\"/><box name=\"Bx\" x=\"1\" y=\"1\" z=\"1\" lunit=\"mm\"/>");
            string previous = "B0";
            for (int i = 0; i < n; i++)
            {
                string name = "S" + i.ToString(CultureInfo.InvariantCulture);
                string first = kind == "S" ? "B0" : previous;
                string second = kind == "F" ? "Bx" : previous;
                string place = kind == "P"
                    ? "<position name=\"p" + i.ToString(CultureInfo.InvariantCulture) + "\" x=\"" + ((i % 40) - 20).ToString(CultureInfo.InvariantCulture) + "\" y=\"1\" z=\"0\" unit=\"mm\"/>"
                    : "";
                s.Append("<subtraction name=\"" + name + "\"><first ref=\"" + first + "\"/><second ref=\"" + second + "\"/>" + place + "</subtraction>");
                previous = name;
            }

            s.Append("</solids><structure><volume name=\"A\"><materialref ref=\"G4_Al\"/><solidref ref=\"" + previous + "\"/></volume><volume name=\"World\"><materialref ref=\"G4_AIR\"/><solidref ref=\"WB\"/><physvol name=\"pv_A\"><volumeref ref=\"A\"/></physvol></volume></structure></gdml>");
            File.WriteAllText(path, s.ToString());
            return path;
        }

        static string Child(string arguments, out int code)
        {
            System.Diagnostics.ProcessStartInfo info = new System.Diagnostics.ProcessStartInfo(Assembly.GetExecutingAssembly().Location, arguments)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(info))
            {
                process.ErrorDataReceived += (s, e) => { };
                process.BeginErrorReadLine();
                string output = null;
                Thread reader = new Thread(() => output = process.StandardOutput.ReadToEnd()) { IsBackground = true };
                reader.Start();
                if (!process.WaitForExit(120000))
                {
                    process.Kill();
                    code = -1;
                    return "снят по времени 120 с";
                }

                reader.Join(5000);
                code = process.ExitCode;
                return (output ?? "").Trim();
            }
        }

        static void Deep()
        {
            Console.WriteLine();
            Console.WriteLine("=== (8) глубина и работа: отдельным процессом ===");
            int code;
            string text = Child("--crash-control", out code);
            Check("контроль: заведомое переполнение в дочернем процессе видно", code == unchecked((int)0xC00000FD), "код 0x" + code.ToString("X8", CultureInfo.InvariantCulture));

            // вид, число, рисуется ли
            object[][] cases =
            {
                new object[] { "F", 149, true }, new object[] { "F", 150, false }, new object[] { "F", 255, false }, new object[] { "F", 256, false }, new object[] { "F", 1000, false }, new object[] { "F", 2000, false }, new object[] { "F", 5000, false }, new object[] { "F", 200000, false },
                new object[] { "S", 149, true }, new object[] { "S", 150, false }, new object[] { "S", 255, false }, new object[] { "S", 256, false }, new object[] { "S", 2000, false }, new object[] { "S", 5000, false }, new object[] { "S", 200000, false },
                new object[] { "L", 7, true }, new object[] { "L", 8, false }, new object[] { "L", 14, false }, new object[] { "L", 16, false },
                new object[] { "P", 7, true }, new object[] { "P", 14, false },
            };
            foreach (object[] one in cases)
            {
                string kind = (string)one[0];
                int n = (int)one[1];
                bool drawn = (bool)one[2];
                string path = Chain(kind, n);
                text = Child("\"--paint-one=" + path + "\"", out code);
                Match ms = Regex.Match(text, @"ms=(\d+)");
                long worst = ms.Success ? long.Parse(ms.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
                bool refused = text.Contains("too complex");
                Check(kind + n + (drawn ? ": нарисовано" : ": подпись"), code == 0 && refused != drawn && worst >= 0 && worst <= 1500,
                      "код 0x" + code.ToString("X8", CultureInfo.InvariantCulture) + " " + new FileInfo(path).Length + " Б; " + text);
            }

            Heavy();

            // Предел глубины сам по себе: цепочка без вторых тел, частей меньше предела.
            foreach (int depth in new[] { 256, 257 })
            {
                using (Drawing d = Draw(Hand(GdmlTransform.Identity(), -50.0, 50.0, 0.0, depth), new Size(400, 300)))
                {
                    bool refused = d.Notes.Exists(n => n.Contains("too complex"));
                    Check("глубина " + depth + ": предел читателя", refused == (depth > GdmlGeometry.MaxSolidDepth) && d.Failure == null, string.Join(" | ", d.Notes));
                }
            }

            // Память обхода: 255 частей при 8 разных телах — десятки областей, а не сотни.
            text = Child("\"--paint-one=" + Chain("L", 7) + "\"", out code);
            Match built = Regex.Match(text, @"regions=(\d+)");
            long regions = built.Success ? long.Parse(built.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            Check("L7: память обхода", code == 0 && regions >= 0 && regions < 40, "областей за отрисовку " + regions);

            // Общая работа на вид: девять тел влезают, десятое нет.
            using (Drawing d = DrawFile(Inline("view_budget")))
            {
                Check("общий предел работы на вид: подпись с именем", d.Notes.Count == 2 && d.Notes[0].Contains("too complex") && d.Notes[0].Contains("V2") && !d.Notes[0].Contains("V1"), string.Join(" | ", d.Notes));
            }
        }

        // ------------------------------------------------------------------
        //  Тяжёлые входы: вершины, а не тела. Каждый — своим процессом.
        // ------------------------------------------------------------------

        const string HeavyHead = "<?xml version=\"1.0\"?><gdml><materials><material name=\"Al\" Z=\"13\"><D value=\"2.7\"/><atom value=\"26.98\"/></material></materials><solids>";
        const string HeavyBox = "<box name=\"B1\" x=\"1000\" y=\"1000\" z=\"1000\" lunit=\"mm\"/>";

        static string HeavyDoc(string name, string solids, string volumes, string placed)
        {
            string path = Path.Combine(tmp, "heavy_" + name + ".gdml");
            File.WriteAllText(path, HeavyHead + solids + "<box name=\"WB\" x=\"100000\" y=\"100000\" z=\"100000\"/></solids><structure>" + volumes
                + "<volume name=\"W\"><materialref ref=\"Al\"/><solidref ref=\"WB\"/>" + placed + "</volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"W\"/></setup></gdml>");
            return path;
        }

        static string HeavyOne(string name, string solids)
        {
            return HeavyDoc(name, solids, "<volume name=\"A\"><materialref ref=\"Al\"/><solidref ref=\"S\"/></volume>", "<physvol name=\"pa\"><volumeref ref=\"A\"/></physvol>");
        }

        static string Polycone(string name, int planes, Func<int, double> radius, double step)
        {
            StringBuilder s = new StringBuilder("<polycone name=\"" + name + "\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\">");
            for (int i = 0; i < planes; i++)
            {
                s.Append("<zplane z=\"" + (i * step - planes * step / 2.0).ToString("R", CultureInfo.InvariantCulture) + "\" rmin=\"0\" rmax=\"" + radius(i).ToString("R", CultureInfo.InvariantCulture) + "\"/>");
            }

            return s.Append("</polycone>").ToString();
        }

        static string SubPoly(string name, string poly)
        {
            return HeavyOne(name, HeavyBox + poly + "<subtraction name=\"S\"><first ref=\"B1\"/><second ref=\"C\"/></subtraction>");
        }

        static string ChainPoly(int planes, int parts)
        {
            StringBuilder s = new StringBuilder(HeavyBox + Polycone("C", planes, i => 400.0 * (1.0 + 0.1 * Math.Sin(i * 0.7)), 800.0 / planes));
            string previous = "B1";
            for (int i = 1; i <= parts; i++)
            {
                string current = i == parts ? "S" : "T" + i.ToString(CultureInfo.InvariantCulture);
                s.Append("<subtraction name=\"" + current + "\"><first ref=\"" + previous + "\"/><second ref=\"C\"/><position name=\"p" + i.ToString(CultureInfo.InvariantCulture)
                    + "\" x=\"" + ((i - parts / 2.0) * 0.5).ToString("R", CultureInfo.InvariantCulture) + "\" y=\"0\" z=\"" + ((i - parts / 2.0) * 0.3).ToString("R", CultureInfo.InvariantCulture) + "\" unit=\"mm\"/></subtraction>");
                previous = current;
            }

            return HeavyOne("chain_poly_" + planes.ToString(CultureInfo.InvariantCulture) + "_x" + parts.ToString(CultureInfo.InvariantCulture), s.ToString());
        }

        static string ManyNames(string name, int count, string solids, string solid)
        {
            // Одно видимое тело: иначе рамки нет, и вместо перечня имён подпись «нет размеров».
            StringBuilder volumes = new StringBuilder("<volume name=\"Seen\"><materialref ref=\"Al\"/><solidref ref=\"SeenBox\"/></volume>");
            StringBuilder placed = new StringBuilder("<physvol name=\"pSeen\"><volumeref ref=\"Seen\"/></physvol>");
            solids = "<box name=\"SeenBox\" x=\"10\" y=\"10\" z=\"10\"/>" + solids;
            for (int i = 0; i < count; i++)
            {
                string id = i.ToString(CultureInfo.InvariantCulture);
                volumes.Append("<volume name=\"C" + id + "\"><materialref ref=\"Al\"/><solidref ref=\"" + solid + "\"/></volume>");
                placed.Append("<physvol name=\"p" + id + "\"><volumeref ref=\"C" + id + "\"/></physvol>");
            }

            return HeavyDoc(name, solids, volumes.ToString(), placed.ToString());
        }

        /// <summary>Сорок профилей по 10000 точек — 2e9 пар; здесь восемь: 4e8, без предела около 6 с.</summary>
        static string ManyProfiles()
        {
            StringBuilder solids = new StringBuilder(), volumes = new StringBuilder(), placed = new StringBuilder();
            for (int k = 0; k < 8; k++)
            {
                string id = k.ToString(CultureInfo.InvariantCulture);
                solids.Append("<genericPolycone name=\"G" + id + "\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\">");
                for (int i = 0; i < 5000; i++)
                {
                    solids.Append("<rzpoint r=\"" + (10.0 + 0.001 * i).ToString("R", CultureInfo.InvariantCulture) + "\" z=\"" + (0.01 * i).ToString("R", CultureInfo.InvariantCulture) + "\"/>");
                }

                for (int i = 4999; i >= 0; i--)
                {
                    solids.Append("<rzpoint r=\"5\" z=\"" + (0.01 * i).ToString("R", CultureInfo.InvariantCulture) + "\"/>");
                }

                solids.Append("</genericPolycone>");
                volumes.Append("<volume name=\"C" + id + "\"><materialref ref=\"Al\"/><solidref ref=\"G" + id + "\"/></volume>");
                placed.Append("<physvol name=\"p" + id + "\"><volumeref ref=\"C" + id + "\"/></physvol>");
            }

            return HeavyDoc("gp_10000x8", solids.ToString(), volumes.ToString(), placed.ToString());
        }

        /// <summary>Несводимые вершины шагом 1e-9 мм в одной прежней ячейке 1e-6 мм.</summary>
        static string MeshCell(int count)
        {
            StringBuilder define = new StringBuilder(), faces = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                double x = 1e-7 + 1e-9 * (i % 1000);
                double y = 1e-7 + 1e-9 * (i / 1000 % 1000);
                double z = 1e-7 + 1e-9 * (i / 1000000);
                define.Append("<position name=\"v" + i.ToString(CultureInfo.InvariantCulture) + "\" x=\"" + x.ToString("F12", CultureInfo.InvariantCulture) + "\" y=\"" + y.ToString("F12", CultureInfo.InvariantCulture) + "\" z=\"" + z.ToString("F12", CultureInfo.InvariantCulture) + "\" unit=\"mm\"/>");
            }

            for (int i = 0; i + 2 < count; i += 3)
            {
                string id = i.ToString(CultureInfo.InvariantCulture);
                faces.Append("<triangular name=\"f" + id + "\" vertex1=\"v" + id + "\" vertex2=\"v" + (i + 1).ToString(CultureInfo.InvariantCulture) + "\" vertex3=\"v" + (i + 2).ToString(CultureInfo.InvariantCulture) + "\"/>");
            }

            string path = Path.Combine(tmp, "heavy_mesh_cell_" + count.ToString(CultureInfo.InvariantCulture) + ".gdml");
            File.WriteAllText(path, "<?xml version=\"1.0\"?><gdml><define>" + define + "</define><materials><material name=\"Al\" Z=\"13\"><D value=\"2.7\"/><atom value=\"26.98\"/></material></materials><solids><tessellated name=\"T\" lunit=\"mm\">" + faces + "</tessellated></solids><structure><volume name=\"W\"><materialref ref=\"Al\"/><solidref ref=\"T\"/></volume></structure><setup name=\"Default\" version=\"1.0\"><world ref=\"W\"/></setup></gdml>");
            return path;
        }

        /// <summary>Одна отрисовка: около 2 с и не больше, в чертеже ничего линейного по размеру файла нет.</summary>
        const long PaintLimit = 2000;

        /// <summary>Настоящие файлы: десятки миллисекунд, запас только на чужой счёт, регрессия видна.</summary>
        const long RealPaintLimit = 500;

        /// <summary>Обычное тело без вычитания предела не имеет и у прежнего чертежа занимает то же: судится только код.</summary>
        const long PlainPaintLimit = 40000;

        const string TallBox = "<box name=\"B1\" x=\"1000\" y=\"1000\" z=\"3000\" lunit=\"mm\"/>";
        const string SubC = "<subtraction name=\"S\"><first ref=\"B1\"/><second ref=\"C\"/></subtraction>";

        /// <summary>Несамопересекающийся профиль-гребёнка: торец режет столько колец, сколько зубцов.</summary>
        static string FinsSub(int fins)
        {
            double span = 400.0 / fins;
            StringBuilder s = new StringBuilder(TallBox + "<genericPolycone name=\"C\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><rzpoint r=\"1\" z=\"-10\"/>");
            for (int i = 0; i < fins; i++)
            {
                string r0 = (1.0 + i * span).ToString("R", CultureInfo.InvariantCulture);
                string r1 = (1.0 + i * span + span / 2.0).ToString("R", CultureInfo.InvariantCulture);
                s.Append("<rzpoint r=\"" + r0 + "\" z=\"-10\"/><rzpoint r=\"" + r0 + "\" z=\"10\"/><rzpoint r=\"" + r1 + "\" z=\"10\"/><rzpoint r=\"" + r1 + "\" z=\"-10\"/>");
            }

            s.Append("<rzpoint r=\"" + (1.0 + fins * span).ToString("R", CultureInfo.InvariantCulture) + "\" z=\"-11\"/><rzpoint r=\"1\" z=\"-11\"/></genericPolycone>" + SubC);
            return HeavyOne("fins_sub_" + fins.ToString(CultureInfo.InvariantCulture), s.ToString());
        }

        /// <summary>Плоскости на одной высоте z = 0: торец режет все кольца разом.</summary>
        static string RingsSub(int rings)
        {
            StringBuilder s = new StringBuilder(TallBox + "<polycone name=\"C\" startphi=\"0\" deltaphi=\"360\" aunit=\"deg\" lunit=\"mm\"><zplane z=\"-400\" rmin=\"0\" rmax=\"1\"/>");
            for (int i = 0; i < rings; i++)
            {
                s.Append("<zplane z=\"0\" rmin=\"" + (1.0 + 2.0 * i / rings * 200.0).ToString("R", CultureInfo.InvariantCulture) + "\" rmax=\"" + (1.0 + (2.0 * i + 1.0) / rings * 200.0).ToString("R", CultureInfo.InvariantCulture) + "\"/>");
            }

            s.Append("<zplane z=\"400\" rmin=\"0\" rmax=\"1\"/></polycone>" + SubC);
            return HeavyOne("rings_sub_" + rings.ToString(CultureInfo.InvariantCulture), s.ToString());
        }

        static string Placed(string name, string solids, int count, double dz)
        {
            StringBuilder placed = new StringBuilder();
            for (int i = 0; i < count; i++)
            {
                string id = i.ToString(CultureInfo.InvariantCulture);
                placed.Append("<physvol name=\"p" + id + "\"><volumeref ref=\"A\"/><position name=\"q" + id + "\" x=\"0\" y=\"0\" z=\"" + (i * dz).ToString("R", CultureInfo.InvariantCulture) + "\" unit=\"mm\"/></physvol>");
            }

            return HeavyDoc(name, solids, "<volume name=\"A\"><materialref ref=\"Al\"/><solidref ref=\"S\"/></volume>", placed.ToString());
        }

        /// <summary>Работа считала их в пределах: глубина 3, поровну на два и пять томов.</summary>
        static string SplitComb(int count, int planes)
        {
            string solids = HeavyBox + Polycone("C", planes, i => i % 2 == 0 ? 400.0 : 100.0, 800.0 / planes)
                + "<subtraction name=\"T1\"><first ref=\"B1\"/><second ref=\"C\"/><position name=\"p1\" x=\"0.7\" y=\"0\" z=\"0\" unit=\"mm\"/></subtraction>"
                + "<subtraction name=\"S\"><first ref=\"T1\"/><second ref=\"C\"/><position name=\"p2\" x=\"1.4\" y=\"0\" z=\"0\" unit=\"mm\"/></subtraction>";
            return Placed("split_comb_n" + count.ToString(CultureInfo.InvariantCulture) + "_p" + planes.ToString(CultureInfo.InvariantCulture), solids, count, 2.0);
        }

        /// <summary>Работа ноль: одно вычитание, но двести томов.</summary>
        static string ZeroMany(int count, int planes)
        {
            string solids = HeavyBox + Polycone("C", planes, i => 400.0 * (1.0 + 0.1 * Math.Sin(i * 0.7)), 800.0 / planes) + "<subtraction name=\"S\"><first ref=\"B1\"/><second ref=\"C\"/></subtraction>";
            return Placed("zero_n" + count.ToString(CultureInfo.InvariantCulture) + "_p" + planes.ToString(CultureInfo.InvariantCulture), solids, count, 1.0);
        }

        static void HeavyCase(string path, bool? refused, long strict = 0)
        {
            int code;
            string text = Child("\"--paint-one=" + path + "\"", out code);
            Match ms = Regex.Match(text, @" ms=(\d+)");
            long worst = ms.Success ? long.Parse(ms.Groups[1].Value, CultureInfo.InvariantCulture) : -1;
            long limit = strict > 0 ? strict : PaintLimit;
            bool complex = text.Contains("too complex");
            Check(Path.GetFileNameWithoutExtension(path), code == 0 && worst >= 0 && worst <= limit && (refused == null || complex == refused),
                  "код 0x" + code.ToString("X8", CultureInfo.InvariantCulture) + ", предел " + limit + " мс; " + text);
        }

        static void Heavy()
        {
            Func<int, double> smooth = i => 400.0 * (1.0 + 0.1 * Math.Sin(i * 0.7));
            Func<int, double> thread = i => i % 2 == 0 ? 399.0 : 400.0;

            // Единый предел на вершины: 8 на отрезок, 1250 плоскостей — порог.
            HeavyCase(SubPoly("sub1_poly_1000", Polycone("C", 1000, smooth, 0.8)), false);
            HeavyCase(SubPoly("sub1_poly_1250", Polycone("C", 1250, smooth, 0.64)), false);
            HeavyCase(SubPoly("sub1_poly_1252", Polycone("C", 1252, smooth, 0.64)), true);
            string union8000 = SubPoly("sub1_poly_8000", Polycone("C", 8000, smooth, 0.1));
            HeavyCase(union8000, true);
            HeavyCase(SubPoly("thread_6000", Polycone("C", 6000, thread, 0.5)), true);

            // Союз в цикле: глубина области 2N переполняла стек внутри GDI+, код 0xC00000FD. Проверяется
            // сбор области напрямую: за пределом чертёж этого тела не собирает.
            int collectCode;
            string collected = Child("\"--collect-one=" + union8000 + "\"", out collectCode);
            Check("область 8000 плоскостей собирается одним путём", collectCode == 0 && collected.StartsWith("мм²=", StringComparison.Ordinal), "код 0x" + collectCode.ToString("X8", CultureInfo.InvariantCulture) + " " + collected);

            // Входы третьего критика: сумма вершин, в которую работа не смотрела.
            HeavyCase(FinsSub(2000), true);
            HeavyCase(RingsSub(5000), true);
            HeavyCase(SplitComb(2, 3000), true);
            HeavyCase(SplitComb(5, 1250), true);
            HeavyCase(ZeroMany(200, 1000), true);

            // У предела: тяжелее всего, что рисуется; запас на чужой счёт, на тихой машине до 2 с.
            HeavyCase(FinsSub(1000), false, 5000);
            HeavyCase(RingsSub(1240), false, 5000);

            // Предел считает вершины: тела те же, что под пределом частей.
            HeavyCase(ChainPoly(50, 5), false);
            HeavyCase(ChainPoly(50, 20), true);
            HeavyCase(ChainPoly(50, 100), true);
            HeavyCase(ChainPoly(4, 149), true);

            // Имена подписи: множество, а не перебор.
            HeavyCase(ManyNames("names_20000", 20000, "<sphere name=\"Tb\" rmax=\"1\" deltaphi=\"6.28\"/>", "Tb"), null, 500);
            using (Drawing d = DrawFile(Path.Combine(tmp, "heavy_names_20000.gdml")))
            {
                Check("20000 имён: подпись короткая", d.Notes.Count == 2 && d.Notes[0].Contains("Not drawn (20000)") && d.Notes[0].Contains("C2 and 19997 more") && d.Notes[0].Length < 120, d.Notes[0]);
            }

            // Читатель: предел пар на файл и сведение вершин.
            string profiles = ManyProfiles();
            int code;
            string text = Child("\"--paint-one=" + profiles + "\"", out code);
            Match read = Regex.Match(text, @"read=(\d+)");
            Check("восемь профилей по 10000: предел на файл, строка в сводке", code == 0 && text.Contains("budget=1") && read.Success && long.Parse(read.Groups[1].Value, CultureInfo.InvariantCulture) < 3000, text);

            text = Child("\"--paint-one=" + MeshCell(30000) + "\"", out code);
            read = Regex.Match(text, @"read=(\d+)");
            Check("30000 вершин в одной прежней ячейке", code == 0 && read.Success && long.Parse(read.Groups[1].Value, CultureInfo.InvariantCulture) < 2000, text);

            // Настоящие файлы — в бюджете, подписи о сложности нет.
            if (real != null)
            {
                foreach (string file in Directory.GetFiles(real, "*.gdml"))
                {
                    HeavyCase(file, false, RealPaintLimit);
                }
            }

            foreach (string dir in heavy)
            {
                string[] files = Directory.GetFiles(dir, "*.gdml");
                Array.Sort(files, StringComparer.Ordinal);
                foreach (string file in files)
                {
                    string shortName = Path.GetFileName(file);
                    // `names_notdrawn` — на трубах: с `AMBER312` они рисуются и идут как простые тела.
                    bool plain = shortName.StartsWith("plain_", StringComparison.Ordinal) || shortName.StartsWith("tree", StringComparison.Ordinal)
                                 || shortName.StartsWith("names_notdrawn", StringComparison.Ordinal);
                    HeavyCase(file, null, plain ? PlainPaintLimit : 0);
                }
            }
        }

        static string Holes(string text)
        {
            SortedSet<string> set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (Match match in Regex.Matches(text ?? "", @"\{\d+"))
            {
                set.Add(match.Value);
            }

            return string.Join(",", set);
        }
    }
}
