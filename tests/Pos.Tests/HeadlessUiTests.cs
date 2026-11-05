using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using Pos.Desktop.ViewModels;
using Pos.Desktop.Views;
using Pos.Infra.Api;
using Pos.Infra.Auth;
using Pos.Infra.Secrets;
using Pos.Infra.Storage;
using Pos.Infra.Sync;

namespace Pos.Tests;

public class HeadlessUiTests
{
    static HeadlessUiTests()
    {
        AppBuilder.Configure<Pos.Desktop.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .SetupWithoutStarting();
    }

    [Fact]
    public void SetupWizardView_renders()
    {
        var tempDir = CreateTempDir();
        var sessionStore = new DeviceSessionStore(new FileSecretStore(tempDir), tempDir);
        var vm = new SetupWizardViewModel(sessionStore, onEnrolled: () => { });

        var view = new SetupWizardView { DataContext = vm };
        RenderToPng(view, Path.Combine(tempDir, "setup-wizard.png"));
    }

    [Fact]
    public void CashierView_with_catalog_and_cart_renders()
    {

#pragma warning disable xUnit1031 
        var (vm, tempDir) = Task.Run(SetUpCashierViewModelAsync).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        try
        {
            Assert.False(vm.IsOnline); // unreachable server — confirms the offline path was exercised
            Assert.Single(vm.Products);

            var view = new CashierView { DataContext = vm };
            RenderToPng(view, Path.Combine(tempDir, "cashier-catalog-and-cart.png"));
        }
        finally
        {
            vm.StopBackgroundSync();
        }
    }

    [Fact]
    public async Task LockScreenView_renders()
    {
        var tempDir = CreateTempDir();
        var db = await LocalDatabase.OpenAsync(Path.Combine(tempDir, "pos.db"), "headless-test-key");
        SeedOneUser(db);
        var api = ApiClient.ForServer(new Uri("http://127.0.0.1:1"));
        var sync = new SyncEngine(db, api);
        var pinVerifier = new PinVerifier(db, sync);
        var vm = new LockScreenViewModel(pinVerifier, onUnlocked: _ => { });
        await vm.LoadUsersAsync();

        var view = new LockScreenView { DataContext = vm };
        RenderToPng(view, Path.Combine(tempDir, "lock-screen.png"));
    }

