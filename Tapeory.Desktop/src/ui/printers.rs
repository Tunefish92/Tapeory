//! Printers: the list with tests, the default printer, and the add/edit form (including USB).

use std::collections::HashMap;

use eframe::egui::{self};

use crate::api::ApiResult;
use crate::i18n::{t, tf};
use crate::icons::Icon;
use crate::models::{ActionResult, Printer, PrinterModel, PrinterRequest, UsbPrinter};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::Ctx;
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Kind, PillButton, Tone, button, danger_button, field, primary_button, primary_button_enabled};

const CONNECTION_TYPES: [&str; 4] = ["IpAddress", "Hostname", "PrintServer", "Usb"];
const OTHER_MODEL: &str = "\u{0}other";

#[derive(Default)]
pub struct PrintersPage {
    started: bool,
    printers: Option<Vec<Printer>>,
    models: Vec<PrinterModel>,
    usb: Vec<UsbPrinter>,
    load: Pending<ApiResult<Vec<Printer>>>,
    models_task: Pending<ApiResult<Vec<PrinterModel>>>,
    usb_task: Pending<ApiResult<Vec<UsbPrinter>>>,
    /// Test results and messages per printer.
    results: HashMap<i64, (bool, String)>,
    running: HashMap<i64, Task<ApiResult<(bool, ActionResult)>>>,
    action: Pending<ApiResult<String>>,
    form: Option<PrinterForm>,
    save: Pending<ApiResult<Printer>>,
    deleting: Option<Printer>,
    error: Option<String>,
}

struct PrinterForm {
    id: Option<i64>,
    name: String,
    model_choice: String,
    custom_model: String,
    connection_type: String,
    address: String,
    port: String,
    print_server: String,
    queue: String,
    usb_identifier: String,
    width: String,
    height: String,
    enabled: bool,
    error: Option<String>,
}

impl PrinterForm {
    fn new(printer: Option<&Printer>, models: &[PrinterModel]) -> PrinterForm {
        let model = printer.and_then(|p| p.model.clone()).unwrap_or_default();
        let known = models.iter().find(|m| model.to_uppercase().contains(&m.name.to_uppercase()) && !model.is_empty());
        let print_server = printer.is_some_and(|p| p.connection_type == "PrintServer");

        PrinterForm {
            id: printer.map(|p| p.id),
            name: printer.map(|p| p.name.clone()).unwrap_or_default(),
            model_choice: match known {
                Some(known) => known.name.clone(),
                None if model.is_empty() => String::new(),
                None => OTHER_MODEL.into(),
            },
            custom_model: if known.is_none() { model.clone() } else { String::new() },
            connection_type: printer.map(|p| p.connection_type.clone()).unwrap_or_else(|| "IpAddress".into()),
            address: printer.and_then(|p| p.address.clone()).unwrap_or_default(),
            port: printer.map(|p| p.port.to_string()).unwrap_or_else(|| if print_server { "631".into() } else { "9100".into() }),
            print_server: printer.and_then(|p| p.print_server_address.clone()).unwrap_or_default(),
            queue: printer.and_then(|p| p.queue_name.clone()).unwrap_or_default(),
            usb_identifier: printer.and_then(|p| p.usb_identifier.clone()).unwrap_or_default(),
            width: printer.and_then(|p| p.label_media_width_mm).map(|v| v.to_string()).unwrap_or_default(),
            height: printer.and_then(|p| p.label_media_height_mm).map(|v| v.to_string()).unwrap_or_default(),
            enabled: printer.is_none_or(|p| p.enabled),
            error: None,
        }
    }

    fn model(&self) -> Option<String> {
        let model = if self.model_choice == OTHER_MODEL { self.custom_model.trim().to_string() } else { self.model_choice.clone() };
        (!model.is_empty()).then_some(model)
    }

    fn request(&self) -> PrinterRequest {
        let text = |value: &str| Some(value.trim().to_string()).filter(|v| !v.is_empty());
        let number = |value: &str| value.trim().replace(',', ".").parse::<f64>().ok();
        let kind = self.connection_type.as_str();

        PrinterRequest {
            name: self.name.trim().to_string(),
            model: self.model(),
            connection_type: self.connection_type.clone(),
            address: if matches!(kind, "IpAddress" | "Hostname") { text(&self.address) } else { None },
            port: if kind == "Usb" { None } else { self.port.trim().parse().ok() },
            print_server_address: if kind == "PrintServer" { text(&self.print_server) } else { None },
            usb_identifier: if kind == "Usb" { text(&self.usb_identifier) } else { None },
            queue_name: if kind == "PrintServer" { text(&self.queue) } else { None },
            label_media_width_mm: number(&self.width),
            label_media_height_mm: number(&self.height),
            enabled: self.enabled,
        }
    }
}

