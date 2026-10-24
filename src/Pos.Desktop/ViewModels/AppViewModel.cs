using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Infra;
using Pos.Infra.Api;
using Pos.Infra.Auth;
using Pos.Infra.Secrets;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Desktop.ViewModels;

public enum AppScreen
{
    Loading,
    Setup,
    Lock,
    Main,
}

/// <summary>
/// Composition root for the three screens (Setup/Lock/Main) and the services they
/// share. No DI container — Phase 1's object graph is small enough that constructing
/// it by hand here is clearer than the ceremony of one.
/// </summary>
public sealed partial class AppViewModel : ViewModelBase
{
    private readonly DeviceSessionStore _sessionStore;

    private LocalDatabase? _db;
    private SyncEngine? _sync;
    private string? _deviceToken;

    [ObservableProperty] private AppScreen _screen = AppScreen.Loading;
    [ObservableProperty] private ViewModelBase? _current;

    public AppViewModel()
    {
        var secrets = new FileSecretStore(AppPaths.SecretsDirectory);
        _sessionStore = new DeviceSessionStore(secrets, AppPaths.SecretsDirectory);
    }

    public async Task InitializeAsync()
    {
        var loaded = await _sessionStore.LoadAsync();
        if (loaded is null)
        {
            ShowSetup();
            return;
        }

        var (enrollment, deviceToken) = loaded.Value;
        _deviceToken = deviceToken;
        await OpenLocalDatabaseAsync(enrollment.ServerUrl, deviceToken);
        await ShowLockAsync();
    }

    private void ShowSetup()
    {
        Screen = AppScreen.Setup;
        Current = new SetupWizardViewModel(_sessionStore, onEnrolled: () => _ = OnEnrolledAsync());
    }

    private async Task OnEnrolledAsync()
    {
        var loaded = await _sessionStore.LoadAsync() ?? throw new InvalidOperationException("Enrollment just saved but not found");
        _deviceToken = loaded.DeviceToken;
        await OpenLocalDatabaseAsync(loaded.Enrollment.ServerUrl, loaded.DeviceToken);
        await ShowLockAsync();
    }

    private async Task OpenLocalDatabaseAsync(string serverUrl, string deviceToken)
    {
        // The device token doubles as the SQLite encryption key material for Phase 1 —
        // it is already a per-device secret the server can revoke, which rotating the
        // DB key on revoke would need anyway. A dedicated random key stored in
        // ISecretStore is the more obviously-correct design; revisit before Phase 5
        // if key rotation on revoke needs to be decoupled from re-enrollment.
        _db = await LocalDatabase.OpenAsync(AppPaths.DatabasePath, deviceToken);
        _sync = new SyncEngine(_db, ApiClient.ForServer(new Uri(serverUrl)));
    }

    private async Task ShowLockAsync()
    {
        if (_db is null) throw new InvalidOperationException("Local database not open");
        var pinVerifier = new PinVerifier(_db, _sync!);
        var lockVm = new LockScreenViewModel(pinVerifier, onUnlocked: user => _ = ShowMainAsync(user));
        await lockVm.LoadUsersAsync();
        Screen = AppScreen.Lock;
        Current = lockVm;
    }

    private async Task ShowMainAsync(RosterUser user)
    {
        var mainVm = new MainViewModel(_db!, _sync!, _deviceToken!, user);
        await mainVm.LoadAsync();
        Screen = AppScreen.Main;
        Current = mainVm;
    }
}
