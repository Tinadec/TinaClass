using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using TinaClass.App.Common.Api;
using TinaClass.App.Common.Shell;
using TKCode.Arc;
using Xunit;

namespace TinaClass.App.Tests.Api;

/// <summary>The sign-in screen that covers the app while <c>"dataSource": "api"</c> and nobody is signed in.</summary>
public class SignInTests
{
    /// <summary>Opens the main window on the fake API (tokens in memory), like <see cref="Ui.Open"/> does on the sample data.</summary>
    public static MainWindow Open(FakeApi api, out IDisposable apiMode, string? rememberedRefreshToken = null)
    {
        apiMode = api.UseApiMode();
        if (rememberedRefreshToken is not null)
        {
            DesktopApp.Services.GetRequiredService<TokenStore>().Save(rememberedRefreshToken);
        }
        TKCode.Arc.Localization.ArcLanguage.Set("en-US");
        ArcTheme.Apply(t => t.MotionLevel(TKCode.Arc.Motion.ArcMotionLevel.Off));
        var window = new MainWindow { DataContext = Ui.Shell, Width = 1440, Height = 900 };
        window.Show();
        Ui.Settle();
        return window;
    }

    public static SignInOverlay? Overlay(MainWindow window) => window.GetVisualDescendants().OfType<SignInOverlay>().SingleOrDefault();

    [AvaloniaFact]
    public void On_the_sample_data_there_is_no_sign_in()
    {
        var window = Ui.Open();
        Assert.Null(Overlay(window));
        Assert.False(ApiSettings.Current.UsesApi);
    }

    [AvaloniaFact]
    public async Task With_the_api_the_screen_asks_first_and_the_app_appears_after_signing_in()
    {
        var api = new FakeApi();
        var window = Open(api, out var mode);
        using (mode)
        {
            var overlay = Assert.IsType<SignInOverlay>(Overlay(window));
            Assert.NotNull(overlay.Shown);
            var viewModel = DesktopApp.Services.GetRequiredService<SignInViewModel>();
            Assert.False(viewModel.SignInCommand.CanExecute(null));

            viewModel.Email = FakeApi.Email;
            viewModel.Password = FakeApi.Password;
            Assert.True(viewModel.SignInCommand.CanExecute(null));
            await viewModel.SignInCommand.ExecuteAsync(null);
            Ui.Settle();

            Assert.Null(overlay.Shown);
            Assert.Equal("Ada", DesktopApp.Services.GetRequiredService<ApiSession>().UserName);
            Assert.NotNull(Ui.Pages(window).View);
        }
    }

    [AvaloniaFact]
    public async Task A_wrong_password_keeps_the_screen_and_says_why()
    {
        var api = new FakeApi();
        var window = Open(api, out var mode);
        using (mode)
        {
            var viewModel = DesktopApp.Services.GetRequiredService<SignInViewModel>();
            viewModel.Email = FakeApi.Email;
            viewModel.Password = "wrong";
            await viewModel.SignInCommand.ExecuteAsync(null);
            Ui.Settle();

            Assert.NotNull(Overlay(window)!.Shown);
            Assert.True(viewModel.HasError);
            Assert.Contains("email or password", viewModel.Error, StringComparison.Ordinal);
            Assert.False(viewModel.IsBusy);
        }
    }

    [AvaloniaFact]
    public async Task Signing_out_brings_the_screen_back_and_forgets_the_pages()
    {
        var api = new FakeApi();
        var window = Open(api, out var mode);
        using (mode)
        {
            var viewModel = DesktopApp.Services.GetRequiredService<SignInViewModel>();
            viewModel.Email = FakeApi.Email;
            viewModel.Password = FakeApi.Password;
            await viewModel.SignInCommand.ExecuteAsync(null);
            Ui.Settle();
            Assert.Null(Overlay(window)!.Shown);

            await viewModel.SignOutCommand.ExecuteAsync(null);
            Ui.Settle();
            Assert.NotNull(Overlay(window)!.Shown);
            Assert.Null(Ui.Pages(window).View);
            Assert.Contains(api.Requests, r => r.Path == "/api/v1/auth/logout");

            // The next person starts fresh: the first page is built again after the next sign-in.
            viewModel.Password = FakeApi.Password;
            await viewModel.SignInCommand.ExecuteAsync(null);
            Ui.Settle();
            Assert.Null(Overlay(window)!.Shown);
            Assert.NotNull(Ui.Pages(window).View);
        }
    }

    [AvaloniaFact]
    public async Task The_preferences_menu_offers_sign_out_only_with_the_api()
    {
        Assert.DoesNotContain(Ui.View(Ui.Open()).Preferences.Items.OfType<MenuEntry>(), e => "Sign out".Equals(e.Header));

        var window = Open(new FakeApi(), out var mode);
        using (mode)
        {
            var entry = Ui.View(window).Preferences.Items.OfType<MenuEntry>().Last();
            Assert.Equal("Sign out", entry.Header);
            var viewModel = DesktopApp.Services.GetRequiredService<SignInViewModel>();
            viewModel.Email = FakeApi.Email;
            viewModel.Password = FakeApi.Password;
            await viewModel.SignInCommand.ExecuteAsync(null);
            Ui.Settle();

            entry.Command!.Execute(null);
            Ui.Settle(10);
            Assert.NotNull(Overlay(window)!.Shown);
        }
    }

    [AvaloniaFact]
    public void A_remembered_session_signs_in_without_asking()
    {
        var api = new FakeApi();
        var window = Open(api, out var mode, rememberedRefreshToken: "refresh-1");
        using (mode)
        {
            Ui.Settle(40);
            Assert.Null(Overlay(window)!.Shown);
            Assert.True(DesktopApp.Services.GetRequiredService<ApiSession>().IsSignedIn);
        }
    }
}
