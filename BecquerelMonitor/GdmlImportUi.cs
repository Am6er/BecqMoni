using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using BecquerelMonitor.EfficiencyMaker;

namespace BecquerelMonitor
{
    /// <summary>
    /// (`AMBER322`) Сопоставление «деталь файла → роль»: какая деталь кристалл, какая — вещество пробы, с какой детали
    /// начинается защита, и берутся ли образец и защита из файла. Деталь null — «как узнала программа»
    /// (у защиты — «найдено лучами»). Детектор берётся всегда (слово Amber 11.11.2026: «детектор должен быть всегда»).
    /// </summary>
    public sealed class GdmlChoice
    {
        public GdmlVolumeInstance Crystal, Sample, ShieldFrom;
        public bool TakeSample, TakeShield;

        public GdmlParts Parts
        {
            get { return GdmlParts.Detector | (this.TakeSample ? GdmlParts.Sample : 0) | (this.TakeShield ? GdmlParts.Shield : 0); }
        }

        public GdmlChoice Clone()
        {
            return (GdmlChoice)this.MemberwiseClone();
        }

        public bool Same(GdmlChoice other)
        {
            return other != null && ReferenceEquals(this.Crystal, other.Crystal) && this.TakeSample == other.TakeSample
                   && this.TakeShield == other.TakeShield
                   && (!this.TakeSample || ReferenceEquals(this.Sample, other.Sample))
                   && (!this.TakeShield || ReferenceEquals(this.ShieldFrom, other.ShieldFrom));
        }

        /// <summary>Раскладка по этому выбору.</summary>
        public GdmlFieldMap Build(GdmlGeometry geometry, GdmlOverview overview)
        {
            return GdmlFieldMap.Build(geometry, overview, this.Crystal, this.TakeSample ? this.Sample : null,
                                      this.TakeShield ? this.ShieldFrom : null);
        }

        /// <summary>То, что узнала программа: всё найденное берётся из файла.</summary>
        public static GdmlChoice Guess(GdmlFieldMap guessed)
        {
            return new GdmlChoice
            {
                Crystal = guessed.Crystal,
                Sample = (guessed.Found & GdmlParts.Sample) != 0 ? guessed.Sample : null,
                TakeSample = (guessed.Found & GdmlParts.Sample) != 0,
                TakeShield = (guessed.Found & GdmlParts.Shield) != 0,
            };
        }

        static GdmlVolumeInstance ByName(GdmlGeometry geometry, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            foreach (GdmlVolumeInstance volume in geometry.Volumes)
            {
                if (volume != null && string.Equals(volume.VolumeName, name, StringComparison.Ordinal))
                {
                    return volume;
                }
            }

            return null;
        }

        /// <summary>
        /// Выбор, записанный в конфигурации. Детали, названной в записи, в копии файла может не оказаться —
        /// её имя уходит в <paramref name="missing"/>, роль остаётся за узнанным программой.
        /// </summary>
        public static GdmlChoice FromInfo(GdmlImportInfo info, GdmlGeometry geometry, GdmlFieldMap guessed, List<string> missing)
        {
            GdmlChoice choice = Guess(guessed);
            choice.TakeSample = info.Sample;
            choice.TakeShield = info.Shield;
            foreach (KeyValuePair<string, Action<GdmlVolumeInstance>> role in new[]
            {
                new KeyValuePair<string, Action<GdmlVolumeInstance>>(info.CrystalPart, v => choice.Crystal = v),
                new KeyValuePair<string, Action<GdmlVolumeInstance>>(info.SamplePart, v => choice.Sample = v),
                new KeyValuePair<string, Action<GdmlVolumeInstance>>(info.ShieldPart, v => choice.ShieldFrom = v),
            })
            {
                if (string.IsNullOrEmpty(role.Key))
                {
                    continue;
                }

                GdmlVolumeInstance volume = ByName(geometry, role.Key);
                if (volume == null)
                {
                    missing.Add(role.Key);
                }
                else
                {
                    role.Value(volume);
                }
            }

            return choice;
        }

