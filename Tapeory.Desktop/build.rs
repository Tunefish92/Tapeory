//! On Windows: the icon and version details of tapeory.exe (shown by Explorer and the taskbar).

fn main() {
    println!("cargo:rerun-if-changed=packaging/windows/tapeory.ico");
    println!("cargo:rerun-if-env-changed=TAPEORY_VERSION");

    if std::env::var("CARGO_CFG_TARGET_OS").as_deref() == Ok("windows") {
        let mut resource = winresource::WindowsResource::new();
        resource
            .set_icon("packaging/windows/tapeory.ico")
            .set("ProductName", "Tapeory")
            .set("FileDescription", "Tapeory")
            .set("CompanyName", "Tunefish")
            .set("LegalCopyright", "MIT License");
        resource.compile().expect("Windows resources");
    }
}
