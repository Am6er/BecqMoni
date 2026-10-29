using System.ComponentModel;

namespace BecquerelMonitor
{
    /// <summary>
    /// ⛔ (`AMBER276`, 29.10.2026) ОКНО ЕДИНОЙ ВКЛАДКИ КАЛИБРОВКИ — энергия и FWHM на одной
    /// вкладке, одной таблицей. Реализация целиком — в <see cref="DCFwhmCalibrationView"/>
    /// (там же постановка Amber дословно); этот наследник ничего не добавляет, кроме
    /// ИМЕНИ ТИПА.
    ///
    /// Имя типа — строка раскладки окон (<c>DockContent.GetPersistString</c> =
    /// <c>GetType().ToString()</c>), а прежняя вкладка калибровки по энергии жила именно
    /// под ним: его несут поставочная раскладка (<c>config\layout\ExpertMode.xml</c>),
    /// раскладки людей и прежние версии программы. Окно, заведённое этим типом, встаёт
    /// на место прежней вкладки энергии в любой прежней раскладке, а раскладка,
    /// записанная новой версией, у прежней версии открывает её вкладку энергии. Строка
    /// прежней вкладки FWHM (<c>BecquerelMonitor.DCFwhmCalibrationView</c>) разбирается в
    /// <c>MainForm.GetContentFromPersistString</c>.
    /// </summary>
    [DesignerCategory("Form")]
    public class DCEnergyCalibrationView : DCFwhmCalibrationView
    {
        public DCEnergyCalibrationView(MainForm mainForm) : base(mainForm)
        {
        }

        /// <summary>
        /// (`AMBER271`) Строка вопроса о кривизне тракта — прежнее место метода (его зовёт
        /// по имени `TractCurvatureFileProbeA271`); считает <see cref="DCFwhmCalibrationView"/>.
        /// </summary>
        internal static new string TractCurvatureChangeNote(ResultData data)
        {
            return DCFwhmCalibrationView.TractCurvatureChangeNote(data);
        }
    }
}