        /// <summary>Запись выбора для конфигурации: имена деталей пишутся только у выбранного человеком.</summary>
        public void ToInfo(GdmlImportInfo info, GdmlChoice guess)
        {
            info.Sample = this.TakeSample;
            info.Shield = this.TakeShield;
            info.CrystalPart = this.Crystal != null && !ReferenceEquals(this.Crystal, guess.Crystal) ? this.Crystal.VolumeName : "";
            info.SamplePart = this.TakeSample && this.Sample != null && !ReferenceEquals(this.Sample, guess.Sample) ? this.Sample.VolumeName : "";
            info.ShieldPart = this.TakeShield && this.ShieldFrom != null ? this.ShieldFrom.VolumeName : "";
        }
    }

    /// <summary>
    /// (`AMBER322`) Блок «Сопоставление»: три списка деталей файла — кристалл, вещество пробы, защита. Один элемент в
    /// двух местах (предложение дизайнера, принято 11.11.2026): в окне импорта и над сводкой на подвкладке «Файл GDML».
    /// Смена выбора ничего не пересчитывает — только сообщает (<see cref="ChoiceChanged"/>); поля меняет кнопка
    /// «Пересчитать» (решение Amber «Кнопкой «Пересчитать»») или «Импортировать» окна импорта.
    /// </summary>
    public sealed class GdmlMappingPanel : UserControl
    {
        sealed class Item
        {
            public GdmlVolumeInstance Volume;
            public string Text = "";
            public bool Bad;

            public override string ToString()
            {
                return this.Text;
            }
        }

        readonly ComboBox crystalCombo, sampleCombo, shieldCombo;
        readonly CheckBox sampleCheck, shieldCheck;
        readonly Label crystalHand, sampleHand, shieldHand, notice, auto;
        readonly Button applyButton;
        readonly LinkLabel resetLink;
        readonly ToolTip tips = new ToolTip();
        GdmlChoice guess = new GdmlChoice(), applied;
        bool filling;
        int crystalIndex;

        /// <summary>Выбор в списках сменился (поля не тронуты).</summary>
        public event EventHandler ChoiceChanged;

        /// <summary>Нажата «Пересчитать».</summary>
        public event EventHandler ApplyRequested;

        public GdmlMappingPanel(bool withActions)
        {
            this.Padding = new Padding(6, 4, 6, 4);
            int y = 4;
            Func<string, Control> head = text =>
            {
                Label label = new Label { AutoSize = true, Location = new Point(6, y), Text = text, Font = new Font(this.Font, FontStyle.Bold) };
                this.Controls.Add(label);
                y += 19;
                return label;
            };
            head(GdmlText.T("GdmlMapTitle", "Mapping"));
            Func<Label> hand = () =>
            {
                Label label = new Label
                {
                    AutoSize = true, ForeColor = Color.DimGray, Visible = false, Text = GdmlText.T("GdmlMapByHand", "chosen by hand"),
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                };
                this.Controls.Add(label);
                return label;
            };
            Func<ComboBox> combo = () =>
            {
                ComboBox box = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(6, y), Width = 100,
                    Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                };
                box.SelectedIndexChanged += this.Selected;
                this.Controls.Add(box);
                y += 27;
                return box;
            };

            this.Controls.Add(new Label { AutoSize = true, Location = new Point(6, y + 1), Text = GdmlText.T("GdmlMapCrystalRole", "Detector, crystal") });
            this.crystalHand = hand();
            this.crystalHand.Top = y + 1;
            y += 18;
            this.crystalCombo = combo();

