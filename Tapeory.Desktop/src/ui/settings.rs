//! Settings: language, theme, units, statistics, backups, users, and About with the update check.

use eframe::egui::{self};

use crate::api::ApiResult;
use crate::i18n::{self, LANGUAGES, t, tf};
use crate::icons::Icon;
use crate::models::{AppSettings, Backup, DashboardStats, Health, Printer, TemporaryPassword, UpdateCheck, UserResponse};
use crate::task::{Pending, Task, finished};
use crate::theme::{self, ThemeChoice};
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Kind, PillButton, Tone, button, danger_button, field, primary_button, primary_button_enabled};
use crate::ui::{Ctx, Route};
use crate::units::{Unit, format_length};

#[derive(Default)]
pub struct SettingsPage {
    started: bool,
    settings: AppSettings,
    settings_task: Pending<ApiResult<AppSettings>>,
    save_task: Pending<ApiResult<AppSettings>>,
    stats: Option<DashboardStats>,
    stats_task: Pending<ApiResult<DashboardStats>>,
    printers: Vec<Printer>,
    printers_task: Pending<ApiResult<Vec<Printer>>>,
    health: Option<Health>,
    health_task: Pending<ApiResult<Health>>,
    update: Option<Result<UpdateCheck, String>>,
    update_task: Pending<ApiResult<UpdateCheck>>,
    backups: [Vec<Backup>; 2],
    backups_task: [Pending<ApiResult<Vec<Backup>>>; 2],
    backup_action: Pending<ApiResult<String>>,
    confirm: Option<Confirm>,
    users: UsersCard,
    update_download: Pending<Result<std::path::PathBuf, String>>,
    update_progress: std::sync::Arc<crate::updater::Progress>,
    update_error: Option<String>,
}

enum Confirm {
    ResetStats,
    Restore(usize, Backup),
    Delete(usize, Backup),
}

const KINDS: [&str; 2] = ["database", "labels"];

impl SettingsPage {
    fn load_backups(&mut self, c: &Ctx, index: usize) {
        let api = c.api.clone();
        self.backups_task[index] = Some(Task::spawn(c.egui, move || api.backups(KINDS[index])));
    }

    fn check_updates(&mut self, c: &Ctx, refresh: bool) {
        let api = c.api.clone();
        self.update = None;
        self.update_task = Some(Task::spawn(c.egui, move || api.check_updates(refresh)));
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            let api = c.api.clone();
            self.settings_task = Some(Task::spawn(c.egui, move || api.settings()));
            let api = c.api.clone();
            self.stats_task = Some(Task::spawn(c.egui, move || api.stats()));
            let api = c.api.clone();
            self.printers_task = Some(Task::spawn(c.egui, move || api.printers()));
            let api = c.api.clone();
            self.health_task = Some(Task::spawn(c.egui, move || api.health()));
            self.check_updates(c, false);
            if c.can_administer() {
                self.load_backups(c, 0);
                self.load_backups(c, 1);
            }
        }

        self.poll(c);

        egui::ScrollArea::vertical().show(ui, |ui| {
            widgets::page_header(ui, &t("settings.title"), |_| {});

            // Three columns like the web, fewer in a narrow window.
            let per_row = if ui.available_width() >= 900.0 {
                3
            } else if ui.available_width() >= 560.0 {
                2
            } else {
                1
            };
            let mut cards: Vec<u8> = vec![0, 1, 2];
            if c.can_administer() {
                cards.extend([3, 4, 5]);
            }
            for (row, chunk) in cards.chunks(per_row).enumerate() {
                widgets::columns(ui, &format!("settings-{row}"), &vec![1.0; per_row], |index, ui| match chunk.get(index) {
                    Some(0) => self.language_card(ui, c),
                    Some(1) => self.appearance_card(ui, c),
                    Some(2) => self.printer_card(ui, c),
                    Some(3) => self.stats_card(ui, c),
                    Some(4) => self.backup_card(ui, c, 0),
                    Some(5) => self.backup_card(ui, c, 1),
                    _ => {}
                });
                ui.add_space(18.0);
            }

            if c.auth.user.as_ref().is_some_and(|user| user.is_admin()) {
                self.users.show(ui, c);
                ui.add_space(18.0);
            }

            self.about_card(ui, c);
        });

