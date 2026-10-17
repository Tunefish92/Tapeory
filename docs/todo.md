# To do

Open items, newest last. Each says what is wrong, what is known so far and what "done" means.

## 1. Quantity field can't be cleared on a phone

**Status:** open, investigated only · **Reported:** 2026-10-17

> Mobile: Quantity cannot be deleted. So it's not possible to print 2–9, only 1 or ≥ 10.

**Cause (confirmed in code):** the quantity field on the print page
(`Tapeory.Web/src/printing/PrintTemplatePage.tsx`) clamps its value on every keystroke:
`onChange={(e) => setQuantity(Math.max(1, Number(e.target.value)))}`. Emptying the field gives
`Number("")` = 0, which is clamped back to 1 at once, so the next digit makes "12", "15", … The
bulk print page's **Copies of each label** field (`BulkPrintPage.tsx`, `clampCopies` in
`onChange`) has the same problem.

**Plan**
- [ ] A shared whole-number input (e.g. `Tapeory.Web/src/components/CountInput.tsx`) that keeps
      the typed text as its own state, passes on only whole numbers within min…max, and puts back
      the last valid number when the field is left empty or invalid (on blur). `inputMode="numeric"`
      for the phone keypad.
- [ ] Use it for the quantity (`PrintTemplatePage.tsx`) and the bulk copies (`BulkPrintPage.tsx`).
- [ ] Tests: the field can be emptied and retyped with one digit; empty or out-of-range input is
      restored on blur. CHANGELOG entry under Fixed.
- [ ] Try it on a phone (iOS Safari and Android Chrome): clear the field, type 2…9, print.
- [ ] Optional: the same per-keystroke pattern is in the printer port (`PrintersPage.tsx`), label
      width (`LabelSizeFields.tsx`) and the number fields in `PropertiesPanel.tsx`; switch them
      over if they show the same problem.

**Done when** any quantity from 1 to the maximum can be typed on a phone and is printed that
many times.

## 2. Elements print at a different position than placed in the editor

**Status:** open, investigated only · **Reported:** 2026-10-17

> Inserted elements have an offset. If I place an element in the centre, it is printed more to
> the top instead of where I placed it. Check that every element is printed exactly where it was
> placed.

**Known so far**

- **Text and dynamic fields: confirmed cause, in code.** The editor (Konva 10) and the print
  renderer put a text line's baseline in different places:
  - Editor: Konva puts the baseline at `(ascent − descent) / 2 + lineHeight / 2`, from the font's
    real metrics. With line height 1.2 and Arial (ascent ≈ 0.905, descent ≈ 0.212) that is about
    **0.95 × font size** below the top of the line.
  - Print: `LabelRenderer.DrawText` uses a fixed approximation,
    `baselineY = lineHeight * i + fontSizeMm * 0.8f` ("Approximate baseline").
  - So printed text sits about **0.15 × font size higher** than in the editor: ~0.6 mm for 12 pt,
    ~1.3 mm for 24 pt. The gap grows with the font size and differs per font, which matches "more
    to the top".
- **Barcode text:** same kind of mismatch. The editor places it at `barsHeight + textHeight * 0.1`
  with Konva's metrics (`BarcodeShape.tsx`), the renderer at `top + height * 0.85f` as a baseline
  (`DrawBarcodeText`). Needs checking.
- **Everything else (rectangles, ellipses, lines, images, barcode bars):** both sides translate by
  `obj.X`/`obj.Y` in mm, so no offset is expected in the rendered bitmap. Still to be checked on
  paper, because a shift there would come from the printer path instead:
  - `BrotherRasterEncoder.RasterLines` centres the label's rows on the printable pins
    (`firstRow = (label.Height − PrintPins) / 2`) and starts at `OffsetPins`. If the printable
    pins aren't centred on the tape for a given model/tape, or the offset table entry is off, the
    whole print shifts across the tape (which is "up/down" in the editor).
  - The rotated die-cut path (`BrotherLabelRaster`, quarter turn) and `PrintArea` margins shown in
    the editor must agree with that centring.

**Plan**
- [ ] Add a calibration template: a 0.3 mm frame on the label edge, centre cross-hair, and text in
      a few sizes/fonts with a hairline at the top of each text box. Print it on the tapes and
      rolls at hand (e.g. TZe 12 / 24 mm, DK-62) and photograph or measure it.
- [ ] Render-level test (API): render that template to a bitmap and compare object positions
      against the editor's geometry, so the rest of the work is checked without a printer.
- [ ] Text: replace the `0.8f` approximation with the font's real metrics, the same formula Konva
      uses: `baseline = (ascent − descent) / 2 + lineHeight / 2` with `SKFont.Metrics`
      (`-Ascent`, `Descent`). Do the same for barcode text, or place it the same way in both.
- [ ] Check `TextFitter` / `fitText.ts` still agree on line breaks and fitted size after the change.
- [ ] If the calibration print still shows a shift for non-text objects: check `OffsetPins` /
      centring per model and tape against Brother's raster tables, and the rotated die-cut path.
- [ ] Compare the server preview image (`POST /api/templates/{id}/preview`) with the editor too —
      it uses the same renderer, so it should show the same offset as the print before the fix.
- [ ] Tests that pin the result: text box top/centre positions within ~1 px at print dpi for
      several fonts and sizes; CHANGELOG entry.

**Done when** the calibration template prints with every element within about 0.2 mm (one or
two dots) of where it is in the editor, on each tape/roll tested, and existing templates' text
moves down to where the editor shows it.