            // (решение Amber 11.11.2026 «На «Файл GDML», в блоке «Сопоставление»») Место под поля детектора, которых
            // в файле нет (разрешение, сторона к пробе): их кладёт сюда редактор; в окне импорта место пустое.
            this.DetectorExtras = new Panel
            {
                Location = new Point(6, y), Height = 0, Width = 100,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            this.Controls.Add(this.DetectorExtras);
            int extrasAt = this.Controls.Count;

            this.sampleCheck = new CheckBox { AutoSize = true, Location = new Point(6, y), Text = GdmlText.T("GdmlMapSampleRole", "and sample, sample material") };
            this.sampleCheck.CheckedChanged += this.Selected;
            this.Controls.Add(this.sampleCheck);
            this.sampleHand = hand();
            this.sampleHand.Top = y + 2;
            y += 20;
            this.sampleCombo = combo();

            this.shieldCheck = new CheckBox { AutoSize = true, Location = new Point(6, y), Text = GdmlText.T("GdmlMapShieldRole", "and shield") };
            this.shieldCheck.CheckedChanged += this.Selected;
            this.Controls.Add(this.shieldCheck);
            this.shieldHand = hand();
            this.shieldHand.Top = y + 2;
            y += 20;
            this.shieldCombo = combo();

            this.auto = new Label
            {
                Location = new Point(6, y), Height = 30, Width = 100, ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = GdmlText.T("GdmlMapAuto", "The reflector, gap, housing and vessel walls are found by the program."),
            };
            this.tips.SetToolTip(this.auto, GdmlText.T("GdmlMapAutoTip", "By rays from the crystal and the sample outwards."));
            this.Controls.Add(this.auto);
            y += 32;

            this.notice = new Label
            {
                Location = new Point(6, y), Height = 30, Width = 100, ForeColor = Color.FromArgb(0xB0, 0x5A, 0x10), Visible = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            };
            this.Controls.Add(this.notice);

            if (withActions)
            {
                this.applyButton = new Button { Location = new Point(6, y), AutoSize = true, Text = GdmlText.T("GdmlMapApply", "Recalculate"), Enabled = false };
                this.applyButton.Click += (s, e) =>
                {
                    EventHandler handler = this.ApplyRequested;
                    if (handler != null)
                    {
                        handler(this, EventArgs.Empty);
                    }
                };
                this.Controls.Add(this.applyButton);
                this.resetLink = new LinkLabel { AutoSize = true, Location = new Point(this.applyButton.Right + 12, y + 5), Text = GdmlText.T("GdmlMapReset", "As guessed") };
                this.resetLink.LinkClicked += (s, e) => this.Show(this.guess, false);
                this.Controls.Add(this.resetLink);
                y += 30;
                this.notice.Top = y;
            }

            for (int i = extrasAt; i < this.Controls.Count; i++)
            {
                this.below.Add(new KeyValuePair<Control, int>(this.Controls[i], this.Controls[i].Top));
            }

            this.plainHeight = y + 4;
            this.Height = this.plainHeight;
            this.hands = new[] { this.crystalHand, this.sampleHand, this.shieldHand };
            this.Layout += (s, e) => this.PlaceHands();
        }

        readonly Label[] hands;

        /// <summary>Место под строкой «Детектор, кристалл» для полей детектора, которых в файле нет.</summary>
        public readonly Panel DetectorExtras;

        readonly List<KeyValuePair<Control, int>> below = new List<KeyValuePair<Control, int>>();
        int extrasHeight;

        /// <summary>Высота места под поля детектора: всё, что ниже, сдвигается.</summary>
        public void SetExtrasHeight(int height)
        {
            this.extrasHeight = Math.Max(0, height);
            this.DetectorExtras.Height = this.extrasHeight;
            foreach (KeyValuePair<Control, int> item in this.below)
            {
                item.Key.Top = item.Value + this.extrasHeight;
            }

            this.Height = this.plainHeight + this.extrasHeight + (this.notice.Visible ? this.notice.Height + 4 : 0);
        }

        /// <summary>Высота блока без строки отказа; со строкой — на её высоту больше.</summary>
        int plainHeight;

        void PlaceHands()
        {
            foreach (Label label in this.hands)
            {
                label.Left = Math.Max(120, this.ClientSize.Width - label.Width - 6);
            }
        }

        static string ItemText(GdmlVolumeInstance volume)
        {
            string material = volume.MaterialName ?? "";
            return string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapItem", "{0} — {1}, {2} g"), volume.VolumeName,
                material.StartsWith("G4_", StringComparison.Ordinal) ? material.Substring(3) : material,
                double.IsNaN(volume.MassG) ? "?" : GdmlSketch.Fmt(volume.MassG));
        }

