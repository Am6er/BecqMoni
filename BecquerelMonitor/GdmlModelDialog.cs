using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Integration;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using Wpf = System.Windows.Controls;
using WpfControls = System.Windows.Controls.Primitives;
using WpfPoint = System.Windows.Point;
using WpfThickness = System.Windows.Thickness;
using MediaColor = System.Windows.Media.Color;

namespace BecquerelMonitor
{
    internal sealed class GdmlModelDialog : Form
    {
        readonly GdmlGeometry geometry;
        readonly GdmlOverview overview;
        readonly Wpf.Viewport3D viewport = new Wpf.Viewport3D { IsHitTestVisible = false, ClipToBounds = true };
        readonly OrthographicCamera camera = new OrthographicCamera();
        readonly Label status;
        CancellationTokenSource loading;
        const double InitialYaw = -1.2, InitialPitch = 0.18;
        readonly ToolStripButton panButton = new ToolStripButton { CheckOnClick = true };
        readonly ToolStripButton collisionButton = new ToolStripButton { CheckOnClick = true };
        readonly ToolStripButton infoButton = new ToolStripButton { CheckOnClick = true };
        readonly GdmlPartInspector inspector;
        readonly System.Windows.Threading.DispatcherTimer pickTimer = new System.Windows.Threading.DispatcherTimer
            { Interval = TimeSpan.FromMilliseconds(35) };
        readonly MaterialGroup highlight = new MaterialGroup();
        GdmlVolumeInstance selectedPart;
        Model3DGroup partFrame, inspectionFrame;
        int selectedIndex = -1;
        readonly Wpf.Border surface = new Wpf.Border();
        readonly GdmlOrientationGlobe orientation;
        readonly Wpf.TextBlock gestureText = new Wpf.TextBlock { Foreground = System.Windows.Media.Brushes.White };
        readonly Wpf.Border gestureHint = new Wpf.Border
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new WpfThickness(6), Padding = new WpfThickness(9, 4, 9, 4), CornerRadius = new System.Windows.CornerRadius(4),
            Background = new SolidColorBrush(MediaColor.FromArgb(230, 36, 43, 51)), IsHitTestVisible = false,
            Visibility = System.Windows.Visibility.Collapsed
        };
        bool temporaryDrag;
        bool PanDrag => panButton.Checked ^ temporaryDrag;
        readonly Wpf.Slider topCut = new Wpf.Slider(), leftCut = new Wpf.Slider();
        readonly ModelVisual3D partLayer = new ModelVisual3D();
        GdmlSectionScene.Axis topAxis, leftAxis;
        GdmlSectionScene partSections, collisionSections;
        Model3DGroup originalParts;
        bool sectionsPending;
        CancellationTokenSource sectionJob;
        readonly ModelVisual3D collisionLayer = new ModelVisual3D();
        CancellationTokenSource collisionJob;
        List<GdmlCollisions.Body> bodies;
        Model3DGroup collisionModel;
        string sceneNote = "", collisionNote = "", collisionSummary;
        WpfPoint lastMouse;
        double radius, yaw = InitialYaw, pitch = InitialPitch, zoom = 1, panX, panY;

