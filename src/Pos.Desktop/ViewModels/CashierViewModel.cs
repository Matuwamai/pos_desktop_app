using System.Collections.ObjectModel;
using System.Net.Sockets;
using System.Text.Json;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Interfaces;
using Pos.Core.Sync;
using Pos.Infra.Api;
using Pos.Infra.Auth;
using Pos.Infra.Sales;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Desktop.ViewModels;

public sealed record HeldTicketSummary(string Id, string Label, DateTimeOffset HeldAt);

/// <summary>
/// The Phase 2 cashier screen: catalog, cart, modifiers, discount, customer, payment,
/// held tickets, drawer open/close, and the periodic background sync — all working off
/// the local SQLite tables Phase 1 pulls, per the Phase 2 plan's "one write path" design
/// (see <see cref="SaleService"/>). Dialogs are owned by the View (shown as
/// <c>Window.ShowDialog</c>) and invoked through the <c>Show*</c> hook delegates below,
/// so this class never references Avalonia's windowing types directly.
/// </summary>
public sealed partial class CashierViewModel : ViewModelBase
{
    private readonly LocalDatabase _db;
    private readonly SyncEngine _sync;
    private readonly SaleService _saleService;
    private readonly string _deviceToken;
    private readonly string _locationId;
    private readonly Action _onLogout;
    private readonly DispatcherTimer _backgroundSyncTimer;

    private List<CatalogProduct> _allProducts = [];
    private string? _locationName;

    public CashierViewModel(LocalDatabase db, SyncEngine sync, string deviceToken, string terminalId, string locationId, string tenantSubdomain, RosterUser cashier, Action onLogout)
    {
        _db = db;
        _sync = sync;
        _saleService = new SaleService(db, terminalId);
        _deviceToken = deviceToken;
        _locationId = locationId;
        TenantSubdomain = tenantSubdomain;
        SignedInAs = cashier;
        _onLogout = onLogout;

        _backgroundSyncTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _backgroundSyncTimer.Tick += (_, _) => _ = SyncNowAsync();
    }

    public string TenantSubdomain { get; }
    public RosterUser SignedInAs { get; }

    // Exposed for CashierView's code-behind only, which owns dialog construction
    // (CustomerDialogViewModel needs db/sync/deviceToken) — see the Show* hooks below.
    internal LocalDatabase Db => _db;
    internal SyncEngine Sync => _sync;
    internal string DeviceToken => _deviceToken;

    public ObservableCollection<CatalogProduct> Products { get; } = [];
    public ObservableCollection<CategoryOption> Categories { get; } = [];
    public ObservableCollection<CartLineRow> CartRows { get; } = [];
    public ObservableCollection<HeldTicketSummary> HeldTickets { get; } = [];

    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string? _selectedCategoryId;

    [ObservableProperty] private bool _isOnline;
    [ObservableProperty] private string _syncStatus = "Not yet synced";
    [ObservableProperty] private bool _isSyncing;
    [ObservableProperty] private int _pendingSaleCount;

    [ObservableProperty] private decimal _subtotal;
    [ObservableProperty] private decimal _discountTotal;
    [ObservableProperty] private decimal _taxTotal;
    [ObservableProperty] private decimal _total;
    [ObservableProperty] private string? _customerName;

    [ObservableProperty] private string? _drawerSessionId;
    [ObservableProperty] private string? _errorMessage;

    private Cart _cart = new();

    // Wired by CashierView's code-behind — see the class doc comment.
    public Func<CatalogProduct, IReadOnlyList<ModifierGroupOptions>, Task<CartLine?>>? ShowVariantPicker { get; set; }
    public Func<string, decimal?, string?, Task<(bool Applied, decimal Amount, string? Reason)>>? ShowDiscountDialog { get; set; }
    public Func<bool, Task<(bool Applied, string Id, string Name)>>? ShowCustomerDialog { get; set; }
    public Func<decimal, Task<IReadOnlyList<OfflineOrderPayment>?>>? ShowPaymentDialog { get; set; }
    public Func<Task<decimal?>>? ShowDrawerOpenDialog { get; set; }
    public Func<Task<decimal?>>? ShowDrawerCloseDialog { get; set; }
    public Func<ReceiptDocument, Task>? ShowReceiptDialog { get; set; }
    public Func<string, Task>? ShowError { get; set; }

