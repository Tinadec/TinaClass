using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Ui;
using TinaClass.App.Features.Knowledge.Localization;
using TinaClass.Contracts.Knowledge;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Features.Knowledge;

/// <summary>A knowledge base in the list on the left.</summary>
public sealed class BaseRow(KnowledgeBaseDto dto)
{
    public KnowledgeBaseDto Dto { get; } = dto;

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string Counts => KnowledgeLangResources.Format(KnowledgeLangResourceKind.DocCounts, Dto.DocumentCount.ToString("N0", Text.Culture), Dto.ChunkCount.ToString("N0", Text.Culture));
}

/// <summary>A document row of the table: what it is and how far indexing has come.</summary>
public sealed partial class DocRow : ObservableObject
{
    public DocRow(DocumentDto dto) => Dto = dto;

    public DocumentDto Dto { get; private set; }

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string Size => SizeText.Format(Dto.Size);

    public string Chunks => Dto.ChunkCount == 0 ? "-" : Dto.ChunkCount.ToString("N0", Text.Culture);

    public string By => Dto.UploaderName;

    public string When => TimeText.Short(Dto.CreatedAt);

    public bool InProgress => DocumentStates.InProgress(Dto.State);

    public bool IsFailed => Dto.State == DocumentStates.Failed;

    public double Progress => Dto.Progress;

    public string? Error => Dto.Error;

    public string StateText => KnowledgeLangResources.Get(Dto.State switch
    {
        DocumentStates.Queued => KnowledgeLangResourceKind.StateQueued,
        DocumentStates.Uploading => KnowledgeLangResourceKind.StateUploading,
        DocumentStates.Parsing => KnowledgeLangResourceKind.StateParsing,
        DocumentStates.Chunking => KnowledgeLangResourceKind.StateChunking,
        DocumentStates.Embedding => KnowledgeLangResourceKind.StateEmbedding,
        DocumentStates.Reindexing => KnowledgeLangResourceKind.StateReindexing,
        DocumentStates.Failed => KnowledgeLangResourceKind.StateFailed,
        _ => KnowledgeLangResourceKind.StateReady,
    });

    public BadgeTone Tone => Dto.State switch
    {
        DocumentStates.Ready => BadgeTone.Success,
        DocumentStates.Failed => BadgeTone.Danger,
        DocumentStates.Queued => BadgeTone.Neutral,
        _ => BadgeTone.Info,
    };

