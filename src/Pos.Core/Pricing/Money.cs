namespace Pos.Core.Pricing;

public static class Money
{
    public static decimal Round(decimal value, int decimalDigits) => Math.Round(value, decimalDigits, MidpointRounding.AwayFromZero);
}
