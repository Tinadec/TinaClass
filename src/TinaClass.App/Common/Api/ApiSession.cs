using CommunityToolkit.Mvvm.ComponentModel;
using TinaClass.Contracts.Auth;

namespace TinaClass.App.Common.Api;

/// <summary>
/// Who is signed in to the API. One per app; the sign-in screen and the shell bind to it. Requests that need a signed-in user wait
/// on <see cref="WaitForSignInAsync"/>, so a page that loads before the person has signed in simply finishes loading afterwards.
/// </summary>
public sealed partial class ApiSession : ObservableObject
{
    private TaskCompletionSource _signedIn = new(TaskCreationOptions.RunContinuationsAsynchronously);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSignedIn), nameof(UserName))]
    private CurrentUser? _user;

    /// <summary>The message the sign-in screen shows after a sign-out it did not ask for (an expired session).</summary>
    [ObservableProperty]
    private string? _signOutReason;

    public bool IsSignedIn => User is not null;

    public string UserName => User?.Name ?? string.Empty;

    /// <summary>Whether the signed-in user may do <paramref name="permission"/> (<c>Permissions</c> in the contracts).</summary>
    public bool Can(string permission) => User?.Permissions.Contains(permission) == true;

    public event EventHandler? SignedIn;

    public event EventHandler? SignedOut;

    public void SignIn(CurrentUser user)
    {
        SignOutReason = null;
        User = user;
        _signedIn.TrySetResult();
        SignedIn?.Invoke(this, EventArgs.Empty);
    }

    public void SignOut(string? reason = null)
    {
        if (_signedIn.Task.IsCompleted)
        {
            _signedIn = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        SignOutReason = reason;
        User = null;
        SignedOut?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Completes when a user is signed in (at once if one is).</summary>
    public Task WaitForSignInAsync(CancellationToken cancellation) => _signedIn.Task.WaitAsync(cancellation);
}
