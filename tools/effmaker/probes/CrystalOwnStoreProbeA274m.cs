using BecquerelMonitor;
using BecquerelMonitor.EfficiencyMaker;
using BecquerelMonitor.FullSpectrumAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Serialization;

namespace CrystalOwnStoreProbeA274m
{
    /// <summary>
    /// (`AMBER274`, полоса `a274m`, 14.11.2026) РАЗДЕЛ СЛОЯ СОБСТВЕННОЙ АКТИВНОСТИ КРИСТАЛЛА В МАТРИЦЕ:
    /// запись, чтение, годность сцене с причиной, отказ разбора — положительный контроль каждого пути.
    /// Решение Amber 14.11.2026 вопросником, дословно: «Сперва запись в матрицу (Рекомендую)».
    ///
    /// Что судится (код возврата 0 — всё сошлось):
    ///   1. причина по состоянию матрицы: раздела нет у LaBr3 — отказ `Missing`; у NaI — `NoOwnActivity`, не
    ///      отказ; без геометрии — `NoScene`;
    ///   2. раздел, собранный штатным входом (`ResponseMatrixBuilder.AttachCrystalSection`), годен, переживает
    ///      запись и чтение побитно; файл без раздела после «прочитать — снять раздел — записать» равен исходному
    ///      по sha256 (на этом стоит дописывание раздела к готовому складу в `CorpusMatrixProbe --crystal-own=1`);
    ///   3. ПОРЧА КЛЕЙМА РАЗДЕЛА → отказ с причиной: рецепт (`rcp`) — `OtherRecipe`; `cl`, `l0`, `scr`, `phys` —
    ///      `OtherPhysics`; отпечаток данных распада `ndb` — `OtherData` (и правка строки в копии `nucdb` меняет отпечаток);
    ///      вещество, размер, шаг — `OtherScene`; раздел с альфами (рецепт `:aq`) — `OtherRecipe`;
    ///      ключ формы беты, переключённый после записи, — `OtherPhysics`;
    ///   4. порча байта раздела в файле → `Damaged`, матрица при этом годна;
    ///   5. привязка (`FsaMatrixBinding.Bind`) доносит причину до анализатора; разбор с ключом слоя и отказом
    ///      раздела называет причину (`FsaResult.CrystalOwnNote` «слой не взят: …») и числами побитно равен
    ///      разбору без слоя; с годным разделом слой вычтен и числа другие (ключ доезжает до построителя);
    ///   6. вход счёта ряда по альфам (`FixSeriesByAlphas` в `FsaAnalyzer.PrepareCrystalOwn`): раздел без альф
    ///      возвращается тем же объектом; раздел с альфами, положенный мимо привязки, даёт в примечании слоя
    ///      строку «по альфам».
    ///
    ///   2б. (с `--store-file=`) раздел слоя рядом с разделом электрона в файле склада: тело и `ELSC` побайтно целы;
    ///
    ///     CrystalOwnStoreProbeA274m --dir=каталог --spectrum=...\LaBrBril_Th228.xml --chains=Th-228 [--histories=20000]
    ///                               [--store-file=склад\сцена.rmx --store-scene=склад\сцена.in]   (файл склада только читается)
    ///     CrystalOwnStoreProbeA274m --scene-out=каталог     (только записать сцены `labr38.in`, `nai38.in` и выйти)
    ///     CrystalOwnStoreProbeA274m --demo-spectrum=вход.xml --demo-out=выход.xml --demo-guid=GUID [--demo-matrix=матрица.rmx]
    ///                               (копия спектра с геометрией LaBr3 38×38 и ссылкой на матрицу — стенд ключа
    ///                                `CorpusFsaProbe --crystal-own=`; корпус не трогается)
    /// </summary>
    static class Program
    {
        static int bad;
        static readonly List<string> chains = new List<string>();
        static readonly List<string> nuclides = new List<string>();

        static void Check(string what, bool ok)
        {
            Console.WriteLine("{0}  {1}", ok ? "СОШЛОСЬ   " : "НЕ СОШЛОСЬ", what);
            if (!ok)
            {
                bad++;
            }
        }

        [STAThread]
        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

            // (`T243`) Эталон настроек — ДО разбора ключей.
            FsaTuningReport.Snapshot();

