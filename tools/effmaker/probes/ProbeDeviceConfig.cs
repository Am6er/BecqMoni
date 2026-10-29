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
// при открытии документа: ссылка `DeviceConfigReference.Guid` ищется в
// `DeviceConfigManager`, найденный прибор кладётся в `ResultData.DeviceConfig`, и
// его настройки поиска пиков клонируются спектру.
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
    /// `DocumentManager.CheckDocument`: у спектра без своей кривой приложение строит
    /// умолчание из трёх чисел настроек прибора (`FwhmCalibration.DefaultCalibration`),
    /// а НЕ берёт кривую, сохранённую в его настройках поиска. Пробы брали её
    /// (`cfg.FwhmCalibration`, блок после этого вызова в каждой из ~60 проб), и на
    /// трёх чужих RC-110 без своей кривой разбор пробы расходился с окном: у прибора
    /// лежала встроенная кривая 15/103 при числах 4/266/26 — пик шире на треть
    /// (38.7 кан. против 30 при 1173 кэВ), другие пики поиска, другой состав «Из
    /// NucBase», χ² 5.94 / 62.40 / 645.30 против 3.73 / 8.94 / 25.50 на экране. После
    /// этой правки блоки проб ниже холостые: кривая уже стоит. Корпус не задет — все
    /// 136 его спектров несут свою кривую, и ни у одного из 24 его приборов
    /// сохранённой кривой нет (проверено 29.10.2026).
    /// </summary>
    public static string Attach(ResultData rd)
    {
        string said = AttachDevice(rd);
        if (rd != null && rd.FwhmCalibration == null && rd.EnergySpectrum != null
            && rd.PeakDetectionMethodConfig is FWHMPeakDetectionMethodConfig cfg)
        {
            rd.FwhmCalibration = FwhmCalibration.DefaultCalibration(cfg, rd.EnergySpectrum.EnergyCalibration);
        }

        return said;
    }

    static string AttachDevice(ResultData rd)
    {
        if (rd == null)
        {
            return "спектра нет";
        }

        if (rd.DeviceConfigReference == null || string.IsNullOrEmpty(rd.DeviceConfigReference.Guid))
        {
            return "ссылки на прибор в файле нет — настройки поиска остаются умолчанием библиотеки";
        }

        DeviceConfigInfo device;
        if (!DeviceConfigManager.GetInstance().DeviceConfigMap.TryGetValue(
                rd.DeviceConfigReference.Guid, out device)
            || device == null)
        {
            return "прибора «" + rd.DeviceConfigReference.Name + "» ("
                   + rd.DeviceConfigReference.Guid
                   + ") в конфигурации НЕТ — настройки поиска остаются умолчанием библиотеки";
        }

        rd.DeviceConfig = device;

        // `AdoptFrom`, а не голый `Clone`: это каноническое правило приложения —
        // из прибора берётся всё, кроме того, что принадлежит СПЕКТРУ (модель
        // ПШПВ и галка Enabled). Сейчас у прочитанного спектра своей копии не
        // бывает вовсе, и `AdoptFrom(прибор, null)` равен клону; правило же
        // останется верным и в тот день, когда копия появится.
        rd.PeakDetectionMethodConfig = FWHMPeakDetectionMethodConfig.AdoptFrom(
            device.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig,
            rd.PeakDetectionMethodConfig as FWHMPeakDetectionMethodConfig);

        return device.Name;
    }
}
