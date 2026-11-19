using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinaClass.App.Common.Api.Localization;

namespace TinaClass.App.Common.Api;

/// <summary>
/// The sign-in screen: email and password, with "keep me signed in" (the refresh token then goes to the system's secret store, see
/// <see cref="TokenStore"/>). It is shown while <see cref="ApiSession.IsSignedIn"/> is false. Also signs out.
/// </summary>
public sealed partial class SignInViewModel : ObservableObject
{
    private readonly ApiClient _api;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _email = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private string _password = string.Empty;

    [ObservableProperty]
    private bool _remember = true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string? _error;

    public SignInViewModel(ApiClient api)
    {
        _api = api;
        api.Session.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ApiSession.IsSignedIn))
            {
                OnPropertyChanged(nameof(IsOpen));
            }
            if (e.PropertyName == nameof(ApiSession.SignOutReason) && api.Session.SignOutReason is { } reason)
            {
                Error = reason;
            }
        };
    }

    public ApiSession Session => _api.Session;

    /// <summary>Whether the sign-in screen is shown: nobody is signed in.</summary>
    public bool IsOpen => !Session.IsSignedIn;

    public bool HasError => Error is not null;

    public string Title => ApiLangResources.Format(ApiLangResourceKind.SignInTitle, AppInfo.Current.Name);

    public string Subtitle => ApiLangResources.Format(ApiLangResourceKind.SignInTo, ApiSettings.Current.Host);

    private bool CanSignIn() => !IsBusy && Email.Contains('@') && Password.Length > 0;

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    private async Task SignIn()
    {
        IsBusy = true;
        Error = null;
        try
        {
            await _api.SignInAsync(Email, Password, Remember);
            Password = string.Empty;
        }
        catch (ServiceException exception)
        {
            Error = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task SignOut() => _api.SignOutAsync();

    /// <summary>Signs in with the session remembered from last time, if any (at start). Failing is not an error: sign in by hand.</summary>
    public async Task TryResumeAsync()
    {
        try
        {
            await _api.ResumeAsync();
        }
        catch (ServiceException)
        {
        }
    }
}
