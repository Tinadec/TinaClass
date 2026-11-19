using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Layout;
using TinaClass.App.Features.Models.Localization;
using TKCode.Arc;
using TKCode.Arc.Localization;

namespace TinaClass.App.Features.Models;

/// <summary>
/// The models page: provider rows, Arc's <c>ProviderKeys</c> (key entry, reveal and test for the providers that need a key) and an Arc
/// <c>Table</c> of models. The keys control and the table are filled here from the view model once it has loaded.
/// </summary>
public partial class ModelsPage : UserControl
{
    private ModelsViewModel? _viewModel;

    public ModelsPage()
    {
        InitializeComponent();
        ArcLanguage.Changed += (_, _) =>
        {
            BuildColumns();
            _viewModel?.RefreshTexts();
        };
        Keys.Labels = KeyLabels();
        ArcLanguage.Changed += (_, _) => Keys.Labels = KeyLabels();
        Keys.KeySaved += async (_, e) =>
        {
            if (_viewModel is not null && Guid.TryParse(e.Provider, out var id))
            {
                await _viewModel.SetKeyAsync(id, e.Key);
            }
        };
        Keys.KeyRemoved += async (_, e) =>
        {
            if (_viewModel is not null && Guid.TryParse(e.Provider, out var id))
            {
                await _viewModel.ClearKeyAsync(id);
            }
        };
        Keys.TestKey = async request =>
        {
            if (_viewModel is null || !Guid.TryParse(request.Provider, out var id))
            {
                return ProviderKeyTestResult.Fail(string.Empty);
            }
            var result = await _viewModel.Service.TestAsync(id, request.CancellationToken);
            return result.Ok ? ProviderKeyTestResult.Ok(string.Format(ModelsLangResources.Get(ModelsLangResourceKind.Latency), result.LatencyMs))
                : ProviderKeyTestResult.Fail(ModelsLangResources.Get(ModelsLangResourceKind.ErrorKeyRequired));
        };
        BuildColumns();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ModelsViewModel vm && vm != _viewModel)
        {
            _viewModel = vm;
            vm.PropertyChanged += (_, p) =>
            {
                if (p.PropertyName == nameof(ModelsViewModel.IsLoading) && !vm.IsLoading)
                {
                    FillKeys(vm);
                }
            };
            if (vm.Providers.Count > 0)
            {
                FillKeys(vm);
            }
        }
    }

    private void FillKeys(ModelsViewModel vm)
    {
        var needKey = vm.Providers.Where(p => p.Dto.RequiresKey).ToList();
        Keys.Providers = [.. needKey.Select(p => new ProviderKeyProvider { Id = p.Id.ToString(), Name = p.Name, Monogram = p.Monogram, MinimumLength = 8 })];
        Keys.Keys = [.. needKey.Select(p => new ProviderKey
        {
            Provider = p.Id.ToString(),
            Status = p.Dto.KeySet ? ProviderKeyStatus.Connected : ProviderKeyStatus.Unset,
            Last4 = p.Dto.KeyHint is { Length: >= 4 } hint ? hint[^4..] : null,
        })];
    }

    private void BuildColumns()
    {
        var text = (ModelsLangResourceKind kind) => ModelsLangResources.Get(kind);
        ModelTable.Columns.Clear();
        ModelTable.Columns.Add(new DataTableColumn(text(ModelsLangResourceKind.ColModel), o => ((ModelRow)o).Name) { Width = new GridLength(2, GridUnitType.Star), IsNumeric = false });
        ModelTable.Columns.Add(new DataTableColumn(text(ModelsLangResourceKind.ColProvider), o => ((ModelRow)o).Provider) { Width = new GridLength(1.4, GridUnitType.Star), IsNumeric = false });
        ModelTable.Columns.Add(new DataTableColumn(text(ModelsLangResourceKind.ColContext), o => ((ModelRow)o).Context) { Width = new GridLength(90) });
        ModelTable.Columns.Add(new DataTableColumn(text(ModelsLangResourceKind.ColPrice), o => ((ModelRow)o).Price) { Width = new GridLength(2, GridUnitType.Star), IsNumeric = false });
        ModelTable.Columns.Add(new DataTableColumn { Header = text(ModelsLangResourceKind.ColDefault), Width = new GridLength(120), CellTemplate = new FuncDataTemplate<ModelRow>((row, _) =>
        {
            if (row is null)
            {
                return new Panel();
            }
            if (row.IsDefault)
            {
                return new Badge { Content = text(ModelsLangResourceKind.Default), Tone = BadgeTone.Info, HorizontalAlignment = HorizontalAlignment.Left };
            }
            var button = new TKCode.Arc.Button { Content = text(ModelsLangResourceKind.Default), Variant = ButtonVariant.Text, Size = ControlSize.Small, HorizontalAlignment = HorizontalAlignment.Left };
            button.Click += async (_, _) => await row.MakeDefaultAsync();
            return button;
        }) });
        ModelTable.Columns.Add(new DataTableColumn { Header = text(ModelsLangResourceKind.ColEnabled), Width = new GridLength(80), CellTemplate = new FuncDataTemplate<ModelRow>((row, _) =>
        {
            var toggle = new Switch { HorizontalAlignment = HorizontalAlignment.Left };
            toggle.Bind(Switch.IsCheckedProperty, new Binding(nameof(ModelRow.IsEnabled)) { Source = row, Mode = BindingMode.TwoWay });
            return toggle;
        }) });
    }

    /// <summary>The key list's words in the app's language (what is not set keeps Arc's English).</summary>
    private static ProviderKeysLabels KeyLabels()
    {
        static string L(ModelsLangResourceKind kind) => ModelsLangResources.Get(kind);
        return new ProviderKeysLabels
        {
            Connected = L(ModelsLangResourceKind.KConnected),
            Invalid = L(ModelsLangResourceKind.KInvalid),
            NotSet = L(ModelsLangResourceKind.KNotSet),
            Testing = L(ModelsLangResourceKind.KTesting),
            NoKey = L(ModelsLangResourceKind.KNoKey),
            KeyRemoved = L(ModelsLangResourceKind.KRemoved),
            NeverUsed = L(ModelsLangResourceKind.KNeverUsed),
            Rejected = L(ModelsLangResourceKind.KRejected),
            AddKey = L(ModelsLangResourceKind.KAdd),
            Undo = L(ModelsLangResourceKind.KUndo),
            Cancel = L(ModelsLangResourceKind.KCancel),
            Save = L(ModelsLangResourceKind.KSave),
        };
    }
}