    [Fact]
    public void PaymentDialog_renders()
    {
        var tempDir = CreateTempDir();
        var vm = new PaymentDialogViewModel(total: 24.30m);

        var view = new PaymentDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "payment-dialog.png"));
    }

    [Fact]
    public void PaymentDialog_offline_hides_gift_card_and_store_credit()
    {
        var tempDir = CreateTempDir();
        var vm = new PaymentDialogViewModel(total: 24.30m, isOnline: false);
        Assert.DoesNotContain(Pos.Core.Sync.OfflinePaymentMethod.GiftCard, vm.Methods);
        Assert.DoesNotContain(Pos.Core.Sync.OfflinePaymentMethod.StoreCredit, vm.Methods);

        var view = new PaymentDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "payment-dialog-offline.png"));
    }

    [Fact]
    public void PaymentDialog_online_with_gift_card_tender_and_store_credit_balance_renders()
    {
        var tempDir = CreateTempDir();
        var vm = new PaymentDialogViewModel(total: 24.30m, isOnline: true, customerId: "customer-1", customerStoreCreditBalance: 15.50m);
        Assert.Contains(Pos.Core.Sync.OfflinePaymentMethod.GiftCard, vm.Methods);
        Assert.Contains(Pos.Core.Sync.OfflinePaymentMethod.StoreCredit, vm.Methods);
        vm.Lines[0].Method = Pos.Core.Sync.OfflinePaymentMethod.GiftCard; // so the code field is visible in the render

        var view = new PaymentDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "payment-dialog-online-gift-card.png"));
    }

    [Fact]
    public void DiscountDialog_promo_mode_renders()
    {
        var tempDir = CreateTempDir();
        var vm = new DiscountDialogViewModel(role: "OWNER", currentAmount: null, currentReason: null, subtotal: 50m, isOnline: true);
        vm.ShowPromoModeCommand.Execute(null);

        var view = new DiscountDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "discount-dialog-promo.png"));
    }

    // Milestone 2 (multi-currency, desktop/mobile): a 0-decimal currency (JPY-style)
    // must render whole units everywhere a *Text property feeds a binding — the
    // mechanical proof every CurrencyInfo.GetDecimalDigits(...) call site this
    // milestone touched actually took effect, not just compiled.
    [Fact]
    public void PaymentDialog_jpy_currency_renders_whole_units_with_no_decimal_point()
    {
        var tempDir = CreateTempDir();
        var vm = new PaymentDialogViewModel(total: 1500m, currency: "JPY");
        Assert.Equal("1500", vm.TotalText);
        Assert.DoesNotContain('.', vm.TotalText);

        var view = new PaymentDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "payment-dialog-jpy.png"));
    }

    [Fact]
    public void DiscountDialog_jpy_currency_amount_field_has_no_decimal_point()
    {
        var tempDir = CreateTempDir();
        var vm = new DiscountDialogViewModel(role: "OWNER", currentAmount: 500m, currentReason: null, subtotal: 2000m, isOnline: true, currency: "JPY");
        Assert.Equal("500", vm.AmountText);

        var view = new DiscountDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "discount-dialog-jpy.png"));
    }

    [Fact]
    public void RefundLookupDialog_renders()
    {
        var tempDir = CreateTempDir();
        var api = ApiClient.ForServer(new Uri("http://127.0.0.1:1"));
        var vm = new RefundLookupDialogViewModel(api, deviceToken: "fake-token", locationId: "location-1",
            processedByUserId: "user-1", hasPermission: true, isOnline: false);

        var view = new RefundLookupDialog { DataContext = vm };
        RenderDialogToPng(view, Path.Combine(tempDir, "refund-lookup-dialog.png"));
    }

    private static void SeedOneUser(LocalDatabase db)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO users (id, name, role, permissions_json, pin_hash, must_change_password, updated_at)
            VALUES ('u1', 'Jane Cashier', 'CASHIER', '[]', NULL, 0, '2024-01-01T00:00:00Z');
            """;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void SyncNowAsync_invokes_OnDeviceInvalid_instead_of_swallowing_a_401()
    {
        // Phase 6 regression: a revoked/expired device must surface through
        // OnDeviceInvalid, not be swallowed as "offline" like every other network
        // failure. This constructs a real CashierViewModel (its DispatcherTimer is an
        // Avalonia dispatcher-bound type), so — same reasoning as the test above —
        // it belongs in this class, on this thread, not a standalone one.
#pragma warning disable xUnit1031 // intentional: see CashierView_with_catalog_and_cart_renders's comment above
        var (vm, received) = Task.Run(SetUpRevokedCashierViewModelAsync).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        try
        {
            Assert.NotNull(received);
            Assert.False(vm.IsOnline);
        }
        finally
        {
            vm.StopBackgroundSync();
        }
    }

    private static async Task<(CashierViewModel Vm, DeviceUnauthorizedException? Received)> SetUpRevokedCashierViewModelAsync()
    {
        var tempDir = CreateTempDir();
        var db = await LocalDatabase.OpenAsync(Path.Combine(tempDir, "pos.db"), "headless-test-key");
        var api = new ApiClient(new HttpClient(new AlwaysUnauthorizedHandler()) { BaseAddress = new Uri("http://fake-server/api/v1/") });
        var sync = new SyncEngine(db, api);
        var signedInAs = new RosterUser("user-1", "Jane Cashier", "CASHIER", ["pos.sell"], MustChangePassword: false);
        var vm = new CashierViewModel(db, sync, api, deviceToken: "fake-token", terminalId: "terminal-1", locationId: "location-1", tenantSubdomain: "acme", signedInAs, onLogout: () => { });

        DeviceUnauthorizedException? received = null;
        vm.OnDeviceInvalid = ex =>
        {
            received = ex;
            return Task.CompletedTask;
        };

        await vm.SyncNowCommand.ExecuteAsync(null);
        return (vm, received);
    }

    private sealed class AlwaysUnauthorizedHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.Unauthorized) { Content = new StringContent("{}") });
    }

    private static async Task<(CashierViewModel Vm, string TempDir)> SetUpCashierViewModelAsync()
    {
        var tempDir = CreateTempDir();
        var db = await LocalDatabase.OpenAsync(Path.Combine(tempDir, "pos.db"), "headless-test-key");
        SeedOneProduct(db);

        // Deliberately unreachable — proves the screen renders its (empty-state) catalog
        // and "offline" status without a server, same as a till with no network. No
        // drawer-open dialog is wired, so LoadAsync's drawer gate just opens at float 0.
        var api = ApiClient.ForServer(new Uri("http://127.0.0.1:1"));
        var sync = new SyncEngine(db, api);
        var signedInAs = new RosterUser("user-1", "Jane Cashier", "CASHIER", ["pos.sell"], MustChangePassword: false);
        var vm = new CashierViewModel(db, sync, api, deviceToken: "fake-token", terminalId: "terminal-1", locationId: "location-1", tenantSubdomain: "acme", signedInAs, onLogout: () => { });
        await vm.LoadAsync();
        return (vm, tempDir);
    }

    private static void SeedOneProduct(LocalDatabase db)
    {
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO products (id, name, is_composite, track_inventory, is_active, updated_at) VALUES ('p1', 'Latte', 0, 1, 1, '2024-01-01T00:00:00Z');
            INSERT INTO variants (id, product_id, sku, price, cost, is_active, updated_at) VALUES ('v1', 'p1', 'LATTE-001', '350', '150', 1, '2024-01-01T00:00:00Z');
            INSERT INTO inventory (variant_id, quantity, updated_at) VALUES ('v1', '12', '2024-01-01T00:00:00Z');
            """;
        cmd.ExecuteNonQuery();
    }

    private static void RenderToPng(Control view, string pngPath)
    {
        var window = new Window { Width = 1024, Height = 700, Content = view };
        RenderWindowToPng(window, pngPath);
    }

    /// <summary>Every dialog view (PaymentDialog, RefundLookupDialog, etc.) is itself a
    /// Window, not a UserControl — it can't be wrapped as another Window's Content
    /// (a Window is already its own top-level), so it's shown directly instead.</summary>
    private static void RenderDialogToPng(Window dialog, string pngPath) => RenderWindowToPng(dialog, pngPath);

    private static void RenderWindowToPng(Window window, string pngPath)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();

        using var bitmap = window.GetLastRenderedFrame();
        Assert.NotNull(bitmap);
#pragma warning disable CS0618 // the BitmapEncoderOptions overload is 12.2+; stick with the simple one until this repo moves past 12.1
        using (var stream = File.Create(pngPath))
            bitmap!.Save(stream);
#pragma warning restore CS0618
        Assert.True(File.Exists(pngPath));
        Assert.True(new FileInfo(pngPath).Length > 0);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"pos-headless-ui-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }
}
