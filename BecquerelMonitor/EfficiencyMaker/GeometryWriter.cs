using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.EfficiencyMaker
{
    /// <summary>
    /// Запись файла геометрии `.in`.
    ///
    /// Файл собирается ЦЕЛИКОМ, в том же порядке и с теми же комментариями, что
    /// у конструктора геометрий LSRM, — чтобы его открывал не только наш расчёт,
    /// но и GMaster. Поэтому пишутся и блоки, которых мы не читаем вовсе
    /// (коаксиальный детектор, воздух): их значения переносятся из исходного
    /// файла (<see cref="GeometryModel.Raw"/>), а если файла не было — берутся
    /// умолчания, помеченные ниже.
    ///
    /// Имена ключей в формате нерегулярны, и это не описка: у отражателя тип
    /// долей называется `DS_FractionTypeReflector`, а не `...CrystalReflector`,
    /// как у остальных, а счётчик элементов вакуума — `DC_nVacuum`, без
    /// `Elements`. Таблица слоёв ниже повторяет формат буква в букву; всякая
    /// «нормализация» здесь ломает чтение файла их программой.
    /// </summary>
    public static class GeometryWriter
    {
        /// <summary>Описание одного вещества в файле: как называются его ключи.</summary>
        sealed class Slot
        {
            public string CountKey;
            public string RoKey;
            public string ZPart;
            public string FractionsPart;
            public string FractionTypeKey;
            public string NamePrefix;
        }

        static readonly Slot DsCrystal = new Slot
        {
            CountKey = "DS_nCrystalElements", RoKey = "DS_RoCrystal",
            ZPart = "DS_ZCrystal", FractionsPart = "DS_FractionsCrystal",
            FractionTypeKey = "DS_FractionTypeCrystal", NamePrefix = "M_DS_Crystal",
        };

        static readonly Slot DsCladding = new Slot
        {
            CountKey = "DS_nCrystalCladdingElements", RoKey = "DS_RoCrystalCladding",
            ZPart = "DS_ZCrystalCladding", FractionsPart = "DS_FractionsCrystalCladding",
            FractionTypeKey = "DS_FractionTypeCrystalCladding", NamePrefix = "M_DS_Crystal_Cladding",
        };

        static readonly Slot DsReflector = new Slot
        {
            CountKey = "DS_nCrystalReflectorElements", RoKey = "DS_RoCrystalReflector",
            ZPart = "DS_ZCrystalReflector", FractionsPart = "DS_FractionsCrystalReflector",
            // именно Reflector, без Crystal — так в формате
            FractionTypeKey = "DS_FractionTypeReflector", NamePrefix = "M_DS_Reflector",
        };

        // Вещества коаксиального детектора — те, которых мы не показываем и
        // переносим из исходного файла. Объявлены здесь, а не строками по
        // месту, потому что список нужен ДВАЖДЫ: писателю и отбору
        // переносимых ключей (<see cref="CarriedFrom"/>). Две копии этого
        // перечня разошлись бы молча.
        static readonly Slot DcCrystal = new Slot
        {
            CountKey = "DC_nCrystalElements", RoKey = "DC_RoCrystal",
            ZPart = "DC_ZCrystal", FractionsPart = "DC_FractionsCrystal",
            FractionTypeKey = "DC_FractionTypeCrystal", NamePrefix = "M_DC_Crystal",
        };

        static readonly Slot DcCladding = new Slot
        {
            CountKey = "DC_nCrystalSideCladdingElements", RoKey = "DC_RoCrystalSideCladding",
            ZPart = "DC_ZCrystalSideCladding", FractionsPart = "DC_FractionsCrystalSideCladding",
            FractionTypeKey = "DC_FractionTypeCrystalSideCladding",
            NamePrefix = "M_DC_Crystal_Cladding",
        };

        static readonly Slot DcMounting = new Slot
        {
            CountKey = "DC_nCrystalMountingElements", RoKey = "DC_RoCrystalMounting",
            ZPart = "DC_ZCrystalMounting", FractionsPart = "DC_FractionsCrystalMounting",
            FractionTypeKey = "DC_FractionTypeCrystalMounting",
            NamePrefix = "M_DC_Crystal_Mounting",
        };

        static readonly Slot DcCap = new Slot
        {
            CountKey = "DC_nDetectorCapElements", RoKey = "DC_RoDetectorCap",
            ZPart = "DC_ZDetectorCap", FractionsPart = "DC_FractionsDetectorCap",
            FractionTypeKey = "DC_FractionTypeDetectorCap", NamePrefix = "M_DC_Detector_Cap",
        };

        static readonly Slot DcVacuum = new Slot
        {
            // счётчик у вакуума называется DC_nVacuum, без Elements
            CountKey = "DC_nVacuum", RoKey = "DC_RoVacuum",
            ZPart = "DC_ZVacuum", FractionsPart = "DC_FractionsVacuum",
            FractionTypeKey = "DC_FractionTypeVacuum", NamePrefix = "M_DC_Vacuum",
        };

        static Slot Wall(string prefix)
        {
            return new Slot
            {
                CountKey = prefix + "_nWallElements", RoKey = prefix + "_RoWall",
                ZPart = prefix + "_ZWall", FractionsPart = prefix + "_FractionsWall",
                FractionTypeKey = prefix + "_FractionTypeWall", NamePrefix = "M_" + prefix + "_Beaker",
            };
        }

        static Slot SourceSlot(string prefix)
        {
            return new Slot
            {
                CountKey = prefix + "_nSourceElements", RoKey = prefix + "_RoSource",
                ZPart = prefix + "_ZSource", FractionsPart = prefix + "_FractionsSource",
                FractionTypeKey = prefix + "_FractionTypeSource", NamePrefix = "M_" + prefix + "_Source",
            };
        }

        static Slot EmptySpace(string prefix)
        {
            return new Slot
            {
                CountKey = prefix + "_nEmptySpaceElements", RoKey = prefix + "_RoEmptySpace",
                ZPart = prefix + "_ZEmptySpace", FractionsPart = prefix + "_FractionsEmptySpace",
                FractionTypeKey = prefix + "_FractionTypeEmptySpace", NamePrefix = "M_" + prefix + "_EmptySpace",
            };
        }

        public static void Save(GeometryModel model, string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Кодировка та же, что у файлов LSRM: однобайтная кириллица в
            // комментариях. UTF-8 их программа не ждёт.
            //
            // ⛔ ЗНАК, КОТОРОГО В 1251 НЕТ, — ОТКАЗ, А НЕ `?` (`A183`).
            //
            // Прежде здесь стоял `File.WriteAllText(..., Encoding.GetEncoding(1251))`
            // с обычной заменой. Та же болезнь, что чинилась со стороны ЧТЕНИЯ
            // (`A161`), только вторая её половина.
            //
            // ⚠ И ПОРТИЛОСЬ ОНО НЕ ТАК, как гласила строка `A183` («станет `?`»)
            // — замер 06.09.2026, `BoundProbeF59`. `Encoding.GetEncoding(1251)`
            // подставляет ЛУЧШЕЕ СООТВЕТСТВИЕ, и вопросительный знак получают
            // только знаки, у которых двойника нет: `中` (U+4E2D) → `?`, а вот
            // `Å` (U+00C5) → `A`. Второй случай ХУЖЕ первого: `NaIATl` выглядит
            // как имя вещества и не вызывает вопросов, `NaI?Tl` хотя бы кричит.
            // Отсюда и выбор — отказ, а не «более уместная» замена.
            //
            // ⚠ ЧЕЙ ЭТО КРУГ — СЧИТАНО 06.09.2026, и не так, как гласила
            // строка: у `Save` НЕТ НИ ОДНОГО вызова из приложения. Все девять
            // зовущих — пробы (`CorpusGeomProbe`, `CultureProbeO14`,
            // `CylinderGeomProbe`, `FacingProbe`, `FsaCascadeProbe`,
            // `GeomEncodingProbe`, `MaterialOrderProbe`, `RawCarryProbe` и
            // `BoxSourceProbe` через `Render`); приложение зовёт только
            // `Render` — в <see cref="ResponseMatrix.ComputeStamp"/>. Значит
            // круг «сохранил — прочитал», который двигал бы отпечаток при
            // неизменной сцене, сегодня проходит по ОСНАСТКЕ, а не по экрану
            // человека. Порча от этого не перестаёт быть порчей — писатель
            // общий, и первый же вызов из формы получил бы её целиком, — но
            // называть её дефектом того, что видит человек, было бы неправдой.
            //
            // ⚠ Почему отказ, а не «писать в кодировке, в которой прочитано».
            // Второе лечит лишь половину случаев: у модели, собранной в полях
            // редактора, исходного файла нет вовсе, и её непредставимый знак
            // так и остался бы `?`. Отказ закрывает оба случая и НАЗЫВАЕТ знак.
            //
            // ⚠ Байты годного файла прежние: `GetBytes` кодировки 1251 даёт
            // ровно то, что писал `WriteAllText` (преамбулы у 1251 нет).
            // Измерено `BoundProbeF59` на ВСЕХ геометриях дерева — байт в байт.
            string text = Render(model);
            byte[] bytes;
            try
            {
                bytes = Strict1251.GetBytes(text);
            }
            catch (EncoderFallbackException ex)
            {
                throw new IOException(Unrepresentable(text, ex, path), ex);
            }

            File.WriteAllBytes(path, bytes);
        }

        /// <summary>
        /// Та же кодовая страница 1251, но БЕЗ молчаливой замены: знак, которого
        /// в ней нет, бросает <see cref="EncoderFallbackException"/> вместо
        /// подстановки «лучшего соответствия».
        /// Держится готовой — `GetEncoding` ходит в таблицу кодовых страниц, а
        /// запись зовётся на каждую сцену пачки.
        /// </summary>
        static readonly Encoding Strict1251 = Encoding.GetEncoding(
            1251, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

        /// <summary>
        /// Текст отказа: САМ ЗНАК, его код, номер строки и сама строка. Без места
        /// отказ бесполезен — в файле геометрии полторы сотни строк, и «какой-то
        /// знак не лёг в 1251» не говорит, что править.
        ///
        /// ⚠ Строка берётся из ресурсов, а не пишется здесь по-русски: у
        /// приложения английский первичен, русский второй (`Resources.resx` +
        /// `Resources.ru.resx`), и отказ, который однажды выйдет в окно, обязан
        /// говорить на языке человека, а не на языке того, кто его писал.
        /// </summary>
        static string Unrepresentable(string text, EncoderFallbackException ex, string path)
        {
            bool pair = ex.IsUnknownSurrogate();
            string sign = pair
                ? new string(new[] { ex.CharUnknownHigh, ex.CharUnknownLow })
                : ex.CharUnknown.ToString(CultureInfo.InvariantCulture);
            int code = pair
                ? char.ConvertToUtf32(ex.CharUnknownHigh, ex.CharUnknownLow)
                : ex.CharUnknown;

            int index = ex.Index;
            if (index < 0 || index > text.Length)
            {
                index = 0;
            }

            int line = 1;
            int lineStart = 0;
            for (int i = 0; i < index; i++)
            {
                if (text[i] == '\n')
                {
                    line++;
                    lineStart = i + 1;
                }
            }

            int lineEnd = text.IndexOf('\n', lineStart);
            string body = (lineEnd < 0
                    ? text.Substring(lineStart)
                    : text.Substring(lineStart, lineEnd - lineStart))
                .TrimEnd('\r');

            return string.Format(CultureInfo.InvariantCulture,
                                 Resources.GeometryWriterUnrepresentable,
                                 path, sign, code, line, body);
        }

        public static string Render(GeometryModel model)
        {
            // Формат `.in` записан в САНТИМЕТРАХ — так его читает GMaster, и
            // единица стоит в самой строке («= 5.03 cm»). Модель держит
            // миллиметры, поэтому пересчёт здесь, один раз на входе.
            model = model.InCentimeters();
            StringBuilder text = new StringBuilder();
            Action<string> line = value => text.Append(value).Append("\r\n");
            Action<string, double> cm = (key, value) => line(string.Format(
                CultureInfo.InvariantCulture, "{0} = {1} cm", key, Trim(value)));

            line("//---------------------------------");
            line("//DETECTOR PARAMETERS BLOCK:");
            line("//---------------------------------");
            line("//Detector types: COAXIAL, SCINTILLATOR");
            line("");
            line("");
            line("DetectorType = SCINTILLATOR");
            line("");
            line("// Coaxial detector");
            foreach (string key in CoaxialKeys)
            {
                cm(key, Carried(model, key));
            }

            line("");
            line("// Scintillator detector");
            // Габариты цилиндра. У прямоугольного кристалла они не выдумываются,
            // а считаются по правилу самого LSRM — равная площадь торца
            // D = 2*sqrt(X*Y/pi), высота = длина бруска. Так файл остаётся
            // осмысленным и для GMaster, который бруска не знает.
            // ⛔ (`A94`) У БРУСКА эти два числа ВЫВОДЯТСЯ, а не берутся из
            // модели: полей `CrystalDiameter`/`CrystalHeight` у него нет —
            // ни в файле, ни в редакторе, ни в сцене. Читать их здесь «а вдруг
            // заданы» значило бы вернуть то самое мёртвое состояние, которое
            // молча правится и никуда не доезжает.
            double diameter, height;
            if (model.Shape == CrystalShape.Box)
            {
                diameter = EquivalentDiameter(model.CrystalBoxX, model.CrystalBoxY);
                height = model.CrystalBoxZ;
            }
            else
            {
                diameter = model.CrystalDiameter;
                height = model.CrystalHeight;
            }

            cm("DS_CrystalDiameter", diameter);
            cm("DS_CrystalHeight", height);
            cm("DS_CrystalFrontReflectorThickness", model.FrontReflectorThickness);
            cm("DS_CrystalSideReflectorThickness", model.SideReflectorThickness);
            cm("DS_CrystalFrontCladdingThickness", model.FrontCladdingThickness);
            cm("DS_CrystalSideCladdingThickness", model.SideCladdingThickness);
            cm("DS_DetectorMountingThickness", model.MountingThickness);
            line("");
            line("");
            line("");
            line("//---------------------------------");
            line("//SOURCE PARAMETERS BLOCK:");
            line("//---------------------------------");
            line("//Source types: POINT, CYLINDER, MARINELLI, BOX");
            line("");
            line("SourceType = " + (model.SourceType == GeometrySourceType.Marinelli ? "MARINELLI"
                                    : model.SourceType == GeometrySourceType.Cylinder ? "CYLINDER"
                                    : model.SourceType == GeometrySourceType.Box ? "BOX" : "POINT"));
            line("");
            line("//Point source");
            cm("pdistance", model.PointDistance);
            line("");
            line("//Cylindrical source");
            cm("SC_BeakerToDetectorFrontDistance", model.BeakerToDetectorDistance);
            cm("SC_BeakerDiameter", model.BeakerDiameter);
            cm("SC_BeakerHeight", model.BeakerHeight);
            cm("SC_BeakerSideWallThickness", model.BeakerSideWallThickness);
            cm("SC_BeakerEndWallThickness", model.BeakerEndWallThickness);
            cm("SC_SourceHeight", model.SourceHeight);
            line("");
            line("//Marinelli beaker source");
            cm("SM_BeakerToDetectorFrontDistance", model.MarinelliToDetectorDistance);
            cm("SM_BeakerDiameter", model.MarinelliBeakerDiameter);
            cm("SM_BeakerHeight", model.MarinelliBeakerHeight);
            cm("SM_BeakerHoleDiameter", model.MarinelliHoleDiameter);
            cm("SM_BeakerHoleHeight", model.MarinelliHoleHeight);
            cm("SM_BeakerSideThickness", model.MarinelliSideThickness);
            cm("SM_BeakerEndWallThickness", model.MarinelliEndWallThickness);
            cm("SM_BeakerHoleSideThickness", model.MarinelliHoleSideThickness);
            cm("SM_BeakerHoleEndWallThickness", model.MarinelliHoleEndWallThickness);
            cm("SM_SourceHeight", model.MarinelliSourceHeight);
            line("");
            // Прямоугольная кювета — НАШЕ расширение формата, как и
            // DS_CrystalBox*. Программа ЛСРМ этих ключей не знает и такой файл
            // прочитает как точечный источник: `SourceType = BOX` ей неизвестен.
            line("//Box source (extension, not read by LSRM)");
            cm("SB_BoxToDetectorFrontDistance", model.BoxToDetectorDistance);
            cm("SB_SourceX", model.BoxSourceX);
            cm("SB_SourceY", model.BoxSourceY);
            cm("SB_BoxSideWallThickness", model.BoxSideWallThickness);
            cm("SB_BoxEndWallThickness", model.BoxEndWallThickness);
            cm("SB_SourceHeight", model.BoxSourceHeight);
            line("");
            line("");
            line("//---------------------------------");
            line("//MATERIAL PARAMETERS BLOCK:");
            line("//---------------------------------");
            line("//Coaxial detector materials:");
            line("//---------------------------");
            line("");
            line("");
            line("// Crystal");
            Carry(text, model, DcCrystal, "Germanium", 5.323, "Ge1");
            line("");
            line("");
            line("// Crystal Cladding");
            Carry(text, model, DcCladding, "Aluminum", 2.7, "Al1");
            line("");
            line("");
            line("//Crystal Mounting");
            Carry(text, model, DcMounting, "Aluminum", 2.7, "Al1");
            line("");
            line("");
            line("//Detector Cap");
            Carry(text, model, DcCap, "Aluminum", 2.7, "Al1");
            line("");
            line("");
            line("//Vacuum");
            Carry(text, model, DcVacuum, "Aluminum", 1e-10, "Al1");
            line("");
            line("");
            line("// Scintillation detector materials:");
            line("//----------------------------------");
            line("");
            line("");
            line("// Crystal");
            Material(text, DsCrystal, model.Crystal);
            line("");
            line("");
            line("// Crystal Cladding ");
            Material(text, DsCladding, model.Cladding);
            line("");
            line("");
            line("// Reflector ");
            Material(text, DsReflector, model.Reflector);
            line("");
            line("");
            line("");
            line("// Cylindrical beaker materials:");
            line("//----------------------------");
            line("");
            line("// Walls ");
            Material(text, Wall("SC"), model.BeakerWall);
            line("");
            line("");
            line("//Source ");
            Material(text, SourceSlot("SC"), model.Source);
            line("");
            line("");
            line("// Empty space ");
            CarrySlot(text, model, EmptySpace("SC"));
            line("");
            line("");
            line("");
            line("//Marinelli beaker materials:");
            line("//---------------------------");
            line("");
            line("// Walls ");
            Material(text, Wall("SM"), model.BeakerWall);
            line("");
            line("");
            line("");
            line("//Source  ");
            Material(text, SourceSlot("SM"), model.Source);
            line("");
            line("");
            line("");
            line("// Empty space ");
            CarrySlot(text, model, EmptySpace("SM"));
            line("");

            // Наше расширение формата — настоящая форма кристалла. Идёт в самом
            // конце и отдельным комментарием: их программа таких ключей не
            // знает и пропускает, а человеку надо понимать, откуда они взялись.
            if (model.Shape == CrystalShape.Box)
            {
                line("");
                line("// Настоящая форма кристалла (наше расширение формата).");
                line("// Длинная сторона Z вдоль оси детектора, торец X*Y смотрит на источник.");
                cm("DS_CrystalBoxX", model.CrystalBoxX);
                cm("DS_CrystalBoxY", model.CrystalBoxY);
                cm("DS_CrystalBoxZ", model.CrystalBoxZ);
            }

            // E21: сторона, которой детектор обращён к пробе. Пишется, только
            // когда она НЕ передняя, — файл без ключа читается как прежде, и
            // все геометрии, снятые до 15.08.2026, остаются собой.
            if (model.Facing != GeometryDetectorFacing.Front)
            {
                line("");
                line("// Сторона детектора, обращённая к пробе (наше расширение формата).");
                line("// SIDE — проба у боковой грани: к ней разворачивается самая широкая");
                line("// грань бруска, глубиной вдоль оси становится наименьший размер.");
                line("DS_Facing = SIDE");
            }

            // E27: съёмка в поле. Форма источника при этом штатная (цилиндр или
            // маринелли) и написана выше как есть — их программа прочитает файл
            // целиком и верно; здесь пишется только НАЗВАНИЕ сцены, по которому
            // наш редактор знает, каким правилом пересчитывать её размеры.
            if (model.Scene != GeometrySceneKind.None)
            {
                line("");
                line("// Съёмка в поле (наше расширение формата): размеры сцены");
                line("// считаны формулой из свободного пробега в пробе, см.");
                line("// GeometrySceneKind. Форма источника выше — настоящая.");
                line("DS_Scene = " + (model.Scene == GeometrySceneKind.Ground
                                      ? "GROUND" : "BOREHOLE"));
            }

            if (model.FwhmAt662Percent > 0.0)
            {
                line("");
                line("// Разрешение прибора (наше расширение формата): допуск");
                line("// поправки на однократное рассеяние, см. GeometryModel.FwhmAt662Percent.");
                line(string.Format(CultureInfo.InvariantCulture,
                    "DS_Fwhm662 = {0:0.###} %", model.FwhmAt662Percent));
            }

            return text.ToString();
        }

        /// <summary>Приведение бруска к цилиндру по правилу LSRM: равная площадь торца.</summary>
        public static double EquivalentDiameter(double x, double y)
        {
            return x > 0.0 && y > 0.0 ? 2.0 * Math.Sqrt(x * y / Math.PI) : 0.0;
        }

        static readonly string[] CoaxialKeys =
        {
            "DC_CrystalDiameter", "DC_CrystalHeight", "DC_CrystalHoleDiameter",
            "DC_CrystalHoleHeight", "DC_CrystalFrontDeadLayer", "DC_CrystalSideDeadLayer",
            "DC_CrystalBackDeadLayer", "DC_CrystalHoleBottomDeadLayer",
            "DC_CrystalHoleSideDeadLayer", "DC_CrystalSideCladdingThickness",
            "DC_CapToCrystalDistance", "DC_DetectorCapDiameter",
            "DC_DetectorCapFrontThickness", "DC_DetectorCapSideThickness",
            "DC_DetectorCapBackThickness", "DC_DetectorMountingThickness",
        };

        /// <summary>Слоты веществ, которые переносятся из разбора, а не считаются.</summary>
        static readonly Slot[] CarriedSlots =
        {
            DcCrystal, DcCladding, DcMounting, DcCap, DcVacuum,
            // «Пустое место» сосуда и маринелли: у ЛСРМ оно записано водой при
            // воздушной плотности, и переписывать эту странность своим
            // воздухом значит менять текст файла (см. `CarrySlot`).
        };

        /// <summary>
        /// Разбор, УРЕЗАННЫЙ до того, что писатель действительно переносит.
        ///
        /// Зачем (`A139`, 04.09.2026): разбор файла теперь ХРАНИТСЯ в
        /// конфигурации прибора — иначе геометрия, приехавшая из конфигурации,
        /// пишет чужие блоки нулями, а та же геометрия из файла — настоящими
        /// числами, и отпечаток матрицы расходится сам собой. Но хранить
        /// ВЕСЬ разбор незачем: из двух сотен ключей писатель читает
        /// шестнадцать размеров коаксиала и семь веществ, остальное — те же
        /// числа, что уже лежат в полях модели. Измерено на `Nano16Pro.in`:
        /// 191 ключ против 55, XML конфигурации 18960 знаков против 11000.
        ///
        /// ⛔ ЭТОТ СПИСОК ОБЯЗАН СОВПАДАТЬ С ТЕМ, ЧТО ЧИТАЕТ `Render`. Если
        /// разойдётся — открытие-сохранение начнёт двигать текст `.in` и
        /// отпечаток; ловится побитовой описью склада (проба `RawCarryProbe`,
        /// круг «КОНФИГУРАЦИЯ (XML)»), и ловится сразу.
        /// </summary>
        public static Dictionary<string, string> CarriedFrom(GeometryModel model)
        {
            var kept = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (model == null)
            {
                return kept;
            }

            Action<string> take = key =>
            {
                string value;
                if (key != null && model.Raw.TryGetValue(key, out value))
                {
                    kept[key] = value;
                }
            };

            foreach (string key in CoaxialKeys)
            {
                take(key);
            }

            var slots = new List<Slot>(CarriedSlots) { EmptySpace("SC"), EmptySpace("SM") };
            foreach (Slot slot in slots)
            {
                // Ровно то, что читает `Read`: плотность, имя и таблица долей.
                // Счётчик элементов и тип долей писатель считает сам.
                take(slot.RoKey);
                take(slot.NamePrefix + ".MName");
                for (int i = 0; i < 24; i++)
                {
                    string index = "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                    take(slot.ZPart + index);
                    take(slot.FractionsPart + index);
                }
            }

            return kept;
        }

        /// <summary>Значение из исходного файла, иначе ноль.</summary>
        static double Carried(GeometryModel model, string key)
        {
            string raw;
            if (!model.Raw.TryGetValue(key, out raw))
            {
                return 0.0;
            }

            System.Text.RegularExpressions.Match m =
                System.Text.RegularExpressions.Regex.Match(raw, @"^\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)");
            double value;
            return m.Success && double.TryParse(m.Groups[1].Value, NumberStyles.Float,
                                                CultureInfo.InvariantCulture, out value)
                ? value : 0.0;
        }

        /// <summary>Вещество, которого мы не показываем: как было, иначе умолчание.</summary>
        static void Carry(StringBuilder text, GeometryModel model, Slot slot,
                          string defaultName, double defaultDensity, string defaultFormula)
        {
            GeometryMaterial material = Read(model, slot);
            if (material == null)
            {
                material = GeometryMaterialLibrary.Make(
                    new GeometryMaterialLibrary.Entry
                    {
                        Name = defaultName, Formula = defaultFormula, Density = defaultDensity
                    }, defaultDensity);
            }

            Material(text, slot, material);
        }

        static void CarrySlot(StringBuilder text, GeometryModel model, Slot slot)
        {
            GeometryMaterial material = Read(model, slot);
            if (material == null)
            {
                // Воздух. Состав в файлах LSRM у «пустого места» записан водой
                // при воздушной плотности; повторять эту странность не будем,
                // но и значить она ничего не может: 0.15 % на 5 см при 40 кэВ.
                material = GeometryMaterialLibrary.Make(
                    new GeometryMaterialLibrary.Entry
                    {
                        Name = "Air, dry", Formula = "N2 O1", Density = 0.001205
                    }, 0.001205);
            }

            Material(text, slot, material);
        }

        /// <summary>Прочитать вещество слота из исходного файла или null.</summary>
        static GeometryMaterial Read(GeometryModel model, Slot slot)
        {
            string density;
            if (!model.Raw.TryGetValue(slot.RoKey, out density))
            {
                return null;
            }

            GeometryMaterial material = new GeometryMaterial();
            string name;
            material.Name = model.Raw.TryGetValue(slot.NamePrefix + ".MName", out name) ? name.Trim() : "";
            double value;
            material.Density = double.TryParse(density.Trim(), NumberStyles.Float,
                                               CultureInfo.InvariantCulture, out value) ? value : 0.0;
            for (int i = 0; i < 24; i++)
            {
                string zKey = slot.ZPart + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string fKey = slot.FractionsPart + "[" + i.ToString(CultureInfo.InvariantCulture) + "]";
                string zRaw, fRaw;
                if (!model.Raw.TryGetValue(zKey, out zRaw) || !model.Raw.TryGetValue(fKey, out fRaw))
                {
                    continue;
                }

                int z;
                double fraction;
                if (int.TryParse(zRaw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out z)
                    && double.TryParse(fRaw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out fraction)
                    && z > 0 && fraction > 0.0)
                {
                    double have;
                    material.Fractions.TryGetValue(z, out have);
                    material.Fractions[z] = have + fraction;
                }
            }

            return material.Fractions.Count > 0 ? material : null;
        }

        static void Material(StringBuilder text, Slot slot, GeometryMaterial material)
        {
            List<int> order = new List<int>(material.Fractions.Keys);
            order.Sort();
            Action<string> line = value => text.Append(value).Append("\r\n");

            line(string.Format(CultureInfo.InvariantCulture, "{0} = {1}", slot.CountKey, order.Count));
            line(string.Format(CultureInfo.InvariantCulture, "{0} = {1}", slot.RoKey, Trim(material.Density)));
            for (int i = 0; i < order.Count; i++)
            {
                line(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] = {2}", slot.ZPart, i, order[i]));
                line(string.Format(CultureInfo.InvariantCulture, "{0}[{1}] = {2:G6}",
                                   slot.FractionsPart, i, material.Fractions[order[i]]));
            }

            line(slot.FractionTypeKey + " = MASS");
            line(slot.NamePrefix + ".MName = " + material.Name);
            line(slot.NamePrefix + ".Nmaterials = 1");
            // Имя дополнено пробелами до 41 знака — так в файлах LSRM; на разбор
            // не влияет, но глазами файлы сравнивать удобнее.
            line(slot.NamePrefix + ".Name[0] = " + material.Name.PadRight(41));
            line(slot.NamePrefix + ".MatRelWeight[0] = 1");
        }

        /// <summary>Число без хвоста нулей: 5.90 -> 5.9, 3.0 -> 3.</summary>
        static string Trim(double value)
        {
            string text = value.ToString("G8", CultureInfo.InvariantCulture);
            return text;
        }
    }
}
