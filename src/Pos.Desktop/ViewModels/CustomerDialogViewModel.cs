using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Desktop.ViewModels;

public sealed record CustomerSearchResult(string Id, string Name, string? Phone);

/// <summary>
/// Search the locally-synced <c>customers</c> table and attach one to the cart, or
/// create a new one — which needs the server, always (see
/// <c>SyncEngine.CreateCustomerAsync</c>'s doc comment: customer creation is online-only
/// in v1, pushed synchronously rather than queued through the outbox like a sale).
/// </summary>
public sealed partial class CustomerDialogViewModel : ViewModelBase
{
    private readonly LocalDatabase _db;
    private readonly SyncEngine _sync;
    private readonly string _deviceToken;

    public CustomerDialogViewModel(LocalDatabase db, SyncEngine sync, string deviceToken, bool isOnline)
    {
        _db = db;
        _sync = sync;
        _deviceToken = deviceToken;
        IsOnline = isOnline;
        _ = SearchAsync();
    }

    public bool IsOnline { get; }
    public ObservableCollection<CustomerSearchResult> Results { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _newName = "";
    [ObservableProperty] private string _newPhone = "";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public (string Id, string Name)? Result { get; private set; }
    public Action<bool>? RequestClose { get; set; }

    partial void OnSearchTextChanged(string value) => _ = SearchAsync();

    private async Task SearchAsync()
    {
        Results.Clear();
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, name, phone FROM customers WHERE is_active = 1 AND (name LIKE $q OR phone LIKE $q OR email LIKE $q) ORDER BY name LIMIT 30";
        cmd.Parameters.AddWithValue("$q", $"%{SearchText}%");
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            Results.Add(new CustomerSearchResult(reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
    }

    [RelayCommand]
    private void Attach(CustomerSearchResult customer)
    {
        Result = (customer.Id, customer.Name);
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private void Detach()
    {
        Result = (string.Empty, string.Empty); // the view interprets an empty id as "clear the attached customer"
        RequestClose?.Invoke(true);
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!IsOnline)
        {
            ErrorMessage = "Connect to the network to add a new customer.";
            return;
        }
        if (string.IsNullOrWhiteSpace(NewName))
        {
            ErrorMessage = "Enter a name.";
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var result = await _sync.CreateCustomerAsync(_deviceToken, NewName.Trim(), string.IsNullOrWhiteSpace(NewPhone) ? null : NewPhone.Trim(), null);
            if (!result.Success)
            {
                ErrorMessage = result.Error ?? "Could not create this customer.";
                return;
            }
            Result = (result.CustomerId!, NewName.Trim());
            RequestClose?.Invoke(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() => RequestClose?.Invoke(false);
}