        internal GdmlModelDialog(GdmlGeometry geometry, GdmlOverview overview)
        {
            this.geometry = geometry;
            this.overview = overview;
            inspector = new GdmlPartInspector(geometry.SourceDocument) { Visibility = System.Windows.Visibility.Collapsed };
            pickTimer.Tick += (s,e) => { pickTimer.Stop(); PickUnderMouse(); };
            highlight.Children.Add(new DiffuseMaterial(new SolidColorBrush(MediaColor.FromRgb(255, 195, 57))));
            highlight.Children.Add(new EmissiveMaterial(new SolidColorBrush(MediaColor.FromRgb(110, 64, 2))));
            highlight.Freeze();
            Text = string.Format(CultureInfo.CurrentCulture, GdmlText.T("Gdml3DTitle", "3D model — {0}"), Path.GetFileName(geometry.FileName));
            Icon = Properties.Resources.becqmoni;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MinimizeBox = false;
            ClientSize = new Size(760, 620);
            MinimumSize = new Size(360, 280);
            KeyPreview = true;

            viewport.Camera = camera;
            // A second viewport has its own depth buffer, so internal overlaps remain visible.
            var overlay = new Wpf.Viewport3D { Camera = camera, IsHitTestVisible = false, ClipToBounds = true };
            overlay.Children.Add(new ModelVisual3D { Content = new AmbientLight(Colors.White) });
            overlay.Children.Add(collisionLayer);
            var views = new Wpf.Grid();
            views.Children.Add(viewport);
            views.Children.Add(overlay);
            orientation = new GdmlOrientationGlobe(SnapToAxis) { Visibility = System.Windows.Visibility.Collapsed };
            var compass = new Wpf.Viewbox { Child = orientation, Stretch = Stretch.Uniform,
                StretchDirection = Wpf.StretchDirection.DownOnly, Margin = new WpfThickness(6),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left, VerticalAlignment = System.Windows.VerticalAlignment.Top };
            views.Children.Add(compass);
            surface.SizeChanged += (s,e) =>
            {
                compass.MaxWidth = Math.Max(0, e.NewSize.Width * 0.48);
                compass.MaxHeight = Math.Max(0, e.NewSize.Height * 0.42);
            };
            gestureHint.Child = gestureText;
            views.Children.Add(gestureHint);
            surface.Child = views;
            SetBackground(GlobalConfigManager.GetInstance().GlobalConfig.ColorConfig.GdmlBackgroundColor.Color);
            surface.Focusable = true;
            surface.Cursor = System.Windows.Input.Cursors.Arrow;
            surface.MouseLeftButtonDown += (s,e) =>
            {
                surface.Focus();
                ReadDragModifier();
                lastMouse = e.GetPosition(surface);
                surface.CaptureMouse();
            };
            surface.MouseLeftButtonUp += (s,e) => { surface.ReleaseMouseCapture(); SchedulePick(); };
            surface.MouseEnter += (s,e) => { ReadDragModifier(); SchedulePick(); };
            surface.MouseMove += (s,e) =>
            {
                ReadDragModifier();
                if (!surface.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed)
                {
                    SchedulePick();
                    return;
                }
                WpfPoint p = e.GetPosition(surface);
                double dx = p.X - lastMouse.X, dy = p.Y - lastMouse.Y;
                lastMouse = p;
                DragBy(dx, dy);
            };
            surface.MouseWheel += (s,e) =>
            {
                zoom = Math.Max(0.2, Math.Min(20, zoom * Math.Pow(1.16, e.Delta / 120.0)));
                UpdateCamera();
                e.Handled = true;
            };
            viewport.SizeChanged += (s,e) => UpdateCamera();
            var content = new Wpf.DockPanel();
            content.PreviewKeyDown += (s,e) =>
            {
                ReadDragModifier();
                if (e.Key == Key.Escape && Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
                {
                    e.Handled = true;
                    TurnOffViewModes();
                }
            };
            content.PreviewKeyUp += (s,e) => ReadDragModifier();
            KeyDown += (s,e) => ReadDragModifier();
            KeyUp += (s,e) => ReadDragModifier();
            Activated += (s,e) => ReadDragModifier();
            Deactivate += (s,e) => SetDragModifiers(Keys.None);
            content.Children.Add(inspector);
            content.Children.Add(BuildSectionPanel());
            content.SizeChanged += (s,e) =>
            {
                bool narrow = content.ActualWidth < 610;
                Wpf.DockPanel.SetDock(inspector, narrow ? Wpf.Dock.Bottom : Wpf.Dock.Right);
                inspector.Width = narrow ? double.NaN : Math.Min(355, content.ActualWidth * 0.39);
                inspector.Height = narrow ? Math.Min(200, content.ActualHeight * 0.42) : double.NaN;
            };
            Controls.Add(new ElementHost { Dock = DockStyle.Fill, Child = content });
            status = new Label
            {
                Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(8, 5, 8, 5),
                Text = GdmlText.T("Gdml3DLoading", "Building the 3D model…"),
            };
            Controls.Add(status);
            Controls.Add(BuildToolbar());
        }

        Wpf.Grid BuildSectionPanel()
        {
            var panel = new Wpf.Grid { Background = System.Windows.SystemColors.ControlBrush };
            panel.RowDefinitions.Add(new Wpf.RowDefinition { Height = System.Windows.GridLength.Auto });
            panel.RowDefinitions.Add(new Wpf.RowDefinition());
            panel.ColumnDefinitions.Add(new Wpf.ColumnDefinition { Width = System.Windows.GridLength.Auto });
            panel.ColumnDefinitions.Add(new Wpf.ColumnDefinition());

            leftCut.IsDirectionReversed = true;
            var topScale = BuildSlider(topCut, Wpf.Orientation.Horizontal, GdmlText.T("Gdml3DVerticalCut", "Vertical cut from the left edge"));
            var leftScale = BuildSlider(leftCut, Wpf.Orientation.Vertical, GdmlText.T("Gdml3DHorizontalCut", "Horizontal cut from the top edge"));
            topCut.ValueChanged += (s,e) => ChangeSection(topCut, ref topAxis);
            leftCut.ValueChanged += (s,e) => ChangeSection(leftCut, ref leftAxis);
            Wpf.Grid.SetColumn(topScale, 1);
            Wpf.Grid.SetRow(leftScale, 1);
            Wpf.Grid.SetRow(surface, 1);
            Wpf.Grid.SetColumn(surface, 1);
            panel.Children.Add(topScale);
            panel.Children.Add(leftScale);
            panel.Children.Add(surface);
            return panel;
        }

        static Wpf.DockPanel BuildSlider(Wpf.Slider slider, Wpf.Orientation orientation, string label)
        {
            bool horizontal = orientation == Wpf.Orientation.Horizontal;
            slider.Orientation = orientation;
            slider.Minimum = 0;
            slider.Maximum = 100;
            slider.SmallChange = 1;
            slider.LargeChange = 10;
            slider.TickFrequency = slider.SmallChange;
            slider.IsSnapToTickEnabled = true;
            slider.AutoToolTipPlacement = WpfControls.AutoToolTipPlacement.BottomRight;
            slider.AutoToolTipPrecision = 0;
            slider.IsMoveToPointEnabled = true;
            slider.IsEnabled = false;
            slider.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(slider, label);

            // Snap every percent, but keep the ten major divisions readable at any window size.
            var marks = new Wpf.Grid { Background = System.Windows.Media.Brushes.Transparent };
            var ticks = new WpfControls.TickBar { Minimum = slider.Minimum, Maximum = slider.Maximum,
                TickFrequency = slider.LargeChange, IsDirectionReversed = slider.IsDirectionReversed,
                Placement = horizontal ? WpfControls.TickBarPlacement.Bottom : WpfControls.TickBarPlacement.Right };
            ticks.SetResourceReference(WpfControls.TickBar.FillProperty, System.Windows.SystemColors.ControlTextBrushKey);
            var centre = new Wpf.Border { Width = horizontal ? 2 : 7, Height = horizontal ? 7 : 2 };
            centre.SetResourceReference(Wpf.Border.BackgroundProperty, System.Windows.SystemColors.ControlTextBrushKey);
            if (horizontal)
            {
                ticks.Height = 4;
                ticks.VerticalAlignment = System.Windows.VerticalAlignment.Top;
            }
            else
            {
                ticks.Width = 4;
                ticks.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
            }
            WpfControls.Track track = null;
            slider.Loaded += (s,e) =>
            {
                track = slider.Template?.FindName("PART_Track", slider) as WpfControls.Track;
                if (track?.Thumb != null)
                    ticks.SetBinding(WpfControls.TickBar.ReservedSpaceProperty, new System.Windows.Data.Binding(
                        horizontal ? "ActualWidth" : "ActualHeight") { Source = track.Thumb });
            };
            marks.MouseLeftButtonDown += (s,e) =>
            {
                if (!slider.IsEnabled || track == null) return;
                slider.Focus();
                WpfPoint p = e.GetPosition(centre);
                slider.Value = new System.Windows.Rect(centre.RenderSize).Contains(p)
                    ? (slider.Minimum + slider.Maximum) / 2
                    : Math.Round(track.ValueFromPoint(e.GetPosition(track)), MidpointRounding.AwayFromZero);
                e.Handled = true;
            };
            marks.Children.Add(ticks);
            marks.Children.Add(centre);
            var panel = new Wpf.DockPanel { Margin = new WpfThickness(4), UseLayoutRounding = true };
            Wpf.DockPanel.SetDock(marks, horizontal ? Wpf.Dock.Bottom : Wpf.Dock.Right);
            panel.Children.Add(marks);
            panel.Children.Add(slider);
            return panel;
        }

        void ChangeSection(Wpf.Slider slider, ref GdmlSectionScene.Axis axis)
        {
            if (partSections == null) return;
            if (slider.Value == 0)
                axis = null;
            else if (axis == null)
            {
                // Lock the cut to the model when the slider leaves its starting position.
                Vector3D direction = slider == leftCut ? -camera.UpDirection :
                    Vector3D.CrossProduct(camera.LookDirection, camera.UpDirection);
                direction.Normalize();
                axis = partSections.Range(direction);
            }
            RefreshSections();
        }

        void RefreshSections()
        {
            if (originalParts == null || IsDisposed) return;
            bool empty = topCut.Value == topCut.Maximum || leftCut.Value == leftCut.Maximum;
            if (empty || (topAxis == null && leftAxis == null))
            {
                sectionsPending = false;
                if (sectionJob != null) sectionJob.Cancel();
                SetPartFrame(empty ? null : originalParts);
                collisionLayer.Content = !empty && collisionButton.Checked ? collisionModel : null;
                return;
            }
            sectionsPending = true;
            if (sectionJob == null)
                RenderSections();
        }

        // Coalesce drag events while a frame is being built. Both layers are published together.
        async void RenderSections()
        {
            using (var stop = new CancellationTokenSource())
            {
                sectionJob = stop;
                try
                {
                    while (sectionsPending && !stop.IsCancellationRequested)
                    {
                        sectionsPending = false;
                        var vertical = topAxis?.At(topCut.Value / topCut.Maximum);
                        var horizontal = leftAxis?.At(leftCut.Value / leftCut.Maximum);
                        var marks = collisionButton.Checked ? collisionSections : null;
                        var frame = await Task.Run(() => new[] {
                            partSections.Cut(vertical, horizontal, stop.Token),
                            marks?.Cut(vertical, horizontal, stop.Token)
                        }, stop.Token);
                        if (!stop.IsCancellationRequested && !IsDisposed)
                        {
                            SetPartFrame(frame[0]);
                            collisionLayer.Content = collisionButton.Checked ? frame[1] : null;
                        }
                    }
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (!IsDisposed && !stop.IsCancellationRequested)
                    {
                        topCut.Value = leftCut.Value = 0;
                        status.Text = string.Format(CultureInfo.CurrentCulture,
                            GdmlText.T("Gdml3DSectionError", "Could not display the cut: {0}"), error.Message);
                    }
                }
                finally
                {
                    sectionJob = null;
                }
            }
            if (sectionsPending && !IsDisposed) RenderSections();
        }

        ToolStripEx BuildToolbar()
        {
            var bar = new ToolStripEx
            {
                Dock = DockStyle.Top, GripStyle = ToolStripGripStyle.Hidden,
                ClickThrough = true, SuppressHighlighting = false, TabStop = true,
                LayoutStyle = ToolStripLayoutStyle.Flow,
                ImageScalingSize = LogicalToDeviceUnits(new Size(20, 20)),
            };
            var reset = new ToolStripButton();
            var background = new ToolStripButton();
            SetTool(panButton, Properties.Resources.GdmlPanIcon, GdmlText.T("Gdml3DPan", "Pan"),
                GdmlText.T("Gdml3DPanTip", "Drag to pan. Hold Shift or Ctrl to temporarily switch between pan and rotation."));
            SetTool(reset, Properties.Resources.GdmlResetIcon, GdmlText.T("Gdml3DReset", "Reset"),
                GdmlText.T("Gdml3DResetTip", "Restore the initial view and tools."));
            SetTool(collisionButton, Properties.Resources.GdmlOverlapIcon, GdmlText.T("Gdml3DShowCollisions", "Show overlaps"),
                GdmlText.T("Gdml3DCollisionTip", "Highlight intersecting volumes in red."));
            SetTool(infoButton, Properties.Resources.GdmlInfoIcon, GdmlText.T("GdmlInfoTitle", "Object information"),
                GdmlText.T("GdmlInfoHover", "Point to a part of the model."));
            SetTool(background, Properties.Resources.GdmlBackgroundIcon, GdmlText.T("Gdml3DBackground", "Background colour"),
                GdmlText.T("Gdml3DBackgroundTip", "Choose the 3D viewer background colour."));

            panButton.CheckedChanged += (s,e) => SetPanMode();
            reset.Click += (s,e) => ResetView();
            background.Click += (s,e) => ChooseBackground();
            collisionButton.Enabled = false;
            collisionButton.CheckedChanged += (s,e) =>
            {
                if (collisionButton.Checked) ShowCollisions();
                else HideCollisions();
            };
            infoButton.Enabled = false;
            infoButton.CheckedChanged += (s,e) => SetInfoMode();
            bar.Items.AddRange(new ToolStripItem[] { panButton, reset, new ToolStripSeparator(), collisionButton, infoButton, background });
            return bar;
        }

        void SetBackground(System.Drawing.Color color)
        {
            surface.Background = new SolidColorBrush(MediaColor.FromRgb(color.R, color.G, color.B));
        }

        void ChooseBackground()
        {
            var config = GlobalConfigManager.GetInstance();
            var colors = config.GlobalConfig.ColorConfig;
            using (var dialog = new ColorDialog { Color = colors.GdmlBackgroundColor.Color, FullOpen = true,
                CustomColors = new[] { ColorTranslator.ToWin32(ColorConfig.DefaultGdmlBackgroundColor) } })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                SetBackground(dialog.Color);
                if (dialog.Color.ToArgb() == colors.GdmlBackgroundColor.Color.ToArgb()) return;
                colors.GdmlBackgroundColor = dialog.Color;
                config.SaveConfigFile();
            }
        }

