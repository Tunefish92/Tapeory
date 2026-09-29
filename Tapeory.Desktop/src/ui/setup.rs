//! First start: where the data lives, "On this computer" (SQLite) or a MySQL/MariaDB server.

use eframe::egui::{self, RichText};

use crate::api::{Api, ApiResult};
use crate::i18n::{t, tf};
use crate::models::{DatabaseSetupRequest, DatabaseTestResult};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::Toasts;
use crate::ui::widgets::{self, button, field, primary_button_enabled, text_input};

pub struct SetupPage {
    server: bool,
    host: String,
    port: String,
    database: String,
    user: String,
    password: String,
    busy: Pending<ApiResult<Option<DatabaseTestResult>>>,
    result: Option<(bool, String)>,
}

impl Default for SetupPage {
    fn default() -> Self {
        SetupPage {
            server: false,
            host: String::new(),
            port: "3306".into(),
            database: "tapeory".into(),
            user: "tapeory".into(),
            password: String::new(),
            busy: None,
            result: None,
        }
    }
}

impl SetupPage {
    fn request(&self) -> DatabaseSetupRequest {
        DatabaseSetupRequest {
            host: self.host.trim().to_string(),
            port: self.port.trim().parse().unwrap_or(3306),
            database: self.database.trim().to_string(),
            user: self.user.trim().to_string(),
            password: self.password.clone(),
        }
    }

    /// Returns true once the database is set up.
    pub fn show(&mut self, ui: &mut egui::Ui, api: &Api, desktop: bool, _toasts: &mut Toasts) -> bool {
        let mut done = false;

        if let Some(result) = finished(&mut self.busy) {
            match result {
                // A test: say how it went.
                Ok(Some(test)) => {
                    self.result = Some(if test.is_success {
                        let text = if test.database_exists {
                            t("setup.testSucceeded")
                        } else {
                            tf("setup.testSucceededNewDatabase", &[("database", &self.database)])
                        };
                        (true, text)
                    } else {
                        (false, test.error_message.unwrap_or_else(|| t("setup.failedFallback")))
                    });
                }
                // Saved (locally or on the server).
                Ok(None) => done = true,
                Err(error) => self.result = Some((false, error.message)),
            }
        }

        let busy = self.busy.is_some();
        // Without the desktop app there's only the server form.
        let server = self.server || !desktop;

        {
            widgets::centered_card(ui, "setup", 560.0, |ui| {
                widgets::logo(ui, 18.5, crate::theme::palette(ui.ctx()).text);
                ui.add_space(10.0);
                ui.label(widgets::heading(t("setup.title")).size(28.0));

                if !server {
                    widgets::muted(ui, &t("setup.desktopIntro"));
                    ui.add_space(8.0);

                    if choice(ui, &t("setup.localTitle"), &t("setup.localText"), true, !busy).clicked() {
                        let api = api.clone();
                        self.result = None;
                        self.busy = Some(Task::spawn(ui.ctx(), move || api.setup_local_database().map(|_| None)));
                    }
                    ui.add_space(6.0);
                    if choice(ui, &t("setup.serverTitle"), &t("setup.serverText"), false, !busy).clicked() {
                        self.server = true;
                        self.result = None;
                    }

                    if busy {
                        widgets::loading(ui, &t("setup.creatingLocal"));
                    }
                } else {
                    widgets::muted(ui, &t("setup.intro"));
                    ui.add_space(8.0);

                    ui.horizontal(|ui| {
                        field(ui, &t("setup.host"), |ui| {
                            ui.add(
                                egui::TextEdit::singleline(&mut self.host)
                                    .margin(egui::Margin::symmetric(11, 9))
                                    .hint_text(crate::ui::widgets::hint(ui.ctx(), t("setup.hostPlaceholder")))
                                    .desired_width(320.0),
                            )
                        });
                        field(ui, &t("setup.port"), |ui| text_input(ui, &mut self.port, 90.0));
                    });
                    field(ui, &t("setup.database"), |ui| text_input(ui, &mut self.database, 420.0));
                    field(ui, &t("setup.user"), |ui| text_input(ui, &mut self.user, 420.0));
                    field(ui, &t("setup.password"), |ui| widgets::password_input(ui, &mut self.password, 420.0));
                    widgets::muted_small(ui, &t("setup.storedNote"));

                    if busy {
                        widgets::loading(ui, &t("setup.testing"));
                    }

                    ui.horizontal(|ui| {
                        if desktop && button(ui, &t("common.back")).clicked() {
                            self.server = false;
                            self.result = None;
                        }
                        if widgets::button_enabled(ui, !busy && !self.host.trim().is_empty(), &t("setup.test")).clicked() {
                            let api = api.clone();
                            let request = self.request();
                            self.result = None;
                            self.busy = Some(Task::spawn(ui.ctx(), move || api.test_database(&request).map(Some)));
                        }
                        if primary_button_enabled(ui, !busy && !self.host.trim().is_empty(), &t("setup.save")).clicked() {
                            let api = api.clone();
                            let request = self.request();
                            self.result = None;
                            self.busy = Some(Task::spawn(ui.ctx(), move || api.setup_database(&request).map(|_| None)));
                        }
                    });
                }

                if let Some((success, message)) = &self.result {
                    ui.add_space(6.0);
                    let tone = if *success { widgets::Tone::Success } else { widgets::Tone::Danger };
                    notice(ui, message, tone);
                }
            });
        }

        done
    }
}