    public async Task LoadAsync()
    {
        await LoadLocationNameAsync();
        await LoadCatalogAsync();
        await LoadHeldTicketsAsync();
        await RefreshPendingCountAsync();

        var openSession = await _saleService.GetOpenDrawerSessionAsync(_locationId);
        if (openSession is null)
        {
            var opening = ShowDrawerOpenDialog is null ? 0 : await ShowDrawerOpenDialog();
            DrawerSessionId = await _saleService.OpenDrawerAsync(_locationId, SignedInAs.Id, opening ?? 0);
        }
        else
        {
            DrawerSessionId = openSession.Id;
        }

        await SyncNowAsync();
        _backgroundSyncTimer.Start();
    }

    public void StopBackgroundSync() => _backgroundSyncTimer.Stop();

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        IsSyncing = true;
        try
        {
            await _sync.SyncNowAsync(_deviceToken);
            IsOnline = true;
            await LoadCatalogAsync(); // pulled prices/stock may have changed
        }
        catch (Exception ex) when (ex is ApiClientException or HttpRequestException or TaskCanceledException or SocketException)
        {
            IsOnline = false;
        }
        finally
        {
            var lastSyncedAt = await _sync.GetLastSyncedAtAsync();
            SyncStatus = lastSyncedAt is null ? "Not yet synced" : $"Last synced {lastSyncedAt.Value.ToLocalTime():g}";
            await RefreshPendingCountAsync();
            IsSyncing = false;
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();
    partial void OnSelectedCategoryIdChanged(string? value) => ApplyFilter();

    [RelayCommand]
    private async Task PickProductAsync(CatalogProduct product)
    {
        CartLine? line;
        if (!product.NeedsPicker)
        {
            var variant = product.Variants[0];
            line = new CartLine
            {
                VariantId = variant.VariantId, ProductName = product.Name, Sku = variant.Sku,
                BaseUnitPrice = variant.Price, Modifiers = [], Quantity = 1, TaxRate = variant.TaxRate,
            };
        }
        else if (ShowVariantPicker is not null)
        {
            var groups = await LoadModifierGroupOptionsAsync(product.ModifierGroupIds);
            line = await ShowVariantPicker(product, groups);
        }
        else
        {
            return;
        }

        if (line is null) return;
        _cart.AddOrIncrement(line);
        RefreshCart();
    }

    [RelayCommand]
    private void IncrementLine(string key) => SetLineQuantity(key, +1);

    [RelayCommand]
    private void DecrementLine(string key) => SetLineQuantity(key, -1);

    private void SetLineQuantity(string key, int delta)
    {
        var line = _cart.Items.FirstOrDefault(i => i.Key == key);
        if (line is null) return;
        _cart.SetQuantity(key, line.Quantity + delta);
        RefreshCart();
    }

    [RelayCommand]
    private void RemoveLine(string key)
    {
        _cart.RemoveLine(key);
        RefreshCart();
    }

    [RelayCommand]
    private async Task OpenDiscountAsync()
    {
        if (ShowDiscountDialog is null) return;
        var (applied, amount, reason) = await ShowDiscountDialog(SignedInAs.Role, _cart.DiscountAmount, _cart.DiscountReason);
        if (!applied) return;
        if (amount <= 0) _cart.ClearDiscount();
        else { _cart.DiscountAmount = amount; _cart.DiscountReason = reason; }
        RefreshCart();
    }

    [RelayCommand]
    private async Task OpenCustomerAsync()
    {
        if (ShowCustomerDialog is null) return;
        var (applied, id, name) = await ShowCustomerDialog(IsOnline);
        if (!applied) return;
        if (string.IsNullOrEmpty(id)) { _cart.CustomerId = null; _cart.CustomerName = null; }
        else { _cart.CustomerId = id; _cart.CustomerName = name; }
        CustomerName = _cart.CustomerName;
    }

    [RelayCommand(CanExecute = nameof(CanPay))]
    private async Task PayAsync()
    {
        if (ShowPaymentDialog is null) return;
        var totals = _cart.ComputeTotals();
        var payments = await ShowPaymentDialog(totals.Total);
        if (payments is null) return;

        SaleResult result;
        try
        {
            result = await _saleService.RingUpSaleAsync(_cart, SignedInAs.Id, _locationId, DrawerSessionId, payments);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Couldn't complete this sale: {ex.Message}";
            return;
        }

        if (ShowReceiptDialog is not null)
        {
            var receipt = new ReceiptDocument(
                TenantSubdomain, _locationName ?? _locationId, result.TerminalReceiptNo, DateTimeOffset.UtcNow,
                [.. _cart.Items.Select(i => new ReceiptLine(i.ProductName, i.Quantity, i.BaseUnitPrice + i.ModifiersTotal, i.Quantity * (i.BaseUnitPrice + i.ModifiersTotal)))],
                result.Totals.Subtotal, result.Totals.DiscountTotal, result.Totals.TaxTotal, result.Totals.Total);
            await ShowReceiptDialog(receipt);
        }

        _cart = new Cart();
        RefreshCart();
        await RefreshPendingCountAsync();
        _ = SyncNowAsync(); // best-effort, don't make the cashier wait on it
    }

    private bool CanPay => _cart.Items.Count > 0;

    [RelayCommand]
    private async Task HoldTicketAsync()
    {
        if (_cart.Items.Count == 0) return;
        var label = _cart.CustomerName ?? $"Ticket {DateTime.Now:HH:mm}";
        var dto = HeldCartDto.FromCart(_cart);
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "INSERT INTO held_tickets (id, label, held_at, cart_json) VALUES ($id, $label, $heldAt, $json)";
        cmd.Parameters.AddWithValue("$id", Guid.NewGuid().ToString());
        cmd.Parameters.AddWithValue("$label", label);
        cmd.Parameters.AddWithValue("$heldAt", DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(dto));
        await cmd.ExecuteNonQueryAsync();

        _cart = new Cart();
        RefreshCart();
        await LoadHeldTicketsAsync();
    }

    [RelayCommand]
    private async Task ResumeHeldTicketAsync(string ticketId)
    {
        string? json = null;
        await using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT cart_json FROM held_tickets WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", ticketId);
            json = (string?)await cmd.ExecuteScalarAsync();
        }
        if (json is null) return;

        var dto = JsonSerializer.Deserialize<HeldCartDto>(json);
        if (dto is null) return;
        _cart = dto.ToCart();
        RefreshCart();
        await DiscardHeldTicketAsync(ticketId);
    }