        /// <summary>Заполнить списки деталями файла; выбранным стоит узнанное программой.</summary>
        public void SetFile(GdmlGeometry geometry, GdmlOverview overview, GdmlFieldMap guessed)
        {
            this.filling = true;
            try
            {
                this.guess = GdmlChoice.Guess(guessed);
                List<GdmlVolumeInstance> parts = new List<GdmlVolumeInstance>();
                foreach (GdmlVolumeInstance volume in geometry.Volumes)
                {
                    if (volume != null && volume.Solid != null && volume.BoundsKnown && !overview.IsOutOfFrame(volume)
                        && !(volume.Material != null && volume.Material.HasDensity && volume.Material.Density < 0.01))
                    {
                        parts.Add(volume);
                    }
                }

                parts.Sort((a, b) => (double.IsNaN(b.MassG) ? -1.0 : b.MassG).CompareTo(double.IsNaN(a.MassG) ? -1.0 : a.MassG));
                this.crystalCombo.Items.Clear();
                this.sampleCombo.Items.Clear();
                this.shieldCombo.Items.Clear();
                if (this.guess.Crystal == null)
                {
                    this.crystalCombo.Items.Add(new Item { Text = GdmlText.T("GdmlMapUnset", "(not chosen)") });
                }

                this.shieldCombo.Items.Add(new Item
                {
                    Text = (guessed.Found & GdmlParts.Shield) != 0
                        ? string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlMapShieldRays", "found by rays: {0} ({1} parts)"), guessed.ShieldMaterials, guessed.ShieldParts)
                        : GdmlText.T("GdmlMapShieldRaysNone", "by rays: nothing found around the scene"),
                });
                this.sampleCombo.Items.Add(new Item { Text = GdmlText.T("GdmlMapUnset", "(not chosen)") });
                foreach (GdmlVolumeInstance volume in parts)
                {
                    bool bad = !GdmlFieldMap.CrystalShapeFits(volume);
                    string text = ItemText(volume);
                    string guessedTail = ", " + GdmlText.T("GdmlMapGuessed", "guessed");
                    this.crystalCombo.Items.Add(new Item
                    {
                        Volume = volume, Bad = bad,
                        Text = text + (bad ? ", " + GdmlText.T("GdmlMapBadShape", "shape does not fit") : ReferenceEquals(volume, this.guess.Crystal) ? guessedTail : ""),
                    });
                    this.sampleCombo.Items.Add(new Item { Volume = volume, Text = text + (ReferenceEquals(volume, this.guess.Sample) ? guessedTail : "") });
                    this.shieldCombo.Items.Add(new Item { Volume = volume, Text = text });
                }

                foreach (ComboBox box in new[] { this.crystalCombo, this.sampleCombo, this.shieldCombo })
                {
                    int widest = box.Width;
                    foreach (Item item in box.Items)
                    {
                        widest = Math.Max(widest, TextRenderer.MeasureText(item.Text, box.Font).Width + 24);
                    }

                    box.DropDownWidth = Math.Min(600, widest);
                }
            }
            finally
            {
                this.filling = false;
            }

            this.Show(this.guess, true);
        }