            string dir = null, spectrumPath = null, sceneOut = null, demoIn = null, demoOut = null, demoGuid = null;
            string storeFile = null, storeScene = null, demoMatrix = null;
            int histories = 20000;
            foreach (string a in args)
            {
                if (a.StartsWith("--dir=", StringComparison.Ordinal)) dir = a.Substring(6);
                else if (a.StartsWith("--spectrum=", StringComparison.Ordinal)) spectrumPath = a.Substring(11);
                else if (a.StartsWith("--scene-out=", StringComparison.Ordinal)) sceneOut = a.Substring(12);
                else if (a.StartsWith("--demo-spectrum=", StringComparison.Ordinal)) demoIn = a.Substring(16);
                else if (a.StartsWith("--demo-out=", StringComparison.Ordinal)) demoOut = a.Substring(11);
                else if (a.StartsWith("--demo-guid=", StringComparison.Ordinal)) demoGuid = a.Substring(12);
                else if (a.StartsWith("--demo-matrix=", StringComparison.Ordinal)) demoMatrix = a.Substring(14);
                else if (a.StartsWith("--store-file=", StringComparison.Ordinal)) storeFile = a.Substring(13);
                else if (a.StartsWith("--store-scene=", StringComparison.Ordinal)) storeScene = a.Substring(14);
                else if (a.StartsWith("--histories=", StringComparison.Ordinal)) histories = int.Parse(a.Substring(12), CultureInfo.InvariantCulture);
                else if (a.StartsWith("--chains=", StringComparison.Ordinal)) chains.AddRange(a.Substring(9).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else if (a.StartsWith("--nuclides=", StringComparison.Ordinal)) nuclides.AddRange(a.Substring(11).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                else
                {
                    Console.Error.WriteLine("непонятный ключ: " + a);
                    return 2;
                }
            }

            GlobalConfigManager.GetInstance();
            DeviceConfigManager.GetInstance();
            NuclideDefinitionManager.GetInstance();

            GeometryModel labr = Scene("Lanthanum bromide", "LaBr3 38x38 (AMBER274)");
            GeometryModel nai = Scene("Sodium iodide", "NaI 38x38 (AMBER274, контроль)");

            if (sceneOut != null)
            {
                Directory.CreateDirectory(sceneOut);
                GeometryWriter.Save(labr, Path.Combine(sceneOut, "labr38.in"));
                GeometryWriter.Save(nai, Path.Combine(sceneOut, "nai38.in"));
                GeometryModel back = GeometryModel.Load(Path.Combine(sceneOut, "labr38.in"));
                Console.WriteLine("сцены записаны: {0}; прочитанная LaBr3 слой иметь должна: {1}", sceneOut,
                                  CrystalOwnActivityBuilder.HasOwnActivity(back));
                return CrystalOwnActivityBuilder.HasOwnActivity(back)
                       && !CrystalOwnActivityBuilder.HasOwnActivity(GeometryModel.Load(Path.Combine(sceneOut, "nai38.in"))) ? 0 : 1;
            }

            if (demoIn != null)
            {
                return Demo(demoIn, demoOut, demoGuid, demoMatrix);
            }

            if (dir == null || spectrumPath == null || (chains.Count == 0 && nuclides.Count == 0))
            {
                Console.Error.WriteLine("нужны --dir=, --spectrum= и проба (--nuclides= или --chains=)");
                return 2;
            }

            Directory.CreateDirectory(dir);

            // ---- 1. причина по состоянию матрицы --------------------------------------------
            Console.WriteLine("== 1. матрица без раздела ==");
            ResponseMatrix bare = Build(labr);
            ResponseMatrix bareNai = Build(nai);
            CrystalSectionRefusal why;
            string detail;
            Check("LaBr3 без раздела: раздела нет, причина Missing — отказ",
                  bare.CrystalSectionFor(labr, out why, out detail) == null && why == CrystalSectionRefusal.Missing
                  && ResponseCrystalSection.IsRefusal(why));
            Check("NaI без раздела: причина NoOwnActivity — не отказ",
                  bareNai.CrystalSectionFor(nai, out why, out detail) == null && why == CrystalSectionRefusal.NoOwnActivity
                  && !ResponseCrystalSection.IsRefusal(why));
            Check("без геометрии: причина NoScene — не отказ",
                  bare.CrystalSectionFor(null, out why, out detail) == null && why == CrystalSectionRefusal.NoScene
                  && !ResponseCrystalSection.IsRefusal(why));
            Check("умолчание анализатора: причина NoMatrix — не отказ",
                  new FsaAnalyzer().CrystalOwnRefusal == CrystalSectionRefusal.NoMatrix
                  && !ResponseCrystalSection.IsRefusal(CrystalSectionRefusal.NoMatrix));

            string pathBare = Path.Combine(dir, "labr38_bare.rmx");
            bare.Save(pathBare);
            string shaBare = Sha(pathBare);
            ResponseMatrix reread = ResponseMatrix.Load(pathBare);
            string pathAgain = Path.Combine(dir, "labr38_again.rmx");
            reread.Save(pathAgain);
            Check("прочитать и записать без правок — файл побайтно тот же (" + shaBare.Substring(0, 12) + ")", Sha(pathAgain) == shaBare);

            // ---- 2. штатный вход, запись, чтение ---------------------------------------------
            Console.WriteLine();
            Console.WriteLine("== 2. раздел штатным входом ==");
            ResponseMatrixBuilder.AttachCrystalSection(nai, bareNai);
            Check("NaI: штатный вход раздела не кладёт", bareNai.CrystalSection == null);
            ResponseMatrix with = ResponseMatrix.Load(pathBare);
            ResponseMatrixBuilder.AttachCrystalSection(labr, with);
            ResponseCrystalSection own = with.CrystalSectionFor(labr, out why, out detail);
            Check("LaBr3: штатный вход кладёт годный раздел (причина None)", own != null && why == CrystalSectionRefusal.None);
            if (own == null)
            {
                Console.WriteLine("раздел не собран: {0}", ResponseCrystalSection.RefusalText(why, detail));
                return 1;
            }

            Console.WriteLine("клеймо раздела: {0}", own.Stamp);
            foreach (CrystalOwnEntry entry in own.Entries)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  {0} {1} {2:F4} Бк, историй {3}, бинов {4}",
                                                entry.Nuclide, entry.Free ? "свободная" : "известная", entry.ActivityBq, entry.Histories,
                                                entry.PerDecay != null ? entry.PerDecay.Length : 0));
            }

