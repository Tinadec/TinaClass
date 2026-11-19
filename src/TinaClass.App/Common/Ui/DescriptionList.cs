using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Media;
using TKCode.Arc;

namespace TinaClass.App.Common.Ui;

/// <summary>
/// A read-only list of "label, value" rows (approval arguments, run details, a search hit's facts). Hand-rolled: Arc's <c>PropertyGrid</c>
/// edits the properties of an object and groups them; this is plain display of pairs (see "UI components" in docs/ARCHITECTURE.md).
/// <see cref="ItemsControl.ItemsSource"/> takes <c>KeyValuePair&lt;string, string&gt;</c> items. Values wrap; labels keep a fixed column.
/// </summary>
public sealed class DescriptionList : ItemsControl
{
    public static readonly StyledProperty<double> LabelWidthProperty = AvaloniaProperty.Register<DescriptionList, double>(nameof(LabelWidth), 84);

    public DescriptionList()
    {
        Classes.Add("description-list");
        ItemTemplate = new FuncDataTemplate<KeyValuePair<string, string>>((pair, _) =>
        {
            var label = new TextBlock { Text = pair.Key, VerticalAlignment = VerticalAlignment.Top };
            label.Classes.Add("muted");
            var value = new TextBlock { Text = pair.Value, TextWrapping = TextWrapping.Wrap };
            var row = new Grid { ColumnSpacing = 12, Margin = new Thickness(0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition(LabelWidth, GridUnitType.Pixel));
            row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(value, 1);
            row.Children.Add(label);
            row.Children.Add(value);
            return row;
        });
    }

    public double LabelWidth
    {
        get => GetValue(LabelWidthProperty);
        set => SetValue(LabelWidthProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(ItemsControl);
}