        self.confirm_dialogs(c);
    }

    fn poll(&mut self, c: &mut Ctx) {
        if let Some(Ok(settings)) = finished(&mut self.settings_task) {
            self.settings = settings;
        }
        if let Some(Err(error)) = finished(&mut self.save_task) {
            c.failed(&error);
        }
        if let Some(result) = finished(&mut self.stats_task) {
            match result {
                Ok(stats) => self.stats = Some(stats),
                Err(error) => c.failed(&error),
            }
        }
        if let Some(Ok(printers)) = finished(&mut self.printers_task) {
            self.printers = printers;
        }
        if let Some(Ok(health)) = finished(&mut self.health_task) {
            self.health = Some(health);
        }
        if let Some(result) = finished(&mut self.update_task) {
            self.update = Some(result.map_err(|e| e.message));
        }
        for index in 0..2 {
            if let Some(Ok(backups)) = finished(&mut self.backups_task[index]) {
                self.backups[index] = backups;
            }
        }
        if let Some(result) = finished(&mut self.backup_action) {
            match result {
                Ok(message) => c.toasts.success(message),
                Err(error) => c.failed(&error),
            }
            self.load_backups(c, 0);
            self.load_backups(c, 1);
            let api = c.api.clone();
            self.stats_task = Some(Task::spawn(c.egui, move || api.stats()));
        }
    }

    /// Applies the change at once and saves it in the background.
    fn save_settings(&mut self, c: &mut Ctx, patch: serde_json::Value) {
        let text = |key: &str| patch.get(key).and_then(|value| value.as_str()).map(str::to_string);
        if let Some(language) = text("language") {
            self.settings.language = Some(language);
        }
        if let Some(theme) = text("theme") {
            self.settings.theme = Some(theme);
        }
        if let Some(unit) = text("unit") {
            self.settings.unit = Some(unit);
        }
        *c.settings_saved = Some(self.settings.clone());

        let api = c.api.clone();
        self.save_task = Some(Task::spawn(c.egui, move || api.update_settings(&patch)));
    }

    fn language_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        theme::card(ui).fill_height().show(ui, |ui| {
            widgets::section(ui, Icon::Globe, &t("settings.language"));
            let current = i18n::language();
            let name = LANGUAGES.iter().find(|(code, _)| *code == current).map(|(_, name)| *name).unwrap_or("English");
            let mut chosen = None;
            egui::ComboBox::from_id_salt("language").selected_text(name).show_ui(ui, |ui| {
                crate::ui::widgets::compact_menu(ui);
                for (code, name) in LANGUAGES {
                    if ui.selectable_label(code == current, name).clicked() {
                        chosen = Some(code);
                    }
                }
            });
            if let Some(code) = chosen {
                i18n::set_language(code);
                self.save_settings(c, crate::app::settings_patch(Some(code), None, None));
            }
        });
    }

    fn appearance_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        theme::card(ui).fill_height().show(ui, |ui| {
            widgets::section(ui, Icon::Moon, &t("settings.theme"));
            let current = ThemeChoice::from_setting(self.settings.theme.as_deref());
            let mut chosen = None;
            egui::ComboBox::from_id_salt("theme").selected_text(theme_name(current)).show_ui(ui, |ui| {
                crate::ui::widgets::compact_menu(ui);
                for choice in [ThemeChoice::Light, ThemeChoice::Dark, ThemeChoice::System] {
                    if ui.selectable_label(choice == current, theme_name(choice)).clicked() {
                        chosen = Some(choice);
                    }
                }
            });
            if let Some(choice) = chosen {
                self.save_settings(c, crate::app::settings_patch(None, Some(choice), None));
            }

            ui.add_space(6.0);
            widgets::muted_small(ui, &t("desktop.units"));
            let mut unit = c.unit;
            ui.horizontal(|ui| {
                ui.radio_value(&mut unit, Unit::Mm, "mm");
                ui.radio_value(&mut unit, Unit::Inch, "in");
            });
            if unit != c.unit {
                self.save_settings(c, crate::app::settings_patch(None, None, Some(unit)));
            }
        });
    }

    fn printer_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        theme::card(ui).fill_height().show(ui, |ui| {
            widgets::section(ui, Icon::Printer, &t("settings.defaultPrinter"));
            match self.printers.iter().find(|printer| printer.is_default) {
                Some(printer) => ui.label(&printer.name),
                None => ui.label(t("settings.noDefaultPrinter")),
            };
            if c.can_administer() && button(ui, &t("settings.managePrinters")).clicked() {
                c.go(Route::Printers);
            }
        });
    }

    fn stats_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        theme::card(ui).fill_height().show(ui, |ui| {
            widgets::section(ui, Icon::Chart, &t("settings.statistics"));
            let Some(stats) = &self.stats else {
                ui.spinner();
                return;
            };
            row(ui, &t("settings.statsLabelsPrinted"), &stats.labels_printed.to_string());
            row(ui, &t("settings.statsTapeUsed"), &format_length(stats.total_printed_length_mm, c.unit));
            let since = if stats.stats_since.is_some() { widgets::date_time(&stats.stats_since) } else { t("settings.statsAllTime") };
            row(ui, &t("settings.statsCountingSince"), &since);

            ui.horizontal_wrapped(|ui| {
                if danger_button(ui, &t("settings.statsReset")).clicked() {
                    self.confirm = Some(Confirm::ResetStats);
                }
                if stats.stats_since.is_some() && button(ui, &t("settings.statsRestore")).clicked() {
                    let api = c.api.clone();
                    self.backup_action = Some(Task::spawn(c.egui, move || api.reset_stats(true).map(|_| t("settings.statsRestoreDone"))));
                }
            });
        });
    }

    fn backup_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx, index: usize) {
        let (title, text) = if index == 0 {
            ("settings.backupDatabaseTitle", "desktop.backupDatabaseText")
        } else {
            ("settings.backupLabelsTitle", "desktop.backupLabelsText")
        };
        let busy = self.backup_action.is_some();

        theme::card(ui).fill_height().show(ui, |ui| {
            widgets::section(ui, if index == 0 { Icon::Database } else { Icon::Tag }, &t(title));
            widgets::muted(ui, &t(text));
            let label = if busy { t("settings.backupCreating") } else { t("settings.backupCreate") };
            if primary_button_enabled(ui, !busy, &label).clicked() {
                let api = c.api.clone();
                self.backup_action =
                    Some(Task::spawn(c.egui, move || api.create_backup(KINDS[index]).map(|_| t("settings.backupCreated"))));
            }

            if self.backups[index].is_empty() {
                widgets::muted_small(ui, &t("settings.backupNone"));
            }

            for backup in self.backups[index].clone().iter().take(6) {
                ui.separator();
                ui.horizontal_wrapped(|ui| {
                    ui.label(widgets::bold(widgets::date_time(&backup.created_at)));
                    widgets::muted_small(ui, &size(backup.size_bytes));
                    if backup.before_restore {
                        widgets::chip(ui, &t("settings.backupBeforeRestore"), Tone::Info);
                    }
                });
                ui.spacing_mut().interact_size.y = 28.0;
                ui.horizontal_wrapped(|ui| {
                    ui.spacing_mut().item_spacing = egui::vec2(6.0, 6.0);
                    if ui.add(PillButton::new(&t("settings.backupDownload"), Kind::Secondary).small()).clicked() {
                        download(c, KINDS[index], backup);
                    }
                    if ui.add(PillButton::new(&t("settings.backupRestore"), Kind::Secondary).small()).clicked() {
                        self.confirm = Some(Confirm::Restore(index, backup.clone()));
                    }
                    if ui.add(PillButton::new(&t("common.delete"), Kind::Danger).small()).clicked() {
                        self.confirm = Some(Confirm::Delete(index, backup.clone()));
                    }
                });
            }
        });
    }

    fn about_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        let mut check = false;
        theme::card(ui).show(ui, |ui| {
            widgets::section(ui, Icon::Info, &t("settings.about"));

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

    fn confirm_dialogs(&mut self, c: &mut Ctx) {
        let Some(confirm) = &self.confirm else { return };

        let (title, text, button_label) = match confirm {
            Confirm::ResetStats => (t("settings.statsReset"), t("settings.statsResetConfirmText"), t("settings.statsResetConfirm")),
            Confirm::Restore(index, backup) => {
                let key = if *index == 0 { "settings.backupRestoreDatabaseConfirmText" } else { "settings.backupRestoreLabelsConfirmText" };
                (
                    t("settings.backupRestore"),
                    tf(key, &[("date", &widgets::date_time(&backup.created_at))]),
                    t("settings.backupRestoreConfirm"),
                )
            }
            Confirm::Delete(_, backup) => (
                t("common.delete"),
                tf("settings.backupDeleteConfirmText", &[("date", &widgets::date_time(&backup.created_at))]),
                t("settings.backupDeleteConfirm"),
            ),
        };

        match widgets::confirm(c.egui, "settings-confirm", &title, &text, &button_label) {
            Some(true) => {
                let api = c.api.clone();
                self.backup_action = Some(match self.confirm.take() {
                    Some(Confirm::ResetStats) => Task::spawn(c.egui, move || api.reset_stats(false).map(|_| t("settings.statsResetDone"))),
                    Some(Confirm::Restore(index, backup)) => Task::spawn(c.egui, move || {
                        api.restore_backup(KINDS[index], &backup.file_name).map(|result| match result.restored_templates {
                            Some(count) => tf("settings.backupRestoredLabels", &[("templates", &count.to_string())]),
                            None => t("settings.backupRestored"),
                        })
                    }),
                    Some(Confirm::Delete(index, backup)) => {
                        Task::spawn(c.egui, move || api.delete_backup(KINDS[index], &backup.file_name).map(|_| t("settings.backupDeleted")))
                    }
                    None => return,
                });
            }
            Some(false) => self.confirm = None,
            None => {}
        }
    }
}

fn theme_name(choice: ThemeChoice) -> String {
    t(match choice {
        ThemeChoice::Light => "settings.themeLight",
        ThemeChoice::Dark => "settings.themeDark",
        ThemeChoice::System => "settings.themeSystem",
    })
}

fn row(ui: &mut egui::Ui, label: &str, value: &str) {
    ui.horizontal(|ui| {
        widgets::muted(ui, label);
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            ui.label(widgets::bold(value));
        });
    });
}

