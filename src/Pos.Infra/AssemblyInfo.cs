using System.Runtime.CompilerServices;

// Pos.Tests reuses SyncEngine.UpsertAsync directly to seed rows for its fake-server
// tests (see PushEngineTests.cs) — the same reasoning SaleService has for reusing it:
// one upsert helper, not a second one just for tests.
[assembly: InternalsVisibleTo("Pos.Tests")]