    [RelayCommand]
    private async Task DiscardHeldTicketAsync(string ticketId)
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "DELETE FROM held_tickets WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", ticketId);
        await cmd.ExecuteNonQueryAsync();
        await LoadHeldTicketsAsync();
    }

    [RelayCommand]
    private async Task CloseDrawerAsync()
    {
        if (DrawerSessionId is null || ShowDrawerCloseDialog is null) return;
        var closingCash = await ShowDrawerCloseDialog();
        if (closingCash is null) return;
        await _saleService.CloseDrawerAsync(DrawerSessionId, SignedInAs.Id, closingCash.Value);
        _ = SyncNowAsync();
        Logout();
    }

    [RelayCommand]
    private void Logout()
    {
        StopBackgroundSync();
        _onLogout();
    }

    private void RefreshCart()
    {
        CartRows.Clear();
        foreach (var line in _cart.Items)
        {
            var priced = Core.Pricing.PricingMath.PriceLine(line.BaseUnitPrice, line.ModifiersTotal, line.Quantity, line.TaxRate);
            var modifiersSummary = line.Modifiers.Count == 0 ? "" : string.Join(", ", line.Modifiers.Select(m => m.Name));
            CartRows.Add(new CartLineRow(line.Key, line.ProductName, modifiersSummary, line.Quantity, priced.LineSubtotal, priced.TaxAmount, priced.LineTotal));
        }

        var totals = _cart.ComputeTotals();
        Subtotal = totals.Subtotal;
        DiscountTotal = totals.DiscountTotal;
        TaxTotal = totals.TaxTotal;
        Total = totals.Total;
        CustomerName = _cart.CustomerName;
        PayCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadLocationNameAsync()
    {
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM locations WHERE id = $id";
        cmd.Parameters.AddWithValue("$id", _locationId);
        _locationName = (string?)await cmd.ExecuteScalarAsync();
    }

    private async Task RefreshPendingCountAsync() => PendingSaleCount = await _sync.GetPendingSaleCountAsync();

    private async Task LoadHeldTicketsAsync()
    {
        HeldTickets.Clear();
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, label, held_at FROM held_tickets ORDER BY held_at DESC";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            HeldTickets.Add(new HeldTicketSummary(reader.GetString(0), reader.GetString(1), DateTimeOffset.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
    }

    private async Task LoadCatalogAsync()
    {
        // Loaded fully up front, not looked up lazily per row below — Sqlite doesn't
        // allow a second command to run while a reader from the first is still open on
        // the same connection.
        await LoadAllTaxRatesAsync();

        var products = new Dictionary<string, (string Name, string? CategoryId, List<string> ModifierGroupIds, List<CatalogVariant> Variants)>();

        await using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT p.id, p.name, p.category_id, p.modifier_group_ids_json,
                       v.id, v.sku, v.price, v.tax_rate_id, p.track_inventory, i.quantity
                FROM products p
                JOIN variants v ON v.product_id = p.id AND v.is_active = 1
                LEFT JOIN inventory i ON i.variant_id = v.id
                WHERE p.is_active = 1
                ORDER BY p.name, v.sku
                """;
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var productId = reader.GetString(0);
                if (!products.TryGetValue(productId, out var entry))
                {
                    var groupIds = JsonSerializer.Deserialize<List<string>>(reader.GetString(3)) ?? [];
                    entry = (reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2), groupIds, []);
                    products[productId] = entry;
                }
                var tracksInventory = reader.GetInt32(8) != 0;
                decimal? stock = tracksInventory && !reader.IsDBNull(9) ? decimal.Parse(reader.GetString(9), System.Globalization.CultureInfo.InvariantCulture) : null;
                decimal? taxRate = !reader.IsDBNull(7) && _taxRateCache.TryGetValue(reader.GetString(7), out var rate) ? rate : null;
                entry.Variants.Add(new CatalogVariant(
                    reader.GetString(4), reader.GetString(5), decimal.Parse(reader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                    taxRate, stock, tracksInventory));
            }
        }

        _allProducts = [.. products.Select(kv => new CatalogProduct(kv.Key, kv.Value.Name, kv.Value.CategoryId, kv.Value.ModifierGroupIds, kv.Value.Variants))];

        Categories.Clear();
        await using (var cmd = _db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT id, name FROM categories WHERE is_active = 1 ORDER BY name";
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync()) Categories.Add(new CategoryOption(reader.GetString(0), reader.GetString(1)));
        }

        ApplyFilter();
    }

    private readonly Dictionary<string, decimal> _taxRateCache = [];

    private async Task LoadAllTaxRatesAsync()
    {
        _taxRateCache.Clear();
        await using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT id, rate FROM tax_rates";
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            _taxRateCache[reader.GetString(0)] = decimal.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<List<ModifierGroupOptions>> LoadModifierGroupOptionsAsync(IReadOnlyList<string> groupIds)
    {
        var result = new List<ModifierGroupOptions>();
        foreach (var groupId in groupIds)
        {
            string name; int minSelect; int maxSelect;
            await using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT name, min_select, max_select FROM modifier_groups WHERE id = $id AND is_active = 1";
                cmd.Parameters.AddWithValue("$id", groupId);
                await using var reader = await cmd.ExecuteReaderAsync();
                if (!await reader.ReadAsync()) continue;
                name = reader.GetString(0);
                minSelect = reader.GetInt32(1);
                maxSelect = reader.GetInt32(2);
            }

            var options = new List<ModifierOption>();
            await using (var cmd = _db.Connection.CreateCommand())
            {
                cmd.CommandText = "SELECT id, name, price_adjustment FROM modifiers WHERE modifier_group_id = $id AND is_active = 1 ORDER BY name";
                cmd.Parameters.AddWithValue("$id", groupId);
                await using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                    options.Add(new ModifierOption(reader.GetString(0), reader.GetString(1), decimal.Parse(reader.GetString(2), System.Globalization.CultureInfo.InvariantCulture)));
            }

            result.Add(new ModifierGroupOptions(groupId, name, minSelect, maxSelect, options));
        }
        return result;
    }

    private void ApplyFilter()
    {
        Products.Clear();
        IEnumerable<CatalogProduct> query = _allProducts;
        if (!string.IsNullOrWhiteSpace(SelectedCategoryId))
            query = query.Where(p => p.CategoryId == SelectedCategoryId);
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var search = SearchText.Trim();
            query = query.Where(p =>
                p.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                p.Variants.Any(v => v.Sku.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }
        foreach (var product in query) Products.Add(product);
    }
}