fn size(bytes: i64) -> String {
    match bytes {
        b if b >= 1_048_576 => format!("{:.1} MB", b as f64 / 1_048_576.0),
        b if b >= 1024 => format!("{:.1} KB", b as f64 / 1024.0),
        b => format!("{b} B"),
    }
}

/// Saves a backup from the engine wherever the user picks.
fn download(c: &mut Ctx, kind: &str, backup: &Backup) {
    let (kind, name) = (kind.to_string(), backup.file_name.clone());
    c.save_file(backup.file_name.clone(), move |api| api.download_backup(&kind, &name));
}

/// Accounts, for administrators of a database that has them.
#[derive(Default)]
struct UsersCard {
    users: Option<Vec<UserResponse>>,
    load: Pending<ApiResult<Vec<UserResponse>>>,
    action: Pending<ApiResult<Option<TemporaryPassword>>>,
    temporary: Option<TemporaryPassword>,
    new_user: String,
    new_display: String,
    new_role: String,
    editing: Option<(i64, String, String, bool)>,
    deleting: Option<(UserResponse, bool)>,
    resetting: Option<UserResponse>,
}

impl UsersCard {
    fn reload(&mut self, c: &Ctx) {
        let api = c.api.clone();
        self.load = Some(Task::spawn(c.egui, move || api.users()));
    }

    fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if self.users.is_none() && self.load.is_none() {
            self.reload(c);
        }
        if self.new_role.is_empty() {
            self.new_role = "User".into();
        }

        if let Some(result) = finished(&mut self.load) {
            match result {
                Ok(users) => self.users = Some(users),
                Err(error) => c.failed(&error),
            }
        }
        if let Some(result) = finished(&mut self.action) {
            match result {
                Ok(temporary) => {
                    if temporary.is_some() {
                        self.temporary = temporary;
                    }
                }
                Err(error) => c.failed(&error),
            }
            self.reload(c);
        }

        let me = c.auth.user.as_ref().map(|user| user.id);

        theme::card(ui).show(ui, |ui| {
            widgets::section(ui, Icon::Users, &t("settings.users"));
            widgets::muted_small(ui, &t("settings.usersIntro"));

            if let Some(temporary) = self.temporary.clone() {
                notice(ui, &tf("settings.usersTemporaryPassword", &[("name", &temporary.user.user_name)]), Tone::Info);
                ui.horizontal(|ui| {
                    ui.label(widgets::bold(&temporary.temporary_password).monospace().size(18.0));
                    if button(ui, &t("settings.usersCopy")).clicked() {
                        ui.ctx().copy_text(temporary.temporary_password.clone());
                    }
                    if button(ui, &t("common.close")).clicked() {
                        self.temporary = None;
                    }
                });
                widgets::muted_small(ui, &t("settings.usersTemporaryHint"));
            }

            let Some(users) = self.users.clone() else {
                ui.spinner();
                return;
            };

            for user in &users {
                ui.separator();
                let editing = self.editing.as_ref().is_some_and(|(id, ..)| *id == user.id);

                if editing {
                    let (_, display, role, disabled) = self.editing.as_mut().expect("editing");
                    let own = me == Some(user.id);
                    ui.horizontal_wrapped(|ui| {
                        field(ui, &t("settings.usersDisplayName"), |ui| widgets::text_input(ui, display, 200.0));
                        field(ui, &t("settings.usersRole"), |ui| {
                            ui.add_enabled_ui(!own, |ui| role_picker(ui, role, &format!("role-{}", user.id)));
                        });
                        ui.add_enabled(!own, egui::Checkbox::new(disabled, t("settings.usersDisabled")));
                    });
                    ui.horizontal(|ui| {
                        if primary_button(ui, &t("common.save")).clicked() {
                            let (id, display, role, disabled) = self.editing.take().expect("editing");
                            let api = c.api.clone();
                            self.action = Some(Task::spawn(c.egui, move || api.update_user(id, &display, &role, disabled).map(|_| None)));
                        }
                        if button(ui, &t("common.cancel")).clicked() {
                            self.editing = None;
                        }
                    });
                    continue;
                }

                ui.horizontal(|ui| {
                    ui.vertical(|ui| {
                        let you = if me == Some(user.id) { format!(" {}", t("settings.usersYou")) } else { String::new() };
                        ui.label(widgets::bold(format!("{}{you}", user.display_name)));
                        let last = if user.last_login_at.is_some() {
                            tf("settings.usersLastLogin", &[("when", &widgets::relative(&user.last_login_at))])
                        } else {
                            t("settings.usersNeverSignedIn")
                        };
                        widgets::muted_small(ui, &format!("{} · {last}", user.user_name));
                    });
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if me != Some(user.id) {
                            if danger_button(ui, &t("common.delete")).clicked() {
                                self.deleting = Some((user.clone(), false));
                            }
                            if button(ui, &t("settings.usersResetPassword")).clicked() {
                                self.resetting = Some(user.clone());
                            }
                        }
                        if button(ui, &t("common.edit")).clicked() {
                            self.editing = Some((user.id, user.display_name.clone(), user.role.clone(), user.disabled));
                        }
                        if user.disabled {
                            widgets::pill(ui, &t("settings.usersDisabled"), Tone::Danger);
                        } else if user.must_change_password {
                            widgets::pill(ui, &t("settings.usersTemporary"), Tone::Neutral);
                        }
                        let role = t(if user.role == "Admin" { "auth.roleAdmin" } else { "auth.roleUser" });
                        widgets::pill(ui, &role, if user.role == "Admin" { Tone::Info } else { Tone::Neutral });
                    });
                });
            }

