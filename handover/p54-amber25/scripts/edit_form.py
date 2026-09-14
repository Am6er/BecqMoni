# -*- coding: utf-8 -*-
import io
p = r'C:\Users\moroz\source\repos\BQ Eng res .NET 4.8\BecquerelMonitor\EfficiencyMakerForm.cs'
t = io.open(p, encoding='utf-8-sig', newline='').read()
lines = t.split('\n')


def idx(sub, start=0):
    for i in range(start, len(lines)):
        if sub in lines[i]:
            return i
    raise SystemExit('not found: ' + sub)


def summary_before(i):
    while '/// <summary>' not in lines[i]:
        i -= 1
    return i


a = summary_before(idx('void calculateButton_Click('))
b = idx('void Finish(EfficiencyFitResult result)')
print('replace', a + 1, '..', b, repr(lines[b - 1]))
new_block = r'''        /// <summary>
        /// Посчитать кривую из геометрии, лежащей в полях редактора. Спектры
        /// для этого не нужны вовсе. Повторное нажатие во время счёта — стоп.
        /// </summary>
        void calculateButton_Click(object sender, EventArgs e)
        {
            if (Busy())
            {
                this.cancelRequested = true;
                return;
            }

            // Геометрия берётся ИЗ ПОЛЕЙ редактора, а не из того, что когда-то
            // загрузили файлом. Иначе выбор готового детектора или правка руками
            // на расчёт не влияли: считалась бы прежняя геометрия, а результат
            // выглядел бы законным.
            if (!this.geometryPanel.TryCommit())
            {
                return;
            }

            this.geometry = this.geometryPanel.Model;
            if (this.geometry == null)
            {
                MessageBox.Show(this, Resources.EfficiencyMakerNoGeometry, this.Text,
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            GeometryModel model = this.geometry;
            EfficiencyCalculationOptions options = CurrentCalcOptions();
            Start(Resources.EfficiencyMakerCalculating,
                  (log, cancelled) => EfficiencyCalculation.Run(model, options, log, cancelled));
        }

        bool Busy()
        {
            return this.worker != null && this.worker.IsBusy;
        }

        /// <summary>
        /// Обвязка прогона: кнопка запуска становится «Стоп», счёт идёт в фоне,
        /// отмена опрашивается заданием. До 13.09.2026 обвязка была общей на два
        /// прогона (фит по спектрам и расчёт из геометрии); фит снят (`AMBER25`),
        /// кнопка осталась одна.
        /// </summary>
        void Start(string status, Func<Action<string>, Func<bool>, EfficiencyFitResult> job)
        {
            // Журнал НЕ чистится — ни здесь, ни где-либо ещё, пока окно не
            // закрыли: предупреждения разбора геометрии попадают в него при
            // импорте, задолго до прогона, и очистка перед расчётом стирала бы
            // то, к чему расчёт не относится.
            if (this.logTextBox.TextLength > 0)
            {
                AppendLog("");
            }

            this.cancelRequested = false;
            Button trigger = this.calculateButton;
            string caption = trigger.Text;
            trigger.Text = Resources.EfficiencyMakerStop;
            // Параметры запираются на прогон: они сняты в начале счёта, и
            // правка в полях по ходу дела относилась бы уже к следующему разу
            // — а выглядела бы как относящаяся к этому.
            this.calcOptionsGroup.Enabled = false;
            this.saveButton.Enabled = false;
            this.exportButton.Enabled = false;
            this.progressBar.Visible = true;
            this.statusLabel.Text = status;

            // Язык интерфейса выставлен только на потоке формы (MainForm), а
            // счёт идёт на потоке BackgroundWorker: без переноса культуры все
            // строки прогона — сводка разброса, предупреждения, ошибки — брались
            // бы из нейтрального ресурса вместо выбранного языка.
            //
            // ⚠ (`A244`) Культура СЧЁТА переносится уже НЕ ради чисел: расчёт
            // печатает их инвариантом сам, и подмена разделителя в `MainForm`
            // ему не нужна. Перенос оставлен до снятия самого костыля
            // `MainForm`, последней полосой `A244`.
            CultureInfo ui = CultureInfo.CurrentUICulture;
            CultureInfo formatting = CultureInfo.CurrentCulture;

            this.worker = new BackgroundWorker { WorkerReportsProgress = true };
            this.worker.DoWork += (s, args) =>
            {
                Thread.CurrentThread.CurrentUICulture = ui;
                Thread.CurrentThread.CurrentCulture = formatting;
                BackgroundWorker self = (BackgroundWorker)s;
                args.Result = job(message => self.ReportProgress(0, message),
                                  () => this.cancelRequested);
            };
            this.worker.ProgressChanged += (s, args) => AppendLog((string)args.UserState);
            this.worker.RunWorkerCompleted += (s, args) =>
            {
                // Окно могли закрыть, не дожидаясь конца прогона: обработчик
                // придёт всё равно, уже после Dispose, и обращение к любому
                // контролу свалило бы приложение (своего обработчика
                // необработанных исключений у него нет).
                if (this.IsDisposed || this.Disposing)
                {
                    return;
                }

                this.progressBar.Visible = false;
                trigger.Text = caption;
                this.calcOptionsGroup.Enabled = true;
                if (args.Error != null)
                {
                    this.statusLabel.Text = args.Error.Message;
                    AppendLog(args.Error.ToString());
                    // Кнопки сохранения гасились на время прогона; ошибка
                    // счёта не повод оставить несохранённую правку геометрии
                    // без кнопки «Сохранить».
                    this.UpdateSaveState();
                    return;
                }

                Finish((EfficiencyFitResult)args.Result);
            };
            this.worker.RunWorkerAsync();
        }
'''
new_lines = new_block.rstrip('\n').split('\n') + ['']
lines = lines[:a] + new_lines + lines[b:]

f = idx('void Finish(EfficiencyFitResult result)')
g = summary_before(idx('void saveButton_Click(object sender, EventArgs e)'))
print('Finish', f + 1, '..', g)
finish_block = r'''        void Finish(EfficiencyFitResult result)
        {
            this.lastResult = result;
            if (!string.IsNullOrEmpty(result.Error))
            {
                this.statusLabel.Text = result.Error;
                AppendLog(result.Error);
                this.graph.SetData(this.referenceCurve, null);
                // Кнопки сохранения гасились на время прогона — вернуть их
                // по фактическому состоянию, иначе правка геометрии остаётся
                // без «Сохранить» до первого удачного счёта.
                this.UpdateSaveState();
                return;
            }

            this.graph.SetData(this.referenceCurve, result);
            // Пересчитанная кривая — тоже правка: она ещё нигде не сохранена.
            if (result.Ok)
            {
                this.SetDirty();
            }

            this.UpdateSaveState();

            // Итог расчёта из геометрии: сколько точек, в каком диапазоне и
            // откуда уровень (он всегда абсолютный, из геометрии).
            this.statusLabel.Text = string.Format(CultureInfo.InvariantCulture,
                Resources.EfficiencyMakerCalcStatus, result.Curve.Count,
                (int)result.MinEnergy, (int)result.MaxEnergy,
                Resources.EfficiencyMakerLevelSimulation);

            AppendLog("");
            AppendLog(this.statusLabel.Text);
        }
'''
lines = lines[:f] + finish_block.rstrip('\n').split('\n') + [''] + lines[g:]
io.open(p, 'w', encoding='utf-8-sig', newline='').write('\n'.join(lines))
print('lines now', len(lines))
