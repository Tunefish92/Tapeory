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
mod models;
mod task;
mod theme;
mod ui;
mod units;
mod updater;

/// The release version (set by the packaging scripts), or the crate's while developing.
pub const VERSION: &str = match option_env!("TAPEORY_VERSION") {
    Some(version) => version,
    None => env!("CARGO_PKG_VERSION"),
};

fn main() -> eframe::Result<()> {
    let options = eframe::NativeOptions {
        viewport: eframe::egui::ViewportBuilder::default()
            .with_title("Tapeory")
            .with_app_id("tapeory")
            .with_inner_size([1280.0, 900.0])
            .with_min_inner_size([900.0, 600.0])
            .with_icon(app_icon()),
        ..Default::default()
    };

    let result = eframe::run_native("Tapeory", options, Box::new(|cc| Ok(Box::new(app::App::new(cc)))));

    // The window is closed and the engine stopped: now an update can replace the files.
    updater::finish();
    result
}

/// The window and taskbar icon, from the same PNG as the Unraid template.
fn app_icon() -> eframe::egui::IconData {
    let decoded = image::load_from_memory(include_bytes!("../../unraid/tapeory.png")).map(|image| image.into_rgba8());

    match decoded {
        Ok(rgba) => eframe::egui::IconData { width: rgba.width(), height: rgba.height(), rgba: rgba.into_raw() },
        Err(_) => eframe::egui::IconData::default(),
    }
}