        static void SetTool(ToolStripButton button, Bitmap icon, string name, string tip)
        {
            button.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
            button.TextImageRelation = TextImageRelation.ImageBeforeText;
            button.Padding = new Padding(3, 2, 3, 2);
            button.Image = icon;
            button.Text = name;
            button.ToolTipText = name + Environment.NewLine + tip;
            button.AccessibleName = name;
            button.AccessibleDescription = tip;
        }

        void SetInfoMode()
        {
            bool enabled = infoButton.Checked;
            inspector.Visibility = enabled ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            InstallPartFrame();
        }

        void SetPartFrame(Model3DGroup frame)
        {
            if (ReferenceEquals(frame, partFrame)) return;
            partFrame = frame;
            InstallPartFrame();
        }

        void InstallPartFrame()
        {
            // Only the list of models is mutable. Meshes and unselected materials remain shared.
            inspectionFrame = null;
            selectedIndex = -1;
            pickTimer.Stop();
            int retained = -1;
            if (infoButton.Checked && partFrame != null)
            {
                inspectionFrame = new Model3DGroup();
                foreach (Model3D model in partFrame.Children)
                {
                    if (selectedPart != null && ReferenceEquals(model.GetValue(GdmlPartInspector.VolumeProperty), selectedPart))
                        retained = inspectionFrame.Children.Count;
                    inspectionFrame.Children.Add(model);
                }
            }
            SelectModel(retained);
            partLayer.Content = inspectionFrame ?? partFrame;
            SchedulePick();
        }

