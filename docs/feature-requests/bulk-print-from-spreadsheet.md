# Feature request: bulk printing from a data file (Excel, CSV, text)

**Status:** implemented on the branch `feature/bulk-print` (2026-10-02), not yet tested on a real
printer · **Requested:** 2026-09-29 by a user, flow specified 2026-10-02 · **Target:** 0.6.0

## What was built differently from the plan below

- Checking and rendering are one pass ("Checking and rendering the labels… 96 of 148"), in chunks
  of 25 rows, not two separate stages.
- A batch is 25 labels and holds whole rows, so the copies of one row stay together.
- The endpoint for reprinting is `POST /api/print-jobs/{id}/reprint-unprinted`. Rows that weren't
  sent get the new status `Cancelled` (shown as "Not printed").
- The web app remembers the column matching per template (in the browser); the desktop app
  doesn't yet.
- The limits are 100 MB per file and 5,000 rows per job (the plan below says 5 MB and 1,000),
  as decided on 2026-10-02. The web app shows long row lists a page at a time.
- Saved profiles (asked for on 2026-10-02, not in the plan below): a new table
  `BulkPrintProfiles` (migrations for MySQL and SQLite), `GET`/`PUT`
  `/api/templates/{id}/bulk-print-profiles` and `DELETE /api/bulk-print-profiles/{id}`. This
  replaces the plan's "remember the matching per template" for the desktop app.
- JSON files and data from a web address (asked for on 2026-10-02): `JsonDataReader`, and
  `POST /api/print-data/fetch` (`PrintDataFetcher`: GET with an optional header, 30 s, same size
  limit). Profiles store the address and the header.
- Not done: paging the rows of a job in the API (`GET /api/print-jobs` still returns every row
  of every job), and the three open questions at the end.

> "Can you, please, think about a feature to import some kind of .csv or .xls data import, to
> achieve a bulk print job?"

## Goal

Next to printing a single label, a template can be printed in bulk: the user imports a data file
(`.xlsx`, `.csv`, `.txt`), each row fills the template's fields, and all rows are printed as one
print job. The single-label print page stays as it is. The flow is the same in the web app and
the desktop app.

## What already exists

- A print job holds many items, each with its own field values and quantity
  (`CreatePrintJobRequest.Items`). The print page always sends one item today, so bulk printing
  needs no new data model and no migration.
- `PrintJobService` already checks every item (required fields, quantity), and
  `PrintJobProcessor` checks barcodes and renders each item.
- `POST /api/templates/{id}/preview` renders one label for given field values.
- The Brother driver accepts a list of labels per transmission
  (`BrotherPrinterDriver.PrintAsync(..., IReadOnlyList<SKBitmap> labels, ...)`).
- A job is stopped before printing when the tape in the printer doesn't match the template.

## User flow

A **Bulk print** button on the template's print page (and on the template card's menu) opens a
wizard with four steps. A step indicator at the top shows where the user is; **Back** never loses
what was entered.

### 1. Choose the file

- File picker and drag and drop (web), native file dialog (desktop). Accepted: `.xlsx`, `.csv`,
  `.txt`.
- While the file is uploaded and read, a progress bar with a status line: "Uploading… 62 %",
  then "Reading the file…".
- A workbook with several sheets: a sheet picker (the first sheet with data is preselected).

### 2. Match the data to the label's fields

Two things are detected automatically. The user is only asked when detection fails.

**Separator (CSV and text files).** Tapeory tries comma, semicolon, tab and pipe and picks the
one that splits the first lines into the same number of columns (two or more), with quoted values
respected. A file with one value per line is a valid one-column file.

