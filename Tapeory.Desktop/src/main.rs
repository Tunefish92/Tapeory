//! Tapeory as a desktop app for Windows and Linux: this egui interface on top of Tapeory's
//! engine (the same .NET server as the Docker image), which it starts invisibly on 127.0.0.1.

#![cfg_attr(all(windows, not(debug_assertions)), windows_subsystem = "windows")]

mod api;
mod app;
mod editor;
mod engine;
mod fonts;
mod i18n;
mod icons;
mod images;
#[cfg(target_os = "linux")]
mod menu_entry;
mod models;
mod task;
mod theme;
mod ui;
mod units;
mod updater;

use std::io::Write;
use std::sync::atomic::{AtomicBool, Ordering};

use eframe::Renderer;

/// The release version (set by the packaging scripts), or the crate's while developing.
pub const VERSION: &str = match option_env!("TAPEORY_VERSION") {
    Some(version) => version,
    None => env!("CARGO_PKG_VERSION"),
};

/// Set once the app is up (its window drawing), so a later error doesn't restart it with another
/// renderer.
pub static STARTED: AtomicBool = AtomicBool::new(false);

fn main() -> eframe::Result<()> {
    // HTTPS (the update check and downloads) uses ring for its crypto, see Cargo.toml.
    let _ = rustls::crypto::ring::default_provider().install_default();

    // Without a console on Windows, a crash would otherwise vanish without a word.
    std::panic::set_hook(Box::new(|info| {
        let message = format!("Tapeory stopped unexpectedly: {info}");
        log(&message);
        tell_user(&message);
    }));

    let mut last_error = None;
    for renderer in renderers() {
        let result = eframe::run_native("Tapeory", options(renderer), Box::new(|cc| Ok(Box::new(app::App::new(cc)))));

        match result {
            Ok(()) => {
                // The window is closed and the engine stopped: now an update can replace the files.
                updater::finish();
                return Ok(());
            }
            Err(error) => {
                log(&format!("The {renderer:?} renderer failed: {error}"));
                last_error = Some(error);
                if STARTED.load(Ordering::SeqCst) {
                    break;
                }
            }
        }
    }

    let error = last_error.expect("at least one renderer was tried");
    tell_user(&format!(
        "Tapeory couldn't open its window: {error}\n\nThe graphics driver may be missing (for example in a virtual machine \
         without 3D acceleration). Details are in {}.",
        log_path().display()
    ));
    Err(error)
}

/// Which renderers to try, in order. Direct3D (through wgpu) comes first on Windows: it works
/// even without a graphics driver (Windows' WARP software renderer), where OpenGL doesn't.
/// TAPEORY_RENDERER picks them instead, e.g. `glow`, `wgpu` or `wgpu,glow`.
fn renderers() -> Vec<Renderer> {
    let chosen: Vec<Renderer> = std::env::var("TAPEORY_RENDERER")
        .unwrap_or_default()
        .split(',')
        .filter_map(|name| match name.trim() {
            "glow" => Some(Renderer::Glow),
            "wgpu" => Some(Renderer::Wgpu),
            _ => None,
        })
        .collect();

    if !chosen.is_empty() {
        chosen
    } else if cfg!(windows) {
        vec![Renderer::Wgpu, Renderer::Glow]
    } else {
        vec![Renderer::Glow, Renderer::Wgpu]
    }
}

fn options(renderer: Renderer) -> eframe::NativeOptions {
    eframe::NativeOptions {
        renderer,
        viewport: eframe::egui::ViewportBuilder::default()
            .with_title("Tapeory")
            .with_app_id("tapeory")
            .with_inner_size([1280.0, 900.0])
            .with_min_inner_size([900.0, 600.0])
            .with_icon(app_icon()),
        ..Default::default()
    }
}

/// The app's own log (next to the engine's): startup problems and crashes.
fn log_path() -> std::path::PathBuf {
    engine::data_folder().join("logs").join("app.log")
}

fn log(message: &str) {
    let path = log_path();
    if let Some(folder) = path.parent() {
        let _ = std::fs::create_dir_all(folder);
    }
    if let Ok(mut file) = std::fs::OpenOptions::new().create(true).append(true).open(&path) {
        let _ = writeln!(file, "{} {message}", chrono::Local::now().format("%Y-%m-%d %H:%M:%S"));
    }
    eprintln!("{message}");
}

/// A message box, so a problem at startup isn't silent (Windows has no console to show it in).
fn tell_user(message: &str) {
    let _ = rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Error)
        .set_title("Tapeory")
        .set_description(message)
        .set_buttons(rfd::MessageButtons::Ok)
        .show();
}

/// The window and taskbar icon, from the same PNG as the Unraid template.
fn app_icon() -> eframe::egui::IconData {
    let decoded = image::load_from_memory(include_bytes!("../../unraid/tapeory.png")).map(|image| image.into_rgba8());

    match decoded {
        Ok(rgba) => eframe::egui::IconData { width: rgba.width(), height: rgba.height(), rgba: rgba.into_raw() },
        Err(_) => eframe::egui::IconData::default(),
    }
}
