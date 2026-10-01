//! Printing a template: field values, quantity, printer, quality and cutting, a live preview.

use std::collections::BTreeMap;
use std::time::{Duration, Instant};

use eframe::egui::{self, TextureHandle};

use crate::api::ApiResult;
use crate::i18n::{t, tf};
use crate::models::{CreatePrintJobRequest, PrintJob, PrintJobItemRequest, Printer, PrinterStatus, TemplateDetail};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Tone, field, primary_button_enabled};
use crate::ui::{Ctx, Route};

const PREVIEW_DELAY: Duration = Duration::from_millis(300);
const MANUAL: i64 = -1;

/// Brother TZe tape widths: the tape a label of a given height prints on (as the engine picks it).
const TAPE_WIDTHS_MM: [f64; 6] = [3.5, 6.0, 9.0, 12.0, 18.0, 24.0];

pub fn tape_for_label_height(height: f64) -> f64 {
    TAPE_WIDTHS_MM
        .into_iter()
        .fold(TAPE_WIDTHS_MM[0], |best, width| if (width - height).abs() < (best - height).abs() { width } else { best })
}

pub struct PrintPage {
    id: i64,
    started: bool,
    template: Option<TemplateDetail>,
    template_task: Pending<ApiResult<TemplateDetail>>,
    printers: Vec<Printer>,
    printers_task: Pending<ApiResult<Vec<Printer>>>,
    values: BTreeMap<String, String>,
    quantity: u32,
    printer: i64,
    printer_name: String,
    quality: String,
    cut_mode: String,
    status: Option<(i64, PrinterStatus)>,
    status_task: Pending<(i64, ApiResult<PrinterStatus>)>,
    preview: Option<TextureHandle>,
    preview_task: Pending<ApiResult<Vec<u8>>>,
    preview_due: Option<Instant>,
    preview_error: Option<String>,
    submit: Pending<ApiResult<PrintJob>>,
    error: Option<String>,
}

impl PrintPage {
    pub fn new(id: i64) -> PrintPage {
        PrintPage {
            id,
            started: false,
            template: None,
            template_task: None,
            printers: Vec::new(),
            printers_task: None,
            values: BTreeMap::new(),
            quantity: 1,
            printer: MANUAL,
            printer_name: String::new(),
            quality: "Standard".into(),
            cut_mode: "AutoCut".into(),
            status: None,
            status_task: None,
            preview: None,
            preview_task: None,
            preview_due: None,
            preview_error: None,
            submit: None,
            error: None,
        }
    }

    fn selected_printer(&self) -> Option<&Printer> {
        self.printers.iter().find(|printer| printer.id == self.printer)
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            let (api, id) = (c.api.clone(), self.id);
            self.template_task = Some(Task::spawn(c.egui, move || api.template(id)));
            let api = c.api.clone();
            self.printers_task = Some(Task::spawn(c.egui, move || api.printers()));
        }

        self.poll(c);

        let Some(template) = self.template.clone() else {
            match &self.error {
                Some(error) => widgets::error_text(ui, error),
                None => widgets::loading(ui, &t("printing.printTemplate.loading")),
            }
            return;
        };

        widgets::page_header(ui, &tf("printing.printTemplate.title", &[("name", &template.name)]), |_| {});

        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
            let narrow = ui.available_width() < 760.0;
            let form_width = if narrow { ui.available_width() } else { 340.0 };