        void SchedulePick()
        {
            if (inspectionFrame != null && surface.IsMouseOver && !surface.IsMouseCaptured && !pickTimer.IsEnabled)
                pickTimer.Start();
        }

        void PickUnderMouse()
        {
            if (inspectionFrame == null || !surface.IsMouseOver || surface.IsMouseCaptured || orientation.IsMouseOver) return;
            var point = Mouse.GetPosition(viewport);
            GeometryModel3D nearest = null;
            double distance = double.PositiveInfinity;
            int depth = -1;
            // Test the displayed part meshes only. Red overlap marks are a separate viewport.
            VisualTreeHelper.HitTest(viewport, null, result =>
            {
                var hit = result as RayMeshGeometry3DHitTestResult;
                var model = hit?.ModelHit as GeometryModel3D;
                var volume = model?.GetValue(GdmlPartInspector.VolumeProperty) as GdmlVolumeInstance;
                if (volume != null && (hit.DistanceToRayOrigin < distance ||
                    (hit.DistanceToRayOrigin == distance && volume.Depth > depth)))
                {
                    nearest = model;
                    distance = hit.DistanceToRayOrigin;
                    depth = volume.Depth;
                }
                return HitTestResultBehavior.Continue;
            }, new PointHitTestParameters(point));
            // Retain the last part over empty space so the user can reach and scroll its card.
            if (nearest == null || (selectedIndex >= 0 && inspectionFrame.Children[selectedIndex] == nearest)) return;
            int index = inspectionFrame.Children.IndexOf(nearest);
            if (index >= 0) SelectModel(index);
        }

