using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Infra.Auth;

namespace Pos.Desktop.ViewModels;

public sealed partial class LockScreenViewModel : ViewModelBase
{
    private readonly PinVerifier _pinVerifier;
    private readonly Action<RosterUser> _onUnlocked;

    public LockScreenViewModel(PinVerifier pinVerifier, Action<RosterUser> onUnlocked)
    {
        _pinVerifier = pinVerifier;
        _onUnlocked = onUnlocked;
    }

    public ObservableCollection<RosterUser> Users { get; } = [];
    [ObservableProperty] private RosterUser? _selectedUser;
    [ObservableProperty] private string _pin = "";
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;

    public async Task LoadUsersAsync()
    {
        Users.Clear();
        foreach (var user in await _pinVerifier.ListActiveUsersAsync())
            Users.Add(user);
    }

    [RelayCommand]
    private void AppendDigit(string digit)
    {
        if (Pin.Length < 12) Pin += digit;
    }

    [RelayCommand]
    private void Backspace()
    {
        if (Pin.Length > 0) Pin = Pin[..^1];
    }

    [RelayCommand]
    private async Task UnlockAsync()
    {
        if (SelectedUser is null)
        {
            StatusMessage = "Choose your name first.";
            return;
        }

        IsBusy = true;
        StatusMessage = null;
        try
        {
            var (result, user) = await _pinVerifier.TryLoginAsync(SelectedUser.Id, Pin);
            switch (result)
            {
                case PinLoginResult.Success:
                    Pin = "";
                    _onUnlocked(user!);
                    break;
                case PinLoginResult.WrongPin:
                    StatusMessage = "Wrong PIN.";
                    break;
                case PinLoginResult.UserNotFound:
                    StatusMessage = "This account is no longer on this terminal — reconnect to refresh the roster.";
                    break;
                case PinLoginResult.MustChangePasswordOnline:
                    StatusMessage = "This account must sign in online first (password change required).";
                    break;
                case PinLoginResult.OfflineWindowExpired:
                    StatusMessage = "This terminal has been offline too long — connect it to the network to continue.";
                    break;
            }
        }
        finally
        {
            Pin = "";
            IsBusy = false;
        }
    }
}
