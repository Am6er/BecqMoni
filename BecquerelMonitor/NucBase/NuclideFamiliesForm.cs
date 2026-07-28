using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using BecquerelMonitor.Properties;

namespace BecquerelMonitor.NucBase
{
    // Редактор классификации нуклида по семействам. Живёт в NucBase, потому что это
    // штатное окно приложения к nucdb.sqlite: раз классификация лежит в базе, править
    // её должен тот же интерфейс, а не внешний скрипт — иначе получаются два пути
    // записи и две правды о том, что в базе.
    //
    // Разметка собирается кодом: контролов немного, а число семейств задаётся
    // таблицей families и меняться может без пересборки формы.
    public class NuclideFamiliesForm : Form
    {
        readonly string nucid;
        readonly ToolTip familyTips = new ToolTip { AutoPopDelay = 20000 };
        readonly List<CheckBox> boxes = new List<CheckBox>();

        public NuclideFamiliesForm(string displayName, string nucid)
        {
            this.nucid = nucid;

            this.Text = string.Format(CultureInfo.CurrentCulture,
                Resources.NuclideFamiliesTitle, displayName);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.AutoScaleMode = AutoScaleMode.Font;

            Label hint = new Label
            {
                Text = Resources.NuclideFamiliesHint,
                Location = new Point(12, 9),
                Size = new Size(420, 32),
                ForeColor = SystemColors.GrayText
            };
            this.Controls.Add(hint);

            List<NuclideFamily> families = NuclideFamilies.All();
            HashSet<string> assigned = NuclideFamilies.Of(nucid);

            int top = hint.Bottom + 8;
            foreach (NuclideFamily family in families)
            {
                CheckBox box = new CheckBox
                {
                    Text = family.LocalizedTitle,
                    Tag = family.Code,
                    Checked = assigned.Contains(family.Code),
                    Location = new Point(16, top),
                    AutoSize = true
                };
                // пояснение — подсказкой, чтобы окно не разрасталось.
                // ToolTip общий на всю форму и лежит в components: отдельный
                // экземпляр на каждый чекбокс никем не освобождался, а ToolTip —
                // это окно и таймер, то есть утечка хендлов на каждое открытие
                // диалога.
                if (!string.IsNullOrEmpty(family.LocalizedInfo))
                {
                    this.familyTips.SetToolTip(box, family.LocalizedInfo);
                }
                this.Controls.Add(box);
                this.boxes.Add(box);
                top = box.Bottom + 6;
            }

            Button ok = new Button
            {
                Text = Resources.ButtonOK,
                DialogResult = DialogResult.OK,
                Location = new Point(236, top + 10),
                Size = new Size(90, 26)
            };
            ok.Click += this.OnSave;
            Button cancel = new Button
            {
                Text = Resources.ButtonCancel,
                DialogResult = DialogResult.Cancel,
                Location = new Point(336, top + 10),
                Size = new Size(90, 26)
            };
            this.Controls.Add(ok);
            this.Controls.Add(cancel);
            this.AcceptButton = ok;
            this.CancelButton = cancel;
            this.ClientSize = new Size(440, cancel.Bottom + 12);
        }

        void OnSave(object sender, EventArgs e)
        {
            List<string> codes = new List<string>();
            foreach (CheckBox box in this.boxes)
            {
                if (box.Checked)
                {
                    codes.Add((string)box.Tag);
                }
            }
            // Классификация стабильного нуклида уходила в базу молча и навсегда
            // невидимой: каталог мастера берёт только нуклиды с известным
            // периодом полураспада, и это верно — у стабильного нет линий, из
            // которых строить ROI. Сохранить не запрещаем (база — не только для
            // мастера), но предупреждаем, что в каталоге записи не будет.
            if (codes.Count > 0 && !NuclideFamilies.IsVisibleInCatalog(this.nucid))
            {
                if (MessageBox.Show(this,
                        string.Format(CultureInfo.CurrentCulture,
                            Resources.NuclideFamiliesNotInCatalog, this.nucid),
                        Resources.Warning, MessageBoxButtons.OKCancel,
                        MessageBoxIcon.Warning) != DialogResult.OK)
                {
                    this.DialogResult = DialogResult.None;
                    return;
                }
            }
            try
            {
                NuclideFamilies.Set(this.nucid, codes);
            }
            catch (Exception ex)
            {
                // Запись в базу — единственное место модуля, где приложение её меняет.
                // Ошибку показываем и оставляем окно открытым: молча потерять правку хуже.
                MessageBox.Show(this,
                    string.Format(CultureInfo.CurrentCulture,
                        Resources.NuclideFamiliesSaveFailed, ex.Message),
                    Resources.ErrorDialogTitle, MessageBoxButtons.OK, MessageBoxIcon.Hand);
                this.DialogResult = DialogResult.None;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.familyTips.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