        void SelectModel(int index)
        {
            if (selectedIndex >= 0)
                inspectionFrame.Children[selectedIndex] = partFrame.Children[selectedIndex];
            selectedIndex = index;
            GdmlVolumeInstance part = null;
            if (index >= 0)
            {
                var model = (GeometryModel3D)partFrame.Children[index];
                part = (GdmlVolumeInstance)model.GetValue(GdmlPartInspector.VolumeProperty);
                var selected = new GeometryModel3D(model.Geometry, highlight)
                    { Transform = model.Transform, BackMaterial = highlight };
                selected.SetValue(GdmlPartInspector.VolumeProperty, part);
                inspectionFrame.Children[index] = selected;
            }
            if (!ReferenceEquals(selectedPart, part))
            {
                selectedPart = part;
                inspector.SelectPart(part);
            }
        }

        void SetPanMode()
        {
            if (!panButton.Checked && surface.IsMouseCaptured)
                surface.ReleaseMouseCapture();
            UpdateDragHint();
            UpdateStatus();
        }

        void ReadDragModifier()
        {
            SetDragModifiers(Control.ModifierKeys);
        }

        void SetDragModifiers(Keys keys)
        {
            bool active = (keys & (Keys.Shift | Keys.Control)) != 0;
            if (temporaryDrag == active) return;
            temporaryDrag = active;
            UpdateDragHint();
        }