    public LucideIconKind Icon => Dto.Mime switch
    {
        "application/pdf" => LucideIconKind.FileText,
        "image/png" or "image/jpeg" => LucideIconKind.Image,
        _ when Dto.Name.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase) => LucideIconKind.Sheet,
        _ => LucideIconKind.File,
    };

    public void Update(DocumentDto dto)
    {
        Dto = dto;
        OnPropertyChanged(string.Empty);
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>A passage the retrieval test found.</summary>
public sealed record HitRow(string Document, string Where, string Text, string Score, double ScoreValue);

/// <summary>
/// The knowledge page: the bases, the documents of the chosen one with their indexing state (it polls while any is in progress, so the
/// stage text and the bar follow), the base's settings and the retrieval test.
/// </summary>
public sealed partial class KnowledgeViewModel : PageViewModel
{
    private readonly IKnowledgeService _service;
    private CancellationTokenSource? _poll;

    public KnowledgeViewModel(IKnowledgeService service)
    {
        _service = service;
        RebuildActions();
    }

    public ObservableCollection<BaseRow> Bases { get; } = [];

    public ObservableCollection<DocRow> Documents { get; } = [];

    public ObservableCollection<HitRow> Hits { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasBase), nameof(Title), nameof(Counts), nameof(Settings))]
    private BaseRow? _selectedBase;

    [ObservableProperty]
    private string _filter = string.Empty;

    [ObservableProperty]
    private string _testQuery = string.Empty;

    [ObservableProperty]
    private string _hitsSummary = string.Empty;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private bool _isSearching;

    public bool HasBase => SelectedBase is not null;

    public bool HasNoBases => Bases.Count == 0 && !IsLoading;

    public bool HasNoDocuments => HasBase && Documents.Count == 0;

    public bool HasNoHits => HasSearched && Hits.Count == 0;

    public string Title => SelectedBase?.Name ?? string.Empty;

    public string Counts => SelectedBase is { } b
        ? KnowledgeLangResources.Format(KnowledgeLangResourceKind.DocCounts, b.Dto.DocumentCount.ToString("N0", Text.Culture), b.Dto.ChunkCount.ToString("N0", Text.Culture))
        : string.Empty;

    public string ShowingText => SelectedBase is { } b && Documents.Count > 0 && Documents.Count < b.Dto.DocumentCount
        ? KnowledgeLangResources.Format(KnowledgeLangResourceKind.ShowingOf, Documents.Count, b.Dto.DocumentCount)
        : string.Empty;

    /// <summary>The settings of the chosen base as label and value pairs (the read-only description list).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Settings
    {
        get
        {
            if (SelectedBase?.Dto is not { } b)
            {
                return [];
            }
            string K(KnowledgeLangResourceKind kind) => KnowledgeLangResources.Get(kind);
            string OnOff(bool value) => K(value ? KnowledgeLangResourceKind.On : KnowledgeLangResourceKind.Off);
            return
            [
                new(K(KnowledgeLangResourceKind.SetEmbedding), b.EmbeddingModelCode),
                new(K(KnowledgeLangResourceKind.SetChunk), KnowledgeLangResources.Format(KnowledgeLangResourceKind.Tokens, b.ChunkSize)),
                new(K(KnowledgeLangResourceKind.SetOverlap), KnowledgeLangResources.Format(KnowledgeLangResourceKind.Tokens, b.ChunkOverlap)),
                new(K(KnowledgeLangResourceKind.SetHybrid), OnOff(b.Hybrid)),
                new(K(KnowledgeLangResourceKind.SetRerank), OnOff(b.Rerank)),
                new(K(KnowledgeLangResourceKind.SetThreshold), (b.ThresholdPercent / 100.0).ToString("0.00", Text.Culture)),
                new(K(KnowledgeLangResourceKind.SetTopK), b.TopK.ToString(Text.Culture)),
                new(K(KnowledgeLangResourceKind.SetVisibility), K(b.Visibility switch
                {
                    "private" => KnowledgeLangResourceKind.VisPrivate,
                    "space" => KnowledgeLangResourceKind.VisSpace,
                    _ => KnowledgeLangResourceKind.VisWorkspace,
                })),
            ];
        }
    }

    /// <summary>Raised when the top bar's "Upload" button is pressed (the page opens the file picker) and for "New knowledge base".</summary>
    public event EventHandler? UploadRequested;

    public event EventHandler? NewBaseRequested;

    public IKnowledgeService Service => _service;

    private void RebuildActions() => Actions =
    [
        new PageAction(() => KnowledgeLangResources.Get(KnowledgeLangResourceKind.Upload), new RelayCommand(() => UploadRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Upload),
        new PageAction(() => KnowledgeLangResources.Get(KnowledgeLangResourceKind.NewBase), new RelayCommand(() => NewBaseRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Plus, IsPrimary: true),
    ];

    protected override async Task LoadAsync()
    {
        try
        {
            await ReloadBasesAsync(SelectedBase?.Id);
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    private async Task ReloadBasesAsync(Guid? select)
    {
        var bases = await _service.BasesAsync();
        Bases.Clear();
        foreach (var b in bases)
        {
            Bases.Add(new BaseRow(b));
        }
        OnPropertyChanged(nameof(HasNoBases));
        SelectedBase = Bases.FirstOrDefault(b => b.Id == select) ?? Bases.FirstOrDefault();
    }

    partial void OnSelectedBaseChanged(BaseRow? value)
    {
        Crumbs = value is null ? [] : [value.Name];
        Hits.Clear();
        HasSearched = false;
        HitsSummary = string.Empty;
        _ = LoadDocumentsAsync();
    }

    partial void OnFilterChanged(string value) => _ = LoadDocumentsAsync();

    private async Task LoadDocumentsAsync()
    {
        _poll?.Cancel();
        if (SelectedBase is not { } selected)
        {
            Documents.Clear();
            OnPropertyChanged(nameof(HasNoDocuments));
            return;
        }
        try
        {
            var documents = await _service.DocumentsAsync(selected.Id, Filter);
            Merge(documents);
            _ = PollAsync(selected.Id);
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>Keeps the existing rows (so their bars animate from where they were) and adds, updates and removes the rest.</summary>
    private void Merge(IReadOnlyList<DocumentDto> documents)
    {
        foreach (var gone in Documents.Where(r => documents.All(d => d.Id != r.Id)).ToList())
        {
            Documents.Remove(gone);
        }
        for (var i = 0; i < documents.Count; i++)
        {
            var existing = Documents.FirstOrDefault(r => r.Id == documents[i].Id);
            if (existing is null)
            {
                Documents.Insert(Math.Min(i, Documents.Count), new DocRow(documents[i]));
            }
            else
            {
                existing.Update(documents[i]);
            }
        }
        OnPropertyChanged(nameof(HasNoDocuments));
        OnPropertyChanged(nameof(ShowingText));
    }

    /// <summary>While any document of the base is in progress, asks again every second and moves the rows along.</summary>
    private async Task PollAsync(Guid baseId)
    {
        var source = _poll = new CancellationTokenSource();
        try
        {
            while (Documents.Any(d => d.InProgress) && !source.IsCancellationRequested)
            {
                await Task.Delay(1000, source.Token);
                var documents = await _service.DocumentsAsync(baseId, Filter, source.Token);
                if (!source.IsCancellationRequested)
                {
                    Merge(documents);
                    RefreshCounts();
                }
            }
        }
        catch (OperationCanceledException)
        {
            // The base or the filter changed; a new poll takes over.
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    private void RefreshCounts()
    {
        OnPropertyChanged(nameof(Counts));
    }

    /// <summary>Adds a document (the upload row of the dropzone, or a test) and starts watching it.</summary>
    public async Task<DocRow?> AddAsync(string name, string mime, long size, string? text)
    {
        if (SelectedBase is not { } selected)
        {
            return null;
        }
        var dto = await _service.AddDocumentAsync(selected.Id, name, mime, size, text);
        var row = new DocRow(dto);
        Documents.Insert(0, row);
        OnPropertyChanged(nameof(HasNoDocuments));
        _poll?.Cancel();
        _ = PollAsync(selected.Id);
        return row;
    }

    [RelayCommand]
    private async Task RetryAsync(DocRow row)
    {
        try
        {
            row.Update(await _service.ReindexAsync(row.Id));
            if (SelectedBase is { } selected)
            {
                _poll?.Cancel();
                _ = PollAsync(selected.Id);
            }
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public async Task DeleteAsync(DocRow row)
    {
        try
        {
            await _service.DeleteDocumentAsync(row.Id);
            Documents.Remove(row);
            OnPropertyChanged(nameof(HasNoDocuments));
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public async Task CreateBaseAsync(string name, string description)
    {
        try
        {
            var created = await _service.CreateBaseAsync(name, description);
            await ReloadBasesAsync(created.Id);
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (SelectedBase is not { } selected || string.IsNullOrWhiteSpace(TestQuery))
        {
            return;
        }
        IsSearching = true;
        try
        {
            var response = await _service.SearchAsync(selected.Id, TestQuery.Trim());
            Hits.Clear();
            foreach (var hit in response.Hits)
            {
                var where = string.Join(" · ", new[] { hit.Heading, hit.Page is { } p ? KnowledgeLangResources.Format(KnowledgeLangResourceKind.Page, p) : null }.Where(s => !string.IsNullOrEmpty(s)));
                Hits.Add(new HitRow(hit.DocumentName, where, hit.Text, KnowledgeLangResources.Format(KnowledgeLangResourceKind.Score, hit.Score.ToString("0.00", Text.Culture)), hit.Score));
            }
            HasSearched = true;
            HitsSummary = KnowledgeLangResources.Format(KnowledgeLangResourceKind.Hits, response.Hits.Count, response.ElapsedMs);
            OnPropertyChanged(nameof(HasNoHits));
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
        finally
        {
            IsSearching = false;
        }
    }

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts()
    {
        RebuildActions();
        foreach (var row in Documents)
        {
            row.Refresh();
        }
        OnPropertyChanged(nameof(Counts));
        OnPropertyChanged(nameof(Settings));
        OnPropertyChanged(nameof(ShowingText));
        if (SelectedBase is { } b)
        {
            Crumbs = [b.Name];
        }
    }
}