/// One of the two big choices on the desktop app's first start: a card with an icon, the title
/// and what it means; the recommended one is highlighted.
fn choice(ui: &mut egui::Ui, title: &str, text: &str, recommended: bool, enabled: bool) -> egui::Response {
    let palette = theme::palette(ui.ctx());
    let icon = if recommended { crate::icons::Icon::Database } else { crate::icons::Icon::Users };
    let width = ui.available_width();
    let background = ui.painter().add(egui::Shape::Noop);

    let inner = egui::Frame::new().inner_margin(egui::Margin::same(16)).show(ui, |ui| {
        ui.set_width(width - 32.0);
        ui.horizontal_top(|ui| {
            widgets::icon_badge(ui, icon, false);
            ui.add_space(4.0);
            ui.vertical(|ui| {
                ui.spacing_mut().item_spacing.y = 3.0;
                ui.label(widgets::heading(title).size(16.0));
                ui.add(egui::Label::new(RichText::new(text).color(palette.muted)).wrap());
            });
        });
    });

    let rect = inner.response.rect;
    let sense = if enabled { egui::Sense::click() } else { egui::Sense::hover() };
    let response = ui.interact(rect, ui.id().with(("setup-choice", title)), sense);
    let (fill, stroke) = if recommended || (response.hovered() && enabled) {
        (palette.primary_soft, egui::Stroke::new(1.5_f32, palette.primary))
    } else {
        (egui::Color32::TRANSPARENT, egui::Stroke::new(1.0_f32, palette.input_border))
    };
    ui.painter().set(background, egui::epaint::RectShape::new(rect, egui::CornerRadius::same(14), fill, stroke, egui::StrokeKind::Inside));
    if enabled { response.on_hover_cursor(egui::CursorIcon::PointingHand) } else { response }
}

/// A coloured message box.
pub fn notice(ui: &mut egui::Ui, message: &str, tone: widgets::Tone) {
    let palette = theme::palette(ui.ctx());
    let (bg, fg) = match tone {
        widgets::Tone::Success => (palette.success_bg, palette.success_text),
        widgets::Tone::Danger => (palette.danger_bg, palette.danger_text),
        widgets::Tone::Info => (palette.info_bg, palette.info_text),
        widgets::Tone::Neutral => (palette.neutral_bg, palette.neutral_text),
    };
    egui::Frame::new().fill(bg).corner_radius(egui::CornerRadius::same(10)).inner_margin(egui::Margin::symmetric(12, 8)).show(ui, |ui| {
        ui.label(RichText::new(message).color(fg));
    });
}
