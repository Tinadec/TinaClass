using TinaClass.App.Common.Localization;
using TinaClass.App.Common.Navigation;
using TinaClass.App.Common.Services;
using TinaClass.App.Common.Settings;
using TinaClass.App.Common.Theme;
using TinaClass.App.Features.Chat;
using TinaClass.Contracts.Sample;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TKCode.Arc;
using TKCode.Arc.Icons.Lucide;
using TKCode.Arc.Localization;

namespace TinaClass.App.Common.Shell;

/// <summary>One step of the top bar's breadcrumb.</summary>
public sealed record Crumb(string Text, bool IsLast, bool HasSeparator);

/// <summary>
/// The window: the sidebar, the page shown and the preferences (appearance, language, motion). Implements
/// <see cref="INavigator"/>, so pages move to each other through it.
/// </summary>
public sealed partial class ShellViewModel : ObservableObject, INavigator
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    private AppPage? _currentPage;

    /// <summary>Whether the layout has a sidebar the title bar's button can fold (the shell view sets it).</summary>
    [ObservableProperty]
    private bool _hasSidebar = true;

    /// <summary>The breadcrumb of the top bar: the page, then what the page adds (folder, conversation).</summary>
    [ObservableProperty]
    private IReadOnlyList<Crumb> _barCrumbs = [];

    [ObservableProperty]
    private IReadOnlyList<PageAction> _barActions = [];

    [ObservableProperty]
    private string? _barNote;

    private PageViewModel? _pageModel;

    /// <summary>Raised when something asks for the command palette (Cmd+K, the top bar's button).</summary>
    public event EventHandler? PaletteRequested;

    [RelayCommand]
    private void OpenPalette() => PaletteRequested?.Invoke(this, EventArgs.Empty);

    partial void OnCurrentPageChanged(AppPage? value)
    {
        if (_pageModel is not null)
        {
            _pageModel.PropertyChanged -= OnPageModelChanged;
        }
        _pageModel = value?.Model();
        if (_pageModel is not null)
        {
            _pageModel.PropertyChanged += OnPageModelChanged;
        }
        SyncBar();
    }

    private void OnPageModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PageViewModel.Crumbs) or nameof(PageViewModel.Actions) or nameof(PageViewModel.BarNote))
        {
            SyncBar();
        }
    }

    private void SyncBar()
    {
        var crumbs = new List<string> { CurrentPage?.Label ?? string.Empty };
        crumbs.AddRange(_pageModel?.Crumbs ?? []);
        BarCrumbs = [.. crumbs.Select((text, i) => new Crumb(text, i == crumbs.Count - 1, i > 0))];
        BarActions = [.. _pageModel?.Actions ?? []];
        BarNote = _pageModel?.BarNote;
    }

    /// <summary>Whether the sidebar is folded to its icon rail (Cmd+B / Ctrl+B); remembered between runs.</summary>
    [ObservableProperty]
    private bool _isSidebarCollapsed = UserSettings.Current.IsSidebarCollapsed;

    /// <summary>The sidebar's selected entry: the page shown.</summary>
    [ObservableProperty]
    private string? _selectedKey;

    private readonly ChatHub _hub;
    private readonly ISpendInfo _spend;
    private readonly IReadOnlyList<IBadgeSource> _badges;

    public ShellViewModel(ChatHub hub, ISpendInfo spend, IEnumerable<IBadgeSource> badges)
    {
        _hub = hub;
        _spend = spend;
        _badges = [.. badges];
        foreach (var badge in _badges)
        {
            badge.Changed += (_, _) => BadgesChanged?.Invoke(this, EventArgs.Empty);
        }
        _hub.Changed += (_, _) => ConversationsChanged?.Invoke(this, EventArgs.Empty);
        // Asking for a conversation (a Home card, the palette) or a new one (Home's composer) shows the chat.
        _hub.OpenRequested += (_, id) => ShowChat(id);
        _hub.StartRequested += (_, _) => ShowChat(null);
        // Switching conversations inside the chat moves the sidebar's selection with it.
        _hub.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ChatHub.CurrentId) && CurrentPage?.Key == AppPages.Chat && _hub.CurrentId is { } id && SelectedKey != "c:" + id)
            {
                SelectedKey = "c:" + id;
            }
        };
        ArcLanguage.Changed += (_, _) =>
        {
            SyncBar();
            OnPropertyChanged(nameof(PageTitle));
            OnPropertyChanged(nameof(UserRole));
            OnPropertyChanged(nameof(WorkspaceNote));
            OnPropertyChanged(nameof(AppearanceTip));
            OnPropertyChanged(nameof(IsEnglish));
            OnPropertyChanged(nameof(IsSimplifiedChinese));
            OnPropertyChanged(nameof(IsTraditionalChinese));
        };
        ArcTheme.Manager.StateChanged += (_, e) => Appearance = e.NewState.Appearance;
    }

    private void ShowChat(Guid? id)
    {
        if (CurrentPage?.Key != AppPages.Chat && AppPages.Find(AppPages.Chat) is { } chat)
        {
            SelectedKey = id is null ? chat.Key : "c:" + id;
            CurrentPage = chat;
        }
    }

    /// <summary>The pages, in sidebar order.</summary>
    public IReadOnlyList<AppPage> Pages => AppPages.All;

    public ChatHub Hub => _hub;

    /// <summary>Raised when a badge changed (the sidebar redraws its entries) and when the conversation list changed.</summary>
    public event EventHandler? BadgesChanged;

    public event EventHandler? ConversationsChanged;

    /// <summary>The badge text of a page (null: none).</summary>
    public string? BadgeOf(string key) => _badges.FirstOrDefault(b => b.PageKey == key)?.Text;

    /// <summary>Loads the lists the shell shows around the page: conversations, badges and the month's spend.</summary>
    public async Task LoadAsync()
    {
        await _hub.RefreshAsync();
        foreach (var badge in _badges)
        {
            await badge.RefreshAsync();
        }
        var snapshot = await _spend.CurrentAsync();
        HasSpend = snapshot is not null;
        if (snapshot is not null)
        {
            SpendLimit = snapshot.Limit;
            SpendValue = snapshot.Spent;
            SpendText = Contracts.Common.Money.Format(snapshot.Spent);
            SpendLimitText = Text.Format(AppLangResourceKind.SpendOf, Contracts.Common.Money.Format(snapshot.Limit, 0));
            SpendLabel = Text.Format(AppLangResourceKind.SpendThisMonth, snapshot.MonthLabel);
        }
    }

    [ObservableProperty]
    private bool _hasSpend;

    [ObservableProperty]
    private double _spendValue;

    [ObservableProperty]
    private double _spendLimit = 1;

    [ObservableProperty]
    private string _spendText = string.Empty;

    [ObservableProperty]
    private string _spendLimitText = string.Empty;

    [ObservableProperty]
    private string _spendLabel = string.Empty;

    /// <summary>The company and department line of the sidebar's header (sample data; the API's workspace is the app name).</summary>
    public string WorkspaceName => ApiMode ? AppInfo.Current.Name : SampleCore.Company;

    public string WorkspaceNote => ApiMode ? string.Empty : Text.Get(AppLangResourceKind.WorkspaceNote);

    public string WorkspaceInitial => WorkspaceName.Length > 0 ? WorkspaceName[..1] : "L";

    private static bool ApiMode
    {
        get
        {
            if (Api.ApiSettings.Current.UsesApi)
            {
                return true;
            }
            return false;
        }
    }

    /// <summary>The person signed in: the API's user, or the sample's owner.</summary>
    public string UserName
    {
        get
        {
            if (ApiMode && DesktopApp.Services.GetService(typeof(Api.ApiSession)) is Api.ApiSession { User: { } user })
            {
                return user.Name;
            }
            return SampleCore.People[0].Name;
        }
    }

    public string UserInitial => UserName.Length > 0 ? UserName[..1] : "?";

    public string UserRole
    {
        get
        {
            var role = SampleCore.People[0].Role;
            if (ApiMode && DesktopApp.Services.GetService(typeof(Api.ApiSession)) is Api.ApiSession { User: { } user })
            {
                role = user.RoleId;
            }
            return Text.Get(role switch
            {
                "owner" => AppLangResourceKind.RoleOwner,
                "admin" => AppLangResourceKind.RoleAdmin,
                "viewer" => AppLangResourceKind.RoleViewer,
                _ => AppLangResourceKind.RoleMember,
            });
        }
    }

    /// <summary>"New chat": the Home page (greeting, composer, starters), unfiled.</summary>
    [RelayCommand]
    private void NewChat()
    {
        _hub.NewChatFolder = null;
        Navigate(AppPages.Home);
    }

    public string PageTitle => CurrentPage?.Label ?? string.Empty;

    public string AppName => AppInfo.Current.Name;

    /// <summary>The mark in the title bar: the template's <c>logoMark</c> option (app.json <c>logo</c>), one of three generic Lucide shapes.</summary>
    public LucideIconKind LogoKind => AppInfo.Current.Logo switch
    {
        "ring" => LucideIconKind.CircleDot,
        "grid" => LucideIconKind.LayoutGrid,
        _ => LucideIconKind.Sparkles,
    };

    public void Navigate(string key)
    {
        if (AppPages.Find(key) is not { } page)
        {
            return;
        }
        SelectedKey = key;
        CurrentPage = page;
    }

    partial void OnSelectedKeyChanged(string? value)
    {
        if (value is not null && value.StartsWith("c:", StringComparison.Ordinal) && Guid.TryParse(value[2..], out var conversation))
        {
            // A conversation of the sidebar's tree: show it in the chat.
            _hub.Open(conversation);
            if (CurrentPage?.Key != AppPages.Chat && AppPages.Find(AppPages.Chat) is { } chat)
            {
                CurrentPage = chat;
            }
        }
        else if (value is not null && value != CurrentPage?.Key)
        {
            Navigate(value);
        }
    }

    partial void OnIsSidebarCollapsedChanged(bool value)
    {
        UserSettings.Current.IsSidebarCollapsed = value;
        UserSettings.Current.Save();
    }

    [RelayCommand]
    private void ToggleSidebar() => IsSidebarCollapsed = !IsSidebarCollapsed;

    public string SidebarTip => Text.Get(AppLangResourceKind.ToggleSidebar) + (OperatingSystem.IsMacOS() ? "  ⌘B" : "  Ctrl+B");

    /// <summary>The app's language (preferences menu); remembered between runs.</summary>
    [RelayCommand]
    private void SetLanguage(string tag)
    {
        if (tag != ArcLanguage.Current.Value)
        {
            ArcLanguage.Set(tag);
            UserSettings.Current.Language = tag;
            UserSettings.Current.Save();
        }
    }

    public bool IsEnglish => ArcLanguage.Current.Value == "en-US";

    public bool IsSimplifiedChinese => ArcLanguage.Current.Value == "zh-CN";

    public bool IsTraditionalChinese => ArcLanguage.Current.Value == "zh-TW";

    /// <summary>How much the app moves: <c>System</c> (follow the operating system), <c>Full</c>, <c>Reduced</c> or <c>Off</c>.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMotionSystem), nameof(IsMotionFull), nameof(IsMotionReduced), nameof(IsMotionOff))]
    private MotionPreference _motion = UserSettings.Current.Motion;

    public bool IsMotionSystem => Motion == MotionPreference.System;

    public bool IsMotionFull => Motion == MotionPreference.Full;

    public bool IsMotionReduced => Motion == MotionPreference.Reduced;

    public bool IsMotionOff => Motion == MotionPreference.Off;

    /// <summary>Sets the motion level (Arc's Full / Reduced / Off, or the system's); remembered between runs.</summary>
    [RelayCommand]
    private void SetMotion(string level)
    {
        if (Enum.TryParse<MotionPreference>(level, out var preference))
        {
            Motion = preference;
            UserSettings.Current.Motion = preference;
            UserSettings.Current.Save();
            AppMotion.Apply(preference);
        }
    }

    /// <summary>The appearance chosen (light, dark or the system's); the title bar's button shows it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLight), nameof(IsDark), nameof(IsSystem), nameof(AppearanceIcon), nameof(AppearanceTip))]
    private ArcAppearance _appearance = ArcTheme.State.Appearance;

    public bool IsLight => Appearance == ArcAppearance.Light;

    public bool IsDark => Appearance == ArcAppearance.Dark;

    public bool IsSystem => Appearance == ArcAppearance.System;

    public LucideIconKind AppearanceIcon => Appearance switch
    {
        ArcAppearance.Light => LucideIconKind.Sun,
        ArcAppearance.Dark => LucideIconKind.Moon,
        _ => LucideIconKind.Monitor,
    };

    public string AppearanceTip => Text.Get(AppLangResourceKind.Appearance) + ": " + Text.Get(Appearance switch
    {
        ArcAppearance.Light => AppLangResourceKind.AppearanceLight,
        ArcAppearance.Dark => AppLangResourceKind.AppearanceDark,
        _ => AppLangResourceKind.AppearanceSystem,
    }) + (OperatingSystem.IsMacOS() ? "  ⇧⌘L" : "  Ctrl+Shift+L");

    /// <summary>Light, dark or System, with the crossfade; the choice is saved (UserSettings.TrackTheme).</summary>
    [RelayCommand]
    private void SetAppearance(string mode)
    {
        if (Enum.TryParse<ArcAppearance>(mode, out var appearance))
        {
            ThemeTransition.Apply(t => t.Appearance(appearance));
        }
    }

    /// <summary>Switches between light and dark (Cmd+Shift+L / Ctrl+Shift+L).</summary>
    [RelayCommand]
    private void ToggleTheme() =>
        ThemeTransition.Apply(t => t.Appearance(ArcTheme.Manager.IsDark ? ArcAppearance.Light : ArcAppearance.Dark));
}
