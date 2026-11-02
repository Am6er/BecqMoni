using BecquerelMonitor;
using System;

// Разбор ссылки на прибор при чтении спектра пробой — ОДНИМ правилом на все
// пробы (`S82`).
//
// Файл без `Main` — идёт довеском к своим пробам, как `ResidualScan.cs` и
// `GadrasDetector.cs`; `build_all.ps1` знает про такие.
//
// ⛔ ЗАЧЕМ. `ResultData.PeakDetectionMethodConfig` помечен `[XmlIgnore]` — в
// файле спектра его НЕТ НИКОГДА, ни у одного спектра, ни у корпусного, ни у
// пользовательского. В приложении его ставит `DocumentManager.PrepareDeviceConfig`
// при открытии документа: прибор ищется `DocumentManager.AttachDeviceConfig` (Guid
// ссылки, затем серийный номер, затем имя — `AMBER269`), найденный кладётся в
// `ResultData.DeviceConfig`, и его настройки поиска пиков клонируются спектру.
//
// Пробы этого не делали. `ResultData.DeviceConfig` тоже `[XmlIgnore]` и заведён
// полем `= new DeviceConfigInfo()`, то есть после чтения файла там лежит ПУСТОЙ
// прибор по умолчанию — не null, поэтому проверка «прибор есть?» проходила, и
// пробы молча брали УМОЛЧАНИЯ БИБЛИОТЕКИ: SNR 10, допуск 10, диапазон
// 30…2800 кэВ, мёртвое время 0.36 мс. Измерено 19.08.2026 на
// `Cs 137 в домике 24.11.2022.xml`: у Amber на экране допуск 11 и диапазон
// от 5 кэВ, пиков 14; на стенде — допуск 10, от 30 кэВ, пиков 11. Настройки
// поиска задают ПОДПИСИ пиков, подписи задают состав библиотеки (`S57`), состав
// задаёт разложение — то есть проба показывала разбор не того спектра, который
// видит человек. Это и была причина `S82`.
//
// ⚠ У корпусных приборов SNR = 4, а умолчание библиотеки = 10, и диапазоны у
// половины групп 15 или 20 кэВ вместо 30. Значит ВСЕ корпусные прогоны шли не
// теми настройками — строка `B21`.
static class ProbeDeviceConfig
{
    /// <summary>
    /// Поставить спектру его прибор и его настройки поиска пиков — ровно так,
    /// как это делает приложение при открытии документа.
    ///
    /// Возвращает строку для печати: имя прибора либо причину отказа. Отказ
    /// ОБЯЗАН быть напечатан вызывающим — молчаливый откат на умолчания и есть
    /// то, чем `S82` стоила двух сессий. Своего `Console` здесь нет нарочно:
    /// у корпусной пробы 126 спектров, и печатать построчно решает она.
    ///
    /// ⛔ (`AMBER269`, 29.10.2026) ЗДЕСЬ ЖЕ — КРИВАЯ FWHM, ТЕМ ЖЕ ПРАВИЛОМ, ЧТО У
    /// `DocumentManager.CheckDocument`: у спектра без своей кривой приложение строило
    /// (до 02.11.2026, см. последний абзац) умолчание из трёх чисел настроек прибора
    /// (`FwhmCalibration.DefaultCalibration`), а НЕ брало кривую, сохранённую в его
    /// настройках поиска. Пробы брали её
    /// (`cfg.FwhmCalibration`, блок после этого вызова в каждой из ~60 проб), и на
    /// трёх чужих RC-110 без своей кривой разбор пробы расходился с окном: у прибора
    /// лежала встроенная кривая 15/103 при числах 4/266/26 — пик шире на треть
    /// (38.7 кан. против 30 при 1173 кэВ), другие пики поиска, другой состав «Из
    /// NucBase», χ² 5.94 / 62.40 / 645.30 против 3.73 / 8.94 / 25.50 на экране. После
    /// этой правки блоки проб ниже холостые: кривая уже стоит. Корпус не задет — все
    /// 136 его спектров несут свою кривую, и ни у одного из 24 его приборов
    /// сохранённой кривой нет (проверено 29.10.2026).
    ///
    /// ⛔ (`AMBER269`, 02.11.2026) ПРИБОР — ТЕМ ЖЕ ВЫЗОВОМ, ЧТО У ДВЕРИ ОТКРЫТИЯ
    /// (`DocumentManager.AttachDeviceConfig` → `MatchDeviceConfig`: Guid, затем
    /// серийный номер, затем имя; решения Amber 01.11.2026 вопросником, дословно:
    /// «По серийному номеру (Рекомендую)», «По номеру, затем имени (Рекомендую)»).
    /// Прежде здесь был поиск только по Guid, и экспорт RadiaCode (в ссылке одно
    /// имя модели, номер — в спектре) проба разбирала без прибора, на умолчаниях
    /// библиотеки, а окно — с прибором; для замера таким файлам подкладывали
    /// `_dev.xml` с Guid. И настройки поиска — КОПИЕЙ настроек прибора, как в
    /// приложении, а не `AdoptFrom`: у прочитанного спектра настройки не пусты, а
    /// заведены полем (`new FWHMPeakDetectionMethodConfig()`), и `AdoptFrom`
    /// оставлял спектру встроенную заглушку кривой 15/3756/103 вместо кривой
    /// прибора — с её формой пика. Кривая FWHM — тем же правилом, что
    /// `DocumentManager.StandInFwhm` (`FwhmCalibration.StandInCalibration`: три числа
    /// и форма пика прибора).
    ///
    /// ⛔ (`AMBER269`, 02.11.2026, позже того же дня) Правило открытия сменилось: решение
    /// Amber вопросником, дословно: «Кривую прибора (Рекомендую)» — спектр без своей
    /// кривой получает СОХРАНЁННУЮ кривую прибора, как от «Применить»
    /// (`FwhmCalibration.StandInFromDevice`: копия; на чужой сетке каналов —
    /// перенесённая; если по ней нельзя — три числа). Проба зовёт тот же вызов с теми
    /// же сетками, что `DocumentManager.StandInFwhm`. Корпус не задет: кривая своя у
    /// всех его спектров.
    ///
    /// ⛔ (`AMBER269`, 02.11.2026, полоса a269fix6) ВСТРОЕННАЯ ЗАГЛУШКА FWHM 15/3756/103 В
    /// ФАЙЛЕ СПЕКТРА — не своя кривая, а её отсутствие, как при открытии
    /// (`DocumentManager.ForgetFileStandInFwhm`). Прежде проба брала её своей кривой: съёмка
    /// RC-110 тория в Маринелли разбиралась с пиками в 1.5–1.8 раза шире данных. Снятая
    /// заглушка называется в строке, которую печатает вызывающий; корпус не задет — заглушки
    /// нет ни у одного из 136 спектров (замер 02.11.2026).
    /// </summary>
    public static string Attach(ResultData rd)
    {
        string said = AttachDevice(rd);
        bool fileStandIn = DocumentManager.ForgetFileStandInFwhm(rd);
        string refusal = null;
        if (rd != null && rd.FwhmCalibration == null && rd.EnergySpectrum != null
            && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            rd.FwhmCalibration = FwhmCalibration.StandInFromDevice(cfg,
                                                                   rd.DeviceConfig != null ? rd.DeviceConfig.NumberOfChannels : 0,
                                                                   rd.EnergySpectrum.EnergyCalibration,
                                                                   rd.EnergySpectrum.NumberOfChannels,
                                                                   out refusal);
        }
        if (fileStandIn)
        {
            // ⚠ Строчными: вызывающие ищут в этой строке «НЕТ» (отказ прибора).
            said += "; кривая FWHM файла — встроенная заглушка 15/3756/103, не принята; "
                    + (rd.FwhmCalibration == null
                       ? "модели разрешения нет" + (string.IsNullOrEmpty(refusal) ? "" : ": " + refusal)
                       : IsStandIn(rd.FwhmCalibration)
                         ? "прибора нет — та же заглушка по встроенным числам, как у спектра без кривой"
                         : "взята кривая прибора");
        }

        return said;
    }

