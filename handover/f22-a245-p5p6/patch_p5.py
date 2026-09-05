# -*- coding: utf-8 -*-
"""
Доля П5 строки `A244` (полоса F22, 05.09.2026): печать и разбор чисел в
панелях `DC*` — инвариантной культурой, обе стороны разом.

Правки заданы ЦЕЛЫМИ строками исходника: скрипт требует, чтобы строка «до»
встречалась в файле РОВНО столько раз, сколько сказано, иначе отказывает. Так
правка не расползается на однотипные соседние места молча.

⛔ Группировки разрядов не заводим вовсе (решение Amber 05.09.2026): формат
   остаётся тем же («f2», «f4», «0.0», «0.#####», умолчание), меняется ТОЛЬКО
   культура. Ни `n1`, ни `N0` здесь не появляются.
"""
import io, sys, os

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..'))

# (файл, сколько раз, строка «до», строка «после»)
EDITS = [
    # ---------------- DCControlPanel.cs --------------------------------
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     'using System.Drawing;',
     'using System.Drawing;\nusing System.Globalization;'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '                if (!int.TryParse(this.realTimeLimitTextBox.Text, out result))',
     '                if (!UserNumber.TryParseInt(this.realTimeLimitTextBox.Text, out result))'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '                this.realTimeLimitTextBox.Text = value.ToString();',
     '                this.realTimeLimitTextBox.Text = value.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.realTimeLimitTextBox.Text = resultDataStatus.PresetTime.ToString();',
     '            this.realTimeLimitTextBox.Text = resultDataStatus.PresetTime.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.totalCntTextBox.Text = activeResultData.EnergySpectrum.TotalPulseCount.ToString();',
     '            this.totalCntTextBox.Text = activeResultData.EnergySpectrum.TotalPulseCount.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.validCntTextBox.Text = activeResultData.EnergySpectrum.ValidPulseCount.ToString();',
     '            this.validCntTextBox.Text = activeResultData.EnergySpectrum.ValidPulseCount.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.invalidCountsTextBox.Text = invalidPulseCount.ToString(); //invalid pulses',
     '            this.invalidCountsTextBox.Text = invalidPulseCount.ToString(CultureInfo.InvariantCulture); //invalid pulses'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.liveTimetextBox.Text = activeResultData.EnergySpectrum.LiveTime.ToString("f2");',
     '            this.liveTimetextBox.Text = activeResultData.EnergySpectrum.LiveTime.ToString("f2", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.countRateTextBox.Text = cps.ToString("f2");',
     '            this.countRateTextBox.Text = cps.ToString("f2", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.deadTimetextBox.Text = deadTime.ToString("f4");',
     '            this.deadTimetextBox.Text = deadTime.ToString("f4", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            this.percentageProgressBar1.PriorText = ((int)totalSeconds).ToString();',
     '            this.percentageProgressBar1.PriorText = ((int)totalSeconds).ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCControlPanel.cs', 1,
     '            if (!int.TryParse(this.realTimeLimitTextBox.Text, out presetTime))',
     '            if (!UserNumber.TryParseInt(this.realTimeLimitTextBox.Text, out presetTime))'),

    # ---------------- DCCountRateView.cs -------------------------------
    ('BecquerelMonitor/DCCountRateView.cs', 1,
     'using System.Drawing;',
     'using System.Drawing;\nusing System.Globalization;'),
    ('BecquerelMonitor/DCCountRateView.cs', 1,
     '            this.LossCountsRatioValLbl.Text = sEffRatio.ToString();',
     '            this.LossCountsRatioValLbl.Text = sEffRatio.ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCCountRateView.cs', 1,
     '            this.cpslabel.Text = Cps.ToString("f2");',
     '            this.cpslabel.Text = Cps.ToString("f2", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCCountRateView.cs', 1,
     '            this.DeadTimeValLbl.Text = deadTime.ToString("f4");',
     '            this.DeadTimeValLbl.Text = deadTime.ToString("f4", CultureInfo.InvariantCulture);'),

    # ---------------- DCEnergyCalibrationView.cs -----------------------
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     'using System.Drawing;\nusing System.Linq;',
     'using System.Drawing;\nusing System.Globalization;\nusing System.Linq;'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 3,
     '            this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString();',
     '            this.numericUpDown3.Text = this.energyCalibration.Coefficients[0].ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 3,
     '            this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString();',
     '            this.numericUpDown2.Text = this.energyCalibration.Coefficients[1].ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 3,
     '                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString();',
     '                this.numericUpDown1.Text = this.energyCalibration.Coefficients[2].ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 3,
     '                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString();',
     '                this.numericUpDown5.Text = this.energyCalibration.Coefficients[3].ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 3,
     '                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString();',
     '                this.numericUpDown4.Text = this.energyCalibration.Coefficients[4].ToString(CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '            String text = String.Format(Resources.ResetCalibrationToDeviceSettingsConflict, defaultEnergyCalibration.PolynomialOrder, energyCalibration.PolynomialOrder);',
     '            String text = String.Format(CultureInfo.InvariantCulture, Resources.ResetCalibrationToDeviceSettingsConflict, defaultEnergyCalibration.PolynomialOrder, energyCalibration.PolynomialOrder);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '                row.Cells.Add(new Cell(num.ToString()));',
     '                row.Cells.Add(new Cell(num.ToString(CultureInfo.InvariantCulture)));'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '                    this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints[row.Index].Channel = (int)decimal.Parse(text);',
     '                    this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints[row.Index].Channel = (int)UserNumber.ParseDecimal(text);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '                    this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints[row.Index].Energy = decimal.Parse(text2);',
     '                    this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints[row.Index].Energy = UserNumber.ParseDecimal(text2);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '                    this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints[row.Index].Count = int.Parse(text3);',
     '                    this.mainForm.ActiveDocument.ActiveResultData.CalibrationPoints[row.Index].Count = UserNumber.ParseInt(text3);'),
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '            this.mainForm.SetStatusTextLeft(String.Format("{0} {1}: {2:0.00000}", Resources.MSGCalibrationDone, Resources.MSGMSE, mse));',
     '            this.mainForm.SetStatusTextLeft(String.Format(CultureInfo.InvariantCulture, "{0} {1}: {2:0.00000}", Resources.MSGCalibrationDone, Resources.MSGMSE, mse));'),
    # ⚠ Разбор поля ввода: терпимо (сначала инвариант, потом культура системы) —
    #    решение Amber. Прежний код разбирал ТОЛЬКО инвариантом, и «1,5» с
    #    русской раскладки отвергался; `str.ToString(...)` над строкой был
    #    вовсе холостым вызовом.
    ('BecquerelMonitor/DCEnergyCalibrationView.cs', 1,
     '            if (double.TryParse(str.ToString(System.Globalization.CultureInfo.InvariantCulture),\n'
     '                System.Globalization.NumberStyles.Float,\n'
     '                System.Globalization.CultureInfo.InvariantCulture,\n'
     '                out result))',
     '            if (UserNumber.TryParseDouble(str, out result))'),

    # ---------------- DCFwhmCalibrationView.cs -------------------------
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                row.Cells.Add(new Cell(position.ToString()));',
     '                row.Cells.Add(new Cell(position.ToString(CultureInfo.InvariantCulture)));'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '            minPeaksRequirementLabel.Text = String.Format(Resources.MinPeaksRequirement, minPeaksRequirement);',
     '            minPeaksRequirementLabel.Text = String.Format(CultureInfo.InvariantCulture, Resources.MinPeaksRequirement, minPeaksRequirement);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.ExpGaussExpLeftTail.ToString("0.0");',
     '                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.ExpGaussExpLeftTail.ToString("0.0", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.ExpGaussExpRightTail.ToString("0.0");',
     '                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.ExpGaussExpRightTail.ToString("0.0", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.VoigtSigma.ToString("0.0");',
     '                peakShapeFirstParameterValueLabel.Text = fwhmCalibration.VoigtSigma.ToString("0.0", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.VoigtGamma.ToString("0.0");',
     '                peakShapeSecondParameterValueLabel.Text = fwhmCalibration.VoigtGamma.ToString("0.0", CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '            messageBuilder.AppendFormat(\n'
     '                GetResourceText("PeakFitChiTablePeakSummary", "Selected shape: {0}."),',
     '            messageBuilder.AppendFormat(\n'
     '                CultureInfo.InvariantCulture,\n'
     '                GetResourceText("PeakFitChiTablePeakSummary", "Selected shape: {0}."),'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                messageBuilder.AppendFormat(\n'
     '                    "{0}: {1} = {2}; {3} = {4}; {5}: {6}",',
     '                messageBuilder.AppendFormat(\n'
     '                    CultureInfo.InvariantCulture,\n'
     '                    "{0}: {1} = {2}; {3} = {4}; {5}: {6}",'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 2,
     '            return String.Format(\n'
     '                "{0}={1:0.0}; {2}={3:0.0}",',
     '            return String.Format(\n'
     '                CultureInfo.InvariantCulture,\n'
     '                "{0}={1:0.0}; {2}={3:0.0}",'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                ? (chi2 / ndp).ToString("0.#####")',
     '                ? (chi2 / ndp).ToString("0.#####", CultureInfo.InvariantCulture)'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                ? score.ToString("0.#####")',
     '                ? score.ToString("0.#####", CultureInfo.InvariantCulture)'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                calibrationPeaks[row.Index].Channel = int.Parse(textvalue);',
     '                calibrationPeaks[row.Index].Channel = UserNumber.ParseInt(textvalue);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                calibrationPeaks[row.Index].Energy = double.Parse(textvalue);',
     '                calibrationPeaks[row.Index].Energy = UserNumber.ParseDouble(textvalue);'),
    ('BecquerelMonitor/DCFwhmCalibrationView.cs', 1,
     '                calibrationPeaks[row.Index].FWHM = double.Parse(textvalue);',
     '                calibrationPeaks[row.Index].FWHM = UserNumber.ParseDouble(textvalue);'),

    # ---------------- DCPeakDetectionView.cs ---------------------------
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     'using System.Collections.Generic;\nusing System.Linq;',
     'using System.Collections.Generic;\nusing System.Globalization;\nusing System.Linq;'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                            text2 = num.ToString("f2") + " (" + num2.ToString("f2") + "%)";',
     '                            text2 = num.ToString("f2", CultureInfo.InvariantCulture) + " (" + num2.ToString("f2", CultureInfo.InvariantCulture) + "%)";'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    row.Cells.Add(new Cell(peak.Energy.ToString("f2"), Math.Round(peak.Energy, 2)));',
     '                    row.Cells.Add(new Cell(peak.Energy.ToString("f2", CultureInfo.InvariantCulture), Math.Round(peak.Energy, 2)));'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    row.Cells.Add(new Cell(peak.Channel.ToString(), peak.Channel));',
     '                    row.Cells.Add(new Cell(peak.Channel.ToString(CultureInfo.InvariantCulture), peak.Channel));'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    row.Cells.Add(new Cell(snr.ToString(), snr));',
     '                    row.Cells.Add(new Cell(snr.ToString(CultureInfo.InvariantCulture), snr));'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    row.Cells.Add(new Cell(peak.FWHM.ToString("f0") + ", " + resolution.ToString("f1") + "% \u00b1" + peak.FWHM_DELTA.ToString("f1")));',
     '                    row.Cells.Add(new Cell(peak.FWHM.ToString("f0", CultureInfo.InvariantCulture) + ", " + resolution.ToString("f1", CultureInfo.InvariantCulture) + "% \u00b1" + peak.FWHM_DELTA.ToString("f1", CultureInfo.InvariantCulture)));'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    channel = Convert.ToInt32(row.Cells[3].Text);',
     '                    channel = Convert.ToInt32(row.Cells[3].Text, CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                        diff = Convert.ToDecimal(row.Cells[2].Text.Split(new string[] { " " }, StringSplitOptions.None)[0]);',
     '                        diff = Convert.ToDecimal(row.Cells[2].Text.Split(new string[] { " " }, StringSplitOptions.None)[0], CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    energy = Convert.ToDecimal(row.Cells[1].Text) - diff;',
     '                    energy = Convert.ToDecimal(row.Cells[1].Text, CultureInfo.InvariantCulture) - diff;'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '                    MessageBox.Show(String.Format(Resources.ERRAddCalibrationPoints, channel.ToString(), ex.Message), Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Error);',
     '                    MessageBox.Show(String.Format(Resources.ERRAddCalibrationPoints, channel.ToString(CultureInfo.InvariantCulture), ex.Message), Resources.ErrorExclamation, MessageBoxButtons.OK, MessageBoxIcon.Error);'),
    ('BecquerelMonitor/DCPeakDetectionView.cs', 1,
     '            decimal energy = Convert.ToDecimal(this.table1.SelectedItems[0].Cells[1].Text);',
     '            decimal energy = Convert.ToDecimal(this.table1.SelectedItems[0].Cells[1].Text, CultureInfo.InvariantCulture);'),

    # ---------------- DCResultView.cs ----------------------------------
    ('BecquerelMonitor/DCResultView.cs', 1,
     'using System;\nusing System.Threading;',
     'using System;\nusing System.Globalization;\nusing System.Threading;'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                this.columnModel1.Columns[2].Text = Resources.Uncertain + " " + errorLevel.ToString() + Resources.Sigma;',
     '                this.columnModel1.Columns[2].Text = Resources.Uncertain + " " + errorLevel.ToString(CultureInfo.InvariantCulture) + Resources.Sigma;'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                        Cell cell = new Cell(measurementResult.ResultValue.ToString(format), Math.Round(measurementResult.ResultValue, format_int));',
     '                        Cell cell = new Cell(measurementResult.ResultValue.ToString(format, CultureInfo.InvariantCulture), Math.Round(measurementResult.ResultValue, format_int));'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                            row.Cells.Add(new Cell(Resources.PlusMinus + num.ToString(format) + " (" + epsilon.ToString(format) + Resources.PercentCharacter + ")"));',
     '                            row.Cells.Add(new Cell(Resources.PlusMinus + num.ToString(format, CultureInfo.InvariantCulture) + " (" + epsilon.ToString(format, CultureInfo.InvariantCulture) + Resources.PercentCharacter + ")"));'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                            row.Cells.Add(new Cell(measurementResult.MDA.ToString(format), Math.Round(measurementResult.MDA, format_int)));',
     '                            row.Cells.Add(new Cell(measurementResult.MDA.ToString(format, CultureInfo.InvariantCulture), Math.Round(measurementResult.MDA, format_int)));'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                            row2.Cells[1].Text = measurementResult2.ResultValue.ToString(format);',
     '                            row2.Cells[1].Text = measurementResult2.ResultValue.ToString(format, CultureInfo.InvariantCulture);'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                            row2.Cells[2].Text = Resources.PlusMinus + num2.ToString(format) + " (" + epsilon.ToString(format) + Resources.PercentCharacter + ")";',
     '                            row2.Cells[2].Text = Resources.PlusMinus + num2.ToString(format, CultureInfo.InvariantCulture) + " (" + epsilon.ToString(format, CultureInfo.InvariantCulture) + Resources.PercentCharacter + ")";'),
    ('BecquerelMonitor/DCResultView.cs', 1,
     '                            row2.Cells[3].Text = measurementResult2.MDA.ToString(format);',
     '                            row2.Cells[3].Text = measurementResult2.MDA.ToString(format, CultureInfo.InvariantCulture);'),
]


