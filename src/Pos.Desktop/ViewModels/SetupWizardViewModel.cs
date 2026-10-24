using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Infra.Api;
using Pos.Infra.Secrets;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// Setup wizard steps, per the Phase 1 plan: server URL → admin login → choose
/// location → register + enroll this terminal. Each step's command only enables once
/// the previous one succeeded, so the view can just bind buttons to CanExecute.
/// </summary>
public enum SetupStep
{
    Connect,
    ChooseLocation,
    RegisterTerminal,
    Done,
}

public sealed partial class SetupWizardViewModel : ViewModelBase
{
    private readonly DeviceSessionStore _sessionStore;
    private readonly Action _onEnrolled;

    private ApiClient? _api;
    private string? _adminToken;

    public SetupWizardViewModel(DeviceSessionStore sessionStore, Action onEnrolled)
    {
        _sessionStore = sessionStore;
        _onEnrolled = onEnrolled;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsConnectStep))]
    [NotifyPropertyChangedFor(nameof(IsChooseLocationStep))]
    [NotifyPropertyChangedFor(nameof(IsRegisterTerminalStep))]
    private SetupStep _step = SetupStep.Connect;

    public bool IsConnectStep => Step == SetupStep.Connect;
    public bool IsChooseLocationStep => Step == SetupStep.ChooseLocation;
    public bool IsRegisterTerminalStep => Step == SetupStep.RegisterTerminal;
    [ObservableProperty] private string _serverUrl = "https://localhost:4000";
    [ObservableProperty] private string _subdomain = "";
    [ObservableProperty] private string _email = "";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _terminalName = Environment.MachineName;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private bool _isBusy;

    public ObservableCollection<LocationSummary> Locations { get; } = [];
    [ObservableProperty] private LocationSummary? _selectedLocation;

    [RelayCommand]
    private async Task LoginAsync()
    {
        IsBusy = true;
        StatusMessage = null;
        try
        {
            if (!Uri.TryCreate(ServerUrl, UriKind.Absolute, out var uri))
            {
                StatusMessage = "Enter a valid server URL, e.g. https://pos.example.com";
                return;
            }

            _api = ApiClient.ForServer(uri);
            var result = await _api.LoginAsync(Subdomain, Email, Password);
            if (result.MfaRequired)
            {
                // Phase 1 scope: a terminal is set up by an admin who can finish MFA on
                // the web app first. Revisit if that proves too restrictive in practice.
                StatusMessage = "This account has MFA enabled — sign in on the web app first, then disable MFA for setup or use an account without it.";
                return;
            }

            _adminToken = result.Token ?? throw new ApiClientException("Login did not return a token");
            Locations.Clear();
            foreach (var location in await _api.ListLocationsAsync(_adminToken))
                Locations.Add(location);

            Step = SetupStep.ChooseLocation;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't sign in: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void ChooseLocation()
    {
        if (SelectedLocation is null)
        {
            StatusMessage = "Pick a location first.";
            return;
        }
        Step = SetupStep.RegisterTerminal;
    }

    [RelayCommand]
    private async Task RegisterAndEnrollAsync()
    {
        if (_api is null || _adminToken is null || SelectedLocation is null) return;
        IsBusy = true;
        StatusMessage = null;
        try
        {
            var deviceIdentifier = $"{Environment.MachineName}-{Environment.OSVersion.Platform}";
            // validations/terminal.js: platform is one of 'web' | 'desktop' | 'mobile' —
            // not an OS name; this terminal is always 'desktop'.
            var terminal = await _api.RegisterTerminalAsync(_adminToken, SelectedLocation.Id, TerminalName, deviceIdentifier, "desktop");
            var enrollment = await _api.EnrollDeviceAsync(_adminToken, terminal.Id);

            await _sessionStore.SaveAsync(
                new DeviceEnrollment(ServerUrl, terminal.Id, SelectedLocation.Id, Subdomain),
                enrollment.Token);

            Step = SetupStep.Done;
            _onEnrolled();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Couldn't register this terminal: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
