using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Features.Chat;
using TinaClass.App.Features.Prompts.Localization;
using TinaClass.Contracts.Prompts;
using TKCode.Arc.Icons.Lucide;

namespace TinaClass.App.Features.Prompts;

/// <summary>A prompt in the list: its name, its slash command and how many test cases pass.</summary>
public sealed class PromptRow(PromptDto dto) : ObservableObject
{
    public PromptDto Dto { get; private set; } = dto;

    public Guid Id => Dto.Id;

    public string Name => Dto.Name;

    public string Command => Dto.Command;

    public bool HasCases => Dto.CasesTotal > 0;

    public string CasesText => PromptsLangResources.Format(PromptsLangResourceKind.CasesBadge, Dto.CasesPassed, Dto.CasesTotal);

    public bool CasesAllPass => Dto.CasesTotal > 0 && Dto.CasesPassed == Dto.CasesTotal;

    public string Meta => PromptsLangResources.Format(PromptsLangResourceKind.Version, Dto.CurrentVersion) + " · " + PromptsLangResources.Format(PromptsLangResourceKind.Uses, Dto.UseCount);

    public void Update(PromptDto dto)
    {
        Dto = dto;
        OnPropertyChanged(string.Empty);
    }

    public void Refresh() => OnPropertyChanged(string.Empty);
}

/// <summary>
/// The prompts page: the list and the chosen prompt in Arc's <c>PromptStudio</c>. The view model loads the versions and test cases of the chosen prompt
/// (<see cref="DetailLoaded"/>), keeps the edited texts (<see cref="SetDraft"/>) and saves, publishes and runs the test cases from the top bar.
/// </summary>
public sealed partial class PromptsViewModel(IPromptsService service, ChatViewModel chat) : PageViewModel
{
    public ObservableCollection<PromptRow> Items { get; } = [];

    public IPromptsService Service => service;

    public ChatViewModel Chat => chat;

    [ObservableProperty]
    private PromptRow? _selected;

    [ObservableProperty]
    private string _query = string.Empty;

    [ObservableProperty]
    private string? _casesResult;

    [ObservableProperty]
    private bool _casesPass;

    public IReadOnlyList<PromptVersionDto> Versions { get; private set; } = [];

    public IReadOnlyList<PromptCaseDto> Cases { get; private set; } = [];

    public bool HasNoPrompts => Items.Count == 0 && !IsLoading;

    public bool HasSelection => Selected is not null;

    private string _draftSystem = string.Empty;
    private string _draftUser = string.Empty;
    private bool _dirty;

    /// <summary>Raised when the versions and cases of the chosen prompt are loaded, and the studio shows them.</summary>
    public event EventHandler? DetailLoaded;

    /// <summary>Raised for "Publish": the page asks for the note, then calls <see cref="PublishAsync"/>.</summary>
    public event EventHandler? PublishRequested;

    /// <summary>Raised for "New prompt": the page asks for the name.</summary>
    public event EventHandler? NewRequested;

    protected override async Task LoadAsync()
    {
        try
        {
            await chat.ActivateAsync();
            await ReloadAsync(Selected?.Id);
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    private async Task ReloadAsync(Guid? select)
    {
        var prompts = await service.ListAsync(Query);
        Items.Clear();
        foreach (var p in prompts)
        {
            Items.Add(new PromptRow(p));
        }
        OnPropertyChanged(nameof(HasNoPrompts));
        Selected = Items.FirstOrDefault(p => p.Id == select) ?? Items.FirstOrDefault();
    }

    partial void OnQueryChanged(string value) => _ = ReloadAsync(Selected?.Id);

    partial void OnSelectedChanged(PromptRow? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        CasesResult = null;
        _dirty = false;
        RebuildBar();
        _ = LoadDetailAsync();
    }

    private async Task LoadDetailAsync()
    {
        if (Selected is not { } selected)
        {
            return;
        }
        try
        {
            Versions = await service.VersionsAsync(selected.Id);
            Cases = await service.CasesAsync(selected.Id);
            _draftSystem = selected.Dto.SystemText;
            _draftUser = selected.Dto.UserText;
            DetailLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>The studio reports the texts being edited.</summary>
    public void SetDraft(string system, string user)
    {
        if (Selected is null)
        {
            return;
        }
        _dirty = system != Selected.Dto.SystemText || user != Selected.Dto.UserText;
        _draftSystem = system;
        _draftUser = user;
        RebuildBar();
    }

    private void RebuildBar()
    {
        Crumbs = Selected is { } s ? [s.Name] : [];
        BarNote = Selected is null ? null
            : _dirty ? PromptsLangResources.Get(PromptsLangResourceKind.Unsaved)
            : PromptsLangResources.Format(PromptsLangResourceKind.PublishedNote, Selected.Dto.CurrentVersion);
        Actions = Selected is null
            ? [new PageAction(() => PromptsLangResources.Get(PromptsLangResourceKind.New), new RelayCommand(() => NewRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Plus, IsPrimary: true)]
            :
            [
                new PageAction(() => PromptsLangResources.Get(PromptsLangResourceKind.New), new RelayCommand(() => NewRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Plus),
                new PageAction(() => PromptsLangResources.Get(PromptsLangResourceKind.RunCases), RunCasesCommand, LucideIconKind.ListChecks),
                new PageAction(() => PromptsLangResources.Get(PromptsLangResourceKind.Save), SaveCommand, LucideIconKind.Save),
                new PageAction(() => PromptsLangResources.Get(PromptsLangResourceKind.Publish), new RelayCommand(() => PublishRequested?.Invoke(this, EventArgs.Empty)), LucideIconKind.Rocket, IsPrimary: true),
            ];
    }

    [RelayCommand]
    public async Task SaveAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }
        try
        {
            row.Update(await service.SaveAsync(row.Dto, _draftSystem, _draftUser));
            _dirty = false;
            RebuildBar();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public async Task PublishAsync(string note)
    {
        if (Selected is not { } row)
        {
            return;
        }
        try
        {
            if (_dirty)
            {
                await SaveAsync();
            }
            row.Update(await service.PublishAsync(row.Id, note));
            await LoadDetailAsync();
            RebuildBar();
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private async Task RunCasesAsync()
    {
        if (Selected is not { } row)
        {
            return;
        }
        try
        {
            var result = await service.RunCasesAsync(row.Id);
            CasesPass = result.Passed == result.Total;
            CasesResult = PromptsLangResources.Format(PromptsLangResourceKind.CasesPassed, result.Passed, result.Total);
            row.Update((await service.ListAsync(null)).First(p => p.Id == row.Id));
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    public async Task CreateAsync(string name, string command)
    {
        try
        {
            var created = await service.CreateAsync(name, command);
            await ReloadAsync(created.Id);
        }
        catch (ServiceException e)
        {
            Error = e.Message;
        }
    }

    /// <summary>The words follow the language.</summary>
    public void RefreshTexts()
    {
        foreach (var row in Items)
        {
            row.Refresh();
        }
        RebuildBar();
    }
}
