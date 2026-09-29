//! The dashboard: templates, printers, labels and jobs at a glance, fun facts, system status.

use eframe::egui::{self, RichText};

use crate::api::ApiResult;
use crate::fonts::{self, face};
use crate::i18n::{t, tf};
use crate::icons::Icon;
use crate::models::{DashboardStats, Health};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::widgets::{self, Tone};
use crate::ui::{Ctx, Route};
use crate::units::format_length;

#[derive(Default)]
pub struct DashboardPage {
    started: bool,
    stats: Option<DashboardStats>,
    health: Option<Health>,
    stats_task: Pending<ApiResult<DashboardStats>>,
    health_task: Pending<ApiResult<Health>>,
    error: Option<String>,
}

/// Things to compare the printed tape with, as on the web dashboard (length in mm).
const THINGS: [(&str, f64); 7] = [
    ("creditCard", 85.6),
    ("a4Sheet", 297.0),
    ("door", 2000.0),
    ("cityBus", 12_000.0),
    ("footballPitch", 105_000.0),
    ("eiffelTower", 330_000.0),
    ("marathon", 42_195_000.0),
];

impl DashboardPage {
    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            let api = c.api.clone();
            self.stats_task = Some(Task::spawn(c.egui, move || api.stats()));
            let api = c.api.clone();
            self.health_task = Some(Task::spawn(c.egui, move || api.health()));
        }

        if let Some(result) = finished(&mut self.stats_task) {
            match result {
                Ok(stats) => self.stats = Some(stats),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }
        if let Some(Ok(health)) = finished(&mut self.health_task) {
            self.health = Some(health);
        }

        egui::ScrollArea::vertical().show(ui, |ui| {
            widgets::page_header(ui, &t("nav.dashboard"), |_| {});

            if let Some(error) = &self.error {
                widgets::error_text(ui, error);
            }

            let Some(stats) = &self.stats else {
                widgets::loading(ui, &t("common.loading"));
                return;
            };

            let rate = if stats.print_job_count > 0 {
                tf(
                    "dashboard.successRate",
                    &[("rate", &format!("{}", (stats.completed_print_job_count * 100) / stats.print_job_count.max(1)))],
                )
            } else {
                t("dashboard.noPrintsYet")
            };

            let stats_row = [
                (Icon::Templates, "dashboard.statTemplates", stats.template_count, None, false),
                (Icon::Printer, "dashboard.statPrinters", stats.printer_count, None, false),
                (Icon::Label, "dashboard.statLabelsPrinted", stats.labels_printed, None, true),
                (Icon::Check, "dashboard.statPrintJobs", stats.print_job_count, Some(rate), false),
            ];
            let columns = if ui.available_width() < 760.0 { 2 } else { 4 };
            for (row, chunk) in stats_row.chunks(columns).enumerate() {
                widgets::columns(ui, &format!("stats{row}"), &vec![1.0; chunk.len()], |index, ui| {
                    let (icon, key, value, note, highlight) = &chunk[index];
                    stat(ui, *icon, &t(key), *value, note.clone(), *highlight);
                });
                ui.add_space(18.0);
            }

            if let Some(since) = &stats.stats_since {
                ui.horizontal(|ui| {
                    widgets::muted_small(ui, &tf("dashboard.statsSince", &[("date", &widgets::date_time(&Some(*since)))]));
                    if ui.link(t("dashboard.statsSinceChange")).clicked() {
                        c.go(Route::Settings);
                    }
                });
                ui.add_space(8.0);
            }

            let health = self.health.clone();
            let facts = |ui: &mut egui::Ui, c: &mut Ctx| {
                theme::card(ui).fill_height().show(ui, |ui| {
                    widgets::section_title(ui, &t("dashboard.funFactsTitle"));
                    ui.add_space(6.0);
                    row(ui, &t("dashboard.totalLength"), &format_length(stats.total_printed_length_mm, c.unit));
                    row(ui, &t("dashboard.totalArea"), &format!("{} cm²", trim_number(stats.total_printed_area_mm2 / 100.0)));
                    let most = stats
                        .most_printed_template_name
                        .as_ref()
                        .map(|name| {
                            tf("dashboard.mostPrintedValue", &[("name", name), ("count", &stats.most_printed_template_count.to_string())])
                        })
                        .unwrap_or_else(|| "—".into());
                    row(ui, &t("dashboard.mostPrinted"), &most);
                    let last =
                        if stats.last_printed_at.is_some() { widgets::date_time(&stats.last_printed_at) } else { t("dashboard.never") };
                    row(ui, &t("dashboard.lastPrinted"), &last);
                    ui.add_space(10.0);
                    comparison_box(ui, &comparison(stats.total_printed_length_mm));
                });
            };
            let system = |ui: &mut egui::Ui| {
                theme::card(ui).fill_height().show(ui, |ui| {
                    widgets::section_title(ui, &t("dashboard.title"));
                    let connected = health.as_ref().is_some_and(|health| health.database_connected);
                    widgets::muted(ui, &t(if connected { "dashboard.system.summaryOk" } else { "dashboard.system.summaryNoDatabase" }));
                    ui.add_space(4.0);
                    status_row(ui, &t("dashboard.system.server"), &t("desktop.engineHint"), &t("dashboard.system.online"), Tone::Success);
                    status_row(
                        ui,
                        &t("dashboard.system.database"),
                        &t("dashboard.system.databaseHint"),
                        &t(if connected { "dashboard.system.connected" } else { "dashboard.system.notConnected" }),
                        if connected { Tone::Success } else { Tone::Danger },
                    );
                    if let Some(health) = &health {
                        ui.label(widgets::bold(t("dashboard.system.storage")));
                        widgets::muted_small(ui, &t("dashboard.system.storageHint"));
                        code_box(ui, &health.storage_path);
                    }
                });
            };

            if ui.available_width() < 760.0 {
                facts(ui, c);
                ui.add_space(18.0);
                system(ui);
            } else {
                widgets::columns(ui, "facts", &[1.4, 1.0], |index, ui| if index == 0 { facts(ui, c) } else { system(ui) });
            }
        });
    }
}

