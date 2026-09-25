using Tapeory.Api.Data;
using Tapeory.Api.Data.Entities;
using Tapeory.Api.Printing;
using Microsoft.EntityFrameworkCore;

namespace Tapeory.Api.Printers;

public sealed class PrinterService(AppDbContext db)
{
    public Task<List<Printer>> ListAsync(CancellationToken cancellationToken) =>
        db.Printers.OrderBy(printer => printer.Name).ToListAsync(cancellationToken);

    public Task<Printer?> GetByIdAsync(int id, CancellationToken cancellationToken) =>
        db.Printers.SingleOrDefaultAsync(printer => printer.Id == id, cancellationToken);

    public async Task<Printer> CreateAsync(CreatePrinterRequest request, CancellationToken cancellationToken)
    {
        // The first printer ever created automatically becomes the default, so there's always
        // one selected once at least one printer exists.
        var isFirstPrinter = !await db.Printers.AnyAsync(cancellationToken);

        var printer = new Printer
        {
            Name = request.Name,
            Model = request.Model,
            ConnectionType = ParseConnectionType(request.ConnectionType),
            Address = request.Address,
            Port = request.Port ?? 9100,
            PrintServerAddress = request.PrintServerAddress,
            UsbIdentifier = request.UsbIdentifier,
            LabelMediaWidthMm = request.LabelMediaWidthMm,
            LabelMediaHeightMm = request.LabelMediaHeightMm,
            Enabled = request.Enabled,
            IsDefault = isFirstPrinter
        };

        db.Printers.Add(printer);
        await db.SaveChangesAsync(cancellationToken);

        return printer;
    }

    public async Task<Printer?> UpdateAsync(int id, UpdatePrinterRequest request, CancellationToken cancellationToken)
    {
        var printer = await db.Printers.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (printer is null)
        {
            return null;
        }

        printer.Name = request.Name;
        printer.Model = request.Model;
        printer.ConnectionType = ParseConnectionType(request.ConnectionType);
        printer.Address = request.Address;
        printer.Port = request.Port ?? 9100;
        printer.PrintServerAddress = request.PrintServerAddress;
        printer.UsbIdentifier = request.UsbIdentifier;
        printer.LabelMediaWidthMm = request.LabelMediaWidthMm;
        printer.LabelMediaHeightMm = request.LabelMediaHeightMm;
        printer.Enabled = request.Enabled;
        printer.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return printer;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var printer = await db.Printers.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (printer is null)
        {
            return false;
        }

        var wasDefault = printer.IsDefault;

        db.Printers.Remove(printer);
        await db.SaveChangesAsync(cancellationToken);

        // Keep the "there's always a default once any printer exists" invariant after a delete.
        if (wasDefault)
        {
            var nextPrinter = await db.Printers.OrderBy(p => p.Name).FirstOrDefaultAsync(cancellationToken);

            if (nextPrinter is not null)
            {
                nextPrinter.IsDefault = true;
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        return true;
    }

    public async Task<bool> SetDefaultAsync(int id, CancellationToken cancellationToken)
    {
        var printer = await db.Printers.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);

        if (printer is null)
        {
            return false;
        }

        var others = await db.Printers
            .Where(p => p.Id != id && p.IsDefault)
            .ToListAsync(cancellationToken);

        foreach (var other in others)
        {
            other.IsDefault = false;
        }

        printer.IsDefault = true;
        await db.SaveChangesAsync(cancellationToken);

        return true;
    }

    public async Task RecordConnectionResultAsync(
        Printer printer, ConnectionTestResult result, CancellationToken cancellationToken)
    {
        printer.LastConnectionStatus = result.IsSuccess
            ? PrinterConnectionStatus.Success
            : PrinterConnectionStatus.Failed;
        printer.LastConnectionCheckedAt = DateTimeOffset.UtcNow;
        printer.LastErrorMessage = result.ErrorMessage;
        printer.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);
    }

    private static PrinterConnectionType ParseConnectionType(string value) =>
        Enum.TryParse<PrinterConnectionType>(value, true, out var parsed)
            ? parsed
            : throw new ArgumentException($"Unknown connection type '{value}'.");
}
