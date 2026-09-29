//! The print history, and a single job with its label previews.

use std::time::Duration;

use eframe::egui::{self};

use crate::api::ApiResult;
use crate::i18n::{t, tf};
use crate::icons::{self, Icon};
use crate::images::ImageState;
use crate::models::{DeleteAllResult, PrintJob};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::grid::{self, Column};
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Kind, PillButton, Tone};
use crate::ui::{Ctx, Route};

#[derive(Default)]
pub struct JobsPage {
    started: bool,
    sort: grid::Sort,
    jobs: Option<Vec<PrintJob>>,
    load: Pending<ApiResult<Vec<PrintJob>>>,
    delete: Pending<ApiResult<String>>,
    delete_all: Pending<ApiResult<DeleteAllResult>>,
    confirm_delete: Option<PrintJob>,
    confirm_delete_all: bool,
    error: Option<String>,
}

impl JobsPage {
    fn reload(&mut self, c: &Ctx) {
        let api = c.api.clone();
        self.load = Some(Task::spawn(c.egui, move || api.print_jobs()));
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            self.reload(c);
        }

        if let Some(result) = finished(&mut self.load) {
            match result {
                Ok(jobs) => self.jobs = Some(jobs),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        // Keep following jobs that are still printing, every two seconds.
        if self.load.is_none() && self.jobs.as_ref().is_some_and(|jobs| jobs.iter().any(PrintJob::in_progress)) {
            let api = c.api.clone();
            self.load = Some(Task::spawn(c.egui, move || {
                std::thread::sleep(Duration::from_secs(2));
                api.print_jobs()
            }));
        }

        if let Some(result) = finished(&mut self.delete) {
            match result {
                Ok(message) => c.toasts.success(message),
                Err(error) => c.failed(&error),
            }
            self.reload(c);
        }

        if let Some(result) = finished(&mut self.delete_all) {
            match result {
                Ok(result) => {
                    let mut message = tf("printing.printJobsList.allDeleted", &[("count", &result.deleted.to_string())]);
                    if result.skipped_in_progress > 0 {
                        message.push(' ');
                        message.push_str(&tf(
                            "printing.printJobsList.keptStillPrinting",
                            &[("count", &result.skipped_in_progress.to_string())],
                        ));
                    }
                    c.toasts.success(message);
                }
                Err(error) => c.failed(&error),
            }
            self.reload(c);
        }

        let jobs = self.jobs.clone();
        widgets::page_header(ui, &t("printing.printJobsList.title"), |ui| {
            let deletable = jobs.as_ref().is_some_and(|jobs| jobs.iter().any(|job| !job.in_progress()));
            if deletable && ui.add(PillButton::new(&t("printing.printJobsList.deleteAll"), Kind::Danger).icon(Icon::Trash)).clicked() {
                self.confirm_delete_all = true;
            }
        });

        if let Some(error) = &self.error {
            widgets::error_text(ui, error);
        }

        let Some(jobs) = jobs else {
            widgets::loading(ui, &t("printing.printJobsList.loading"));
            return;
        };

        if jobs.is_empty() {
            theme::card(ui).show(ui, |ui| widgets::muted(ui, &t("printing.printJobsList.empty")));
            return;
        }

        let columns = [
            Column::new(t("printing.printJobsList.columnJob"), 2.6),
            Column::new(t("printing.printJobsList.columnTemplate"), 1.6).hide_below(640.0),
            Column::new(t("printing.printJobsList.columnPrinter"), 1.9).hide_below(760.0),
            Column::new(t("printing.printJobsList.columnLabels"), 0.9).hide_below(900.0),
            Column::new(t("printing.printJobsList.columnStatus"), 1.3),
            Column::new(t("printing.printJobsList.columnCreated"), 1.5).hide_below(560.0),
            Column::fixed(if jobs.iter().any(|job| !job.template_deleted) { 120.0 } else { 86.0 }),
        ];

        let mut rows: Vec<&PrintJob> = jobs.iter().collect();
        if let Some((column, ascending)) = self.sort {
            rows.sort_by(|a, b| {
                let order = match column {
                    0 => a.id.cmp(&b.id),
                    1 => a.template_name.to_lowercase().cmp(&b.template_name.to_lowercase()),
                    2 => a.printer_name.cmp(&b.printer_name),
                    3 => a.label_count().cmp(&b.label_count()),
                    4 => a.status.cmp(&b.status),
                    _ => a.created_at.cmp(&b.created_at),
                };
                if ascending { order } else { order.reverse() }
            });
        }

        let palette = theme::palette(ui.ctx());
        let footer = tf("printing.printJobsList.gridCount", &[("count", &rows.len().to_string())]);
        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
            let mut sort = self.sort;
            let clicked = grid::show(ui, &columns, &mut sort, rows.len(), 64.0, Some(&footer), |ui, row, column| {
                let job = rows[row];
                match column {
                    0 => {
                        let (bg, fg) = match widgets::status_tone(&job.status) {
                            Tone::Success => (palette.success_bg, palette.success_text),
                            Tone::Danger => (palette.danger_bg, palette.danger_text),
                            Tone::Info => (palette.info_bg, palette.info_text),
                            Tone::Neutral => (palette.neutral_bg, palette.neutral_text),
                        };
                        let (rect, _) = ui.allocate_exact_size(egui::vec2(40.0, 40.0), egui::Sense::hover());
                        ui.painter().rect_filled(rect, egui::CornerRadius::same(12), bg);
                        icons::paint(ui, Icon::Printer, egui::Rect::from_center_size(rect.center(), egui::vec2(20.0, 20.0)), fg);
                        ui.vertical(|ui| {
                            ui.spacing_mut().item_spacing.y = 0.0;
                            ui.add_space(10.0);
                            ui.label(widgets::heading(format!("#{}", job.id)).size(16.0));
                            ui.label(
                                egui::RichText::new(tf("printing.printJobsList.itemsCount", &[("count", &job.items.len().to_string())]))
                                    .size(12.5)
                                    .color(palette.muted),
                            );
                        });
                    }
                    1 => {
                        ui.vertical(|ui| {
                            ui.spacing_mut().item_spacing.y = 0.0;
                            ui.add_space(if job.template_deleted { 12.0 } else { 20.0 });
                            grid::muted(ui, &job.template_name);
                            if job.template_deleted {
                                ui.add(
                                    egui::Label::new(
                                        egui::RichText::new(t("printing.printJobsList.templateDeleted")).size(12.5).color(palette.muted),
                                    )
                                    .truncate(),
                                );
                            }
                        });
                    }
                    2 => {
                        icons::icon(ui, Icon::Printer, 15.0, palette.muted);
                        ui.vertical(|ui| {
                            ui.spacing_mut().item_spacing.y = 0.0;
                            ui.add_space(if job.printed_by.is_some() { 12.0 } else { 20.0 });
                            ui.add(egui::Label::new(job.printer_name.clone().unwrap_or_else(|| "—".into())).truncate());
                            if let Some(by) = &job.printed_by {
                                ui.add(
                                    egui::Label::new(
                                        egui::RichText::new(tf("printing.printJobsList.printedBy", &[("name", by)]))
                                            .size(12.5)
                                            .color(palette.muted),
                                    )
                                    .truncate(),
                                );
                            }
                        });
                    }
                    3 => {
                        ui.label(widgets::heading(job.label_count().to_string()).size(15.0));
                    }
                    4 => {
                        widgets::pill(ui, &widgets::status_text(&job.status), widgets::status_tone(&job.status));
                        if job.in_progress() {
                            ui.spinner();
                        }
                    }
                    5 => {
                        grid::muted(ui, &widgets::relative(&job.created_at)).on_hover_text(widgets::date_time(&job.created_at));
                    }
                    _ => {
                        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                            ui.spacing_mut().item_spacing.x = 2.0;
                            ui.add_enabled_ui(!job.in_progress(), |ui| {
                                let delete = widgets::icon_button(ui, Icon::Trash, &t("printing.printJobsList.deleteJob"));
                                if delete.on_disabled_hover_text(t("printing.printJobsList.deleteJobStillPrinting")).clicked() {
                                    self.confirm_delete = Some(job.clone());
                                }
                            });
                            if widgets::icon_button(ui, Icon::Eye, &t("printing.printJobsList.viewDetails")).clicked() {
                                c.go(Route::Job(job.id));
                            }
                            if !job.template_deleted
                                && widgets::icon_button(ui, Icon::Refresh, &t("printing.printJobsList.printAgain")).clicked()
                            {
                                c.go(Route::Print(job.template_id));
                            }
                        });
                    }
                }
            });

