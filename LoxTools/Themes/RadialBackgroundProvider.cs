using System.Windows;
using System.Windows.Media;

namespace LoxTools {

    /// <summary>
    /// Shared radial background used by multiple windows (matches SettingsWindow).
    /// </summary>
    internal static class RadialBackgroundProvider {

        internal static readonly Brush RadialBg = CreateRadialBackground();

        private static Brush CreateRadialBackground() {
            var glow = new RadialGradientBrush {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                Center = new Point(0.78, 0.42),
                GradientOrigin = new Point(0.78, 0.42),
                RadiusX = 0.95,
                RadiusY = 0.95
            };

            glow.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#191E24"), 0.0));
            glow.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#283D2C"), 0.45));
            glow.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#191E24"), 1.0));
            glow.Freeze();

            var vignette = new RadialGradientBrush {
                MappingMode = BrushMappingMode.RelativeToBoundingBox,
                Center = new Point(0.5, 0.5),
                GradientOrigin = new Point(0.5, 0.5),
                RadiusX = 0.85,
                RadiusY = 0.85
            };
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
            vignette.GradientStops.Add(new GradientStop(Color.FromArgb(120, 0, 0, 0), 1.0));
            vignette.Freeze();

            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(glow, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            group.Children.Add(new GeometryDrawing(vignette, null, new RectangleGeometry(new Rect(0, 0, 1, 1))));
            group.Freeze();

            var brush = new DrawingBrush(group) {
                Stretch = Stretch.Fill,
                ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
                ViewportUnits = BrushMappingMode.RelativeToBoundingBox
            };
            brush.Freeze();
            return brush;
        }
    }
}
