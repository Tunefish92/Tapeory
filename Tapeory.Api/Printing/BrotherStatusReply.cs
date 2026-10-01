namespace Tapeory.Api.Printing;

/// <summary>
/// The 32-byte status a Brother label printer sends on its own connection: as the answer to a
/// status request (ESC i S), and by itself while it prints ("phase change", "printing completed",
/// "error occurred"). Network printers don't answer on their raw port, so this is how a USB
/// printer says which tape is loaded and whether the labels came out.
/// </summary>
public sealed record BrotherStatusReply(byte Error1, byte Error2, byte MediaWidthMm, byte MediaType, byte StatusType, byte Phase)
{
    public const int Length = 32;

    /// <summary>Invalidate (so a half-sent job can't swallow the request), initialise, request status.</summary>
    public static readonly byte[] Request = [.. new byte[200], 0x1B, 0x40, 0x1B, 0x69, 0x53];

    public const byte PrintingCompleted = 0x01;
    public const byte ErrorOccurred = 0x02;

    public static BrotherStatusReply? Parse(ReadOnlySpan<byte> reply) =>
        reply.Length >= Length && reply[0] == 0x80 && reply[1] == 0x20
            ? new BrotherStatusReply(reply[8], reply[9], reply[10], reply[11], reply[18], reply[19])
            : null;

    /// <summary>What stops the printer, in words, or null.</summary>
    public string? Problem
    {
        get
        {
            var problems = new List<string>();

            if ((Error1 & 0x01) != 0) problems.Add("no tape");
            if ((Error1 & 0x02) != 0) problems.Add("end of tape");
            if ((Error1 & 0x04) != 0) problems.Add("cutter jam");
            if ((Error1 & 0x08) != 0) problems.Add("weak batteries");
            if ((Error1 & 0x40) != 0) problems.Add("wrong power adapter");
            if ((Error2 & 0x01) != 0) problems.Add("the tape doesn't match the job");
            if ((Error2 & 0x10) != 0) problems.Add("cover open");
            if ((Error2 & 0x20) != 0) problems.Add("overheated");
            if ((Error2 & 0x40) != 0) problems.Add("the tape can't be fed");
            if ((Error2 & 0x80) != 0) problems.Add("system error");
            if (problems.Count == 0 && (Error1 != 0 || Error2 != 0)) problems.Add($"error {Error1:X2} {Error2:X2}");

            return problems.Count == 0 ? null : string.Join(", ", problems);
        }
    }

    /// <summary>The same picture a network printer gives over SNMP, so both are checked alike.</summary>
    public PrinterStatusSnapshot ToSnapshot() => new(
        DeviceStatus: Problem is not null ? 1 : Phase == 0x01 ? 4 : 3,
        ErrorState: [],
        Display: Problem ?? (Phase == 0x01 ? "PRINTING" : "READY"),
        LabelCount: null,
        MediaName: MediaWidthMm > 0 ? $"{MediaWidthMm}mm" : null);
}
