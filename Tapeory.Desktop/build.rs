//! On Windows: the icon and version details of tapeory.exe (shown by Explorer and the taskbar, and
//! checked by SignPath before it signs a release).

fn main() {
    println!("cargo:rerun-if-changed=packaging/windows/tapeory.ico");
    println!("cargo:rerun-if-env-changed=TAPEORY_VERSION");

    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        // The release version (set by the packaging scripts), like VERSION in main.rs.
        let version = std::env::var("TAPEORY_VERSION").unwrap_or_else(|_| env!("CARGO_PKG_VERSION").to_string());

        let mut resource = winresource::WindowsResource::new();
        resource
            .set_icon("packaging/windows/tapeory.ico")
            .set("ProductName", "Tapeory")
            .set("FileDescription", "Tapeory")
            .set("CompanyName", "Tunefish")
            .set("LegalCopyright", "Copyright (c) 2026 Tunefish, MIT License")
            .set("ProductVersion", &version)
            .set("FileVersion", &version)
            .set_version_info(winresource::VersionInfo::PRODUCTVERSION, numeric(&version))
            .set_version_info(winresource::VersionInfo::FILEVERSION, numeric(&version));
        resource.compile().expect("Windows resources");
    }
}

/// "1.2.3" (or "1.2.3-beta") → 0x0001_0002_0003_0000, Windows' binary form of a version.
fn numeric(version: &str) -> u64 {
    let core = version.split(['-', '+']).next().unwrap_or_default();
    core.split('.')
        .take(4)
        .map(|part| part.parse::<u64>().unwrap_or(0) & 0xFFFF)
        .chain(std::iter::repeat(0))
        .take(4)
        .fold(0, |all, part| (all << 16) | part)
}
