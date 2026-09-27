namespace Tapeory.Api.Data.Entities;

/// <summary>Print resolution. Standard is the head's native 180 × 180 dpi; High doubles the
/// resolution along the tape (180 × 360 dpi) on models that support it, and prints slower.</summary>
public enum PrintQuality
{
    Standard = 0,
    High = 1
}
