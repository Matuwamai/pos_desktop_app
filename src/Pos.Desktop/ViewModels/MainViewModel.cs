using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Infra.Api;
using Pos.Infra.Auth;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Desktop.ViewModels;

/// <summary>
/// The Phase 1 proof screen: a read-only product list fed entirely from SQLite, plus
/// a sync status bar (online/offline, pending outbox count, last-synced time) — the
/// thing this phase exists to demonstrate. Phase 2 turns this into the real POS
/// cashier screen on the same local tables.
/// </summary>
public sealed partial class MainViewModel : ViewModelBase
{
    private readonly LocalDatabase _db;
    private readonly SyncEngine _sync;
    private readonly string _deviceToken;

    public MainViewModel(LocalDatabase db, SyncEngine sync, string deviceToken, RosterUser signedInAs)
    {
        _db = db;
        _sync = sync;
        _deviceToken = deviceToken;
        SignedInAs = signedInAs;
    }

    public RosterUser SignedInAs { get; }
    public ObservableCollection<ProductRowViewModel> Products { get; } = [];

    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _syncStatus = "Not yet synced";
    [ObservableProperty] private bool _isSyncing;

    public async Task LoadAsync()
    {
        await LoadProductsFromLocalAsync();
        await RefreshSyncStatusAsync();
        await SyncNowAsync();
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsSyncing = true;
        try
        {
            await _sync.PullAllAsync(_deviceToken);
            IsOnline = true;
            await LoadProductsFromLocalAsync();
        }
        catch (ApiClientException)
        {
            // No connectivity, device revoked, or the server rejected the token — the
            // till keeps working from whatever it already pulled; see IsOnline in the
            // status bar. Phase 1 doesn't yet surface *why* (network vs. revoked).
            IsOnline = false;
        }
        finally
        {
            await RefreshSyncStatusAsync();
            IsSyncing = false;
        }
    }

    private async Task RefreshSyncStatusAsync()
    {
        var lastSyncedAt = await _sync.GetLastSyncedAtAsync();
        SyncStatus = lastSyncedAt is null
            ? "Not yet synced"
            : $"Last synced {lastSyncedAt.Value.ToLocalTime():g}";
    }

    private async Task LoadProductsFromLocalAsync()
    {
        Products.Clear();
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT v.id, p.name, v.sku, v.price, i.quantity, p.track_inventory
            FROM variants v
            JOIN products p ON p.id = v.product_id
            LEFT JOIN inventory i ON i.variant_id = v.id
            WHERE v.is_active = 1 AND p.is_active = 1
            ORDER BY p.name, v.sku
            """;
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var trackInventory = reader.GetInt32(5) != 0;
            decimal? stock = trackInventory && !reader.IsDBNull(4) ? decimal.Parse(reader.GetString(4), CultureInfo.InvariantCulture) : null;
            Products.Add(new ProductRowViewModel(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                decimal.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                stock,
                trackInventory));
        }
    }
}