            string pathWith = Path.Combine(dir, "labr38_with.rmx");
            with.Save(pathWith);
            ResponseMatrix loaded = ResponseMatrix.Load(pathWith);
            ResponseCrystalSection read = loaded != null ? loaded.CrystalSectionFor(labr, out why, out detail) : null;
            Check("файл с разделом читается, раздел годен", read != null && why == CrystalSectionRefusal.None);
            Check("матрица с разделом годна сцене (тело не тронуто)", loaded != null && loaded.IsValidFor(labr)
                  && loaded.BodyFingerprint == reread.BodyFingerprint);
            Check("раздел после чтения побитно тот же", read != null && SameSection(own, read));
            if (loaded != null)
            {
                loaded.CrystalSection = null;
                string pathStripped = Path.Combine(dir, "labr38_stripped.rmx");
                loaded.Save(pathStripped);
                Check("файл с разделом без раздела слоя = исходный файл по sha256", Sha(pathStripped) == shaBare);
                loaded = ResponseMatrix.Load(pathWith);
            }

            Check("чужой сцене (NaI) раздел LaBr3 не отдаётся, причина NoOwnActivity",
                  loaded.CrystalSectionFor(nai, out why, out detail) == null && why == CrystalSectionRefusal.NoOwnActivity);
            GeometryModel bigger = Scene("Lanthanum bromide", "LaBr3 51x51");
            bigger.CrystalDiameter = 51.0;
            bigger.CrystalHeight = 51.0;
            Check("другому кристаллу LaBr3 (51×51) раздел не отдаётся, причина OtherScene — отказ",
                  loaded.CrystalSectionFor(bigger, out why, out detail) == null && why == CrystalSectionRefusal.OtherScene
                  && ResponseCrystalSection.IsRefusal(why));
            Console.WriteLine("  причина: {0}", ResponseCrystalSection.RefusalText(why, detail));

            // ---- 2б. соседство с разделом электрона на файле склада -----------------------------
            if (storeFile != null && storeScene != null)
            {
                Console.WriteLine();
                Console.WriteLine("== 2б. `CWNA` рядом с `ELSC` на матрице склада: {0} ==", Path.GetFileName(storeFile));
                GeometryModel scene = GeometryModel.Load(storeScene);
                string shaStore = Sha(storeFile);
                ResponseMatrix store = ResponseMatrix.Load(storeFile);
                ElectronSectionRefusal electronWhy;
                bool electronBefore = store != null && store.ElectronSectionFor(scene, out electronWhy) != null;
                Check("матрица склада читается, годна своей сцене, раздел электрона годен",
                      store != null && store.IsValidFor(scene) && electronBefore);
                string pathSame = Path.Combine(dir, "store_same.rmx");
                store.Save(pathSame);
                Check("прочитать и записать матрицу склада — файл побайтно тот же (" + shaStore.Substring(0, 12) + ")", Sha(pathSame) == shaStore);
                Check("кристалл сцены склада собственной активности не имеет — штатный вход раздел не кладёт",
                      store.CrystalSectionFor(scene, out why, out detail) == null && why == CrystalSectionRefusal.NoOwnActivity);
                // раздел слоя кладётся насильно (чужой, LaBr3) — только чтобы проверить байты файла с двумя разделами
                store.CrystalSection = own;
                string pathBoth = Path.Combine(dir, "store_both.rmx");
                store.Save(pathBoth);
                ResponseMatrix both = ResponseMatrix.Load(pathBoth);
                Check("файл с двумя разделами читается: тело то же, раздел электрона годен, раздел слоя на месте и побитно тот же",
                      both != null && both.IsValidFor(scene) && both.BodyFingerprint == store.BodyFingerprint
                      && both.ElectronSectionFor(scene, out electronWhy) != null
                      && both.CrystalSection != null && SameSection(own, both.CrystalSection) && both.CrystalSectionDamage == null);
                Check("чужой раздел слоя сцене склада не отдаётся (NoOwnActivity)",
                      both != null && both.CrystalSectionFor(scene, out why, out detail) == null && why == CrystalSectionRefusal.NoOwnActivity);
                if (both != null)
                {
                    both.CrystalSection = null;
                    string pathBack = Path.Combine(dir, "store_back.rmx");
                    both.Save(pathBack);
                    Check("снять раздел слоя — файл склада побайтно прежний (тело и `ELSC` не тронуты)", Sha(pathBack) == shaStore);
                    File.Delete(pathBack);
                }

                File.Delete(pathSame);
                File.Delete(pathBoth);
            }