- Detected: nothing to do; the separator is shown in a small line ("Separated by semicolons ·
  Change") so it can still be corrected.
- Not detected (no separator gives a consistent table, or two do equally well): the wizard asks
  **"Which separator does the file use?"** with the choices comma, semicolon, tab, pipe, space
  and "other" (one character). The first lines of the file are shown below as a live table, so
  the right choice is visible at once.

**Header row.** The first row counts as a header when at least one of its cells is a field's name
or label (compared without case, accents and surrounding spaces: "Name " → `name`).

- Header detected and every required field has a column: the columns are matched by name and the
  wizard goes straight to step 3. The matching is shown and can be changed.
- No header detected: the wizard asks **"Which column belongs to which field?"** Each field of
  the label gets a dropdown with the columns ("Column A", "Column B", …, each with its first
  values as a hint). A switch "The first row is a header, don't print it" is off.
- Header detected, but a required field has no column: the same question, only for the fields
  that are still open; the matched ones are prefilled.

Rules for the matching:

- A required field needs a column, or the user can't continue. An optional field without a
  column uses its default value.
- Columns that match no field are ignored (shown greyed out).
- An optional **Quantity** column (header `quantity`, `qty`, `copies`, `anzahl`, `menge`, or
  chosen by hand) sets the copies per row; without it each row prints once.
- The matching is remembered per template, so the same file needs no clicks next time.

### 3. Check and look through the labels

Tapeory now gathers the data and renders every label. A progress bar and a status line show the
stage and the count:

1. "Reading the rows… 148 found"
2. "Checking the data… 96 of 148"
3. "Rendering the labels… 96 of 148"

The bar is determinate (rows done / rows in total) and can be cancelled. Labels can already be
looked at while the rest is still rendering.

Then the step shows:

- **A label viewer:** the rendered label, large, with "Label 12 of 148", previous/next buttons
  (also the arrow keys) and a field to jump to a number. Beside it, the row's values.
- **A table of all rows** with a tick per row, the values, and a problem column. Clicking a row
  shows its label. A filter "Only rows with problems".
- **Problems** a row can have:
  - a required field is empty;
  - a barcode can't be encoded with this value (for example an invalid EAN-13);
  - the text doesn't fit its box (a warning, the row can still be printed).

  Rows with an error are unticked and can't be ticked; rows with a warning stay ticked.
- **A summary:** "146 labels will be printed, 2 rows have problems · about 3.6 m of 9 mm tape".

### 4. Print

- Printer, quality and cut mode as on the single-label page, then **Print 146 labels**.
- The wizard switches to the job's progress: a progress bar "Printing label 37 of 146", the
  printer's state, and a **Stop** button that cancels the labels not yet sent.
- When it's done: "146 labels printed", or what failed with **Reprint failed rows**. The job is in
  the print history like any other, with one line per row.

## Technical plan

### Reading the file (server/engine, shared by web and desktop)

- New endpoint `POST /api/print-data/parse` (multipart: the file, plus optional `separator`,
  `sheet`, `templateId`). It returns:
  - `sheets` (names) and the chosen sheet;
  - `separator` and `separatorDetected` (false = the client must ask);
  - `hasHeader` (detected against the template's fields) and `columns` (header texts or
    "Column A…");
  - `rows` (all rows as string arrays);
  - `suggestedMapping` (field name → column index, and the quantity column).

  Calling it again with an explicit `separator` or `sheet` re-reads the file with that choice.
- The file is only read in memory and never stored. The values end up in the job items, as today.
- Library: **ExcelDataReader** (MIT) for `.xlsx`; CSV and text files are read with our own small
  reader (quotes, doubled quotes, line breaks inside quotes), so separator detection is in our
  hands.
- Encoding: UTF-8 with or without BOM, UTF-16 with BOM, otherwise Windows-1252 (German Excel
  saves CSV with semicolons in Windows-1252).
- Excel dates and numbers are taken as Excel displays them (`29.09.2026`, `1,50`), not as raw
  values.
- Empty rows are skipped; values are trimmed.
- Limits: 5 MB per file and 1,000 rows per job, with a clear message beyond that.
- New code: `Tapeory.Api/PrintData/` (`DelimitedTextReader`, `SeparatorDetector`,
  `HeaderDetector`, `SpreadsheetReader`, `PrintDataController`).

### Checking and rendering the rows

- New endpoint `POST /api/templates/{id}/check-rows`: takes up to 50 rows of field values and
  returns, per row, its errors and warnings. It runs the same code the print job runs
  (`FieldValueValidator`, `BarcodeValidation`, the renderer), so a row that passes here prints.
- The client sends the rows in chunks of 50. That gives the progress bar real numbers without
  any server-side state, and cancelling is simply not sending the next chunk.
- The label viewer loads a row's image through the existing preview endpoint when the row is
  shown, and preloads the next and previous ones; images are kept in memory for the session.
- Text-fit warning: the renderer reports when a text had to be cut off or shrunk below its
  minimum size. To check when starting: what the renderer does today with text that's too long.

### Printing in batches

- Today every item is its own transmission and, with status reporting, waits for the printer's
  confirmation each time. That's fine for a few items and slow for hundreds.
- `PrintJobProcessor` sends consecutive items in batches (for example 25 labels) as one
  transmission. Cut modes then work across rows: auto cut, or chain printing without waste
  between labels.
- Status is written per batch to every row in it. A failed batch marks its rows as failed, and
  the following batches aren't sent (the printer needs attention anyway).
- **Stop:** new endpoint `POST /api/print-jobs/{id}/cancel`; the processor checks between
  batches and marks the remaining rows as cancelled (a new value in `PrintJobStatus`, which is
  stored as a number, so no migration is needed).
- **Reprint failed rows:** creates a new job from the failed and cancelled rows of a job.
- `PrintJobResponse` gets counts (`total`, `completed`, `failed`) so the progress bar doesn't
  need all items; the job page loads items in pages for large jobs.

### Web app (Tapeory.Web)

- New page `printing/BulkPrintPage.tsx` (route `/templates/:id/bulk-print`) with the four steps
  as components, plus `api/printData.ts`.
- Upload with progress (`XMLHttpRequest` upload events, since `fetch` has none).
- A shared `ProgressBar` component with a status line; reuse `DataGrid` for the row table.
- Job detail page: progress bar for running jobs, paged items, **Stop**, **Reprint failed rows**.

### Desktop app (Tapeory.Desktop)

- New `ui/bulk_print.rs` with the same four steps; the file is picked with the native dialog
  (off the UI thread, like the other dialogs) and sent to the engine's parse endpoint.
- Chunked checking runs on a worker thread and reports progress to the UI (as the updater's
  download does); `egui::ProgressBar` with the status line; the row table uses `ui/grid.rs`.

### Tests

- Reader: separators (comma, semicolon, tab, pipe), ambiguous and undetectable files, quoted
  values with separators and line breaks, encodings, one-column text files, empty rows.
- Header detection: header present, absent, partly matching; names against labels; accents.
- `.xlsx`: several sheets, number and date cells, empty cells.
- `check-rows`: required fields, invalid barcodes, text that doesn't fit.
- Batches against the fake printer (raw port 9100): several batches, a failed batch, a stopped
  job, reprinting the failed rows.
- Web: the wizard's steps, including the two questions (separator, columns).
- `Tapeory.Desktop/tests/engine_realtest.py`: a CSV import and a bulk job.
- On the real printer: a small bulk job (9 mm tape) over network and USB.

### Docs

- README section "Bulk printing", CHANGELOG entry, and texts in all five languages (de, en, es,
  fr, it).

## Work order

1. Reader, separator and header detection, parse endpoint, with tests.
2. `check-rows` endpoint.
3. Web wizard, steps 1 to 3 (printing through the existing item-by-item processor).
4. Batches, job progress, Stop and Reprint failed rows.
5. Desktop wizard.
6. Real-printer test, docs, translations.

Steps 1 to 3 already give a working bulk print; step 4 makes it fast.

## Decisions (recommended defaults, to confirm when starting)

| Question | Recommendation |
|---|---|
| Formats | `.xlsx`, `.csv`, `.txt`; old `.xls` comes for free with ExcelDataReader; `.ods` and pasting from the clipboard possibly later |
| When is the first row a header? | When at least one cell is a field's name or label |
| Unmatched required field | The user must pick a column; no printing with empty required fields |
| Quantity column | Optional; without it, each row prints once |
| Remember the column matching per template | Yes |
| Row limit per job | 1,000 |
| Excel dates and numbers | As Excel displays them |
| Rows with errors | Left out, listed, and the rest can be printed |
| A batch fails | The job stops; the rest can be reprinted with one click |
| Web app and desktop app | Together, since reading and checking are shared |

## Open questions

- Should a bulk job be saved as a reusable data set (print the same list again later), or is
  "Print again" from the history enough?
- Should rows be printed sorted (by a column), or always in file order?
- Is a **Stop** button for a running job wanted in the first version?
