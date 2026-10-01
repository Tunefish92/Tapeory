//! Tapeory in the desktop's application menu (Linux): an AppImage or an unpacked tar.gz has no
//! installer to put it there. The entry is a .desktop file and an icon in the user's own
//! folders, as the freedesktop.org specifications describe, so no administrator rights are needed.

use std::path::{Path, PathBuf};

const ICON: &[u8] = include_bytes!("../../unraid/tapeory.png");
const ICON_FOLDER: &str = "icons/hicolor/512x512/apps";

/// The program the menu entry starts: the AppImage itself (not the copy it unpacks to run), or
/// this executable.
pub fn program() -> Option<PathBuf> {
    std::env::var_os("APPIMAGE").map(PathBuf::from).filter(|path| path.is_file()).or_else(|| std::env::current_exe().ok())
}

/// The user's data folder for menu entries and icons (`~/.local/share`).
pub fn user_folder() -> Option<PathBuf> {
    dirs::data_dir()
}

fn entry_path(folder: &Path) -> PathBuf {
    folder.join("applications").join("tapeory.desktop")
}

fn icon_path(folder: &Path) -> PathBuf {
    folder.join(ICON_FOLDER).join("tapeory.png")
}

/// A path as the `Exec` key wants it: in double quotes, with the characters that are special
/// inside them escaped.
fn quoted(path: &Path) -> String {
    let mut text = String::from('"');
    for character in path.display().to_string().chars() {
        if matches!(character, '"' | '`' | '$' | '\\') {
            text.push('\\');
        }
        text.push(character);
    }
    text.push('"');
    // In a desktop file a literal percent sign is written twice.
    text.replace('%', "%%")
}

fn entry(program: &Path) -> String {
    format!(
        "[Desktop Entry]\nType=Application\nName=Tapeory\nComment=Design and print Brother P-touch labels\nExec={}\nIcon=tapeory\n\
         Categories=Office;\nTerminal=false\nStartupWMClass=tapeory\n",
        quoted(program)
    )
}

/// Whether the menu has an entry that starts this program (one left behind for a file that has
/// since moved doesn't count).
pub fn present(folder: &Path, program: &Path) -> bool {
    std::fs::read_to_string(entry_path(folder)).is_ok_and(|text| text == entry(program))
}

pub fn add(folder: &Path, program: &Path) -> std::io::Result<()> {
    for path in [entry_path(folder), icon_path(folder)] {
        if let Some(parent) = path.parent() {
            std::fs::create_dir_all(parent)?;
        }
    }
    std::fs::write(icon_path(folder), ICON)?;
    std::fs::write(entry_path(folder), entry(program))
}

pub fn remove(folder: &Path) -> std::io::Result<()> {
    for path in [entry_path(folder), icon_path(folder)] {
        match std::fs::remove_file(path) {
            Err(error) if error.kind() != std::io::ErrorKind::NotFound => return Err(error),
            _ => {}
        }
    }
    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn adds_and_removes_the_menu_entry_and_its_icon() {
        let folder = std::env::temp_dir().join(format!("tapeory-menu-{}", std::process::id()));
        let program = Path::new("/home/someone/My Apps/Tapeory-0.5.0.AppImage");
        assert!(!present(&folder, program));

        add(&folder, program).unwrap();

        let text = std::fs::read_to_string(folder.join("applications/tapeory.desktop")).unwrap();
        assert!(text.contains("Exec=\"/home/someone/My Apps/Tapeory-0.5.0.AppImage\"\n"), "{text}");
        assert!(text.contains("Icon=tapeory\n") && text.contains("StartupWMClass=tapeory\n"));
        assert_eq!(std::fs::read(folder.join("icons/hicolor/512x512/apps/tapeory.png")).unwrap(), ICON);
        assert!(present(&folder, program));
        assert!(!present(&folder, Path::new("/somewhere/else/Tapeory.AppImage")), "an entry for a moved file doesn't count");

        remove(&folder).unwrap();
        assert!(!present(&folder, program));
        remove(&folder).unwrap(); // nothing left to remove is fine
        std::fs::remove_dir_all(&folder).unwrap();
    }

    #[test]
    fn quotes_what_is_special_in_a_command_line() {
        assert_eq!(quoted(Path::new("/a/b c")), "\"/a/b c\"");
        assert_eq!(quoted(Path::new("/a/$HOME`x`\"q\"\\")), "\"/a/\\$HOME\\`x\\`\\\"q\\\"\\\\\"");
        assert_eq!(quoted(Path::new("/a/100%")), "\"/a/100%%\"");
    }
}
