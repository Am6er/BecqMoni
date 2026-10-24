// "Refresh" found several AtomSpectra boards (or one, and another one without its address): the person chooses one
// (address, serial number). Texts: Properties.Resources (English, Russian).
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace BecquerelMonitor
{
    public static class AtomSpectraBoardChooser
    {
        public static AtomSpectraBoardEntry Choose(IWin32Window owner, IList<AtomSpectraBoardEntry> boards)
        {
            if (boards == null || boards.Count == 0)
            {
                return null;
            }

            ListView listView;
            using (Form form = Build(boards, out listView))
            {
                DialogResult res = owner != null ? form.ShowDialog(owner) : form.ShowDialog();

                if (res == DialogResult.OK && listView.SelectedItems.Count > 0)
                {
                    return (AtomSpectraBoardEntry)listView.SelectedItems[0].Tag;
                }

                return null;
            }
        }

        // the window is built here (a test builds it without showing); the list MUST be in form.Controls.
        internal static Form Build(IList<AtomSpectraBoardEntry> boards, out ListView listView)
        {
            Form form = new Form();
            form.Text = Resources.AtomSpectraBoardChoiceTitle;
            form.FormBorderStyle = FormBorderStyle.FixedDialog;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.CenterParent;
            form.AutoScaleMode = AutoScaleMode.Font;
            form.ClientSize = new Size(560, 300);
            form.Font = SystemFonts.MessageBoxFont;

            // boards that answered without an address are not in the list: the prompt says so (one more line, the list is shorter)
            AtomSpectraBoardList found = boards as AtomSpectraBoardList;
            int incomplete = found != null ? found.Incomplete : 0;
            int extra = incomplete > 0 ? 34 : 0;

            Label promptLabel = new Label();
            promptLabel.Location = new Point(12, 12);
            promptLabel.AutoSize = false;
            promptLabel.Size = new Size(536, 36 + extra);
            promptLabel.Text = Resources.AtomSpectraBoardChoicePrompt;
            if (incomplete > 0)
            {
                promptLabel.Text += Environment.NewLine + string.Format(CultureInfo.InvariantCulture, Resources.AtomSpectraBoardChoiceIncomplete, incomplete);
            }
            form.Controls.Add(promptLabel);

            ListView list = new ListView();
            list.Location = new Point(12, 52 + extra);
            list.Size = new Size(536, 196 - extra);
            list.TabIndex = 0;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.MultiSelect = false;
            list.HideSelection = false;
            list.GridLines = true;
            list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            list.Columns.Add(Resources.AtomSpectraBoardChoiceAddress, 120);
            list.Columns.Add(Resources.AtomSpectraBoardChoiceSerial, 120);
            list.Columns.Add(Resources.AtomSpectraBoardChoiceHost, 160);
            list.Columns.Add(Resources.AtomSpectraBoardChoiceService, 110);

            foreach (AtomSpectraBoardEntry entry in boards)
            {
                string service = entry.Source == "atomspectra" ? "_atomspectra._tcp" : "_http._tcp";
                ListViewItem item = new ListViewItem(entry.Address);
                item.SubItems.Add(entry.Serial ?? "?");
                item.SubItems.Add(entry.HostName);
                item.SubItems.Add(service);
                item.Tag = entry;
                list.Items.Add(item);
            }

            if (list.Items.Count > 0)
            {
                list.Items[0].Selected = true;
                list.Items[0].Focused = true;
            }

            Button okButton = new Button();
            okButton.Text = Resources.AtomSpectraBoardChoiceOk;
            okButton.DialogResult = DialogResult.OK;
            okButton.TabIndex = 1;
            okButton.Size = new Size(90, 28);
            okButton.Location = new Point(362, 258);
            okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            okButton.Enabled = list.SelectedItems.Count > 0;

            Button cancelButton = new Button();
            cancelButton.Text = Resources.AtomSpectraBoardChoiceCancel;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.TabIndex = 2;
            cancelButton.Size = new Size(90, 28);
            cancelButton.Location = new Point(458, 258);
            cancelButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;

            form.Controls.Add(okButton);
            form.Controls.Add(cancelButton);
            form.AcceptButton = okButton;
            form.CancelButton = cancelButton;

            list.SelectedIndexChanged += (sender, e) =>
            {
                okButton.Enabled = list.SelectedItems.Count > 0;
            };

            list.DoubleClick += (sender, e) =>
            {
                if (list.SelectedItems.Count > 0)
                {
                    form.DialogResult = DialogResult.OK;
                }
            };
            form.Controls.Add(list);
            form.ActiveControl = list;   // the focus on the list (arrows choose a board, Enter is OK), not on OK
            listView = list;
            return form;
        }
    }
}
