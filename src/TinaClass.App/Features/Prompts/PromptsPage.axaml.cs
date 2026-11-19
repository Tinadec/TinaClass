using Avalonia.Controls;
using Avalonia.Input;
using TinaClass.App.Common.Sample;
using TinaClass.App.Features.Prompts.Localization;
using TinaClass.Contracts.Prompts;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Prompts;

/// <summary>
/// The prompts page: <c>PromptStudio</c> shows the chosen prompt. This code gives it the texts, versions and test cases of the view model, the
/// words of the app's language (<see cref="Labels"/>) and a run handler that calls the service and streams the answer.
/// </summary>
public partial class PromptsPage : UserControl
{
    private PromptsViewModel? _viewModel;
    private bool _filling;

    public PromptsPage()
    {
        InitializeComponent();
        Studio.RunHandler = RunAsync;
        Studio.Labels = Labels();
        Studio.TemplateChanged += (_, _) =>
        {
            if (!_filling)
            {
                _viewModel?.SetDraft(Studio.Draft.System, Studio.Draft.User);
            }
        };
        ArcLanguage.Changed += (_, _) =>
        {
            Studio.Labels = Labels();
            SearchBox.Placeholder = PromptsLangResources.Get(PromptsLangResourceKind.Search);
            _viewModel?.RefreshTexts();
        };
        SearchBox.Placeholder = PromptsLangResources.Get(PromptsLangResourceKind.Search);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.S && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Control && _viewModel is { } vm)
        {
            _ = vm.SaveAsync();
            e.Handled = true;
        }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is PromptsViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.DetailLoaded += (_, _) => Show(vm);
            vm.PublishRequested += async (_, _) => await PublishAsync(vm);
            vm.NewRequested += async (_, _) => await NewAsync(vm);
        }
    }

    /// <summary>Gives the studio the chosen prompt: title, texts, variables, versions (the newest is in production) and test cases.</summary>
    private void Show(PromptsViewModel vm)
    {
        if (vm.Selected?.Dto is not { } prompt)
        {
            return;
        }
        _filling = true;
        Studio.Now = SampleClock.Now;
        Studio.Title = prompt.Name;
        if (Studio.Models.Count == 0)
        {
            foreach (var provider in vm.Chat.Providers)
            {
                Studio.Providers.Add(provider);
            }
            foreach (var model in vm.Chat.Models)
            {
                Studio.Models.Add(model);
            }
            Studio.SelectedModel = Studio.Models.FirstOrDefault(m => m.Id == vm.Chat.DefaultModelId) ?? Studio.Models.FirstOrDefault();
        }
        Studio.Versions.Clear();
        foreach (var v in vm.Versions.OrderBy(v => v.Number))
        {
            Studio.Versions.Add(new PromptVersion
            {
                Id = v.Id.ToString(),
                Label = PromptsLangResources.Format(PromptsLangResourceKind.Version, v.Number),
                Message = v.Note,
                Author = new PromptAuthor { Name = v.CreatedByName },
                CreatedAt = v.CreatedAt,
                System = v.SystemText,
                User = v.UserText,
            });
        }
        Studio.ProductionVersion = Studio.Versions.LastOrDefault()!;
        Studio.Variables.Clear();
        foreach (var variable in prompt.Variables)
        {
            Studio.Variables.Add(variable);
        }
        Studio.TestCases.Clear();
        foreach (var c in vm.Cases)
        {
            var test = new PromptTestCase { Id = c.Id.ToString(), Label = c.Name };
            foreach (var (key, value) in SamplePromptsService.ParseValues(c.Input))
            {
                test.Values[key] = value;
            }
            Studio.TestCases.Add(test);
        }
        Studio.SelectedTestCase = Studio.TestCases.FirstOrDefault()!;
        Studio.Draft = new PromptTemplate(prompt.SystemText, prompt.UserText);
        _filling = false;
    }

    /// <summary>The trial run: the service answers (the sample's text, or the provider's through the API) and the text streams in a few characters at a time.</summary>
    private async IAsyncEnumerable<string> RunAsync(PromptRunRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        if (_viewModel?.Selected is not { } row)
        {
            yield break;
        }
        var response = await _viewModel.Service.RunAsync(row.Id, request.Values, request.Model?.Id, cancellation);
        for (var i = 0; i < response.Output.Length; i += 6)
        {
            yield return response.Output.Substring(i, Math.Min(6, response.Output.Length - i));
            await Task.Delay(SampleChatDelay, cancellation);
        }
    }

    private static int SampleChatDelay => Features.Chat.SampleChatService.Speed > 0 ? 18 : 0;

    private async Task PublishAsync(PromptsViewModel vm)
    {
        var note = new Input { Label = PromptsLangResources.Get(PromptsLangResourceKind.PublishNote), Text = PromptsLangResources.Get(PromptsLangResourceKind.PublishDefault) };
        var result = await Dialog.ShowAsync(this, PromptsLangResources.Get(PromptsLangResourceKind.PublishTitle), new StackPanel { MinWidth = 360, Children = { note } }, null,
            new DialogAction(PromptsLangResources.Get(PromptsLangResourceKind.Cancel), false, ButtonVariant.Secondary),
            new DialogAction(PromptsLangResources.Get(PromptsLangResourceKind.Publish), true, ButtonVariant.Primary));
        if (result is true)
        {
            await vm.PublishAsync(note.Text?.Trim() ?? string.Empty);
        }
    }

    private async Task NewAsync(PromptsViewModel vm)
    {
        var name = new Input { Label = PromptsLangResources.Get(PromptsLangResourceKind.NewName), Text = PromptsLangResources.Get(PromptsLangResourceKind.NewName) };
        var command = new Input { Label = "/", Text = PromptsLangResources.Get(PromptsLangResourceKind.NewCommand) };
        var result = await Dialog.ShowAsync(this, PromptsLangResources.Get(PromptsLangResourceKind.New), new StackPanel { MinWidth = 360, Spacing = 12, Children = { name, command } }, null,
            new DialogAction(PromptsLangResources.Get(PromptsLangResourceKind.Cancel), false, ButtonVariant.Secondary),
            new DialogAction(PromptsLangResources.Get(PromptsLangResourceKind.New), true, ButtonVariant.Primary));
        if (result is true && !string.IsNullOrWhiteSpace(name.Text))
        {
            await vm.CreateAsync(name.Text.Trim(), command.Text?.Trim() ?? "/new");
        }
    }

    /// <summary>PromptStudio's words in the app's language (the rest keep Arc's English).</summary>
    private static PromptStudioLabels Labels()
    {
        string L(PromptsLangResourceKind kind) => PromptsLangResources.Get(kind);
        string F(PromptsLangResourceKind kind, object value) => PromptsLangResources.Format(kind, value);
        return new PromptStudioLabels
        {
            SystemTab = L(PromptsLangResourceKind.LSystem),
            UserTab = L(PromptsLangResourceKind.LUser),
            SystemPlaceholder = L(PromptsLangResourceKind.LSystemPlaceholder),
            UserPlaceholder = L(PromptsLangResourceKind.LUserPlaceholder),
            TypeToAddBefore = L(PromptsLangResourceKind.LTypeBefore),
            TypeToAddAfter = L(PromptsLangResourceKind.LTypeAfter),
            Tokens = count => F(PromptsLangResourceKind.LTokens, count),
            EmptyValue = L(PromptsLangResourceKind.LEmptyValue),
            VariablesName = L(PromptsLangResourceKind.LVariables),
            TestCasesName = L(PromptsLangResourceKind.LTestCase),
            Run = L(PromptsLangResourceKind.LRun),
            Stop = L(PromptsLangResourceKind.LStop),
            Retry = L(PromptsLangResourceKind.LRetry),
            Idle = label => F(PromptsLangResourceKind.LIdle, label),
            IdleNoCase = L(PromptsLangResourceKind.LIdleNoCase),
            RunsAreSimulated = L(PromptsLangResourceKind.LSimulatedReply),
            Thinking = L(PromptsLangResourceKind.LThinking),
            Writing = L(PromptsLangResourceKind.LWriting),
            Reply = L(PromptsLangResourceKind.LReply),
            Stopped = L(PromptsLangResourceKind.LStopped),
            RunFailed = L(PromptsLangResourceKind.LRunFailed),
            CopyReply = L(PromptsLangResourceKind.LCopy),
            Copied = L(PromptsLangResourceKind.LCopied),
            Production = L(PromptsLangResourceKind.LProduction),
            Latest = L(PromptsLangResourceKind.LLatest),
            VersionsName = L(PromptsLangResourceKind.LVersions),
            CompareWith = label => F(PromptsLangResourceKind.LCompareWith, label),
            Promote = L(PromptsLangResourceKind.LPromote),
            Undo = L(PromptsLangResourceKind.LUndo),
            BackToEditing = L(PromptsLangResourceKind.LBackToEditing),
            Added = L(PromptsLangResourceKind.LAdded),
            Removed = L(PromptsLangResourceKind.LRemoved),
            ModelsGroup = L(PromptsLangResourceKind.LModel),
            EffortTitle = L(PromptsLangResourceKind.LEffort),
            DaysAgo = days => days switch
            {
                0 => L(PromptsLangResourceKind.LToday),
                1 => L(PromptsLangResourceKind.LYesterday),
                _ => F(PromptsLangResourceKind.LDaysAgo, days),
            },
            LastWeek = L(PromptsLangResourceKind.LLastWeek),
            WeeksAgo = weeks => F(PromptsLangResourceKind.LWeeksAgo, weeks),
            DoneFooter = (seconds, tokens, cost, simulated) => string.Join(" · ", new[]
            {
                simulated ? L(PromptsLangResourceKind.LSimulated) : null,
                F(PromptsLangResourceKind.LSecondsFormat, seconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)),
                F(PromptsLangResourceKind.LTokens, tokens),
                cost,
            }.Where(part => part is not null)),
        };
    }
}
