//! The screens, and what they share.

pub mod about;
pub mod auth;
pub mod bulk_print;
pub mod dashboard;
pub mod grid;
pub mod jobs;
pub mod print;
pub mod printers;
pub mod settings;
pub mod setup;
pub mod templates;
pub mod widgets;

use std::time::{Duration, Instant};

use eframe::egui;

use crate::api::{Api, ApiError, ApiResult};
use crate::fonts::Fonts;
use crate::images::Images;
use crate::models::{AppSettings, AuthState};
use crate::task::Task;
use crate::units::Unit;

/// Where the app is.
#[derive(Clone, Debug, PartialEq)]
pub enum Route {
    Dashboard,
    Templates,
    /// A template in the editor; None for a new one.
    Editor(Option<i64>),
    Print(i64),
    /// Printing a template once per row of a data file.
    BulkPrint(i64),
    Jobs,
    Job(i64),
    Printers,
    Settings,
    About,
}

/// What every screen gets each frame.
pub struct Ctx<'a> {
    pub egui: &'a egui::Context,
    pub api: &'a Api,
    pub fonts: &'a mut Fonts,
    pub images: &'a mut Images,
    pub unit: Unit,
    pub auth: &'a AuthState,
    pub toasts: &'a mut Toasts,
    pub navigate: &'a mut Option<Route>,
    pub session_ended: &'a mut bool,
    /// Settings saved on the Settings page, for the app to apply.
    pub settings_saved: &'a mut Option<AppSettings>,
    /// Files being saved (dialog, download, write), reported by the app when done.
    pub files: &'a mut Vec<Task<ApiResult<String>>>,
}

/// A file dialog, off the UI thread (it would freeze the window while open). `filter` is a name
/// and extensions; None when the user cancels.
pub fn pick_file(filter: (String, &[&str])) -> Option<std::path::PathBuf> {
    rfd::FileDialog::new().add_filter(filter.0, filter.1).pick_file()
}

impl Ctx<'_> {
    pub fn go(&mut self, route: Route) {
        *self.navigate = Some(route);
    }

    /// Admins, or anyone while there are no accounts (the engine applies the same rule).
    pub fn can_administer(&self) -> bool {
        !self.auth.has_users || self.auth.user.as_ref().is_some_and(|user| user.is_admin())
    }

    /// Asks where to save, then fetches the file from the engine and writes it, all off the UI
    /// thread; a notification says where it went.
    pub fn save_file(&mut self, suggested: String, fetch: impl FnOnce(&Api) -> ApiResult<Vec<u8>> + Send + 'static) {
        let api = self.api.clone();
        self.files.push(Task::spawn(self.egui, move || {
            // Starts in the Downloads folder (or the home folder) rather than wherever the app was
            // started from.
            let mut dialog = rfd::FileDialog::new().set_file_name(&suggested);
            if let Some(folder) = dirs::download_dir().filter(|folder| folder.is_dir()).or_else(dirs::home_dir) {
                dialog = dialog.set_directory(folder);
            }
            let path = dialog.save_file().ok_or_else(ApiError::cancelled)?;
            let bytes = fetch(&api)?;
            std::fs::write(&path, bytes).map_err(ApiError::io)?;
            Ok(crate::i18n::tf("desktop.saved", &[("path", &path.display().to_string())]))
        }));
    }

    /// Reports a failed call: a 401 means the session ended (the app shows the sign-in again);
    /// a closed file dialog is nothing; anything else becomes an error notification.
    pub fn failed(&mut self, error: &ApiError) {
        if error.is_unauthorized() {
            *self.session_ended = true;
        } else if !error.is_cancelled() {
            self.toasts.error(error.message.clone());
        }
    }

    /// Like `failed`, but returns the message for showing next to the form instead.
    pub fn message(&mut self, error: &ApiError) -> String {
        if error.is_unauthorized() {
            *self.session_ended = true;
        }
        error.message.clone()
    }
}

#[derive(Clone, Copy, PartialEq)]
pub enum ToastKind {
    Success,
    Error,
}

/// Short notifications in the corner, like the web UI's.
#[derive(Default)]
pub struct Toasts {
    items: Vec<(String, ToastKind, Instant)>,
}

impl Toasts {
    pub fn success(&mut self, message: impl Into<String>) {
        self.items.push((message.into(), ToastKind::Success, Instant::now()));
    }

    pub fn error(&mut self, message: impl Into<String>) {
        self.items.push((message.into(), ToastKind::Error, Instant::now()));
    }

    pub fn show(&mut self, ctx: &egui::Context) {
        let lifetime = Duration::from_secs(5);
        self.items.retain(|(_, kind, shown)| *kind == ToastKind::Error && shown.elapsed() < lifetime * 2 || shown.elapsed() < lifetime);

        if self.items.is_empty() {
            return;
        }

        let palette = crate::theme::palette(ctx);
        let mut dismissed = None;

        egui::Area::new(egui::Id::new("toasts"))
            .anchor(egui::Align2::RIGHT_BOTTOM, egui::vec2(-16.0, -16.0))
            .order(egui::Order::Foreground)
            .show(ctx, |ui| {
                for (index, (message, kind, _)) in self.items.iter().enumerate() {
                    let (bg, fg) = match kind {
                        ToastKind::Success => (palette.success_bg, palette.success_text),
                        ToastKind::Error => (palette.danger_bg, palette.danger_text),
                    };

                    egui::Frame::new()
                        .fill(bg)
                        .corner_radius(egui::CornerRadius::same(10))
                        .inner_margin(egui::Margin::symmetric(14, 10))
                        .show(ui, |ui| {
                            ui.set_max_width(380.0);
                            ui.horizontal(|ui| {
                                ui.label(egui::RichText::new(message).color(fg));
                                if ui.small_button("×").clicked() {
                                    dismissed = Some(index);
                                }
                            });
                        });
                    ui.add_space(6.0);
                }
            });

        if let Some(index) = dismissed {
            self.items.remove(index);
        }

        ctx.request_repaint_after(Duration::from_millis(500));
    }
}
