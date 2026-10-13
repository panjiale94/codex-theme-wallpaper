using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CodexDreamSkin;

internal sealed class ThemePreset(string id, string name, string description, string file, string effect)
{
    public string Id { get; } = id;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string ResourceFile { get; } = file;
    public string Effect { get; } = effect;
    public ImageSource Thumbnail { get; } = effect == "aurora" ? CreateAurora() : CreateThumbnail(file);

    private static ImageSource CreateAurora()
    {
        var drawing = new DrawingGroup();
        drawing.Children.Add(new GeometryDrawing(new LinearGradientBrush(
            Color.FromRgb(7, 20, 42), Color.FromRgb(31, 10, 65), 30), null,
            new RectangleGeometry(new System.Windows.Rect(0, 0, 420, 236))));
        drawing.Children.Add(new GeometryDrawing(new RadialGradientBrush(
            Color.FromArgb(220, 12, 170, 180), Colors.Transparent), null,
            new EllipseGeometry(new System.Windows.Rect(0, 30, 300, 160))));
        drawing.Children.Add(new GeometryDrawing(new RadialGradientBrush(
            Color.FromArgb(190, 138, 80, 250), Colors.Transparent), null,
            new EllipseGeometry(new System.Windows.Rect(180, 25, 260, 200))));
        drawing.Freeze(); var image = new DrawingImage(drawing); image.Freeze(); return image;
    }

    private static ImageSource CreateThumbnail(string file)
    {
        var bitmap = new BitmapImage();
        bitmap.BeginInit(); bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 420;
        bitmap.UriSource = new Uri("pack://application:,,,/Assets/" + file);
        bitmap.EndInit(); bitmap.Freeze(); return bitmap;
    }
}

internal static class ThemeCatalog
{
    internal static IReadOnlyList<ThemeDefinitions.Definition> Definitions => ThemeDefinitions.Definitions;
    internal static List<ThemePreset> Create() => Definitions
        .Select(x => new ThemePreset(x.Id, x.Name, x.Description, x.File, x.Effect)).ToList();
}
