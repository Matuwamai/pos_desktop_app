namespace Pos.Hardware.EscPos;

/// <summary>48 columns is the standard for an 80mm printer's Font A — the default. A
/// 58mm printer (32 cols) or a printer forced into Font B would need a different value;
/// exposed as a printer-settings field rather than hardcoded for that reason.</summary>
public sealed record PrinterProfile(int CharsPerLine = 48);
