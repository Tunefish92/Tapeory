//! Updating from a GitHub release: the installed Windows app runs the new installer, an AppImage
//! replaces itself. Both happen after the window has closed and the engine has stopped, so no file
//! is in use; then the new version starts. Other copies (the zip and tar.gz) open the release page.

use std::io::{Read, Write};
use std::path::{Path, PathBuf};
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, OnceLock};
use std::time::Duration;

use sha2::{Digest, Sha256};

use crate::models::ReleaseAsset;

/// How this copy of Tapeory was installed, which decides how it updates.
#[derive(Clone, Debug, PartialEq)]
pub enum Install {
    /// Installed with the Windows setup (it put an uninstaller next to the app).
    WindowsSetup,
    /// Running as an AppImage, at this path.
    AppImage(PathBuf),
    /// Unpacked from a zip or tar.gz, or built from source: updated by hand.
    Manual,
}

pub fn install() -> Install {
    if let Some(path) = std::env::var_os("APPIMAGE").map(PathBuf::from).filter(|path| path.is_file()) {
        return Install::AppImage(path);
    }

    let exe_folder = std::env::current_exe().ok().and_then(|exe| exe.parent().map(Path::to_path_buf));
    if cfg!(windows) && exe_folder.is_some_and(|folder| folder.join("unins000.exe").is_file()) {
        return Install::WindowsSetup;
    }

    Install::Manual
}

/// The release file this install updates from.
pub fn asset_for<'a>(install: &Install, assets: &'a [ReleaseAsset]) -> Option<&'a ReleaseAsset> {
    let suffix = match install {
        Install::WindowsSetup => "-windows-x64-setup.exe",
        Install::AppImage(_) => "-linux-x86_64.AppImage",
        Install::Manual => return None,
    };
    assets.iter().find(|asset| asset.name.ends_with(suffix))
}

/// Bytes downloaded so far, for the progress bar.
#[derive(Default)]
pub struct Progress {
    pub done: AtomicU64,
    pub total: AtomicU64,
}

impl Progress {
    pub fn fraction(&self) -> f32 {
        let total = self.total.load(Ordering::Relaxed);
        if total == 0 { 0.0 } else { self.done.load(Ordering::Relaxed) as f32 / total as f32 }
    }
}

/// Downloads the file next to where it's needed and checks its size and SHA-256.
pub fn download(asset: &ReleaseAsset, install: &Install, progress: Arc<Progress>, repaint: impl Fn()) -> Result<PathBuf, String> {
    let folder = match install {
        // Next to the AppImage, so the final rename stays on one file system.
        Install::AppImage(path) => path.parent().map(Path::to_path_buf).unwrap_or_else(std::env::temp_dir),
        _ => std::env::temp_dir().join("tapeory-update"),
    };
    std::fs::create_dir_all(&folder).map_err(|e| format!("Couldn't create {}: {e}", folder.display()))?;
    let target = folder.join(format!(".{}.part", asset.name));

    let client = reqwest::blocking::Client::builder()
        .user_agent(format!("Tapeory/{}", crate::VERSION))
        .connect_timeout(Duration::from_secs(20))
        .timeout(Duration::from_secs(30 * 60))
        .build()
        .map_err(|e| e.to_string())?;
    let mut response = client
        .get(&asset.download_url)
        .send()
        .and_then(|response| response.error_for_status())
        .map_err(|e| format!("The download failed: {e}"))?;

    progress.total.store(response.content_length().unwrap_or(asset.size), Ordering::Relaxed);
    let mut file = std::fs::File::create(&target).map_err(|e| format!("Couldn't write {}: {e}", target.display()))?;
    let mut hasher = Sha256::new();
    let mut buffer = vec![0u8; 256 * 1024];
    let mut done = 0u64;

    loop {
        let read = response.read(&mut buffer).map_err(|e| format!("The download failed: {e}"))?;
        if read == 0 {
            break;
        }
        hasher.update(&buffer[..read]);
        file.write_all(&buffer[..read]).map_err(|e| format!("Couldn't write {}: {e}", target.display()))?;
        done += read as u64;
        progress.done.store(done, Ordering::Relaxed);
        repaint();
    }
    file.flush().map_err(|e| e.to_string())?;
    drop(file);

    let fail = |message: String| {
        let _ = std::fs::remove_file(&target);
        Err(message)
    };
    if asset.size > 0 && done != asset.size {
        return fail(format!("The download is incomplete ({done} of {} bytes).", asset.size));
    }
    let digest = hex(&hasher.finalize());
    if let Some(expected) = &asset.sha256
        && !expected.eq_ignore_ascii_case(&digest)
    {
        return fail("The download doesn't match the release's checksum.".to_string());
    }

    let ready = folder.join(&asset.name);
    std::fs::rename(&target, &ready).map_err(|e| e.to_string())?;
    Ok(ready)
}

