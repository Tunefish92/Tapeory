//! About: the version with the update check, where this installation keeps its files, and (on
//! Linux) the application menu entry.

use eframe::egui::{self};

use crate::api::ApiResult;
use crate::i18n::{t, tf};
use crate::icons::Icon;
use crate::models::{Health, UpdateCheck};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::Ctx;
use crate::ui::settings::row;
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Kind, PillButton, Tone, button};

#[derive(Default)]
pub struct AboutPage {
    started: bool,
    health: Option<Health>,
    health_task: Pending<ApiResult<Health>>,
    update: Option<Result<UpdateCheck, String>>,
    update_task: Pending<ApiResult<UpdateCheck>>,
    update_download: Pending<Result<std::path::PathBuf, String>>,
    update_progress: std::sync::Arc<crate::updater::Progress>,
    update_error: Option<String>,
}

impl AboutPage {
    fn check_updates(&mut self, c: &Ctx, refresh: bool) {
        let api = c.api.clone();
        self.update = None;
        self.update_task = Some(Task::spawn(c.egui, move || api.check_updates(refresh)));
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            let api = c.api.clone();
            self.health_task = Some(Task::spawn(c.egui, move || api.health()));
            self.check_updates(c, false);
        }

        if let Some(Ok(health)) = finished(&mut self.health_task) {
            self.health = Some(health);
        }
        if let Some(result) = finished(&mut self.update_task) {
            self.update = Some(result.map_err(|e| e.message));
        }

        egui::ScrollArea::vertical().show(ui, |ui| {
            widgets::page_header(ui, &t("nav.about"), |_| {});
            let width = ui.available_width().min(760.0);
            ui.allocate_ui_with_layout(egui::vec2(width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
                self.about_card(ui, c);
                ui.add_space(18.0);
                links_card(ui);
            });
        });
    }

    fn about_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        let mut check = false;
        theme::card(ui).show(ui, |ui| {
            widgets::section(ui, Icon::Info, &t("app.name"));

            ui.horizontal(|ui| {
                widgets::muted(ui, &t("settings.version"));
                ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                    match &self.update {
                        None => {
                            widgets::muted_small(ui, &t("settings.updateChecking"));
                        }
                        Some(Err(_)) => {
                            if button(ui, &t("settings.updateCheck")).clicked() {
                                check = true;
                            }
                            widgets::muted_small(ui, &t("settings.updateFailed"));
                        }
                        Some(Ok(update)) if update.error_message.is_some() || update.latest_version.is_none() => {
                            if button(ui, &t("settings.updateCheck")).clicked() {
                                check = true;
                            }
                            widgets::muted_small(ui, &t("settings.updateFailed"));
                        }
                        Some(Ok(update)) if update.update_available => {
                            let install = crate::updater::install();
                            let asset = crate::updater::asset_for(&install, update.assets.as_deref().unwrap_or_default()).cloned();
                            if let Some(asset) = asset
                                && self.update_download.is_none()
                                && ui.add(PillButton::new(&t("desktop.updateNow"), Kind::Primary).small()).clicked()
                            {
                                let progress = self.update_progress.clone();
                                let ctx = ui.ctx().clone();
                                self.update_error = None;
                                self.update_download = Some(Task::spawn(ui.ctx(), move || {
                                    crate::updater::download(&asset, &install, progress, || ctx.request_repaint()).inspect(|file| {
                                        crate::updater::install_on_exit(&install, file.clone());
                                    })
                                }));
                            }
                            if let Some(url) = &update.release_url
                                && button(ui, &t("settings.updateReleaseNotes")).clicked()
                            {
                                ui.ctx().open_url(egui::OpenUrl::new_tab(url));
                            }
                            let version = update.latest_version.clone().unwrap_or_default();
                            widgets::pill(ui, &tf("settings.updateAvailable", &[("version", &version)]), Tone::Info);
                        }
                        Some(Ok(_)) => {
                            if button(ui, &t("settings.updateCheck")).clicked() {
                                check = true;
                            }
                            widgets::pill(ui, &t("settings.updateUpToDate"), Tone::Success);
                        }
                    }
                    ui.label(widgets::bold(crate::VERSION));
                });
            });
            self.update_status(ui);
            ui.separator();
            if let Some(health) = &self.health {
                row(ui, &t("settings.storagePath"), &health.storage_path);
            }
            row(ui, &t("desktop.logs"), &crate::engine::data_folder().join("logs").display().to_string());
            #[cfg(target_os = "linux")]
            menu_entry_row(ui, c);
        });
        if check {
            self.check_updates(c, true);
        }
    }

    /// Download progress, then the restart into the new version; or how to update by hand.
    fn update_status(&mut self, ui: &mut egui::Ui) {
        if let Some(result) = finished(&mut self.update_download) {
            match result {
                // Installed once the window has closed (see updater::finish).
                Ok(_) => ui.ctx().send_viewport_cmd(egui::ViewportCommand::Close),
                Err(message) => self.update_error = Some(message),
            }
        }

        if self.update_download.is_some() {
            let fraction = self.update_progress.fraction();
            ui.add(egui::ProgressBar::new(fraction).desired_height(8.0).corner_radius(4));
            let percent = format!("{:.0}", fraction * 100.0);
            widgets::muted_small(
                ui,
                &if fraction >= 1.0 { t("desktop.updateRestarting") } else { tf("desktop.updateDownloading", &[("percent", &percent)]) },
            );
        }
        if let Some(error) = &self.update_error {
            notice(ui, &tf("desktop.updateFailed", &[("message", error)]), Tone::Danger);
        }
        if let Some(Ok(update)) = &self.update
            && update.update_available
            && crate::updater::install() == crate::updater::Install::Manual
        {
            widgets::muted_small(ui, &t("desktop.updateManual"));
        }
    }
}

