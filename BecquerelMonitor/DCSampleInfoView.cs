using BecquerelMonitor.Properties;
using System;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    // Token: 0x020000C0 RID: 192
    public partial class DCSampleInfoView : ToolWindow
    {
        // Token: 0x06000941 RID: 2369 RVA: 0x00036158 File Offset: 0x00034358
        public DCSampleInfoView(MainForm mainForm)
        {
            this.mainForm = mainForm;
            this.InitializeComponent();
        }

        // Token: 0x06000942 RID: 2370 RVA: 0x00036178 File Offset: 0x00034378
        public void LoadFormContents()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            this.contentsLoading = true;
            ResultData activeResultData = activeDocument.ActiveResultData;
            SampleInfoData sampleInfo = activeResultData.SampleInfo;
            this.textBoxName.Text = sampleInfo.Name;
            this.textBoxLocation.Text = sampleInfo.Location;
            this.dateTimePickerSampleTime.Value = sampleInfo.Time;
            this.dateTimePicker2.Value = activeResultData.StartTime;
            // ⛔ `A1`. НОЛЬ — ЭТО «НЕ УКАЗАНО», А НЕ НЕДОПУСТИМОЕ ЗНАЧЕНИЕ.
            //
            // Прежде здесь стоял минимум 0.001, и присваивание `Value` при
            // нулевой массе бросало `ArgumentOutOfRangeException`: человек
            // получал окно «Необработанное исключение» ВМЕСТО СПЕКТРА. Файлов
            // с нулевой массой в одном только корпусе пятнадцать, а панель
            // могла быть при этом даже не видна — `LoadFormContents()` зовут
            // при всякой смене документа и спектра (`MainForm.cs`), и ни в
            // одном месте нет `try`/`catch`.
            //
            // Что ноль означает «не указано», счётная половина программы уже
            // знает: `MeasurementResultManager.cs:79-88` при `Weight <= 0`
            // честно ставит `Resources.ResultNoWeight`. Теперь это знает и
            // панель ввода.
            //
            // ⚠ Ветви ОБЪЁМА несли опечатку-близнец: обе правили
            // `numericUpDownWeight.Minimum` вместо `numericUpDownVolume` — и
            // ТОЛЬКО поэтому нулевой объём не ронял приложение (у своего поля
            // минимум так и оставался нулём от конструктора). Опечатка
            // исправлена, но чинить её в отдельности было НЕЛЬЗЯ: сама по
            // себе она внесла бы падение и на объёме.
            // ⚠ `A10`. Обозначения единиц берутся ИЗ РЕСУРСОВ. Прежде здесь
            // стояли английские литералы «kg»/«g»/«l»/«ml», и они писались
            // ПОВЕРХ перевода: в паре `DCSampleInfoView.ru.resx` лежат «кг» и
            // «л», конструктор их ставил, а этот метод затирал при первой же
            // смене документа — в русском окне рядом с русскими подписями
            // стояли английские единицы. Ключей заведено четыре, а не два:
            // «г» и «мл» в паре не было вовсе.
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                this.label5.Text = Resources.UnitKilogram;
                this.numericUpDownWeight.Minimum = 0m;
                this.numericUpDownWeight.Maximum = 100m;
                this.numericUpDownWeight.Increment = 0.1m;
                this.weightOutOfRange = SetValueSafely(this.numericUpDownWeight, (decimal)sampleInfo.Weight);
            }
            else
            {
                this.label5.Text = Resources.UnitGram;
                this.numericUpDownWeight.Minimum = 0m;
                this.numericUpDownWeight.Maximum = 100000m;
                this.numericUpDownWeight.Increment = 100m;
                this.weightOutOfRange = SetValueSafely(this.numericUpDownWeight, (decimal)sampleInfo.Weight * 1000m);
            }
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                this.label6.Text = Resources.UnitLiter;
                this.numericUpDownVolume.Minimum = 0m;
                this.numericUpDownVolume.Maximum = 100m;
                this.numericUpDownVolume.Increment = 0.1m;
                this.volumeOutOfRange = SetValueSafely(this.numericUpDownVolume, (decimal)sampleInfo.Volume);
            }
            else
            {
                this.label6.Text = Resources.UnitMilliliter;
                this.numericUpDownVolume.Minimum = 0m;
                this.numericUpDownVolume.Maximum = 100000m;
                this.numericUpDownVolume.Increment = 100m;
                this.volumeOutOfRange = SetValueSafely(this.numericUpDownVolume, (decimal)sampleInfo.Volume * 1000m);
            }
            this.textBoxNote.Text = sampleInfo.Note;
            this.contentsLoading = false;
        }

        /// <summary>
        /// Положить число в поле, не дав ему бросить исключение, и вернуть
        /// ИСХОДНОЕ значение, если оно в поле не помещалось (иначе — null).
        /// </summary>
        /// <remarks>
        /// ⛔ `A1`. Падало приложение на нулевой массе, но дверь та же и для
        /// слишком большой: `NumericUpDown.Value` бросает
        /// `ArgumentOutOfRangeException` на всём, что вне
        /// [<c>Minimum</c>; <c>Maximum</c>], а масса и объём приходят из файла
        /// числами с плавающей точкой и ничем не ограничены. Проба на 200 кг
        /// уронила бы программу так же, как проба без массы.
        ///
        /// Возвращаемое значение — не украшение: обрезка меняет ПОКАЗАННОЕ
        /// число, и <see cref="SaveFormContents"/> иначе записал бы обрезок
        /// поверх настоящего. Пока человек поле не тронул, настоящее число
        /// хранится и уходит на диск целым.
        /// </remarks>
        static decimal? SetValueSafely(NumericUpDown box, decimal value)
        {
            if (value < box.Minimum)
            {
                box.Value = box.Minimum;
                return value;
            }
            if (value > box.Maximum)
            {
                box.Value = box.Maximum;
                return value;
            }
            box.Value = value;
            return null;
        }

        // Token: 0x06000943 RID: 2371 RVA: 0x000363D8 File Offset: 0x000345D8
        public void SaveFormContents()
        {
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Name = this.textBoxName.Text;
            activeResultData.SampleInfo.Location = this.textBoxLocation.Text;
            activeResultData.SampleInfo.Time = this.dateTimePickerSampleTime.Value;
            // ⛔ `A1`. Число, не поместившееся в поле, ПОКАЗАНО обрезанным, но
            // записывать обрезок нельзя: человек его не вводил. Пока поле не
            // тронуто, на диск уходит настоящее значение из файла; тронет —
            // обработчик `numericUpDownWeight_ValueChanged` снимет отметку, и
            // запишется введённое.
            decimal weightValue = this.weightOutOfRange ?? this.numericUpDownWeight.Value;
            decimal volumeValue = this.volumeOutOfRange ?? this.numericUpDownVolume.Value;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                activeResultData.SampleInfo.Weight = (double)weightValue;
            }
            else
            {
                activeResultData.SampleInfo.Weight = (double)(weightValue / 1000m);
            }
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                activeResultData.SampleInfo.Volume = (double)volumeValue;
            }
            else
            {
                activeResultData.SampleInfo.Volume = (double)(volumeValue / 1000m);
            }
            activeResultData.SampleInfo.Note = this.textBoxNote.Text;
        }

        // Token: 0x06000944 RID: 2372 RVA: 0x0003652C File Offset: 0x0003472C
        void SetActiveDocumentDirty()
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            activeDocument.Dirty = true;
            activeDocument.ActiveResultData.Dirty = true;
        }

        // Token: 0x06000948 RID: 2376 RVA: 0x00036588 File Offset: 0x00034788
        void textBoxNote_TextChanged(object sender, EventArgs e)
        {
            this.UpdateNoteValue();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x06000949 RID: 2377 RVA: 0x00036590 File Offset: 0x00034790
        void textBoxName_Changed(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Name = this.textBoxName.Text;

            this.SetActiveDocumentDirty();
            this.mainForm.UpdateSpectrumListView();
        }

        // Token: 0x0600094A RID: 2378 RVA: 0x000365E8 File Offset: 0x000347E8
        void textBoxLocation_Changed(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Location = this.textBoxLocation.Text;

            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094C RID: 2380 RVA: 0x0003668C File Offset: 0x0003488C
        void dateTimePickerSampleTime_ValueChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Time = this.dateTimePickerSampleTime.Value;

            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094D RID: 2381 RVA: 0x00036690 File Offset: 0x00034890
        void numericUpDownWeight_ValueChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }

            // Человек тронул поле — показанное и есть введённое,
            // прятать за ним прежнее число больше нечего (`A1`).
            this.weightOutOfRange = null;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.WeightUnit == WeightUnit.Kilogram)
            {
                activeResultData.SampleInfo.Weight = (double)this.numericUpDownWeight.Value;
            }
            else
            {
                activeResultData.SampleInfo.Weight = (double)(this.numericUpDownWeight.Value / 1000m);
            }

            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        // Token: 0x0600094E RID: 2382 RVA: 0x0003673C File Offset: 0x0003493C
        void numericUpDownVolume_ValueChanged(object sender, EventArgs e)
        {
            if (this.contentsLoading)
            {
                return;
            }

            // Человек тронул поле — показанное и есть введённое,
            // прятать за ним прежнее число больше нечего (`A1`).
            this.volumeOutOfRange = null;
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            if (this.globalConfigManager.GlobalConfig.MeasurementConfig.VolumeUnit == VolumeUnit.Liter)
            {
                activeResultData.SampleInfo.Volume = (double)this.numericUpDownVolume.Value;
            }
            else
            {
                activeResultData.SampleInfo.Volume = (double)(this.numericUpDownVolume.Value / 1000m);
            }

            this.UpdateMeasurementResult();
            this.SetActiveDocumentDirty();
        }

        void UpdateNoteValue()
        {
            if (this.contentsLoading)
            {
                return;
            }
            DocEnergySpectrum activeDocument = this.mainForm.ActiveDocument;
            if (activeDocument == null)
            {
                return;
            }
            ResultData activeResultData = activeDocument.ActiveResultData;
            activeResultData.SampleInfo.Note = this.textBoxNote.Text;
        }

        // Token: 0x06000951 RID: 2385 RVA: 0x00036840 File Offset: 0x00034A40
        void UpdateMeasurementResult()
        {
            this.mainForm.ShowMeasurementResult(false);
        }

        // Token: 0x0400052A RID: 1322
        GlobalConfigManager globalConfigManager = GlobalConfigManager.GetInstance();

        // Token: 0x0400052B RID: 1323
        MainForm mainForm;

        // Token: 0x0400052C RID: 1324
        bool contentsLoading;

        /// <summary>
        /// Настоящая масса из файла, если она не поместилась в поле ввода и
        /// показана обрезанной; null — показано ровно то, что в файле.
        /// Снимается, как только человек поле правит. См. `A1`.
        /// </summary>
        decimal? weightOutOfRange;

        /// <summary>Настоящий объём из файла — см. <see cref="weightOutOfRange"/>.</summary>
        decimal? volumeOutOfRange;
    }
}
