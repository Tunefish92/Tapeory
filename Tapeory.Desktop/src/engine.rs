//! Tapeory's engine: the .NET server started invisibly with `--engine`. It listens on
//! 127.0.0.1 with a free port, only for requests that carry the secret we give it, and stops by
//! itself when its standard input closes, which happens when this app exits (or crashes).

use std::fs::OpenOptions;
use std::io::{BufRead, BufReader, Write};
use std::path::{Path, PathBuf};
use std::process::{Child, ChildStdin, Command, Stdio};
use std::sync::mpsc::channel;
use std::time::{Duration, Instant};

const READY_PREFIX: &str = "TAPEORY_ENGINE_READY";
const START_TIMEOUT: Duration = Duration::from_secs(90);

pub struct Engine {
    child: Child,
    /// Held open for as long as the app runs; dropping it tells the engine to stop.
    stdin: Option<ChildStdin>,
    pub address: String,
    pub token: String,
}

impl Engine {
    /// Starts the engine and waits until it's ready (a few seconds).
    pub fn start(data_folder: &Path) -> Result<Engine, String> {
        let executable = find_engine().ok_or_else(|| {
            "Tapeory's engine wasn't found next to the app. Reinstall Tapeory, or set TAPEORY_ENGINE to its path.".to_string()
        })?;

        std::fs::create_dir_all(data_folder).map_err(|e| format!("Couldn't create {}: {e}", data_folder.display()))?;
        let log_path = data_folder.join("logs").join("engine.log");
        let _ = std::fs::create_dir_all(log_path.parent().unwrap_or(data_folder));

        let token = format!("{}{}", uuid::Uuid::new_v4().simple(), uuid::Uuid::new_v4().simple());

        let mut command = if executable.extension().is_some_and(|ext| ext == "dll") {
            let mut command = Command::new("dotnet");
            command.arg(&executable);
            command
        } else {
            Command::new(&executable)
        };

        command
            .arg("--engine")
            .env("TAPEORY_ENGINE_TOKEN", &token)
            .env("TAPEORY_STORAGE_PATH", data_folder)
            .env_remove("ConnectionStrings__Default")
            .stdin(Stdio::piped())
            .stdout(Stdio::piped())
            .stderr(Stdio::piped());

        if let Some(folder) = executable.parent() {
            command.current_dir(folder);
        }

        #[cfg(windows)]
        {
            use std::os::windows::process::CommandExt;
            const CREATE_NO_WINDOW: u32 = 0x0800_0000;
            command.creation_flags(CREATE_NO_WINDOW);
        }

        let mut child = command.spawn().map_err(|e| format!("Couldn't start Tapeory's engine ({}): {e}", executable.display()))?;

        let stdin = child.stdin.take();
        let stdout = child.stdout.take().expect("piped stdout");
        let stderr = child.stderr.take().expect("piped stderr");
        let (ready_sender, ready_receiver) = channel::<String>();

        // Everything the engine writes goes to its log; the ready line also comes back here.
        let log = open_log(&log_path);
        let stdout_log = log.as_ref().and_then(|file| file.try_clone().ok());
        std::thread::spawn(move || {
            let mut log = stdout_log;
            for line in BufReader::new(stdout).lines().map_while(Result::ok) {
                if let Some(address) = line.strip_prefix(READY_PREFIX) {
                    let _ = ready_sender.send(address.trim().to_string());
                }
                if let Some(file) = log.as_mut() {
                    let _ = writeln!(file, "{line}");
                }
            }
        });
        std::thread::spawn(move || {
            let mut log = log;
            for line in BufReader::new(stderr).lines().map_while(Result::ok) {
                if let Some(file) = log.as_mut() {
                    let _ = writeln!(file, "{line}");
                }
            }
        });

        let started = Instant::now();
        loop {
            if let Ok(address) = ready_receiver.recv_timeout(Duration::from_millis(200)) {
                return Ok(Engine { child, stdin, address, token });
            }

            if let Ok(Some(status)) = child.try_wait() {
                return Err(format!("Tapeory's engine stopped while starting ({status}). Details are in {}.", log_path.display()));
            }

            if started.elapsed() > START_TIMEOUT {
                let _ = child.kill();
                return Err(format!(
                    "Tapeory's engine didn't start within {} seconds. Details are in {}.",
                    START_TIMEOUT.as_secs(),
                    log_path.display()
                ));
            }
        }
    }
}

impl Drop for Engine {
    fn drop(&mut self) {
        // Closing its input is the engine's signal to stop cleanly.
        self.stdin.take();

        let deadline = Instant::now() + Duration::from_secs(8);
        while Instant::now() < deadline {
            if let Ok(Some(_)) = self.child.try_wait() {
                return;
            }
            std::thread::sleep(Duration::from_millis(100));
        }

        let _ = self.child.kill();
        let _ = self.child.wait();
    }
}

/// The engine's log, started afresh (the previous one kept as engine.log.1) once it's over 5 MB.
fn open_log(path: &Path) -> Option<std::fs::File> {
    if std::fs::metadata(path).is_ok_and(|meta| meta.len() > 5 * 1024 * 1024) {
        let _ = std::fs::rename(path, path.with_extension("log.1"));
    }
    OpenOptions::new().create(true).append(true).open(path).ok()
}

/// Where the data lives: %LOCALAPPDATA%\Tapeory on Windows, ~/.local/share/tapeory on Linux.
pub fn data_folder() -> PathBuf {
    if let Some(folder) = std::env::var_os("TAPEORY_DATA_FOLDER").filter(|value| !value.is_empty()) {
        return PathBuf::from(folder);
    }

    let base = dirs::data_local_dir().unwrap_or_else(|| PathBuf::from("."));
    base.join(if cfg!(windows) { "Tapeory" } else { "tapeory" })
}

/// The engine: TAPEORY_ENGINE if set, the `engine` folder next to this program in an installed
/// copy, or, while developing, Tapeory.Api's build output in the repository.
fn find_engine() -> Option<PathBuf> {
    if let Some(path) = std::env::var_os("TAPEORY_ENGINE").map(PathBuf::from) {
        return path.exists().then_some(path);
    }

    let name = if cfg!(windows) { "Tapeory.Api.exe" } else { "Tapeory.Api" };
    let exe_folder = std::env::current_exe().ok()?.parent()?.to_path_buf();

    let installed = exe_folder.join("engine").join(name);
    if installed.exists() {
        return Some(installed);
    }

    exe_folder.ancestors().find_map(|folder| {
        ["Release", "Debug"].iter().find_map(|configuration| {
            let candidate = folder.join("Tapeory.Api").join("bin").join(configuration).join("net10.0").join(name);
            candidate.exists().then_some(candidate)
        })
    })
}