        void UpdateDragHint()
        {
            surface.Cursor = PanDrag ? System.Windows.Input.Cursors.Hand : System.Windows.Input.Cursors.Arrow;
            gestureHint.Visibility = temporaryDrag ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (temporaryDrag)
                gestureText.Text = PanDrag ? GdmlText.T("Gdml3DTemporaryPan", "Shift / Ctrl · Drag to pan")
                    : GdmlText.T("Gdml3DTemporaryRotate", "Shift / Ctrl · Drag to rotate");
        }

        void DragBy(double dx, double dy)
        {
            if (PanDrag)
            {
                if (viewport.ActualWidth <= 0) return;
                double perPixel = camera.Width / viewport.ActualWidth;
                panX -= dx * perPixel;
                panY += dy * perPixel;
            }
            else
            {
                yaw -= dx * 0.01;
                pitch += dy * 0.01;
            }
            UpdateCamera();
        }

        void SnapToAxis(int axis, int sign)
        {
            if (axis == 2)
            {
                yaw = -Math.PI/2;
                pitch = sign*Math.PI/2;
            }
            else
            {
                yaw = axis == 0 ? (sign > 0 ? 0 : Math.PI) : sign*Math.PI/2;
                pitch = 0;
            }
            UpdateCamera();
        }

        void TurnOffViewModes()
        {
            panButton.Checked = false;
            collisionButton.Checked = false;
            infoButton.Checked = false;
        }

        void ResetView()
        {
            TurnOffViewModes();
            yaw = InitialYaw;
            pitch = InitialPitch;
            zoom = 1;
            panX = panY = 0;
            topCut.Value = leftCut.Value = 0;
            collisionNote = "";
            UpdateStatus();
            UpdateCamera();
        }

        void UpdateStatus()
        {
            if (bodies == null)
                return;
            string drag = panButton.Checked
                ? GdmlText.T("Gdml3DPanControls", "Drag with the left mouse button to pan. Use the wheel to zoom.")
                : GdmlText.T("Gdml3DControls", "Drag with the left mouse button to rotate. Use the wheel to zoom.");
            status.Text = (collisionNote.Length == 0 ? drag : collisionNote) + sceneNote;
        }

        void HideCollisions()
        {
            if (collisionJob != null)
                collisionJob.Cancel();
            collisionLayer.Content = null;
            viewport.Opacity = 1;
            collisionNote = "";
            UpdateStatus();
        }