            ui.separator();
            ui.horizontal_wrapped(|ui| {
                field(ui, &t("settings.usersUserName"), |ui| widgets::text_input(ui, &mut self.new_user, 180.0));
                field(ui, &t("settings.usersDisplayNameOptional"), |ui| widgets::text_input(ui, &mut self.new_display, 180.0));
                field(ui, &t("settings.usersRole"), |ui| role_picker(ui, &mut self.new_role, "new-role"));
                if primary_button_enabled(ui, !self.new_user.trim().is_empty(), &t("settings.usersAdd")).clicked() {
                    let api = c.api.clone();
                    let (user, display, role) =
                        (self.new_user.trim().to_string(), self.new_display.trim().to_string(), self.new_role.clone());
                    self.new_user.clear();
                    self.new_display.clear();
                    self.action = Some(Task::spawn(c.egui, move || api.create_user(&user, &display, &role).map(Some)));
                }
            });
        });

        if let Some(user) = self.resetting.clone() {
            let text = tf("settings.usersResetConfirm", &[("name", &user.display_name)]);
            match widgets::confirm(c.egui, "reset-user", &t("settings.usersResetPassword"), &text, &t("settings.usersResetPassword")) {
                Some(true) => {
                    let api = c.api.clone();
                    self.action = Some(Task::spawn(c.egui, move || api.reset_password(user.id).map(Some)));
                    self.resetting = None;
                }
                Some(false) => self.resetting = None,
                None => {}
            }
        }

        if let Some((user, delete_templates)) = &mut self.deleting {
            let mut outcome = None;
            egui::Window::new(tf("settings.usersDeleteTitle", &[("name", &user.display_name)]))
                .collapsible(false)
                .resizable(false)
                .anchor(egui::Align2::CENTER_CENTER, egui::Vec2::ZERO)
                .show(c.egui, |ui| {
                    ui.label(tf("settings.usersDeleteConfirm", &[("name", &user.display_name)]));
                    ui.label(widgets::bold(t("settings.usersDeleteTemplates")));
                    ui.radio_value(delete_templates, false, t("settings.usersDeleteTransfer"));
                    ui.radio_value(delete_templates, true, t("settings.usersDeleteTemplatesToo"));
                    ui.horizontal(|ui| {
                        if danger_button(ui, &t("settings.usersDeleteAction")).clicked() {
                            outcome = Some(true);
                        }
                        if button(ui, &t("common.cancel")).clicked() {
                            outcome = Some(false);
                        }
                    });
                });
            match outcome {
                Some(true) => {
                    let (user, delete_templates) = self.deleting.take().expect("deleting");
                    let api = c.api.clone();
                    self.action = Some(Task::spawn(c.egui, move || api.delete_user(user.id, delete_templates).map(|_| None)));
                }
                Some(false) => self.deleting = None,
                None => {}
            }
        }
    }
}

fn role_picker(ui: &mut egui::Ui, role: &mut String, id: &str) {
    let name = |role: &str| t(if role == "Admin" { "auth.roleAdmin" } else { "auth.roleUser" });
    egui::ComboBox::from_id_salt(id).selected_text(name(role)).show_ui(ui, |ui| {
        crate::ui::widgets::compact_menu(ui);
        ui.selectable_value(role, "User".to_string(), name("User"));
        ui.selectable_value(role, "Admin".to_string(), name("Admin"));
    });
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
