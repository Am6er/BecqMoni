using BecquerelMonitor;
using System;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace DeviceAnalysisProbe
{
    /// <summary>
    /// Правка вкладки Analysis в настройках прибора — открытому спектру.
    ///
    /// ⚠ Общего правила `ProbeDeviceConfig` (`S82`) здесь НЕТ И НЕ НАДО (`T59`,
    /// разобрано 23.08.2026): проба не читает спектр из файла вовсе — и прибор,
    /// и спектр она заводит СВОИ, синтетические, потому что проверяет само
    /// правило переноса (`AdoptFrom`), а не то, как оно применяется к чужим
    /// данным. Позвать здесь общий разбор значило бы мерить его же собой.
    ///
    /// Повод. Спектр держит СВОЮ копию настроек поиска пиков: SNR и допуск
    /// правятся в панели поиска для одного спектра, а не для всех разом. Копия
    /// снималась один раз, при открытии документа, и сохранение конфигурации
    /// прибора до открытого спектра не доходило вовсе — на экране оставались
    /// пики, найденные по старому SNR. Молча: ни ошибки, ни признака.
    ///
    /// Проверяются две вещи, и обе иначе не видны:
    ///
    ///  1. **Что переносится, а что остаётся.** Числа поиска берутся у прибора,
    ///     калибровка ПШПВ с формой пика и признак показа пиков — у спектра.
    ///     Перенести калибровку значило бы стереть подбор по этому спектру,
    ///     не перенести числа — не сделать ровно то, о чём просили.
    ///  2. **У сигнала есть потребитель.** Сохранение поднимает
    ///     `DeviceConfigListChanged`, обработчик обязан позвать разноску по
    ///     документам, а та — снять копию и пересчитать пики. Заведённый и
    ///     никем не прочитанный сигнал — ошибка того же вида, что чинилась.
    ///
    /// Разноска по документам целиком отсюда не гоняется: `MainForm` без окна
    /// не собрать. Проверяется её состав — что она зовёт, — а сама она
    /// проверяется в приложении.
    ///
    ///     devanalysisprobe
    ///
    /// Ожидание: «ВСЕ СОШЛИСЬ». Собирать с `WeifenLuo.WinFormsUI.Docking.dll`.
    /// </summary>
    static class Program
    {
        static int failed;

        static int Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;

            NumbersComeFromDevice();
            SpectrumKeepsItsOwn();
            MissingPartsFallBack();
            SavedConfigReachesDocuments();

            Console.WriteLine();
            Console.WriteLine(failed == 0 ? "ВСЕ СОШЛИСЬ" : "РАСХОЖДЕНИЙ: " + failed);
            return failed == 0 ? 0 : 1;
        }

        /// <summary>Числа поиска пиков — те, что задали прибору.</summary>
        static void NumbersComeFromDevice()
        {
            Console.WriteLine("=== Числа поиска берутся у прибора");
            FWHMPeakDetectionMethodConfig device = Device();
            FWHMPeakDetectionMethodConfig spectrum = Spectrum();
            FWHMPeakDetectionMethodConfig got = FWHMPeakDetectionMethodConfig.AdoptFrom(device, spectrum);

            Near("Min_SNR", got.Min_SNR, 4.0);
            Near("Tolerance", got.Tolerance, 1.5);
            Near("Min_Range", got.Min_Range, 12.0);
            Near("Max_Range", got.Max_Range, 2900.0);
            Near("Min_FWHM_Tol", (double)got.Min_FWHM_Tol, 30.0);
            Near("Max_FWHM_Tol", (double)got.Max_FWHM_Tol, 170.0);
            Same("Max_Items", got.Max_Items, 60);
            Same("Ch_Concat", got.Ch_Concat, 2048);
            Near("PeakWidthWidenFactor", got.PeakWidthWidenFactor, 1.7);
            Flag("UseCenterOfMassCentroid", got.UseCenterOfMassCentroid, false);

            // Копия, а не ссылка: правка прибора задним числом не должна менять
            // настройки уже открытого спектра.
            if (object.ReferenceEquals(got, device))
            {
                Fail("спектру отдана та же ссылка — прибор и спектр стали одним объектом");
            }
            else
            {
                Console.WriteLine("    ок: спектр получил копию, а не ссылку");
            }
        }

        /// <summary>
        /// Калибровка ПШПВ и признак показа пиков принадлежат спектру.
        /// </summary>
        static void SpectrumKeepsItsOwn()
        {
            Console.WriteLine();
            Console.WriteLine("=== Своё у спектра остаётся своим");
            FWHMPeakDetectionMethodConfig device = Device();
            FWHMPeakDetectionMethodConfig spectrum = Spectrum();
            FWHMPeakDetectionMethodConfig got = FWHMPeakDetectionMethodConfig.AdoptFrom(device, spectrum);

            if (got.FwhmCalibration == null)
            {
                Fail("калибровка ПШПВ потеряна вовсе");
                return;
            }

            // У спектра форма пика подобрана своя (ExpGaussExp), у прибора
            // стоит гауссиана. Победить обязан спектр: подбор шёл по нему.
            Same("форма пика", got.FwhmCalibration.PeakType, spectrum.FwhmCalibration.PeakType);
            Near("левый хвост", got.FwhmCalibration.ExpGaussExpLeftTail,
                 spectrum.FwhmCalibration.ExpGaussExpLeftTail);
            if (object.ReferenceEquals(got.FwhmCalibration, spectrum.FwhmCalibration))
            {
                Fail("калибровка отдана той же ссылкой — правка настроек поедет в файл спектра");
            }
            else
            {
                Console.WriteLine("    ок: калибровка спектра перенесена копией");
            }

            // Кнопка показа пиков — состояние панели спектра, а не прибора.
            Flag("показ пиков", got.Enabled, false);
        }

        /// <summary>Чего нет у спектра — берётся у прибора, и наоборот.</summary>
        static void MissingPartsFallBack()
        {
            Console.WriteLine();
            Console.WriteLine("=== Когда брать нечего");
            FWHMPeakDetectionMethodConfig device = Device();
            FWHMPeakDetectionMethodConfig bare = new FWHMPeakDetectionMethodConfig();
            bare.FwhmCalibration = null;
            FWHMPeakDetectionMethodConfig got = FWHMPeakDetectionMethodConfig.AdoptFrom(device, bare);
            if (got.FwhmCalibration == null)
            {
                Fail("у спектра калибровки нет, у прибора есть — а в итоге нет ни у кого");
            }
            else
            {
                Console.WriteLine("    ок: без своей калибровки берётся приборная");
            }

            // Прибор без настроек поиска (чужой файл): спектр остаётся при своих,
            // а не обнуляется.
            FWHMPeakDetectionMethodConfig spectrum = Spectrum();
            FWHMPeakDetectionMethodConfig kept = FWHMPeakDetectionMethodConfig.AdoptFrom(null, spectrum);
            if (!object.ReferenceEquals(kept, spectrum))
            {
                Fail("без настроек у прибора спектр не оставили при своих");
            }
            else
            {
                Console.WriteLine("    ок: у прибора настроек нет — спектр при своих");
            }
        }

        /// <summary>
        /// Сигнал доходит до документов: обработчик сохранения зовёт разноску,
        /// разноска — перенос настроек и пересчёт пиков.
        /// </summary>
        static void SavedConfigReachesDocuments()
        {
            Console.WriteLine();
            Console.WriteLine("=== Сохранение доходит до открытых спектров");
            Type main = typeof(NuclideDefinition).Assembly.GetType("BecquerelMonitor.MainForm");
            if (main == null)
            {
                Fail("класса MainForm в сборке нет");
                return;
            }

            Calls(main, "manager_DeviceConfigChanged", "ApplyDeviceConfigToDocuments");
            Calls(main, "ApplyDeviceConfigToDocuments", "AdoptFrom");
            Calls(main, "ApplyDeviceConfigToDocuments", "UpdateDetectedPeakView");
            Calls(main, "ApplyDeviceConfigToDocuments", "UpdateEnergySpectrum");
        }

        // --------------------------------------------------------------

        /// <summary>Настройки, «заданные прибору»: всё отличается от умолчаний.</summary>
        static FWHMPeakDetectionMethodConfig Device()
        {
            FWHMPeakDetectionMethodConfig config = new FWHMPeakDetectionMethodConfig
            {
                Min_SNR = 4.0,
                Tolerance = 1.5,
                Min_Range = 12.0,
                Max_Range = 2900.0,
                Min_FWHM_Tol = 30m,
                Max_FWHM_Tol = 170m,
                Max_Items = 60,
                Ch_Concat = 2048,
                PeakWidthWidenFactor = 1.7,
                UseCenterOfMassCentroid = false,
                Enabled = true,
            };
            config.FwhmCalibration.PeakType = FwhmCalibration.GaussianPeakType;
            return config;
        }

        /// <summary>Настройки спектра: своя форма пика и выключенный показ.</summary>
        static FWHMPeakDetectionMethodConfig Spectrum()
        {
            FWHMPeakDetectionMethodConfig config = new FWHMPeakDetectionMethodConfig
            {
                Min_SNR = 10.0,
                Tolerance = 11.0,
                Enabled = false,
            };
            config.FwhmCalibration.PeakType = FwhmCalibration.ExpGaussExpPeakType;
            config.FwhmCalibration.ExpGaussExpLeftTail = 0.77;
            return config;
        }

        /// <summary>
        /// Зовёт ли метод <paramref name="callee"/>. Тело читается как байты, и
        /// токены разбираются подряд — разметку IL проба не строит: ложное
        /// совпадение должно было бы разрешиться в метод РОВНО с этим именем.
        /// </summary>
        static void Calls(Type type, string method, string callee)
        {
            MethodInfo info = type.GetMethod(method,
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (info == null)
            {
                Fail("метода " + method + " в MainForm нет");
                return;
            }

            byte[] il = info.GetMethodBody().GetILAsByteArray();
            for (int i = 0; i + 4 < il.Length; i++)
            {
                if (il[i] != 0x28 && il[i] != 0x6F)
                {
                    continue;   // не call и не callvirt
                }

                try
                {
                    MethodBase target = type.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1));
                    if (target != null && target.Name == callee)
                    {
                        Console.WriteLine("    ок: {0} зовёт {1}", method, callee);
                        return;
                    }
                }
                catch (Exception)
                {
                    // Не всякая четвёрка байт — токен: смещение угадано неверно.
                }
            }

            Fail(method + " не зовёт " + callee + " — правка до спектра не дойдёт");
        }

        static void Near(string caption, double got, double want, double tolerance = 1e-9)
        {
            if (Math.Abs(got - want) > tolerance)
            {
                Fail(string.Format(CultureInfo.InvariantCulture,
                                   "{0}: {1}, ожидалось {2}", caption, got, want));
            }
            else
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                                                "    ок: {0} = {1}", caption, got));
            }
        }

        static void Same(string caption, int got, int want)
        {
            if (got != want)
            {
                Fail(caption + ": " + got + ", ожидалось " + want);
            }
            else
            {
                Console.WriteLine("    ок: {0} = {1}", caption, got);
            }
        }

        static void Flag(string caption, bool got, bool want)
        {
            if (got != want)
            {
                Fail(caption + ": " + got + ", ожидалось " + want);
            }
            else
            {
                Console.WriteLine("    ок: {0} = {1}", caption, got);
            }
        }

        static void Fail(string message)
        {
            failed++;
            Console.WriteLine("    РАСХОЖДЕНИЕ: " + message);
        }
    }
}