impl PrintersPage {
    fn reload(&mut self, c: &Ctx) {
        let api = c.api.clone();
        self.load = Some(Task::spawn(c.egui, move || api.printers()));
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            self.reload(c);
            let api = c.api.clone();
            self.models_task = Some(Task::spawn(c.egui, move || api.printer_models()));
            let api = c.api.clone();
            self.usb_task = Some(Task::spawn(c.egui, move || api.usb_printers()));
        }

        if let Some(result) = finished(&mut self.load) {
            match result {
                Ok(printers) => self.printers = Some(printers),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }
        if let Some(Ok(models)) = finished(&mut self.models_task) {
            self.models = models;
        }
        if let Some(Ok(usb)) = finished(&mut self.usb_task) {
            self.usb = usb;
        }

        let mut done = Vec::new();
        for (id, task) in &mut self.running {
            if let Some(result) = task.take() {
                done.push((*id, result));
            }
        }
        for (id, result) in done {
            self.running.remove(&id);
            let outcome = match result {
                Ok((true, result)) => match (result.is_success, result.loaded_tape_mm) {
                    (true, Some(tape)) => (true, tf("printers.connectionSucceededWithTape", &[("tape", &tape.to_string())])),
                    (true, None) => (true, t("printers.connectionSucceeded")),
                    (false, _) => (false, result.error_message.unwrap_or_else(|| t("printers.connectionFailedFallback"))),
                },
                Ok((false, result)) if result.is_success => (true, t("printers.testPrintSucceeded")),
                Ok((false, result)) => (false, result.error_message.unwrap_or_else(|| t("printers.testPrintFailedFallback"))),
                Err(error) => (false, c.message(&error)),
            };
            self.results.insert(id, outcome);
            self.reload(c);
        }

        if let Some(result) = finished(&mut self.action) {
            match result {
                Ok(message) => c.toasts.success(message),
                Err(error) => c.failed(&error),
            }
            self.reload(c);
        }

        if let Some(result) = finished(&mut self.save) {
            match result {
                Ok(_) => {
                    let updated = self.form.as_ref().is_some_and(|form| form.id.is_some());
                    c.toasts.success(t(if updated { "printers.printerUpdated" } else { "printers.printerAdded" }));
                    self.form = None;
                    self.reload(c);
                }
                Err(error) => {
                    let message = c.message(&error);
                    if let Some(form) = &mut self.form {
                        form.error = Some(message);
                    }
                }
            }
        }

        widgets::page_header(ui, &t("printers.title"), |ui| {
            if primary_button(ui, &t("printers.addPrinter")).clicked() {
                self.form = Some(PrinterForm::new(None, &self.models));
            }
        });

        if let Some(error) = &self.error {
            widgets::error_text(ui, error);
        }

        let Some(printers) = self.printers.clone() else {
            widgets::loading(ui, &t("printers.loading"));
            return;
        };

        if printers.is_empty() {
            widgets::muted(ui, &t("printers.empty"));
        }

        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
            // As many columns of at least 340 points as fit (the web's auto-fill grid).
            let gap = 18.0;
            let columns = ((ui.available_width() + gap) / (340.0 + gap)).floor().max(1.0) as usize;
            for (row, chunk) in printers.chunks(columns).enumerate() {
                widgets::columns(ui, &format!("printers-{row}"), &vec![1.0; columns], |index, ui| {
                    if let Some(printer) = chunk.get(index) {
                        self.card(ui, c, printer);
                    }
                });
                ui.add_space(gap);
            }
        });

        self.form_window(c);

        if let Some(printer) = self.deleting.clone() {
            let text = tf("printers.confirmDelete", &[("name", &printer.name)]);
            match widgets::confirm(c.egui, "delete-printer", &t("common.delete"), &text, &t("common.delete")) {
                Some(true) => {
                    let api = c.api.clone();
                    self.action = Some(Task::spawn(c.egui, move || api.delete_printer(printer.id).map(|_| t("printers.printerDeleted"))));
                    self.deleting = None;
                }
                Some(false) => self.deleting = None,
                None => {}
            }
        }
    }

    fn card(&mut self, ui: &mut egui::Ui, c: &mut Ctx, printer: &Printer) {
        let palette = theme::palette(ui.ctx());
        theme::card(ui).fill_height().show(ui, |ui| {
            ui.horizontal_top(|ui| {
                widgets::icon_badge(ui, if printer.connection_type == "Usb" { Icon::Usb } else { Icon::Printer }, false);
                ui.add_space(4.0);
                ui.vertical(|ui| {
                    ui.spacing_mut().item_spacing.y = 6.0;
                    ui.add(egui::Label::new(widgets::heading(&printer.name).size(17.0)).truncate());
                    ui.horizontal_wrapped(|ui| {
                        ui.spacing_mut().item_spacing.x = 6.0;
                        if printer.is_default {
                            widgets::chip(ui, &t("printers.default"), Tone::Info);
                        }
                        if !printer.enabled {
                            widgets::chip(ui, &t("printers.disabled"), Tone::Neutral);
                        }
                        if !printer.on_this_computer {
                            widgets::chip(
                                ui,
                                &tf("printers.onComputer", &[("name", printer.computer_name.as_deref().unwrap_or("?"))]),
                                Tone::Neutral,
                            );
                        }
                        let (key, tone) = match printer.last_connection_status.as_str() {
                            "Success" => ("printers.statusSuccess", Tone::Success),
                            "Failed" => ("printers.statusFailed", Tone::Danger),
                            _ => ("printers.statusUnknown", Tone::Neutral),
                        };
                        widgets::pill(ui, &t(key), tone);
                    });
                });
            });
            ui.add_space(8.0);

            let info = |ui: &mut egui::Ui, label: &str, value: &str| {
                ui.horizontal(|ui| {
                    ui.allocate_ui_with_layout(egui::vec2(110.0, 20.0), egui::Layout::left_to_right(egui::Align::Center), |ui| {
                        ui.set_width(110.0);
                        ui.label(egui::RichText::new(label).color(palette.muted));
                    });
                    ui.add(egui::Label::new(value).truncate());
                });
            };
            ui.spacing_mut().item_spacing.y = 4.0;
            info(ui, &t("printers.model"), &printer.model.clone().unwrap_or_else(|| t("printers.unknownModel")));
            info(ui, &t(&format!("printers.connectionType{}", printer.connection_type)), &connection_target(printer));
            ui.spacing_mut().item_spacing.y = 10.0;

            if !printer.on_this_computer {
                notice(ui, &tf("printers.elsewhereNote", &[("name", printer.computer_name.as_deref().unwrap_or("?"))]), Tone::Neutral);
            } else if let Some(error) = &printer.last_error_message {
                notice(ui, &tf("printers.lastError", &[("message", error)]), Tone::Danger);
            }

            if let Some((success, message)) = self.results.get(&printer.id) {
                notice(ui, message, if *success { Tone::Success } else { Tone::Danger });
            }

            let busy = self.running.contains_key(&printer.id);
            if busy {
                widgets::loading(ui, &t("printers.testingConnection"));
            }

            ui.separator();

            if printer.on_this_computer && c.can_administer() {
                let half = ((ui.available_width() - 10.0) / 2.0).floor();
                ui.horizontal(|ui| {
                    if ui.add(PillButton::new(&t("printers.testConnection"), Kind::Secondary).enabled(!busy).min_width(half)).clicked() {
                        let (api, id) = (c.api.clone(), printer.id);
                        self.running.insert(id, Task::spawn(c.egui, move || api.test_connection(id).map(|r| (true, r))));
                    }
                    if ui.add(PillButton::new(&t("printers.testPrint"), Kind::Secondary).enabled(!busy).min_width(half)).clicked() {
                        let (api, id) = (c.api.clone(), printer.id);
                        self.running.insert(id, Task::spawn(c.egui, move || api.test_print(id).map(|r| (false, r))));
                    }
                });
            }
            if c.can_administer() {
                ui.horizontal(|ui| {
                    if button(ui, &t("common.edit")).clicked() {
                        self.form = Some(PrinterForm::new(Some(printer), &self.models));
                    }
                    if !printer.is_default && button(ui, &t("printers.setDefault")).clicked() {
                        let (api, id) = (c.api.clone(), printer.id);
                        let message = tf("printers.defaultPrinterUpdated", &[("name", &printer.name)]);
                        self.action = Some(Task::spawn(c.egui, move || api.set_default_printer(id).map(|_| message)));
                    }
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        if danger_button(ui, &t("common.delete")).clicked() {
                            self.deleting = Some(printer.clone());
                        }
                    });
                });
            }
        });
    }

    fn form_window(&mut self, c: &mut Ctx) {
        let Some(form) = &mut self.form else { return };
        let title = if form.id.is_some() { t("printers.editPrinter") } else { t("printers.addPrinter") };
        let saving = self.save.is_some();
        let mut close = false;
        let mut submit = false;

        widgets::dialog(c.egui, "printer-form", &title, 480.0, |ui| {
            egui::ScrollArea::vertical()
                .max_height((c.egui.screen_rect().height() - 300.0).max(220.0))
                .min_scrolled_height((c.egui.screen_rect().height() - 300.0).max(220.0))
                .show(ui, |ui| {
                    widgets::section_title(ui, &t("printers.sectionPrinter"));
                    field(ui, &t("printers.name"), |ui| widgets::text_input(ui, &mut form.name, f32::INFINITY));

                    field(ui, &t("printers.model"), |ui| {
                        let shown = match form.model_choice.as_str() {
                            "" => t("printers.modelChoose"),
                            OTHER_MODEL => t("printers.modelOther"),
                            name => name.to_string(),
                        };
                        egui::ComboBox::from_id_salt("model").width(ui.available_width()).selected_text(shown).show_ui(ui, |ui| {
                            crate::ui::widgets::compact_menu(ui);
                            for (family, heading) in [("Pt", "printers.modelGroupPt"), ("Ql", "printers.modelGroupQl")] {
                                ui.label(widgets::bold(t(heading)));
                                for model in self.models.iter().filter(|m| m.family.starts_with(family)) {
                                    ui.selectable_value(&mut form.model_choice, model.name.clone(), &model.name);
                                }
                            }
                            ui.separator();
                            ui.selectable_value(&mut form.model_choice, OTHER_MODEL.to_string(), t("printers.modelOther"));
                        });
                    });
                    if form.model_choice == OTHER_MODEL {
                        field(ui, &t("printers.modelCustom"), |ui| {
                            ui.add(
                                egui::TextEdit::singleline(&mut form.custom_model)
                                    .margin(egui::Margin::symmetric(11, 9))
                                    .hint_text(crate::ui::widgets::hint(ui.ctx(), t("printers.modelPlaceholder")))
                                    .desired_width(f32::INFINITY),
                            )
                        });
                    }
                    widgets::muted_small(ui, &model_hint(form.model().as_deref(), &self.models));

                    ui.add_space(6.0);
                    widgets::section_title(ui, &t("printers.sectionConnection"));
                    field(ui, &t("printers.connectionType"), |ui| {
                        egui::ComboBox::from_id_salt("connection")
                            .width(ui.available_width())
                            .selected_text(t(&format!("printers.connectionType{}", form.connection_type)))
                            .show_ui(ui, |ui| {
                                crate::ui::widgets::compact_menu(ui);
                                for kind in CONNECTION_TYPES {
                                    if ui
                                        .selectable_value(
                                            &mut form.connection_type,
                                            kind.to_string(),
                                            t(&format!("printers.connectionType{kind}")),
                                        )
                                        .clicked()
                                    {
                                        form.port = if kind == "PrintServer" { "631".into() } else { "9100".into() };
                                    }
                                }
                            });
                    });

                    match form.connection_type.as_str() {
                        "IpAddress" | "Hostname" => {
                            let label = t(&format!("printers.connectionType{}", form.connection_type));
                            field(ui, &label, |ui| widgets::text_input(ui, &mut form.address, f32::INFINITY));
                            field(ui, &t("printers.port"), |ui| widgets::text_input(ui, &mut form.port, 120.0));
                            widgets::muted_small(ui, &t("printers.portHint"));
                        }
                        "PrintServer" => {
                            field(ui, &t("printers.printServerAddress"), |ui| {
                                widgets::text_input(ui, &mut form.print_server, f32::INFINITY)
                            });
                            field(ui, &t("printers.port"), |ui| widgets::text_input(ui, &mut form.port, 120.0));
                            widgets::muted_small(ui, &t("printers.portHintIpp"));
                            field(ui, &t("printers.queueName"), |ui| widgets::text_input(ui, &mut form.queue, f32::INFINITY));
                            widgets::muted_small(ui, &t("printers.queueNameHint"));
                            notice(ui, &t("printers.printServerNote"), Tone::Info);
                        }
                        _ => {
                            field(ui, &t("printers.usbIdentifier"), |ui| {
                                let shown = self
                                    .usb
                                    .iter()
                                    .find(|usb| usb.identifier == form.usb_identifier)
                                    .map(|usb| format!("{} ({})", usb.name, usb.identifier))
                                    .unwrap_or_else(|| {
                                        if form.usb_identifier.is_empty() { t("desktop.usbChoose") } else { form.usb_identifier.clone() }
                                    });
                                egui::ComboBox::from_id_salt("usb").width(ui.available_width()).selected_text(shown).show_ui(ui, |ui| {
                                    crate::ui::widgets::compact_menu(ui);
                                    for usb in &self.usb {
                                        ui.selectable_value(
                                            &mut form.usb_identifier,
                                            usb.identifier.clone(),
                                            format!("{} ({})", usb.name, usb.identifier),
                                        );
                                    }
                                });
                            });
                            if self.usb.is_empty() {
                                widgets::muted_small(ui, &t("desktop.usbNone"));
                            }
                            if ui.add(PillButton::new(&t("desktop.usbRefresh"), Kind::Secondary).small().icon(Icon::Refresh)).clicked() {
                                let api = c.api.clone();
                                self.usb_task = Some(Task::spawn(c.egui, move || api.usb_printers()));
                            }
                        }
                    }

                    ui.add_space(6.0);
                    widgets::section_title(ui, &t("printers.testPrintSize"));
                    ui.horizontal(|ui| {
                        field(ui, &t("printers.labelWidth"), |ui| widgets::text_input(ui, &mut form.width, 100.0));
                        field(ui, &t("printers.labelHeight"), |ui| widgets::text_input(ui, &mut form.height, 100.0));
                    });
                    widgets::muted_small(ui, &t("printers.testPrintSizeHint"));
                    ui.checkbox(&mut form.enabled, t("printers.enabled"));

                    if let Some(error) = &form.error {
                        notice(ui, error, Tone::Danger);
                    }

                    ui.horizontal(|ui| {
                        let label = if form.id.is_some() { t("printers.saveChanges") } else { t("printers.addPrinter") };
                        if primary_button_enabled(ui, !saving && !form.name.trim().is_empty(), &label).clicked() {
                            submit = true;
                        }
                        if button(ui, &t("common.cancel")).clicked() {
                            close = true;
                        }
                    });
                });
        });

        if submit {
            let (api, id, request) = (c.api.clone(), form.id, form.request());
            form.error = None;
            self.save = Some(Task::spawn(c.egui, move || api.save_printer(id, &request)));
        }
        if close {
            self.form = None;
        }
    }
}

fn connection_target(printer: &Printer) -> String {
    match printer.connection_type.as_str() {
        "PrintServer" => {
            let server = printer.print_server_address.clone().unwrap_or_default();
            match &printer.queue_name {
                Some(queue) => format!("{server}:{}/printers/{queue}", printer.port),
                None => format!("{server}:{}", printer.port),
            }
        }
        "Usb" => printer.usb_identifier.clone().unwrap_or_else(|| "—".into()),
        _ => format!("{}:{}", printer.address.clone().unwrap_or_default(), printer.port),
    }
}

fn model_hint(model: Option<&str>, models: &[PrinterModel]) -> String {
    let Some(model) = model else { return t("printers.modelHintEmpty") };
    match models.iter().find(|known| model.to_uppercase().contains(&known.name.to_uppercase())) {
        Some(known) => tf(
            if known.network { "printers.modelHintNetwork" } else { "printers.modelHintUsb" },
            &[("model", &known.name), ("dpi", &known.dpi.to_string())],
        ),
        None => t("printers.modelHintUnknown"),
    }
}
