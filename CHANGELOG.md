# Changelog

This file records all notable changes to Tapeory.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0, minor
versions may include breaking changes.

## [Unreleased]

### Added
- Template files: every template has a **Download** button (cards, list rows and the desktop
  app) that saves it as a `.tapeory` file, and **Import template** takes such a file back in, in
  the web app and the desktop app. The file is readable text (JSON) and holds everything needed
  to rebuild the label elsewhere: the label size, all objects (texts, barcodes, shapes, fields),
  the fields' names, labels and defaults, name, description, group and tags, and the images
  themselves. An imported template gets its own copies of the images, which are checked like
  uploaded ones (type, size, scripts stripped from SVGs); if one is refused, nothing is created.
  The imported template is a private draft of the account that imported it.

### Changed
- The templates page has one **Import template** button for `.tapeory` and `.lbx` files, in
  place of "Import .lbx".
- The earlier export (`.tapeory.json`, which left the images out) is replaced by `.tapeory`;
  files in the old format can still be imported.

## [0.5.0] - 2026-10-01

Tapeory as a desktop app for Linux, and printing to USB printers.

### Added
- A desktop app for Linux (x86_64, glibc 2.35 or newer: Ubuntu 22.04, Debian 12 and later),
  without Docker or a server: an AppImage and a tar.gz, attached to each GitHub release.
  Everything it needs is included. It has the web app's features and look (editor, `.lbx`
  import, printing, print history, printers, backups, statistics, accounts, five languages,
  light and dark), written in Rust with egui, with Tapeory's server running invisibly as its
  engine. The editor draws with the same fonts the label is printed with; its tools sit in a
  column on the right, and the canvas and the selected object's properties fit the window
  together, with the zoom fitted to the label. Tested on Ubuntu 22.04 and 24.04, Debian 12,
  Fedora, openSUSE Tumbleweed and Arch.
- The Windows version of the desktop app is built but on hold: Windows Defender blocks the
  unsigned program as a false positive, so releases don't include it until the builds are
  code-signed. Code signing through SignPath Foundation is prepared (signing steps in CI, the
  code signing policy and a privacy section in the README, product name and version in the
  Windows programs and setup).
- Settings → About → **Add to application menu** puts the desktop app into the desktop's
  application menu (a menu entry and an icon in the user's own folders), since an AppImage has
  no installer; the same button removes it again.
- On first start, the desktop app keeps its data in a local database on the computer (SQLite, no
  accounts) or in a MySQL/MariaDB database shared with a Tapeory server.
- The AppImage updates itself from Settings → About: the new release is downloaded, checked
  against its SHA-256, installed and started.
- USB printers: Tapeory prints to Brother printers plugged into the computer (on Linux through
  `/dev/usb/lp*`), listed by name in the printer form. Over USB the printer is asked which tape
  is loaded, a label for another tape width is refused before it's sent, and each label is
  followed until it's out, as over the network. With a shared database, a USB printer belongs
  to the computer it's plugged into, and only that computer prints its jobs.
- In Docker, a USB printer passed in with `--device /dev/usb/lp0` works without further setup:
  the container joins the device's group.
- The desktop app draws with OpenGL and falls back to Vulkan through wgpu (Direct3D first on
  Windows), so it also opens on computers without a working OpenGL driver, e.g. virtual
  machines. If no renderer works, it says so in a message box instead of failing silently, and
  startup problems and crashes are written to `logs/app.log`.
- A `LICENSE` file (MIT).
- A real-life test (`Tapeory.Desktop/tests/engine_realtest.py`) that runs the packaged engine
  through every feature against a fake printer and leaves a sample database behind.
- Tapeory's server runs on SQLite as well as MySQL; the test suite runs on both.
- The update check lists the release's files and their checksums.

### Changed
- Test Print uses the tape the printer reports instead of a fixed 25 mm label, and is refused
  with an explanation when the tape isn't known and no test print size is set. A job for the
  wrong tape width leaves the printer waiting with an error until it's cancelled there.
- A printer that has stopped with an error its status bits don't show (the display says ERROR,
  e.g. while a job for another tape waits) is recognised: jobs are refused with the reason
  instead of being sent.