def main():
    if len(sys.argv) > 1 and sys.argv[1] == '--revert':
        pairs = [(f, n, b, a) for (f, n, a, b) in EDITS]
        pairs.reverse()
    else:
        pairs = EDITS
    # ⛔ ДВА ПРОХОДА, и это не аккуратность ради аккуратности. Однопроходный
    #    скрипт, отказав на пятой правке, оставляет четыре применёнными — а
    #    повторный запуск по такому дереву уже НЕ РАВЕН первому: строка
    #    `using System.Drawing;` после вставки соседа встречается по-прежнему
    #    один раз, и `using` добавился бы вторым. Поймано на первом же прогоне.
    files = {}
    for path, times, old, new in pairs:
        full = os.path.join(ROOT, path)
        if full not in files:
            files[full] = io.open(full, encoding='utf-8-sig', newline='').read()
        src = files[full]
        # Файлы дерева — CRLF; образец пишется через `\n` и приводится здесь.
        if '\r\n' in src:
            old = old.replace('\n', '\r\n')
            new = new.replace('\n', '\r\n')
        got = src.count(old)
        if got != times:
            raise SystemExit('ОТКАЗ (ничего не записано): %s — строка встречается %d раз, ждали %d:\n%s'
                             % (path, got, times, old[:160]))
        files[full] = src.replace(old, new)
        print('%-46s x%d  %s' % (os.path.basename(path), times, old.strip()[:70]))

    total = sum(t for (_, t, _, _) in pairs)
    for full, text in files.items():
        io.open(full, 'w', encoding='utf-8-sig', newline='').write(text)
    print('файлов переписано: %d, замен строк: %d' % (len(files), total))


main()
