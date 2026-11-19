using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TinaClass.App.Common.Shell;

/// <summary>Converters of the breadcrumb: the last step is medium weight, the earlier ones regular.</summary>
public static class Crumbs
{
    public static IValueConverter Weight { get; } = new FuncValueConverter<bool, FontWeight>(last => last ? FontWeight.Medium : FontWeight.Normal);
}
