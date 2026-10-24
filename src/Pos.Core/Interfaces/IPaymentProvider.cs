namespace Pos.Core.Interfaces;

/// <summary>
/// One tender method behind one interface — Cash and ManualMpesa run fully offline;
/// ServerBackedMpesa (STK push) needs connectivity (Phase 4). Secrets and provider
/// callbacks stay server-side; the till never holds a mobile-money API credential.
/// </summary>
public interface IPaymentProvider
{
    string Method { get; }
    Task<PaymentOutcome> ChargeAsync(PaymentRequest request, CancellationToken ct = default);
}

public sealed record PaymentRequest(decimal Amount, string? CustomerPhone, string? Reference);

public sealed record PaymentOutcome(bool Success, string Status, string? TransactionRef, string? FailureReason);
