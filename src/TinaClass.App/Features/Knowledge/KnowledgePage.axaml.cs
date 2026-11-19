using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using TinaClass.App.Features.Knowledge.Localization;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Knowledge;

/// <summary>
/// The knowledge page. Arc's <c>FileDropzone</c> adds the files (its rows show the upload and then the indexing progress), the
/// <c>Table</c> lists the documents with a <c>Badge</c> for the state and a <c>Progress</c> while one is indexing.
/// </summary>
public partial class KnowledgePage : UserControl
{
    private const long MaxTextBytes = 1_000_000;
    private KnowledgeViewModel? _viewModel;

    public KnowledgePage()
    {
        InitializeComponent();
        ArcLanguage.Changed += (_, _) =>
        {
            Fill();
            _viewModel?.RefreshTexts();
        };
        Dropzone.UploadHandler = UploadAsync;
        Fill();
        // The table sits in a panel that is hidden until a base is chosen; Arc's Table fixes its column widths when the columns are set, so
        // they are set again once the table has a width (otherwise the star columns collapse to their minimum).
        var filledAt = 0.0;
        DocTable.SizeChanged += (_, e) =>
        {
            if (e.NewSize.Width > 0 && Math.Abs(e.NewSize.Width - filledAt) > 0.5 && filledAt == 0)
            {
                filledAt = e.NewSize.Width;
                Fill();
            }
        };
        SizeChanged += (_, e) =>
        {
            // Design 9: under 1100 px the properties column is dropped (the window is wider than this page by the sidebar).
            var narrow = e.NewSize.Width < 860;
            Columns.ColumnDefinitions[2].Width = narrow ? new GridLength(0) : new GridLength(288);
            Side.IsVisible = !narrow && (_viewModel?.HasBase ?? false);
        };
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.U && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control)
        {
            _ = Dropzone.BrowseAsync();
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is KnowledgeViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.UploadRequested += (_, _) => _ = Dropzone.BrowseAsync();
            vm.NewBaseRequested += async (_, _) => await NewBaseAsync();
            TestBox.SearchSubmitted += (_, _) => vm.SearchCommand.Execute(null);
        }
    }

    private void Fill()
    {
        string K(KnowledgeLangResourceKind kind) => KnowledgeLangResources.Get(kind);
        Dropzone.Label = K(KnowledgeLangResourceKind.Upload);
        Dropzone.DropLabel = K(KnowledgeLangResourceKind.DropLabel);
        Dropzone.Description = K(KnowledgeLangResourceKind.DropNote);
        Dropzone.Note = K(KnowledgeLangResourceKind.DropLimit);
        Dropzone.MaxFileSize = 25_000_000;
        FilterBox.Placeholder = K(KnowledgeLangResourceKind.Filter);
        DocTable.Columns.Clear();
        DocTable.Columns.Add(new DataTableColumn { Header = K(KnowledgeLangResourceKind.ColName), Width = new GridLength(2.2, GridUnitType.Star), CellTemplate = new FuncDataTemplate<DocRow>((row, _) => NameCell(row)) });
        DocTable.Columns.Add(new DataTableColumn { Header = K(KnowledgeLangResourceKind.ColState), Width = new GridLength(1.3, GridUnitType.Star), CellTemplate = new FuncDataTemplate<DocRow>((row, _) => StateCell(row)) });
        DocTable.Columns.Add(new DataTableColumn(K(KnowledgeLangResourceKind.ColSize), o => ((DocRow)o).Size) { Width = new GridLength(84) });
        DocTable.Columns.Add(new DataTableColumn(K(KnowledgeLangResourceKind.ColChunks), o => ((DocRow)o).Chunks) { Width = new GridLength(88) });
        DocTable.Columns.Add(new DataTableColumn { Header = string.Empty, Key = "actions", Width = new GridLength(80), CellTemplate = new FuncDataTemplate<DocRow>((row, _) => ActionsCell(row)) });
    }

    private static Control NameCell(DocRow? row)
    {
        if (row is null)
        {
            return new Panel();
        }
        var icon = new LucideIcon { Kind = row.Icon, Size = 16, VerticalAlignment = VerticalAlignment.Center };
        var name = new TextBlock { Text = row.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(name, row.Name);
        var by = new TextBlock { Text = row.By + " · " + row.When, FontSize = 12 };
        by.Classes.Add("muted");
        var stack = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { name, by } };
        var grid = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(1, GridUnitType.Star) } };
        Grid.SetColumn(stack, 1);
        grid.Children.Add(icon);
        grid.Children.Add(stack);
        return grid;
    }

    /// <summary>The state badge; while a document is indexing its stage text changes with the progress and a thin bar fills.</summary>
    private static Control StateCell(DocRow? row)
    {
        if (row is null)
        {
            return new Panel();
        }
        var badge = new Badge { HorizontalAlignment = HorizontalAlignment.Left, Content = row.StateText, Tone = row.Tone };
        var bar = new Progress { Maximum = 100, Value = row.Progress, IsValueVisible = false, IsVisible = row.InProgress, MinWidth = 96 };
        var reason = new TextBlock { Text = row.Error, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, IsVisible = row.IsFailed && !string.IsNullOrEmpty(row.Error) };
        reason.Classes.Add("muted");
        ToolTip.SetTip(reason, row.Error);
        row.PropertyChanged += (_, _) =>
        {
            badge.Content = row.StateText;
            badge.Tone = row.Tone;
            bar.Value = row.Progress;
            bar.IsVisible = row.InProgress;
            reason.Text = row.Error;
            reason.IsVisible = row.IsFailed && !string.IsNullOrEmpty(row.Error);
        };
        return new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { badge, bar, reason } };
    }

    private Control ActionsCell(DocRow? row)
    {
        if (row is null)
        {
            return new Panel();
        }
        var retry = new TKCode.Arc.Button { Variant = ButtonVariant.Ghost, Classes = { "compact", "icon-only" }, Content = new LucideIcon { Kind = LucideIconKind.RotateCw, Size = 16 }, IsVisible = row.IsFailed };
        ToolTip.SetTip(retry, KnowledgeLangResources.Get(KnowledgeLangResourceKind.Retry));
        retry.Click += (_, _) => _viewModel?.RetryCommand.Execute(row);
        var delete = new TKCode.Arc.Button { Variant = ButtonVariant.Ghost, Classes = { "compact", "icon-only" }, Content = new LucideIcon { Kind = LucideIconKind.Trash2, Size = 16 } };
        ToolTip.SetTip(delete, KnowledgeLangResources.Get(KnowledgeLangResourceKind.Delete));
        delete.Click += async (_, _) => await DeleteAsync(row);
        row.PropertyChanged += (_, _) => retry.IsVisible = row.IsFailed;
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right, Children = { retry, delete } };
    }

    private async Task DeleteAsync(DocRow row)
    {
        var ok = await Dialog.ConfirmAsync(this, KnowledgeLangResources.Get(KnowledgeLangResourceKind.Delete), KnowledgeLangResources.Format(KnowledgeLangResourceKind.ConfirmDelete, row.Name),
            KnowledgeLangResources.Get(KnowledgeLangResourceKind.Delete), KnowledgeLangResources.Get(KnowledgeLangResourceKind.Cancel), isDanger: true);
        if (ok && _viewModel is { } vm)
        {
            await vm.DeleteAsync(row);
        }
    }

    private async Task NewBaseAsync()
    {
        string K(KnowledgeLangResourceKind kind) => KnowledgeLangResources.Get(kind);
        var name = new Input { Label = K(KnowledgeLangResourceKind.NewBaseName) };
        var what = new Input { Label = K(KnowledgeLangResourceKind.NewBaseDescription) };
        var body = new StackPanel { Spacing = 12, MinWidth = 360, Children = { name, what } };
        var result = await Dialog.ShowAsync(this, K(KnowledgeLangResourceKind.NewBaseTitle), body, null,
            new DialogAction(K(KnowledgeLangResourceKind.Cancel), false, ButtonVariant.Secondary), new DialogAction(K(KnowledgeLangResourceKind.Create), true, ButtonVariant.Primary));
        if (result is true && !string.IsNullOrWhiteSpace(name.Text) && _viewModel is { } vm)
        {
            await vm.CreateBaseAsync(name.Text.Trim(), what.Text?.Trim() ?? string.Empty);
        }
    }

    /// <summary>
    /// The dropzone's upload: reads the text of small text files, adds the document and follows its indexing so that the file row's bar is
    /// the pipeline's progress (upload, parse, embed); a document that fails turns the row red with the reason.
    /// </summary>
    private async Task UploadAsync(FileDropzoneItem item, IProgress<double> progress, CancellationToken cancellation)
    {
        if (_viewModel is not { } vm)
        {
            return;
        }
        string? text = null;
        var extension = Path.GetExtension(item.Name).ToLowerInvariant();
        if (item.File is { } file && item.Size <= MaxTextBytes && extension is ".md" or ".txt" or ".csv")
        {
            await using var stream = await file.OpenReadAsync();
            using var reader = new StreamReader(stream);
            text = await reader.ReadToEndAsync(cancellation);
        }
        var row = await vm.AddAsync(item.Name, MimeOf(extension), item.Size, text) ?? throw new InvalidOperationException(KnowledgeLangResources.Get(KnowledgeLangResourceKind.UploadFailed));
        while (row.InProgress)
        {
            progress.Report(row.Progress);
            await Task.Delay(300, cancellation);
        }
        if (row.IsFailed)
        {
            throw new InvalidOperationException(row.Error ?? KnowledgeLangResources.Get(KnowledgeLangResourceKind.UploadFailed));
        }
        progress.Report(100);
    }

    private static string MimeOf(string extension) => extension switch
    {
        ".pdf" => "application/pdf",
        ".md" => "text/markdown",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".docx" or ".doc" => "application/msword",
        ".xlsx" or ".xls" => "application/vnd.ms-excel",
        _ => "application/octet-stream",
    };
}