            if narrow {
                self.form_card(ui, c, &template, form_width);
                ui.add_space(18.0);
                self.preview_card(ui);
            } else {
                ui.horizontal_top(|ui| {
                    ui.spacing_mut().item_spacing.x = 22.0;
                    self.form_card(ui, c, &template, form_width);
                    ui.vertical(|ui| self.preview_card(ui));
                });
            }
        });
    }

    fn form_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateDetail, width: f32) {
        ui.allocate_ui_with_layout(egui::vec2(width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
            theme::card(ui).show(ui, |ui| {
                ui.set_width(width - 46.0);
                self.form(ui, c, template);
            });
        });
    }

    /// The rendered label on a squared background, like a cutting mat (the web's preview).
    fn preview_card(&mut self, ui: &mut egui::Ui) {
        let palette = theme::palette(ui.ctx());
        let (rect, _) = ui.allocate_exact_size(egui::vec2(ui.available_width(), 260.0), egui::Sense::hover());
        ui.painter().rect(
            rect,
            egui::CornerRadius::same(18),
            palette.subtle,
            egui::Stroke::new(1.0_f32, palette.border),
            egui::StrokeKind::Inside,
        );
        let grid = rect.shrink(1.0);
        let step = 16.0;
        let line = egui::Stroke::new(1.0_f32, palette.border.gamma_multiply(0.8));
        let mut x = grid.left() + step;
        while x < grid.right() {
            ui.painter().vline(x, grid.y_range(), line);
            x += step;
        }
        let mut y = grid.top() + step;
        while y < grid.bottom() {
            ui.painter().hline(grid.x_range(), y, line);
            y += step;
        }

        let area = rect.shrink2(egui::vec2(48.0, 56.0));
        match &self.preview {
            Some(texture) => {
                let size = texture.size_vec2();
                let scale = (area.width() / size.x).min(area.height() / size.y).min(3.0);
                let image = egui::Rect::from_center_size(area.center(), size * scale);
                ui.painter().add(
                    egui::Shadow { offset: [0, 12], blur: 28, spread: 0, color: palette.shadow }
                        .as_shape(image, egui::CornerRadius::same(8)),
                );
                ui.painter().rect_filled(image, egui::CornerRadius::same(8), egui::Color32::WHITE);
                let mesh_rect = image.shrink(4.0);
                let mut image_ui = ui.new_child(egui::UiBuilder::new().max_rect(image));
                image_ui.put(mesh_rect, egui::Image::new((texture.id(), mesh_rect.size())).corner_radius(4));
            }
            None if self.preview_error.is_some() => {
                ui.painter().text(
                    rect.center(),
                    egui::Align2::CENTER_CENTER,
                    self.preview_error.as_deref().unwrap_or_default(),
                    egui::FontId::proportional(14.0),
                    palette.danger,
                );
            }
            None => {
                ui.painter().text(
                    rect.center(),
                    egui::Align2::CENTER_CENTER,
                    t("printing.printTemplate.rendering"),
                    egui::FontId::proportional(14.0),
                    palette.muted,
                );
            }
        }
        if self.preview_task.is_some() && self.preview.is_some() {
            ui.painter().text(
                rect.right_bottom() + egui::vec2(-16.0, -14.0),
                egui::Align2::RIGHT_BOTTOM,
                t("printing.printTemplate.rendering"),
                egui::FontId::proportional(12.5),
                palette.muted,
            );
        }
    }

    fn poll(&mut self, c: &mut Ctx) {
        if let Some(result) = finished(&mut self.template_task) {
            match result {
                Ok(template) => {
                    for field in &template.current_version.fields {
                        self.values.insert(field.name.clone(), field.default_value.clone().unwrap_or_default());
                    }
                    self.template = Some(template);
                    self.preview_due = Some(Instant::now());
                }
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        if let Some(Ok(printers)) = finished(&mut self.printers_task) {
            // The default printer, unless it's another computer's USB printer.
            if let Some(default) = printers.iter().find(|p| p.is_default && p.on_this_computer) {
                self.printer = default.id;
            }
            self.printers = printers;
            self.request_status(c);
        }

        if let Some((id, Ok(status))) = finished(&mut self.status_task) {
            self.status = Some((id, status));
        }

        // Re-render the preview once typing has paused.
        if let Some(due) = self.preview_due {
            if Instant::now() >= due && self.preview_task.is_none() {
                self.preview_due = None;
                let (api, id, values) = (c.api.clone(), self.id, self.values.clone());
                self.preview_task = Some(Task::spawn(c.egui, move || api.preview(id, &values)));
            } else {
                c.egui.request_repaint_after(PREVIEW_DELAY);
            }
        }

        if let Some(result) = finished(&mut self.preview_task) {
            match result.map_err(|e| e.message).and_then(|bytes| crate::images::decode(&bytes)) {
                Ok(image) => {
                    self.preview = Some(c.egui.load_texture(format!("preview:{}", self.id), image, egui::TextureOptions::LINEAR));
                    self.preview_error = None;
                }
                Err(message) => self.preview_error = Some(message),
            }
        }

        if let Some(result) = finished(&mut self.submit) {
            match result {
                Ok(job) => c.go(Route::Job(job.id)),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }
    }

    fn request_status(&mut self, c: &Ctx) {
        let Some(printer) = self.selected_printer() else { return };
        if !printer.on_this_computer || self.status.as_ref().is_some_and(|(id, _)| *id == printer.id) {
            return;
        }
        let (api, id) = (c.api.clone(), printer.id);
        self.status_task = Some(Task::spawn(c.egui, move || (id, api.printer_status(id))));
    }

    fn form(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateDetail) {
        let fields = &template.current_version.fields;
        if fields.is_empty() {
            widgets::muted(ui, &t("printing.printTemplate.noFields"));
        }

        for field_def in fields {
            let label = format!(
                "{}{}",
                field_def.label.clone().filter(|label| !label.is_empty()).unwrap_or_else(|| field_def.name.clone()),
                if field_def.required { " *" } else { "" }
            );
            let value = self.values.entry(field_def.name.clone()).or_default();
            if field(ui, &label, |ui| widgets::text_input(ui, value, f32::INFINITY)).changed() {
                self.preview_due = Some(Instant::now() + PREVIEW_DELAY);
            }
        }

        field(ui, &t("printing.printTemplate.quantity"), |ui| {
            ui.add_sized(egui::vec2(ui.available_width(), 38.0), egui::DragValue::new(&mut self.quantity).range(1..=999))
        });

        let previous = self.printer;
        field(ui, &t("printing.printTemplate.printer"), |ui| {
            let selected = self.selected_printer().map(printer_label).unwrap_or_else(|| t("printing.printTemplate.manualOption"));
            egui::ComboBox::from_id_salt("printer").width(ui.available_width()).selected_text(selected).show_ui(ui, |ui| {
                crate::ui::widgets::compact_menu(ui);
                for printer in &self.printers {
                    ui.add_enabled_ui(printer.on_this_computer, |ui| {
                        ui.selectable_value(&mut self.printer, printer.id, printer_label(printer));
                    });
                }
                ui.selectable_value(&mut self.printer, MANUAL, t("printing.printTemplate.manualOption"));
            });
        });
        if self.printer != previous {
            self.request_status(c);
        }

        if self.printer == MANUAL {
            field(ui, &t("printing.printTemplate.printerNameOptional"), |ui| {
                widgets::text_input(ui, &mut self.printer_name, f32::INFINITY)
            });
        }

        if let Some(printer) = self.selected_printer().cloned() {
            if !printer.resolutions.is_empty() {
                if !printer.resolutions.iter().any(|r| r.quality == self.quality) {
                    self.quality = "Standard".into();
                }
                field(ui, &t("printing.quality.label"), |ui| {
                    egui::ComboBox::from_id_salt("quality")
                        .width(ui.available_width())
                        .selected_text(quality_label(&printer, &self.quality))
                        .show_ui(ui, |ui| {
                            crate::ui::widgets::compact_menu(ui);
                            for resolution in &printer.resolutions {
                                ui.selectable_value(
                                    &mut self.quality,
                                    resolution.quality.clone(),
                                    quality_label(&printer, &resolution.quality),
                                );
                            }
                        });
                });
            }

            let cut_modes = if printer.cut_modes.is_empty() {
                vec!["AutoCut".into(), "HalfCut".into(), "CutAtEnd".into(), "ChainPrinting".into(), "CutMarks".into()]
            } else {
                printer.cut_modes.clone()
            };
            if !cut_modes.contains(&self.cut_mode) {
                self.cut_mode = cut_modes[0].clone();
            }
            field(ui, &t("printing.cutMode.label"), |ui| {
                egui::ComboBox::from_id_salt("cut")
                    .width(ui.available_width())
                    .selected_text(t(&format!("printing.cutMode.{}", self.cut_mode)))
                    .show_ui(ui, |ui| {
                        crate::ui::widgets::compact_menu(ui);
                        for mode in &cut_modes {
                            ui.selectable_value(&mut self.cut_mode, mode.clone(), t(&format!("printing.cutMode.{mode}")));
                        }
                    });
            });
            widgets::muted_small(ui, &t(&format!("printing.cutMode.{}Hint", self.cut_mode)));

            // Warn before a label goes onto the wrong tape.
            let needed = tape_for_label_height(template.current_version.height_mm);
            if let Some((id, status)) = &self.status
                && *id == printer.id
                && let Some(loaded) = status.loaded_tape_mm
                && (loaded - needed).abs() >= 0.5
            {
                notice(
                    ui,
                    &tf("printing.printTemplate.tapeMismatch", &[("loaded", &format!("{loaded}")), ("needed", &format!("{needed}"))]),
                    Tone::Danger,
                );
            }
        }

        ui.add_space(8.0);
        let submitting = self.submit.is_some();
        let label = if submitting { t("printing.printTemplate.submitting") } else { t("printing.printTemplate.submit") };
        if primary_button_enabled(ui, !submitting, &label).clicked() {
            let manual = self.printer == MANUAL;
            let request = CreatePrintJobRequest {
                template_id: self.id,
                printer_id: (!manual).then_some(self.printer),
                printer_name: if manual { Some(self.printer_name.trim().to_string()).filter(|n| !n.is_empty()) } else { None },
                items: vec![PrintJobItemRequest { field_values: self.values.clone(), quantity: self.quantity }],
                quality: (!manual).then(|| self.quality.clone()),
                cut_mode: (!manual).then(|| self.cut_mode.clone()),
            };
            let api = c.api.clone();
            self.error = None;
            self.submit = Some(Task::spawn(c.egui, move || api.create_print_job(&request)));
        }

        if let Some(error) = &self.error {
            widgets::error_text(ui, error);
        }
    }
}

fn printer_label(printer: &Printer) -> String {
    let mut label = printer.name.clone();
    if printer.is_default {
        label.push_str(&t("printing.printTemplate.optionDefaultSuffix"));
    }
    if !printer.enabled {
        label.push_str(&t("printing.printTemplate.optionDisabledSuffix"));
    }
    if !printer.on_this_computer {
        label.push_str(&tf("printing.printTemplate.optionElsewhereSuffix", &[("name", printer.computer_name.as_deref().unwrap_or("?"))]));
    }
    label
}

fn quality_label(printer: &Printer, quality: &str) -> String {
    match printer.resolutions.iter().find(|resolution| resolution.quality == quality) {
        Some(resolution) => tf(
            "printing.quality.option",
            &[
                ("name", &t(&format!("printing.quality.{}", quality.to_lowercase()))),
                ("horizontal", &resolution.horizontal_dpi.to_string()),
                ("vertical", &resolution.vertical_dpi.to_string()),
            ],
        ),
        None => quality.to_string(),
    }
}

#[cfg(test)]
mod tests {
    #[test]
    fn picks_the_closest_tape() {
        assert_eq!(super::tape_for_label_height(12.0), 12.0);
        assert_eq!(super::tape_for_label_height(25.0), 24.0);
        assert_eq!(super::tape_for_label_height(62.0), 24.0);
    }
}