- Printers added by hostname keep their tape detection and print status where looking the name
  up is slow (e.g. in a container): the lookup no longer counts against the status timeout.
- The USB connection test also checks that Tapeory may write to the printer, instead of
  reporting success for a printer every job would then fail on.
- New text, fields, shapes, barcodes and images are placed inside the label, also on narrow
  tape, and new text starts in a font the server has (Inter where Arial isn't installed).
- German texts address the reader informally throughout.
- Database queries are no longer written to the log at the Information level, so the print
  queue doesn't add a log line every second.
- Libraries updated, none with known vulnerabilities: EF Core 9.0.20, MySqlConnector 2.6.2,
  SkiaSharp 4.153.1, the test packages, the web app's build tools, and in the desktop app
  reqwest 0.13, rfd 0.17, dirs 7 and sha2 0.11. EF Core stays on 9 until there's a MySQL provider
  for EF Core 10 (see `docs/feature-requests/mysql-provider-for-ef-core-10.md`).
- The server no longer writes to the Windows event log.

### Fixed
- Signing in survives a container update: the keys that sign the session cookie are kept in the
  storage folder (`config/keys`) instead of the container's temporary files, where every new
  container signed everyone out.
- Web app: on the printers page, long button labels (e.g. in German) no longer wrap inside the
  buttons; a user without administrator rights who opens the page by its address sees the list
  without the buttons the server refuses anyway; the template details in the editor no longer
  take up a whole phone screen.
- Desktop app: the Add Printer dialog shows its buttons and error messages without scrolling;
  printer cards keep to the page width and their buttons no longer overlap with long labels;
  the editor stays inside the window when a barcode is selected or labels are long, and Save
  and Publish stay within reach below an import notice; the template search field keeps its
  width; the status column fits "Veröffentlicht"; files are saved to the Downloads folder by
  default; texts say "on this computer" where the web app says "on the server"; an error isn't
  shown twice on a printer card.

## [0.4.0] - 2026-09-27

User accounts with administrator and user roles, and private and public templates.

### Added
- User accounts. Right after the database setup, the first account is created, and it is the
  administrator; from then on, everyone signs in. Administrators add accounts in Settings → Users
  as administrators or users, change their role or name, disable them, reset their password, or
  delete them; Tapeory always keeps one active administrator. Users design, import and print
  labels; printers, backups, statistics and accounts are for administrators.
- Everyone can change their own password from the account menu in the header, which signs them
  out on their other devices. New accounts and reset passwords get a temporary password that has
  to be replaced on first sign-in.
- The print history shows who printed each job.
- `tapeory reset-password <user>` on the server (`docker exec tapeory tapeory reset-password
  <user>`) gives a locked-out account a temporary password.
- Signing in pauses for 15 minutes after 5 wrong passwords for an account, or 20 from one address.
- Private and public templates. New, imported and duplicated templates are private to the account
  that made them; the owner (or an administrator) makes one public from the editor, and then every
  account can see, print and duplicate it, while only the owner and administrators can change it.
  Someone else's public template opens read-only, with "Duplicate to edit". Administrators see and
  change every template. The templates page filters by Mine, Public and All, and shows whose each
  template is.
- Each account sees only its own print history; administrators see everyone's. The dashboard only
  names templates you can see.
- Deleting an account asks whether its templates move to you or are deleted with it.
- Label backups keep each template's owner and visibility.
- Uploaded images are only served to accounts that can see a template using them.

### Changed
- Installs upgraded from 0.3 stay open until the first account is created, with a banner offering
  to create it. Restoring a database backup restores its accounts too.
- Existing templates become public, with no owner ("shared"), so nobody loses any after upgrading;
  only administrators can change them.
- Docker images are published only for releases, tagged with the version (`0.4.0`) and `latest`
  (the newest release). The `sha-<commit>` and `0.3`-style tags are gone; a manual "Clean up
  image tags" workflow removed the old ones.

## [0.3.0] - 2026-09-27

Barcodes, QL printers and 360 dpi P-touch printers. QL and PT-P900-series printing is built from
Brother's official command references; only the PT-P750W has been tested on real hardware so far.

### Added
- Barcodes and QR codes in the editor: Code 128, Code 39, EAN-13, EAN-8, UPC-A, UPC-E, ITF,
  Codabar, QR, Data Matrix, PDF417, and Aztec. A barcode encodes a fixed value or a template
  field filled in when printing (share a text field's name to print the same value as text and
  as a barcode). 1D barcodes can show their value under the bars, EAN/UPC with the check digit.
  Encoding happens on the server (ZXing.Net), so the editor shows exactly what prints; bars are
  snapped to the printer's dots so they stay scannable at 180 dpi.
- A value a barcode can't encode (say, letters in an EAN-13) is reported when the print job is
  submitted and in the editor, instead of printing a broken code.
- Brother QL label printers: all 21 QL models (QL-500 through QL-1115NWB) on DK continuous
  rolls, die-cut and round labels up to 104 mm, with 600 dpi high resolution, and black/red
  printing on DK-22251 with the QL-800 series. Die-cut templates designed the other way round are
  turned to fit.
- 360 dpi P-touch printers (PT-P900, PT-P900W, PT-P950NW, PT-P910BT) with TZe tape up to 36 mm and
  720 dpi high resolution, and the PT-E310BT, PT-E510 and PT-E560BT. Heat-shrink tube printing on
  every P-touch model that takes it.
- A printer model table from Brother's Raster Command References: each model gets its own print
  head, media, commands, resolutions and cutting options (no half cut or chain printing on QL
  printers, cut marks only without a cutter). The printer form lists the models and says
  whether one prints over the network or through a CUPS server; [docs/printers.md](docs/printers.md)
  lists every PT and QL model Brother makes and whether Tapeory supports it.
- Ellipses in the editor.
- Tape detection and printer-confirmed printing through a CUPS queue: Tapeory reads the queue's
  device address (socket://, ipp://, lpd://…) and asks the printer itself over SNMP. A wrong tape
  or a printer error stops the job before it reaches CUPS, a printer that stops mid-job gets the
  job cancelled on the server, and a job completes when the printer's label counter confirms it.
- Printer address fields reject a URL or CUPS device address (like `socket://10.0.0.184:9100`)
  with a hint to enter just the host name or IP address.
- `.lbx` import converts barcodes (a merged barcode becomes a field), rectangles, rounded
  rectangles, ellipses, and lines and polylines (P-touch's `draw:poly`), with their outline and fill; decorative frames become simple
  borders. Free-form shapes and rare barcode types are reported as warnings.
- Update check: Settings → About shows next to the version whether a newer release is out on
  GitHub, with a link to its release notes. The server asks GitHub at most every six hours.

### Changed
- The footer shows just the copyright, "© <year> by Tunefish", linking to Tunefish's GitHub
  repositories.
- New screenshots in the README, on Docker Hub and in the Unraid template.

### Fixed
- The DK-11209 media preset was turned sideways: it runs 62 mm across the roll with 29 mm long labels.

## [0.2.1] - 2026-09-27

### Added
- Tape detection: Tapeory reads which tape a directly connected printer has loaded (over SNMP).
  The print form warns when it doesn't match the template, a job for the wrong tape stops
  before anything is printed, and Test Connection shows the loaded tape.
- Cut marks: a cutting option that doesn't cut, printing all labels as one strip with a dashed
  line at each cut to cut by hand.
- The image carries Unraid's `net.unraid.docker.icon` and `net.unraid.docker.webui` labels, so
  the icon and the WebUI link show up on Unraid's Docker page even without the template.
- Support moved to GitHub Discussions; the Unraid template and Community Applications profile
  link there.

### Changed
- CI uses the current major versions of its GitHub Actions.

## [0.2.0] - 2026-09-27

The first public and first stable release. Tapeory is still early: expect rough edges and
breaking changes between minor versions until 1.0. Everything below is new.

### Added

**Setup and deployment**
- One Docker image serves both the web UI and the API, next to a MySQL 8 container
  (`docker-compose.yml`, `.env.example`), with health checks on both services. Compose stops
  with an error instead of starting if `MYSQL_ROOT_PASSWORD` or `MYSQL_PASSWORD` is missing.
- A first-start setup screen for the database connection. It tests the connection, creates the
  tables, and saves the details to `/data/config/database.json`. Setting
  `ConnectionStrings__Default` skips the screen.
- Database migrations apply on startup; turn this off with `TAPEORY_AUTO_MIGRATE=false`.
- `/api/health` endpoint that reports whether the database is reachable.
- Prebuilt multi-arch images (`amd64`/`arm64`) on GitHub Container Registry
  (`ghcr.io/tunefish92/tapeory`) and Docker Hub (`tunefish92/tapeory`), tagged `latest`, by
  release version, and by commit.
  `docker-compose.yml` uses this image by default and can still build from source.
- `PUID`/`PGID` settings: the container gives the storage folder to that user on startup and
  runs the app as them. Folders that Docker creates as root, and Unraid's `99:100` appdata, work
  without manual `chown`.
- Unraid Community Applications template (`unraid/tapeory.xml`) using the prebuilt image, with
  an icon, screenshots, and the `ca_profile.xml` that Community Applications requires.
- GitHub Actions CI: backend and frontend build and tests, a Docker image build with a smoke
  test, image publishing to GitHub Container Registry, and optional publishing to Docker Hub.
- Several Tapeory instances can share one database: each queued print job is claimed by exactly
  one of them.

**Label editor**
- Canvas editor with text, dynamic-field, rectangle, line, and image objects. The canvas uses
  the full width, with the selected object's properties in a wide panel below it; text and
  dynamic fields group theirs into Content, Font, Size and position, and Text fitting and
  options.
- Move, resize, and rotate objects; change layer order; lock, hide, duplicate, and delete.
- Undo/redo, zoom, arrow-key nudging, and keyboard shortcuts.
- Text alignment and text fitting for each text box: overflow, shrink to fit, or wrap and shrink.
- Brother media presets: TZe/HGe tape, HSe heat-shrink tube, DK continuous and die-cut rolls.
- Preview mode that fills dynamic fields with sample values.
- Thirteen Google Fonts ship with Tapeory, each in Regular and Bold: Roboto, Open Sans, Lato,
  Montserrat, Inter, IBM Plex Sans and Serif, Roboto Slab, Bebas Neue, League Spartan,
  Quicksand, Comic Neue, and Fira Code (`Tapeory.Api/Fonts`, with their licenses). They're
  available on every system; the Docker image adds Liberation and DejaVu. The font picker lists
  every font the server can render, and the editor loads the same files, so the preview matches
  the printed label.

**Templates**
- Create, edit, duplicate, and delete templates, with draft, published, and archived states.
- Immutable version history for every template.
- Template groups with case- and accent-insensitive matching and bulk rename.
- Template thumbnails and preview images, in a card view and a list view.
- Export and import in Tapeory's native JSON format.
- `.lbx` import from Brother P-touch Editor. It converts text objects and images, and
  database-merge fields become dynamic fields. Tape labels saved in landscape with automatic
  length get the tape width as height and a length that fits their content. Anything it can't
  convert is listed as a warning in the editor, and the original file is stored unchanged and
  can be downloaded.
- Image uploads (PNG, JPEG, WebP, SVG, and TIFF and BMP, which are converted to PNG) with checks
  on file type and size.

**Printing**
- Printing on Brother P-touch PT printers with a 128-pin head (PT-P750W, PT-E550W, PT-P710BT,
  PT-P700) in Brother's raster format, over the network. The tape width follows the label
  height, and each job starts with Brother's reset sequence.
- A print form with field values, required-field checks, default values, quantity, printer
  choice, and a preview that updates live while you type.
- Print quality: standard (180 × 180 dpi), or high (180 × 360 dpi) on models that support it.
- Cutting: auto cut (the default), half cut, cut at end, or chain printing.
- Live print status: jobs show "Sending to printer" and "Printing", and complete only when the
  printer's label counter (read over SNMP) confirms the labels came out. If the printer stops,
  for example with no tape or an open cover, the job fails with the printer's own message.
- Print server queues: a print-server printer can name a CUPS/IPP queue, and Tapeory submits the
  job to it over IPP as raw data and follows it until CUPS reports it done. A job the server
  can't finish is cancelled there, with CUPS's reason.
- A background print queue with a job detail page, previews for each label, and a print
  history. Jobs can be deleted from the history one at a time or all at once.
- Printer management with IP address, hostname, print server, and USB connection types, a
  default printer, and connection tests and test prints. The test print is centred on the tape
  and sized to fit.
- Server-side rendering of templates to PNG (300 DPI) and PDF with SkiaSharp, including SVG
  images and rotated objects.

**App**
- **Database backup** card in Settings: saves a full SQL dump of the database on the server, and
  restores it. Newer migrations are applied after a restore.
- **Label backup** card in Settings: saves all templates with their images, preview images and
  `.lbx` originals as a `.zip` on the server, and restores it, replacing the current templates.
- Both cards list their backups, which can be downloaded, restored or deleted. Every restore first
  saves the current state as a "Before restore" backup, so a restore can be undone.
- Dashboard with usage statistics, which can be reset and restored to all-time totals.
- Settings for language, theme (light, dark, or system), and units (mm or inches), saved on the
  server.
- 5 UI languages: English, German, French, Italian, and Spanish.

### Changed
- The project was renamed from **Labelly** to **Tapeory**. Projects, namespaces, the Docker
  image, and environment variables now use the new name (for example, `LABELLY_AUTO_MIGRATE`
  is now `TAPEORY_AUTO_MIGRATE`).

### Security
- Every stored file gets a generated name, and a guard blocks path traversal outside the storage
  folder.
- Uploaded SVG files are sanitized before they are stored or rendered.
- Embedded `.lbx` images and uploaded TIFF/BMP files are size-limited before they're decoded.
- Once a database connection is saved, the setup endpoints stop accepting new connection
  details.

### Known limitations
- Only Brother PT printers with a 128-pin head can print. QL printers and 360 dpi PT models
  (such as the PT-P900 series) aren't supported yet, and neither are USB printers.
- The printer's network port can't report which tape is loaded, so the tape width comes from
  the label height.
- Live print status needs SNMP (community `public`); without it, a job counts as done once it's
  sent. Through a CUPS queue, the status is what CUPS reports.
- The editor doesn't support barcodes or QR codes yet.
- `.lbx` import doesn't convert barcodes or shapes.
- There is no authentication. Run Tapeory on a trusted network or behind an authenticating
  reverse proxy.

[Unreleased]: https://github.com/Tunefish92/Tapeory/compare/v0.5.0...HEAD
[0.5.0]: https://github.com/Tunefish92/Tapeory/compare/v0.4.0...v0.5.0
[0.4.0]: https://github.com/Tunefish92/Tapeory/compare/v0.3.0...v0.4.0
[0.3.0]: https://github.com/Tunefish92/Tapeory/compare/v0.2.1...v0.3.0
[0.2.1]: https://github.com/Tunefish92/Tapeory/compare/v0.2.0...v0.2.1
[0.2.0]: https://github.com/Tunefish92/Tapeory/releases/tag/v0.2.0
