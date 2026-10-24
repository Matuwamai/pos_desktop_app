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

/// <summary>
/// Renders the setup screen and the (Phase 1) product list to PNG with no display server —
/// a smoke test, not a pixel-perfect snapshot test: it proves each screen's XAML parses, its
/// bindings resolve against a real view model, and layout completes without throwing.
///
/// Bootstraps the headless Avalonia app itself (a static constructor, once per test run)
/// rather than via Avalonia.Headless.XUnit's [AvaloniaFact]: that package currently pulls in
/// xunit.v3, which collides with this project's xunit v2 (FactAttribute defined in both).
/// </summary>
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
    public void MainView_with_product_list_renders()
    {
        // AppBuilder.SetupWithoutStarting() (the static ctor above) installs an Avalonia
        // dispatcher SynchronizationContext on this thread. Awaiting directly in a [Fact]
        // would post each continuation to that dispatcher's queue, which nothing is
        // pumping until *after* the await — a deadlock. Task.Run hands the async setup to
        // a thread-pool thread with no such context, so it runs to completion normally;
        // only the UI construction below (window, render) touches the dispatcher thread.
#pragma warning disable xUnit1031 // intentional: see the comment above — this is what avoids the deadlock, not what causes it
        var (vm, tempDir) = Task.Run(SetUpMainViewModelAsync).GetAwaiter().GetResult();
#pragma warning restore xUnit1031

        Assert.False(vm.IsOnline); // unreachable server — confirms the offline path was exercised
        Assert.Single(vm.Products);

        var view = new MainView { DataContext = vm };
        RenderToPng(view, Path.Combine(tempDir, "main-product-list.png"));
    }

    private static async Task<(MainViewModel Vm, string TempDir)> SetUpMainViewModelAsync()
    {
        var tempDir = CreateTempDir();
        var db = await LocalDatabase.OpenAsync(Path.Combine(tempDir, "pos.db"), "headless-test-key");
        SeedOneProduct(db);

        // Deliberately unreachable — proves the screen renders its (empty-state) product
        // list and "offline" status without a server, same as a till with no network.
        var sync = new SyncEngine(db, ApiClient.ForServer(new Uri("http://127.0.0.1:1")));
        var signedInAs = new RosterUser("user-1", "Jane Cashier", "CASHIER", ["pos.sell"], MustChangePassword: false);
        var vm = new MainViewModel(db, sync, deviceToken: "fake-token", signedInAs);
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
