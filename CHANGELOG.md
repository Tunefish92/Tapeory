# Changelog

This file records all notable changes to Tapeory.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project
aims to follow [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Until 1.0, minor
versions may include breaking changes.

## [Unreleased]

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
- Fonts in the image (Liberation and DejaVu), so the editor and renderer have fonts to use.
- Unraid Community Applications template (`unraid/tapeory.xml`) using the prebuilt image, with
  an icon, screenshots, and the `ca_profile.xml` that Community Applications requires.
- GitHub Actions CI: backend and frontend build and tests, a Docker image build with a smoke
  test, image publishing to GitHub Container Registry, and optional publishing to Docker Hub.

**Label editor**
- Canvas editor with text, dynamic-field, rectangle, line, and image objects.
- Move, resize, and rotate objects; change layer order; lock, hide, duplicate, and delete.
- Undo/redo, zoom, arrow-key nudging, and keyboard shortcuts.
- Text alignment and text fitting for each text box: overflow, shrink to fit, or wrap and shrink.
- Brother media presets: TZe/HGe tape, HSe heat-shrink tube, DK continuous and die-cut rolls.
- Preview mode that fills dynamic fields with sample values.
- Font picker listing the fonts installed on the server. The editor loads the same font files,
  so the preview matches the rendered label.

**Templates**
- Create, edit, and delete templates, with draft, published, and archived states.
- Immutable version history for every template.
- Template groups with case- and accent-insensitive matching and bulk rename.
- Template thumbnails and preview images.
- Export and import in Tapeory's native JSON format.
- `.lbx` import from Brother P-touch Editor. It converts text objects, and database-merge fields
  become dynamic fields. Anything it can't convert is listed as a warning in the editor, and the
  original file is stored unchanged and can be downloaded.
- Image uploads (PNG, JPEG, WebP, SVG) with checks on file type and size.

**Rendering and printing**
- Server-side rendering of templates to PNG (300 DPI) and PDF with SkiaSharp, including SVG
  images and rotated objects.
- A print form with field values, required-field checks, default values, quantity, printer
  choice, and a live rendered preview.
- A background print queue with job status, a job detail page, previews for each label, and a
  print history. Jobs can be deleted from the history one at a time or all at once.
- Printer management with IP address, hostname, print server, and USB connection types. Each
  printer can have a label media size, be enabled or disabled, or be set as the default.
  Connection tests and test prints show the last connection status.
- **Experimental:** sending rendered labels to network printers over a raw TCP socket.

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
- 12 UI languages: English, German, French, Italian, Spanish, Portuguese, Russian, Chinese,
  Hindi, Bengali, Arabic, and Indonesian.

### Changed
- The project was renamed from **Labelly** to **Tapeory**. Projects, namespaces, the Docker
  image, and environment variables now use the new name (for example, `LABELLY_AUTO_MIGRATE`
  is now `TAPEORY_AUTO_MIGRATE`).

### Security
- Every stored file gets a generated name, and a guard blocks path traversal outside the storage
  folder.
- Uploaded SVG files are sanitized before they are stored or rendered.
- Once a database connection is saved, the setup endpoints stop accepting new connection
  details.

### Known limitations
- Printers get a PNG over a raw socket, not Brother's raster command protocol, so real Brother
  hardware may not print it. This hasn't been tested on physical printers.
- USB printers can be configured, but Tapeory can't send jobs to them yet.
- The editor doesn't support barcodes or QR codes yet.
- `.lbx` import doesn't convert barcodes, embedded images (TIFF), or shapes.
- There is no authentication. Run Tapeory on a trusted network or behind an authenticating
  reverse proxy.

[Unreleased]: https://github.com/Tunefish92/Tapeory/compare/v0.2.0...HEAD
[0.2.0]: https://github.com/Tunefish92/Tapeory/releases/tag/v0.2.0
