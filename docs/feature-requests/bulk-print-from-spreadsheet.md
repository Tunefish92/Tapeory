# Feature request: bulk printing from a spreadsheet (CSV / Excel)

**Status:** planned, not started · **Requested by:** a user, 2026-09-29 · **Target:** after the
desktop app release (0.5.0), e.g. 0.6.0

> "Can you, please, think about a feature to import some kind of .csv or .xls data import, to
> achieve a bulk print job?"

## Goal

Print one label per row of a spreadsheet: import a CSV or Excel file (or paste cells), match its
columns to the template's fields, check the rows, and print them all as one print job. Works the
same in the web app and the desktop app.

## What already exists

- A print job holds many items, each with its own field values and quantity
  (`CreatePrintJobRequest.Items`). The print page currently always sends a single item, so bulk
  printing needs no new data model.
- Field validation (required fields), barcode encoding (`POST /api/barcodes/encode`), and the
  text-fitting logic can be reused to check rows before printing.
- The Brother driver already accepts a list of labels per transmission
  (`BrotherPrinterDriver.PrintAsync(..., IReadOnlyList<SKBitmap> labels, ...)`).

## User flow

1. On a template's print page: **Import data…** (CSV, XLSX, XLS), or paste cells from the
   clipboard. Copying cells from Excel or Google Sheets gives tab-separated text.
2. **Match columns to fields.** Matching is automatic by field name or label, ignoring case and
   accents ("Name" → `name`); a dropdown per field changes it. An optional **Quantity** column
   sets copies per row. Fields without a column use their default value. A sheet picker for
   workbooks with several sheets, and a "first row is a header" toggle.
3. **Check the data.** A table of all rows marks problems:
   - a required field is empty;
   - a barcode can't be encoded (e.g. invalid EAN-13);
   - text doesn't fit its box.

   Rows can be unticked, and clicking a row previews its rendered label. A summary shows the
   totals, e.g. "148 labels, about 3.6 m of 12 mm tape".
4. **Print.** One print job, one item per row. The print history shows each row's status and
   offers **Reprint failed rows**.

## Technical plan

### Parsing (engine/server, shared by web and desktop)

- New endpoint `POST /api/print-data/parse` (multipart file, or text for pasted data). It returns
  the sheets, columns, rows and a suggested column → field mapping for a given template.
- The uploaded file is only parsed in memory, not stored. The values end up in the job items as
  today.
- Library: **ExcelDataReader** (MIT) for `.xlsx`, `.xls` and CSV.
- CSV: detect the separator (`,`, `;`, tab) and the encoding (UTF-8 with or without BOM, falling
  back to Windows-1252). German Excel saves CSV with semicolons in Windows-1252.
- Excel dates and numbers are printed as Excel displays them (e.g. `29.09.2026`), not as raw
  values.
- Limits: e.g. 5 MB per file and 1,000 rows per job, with a clear message beyond that.

### Validation

- The client checks rows before printing: required fields, barcode encoding, text fit.
- The server validates the job request again (existing `PrintJobService` checks).

### Printing in batches

- Today each item is a separate transmission to the printer and, with SNMP, waits for the
  printer's confirmation each time. That's fine for a few items and slow for hundreds.
- `PrintJobProcessor` should send consecutive items in batches (e.g. 50 labels) as one
  transmission. Cut modes then work across rows: auto cut, or chain printing without waste
  between labels.
- Status is tracked per batch and written to every row in it; a failed batch marks its rows
  failed, so **Reprint failed rows** can create a new job from exactly those.

### Web app (Tapeory.Web)

- Import dialog on the print page (file picker, drag and drop, paste).
- Mapping step and a data table (reuse `DataGrid`) with per-row problems and a label preview.
- Job detail: paginate or virtualize items for large jobs; add **Reprint failed rows**.

### Desktop app (Tapeory.Desktop)

- The same flow with the native file dialog (off the UI thread, like the other dialogs) and the
  grid widget (`ui/grid.rs`); parsing goes through the engine endpoint.

### Tests

- Parsing: CSV variants (separators, encodings, quoted fields with line breaks), `.xlsx` and
  `.xls`, empty cells, Excel number and date cells, several sheets.
- Column matching (names, labels, accents).
- Batch printing against a fake printer (raw port 9100), including a failed batch.
- Extend `Tapeory.Desktop/tests/engine_realtest.py` with a CSV import and a bulk job.

### Docs

- README section "Bulk printing from a spreadsheet", CHANGELOG entry, and texts in all five
  languages (de, en, es, fr, it).

## Decisions (recommended defaults, to confirm when starting)

| Question | Recommendation |
|---|---|
| Formats | CSV, XLSX, XLS and clipboard paste; ODS (LibreOffice) possibly later |
| Quantity column | Optional; without it, each row prints once |
| Remember the column mapping per template | Yes, so re-importing the same spreadsheet needs no clicks |
| Row limit per job | 1,000 |
| Excel dates and numbers | As Excel displays them |
| Web app and desktop app | Together, since the parsing is shared |

## Open questions

- Should a bulk job be saved as a reusable "data set" (reprint the same list later), or is
  "Print again" from the history enough?
- Should rows be printed sorted (e.g. by a column), or always in file order?
