using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Xml;

namespace BecquerelMonitor
{
    internal sealed class GdmlPartInspector : DockPanel
    {
        // A model identifies one physical placement even when many placements share its mesh.
        internal static readonly DependencyProperty VolumeProperty = DependencyProperty.RegisterAttached(
            "Volume", typeof(GdmlVolumeInstance), typeof(GdmlPartInspector), new PropertyMetadata(null));

        const int PageSize = 12000;
        readonly GdmlSourceDocument document;
        readonly TextBlock heading = new TextBlock { FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
        readonly TextBox properties = ReadOnlyText();
        readonly TextBox xml = ReadOnlyText();
        readonly TabControl tabs = new TabControl();
        readonly TabItem sourceTab = new TabItem { Header = "GDML" };
        readonly StackPanel pages = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock pageNumber = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        readonly Button previous = new Button { Content = "‹" }, next = new Button { Content = "›" };
        GdmlVolumeInstance current;
        CancellationTokenSource reading;
        string source;
        int page;

        internal GdmlPartInspector(GdmlSourceDocument document)
        {
            this.document = document;
            Background = SystemColors.ControlBrush;
            Margin = new Thickness(4, 2, 3, 2);
            heading.Margin = new Thickness(5, 5, 4, 6);
            SetDock(heading, Dock.Top);
            Children.Add(heading);
            var details = GdmlText.T("GdmlInfoProperties", "Properties");
            AutomationProperties.SetName(properties, details);
            AutomationProperties.SetName(xml, "GDML");
            tabs.Items.Add(new TabItem { Header = details, Content = properties });
            var content = new DockPanel();
            SetDock(pages, Dock.Bottom);
            content.Children.Add(pages);
            content.Children.Add(xml);
            xml.FontFamily = new FontFamily("Consolas");
            xml.TextWrapping = TextWrapping.NoWrap;
            xml.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            SetPageButton(previous, GdmlText.T("GdmlInfoPrevious", "Previous page"), -1);
            SetPageButton(next, GdmlText.T("GdmlInfoNext", "Next page"), 1);
            pages.Children.Add(previous);
            pages.Children.Add(pageNumber);
            pages.Children.Add(next);
            sourceTab.Content = content;
            tabs.Items.Add(sourceTab);
            tabs.SelectionChanged += (s,e) =>
            {
                if (e.Source != tabs) return;
                if (sourceTab.IsSelected) LoadSource();
                else if (reading != null) reading.Cancel();
            };
            Children.Add(tabs);
            SelectPart(null);
        }

        static TextBox ReadOnlyText()
        {
            return new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(5),
                BorderThickness = new Thickness(0), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        void SetPageButton(Button button, string name, int step)
        {
            button.Margin = new Thickness(4, 2, 4, 2);
            button.Padding = new Thickness(6, 0, 6, 0);
            button.ToolTip = name;
            AutomationProperties.SetName(button, name);
            button.Click += (s,e) => { page += step; ShowPage(); };
        }

        internal void SelectPart(GdmlVolumeInstance part)
        {
            if (reading != null) reading.Cancel();
            current = part;
            source = null;
            page = 0;
            pages.Visibility = Visibility.Collapsed;
            xml.Clear();
            heading.Text = part == null ? GdmlText.T("GdmlInfoTitle", "Object information") : part.VolumeName;
            properties.Text = part == null ? GdmlText.T("GdmlInfoHover", "Point to a part of the model.") : GdmlPartDetails.Describe(part);
            properties.ScrollToHome();
            sourceTab.IsEnabled = part != null && document != null;
            if (!sourceTab.IsEnabled) tabs.SelectedIndex = 0;
            else if (sourceTab.IsSelected) LoadSource();
        }

        async void LoadSource()
        {
            if (current == null || document == null || source != null) return;
            if (reading != null) reading.Cancel();
            var part = current;
            using (var stop = new CancellationTokenSource())
            {
                reading = stop;
                xml.Text = GdmlText.T("GdmlInfoReading", "Reading GDML…");
                try
                {
                    // A quick pass over several parts should not read the file for each one.
                    await Task.Delay(160, stop.Token);
                    string data = await Task.Run(() => document.Read(part, stop.Token), stop.Token);
                    if (stop.IsCancellationRequested) return;
                    source = data;
                    ShowPage();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is XmlException)
                {
                    if (!stop.IsCancellationRequested) xml.Text = error.Message;
                }
                finally
                {
                    if (ReferenceEquals(reading, stop)) reading = null;
                }
            }
        }

        void ShowPage()
        {
            int total = Math.Max(1, (source.Length + PageSize - 1) / PageSize);
            int start = page * PageSize, end = Math.Min(source.Length, start + PageSize);
            // Keep UTF-16 pairs together at page boundaries.
            if (start > 0 && char.IsLowSurrogate(source[start])) start--;
            if (end < source.Length && char.IsLowSurrogate(source[end])) end--;
            xml.Text = source.Substring(start, end - start);
            xml.ScrollToHome();
            pageNumber.Text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", page + 1, total);
            pages.Visibility = total > 1 ? Visibility.Visible : Visibility.Collapsed;
            previous.IsEnabled = page > 0;
            next.IsEnabled = page + 1 < total;
        }
    }
}