        async void ShowCollisions()
        {
            if (bodies == null)
                return;
            if (collisionModel == null)
            {
                using (var stop = new CancellationTokenSource())
                {
                    collisionJob = stop;
                    collisionNote = GdmlText.T("Gdml3DCollisionBusy", "Checking for overlaps…");
                    UpdateStatus();
                    try
                    {
                        GdmlCollisions result = await Task.Run(() => GdmlCollisions.Find(bodies, stop.Token), stop.Token);
                        if (stop.IsCancellationRequested || IsDisposed)
                            return;
                        var red = new DiffuseMaterial(new SolidColorBrush(MediaColor.FromRgb(250, 14, 20)));
                        var marks = new Model3DGroup();
                        var sections = new GdmlSectionScene();
                        foreach (MeshGeometry3D mesh in result.Regions)
                        {
                            var model = new GeometryModel3D(mesh, red) { BackMaterial = red };
                            marks.Children.Add(model);
                            sections.Add(model, true);
                        }
                        marks.Freeze();
                        collisionModel = marks;
                        collisionSections = sections;
                        collisionSummary = result.Regions.Count == 0
                            ? GdmlText.T("Gdml3DNoCollisions", "No overlaps found at grid resolution.")
                            : string.Format(CultureInfo.CurrentCulture, GdmlText.T("Gdml3DCollisionCount", "Overlapping pairs: {0}. Shared regions are shown in red."), result.Regions.Count);
                        if (result.UncheckedParts > 0)
                            collisionSummary += " " + string.Format(CultureInfo.CurrentCulture,
                                GdmlText.T("Gdml3DCollisionSkipped", "Meshes with undefined interior skipped: {0}."), result.UncheckedParts);
                    }
                    catch (OperationCanceledException) when (stop.IsCancellationRequested) { return; }
                    catch (Exception error)
                    {
                        if (!stop.IsCancellationRequested && !IsDisposed)
                        {
                            collisionButton.Checked = false;
                            collisionNote = string.Format(CultureInfo.CurrentCulture,
                                GdmlText.T("Gdml3DCollisionError", "Could not check overlaps: {0}"), error.Message);
                            UpdateStatus();
                        }
                        return;
                    }
                    finally
                    {
                        if (ReferenceEquals(collisionJob, stop))
                            collisionJob = null;
                    }
                }
            }
            RefreshSections();
            viewport.Opacity = collisionModel.Children.Count == 0 ? 1 : 0.55;
            collisionNote = collisionSummary;
            UpdateStatus();
        }

        sealed class Scene
        {
            internal readonly Model3DGroup Parts = new Model3DGroup();
            internal readonly List<GdmlCollisions.Body> Bodies = new List<GdmlCollisions.Body>();
            internal readonly GdmlSectionScene Sections = new GdmlSectionScene();
            internal int Omitted;
            internal bool UsesGrid;
        }

        static bool IsAir(string material)
        {
            switch ((material ?? "").Trim().ToUpperInvariant())
            {
                case "G4_AIR":
                case "AIR":
                case "DRY AIR":
                case "DRY_AIR":
                case "DRYAIR":
                case "ВОЗДУХ":
                    return true;
                default:
                    return false;
            }
        }

