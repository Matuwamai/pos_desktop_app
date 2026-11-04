using System.Net.Sockets;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Data.Sqlite;
using Pos.Infra;
using Pos.Infra.Api;
using Pos.Infra.Auth;
using Pos.Infra.Diagnostics;
using Pos.Infra.Secrets;
using Pos.Infra.Storage;
using Pos.Infra.Sync;
using Pos.Infra.Updates;

namespace Pos.Desktop.ViewModels;

public enum AppScreen
{
    Loading,
    Setup,
    Lock,
    Main,
}


public sealed partial class AppViewModel : ViewModelBase
{
    private readonly DeviceSessionStore _sessionStore;

    private LocalDatabase? _db;
    private SyncEngine? _sync;
    private ApiClient? _api;
    private string? _deviceToken;
    private string? _terminalId;
    private string? _locationId;
    private string? _tenantSubdomain;

    [ObservableProperty] private AppScreen _screen = AppScreen.Loading;
    [ObservableProperty] private ViewModelBase? _current;
    [ObservableProperty] private string? _fatalError;

    [ObservableProperty] private string? _notice;
    [ObservableProperty] private bool _updateAvailable;

    public AppViewModel()
    {
        var secrets = new FileSecretStore(AppPaths.SecretsDirectory);
        _sessionStore = new DeviceSessionStore(secrets, AppPaths.SecretsDirectory);
    }

    public async Task InitializeAsync()
    {

        try
        {
            var loaded = await _sessionStore.LoadAsync();
            if (loaded is null)
            {
                ShowSetup();
                return;
            }

            var (enrollment, deviceToken) = loaded.Value;
            _deviceToken = deviceToken;
            _terminalId = enrollment.TerminalId;
            _tenantSubdomain = enrollment.TenantSubdomain;
            _locationId = enrollment.LocationId;
            await OpenLocalDatabaseAsync(enrollment.ServerUrl, deviceToken);
            await ShowLockAsync();
            _ = CheckForUpdateAsync();
        }
        catch (SqliteException ex)
        {
            await HandleDeviceInvalidAsync("This device's local data could not be opened and has been reset — please re-enroll.", ex);
        }
        catch (Exception ex)
        {
            FatalError = ex.ToString();
        }
    }


    public Task HandleDeviceRevokedAsync(DeviceUnauthorizedException cause) =>
        HandleDeviceInvalidAsync("This device's access was revoked or has expired — please re-enroll.", cause);


    private async Task HandleDeviceInvalidAsync(string reason, Exception cause)
    {
        CrashLogger.LogHandledException(cause, "DeviceInvalid: " + reason);
        try
        {
            if (_db is not null)
            {
                await _db.DisposeAsync();
                _db = null;
            }
            if (File.Exists(AppPaths.DatabasePath)) File.Delete(AppPaths.DatabasePath);
            await _sessionStore.ClearAsync();
        }
        catch (Exception wipeEx)
        {
            // The wipe itself failing (e.g. the DB file locked) must not block getting
            // the user back to a usable screen with an honest message.
            CrashLogger.LogHandledException(wipeEx, "HandleDeviceInvalidAsync wipe failed");
        }

        _sync = null;
        _api = null;
        _deviceToken = null;
        Notice = reason;
        ShowSetup();
    }

    private void ShowSetup()
    {
        Screen = AppScreen.Setup;
        Current = new SetupWizardViewModel(_sessionStore, onEnrolled: () => _ = OnEnrolledAsync());
    }

    private async Task OnEnrolledAsync()
    {

        try
        {
            var loaded = await _sessionStore.LoadAsync() ?? throw new InvalidOperationException("Enrollment just saved but not found");
            _deviceToken = loaded.DeviceToken;
            _terminalId = loaded.Enrollment.TerminalId;
            _tenantSubdomain = loaded.Enrollment.TenantSubdomain;
            _locationId = loaded.Enrollment.LocationId;
            await OpenLocalDatabaseAsync(loaded.Enrollment.ServerUrl, loaded.DeviceToken);
            await ShowLockAsync();
        }
        catch (SqliteException ex)
        {
            await HandleDeviceInvalidAsync("This device's local data could not be opened and has been reset — please re-enroll.", ex);
        }
        catch (Exception ex)
        {
            FatalError = ex.ToString();
        }
    }

    private async Task OpenLocalDatabaseAsync(string serverUrl, string deviceToken)
    {

        _db = await LocalDatabase.OpenAsync(AppPaths.DatabasePath, deviceToken);
        _api = ApiClient.ForServer(new Uri(serverUrl));
        _sync = new SyncEngine(_db, _api);
    }

    private async Task ShowLockAsync()
    {
        if (_db is null) throw new InvalidOperationException("Local database not open");

        Notice = null;


        try
        {
            await _sync!.PullAllAsync(_deviceToken!);
        }
        catch (DeviceUnauthorizedException ex)
        {
            await HandleDeviceInvalidAsync("This device's access was revoked or has expired — please re-enroll.", ex);
            return;
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            // Swallowed deliberately — see comment above.
        }

        var pinVerifier = new PinVerifier(_db, _sync!);
        var lockVm = new LockScreenViewModel(pinVerifier, onUnlocked: user => _ = ShowMainAsync(user));
        await lockVm.LoadUsersAsync();
        Screen = AppScreen.Lock;
        Current = lockVm;
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            UpdateAvailable = await UpdateChecker.CheckForUpdateAsync();
        }
        catch (Exception ex)
        {

            CrashLogger.LogHandledException(ex, "CheckForUpdateAsync");
        }
    }


    private async Task ShowMainAsync(RosterUser user)
    {
        var cashierVm = new CashierViewModel(_db!, _sync!, _api!, _deviceToken!, _terminalId!, _locationId!, _tenantSubdomain!, user, onLogout: () => _ = ShowLockAsync());

        Screen = AppScreen.Main;
        Current = cashierVm;
        await cashierVm.LoadAsync();
    }
}