    /// <summary>Кривая — встроенная заглушка (правилом приложения, через его открытый вызов).</summary>
    static bool IsStandIn(FwhmCalibration curve)
    {
        ResultData probe = new ResultData();
        probe.FwhmCalibration = curve.Clone();
        return DocumentManager.ForgetFileStandInFwhm(probe);
    }

    static string AttachDevice(ResultData rd)
    {
        if (rd == null)
        {
            return "спектра нет";
        }

        DocumentManager.DeviceMatch how;
        System.Collections.Generic.List<DeviceConfigInfo> candidates;
        DeviceConfigInfo device = DocumentManager.AttachDeviceConfig(
            rd, DeviceConfigManager.GetInstance().DeviceConfigList, out how, out candidates);
        if (device != null)
        {
            return how == DocumentManager.DeviceMatch.Guid
                ? device.Name
                : device.Name + " (найден " + (how == DocumentManager.DeviceMatch.Serial ? "по серийному номеру" : "по имени") + ")";
        }

        string name = rd.DeviceConfigReference != null ? rd.DeviceConfigReference.Name : null;
        string guid = rd.DeviceConfigReference != null ? rd.DeviceConfigReference.Guid : null;
        if (how == DocumentManager.DeviceMatch.Ambiguous)
        {
            // «НЕТ» в начале — его ищут вызывающие (`note.IndexOf("НЕТ")`), как у отказа ниже.
            return "прибора НЕТ, подходят несколько: " + DocumentManager.DescribeDeviceAmbiguity(rd, 0, candidates)
                   + " — настройки поиска остаются умолчанием библиотеки";
        }
        if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(guid))
        {
            return "ссылки на прибор в файле нет — настройки поиска остаются умолчанием библиотеки";
        }
        return "прибора «" + name + "» (" + (string.IsNullOrEmpty(guid) ? "без Guid" : guid)
               + ") в конфигурации НЕТ ни по Guid, ни по номеру, ни по имени — настройки поиска остаются умолчанием библиотеки";
    }
}