            // ---- 3. порча клейма раздела ------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("== 3. порча клейма раздела ==");
            read = loaded.CrystalSection;           // матрица перечитана выше — портим клеймо ЕЁ раздела
            string good = read.Stamp;
            Mutant(loaded, labr, good, "rcp", v => "1" + v.Substring(1), CrystalSectionRefusal.OtherRecipe, "рецепт слоя прежней версии");
            Mutant(loaded, labr, good, "rcp", v => v + ":aq0.3785:as0.019:av1", CrystalSectionRefusal.OtherRecipe, "рецепт с альфами (`:aq`)");
            Mutant(loaded, labr, good, "cl", v => "1", CrystalSectionRefusal.OtherPhysics, "форма беты `cl=1`");
            Mutant(loaded, labr, good, "l0", v => null, CrystalSectionRefusal.OtherPhysics, "без поля `l0`");
            Mutant(loaded, labr, good, "scr", v => "1", CrystalSectionRefusal.OtherPhysics, "с полем `scr=1`");
            Mutant(loaded, labr, good, "phys", v => "29", CrystalSectionRefusal.OtherPhysics, "физика переноса 29");
            Mutant(loaded, labr, good, "d", v => "51", CrystalSectionRefusal.OtherScene, "диаметр кристалла 51");
            Mutant(loaded, labr, good, "mat", v => "Cerium bromide", CrystalSectionRefusal.OtherScene, "вещество кристалла");
            Mutant(loaded, labr, good, "bin", v => "1", CrystalSectionRefusal.OtherScene, "шаг сетки 1 кэВ");
            Mutant(loaded, labr, good, "ndb", v => "0123456789abcdef", CrystalSectionRefusal.OtherData, "отпечаток данных распада `ndb`");