/// A hash as lowercase hex, the form GitHub gives a release file's SHA-256 in.
fn hex(bytes: &[u8]) -> String {
    bytes.iter().map(|byte| format!("{byte:02x}")).collect()
}

/// What to do once the window has closed (and the engine has stopped).
enum AfterExit {
    RunSetup(PathBuf),
    ReplaceAppImage { new: PathBuf, current: PathBuf },
}

fn after_exit() -> &'static Mutex<Option<AfterExit>> {
    static AFTER_EXIT: OnceLock<Mutex<Option<AfterExit>>> = OnceLock::new();
    AFTER_EXIT.get_or_init(|| Mutex::new(None))
}

/// Installs `file` when the app exits; the caller then closes the window.
pub fn install_on_exit(install: &Install, file: PathBuf) {
    let step = match install {
        Install::WindowsSetup => AfterExit::RunSetup(file),
        Install::AppImage(current) => AfterExit::ReplaceAppImage { new: file, current: current.clone() },
        Install::Manual => return,
    };
    if let Ok(mut slot) = after_exit().lock() {
        *slot = Some(step);
    }
}

/// Called by `main` after the window has closed: runs the pending update, if any.
pub fn finish() {
    let Some(step) = after_exit().lock().ok().and_then(|mut slot| slot.take()) else { return };

    match step {
        AfterExit::RunSetup(setup) => {
            // Silent, per-user; the setup starts Tapeory again when it's done.
            let _ = std::process::Command::new(&setup).args(["/SILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS"]).spawn();
        }
        AfterExit::ReplaceAppImage { new, current } => {
            if replace_appimage(&new, &current).is_ok() {
                let _ = std::process::Command::new(&current).spawn();
            }
        }
    }
}

fn replace_appimage(new: &Path, current: &Path) -> std::io::Result<()> {
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        std::fs::set_permissions(new, std::fs::Permissions::from_mode(0o755))?;
    }
    // A rename replaces the file even while the old one is still mounted.
    std::fs::rename(new, current)
}

#[cfg(test)]
mod tests {
    use super::*;

    fn asset(name: &str) -> ReleaseAsset {
        ReleaseAsset { name: name.into(), download_url: format!("https://example.com/{name}"), size: 1, sha256: None }
    }

    #[test]
    fn picks_the_file_for_the_install() {
        let assets = [
            asset("Tapeory-1.0.0-windows-x64.zip"),
            asset("Tapeory-1.0.0-windows-x64-setup.exe"),
            asset("Tapeory-1.0.0-linux-x86_64.tar.gz"),
            asset("Tapeory-1.0.0-linux-x86_64.AppImage"),
        ];

        assert_eq!(asset_for(&Install::WindowsSetup, &assets).map(|a| a.name.as_str()), Some("Tapeory-1.0.0-windows-x64-setup.exe"));
        assert_eq!(
            asset_for(&Install::AppImage("/x".into()), &assets).map(|a| a.name.as_str()),
            Some("Tapeory-1.0.0-linux-x86_64.AppImage")
        );
        assert_eq!(asset_for(&Install::Manual, &assets), None);
    }

    #[test]
    fn writes_the_checksum_like_github() {
        assert_eq!(hex(&Sha256::digest(b"abc")), "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    #[test]
    fn replaces_an_appimage_and_makes_it_executable() {
        let folder = std::env::temp_dir().join(format!("tapeory-updater-{}", std::process::id()));
        std::fs::create_dir_all(&folder).unwrap();
        let (current, new) = (folder.join("Tapeory.AppImage"), folder.join("new.AppImage"));
        std::fs::write(&current, b"old").unwrap();
        std::fs::write(&new, b"new").unwrap();

        replace_appimage(&new, &current).unwrap();

        assert_eq!(std::fs::read(&current).unwrap(), b"new");
        assert!(!new.exists());
        #[cfg(unix)]
        {
            use std::os::unix::fs::PermissionsExt;
            assert_eq!(std::fs::metadata(&current).unwrap().permissions().mode() & 0o777, 0o755);
        }
        std::fs::remove_dir_all(&folder).unwrap();
    }
}