            self.sort = sort;
            if let Some(row) = clicked {
                c.go(Route::Job(rows[row].id));
            }
        });

        if let Some(job) = self.confirm_delete.clone() {
            let text = tf("printing.printJobsList.confirmDeleteJob", &[("id", &job.id.to_string())]);
            match widgets::confirm(c.egui, "delete-job", &t("printing.printJobsList.deleteJob"), &text, &t("common.delete")) {
                Some(true) => {
                    let api = c.api.clone();
                    let message = tf("printing.printJobsList.jobDeleted", &[("id", &job.id.to_string())]);
                    self.delete = Some(Task::spawn(c.egui, move || api.delete_print_job(job.id).map(|_| message)));
                    self.confirm_delete = None;
                }
                Some(false) => self.confirm_delete = None,
                None => {}
            }
        }

        if self.confirm_delete_all {
            let count = jobs.iter().filter(|job| !job.in_progress()).count();
            let text = tf("printing.printJobsList.confirmDeleteAll", &[("count", &count.to_string())]);
            match widgets::confirm(c.egui, "delete-all-jobs", &t("printing.printJobsList.deleteAll"), &text, &t("common.delete")) {
                Some(true) => {
                    let api = c.api.clone();
                    self.delete_all = Some(Task::spawn(c.egui, move || api.delete_all_print_jobs()));
                    self.confirm_delete_all = false;
                }
                Some(false) => self.confirm_delete_all = false,
                None => {}
            }
        }
    }
}

