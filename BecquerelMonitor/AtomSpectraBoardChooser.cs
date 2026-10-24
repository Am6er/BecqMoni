// "Refresh" found several AtomSpectra boards: the person chooses one (address, serial number).
// Texts: Properties.Resources by name (ResourceManager.GetString) with an English fallback.
using BecquerelMonitor.Properties;
using System;
using System.Collections.Generic;
using System.Drawing;
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

            Label promptLabel = new Label();
            promptLabel.Location = new Point(12, 12);
            promptLabel.AutoSize = false;
            promptLabel.Size = new Size(536, 36);
            promptLabel.Text = Resources.AtomSpectraBoardChoicePrompt;
            form.Controls.Add(promptLabel);

            ListView list = new ListView();
            list.Location = new Point(12, 52);
            list.Size = new Size(536, 196);
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
            okButton.Text = "OK";
            okButton.DialogResult = DialogResult.OK;
            okButton.Size = new Size(90, 28);
            okButton.Location = new Point(362, 258);
            okButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            okButton.Enabled = list.SelectedItems.Count > 0;

            Button cancelButton = new Button();
            cancelButton.Text = Resources.AtomSpectraBoardChoiceCancel;
            cancelButton.DialogResult = DialogResult.Cancel;
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
            listView = list;
            return form;
        }
    }
}