            // Отпечаток данных распада ловит правку базы: копия `nucdb`, одна строка La-138 сдвинута.
            string nucdb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nucdb.sqlite");
            string nucdbCopy = Path.Combine(dir, "nucdb_mutant.sqlite");
            File.Copy(nucdb, nucdbCopy, true);
            string fpHome = CrystalOwnActivityBuilder.DecayDataFingerprint(labr);
            string fpCopy = CrystalOwnActivityBuilder.DecayDataFingerprint(labr, nucdbCopy);
            int touched;
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + nucdbCopy))
            {
                connection.Open();
                using (Microsoft.Data.Sqlite.SqliteCommand command = connection.CreateCommand())
                {
                    command.CommandText = "update decay_radiations set intensity_num = intensity_num * 1.01 where rowid ="
                                          + " (select min(rowid) from decay_radiations where parent_nucid = '138LA' and intensity_num > 0)";
                    touched = command.ExecuteNonQuery();
                }
            }

            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            File.SetLastWriteTimeUtc(nucdbCopy, DateTime.UtcNow.AddSeconds(5));
            string fpMutant = CrystalOwnActivityBuilder.DecayDataFingerprint(labr, nucdbCopy);
            Console.WriteLine("отпечаток данных распада: база {0}, копия {1}, копия с правкой одной строки La-138 {2}; у NaI {3}",
                              fpHome, fpCopy, fpMutant, CrystalOwnActivityBuilder.DecayDataFingerprint(nai));
            Check("поле `ndb` в клейме раздела равно отпечатку данных распада базы",
                  ResponseCrystalSection.StampField(good, "ndb") == fpHome && fpHome.Length == 16);
            Check("копия базы даёт тот же отпечаток; правка одной строки La-138 в копии — другой",
                  fpCopy == fpHome && touched == 1 && fpMutant != fpHome && fpMutant.Length == 16);
            Check("у кристалла без собственной активности отпечатка нет (`none`)", CrystalOwnActivityBuilder.DecayDataFingerprint(nai) == "none");
            // копию держит пул соединений чтения — отпустить; не удалилась — остаётся в каталоге пробы, на суд не влияет
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try
            {
                File.Delete(nucdbCopy);
            }
            catch (IOException)
            {
            }
            read.Stamp = good;
            Check("клеймо возвращено — раздел снова годен", loaded.CrystalSectionFor(labr, out why, out detail) != null);

            bool lambda = FsaBetaLibrary.CoulombLambda;
            FsaBetaLibrary.CoulombLambda = !lambda;
            Check("ключ формы беты переключён после записи — раздел не годен, причина OtherPhysics",
                  loaded.CrystalSectionFor(labr, out why, out detail) == null && why == CrystalSectionRefusal.OtherPhysics);
            Console.WriteLine("  причина: {0}", ResponseCrystalSection.RefusalText(why, detail));
            FsaBetaLibrary.CoulombLambda = lambda;
            Check("ключ возвращён — раздел годен", loaded.CrystalSectionFor(labr, out why, out detail) != null);

            string note;
            ResponseCrystalSection alpha = CrystalOwnActivityBuilder.Build(labr, loaded.BinKev, Math.Max(histories, CrystalOwnActivityBuilder.MinHistories), 0x274,
                                                                           new CrystalOwnParts { AlphaQuenching = 0.3785, AlphaQuenchingSlopePerMev = 0.019 }, out note);
            ResponseCrystalSection keep = loaded.CrystalSection;
            loaded.CrystalSection = alpha;
            Check("раздел, собранный с альфами, привязка не отдаёт: причина OtherRecipe (источника QF у прибора ещё нет)",
                  alpha.HasAlpha && loaded.CrystalSectionFor(labr, out why, out detail) == null && why == CrystalSectionRefusal.OtherRecipe);
            Console.WriteLine("  причина: {0}", ResponseCrystalSection.RefusalText(why, detail));
            loaded.CrystalSection = keep;

            // ---- 4. порча байта раздела в файле -----------------------------------------------
            Console.WriteLine();
            Console.WriteLine("== 4. порча байта раздела в файле ==");
            string pathDamaged = Path.Combine(dir, "labr38_damaged.rmx");
            byte[] bytes = File.ReadAllBytes(pathWith);
            int at = LastIndexOf(bytes, Encoding.ASCII.GetBytes(ResponseCrystalSection.EndMark));
            Check("метка конца раздела `CWNE` в файле найдена", at > 64);
            if (at > 64)
            {
                bytes[at - 200] ^= 0x5A;            // внутри гистограммы раздела: до отпечатка (32) и длины (8)
                File.WriteAllBytes(pathDamaged, bytes);
                ResponseMatrix damaged = ResponseMatrix.Load(pathDamaged);
                Check("файл с испорченным разделом читается, матрица годна сцене", damaged != null && damaged.IsValidFor(labr));
                Check("раздел снят, причина Damaged — отказ",
                      damaged != null && damaged.CrystalSectionFor(labr, out why, out detail) == null
                      && why == CrystalSectionRefusal.Damaged && ResponseCrystalSection.IsRefusal(why));
                Console.WriteLine("  причина: {0}", ResponseCrystalSection.RefusalText(why, detail));
            }

            // ---- 5. привязка и разбор -----------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("== 5. привязка и разбор: {0} ==", Path.GetFileName(spectrumPath));
            ResultData rd = Load(spectrumPath);
            ResponseMatrix none = ResponseMatrix.Load(pathBare);
            ResponseMatrix full = ResponseMatrix.Load(pathWith);
            ResponseMatrix foreign = ResponseMatrix.Load(pathWith);
            foreign.CrystalSection.Stamp = SetField(foreign.CrystalSection.Stamp, "rcp", "1:bgcds-vl:sc2");

            Arm offBare = Run("слой ВЫКЛ, раздела нет", rd, labr, none, false, null);
            Arm offFull = Run("слой ВЫКЛ, раздел есть", rd, labr, full, false, null);
            Arm onBare = Run("слой ВКЛ, раздела нет", rd, labr, none, true, null);
            Arm onForeign = Run("слой ВКЛ, раздел чужого рецепта", rd, labr, foreign, true, null);
            Arm onFull = Run("слой ВКЛ, раздел годен", rd, labr, full, true, null);
            foreach (Arm arm in new[] { offBare, offFull, onBare, onForeign, onFull })
            {
                if (arm.Result == null)
                {
                    Check("разбор прошёл: " + arm.Name, false);
                    continue;
                }

                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-34} причина привязки {1,-13} χ²/ndf П {2,10:F4} усиление {3:F6} строк слоя {4}; примечание: {5}",
                    arm.Name, arm.Refusal, arm.Result.Chi2NdfPoisson, arm.Result.Gain,
                    arm.Result.CrystalOwn != null ? arm.Result.CrystalOwn.Count : 0, arm.Result.CrystalOwnNote ?? "нет"));
            }

            if (offBare.Result != null && offFull.Result != null && onBare.Result != null && onForeign.Result != null && onFull.Result != null)
            {
                Check("слой ВЫКЛ: раздел в матрице ничего не меняет (побитно), примечания нет",
                      SameResult(offBare.Result, offFull.Result) && offFull.Result.CrystalOwnNote == null
                      && (offFull.Result.CrystalOwn == null || offFull.Result.CrystalOwn.Count == 0));
                Check("слой ВКЛ, раздела нет: причина Missing доехала до анализатора",
                      onBare.Refusal == CrystalSectionRefusal.Missing);
                Check("слой ВКЛ, раздела нет: разбор назвал причину («слой не взят: …»)",
                      (onBare.Result.CrystalOwnNote ?? "").StartsWith("слой не взят: ", StringComparison.Ordinal));
                Check("слой ВКЛ, раздела нет: числа побитно как без слоя", SameResult(offBare.Result, onBare.Result));
                Check("слой ВКЛ, раздел чужого рецепта: причина OtherRecipe доехала, разбор её назвал, числа как без слоя",
                      onForeign.Refusal == CrystalSectionRefusal.OtherRecipe
                      && (onForeign.Result.CrystalOwnNote ?? "").Contains("другим рецептом слоя")
                      && SameResult(offBare.Result, onForeign.Result));
                Check("слой ВКЛ, раздел годен: слой вычтен (строки слоя есть), числа ДРУГИЕ — ключ доехал до построителя",
                      onFull.Refusal == CrystalSectionRefusal.None && onFull.Result.CrystalOwn != null
                      && onFull.Result.CrystalOwn.Count > 0 && !SameResult(offBare.Result, onFull.Result));
                foreach (FsaCrystalOwnResult row in onFull.Result.CrystalOwn ?? new List<FsaCrystalOwnResult>())
                {
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  строка слоя: {0} {1} {2:F4} Бк, {3:F1} отсч",
                                                    row.Nuclide, row.Free ? "свободная" : "известная", row.ActivityBq, row.Counts));
                }
            }

            // ---- 6. вход счёта ряда по альфам -------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("== 6. вход счёта ряда по альфам ==");
            string alphaNote;
            ResponseCrystalSection same = CrystalOwnActivityBuilder.FixSeriesByAlphas(
                full.CrystalSection, rd.EnergySpectrum.Spectrum, rd.EnergySpectrum.EffectiveLiveTime,
                rd.EnergySpectrum.EnergyCalibration, rd.FwhmCalibration, null, out alphaNote);
            Check("раздел без альф вход возвращает тем же объектом, примечания нет — путь побитно прежний",
                  ReferenceEquals(same, full.CrystalSection) && alphaNote == null);
            Arm onAlpha = Run("слой ВКЛ, раздел с альфами мимо привязки", rd, labr, full, true, alpha);
            if (onAlpha.Result != null)
            {
                Console.WriteLine("  примечание слоя: {0}", onAlpha.Result.CrystalOwnNote ?? "нет");
                Check("раздел с альфами: вход разбора вызван (в примечании слоя строка «по альфам»)",
                      (onAlpha.Result.CrystalOwnNote ?? "").Contains("по альфам"));
            }
            else
            {
                Check("разбор с разделом с альфами прошёл", false);
            }

            Console.WriteLine();
            Console.WriteLine(bad == 0 ? "ВСЁ СОШЛОСЬ" : "НЕ СОШЛОСЬ: " + bad.ToString(CultureInfo.InvariantCulture));
            return bad == 0 ? 0 : 1;
        }

        // ------------------------------------------------------------------------------------------

        sealed class Arm
        {
            public string Name;
            public FsaResult Result;
            public CrystalSectionRefusal Refusal;
        }

        /// <summary>Разбор тем же путём, что приложение: матрица — привязкой; `direct` — раздел мимо привязки (только для п. 6).</summary>
        static Arm Run(string name, ResultData rd, GeometryModel geometry, ResponseMatrix matrix, bool layer, ResponseCrystalSection direct)
        {
            var analyzer = new FsaAnalyzer();
            analyzer.PileUp = false;
            // Проба идёт по матрице без кривой эффективности: сцену она задаёт сама.
            analyzer.RequireGeometry = false;
            analyzer.CrystalOwnLayer = layer;
            FsaTuningReport.Print(analyzer, name);
            FsaMatrixBinding.Bind(analyzer, geometry, matrix);
            var arm = new Arm { Name = name, Refusal = analyzer.CrystalOwnRefusal };
            if (direct != null)
            {
                analyzer.CrystalOwnSection = direct;
            }

            if (rd.Efficiency == null)
            {
                rd.Efficiency = new EfficiencyConfigData { Geometry = geometry };
            }
            else
            {
                rd.Efficiency.Geometry = geometry;
            }

            FsaEfficiency efficiency = FsaEfficiency.FromConfig(rd.Efficiency);
            FsaSampleSpec spec = FsaSampleSpec.FromManifest(rd, chains, nuclides, true, true);
            FsaCalculationOptions.Of(rd).ApplyTo(spec);
            List<FsaComponent> library = FsaSampleLibrary.Build(spec);
            if (library.Count == 0)
            {
                Console.Error.WriteLine("библиотека пуста");
                return arm;
            }

            // Фон не подаётся: слой действует только без вычитаемого фона.
            arm.Result = analyzer.Analyze(rd.EnergySpectrum, null, rd.FwhmCalibration, library, efficiency);
            if (arm.Result == null)
            {
                Console.Error.WriteLine("отказ разбора [{0}]: {1} — {2}", name, analyzer.Refusal, analyzer.RefusalNote);
            }

            return arm;
        }

        static void Mutant(ResponseMatrix matrix, GeometryModel geometry, string good, string field, Func<string, string> change,
                           CrystalSectionRefusal expected, string what)
        {
            string value = ResponseCrystalSection.StampField(good, field);
            matrix.CrystalSection.Stamp = SetField(good, field, change(value ?? ""));
            CrystalSectionRefusal why;
            string detail;
            ResponseCrystalSection given = matrix.CrystalSectionFor(geometry, out why, out detail);
            Check(what + ": раздел не отдан, причина " + expected + " — отказ",
                  given == null && why == expected && ResponseCrystalSection.IsRefusal(why)
                  && !matrix.CrystalSection.UsableFor(geometry, matrix.BinKev));
            Console.WriteLine("  причина: {0}", ResponseCrystalSection.RefusalText(why, detail));
        }

        /// <summary>Поле клейма: заменить, добавить (перед `rcp`) или убрать (значение null).</summary>
        static string SetField(string stamp, string field, string value)
        {
            var parts = new List<string>((stamp ?? "").Split(';'));
            int at = parts.FindIndex(p => p.StartsWith(field + "=", StringComparison.Ordinal));
            if (at >= 0)
            {
                if (value == null)
                {
                    parts.RemoveAt(at);
                }
                else
                {
                    parts[at] = field + "=" + value;
                }
            }
            else if (value != null)
            {
                int rcp = parts.FindIndex(p => p.StartsWith("rcp=", StringComparison.Ordinal));
                parts.Insert(rcp >= 0 ? rcp : parts.Count, field + "=" + value);
            }

            return string.Join(";", parts.ToArray());
        }

        static bool SameSection(ResponseCrystalSection a, ResponseCrystalSection b)
        {
            if (!string.Equals(a.Stamp, b.Stamp, StringComparison.Ordinal) || a.BinKev != b.BinKev || a.Count != b.Count
                || a.Histories != b.Histories)
            {
                return false;
            }

            for (int i = 0; i < a.Count; i++)
            {
                CrystalOwnEntry x = a.Entries[i], y = b.Entries[i];
                if (x.Nuclide != y.Nuclide || x.Layer != y.Layer || x.Free != y.Free || x.Histories != y.Histories
                    || BitConverter.DoubleToInt64Bits(x.ActivityBq) != BitConverter.DoubleToInt64Bits(y.ActivityBq)
                    || !Same(x.PerDecay, y.PerDecay) || !Same(x.AlphaPerDecay, y.AlphaPerDecay))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>Числа двух разборов побитно: модель по каналам, χ², усиление, нуль, скорости компонентов.</summary>
        static bool SameResult(FsaResult a, FsaResult b)
        {
            if (!Same(a.Model, b.Model)
                || BitConverter.DoubleToInt64Bits(a.Chi2NdfPoisson) != BitConverter.DoubleToInt64Bits(b.Chi2NdfPoisson)
                || BitConverter.DoubleToInt64Bits(a.Gain) != BitConverter.DoubleToInt64Bits(b.Gain)
                || BitConverter.DoubleToInt64Bits(a.OffsetChannels) != BitConverter.DoubleToInt64Bits(b.OffsetChannels)
                || a.Components.Count != b.Components.Count)
            {
                return false;
            }

            for (int i = 0; i < a.Components.Count; i++)
            {
                if (a.Components[i].Name != b.Components[i].Name
                    || BitConverter.DoubleToInt64Bits(a.Components[i].CountRate) != BitConverter.DoubleToInt64Bits(b.Components[i].CountRate))
                {
                    return false;
                }
            }

            return true;
        }

        static bool Same(double[] a, double[] b)
        {
            if (a == null || b == null)
            {
                return a == b;
            }

            if (a.Length != b.Length)
            {
                return false;
            }

            for (int i = 0; i < a.Length; i++)
            {
                if (BitConverter.DoubleToInt64Bits(a[i]) != BitConverter.DoubleToInt64Bits(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        static int LastIndexOf(byte[] data, byte[] mark)
        {
            for (int i = data.Length - mark.Length; i >= 0; i--)
            {
                bool hit = true;
                for (int k = 0; k < mark.Length && hit; k++)
                {
                    hit = data[i + k] == mark[k];
                }

                if (hit)
                {
                    return i;
                }
            }

            return -1;
        }

        static string Sha(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
            {
                var sb = new StringBuilder();
                foreach (byte b in sha.ComputeHash(stream))
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }

                return sb.ToString();
            }
        }

        /// <summary>Сцена 38×38 — та же, что у `CrystalOwnActivityProbeA274` и `CrystalOwnShelfProbeA274c`; вещество кристалла — ключом.</summary>
        static GeometryModel Scene(string crystal, string name)
        {
            return new GeometryModel
            {
                Name = name,
                IsScintillator = true,
                Shape = CrystalShape.Cylinder,
                CrystalDiameter = 38.0,
                CrystalHeight = 38.0,
                Crystal = Material(crystal),
                Reflector = Material("Polytetrafluoroethylene"),
                Cladding = Material("Aluminum"),
                Gap = Material("Air, dry"),
                FrontReflectorThickness = 1.0,
                SideReflectorThickness = 1.5,
                FrontCladdingThickness = 1.0,
                SideCladdingThickness = 1.0,
                FrontGapThickness = 0.0,
                SideGapThickness = 0.0,
                MountingThickness = 0.0,
            };
        }

        static GeometryMaterial Material(string name)
        {
            GeometryMaterialLibrary.Entry entry = GeometryMaterialLibrary.ByName(name);
            return entry != null ? GeometryMaterialLibrary.Make(entry, entry.Density) : new GeometryMaterial();
        }

        /// <summary>Малая матрица сцены на ЦП — рецепт пробы слоя (16 узлов 30–3000 кэВ, 20 тыс. историй): секунды.</summary>
        static ResponseMatrix Build(GeometryModel geometry)
        {
            var options = new ResponseMatrixOptions
            {
                NodeCount = 16,
                Histories = 20000,
                BinKev = 2.0,
                MinEnergyKev = 30.0,
                MaxEnergyKev = 3000.0,
            };
            ResponseMatrix matrix = ResponseMatrixBuilder.Build(geometry, options, null, System.Threading.CancellationToken.None);
            Console.WriteLine("матрица собрана: {0}, {1} узлов", geometry.Name, matrix.Energies != null ? matrix.Energies.Length : 0);
            return matrix;
        }

        static ResultData Load(string path)
        {
            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            EnergySpectrum s = rd.EnergySpectrum;
            if (s != null && s.Spectrum != null && s.TotalPulseCount == 0)
            {
                long total = 0;
                for (int i = 0; i < s.Spectrum.Length; i++)
                {
                    total += s.Spectrum[i];
                }

                s.TotalPulseCount = total;
                s.ValidPulseCount = total;
            }

            return rd;
        }

        /// <summary>
        /// СТЕНД ключа `CorpusFsaProbe --crystal-own=`: копия спектра корпуса с геометрией LaBr3 38×38 и ссылкой на
        /// матрицу по Guid. Корпус не трогается: вход читается, выход пишется в свой каталог.
        /// </summary>
        static int Demo(string input, string output, string guid, string matrixPath)
        {
            if (output == null || guid == null)
            {
                Console.Error.WriteLine("нужны --demo-out= и --demo-guid=");
                return 2;
            }

            var serializer = new XmlSerializer(typeof(ResultDataFile));
            ResultDataFile file;
            using (var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                file = (ResultDataFile)serializer.Deserialize(stream);
            }

            ResultData rd = file.ResultDataList[0];
            if (rd.Efficiency == null)
            {
                rd.Efficiency = new EfficiencyConfigData();
            }

            rd.Efficiency.Geometry = Scene("Lanthanum bromide", "LaBr3 38x38 (AMBER274)");
            rd.Efficiency.Guid = guid;
            rd.Efficiency.UseResponseMatrix = true;
            if (matrixPath != null)
            {
                // Кривая стенда — Σ канала пика самой матрицы в её узлах: разбор без кривой отказывает (гейт `A277`).
                ResponseMatrix matrix = ResponseMatrix.Load(matrixPath);
                if (matrix == null || !matrix.IsValidFor(rd.Efficiency.Geometry))
                {
                    Console.Error.WriteLine("матрица стенда не годна сцене: " + matrixPath);
                    return 1;
                }

                float[][] peak = matrix.ChannelRows[(int)EfficiencySimulator.ResponseChannel.Peak];
                var curve = new List<ROIEfficiencyData>();
                for (int i = 0; i < matrix.Energies.Length; i++)
                {
                    double sum = 0.0;
                    foreach (float v in peak[i])
                    {
                        sum += v;
                    }

                    if (sum > 0.0)
                    {
                        curve.Add(new ROIEfficiencyData { Energy = matrix.Energies[i], Efficiency = sum, ErrorPercent = 0.0 });
                    }
                }

                rd.Efficiency.Curve = curve;
                Console.WriteLine("стенд: кривая из пика матрицы, точек {0}", curve.Count);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            using (var stream = new FileStream(output, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(stream, file);
            }

            Console.WriteLine("стенд: {0} → {1}; геометрия есть: {2}, матрица по Guid {3}, UseResponseMatrix {4}",
                              input, output, rd.Efficiency.HasGeometry, rd.Efficiency.Guid, rd.Efficiency.UseResponseMatrix);
            return rd.Efficiency.HasGeometry ? 0 : 1;
        }
    }
}