pub struct JobPage {
    id: i64,
    job: Option<PrintJob>,
    load: Pending<ApiResult<PrintJob>>,
    error: Option<String>,
}

impl JobPage {
    pub fn new(id: i64) -> JobPage {
        JobPage { id, job: None, load: None, error: None }
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        // Loads once, then again every two seconds while the job is still printing.
        if self.load.is_none() && self.job.as_ref().is_none_or(PrintJob::in_progress) && self.error.is_none() {
            let (api, id) = (c.api.clone(), self.id);
            let first = self.job.is_none();
            self.load = Some(Task::spawn(c.egui, move || {
                if !first {
                    std::thread::sleep(Duration::from_secs(2));
                }
                api.print_job(id)
            }));
        }

        if let Some(result) = finished(&mut self.load) {
            match result {
                Ok(job) => self.job = Some(job),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        widgets::page_header(ui, &tf("printing.printJobDetail.title", &[("id", &self.id.to_string())]), |ui| {
            if ui.add(PillButton::new(&t("printing.printJobDetail.backToHistory"), Kind::Secondary).icon(Icon::ArrowLeft)).clicked() {
                c.go(Route::Jobs);
            }
            if ui.add(PillButton::new(&t("printing.printJobDetail.backToTemplates"), Kind::Secondary).icon(Icon::ArrowLeft)).clicked() {
                c.go(Route::Templates);
            }
        });

        if let Some(error) = &self.error {
            widgets::error_text(ui, error);
            return;
        }

        let Some(job) = self.job.clone() else {
            widgets::loading(ui, &t("printing.printJobDetail.loading"));
            return;
        };

        let palette = theme::palette(ui.ctx());
        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
            theme::card(ui).show(ui, |ui| {
                ui.spacing_mut().item_spacing.y = 10.0;
                let row = |ui: &mut egui::Ui, label: &str, add: &mut dyn FnMut(&mut egui::Ui)| {
                    ui.horizontal(|ui| {
                        ui.allocate_ui_with_layout(egui::vec2(108.0, 22.0), egui::Layout::left_to_right(egui::Align::Center), |ui| {
                            ui.set_width(108.0);
                            ui.label(egui::RichText::new(label).color(palette.muted));
                        });
                        add(ui);
                    });
                };
                let value = |text: String| {
                    move |ui: &mut egui::Ui| {
                        _ = ui.label(widgets::heading(text.clone()).size(16.0).family(crate::fonts::face(crate::fonts::HEADING_MEDIUM)))
                    }
                };

                let template = if job.template_deleted {
                    format!("{} ({})", job.template_name, t("printing.printJobsList.templateDeleted"))
                } else {
                    job.template_name.clone()
                };
                row(ui, &t("printing.printJobDetail.template"), &mut value(template));
                row(ui, &t("printing.printJobDetail.printer"), &mut value(job.printer_name.clone().unwrap_or_else(|| "—".into())));
                if let Some(by) = &job.printed_by {
                    row(ui, &t("printing.printJobDetail.printedBy"), &mut value(by.clone()));
                }
                if let Some(quality) = &job.quality {
                    row(ui, &t("printing.quality.label"), &mut value(t(&format!("printing.quality.{}", quality.to_lowercase()))));
                }
                if let Some(cut) = &job.cut_mode {
                    row(ui, &t("printing.cutMode.label"), &mut value(t(&format!("printing.cutMode.{cut}"))));
                }
                row(ui, &t("printing.printJobDetail.status"), &mut |ui| {
                    widgets::pill(ui, &widgets::status_text(&job.status), widgets::status_tone(&job.status));
                    if job.in_progress() {
                        ui.spinner();
                    }
                });
                row(ui, &t("printing.printJobDetail.created"), &mut value(widgets::date_time(&job.created_at)));
                if job.completed_at.is_some() {
                    row(ui, &t("printing.printJobDetail.completed"), &mut value(widgets::date_time(&job.completed_at)));
                }
                if let Some(error) = &job.error_message {
                    notice(ui, error, Tone::Danger);
                }
            });
            ui.add_space(18.0);

            let width = ui.available_width().min(700.0);
            for item in &job.items {
                ui.allocate_ui_with_layout(egui::vec2(width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
                    theme::card(ui).inner_margin(egui::Margin::same(18)).show(ui, |ui| {
                        ui.horizontal(|ui| {
                            ui.label(
                                egui::RichText::new(format!(
                                    "{} —",
                                    tf("printing.printJobDetail.quantity", &[("count", &item.quantity.to_string())])
                                ))
                                .size(17.0),
                            );
                            widgets::pill(ui, &widgets::status_text(&item.status), widgets::status_tone(&item.status));
                        });
                        if !item.field_values.is_empty() {
                            let values: Vec<String> = item.field_values.iter().map(|(name, value)| format!("{name}: {value}")).collect();
                            widgets::muted_small(ui, &values.join(" · "));
                        }
                        if let Some(message) = &item.error_message {
                            // A failure is red; "sent, can't confirm" is only a note.
                            let color = if item.status == "Failed" { palette.danger } else { palette.muted };
                            ui.label(egui::RichText::new(message).color(color));
                        }

                        if let Some(url) = &item.preview_url {
                            match c.images.load(c.egui, c.api, url) {
                                ImageState::Ready(texture) => {
                                    let size = texture.size_vec2();
                                    let scale = (ui.available_width() / size.x).min(56.0 / size.y).min(1.0);
                                    let (rect, _) = ui.allocate_exact_size(size * scale, egui::Sense::hover());
                                    ui.painter().add(
                                        egui::Shadow { offset: [0, 8], blur: 20, spread: 0, color: palette.shadow }
                                            .as_shape(rect, egui::CornerRadius::same(4)),
                                    );
                                    ui.painter().rect_filled(rect, egui::CornerRadius::same(4), egui::Color32::WHITE);
                                    ui.painter().image(
                                        texture.id(),
                                        rect,
                                        egui::Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0)),
                                        egui::Color32::WHITE,
                                    );
                                }
                                ImageState::Loading => {
                                    ui.spinner();
                                }
                                ImageState::Failed => {}
                            }
                        }
                    });
                });
                ui.add_space(14.0);
            }
        });
    }
}