fn stat(ui: &mut egui::Ui, icon: Icon, label: &str, value: i64, note: Option<String>, highlight: bool) {
    let palette = theme::palette(ui.ctx());
    let (text, muted) =
        if highlight { (egui::Color32::WHITE, egui::Color32::from_white_alpha(225)) } else { (palette.text, palette.muted) };

    let content = |ui: &mut egui::Ui| {
        widgets::icon_badge(ui, icon, highlight);
        ui.add_space(14.0);
        ui.label(RichText::new(spaced_upper(label)).family(face(fonts::HEADING)).size(12.5).color(muted));
        ui.label(RichText::new(value.to_string()).family(face(fonts::HEADING)).size(34.0).color(text));
        if let Some(note) = note {
            ui.label(RichText::new(note).size(13.0).color(muted));
        }
    };

    if highlight {
        widgets::gradient_card(ui, content);
    } else {
        theme::card(ui).fill_height().show(ui, content);
    }
}

/// Upper case with a little letter spacing, like the web's `letter-spacing: 0.08em` labels.
fn spaced_upper(text: &str) -> String {
    text.to_uppercase().chars().map(|c| c.to_string()).collect::<Vec<_>>().join("\u{200A}")
}

/// "36" rather than "36.0", "12.5" stays.
fn trim_number(value: f64) -> String {
    let text = format!("{value:.1}");
    text.strip_suffix(".0").map(str::to_string).unwrap_or(text)
}

/// The comparison ("That's about 1× the length of an A4 sheet.") in a soft box with a ruler.
fn comparison_box(ui: &mut egui::Ui, text: &str) {
    let palette = theme::palette(ui.ctx());
    egui::Frame::new().fill(palette.primary_soft).corner_radius(egui::CornerRadius::same(12)).inner_margin(egui::Margin::same(16)).show(
        ui,
        |ui| {
            ui.set_width(ui.available_width());
            ui.horizontal(|ui| {
                let (rect, _) = ui.allocate_exact_size(egui::vec2(34.0, 34.0), egui::Sense::hover());
                ui.painter().rect_filled(rect, egui::CornerRadius::same(8), palette.elevated);
                crate::icons::paint(ui, Icon::Ruler, egui::Rect::from_center_size(rect.center(), egui::vec2(18.0, 18.0)), palette.primary);
                ui.add_space(4.0);
                ui.add(egui::Label::new(text).wrap());
            });
        },
    );
}

/// A path or other value in monospace on a subtle background.
fn code_box(ui: &mut egui::Ui, text: &str) {
    let palette = theme::palette(ui.ctx());
    egui::Frame::new().fill(palette.subtle).corner_radius(egui::CornerRadius::same(8)).inner_margin(egui::Margin::symmetric(10, 8)).show(
        ui,
        |ui| {
            ui.set_width(ui.available_width());
            ui.label(RichText::new(text).monospace());
        },
    );
}

fn row(ui: &mut egui::Ui, label: &str, value: &str) {
    ui.spacing_mut().item_spacing.y = 8.0;
    ui.horizontal(|ui| {
        ui.set_min_height(28.0);
        widgets::muted(ui, label);
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            ui.label(widgets::bold(value));
        });
    });
    ui.separator();
}

fn status_row(ui: &mut egui::Ui, title: &str, hint: &str, state: &str, tone: Tone) {
    ui.allocate_ui_with_layout(egui::vec2(ui.available_width(), 0.0), egui::Layout::right_to_left(egui::Align::Center), |ui| {
        widgets::pill(ui, state, tone);
        ui.allocate_ui_with_layout(egui::vec2(ui.available_width(), 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
            ui.spacing_mut().item_spacing.y = 2.0;
            ui.label(widgets::bold(title));
            widgets::muted_small(ui, hint);
        });
    });
    ui.separator();
}

/// "That's about 2.8× the length of an A4 sheet." for the largest thing the tape beats.
fn comparison(total_mm: f64) -> String {
    match THINGS.iter().rev().find(|(_, length)| total_mm >= *length) {
        Some((thing, length)) => {
            tf("dashboard.comparison", &[("factor", &trim_number(total_mm / length)), ("thing", &t(&format!("dashboard.things.{thing}")))])
        }
        None => t("dashboard.comparisonEmpty"),
    }
}