/// Adds Tapeory to the desktop's application menu, or takes it out again: an AppImage or an
/// unpacked tar.gz has no installer that would.
#[cfg(target_os = "linux")]
fn menu_entry_row(ui: &mut egui::Ui, c: &mut Ctx) {
    use crate::menu_entry;

    let (Some(folder), Some(program)) = (menu_entry::user_folder(), menu_entry::program()) else { return };
    let present = menu_entry::present(&folder, &program);

    ui.separator();
    ui.horizontal(|ui| {
        ui.vertical(|ui| {
            widgets::muted(ui, &t("desktop.menuEntry"));
            widgets::muted_small(ui, &t(if present { "desktop.menuEntryPresent" } else { "desktop.menuEntryHint" }));
        });
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            let (label, done) = if present {
                ("desktop.menuEntryRemove", "desktop.menuEntryRemoved")
            } else {
                ("desktop.menuEntryAdd", "desktop.menuEntryAdded")
            };
            if button(ui, &t(label)).clicked() {
                let result = if present { menu_entry::remove(&folder) } else { menu_entry::add(&folder, &program) };
                match result {
                    Ok(()) => c.toasts.success(t(done)),
                    Err(error) => c.toasts.error(tf("desktop.menuEntryFailed", &[("message", &error.to_string())])),
                }
            }
        });
    });
}

const REPOSITORY_URL: &str = "https://github.com/Tunefish92/Tapeory";

/// The project on GitHub: the source, the issues, and the form for a new one.
fn links_card(ui: &mut egui::Ui) {
    theme::card(ui).show(ui, |ui| {
        ui.set_width(ui.available_width());
        widgets::section(ui, Icon::Globe, &t("about.links"));
        ui.horizontal_wrapped(|ui| {
            ui.spacing_mut().item_spacing.x = 10.0;
            let links = [("about.source", String::new()), ("about.issues", "/issues".to_string())];
            for (key, path) in links {
                if button(ui, &t(key)).clicked() {
                    ui.ctx().open_url(egui::OpenUrl::new_tab(format!("{REPOSITORY_URL}{path}")));
                }
            }
            if ui.add(PillButton::new(&t("about.newIssue"), Kind::Primary)).clicked() {
                ui.ctx().open_url(egui::OpenUrl::new_tab(format!("{REPOSITORY_URL}/issues/new")));
            }
        });
    });
}