        Scene BuildScene(CancellationToken cancel)
        {
            Scene scene = new Scene();
            GdmlModelMesh builder = new GdmlModelMesh(cancel);
            Dictionary<System.Drawing.Color,Material> colors = new Dictionary<System.Drawing.Color,Material>();
            var interiors = new Dictionary<GdmlSolid, bool>();
            foreach (GdmlVolumeInstance part in geometry.Volumes)
            {
                cancel.ThrowIfCancellationRequested();
                if (overview.IsOutOfFrame(part))
                    continue;
                if (GdmlGeometry.IsUnsupported(part) || GdmlGeometry.IsUnplaced(part))
                {
                    scene.Omitted++;
                    continue;
                }
                MeshGeometry3D mesh = builder.Get(part.Solid);
                if (mesh == null || mesh.TriangleIndices.Count == 0)
                {
                    scene.Omitted++;
                    continue;
                }
                System.Drawing.Color color = overview.ColorOf(part.MaterialName);
                Material material;
                if (!colors.TryGetValue(color, out material))
                {
                    material = new DiffuseMaterial(new SolidColorBrush(MediaColor.FromRgb(color.R, color.G, color.B)));
                    material.Freeze();
                    colors.Add(color, material);
                }
                Matrix3D placement = GdmlModelMesh.Placement(part.Transform);
                scene.Bodies.Add(new GdmlCollisions.Body(part, mesh, placement));
                GeometryModel3D model = new GeometryModel3D(mesh, material)
                {
                    Transform = new MatrixTransform3D(placement),
                    BackMaterial = material,
                };
                model.SetValue(GdmlPartInspector.VolumeProperty, part);
                model.Freeze();
                bool visible = !IsAir(part.MaterialName);
                if (visible) scene.UsesGrid |= GdmlModelMesh.IsBoolean(part.Solid);
                scene.Sections.Add(model, GdmlCollisions.HasDefinedInterior(part.Solid, interiors), visible);
            }
            scene.Sections.BuildVisibleParts(scene.Parts, cancel);
            scene.Parts.Freeze();
            return scene;
        }

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                loading = stop;
                try
                {
                    Scene scene = await Task.Run(() => BuildScene(stop.Token), stop.Token);
                    if (stop.IsCancellationRequested || IsDisposed)
                        return;
                    Rect3D bounds = scene.Parts.Bounds;
                    string hint = GdmlText.T("Gdml3DEmpty", "There are no surfaces to display.");
                    if (!bounds.IsEmpty)
                    {
                        radius = 0.5 * new Vector3D(bounds.SizeX, bounds.SizeY, bounds.SizeZ).Length;
                        if (radius > 0 && !double.IsInfinity(radius))
                        {
                            var centreScene = new TranslateTransform3D(-bounds.X - bounds.SizeX / 2,
                                -bounds.Y - bounds.SizeY / 2, -bounds.Z - bounds.SizeZ / 2);
                            partLayer.Transform = centreScene;
                            originalParts = scene.Parts;
                            SetPartFrame(originalParts);
                            partSections = scene.Sections;
                            viewport.Children.Add(partLayer);
                            collisionLayer.Transform = centreScene;
                            bodies = scene.Bodies;
                            orientation.Visibility = System.Windows.Visibility.Visible;
                            infoButton.Enabled = true;
                            collisionButton.Enabled = true;
                            topCut.IsEnabled = leftCut.IsEnabled = true;
                            Model3DGroup lights = new Model3DGroup();
                            lights.Children.Add(new AmbientLight(MediaColor.FromRgb(85, 85, 85)));
                            lights.Children.Add(new DirectionalLight(MediaColor.FromRgb(195, 195, 195), new Vector3D(-1, 2, -2)));
                            lights.Children.Add(new DirectionalLight(MediaColor.FromRgb(95, 95, 95), new Vector3D(1, -2, 0)));
                            viewport.Children.Add(new ModelVisual3D { Content = lights });
                            UpdateCamera();
                        }
                    }
                    if (scene.UsesGrid)
                        sceneNote += " " + GdmlText.T("Gdml3DBoolean", "Boolean solids are shown at grid resolution.");
                    if (scene.Omitted > 0)
                        sceneNote += " " + string.Format(CultureInfo.CurrentCulture, GdmlText.T("Gdml3DSkipped", "Parts omitted: {0}."), scene.Omitted);
                    if (bodies == null)
                        status.Text = hint + sceneNote;
                    else
                        UpdateStatus();
                }
                catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
                catch (Exception error)
                {
                    if (!IsDisposed && !stop.IsCancellationRequested)
                        status.Text = string.Format(CultureInfo.CurrentCulture, GdmlText.T("Gdml3DError", "Could not display the model: {0}"), error.Message);
                }
                finally
                {
                    loading = null;
                }
            }
        }

        void UpdateCamera()
        {
            if (!(radius > 0) || double.IsInfinity(radius) || viewport.ActualHeight <= 0 || viewport.ActualWidth <= 0)
                return;
            double cos = Math.Cos(pitch), sin = Math.Sin(pitch);
            Vector3D outward = new Vector3D(cos*Math.Cos(yaw), cos*Math.Sin(yaw), sin);
            Vector3D right = new Vector3D(-Math.Sin(yaw), Math.Cos(yaw), 0);
            Vector3D up = new Vector3D(-sin * Math.Cos(yaw), -sin * Math.Sin(yaw), cos);
            orientation.SetOrientation(right, up, outward);
            camera.Position = (Point3D)(outward * (3 * radius) + right * panX + up * panY);
            camera.LookDirection = -outward;
            camera.UpDirection = up;
            camera.NearPlaneDistance = radius;
            camera.FarPlaneDistance = 5*radius;
            camera.Width = 2.12*radius*Math.Max(1, viewport.ActualWidth/viewport.ActualHeight)/zoom;
            SchedulePick();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData != Keys.Escape)
                return base.ProcessCmdKey(ref msg, keyData);
            TurnOffViewModes();
            return true;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            pickTimer.Stop();
            inspector.SelectPart(null);
            sectionsPending = false;
            if (sectionJob != null) sectionJob.Cancel();
            if (loading != null)
                loading.Cancel();
            if (collisionJob != null)
                collisionJob.Cancel();
            base.OnFormClosed(e);
        }
    }
}