        static void Pick(ComboBox box, GdmlVolumeInstance volume)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (ReferenceEquals(((Item)box.Items[i]).Volume, volume))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            box.SelectedIndex = box.Items.Count > 0 ? 0 : -1;
        }

        /// <summary>Показать выбор в списках. <paramref name="asApplied"/> — это и есть применённое к полям.</summary>
        public void Show(GdmlChoice choice, bool asApplied)
        {
            this.filling = true;
            try
            {
                Pick(this.crystalCombo, choice.Crystal);
                this.crystalIndex = this.crystalCombo.SelectedIndex;
                this.sampleCheck.Checked = choice.TakeSample;
                Pick(this.sampleCombo, choice.TakeSample ? choice.Sample : null);
                this.shieldCheck.Checked = choice.TakeShield;
                Pick(this.shieldCombo, choice.TakeShield ? choice.ShieldFrom : null);
            }
            finally
            {
                this.filling = false;
            }

            if (asApplied)
            {
                this.applied = choice.Clone();
                this.SetNotice(null);
            }

            this.Refresh(!asApplied);
        }

        public GdmlChoice Guessed
        {
            get { return this.guess; }
        }

        static GdmlVolumeInstance VolumeOf(ComboBox box)
        {
            Item item = box.SelectedItem as Item;
            return item == null ? null : item.Volume;
        }

        /// <summary>Что сейчас выбрано в списках.</summary>
        public GdmlChoice Choice
        {
            get
            {
                return new GdmlChoice
                {
                    Crystal = VolumeOf(this.crystalCombo),
                    TakeSample = this.sampleCheck.Checked && VolumeOf(this.sampleCombo) != null,
                    Sample = this.sampleCheck.Checked ? VolumeOf(this.sampleCombo) : null,
                    TakeShield = this.shieldCheck.Checked,
                    ShieldFrom = this.shieldCheck.Checked ? VolumeOf(this.shieldCombo) : null,
                };
            }
        }

        /// <summary>Строка под списками: почему выбор не применён. null — убрать.</summary>
        public void SetNotice(string text)
        {
            this.notice.Text = text ?? "";
            this.notice.Visible = !string.IsNullOrEmpty(text);
            this.Height = this.plainHeight + this.extrasHeight + (this.notice.Visible ? this.notice.Height + 4 : 0);
            this.tips.SetToolTip(this.notice, string.IsNullOrEmpty(text) ? null : AppUi.WrapWords(text, 80));
        }

        void Selected(object sender, EventArgs e)
        {
            if (this.filling)
            {
                return;
            }

            // Деталь неподходящей формы кристаллом не выбирается: список возвращается к прежней строке.
            Item crystal = this.crystalCombo.SelectedItem as Item;
            if (ReferenceEquals(sender, this.crystalCombo) && crystal != null && crystal.Bad)
            {
                this.filling = true;
                this.crystalCombo.SelectedIndex = this.crystalIndex;
                this.filling = false;
                this.SetNotice(string.Format(CultureInfo.InvariantCulture,
                    GdmlText.T("GdmlMapRefusedShape", "{0} does not fit as the crystal: it is neither a cylinder nor a box."), crystal.Volume.VolumeName));
                return;
            }

            this.crystalIndex = this.crystalCombo.SelectedIndex;
            this.SetNotice(null);
            this.Refresh(true);
        }

        void Refresh(bool raise)
        {
            this.sampleCombo.Enabled = this.sampleCheck.Checked;
            this.shieldCombo.Enabled = this.shieldCheck.Checked;
            GdmlChoice now = this.Choice;
            this.crystalHand.Visible = now.Crystal != null && !ReferenceEquals(now.Crystal, this.guess.Crystal);
            this.sampleHand.Visible = now.TakeSample != this.guess.TakeSample || (now.TakeSample && !ReferenceEquals(now.Sample, this.guess.Sample));
            this.shieldHand.Visible = now.TakeShield != this.guess.TakeShield || now.ShieldFrom != null;
            foreach (ComboBox box in new[] { this.crystalCombo, this.sampleCombo, this.shieldCombo })
            {
                this.tips.SetToolTip(box, box.SelectedItem == null ? null : AppUi.WrapWords(box.SelectedItem.ToString(), 80));
            }

            if (this.applyButton != null)
            {
                this.applyButton.Enabled = this.applied == null || !now.Same(this.applied);
            }

            this.PlaceHands();
            if (raise)
            {
                EventHandler handler = this.ChoiceChanged;
                if (handler != null)
                {
                    handler(this, EventArgs.Empty);
                }
            }
        }

        /// <summary>Сопоставление отличается от узнанного программой.</summary>
        public bool ByHand(GdmlChoice choice)
        {
            return choice != null && !choice.Same(this.guess);
        }
    }

    /// <summary>
    /// (`AMBER322`) Окно импорта GDML: «что содержится в файле» — блок сопоставления и маленький чертёж, на котором
    /// выбранные детали в полном цвете, остальные бледные (решения Amber 11.11.2026: окно с галками «детектор»,
    /// «и образец», «и защита»; списки деталей; «Маленький чертёж с подсветкой»).
    /// </summary>
    public sealed class GdmlImportDialog : Form
    {
        readonly GdmlGeometry geometry;
        readonly GdmlOverview overview;
        readonly GdmlMappingPanel mapping;
        readonly GdmlSketch sketch;
        readonly Label result;
        readonly Button ok;

        /// <summary>Раскладка по выбранному; годна, когда детектор разложен.</summary>
        public GdmlFieldMap Map { get; private set; }

        public GdmlChoice Choice { get; private set; }

        public GdmlImportDialog(GdmlGeometry geometry, GdmlOverview overview, GdmlFieldMap guessed)
        {
            this.geometry = geometry;
            this.overview = overview;
            this.Text = string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlDialogTitle", "GDML import: {0}"), System.IO.Path.GetFileName(geometry.FileName ?? ""));
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.ClientSize = new Size(860, 470);
            this.MinimumSize = new Size(700, 440);

            Label question = new Label
            {
                Dock = DockStyle.Top, Height = 26, Padding = new Padding(8, 7, 8, 0),
                Text = GdmlText.T("GdmlDialogQuestion", "What does the file contain? The parts taken from the file are read-only; the rest is set by hand."),
            };
            Panel bottom = new Panel { Dock = DockStyle.Bottom, Height = 74 };
            this.result = new Label { Location = new Point(8, 4), Size = new Size(840, 32), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
            this.ok = new Button { Text = GdmlText.T("GdmlDialogImport", "Import"), Size = new Size(120, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.OK };
            Button cancel = new Button { Text = GdmlText.T("GdmlDialogCancel", "Cancel"), Size = new Size(90, 26), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };
            this.ok.Location = new Point(bottom.Width - 226, 40);
            cancel.Location = new Point(bottom.Width - 98, 40);
            bottom.Controls.Add(this.result);
            bottom.Controls.Add(this.ok);
            bottom.Controls.Add(cancel);
            this.AcceptButton = this.ok;
            this.CancelButton = cancel;

            this.mapping = new GdmlMappingPanel(false) { Dock = DockStyle.Left, Width = 400 };
            // (`AMBER301`) Маленький чертёж — тот же рисунок без обвязки: размеры, пояснения и граммы стоят в списках слева.
            this.sketch = new GdmlSketch { Dock = DockStyle.Fill, Compact = true };
            this.Controls.Add(this.sketch);
            this.Controls.Add(this.mapping);
            this.Controls.Add(bottom);
            this.Controls.Add(question);

            this.sketch.SetGeometry(geometry);
            this.mapping.SetFile(geometry, overview, guessed);
            this.mapping.ChoiceChanged += (s, e) => this.Rebuild(null);
            this.Rebuild(guessed);
        }

        void Rebuild(GdmlFieldMap ready)
        {
            GdmlChoice choice = this.mapping.Choice;
            GdmlFieldMap map = ready ?? choice.Build(this.geometry, this.overview);
            this.Map = map;
            this.Choice = choice;
            bool good = (map.Found & GdmlParts.Detector) != 0;
            GdmlParts parts = choice.Parts & map.Found;
            string text = GdmlOverview.TakenText(map, good ? parts : GdmlParts.None);
            if (good && choice.TakeSample && (map.Found & GdmlParts.Sample) == 0)
            {
                text += " " + string.Format(CultureInfo.InvariantCulture, GdmlText.T("GdmlSourceNote", "The sample is not transferred: {0}."),
                    map.SampleProblem ?? GdmlText.T("GdmlWhyOffAxis", "it is not on the crystal axis"));
            }

            if (good && choice.TakeShield && (map.Found & GdmlParts.Shield) == 0)
            {
                text += " " + GdmlText.T("GdmlShieldNote", "No shield is found around the scene.");
            }

            this.result.Text = text;
            this.ok.Enabled = good;

            // Подсветка: выбранное и всё, что по нему легло в поля, — в полном цвете; остальное бледное.
            this.sketch.SetPale(volume => !(good ? map.IsUsed(volume, parts) : ReferenceEquals(volume, choice.Crystal)));
            this.sketch.SetMarks(GdmlSketchMarks.From(this.geometry, map, good ? parts : GdmlParts.None, false));
        }

        /// <summary>Выбрать в списках программно (проба): как человек, с пересчётом итога.</summary>
        public void Choose(GdmlChoice choice)
        {
            this.mapping.Show(choice, false);
            this.Rebuild(null);
        }
    }
}
