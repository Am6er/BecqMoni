using BecquerelMonitor.EfficiencyMaker;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace BecquerelMonitor
{
    // The camera and the globe share the same screen basis, including at the poles.
    internal sealed class GdmlOrientationGlobe : Canvas
    {
        const double Radius = 47;
        static readonly Point Centre = new Point(76, 59);
        static readonly Vector3D[] Axes = { new Vector3D(1,0,0), new Vector3D(0,1,0), new Vector3D(0,0,1) };
        static readonly string[] Names = { "X", "Y", "Z" };
        static readonly Color[] ColorsByAxis = { Color.FromRgb(236,112,99), Color.FromRgb(112,188,128), Color.FromRgb(96,166,226) };
        static readonly Brush PanelBrush = new SolidColorBrush(Color.FromArgb(230, 36, 43, 51));
        static readonly Pen Rim = new Pen(new SolidColorBrush(Color.FromRgb(127,145,160)), 1);
        static readonly Pen GridLine = new Pen(new SolidColorBrush(Color.FromRgb(111,131,148)), 0.8);
        static readonly Brush Sphere = new RadialGradientBrush(Color.FromRgb(84,104,121), Color.FromRgb(48,59,71))
            { GradientOrigin = new Point(0.32,0.26), RadiusX = 0.78, RadiusY = 0.78 };
        readonly Button[] ends = new Button[6];
        readonly TextBlock[] angles = new TextBlock[3];
        readonly Action<int,int> snap;
        readonly string viewTip = GdmlText.T("Gdml3DAxisView", "View from {0}");
        Vector3D right, up, outward;

        internal GdmlOrientationGlobe(Action<int,int> snap)
        {
            this.snap = snap;
            Width = 152;
            Height = 137;
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Marker";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(1));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            var label = new FrameworkElementFactory(typeof(ContentPresenter));
            label.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
            label.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(label);
            template.VisualTree = border;
            var hover = new Trigger { Property = IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brushes.White, "Marker"));
            template.Triggers.Add(hover);
            string angleTip = GdmlText.T("Gdml3DAxisAngles", "Angles between the direction to the camera and the positive X, Y and Z axes.");
            ToolTip = angleTip;
            for (int axis = 0; axis < 3; axis++)
            {
                var readout = new TextBlock { Width = 50, TextAlignment = TextAlignment.Center,
                    FontSize = 10.5, Foreground = new SolidColorBrush(ColorsByAxis[axis]), IsHitTestVisible = false };
                angles[axis] = readout;
                SetLeft(readout, 1 + 50*axis);
                SetTop(readout, 117);
                Children.Add(readout);
                for (int side = 0; side < 2; side++)
                {
                    int index = 2*axis + side;
                    var b = new Button { Width = 23, Height = 21, Template = template,
                        Content = AxisName(axis, side == 0 ? 1 : -1), FontSize = 10.5, FontWeight = FontWeights.Bold,
                        BorderBrush = Brushes.SlateGray, Cursor = Cursors.Hand };
                    b.Click += (s,e) => SnapTo(index);
                    ends[index] = b;
                    Children.Add(b);
                }
            }
        }

        static string AxisName(int axis, int sign) { return (sign > 0 ? "+" : "−") + Names[axis]; }

        int ClickDirection(int index)
        {
            int sign = index % 2 == 0 ? 1 : -1;
            // In an axial view both poles project onto the centre. A second click flips the view.
            return Vector3D.DotProduct(outward, Axes[index/2])*sign > 1-1e-12 ? -sign : sign;
        }

        void SnapTo(int index) { snap(index/2, ClickDirection(index)); }

        internal void SetOrientation(Vector3D screenRight, Vector3D screenUp, Vector3D cameraDirection)
        {
            if (right == screenRight && up == screenUp && outward == cameraDirection) return;
            right = screenRight;
            up = screenUp;
            outward = cameraDirection;
            for (int axis = 0; axis < 3; axis++)
            {
                double depth = Vector3D.DotProduct(Axes[axis],outward);
                double degrees = Math.Acos(Math.Max(-1, Math.Min(1,depth)))*180/Math.PI;
                angles[axis].Text = string.Format(CultureInfo.CurrentCulture, "{0} {1:F1}°", Names[axis], degrees);
                for (int side = 0; side < 2; side++)
                {
                    int sign = side == 0 ? 1 : -1;
                    int index = 2*axis+side;
                    Button b = ends[index];
                    Point p = Project(Axes[axis]*sign);
                    SetLeft(b, p.X-b.Width/2);
                    SetTop(b, p.Y-b.Height/2);
                    bool near = depth*sign >= -1e-10;
                    b.Background = near ? angles[axis].Foreground : PanelBrush;
                    b.Foreground = near ? Brushes.Black : angles[axis].Foreground;
                    SetZIndex(b, 1001 + (int)Math.Round(1000*depth*sign));
                    string tip = string.Format(CultureInfo.CurrentCulture, viewTip, AxisName(axis, ClickDirection(index)));
                    b.ToolTip = tip;
                    AutomationProperties.SetName(b, tip);
                }
            }
            InvalidateVisual();
        }

        Point Project(Vector3D v)
        {
            return new Point(Centre.X+Radius*Vector3D.DotProduct(v,right), Centre.Y-Radius*Vector3D.DotProduct(v,up));
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            dc.DrawRoundedRectangle(PanelBrush, null, new Rect(0,0,Width,Height), 7, 7);
            dc.DrawEllipse(Sphere, Rim, Centre, Radius, Radius);
            for (int axis = 0; axis < 3; axis++)
            {
                // Trace the visible semicircle of each of the three great circles.
                var a = Axes[(axis+1)%3];
                var b = Axes[(axis+2)%3];
                double middle = Math.Atan2(Vector3D.DotProduct(outward,b), Vector3D.DotProduct(outward,a));
                var arc = new StreamGeometry();
                using (var ctx = arc.Open())
                    for (int i = 0; i <= 32; i++)
                    {
                        double t = middle - Math.PI/2 + i*Math.PI/32;
                        Point p = Project(a*Math.Cos(t)+b*Math.Sin(t));
                        if (i == 0) ctx.BeginFigure(p,false,false);
                        else ctx.LineTo(p,true,true);
                    }
                dc.DrawGeometry(null, GridLine, arc);
                dc.DrawLine(new Pen(angles[axis].Foreground, 1.5), Centre, Project(Axes[axis]));
            }
        }
    }
}
