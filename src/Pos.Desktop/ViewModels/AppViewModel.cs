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
    private ApiClient? _api;
    private string? _deviceToken;
    private string? _terminalId;
    private string? _locationId;
    private string? _tenantSubdomain;

    [ObservableProperty] private AppScreen _screen = AppScreen.Loading;
    [ObservableProperty] private ViewModelBase? _current;
    [ObservableProperty] private string? _fatalError;
    // A non-fatal banner shown once on the Setup screen after HandleDeviceInvalidAsync
    // resets the device (revoked/expired token, or an undecryptable local DB) — unlike
    // FatalError, the Setup form underneath is still fully usable.
    [ObservableProperty] private string? _notice;
    [ObservableProperty] private bool _updateAvailable;

    public AppViewModel()
    {
        var secrets = new FileSecretStore(AppPaths.SecretsDirectory);
        _sessionStore = new DeviceSessionStore(secrets, AppPaths.SecretsDirectory);
    }

    public async Task InitializeAsync()
    {
        // This whole method runs fire-and-forget from MainWindow's constructor (nothing
        // to await it against that early) — without this try/catch, any exception here
        // becomes an unobserved task exception that vanishes silently, leaving Current
        // null and the window permanently blank with no clue why. FatalError is the one
        // thing the window can show no matter which screen was (or wasn't) reached.
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

    /// <summary>Entry point for CashierViewModel's <c>OnDeviceInvalid</c> hook (wired by
    /// CashierView's code-behind) — the cashier screen has no reference to this class
    /// otherwise, by design (see its own class doc comment).</summary>
    public Task HandleDeviceRevokedAsync(DeviceUnauthorizedException cause) =>
        HandleDeviceInvalidAsync("This device's access was revoked or has expired — please re-enroll.", cause);

    /// <summary>The one recovery path shared by a revoked/expired device token and an
    /// undecryptable local database (see OpenLocalDatabaseAsync's comment below) — both
    /// have the same only sane remedy, so there is exactly one implementation of it.
    /// Wipes local state and returns to the Setup screen with an explanatory notice,
    /// instead of either silently doing nothing (the old behavior for a 401, swallowed
    /// as "offline") or permanently dead-ending on FatalError (the old behavior for a
    /// bad DB key).</summary>
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
        // Also fire-and-forget (from SetupWizardViewModel's onEnrolled callback) — same
        // reasoning as InitializeAsync's try/catch.
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
        // The device token doubles as the SQLite encryption key material — still true
        // after Phase 6, and no longer flagged as a risk to revisit: rotating the DB key
        // independently of the device token would only matter if a revoke/expiry had to
        // invalidate the DB *without* losing its contents. It doesn't — HandleDeviceInvalidAsync
        // (triggered by a DeviceUnauthorizedException from ShowLockAsync/CashierViewModel's
        // sync calls, or by a SqliteException here if the key itself no longer decrypts
        // the file) deletes the DB outright and forces re-enrollment either way, so there
        // is nothing left to rotate separately.
        _db = await LocalDatabase.OpenAsync(AppPaths.DatabasePath, deviceToken);
        _api = ApiClient.ForServer(new Uri(serverUrl));
        _sync = new SyncEngine(_db, _api);
    }

    private async Task ShowLockAsync()
    {
        if (_db is null) throw new InvalidOperationException("Local database not open");

        // Past Setup, the "please re-enroll" banner (if HandleDeviceInvalidAsync ever
        // set one on a prior run of this screen) no longer applies — clear it before
        // re-attempting the pull below, which will set a fresh one if it 401s again.
        Notice = null;

        // The roster (and everything else) only exists locally once a pull has run — on
        // a freshly enrolled device there is nothing to show here otherwise, and the
        // lock screen is the *only* screen reachable before a PIN unlock, so this is the
        // one place that first pull can happen. A fresh device therefore needs
        // connectivity at least once before anyone can sign in — expected, not a bug: a
        // terminal with no cached roster can't verify anyone's PIN. If this fails (no
        // network yet), PinVerifier's max-offline-window check reports exactly that
        // ("terminal has been offline too long") rather than just showing an empty list.
        //
        // This is also the first network call made with the device token after every
        // app launch (and after every logout, since CashierViewModel's onLogout calls
        // back into this method) — centralizing the 401 check here, rather than at each
        // caller, means a revoked/expired device is caught the same way no matter how
        // this screen was reached.
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
            // Never let a background update check disrupt the app — see UpdateChecker's
            // own doc comment; this is one extra layer of the same guarantee.
            CrashLogger.LogHandledException(ex, "CheckForUpdateAsync");
        }
    }


    private async Task ShowMainAsync(RosterUser user)
    {
        var cashierVm = new CashierViewModel(_db!, _sync!, _api!, _deviceToken!, _terminalId!, _locationId!, _tenantSubdomain!, user, onLogout: () => _ = ShowLockAsync());
        // Current is set *before* LoadAsync runs, not after: LoadAsync opens the drawer
        // dialog via CashierView's code-behind-wired Show* hooks, which only get wired
        // once CashierView actually materializes — which happens here, synchronously,
        // as soon as Current's DataTemplate binds.
        Screen = AppScreen.Main;
        Current = cashierVm;
        await cashierVm.LoadAsync();
    }
}
