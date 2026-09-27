# Supported printers

Tapeory prints on Brother P-touch (PT) and QL label printers in Brother's raster format. Everything
it knows about a model (print head, resolution, media, and which commands and options it takes)
comes from Brother's official *Raster Command Reference* manuals. The list below covers all 162
PT and QL models on Brother's support websites in all 49 countries and regions, checked against
Tapeory's model table (`Tapeory.Api/Printing/BrotherCatalog.cs`): 34 are supported.

**How to connect**

- **Network:** add the printer with its IP address or hostname; Tapeory prints to its raw port
  (usually 9100) and reads its status over SNMP.
- **USB (or Bluetooth) models:** connect the printer to a machine running CUPS, create a raw queue
  for it, and add that CUPS server in Tapeory as a *print server* with the queue name. When the
  queue prints to the printer's network address, Tapeory also reads its status over SNMP.

Enter the model name in the printer's settings; Tapeory offers the resolutions and cutting options
that model supports.

## Supported

### P-touch, 180 dpi (TZe tape up to 24 mm, HSe heat-shrink tube)

| Model | Connection | High resolution (180 × 360 dpi) | Half cut |
| --- | --- | --- | --- |
| PT-P750W | Network | ✓ | ✓ |
| PT-E550W | Network | ✓ | ✓ |
| PT-P700 | USB (CUPS) | | |
| PT-H500 | USB (CUPS) | | |
| PT-E500 | USB (CUPS) | | |
| PT-P710BT | USB (CUPS) | ✓ | |
| PT-E310BT | USB (CUPS) | | ✓ |
| PT-E510 | USB (CUPS) | ✓ | ✓ |
| PT-E560BT | USB (CUPS) | ✓ | ✓ |

### P-touch, 360 dpi (TZe tape up to 36 mm, HSe heat-shrink tube)

| Model | Connection | High resolution (360 × 720 dpi) | Half cut |
| --- | --- | --- | --- |
| PT-P900W | Network | ✓ | ✓ |
| PT-P950NW | Network | ✓ | ✓ |
| PT-P900 | USB (CUPS) | ✓ | ✓ |
| PT-P910BT | USB (CUPS) | | ✓ |

### QL, 300 dpi (DK rolls up to 62 mm)

Continuous rolls (12–62 mm), die-cut labels (17 × 54 to 62 × 100 mm) and round labels (12, 24,
58 mm). High resolution is 600 × 300 dpi.

| Model | Connection | High resolution | Cutter | Black/red (DK-22251) |
| --- | --- | --- | --- | --- |
| QL-580N | Network | ✓ | ✓ | |
| QL-710W | Network | ✓ | ✓ | |
| QL-720NW | Network | ✓ | ✓ | |
| QL-810W | Network | ✓ | ✓ | ✓ |
| QL-820NWB | Network | ✓ | ✓ | ✓ |
| QL-500 (QL-500A) | USB (CUPS) | | no cutter | |
| QL-550 | USB (CUPS) | | ✓ | |
| QL-560 | USB (CUPS) | | ✓ | |
| QL-570 (QL-570VM) | USB (CUPS) | ✓ | ✓ | |
| QL-600 | USB (CUPS) | ✓ | ✓ | |
| QL-650TD | USB (CUPS) | ✓ | ✓ | |
| QL-700 | USB (CUPS) | ✓ | ✓ | |
| QL-800 | USB (CUPS) | ✓ | ✓ | ✓ |

### QL, 300 dpi wide format (DK rolls up to 104 mm)

Adds 102 and 103.6 mm continuous rolls and 102 × 51, 102 × 152 and 103 × 164 mm die-cut labels.

| Model | Connection | High resolution | Cutter |
| --- | --- | --- | --- |
| QL-1050N | Network | ✓ | ✓ |
| QL-1060N | Network | ✓ | ✓ |
| QL-1110NWB | Network | ✓ | ✓ |
| QL-1115NWB | Network | ✓ | ✓ |
| QL-1050 | USB (CUPS) | ✓ | ✓ |
| QL-1100 | USB (CUPS) | ✓ | ✓ |

**Cutting options:** P-touch printers offer auto cut, cut at end, chain printing, cut marks, and
half cut where the model has it. QL printers offer auto cut, cut at end and cut marks (they have no
half cut or chain printing); the QL-500, without a cutter, prints cut marks only.

## Not supported

**With a computer connection, but no published raster protocol.** Brother publishes no Raster
Command Reference for these, so Tapeory doesn't guess at their format:
PT-1230PC, PT-1500PC, PT-2300, PT-2420PC, PT-2430PC, PT-2450DX, PT-2500PC, PT-2700, PT-2730,
PT-3600, PT-7600, PT-9200DX, PT-9200PC, PT-9500PC, PT-9600, PT-9700PC, PT-9800PCN, PT-D410,
PT-D450, PT-D460BT, PT-D600, PT-D610BT, PT-D800W, PT-E720BT, PT-E800T, PT-E800TK, PT-E800W,
PT-E850TKW, PT-E920BT. The PT-P300BT and PT-N25BT work only with Brother's mobile apps.

**Standalone labelers** with a built-in keyboard and no computer connection can't receive print
jobs: PT-12, PT-24, PT-45M, PT-55, PT-65, PT-70, PT-80, PT-90, PT-170, PT-190, PT-240, PT-550,
PT-900, PT-1000, PT-1005, PT-1010, PT-1080, PT-1090, PT-1100, PT-1100CH, PT-1180, PT-12K,
PT-1260, PT-1280, PT-1280KT, PT-1280SN, PT-1290, PT-1400, PT-1600, PT-1650, PT-1700, PT-1750,
PT-1800, PT-1810, PT-1830, PT-1880, PT-1890, PT-18N, PT-18R, PT-18RKT, PT-1900, PT-1910,
PT-1950, PT-1960, PT-2030, PT-2030AD, PT-2040, PT-2100, PT-2110, PT-2310, PT-2400, PT-2410,
PT-2460, PT-2470, PT-2480, PT-2600, PT-2610, PT-2710, PT-7100, PT-7500, PT-9400, PT-D200 (and
its colour editions PT-D200DR/KN/KT/LB/RK/SN), PT-D201, PT-D202, PT-D210, PT-D220, PT-D400,
PT-E100, PT-E105, PT-E110, PT-E115, PT-E115B, PT-E200, PT-E300, PT-H100, PT-H101, PT-H101C,
PT-H101GB, PT-H102, PT-H103W, PT-H105, PT-H107, PT-H108, PT-H110, PT-H111, PT-H200, PT-H300,
PT-H75, PT-M95, PT-N10, PT-N20.

(Some older models in this group had a serial or USB port on certain versions; none of them has a
published raster protocol either.)

## Sources

- Brother *Software Developer's Manual – Raster Command Reference*: PT-E550W/P750W/P710BT,
  PT-H500/P700/E500, PT-E310BT/E510/E560BT, PT-P900/P900W/P950NW (incl. P910BT), QL-800/810W/820NWB,
  QL-1100/1110NWB, QL-600/710W/720NW, and the QL series command reference
  (QL-500/550/560/570/580N/650TD/700/1050/1060N), all at [support.brother.com](https://support.brother.com).
- Model list: the P-touch and label printer categories of Brother's support websites in all 49
  countries and regions (crawled September 2026). Brother's TD, TJ, RJ and PJ printers use other
  formats and aren't covered.
- Cross-checked against the open-source [brother_ql](https://github.com/pklaus/brother_ql) and
  [ptouch-print](https://git.familie-radermacher.ch/linux/ptouch-print.git) projects.
