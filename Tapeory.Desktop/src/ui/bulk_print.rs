//! Bulk printing: one label per row of a data file (Excel, CSV, text). Four steps, as in the web
//! app: choose the file, match its columns to the label's fields, check and look through the
//! labels, print them as one job. The engine reads the file and checks the rows.

use std::collections::{BTreeMap, HashMap};
use std::path::PathBuf;
use std::rc::Rc;
use std::sync::Arc;
use std::sync::atomic::{AtomicBool, AtomicUsize, Ordering};

use eframe::egui::{self, TextureHandle};

use crate::api::{ApiError, ApiResult};
use crate::fonts::{self, face};
use crate::i18n::{t, tf};
use crate::icons::Icon;
use crate::models::{
    BulkPrintProfile, BulkPrintProfileColumn, BulkPrintProfileSettings, CheckedRow, PrintData, PrintJob, PrintJobItemRequest,
    TemplateDetail, TemplateField,
};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::print::{PrintOptions, label_preview, tape_for_label_height};
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Kind, PillButton, Tone, field};
use crate::ui::{Ctx, Route};

/// How many rows one check request takes; the progress bar moves once per chunk.
const CHECK_CHUNK: usize = 50;
/// How many rows of the file the matching step shows.
const SAMPLE_ROWS: usize = 5;
const MAX_QUANTITY: u32 = 999;
/// The most copies of each label that can be set for a whole bulk print.
const MAX_COPIES: u32 = 99;
/// The largest data file the engine reads, in megabytes.
const MAX_FILE_MB: u64 = 100;
/// The separators offered when the file's own couldn't be told, with their text keys.
const SEPARATORS: [(&str, &str); 6] =
    [(",", "comma"), (";", "semicolon"), ("tab", "tab"), ("|", "pipe"), ("space", "space"), ("none", "none")];

#[derive(Clone, Copy, PartialEq)]
enum Step {
    File,
    Match,
    Check,
    Print,
}

const STEPS: [(Step, &str); 4] = [(Step::File, "file"), (Step::Match, "match"), (Step::Check, "check"), (Step::Print, "print")];

/// The ways to import data, one tab each; more can be added here.
#[derive(Clone, Copy, PartialEq)]
enum ImportTab {
    File,
    Api,
}

const IMPORT_TABS: [(ImportTab, &str); 2] = [(ImportTab::File, "file"), (ImportTab::Api, "api")];

/// The tab a saved profile belongs to.
fn profile_tab(profile: &BulkPrintProfile) -> ImportTab {
    if profile.settings.url.is_some() { ImportTab::Api } else { ImportTab::File }
}

/// Where the data comes from: a file on this computer, or a web address the engine asks.
#[derive(Clone, Debug, PartialEq)]
enum Source {
    File(PathBuf),
    Url { url: String, header_name: String, header_value: String },
}

impl Source {
    /// What to call it on the page: the file's name, or the address.
    fn name(&self) -> String {
        match self {
            Source::File(path) => path.file_name().and_then(|name| name.to_str()).unwrap_or_default().to_string(),
            Source::Url { url, .. } => url.clone(),
        }
    }
}

/// Which column fills which field; None = no column (the field's default is printed).
type Mapping = BTreeMap<String, Option<usize>>;

/// One label to print: a row of the file, turned into field values.
#[derive(Clone, Debug, PartialEq)]
struct BulkRow {
    values: BTreeMap<String, String>,
    quantity: u32,
    /// The quantity cell isn't a whole number from 1 to 999.
    quantity_error: bool,
}

/// "A", "B", … "Z", "AA", … like a spreadsheet's columns.
fn column_letter(index: usize) -> String {
    let mut letters = String::new();
    let mut n = index as i64;
    while n >= 0 {
        letters.insert(0, (b'A' + (n % 26) as u8) as char);
        n = n / 26 - 1;
    }
    letters
}

fn field_label(field: &TemplateField) -> String {
    field.label.clone().filter(|label| !label.is_empty()).unwrap_or_else(|| field.name.clone())
}

/// A required field without a default needs a column, or no row could be printed.
fn field_needs_column(field: &TemplateField) -> bool {
    field.required && field.default_value.as_deref().unwrap_or_default().trim().is_empty()
}

fn missing_fields<'a>(fields: &'a [TemplateField], mapping: &Mapping) -> Vec<&'a TemplateField> {
    fields.iter().filter(|field| field_needs_column(field) && mapping.get(&field.name).copied().flatten().is_none()).collect()
}

/// Turns the file's rows into labels.
fn build_rows(
    data: &PrintData,
    has_header: bool,
    fields: &[TemplateField],
    mapping: &Mapping,
    quantity_column: Option<usize>,
) -> Vec<BulkRow> {
    data.rows
        .iter()
        .skip(usize::from(has_header))
        .map(|cells| {
            let values = fields
                .iter()
                .filter_map(|field| {
                    let column = mapping.get(&field.name).copied().flatten()?;
                    Some((field.name.clone(), cells.get(column).cloned().unwrap_or_default()))
                })
                .collect();

            let quantity_text = quantity_column.and_then(|column| cells.get(column)).map(|cell| cell.trim()).unwrap_or_default();
            let quantity = if quantity_text.is_empty() { Some(1) } else { quantity_text.parse::<u32>().ok() };
            let valid = quantity.filter(|quantity| (1..=MAX_QUANTITY).contains(quantity));

            BulkRow { values, quantity: valid.unwrap_or(1), quantity_error: valid.is_none() }
        })
        .collect()
}

/// The settings to save as a profile: the file, how it was read and which column fills which
/// field. The print settings are added by the caller.
fn profile_settings(
    source: &Source,
    data: &PrintData,
    has_header: bool,
    mapping: &Mapping,
    quantity_column: Option<usize>,
) -> BulkPrintProfileSettings {
    let header = if has_header { data.rows.first() } else { None };
    let header_of = |column: usize| header.and_then(|header| header.get(column)).cloned();

    let filled = |text: &String| Some(text.clone()).filter(|text| !text.is_empty());
    let (file_name, file_path, url, url_header_name, url_header_value) = match source {
        Source::File(path) => (Some(source.name()), Some(path.display().to_string()), None, None, None),
        Source::Url { url, header_name, header_value } => (None, None, Some(url.clone()), filled(header_name), filled(header_value)),
    };

    BulkPrintProfileSettings {
        file_name,
        file_path,
        url,
        url_header_name,
        url_header_value,
        separator: if data.kind == "text" { data.separator.clone() } else { None },
        sheet: (data.kind == "spreadsheet").then_some(data.sheet),
        has_header,
        columns: Some(
            mapping
                .iter()
                .filter_map(|(field, column)| {
                    column.map(|column| BulkPrintProfileColumn { field: field.clone(), column, header: header_of(column) })
                })
                .collect(),
        ),
        quantity_column,
        quantity_header: quantity_column.and_then(header_of),
        ..Default::default()
    }
}

/// A profile's matching for a file as it is now: a column is found by its header text if the
/// file has a header (so columns may have moved), otherwise by its position.
fn apply_profile(settings: &BulkPrintProfileSettings, data: &PrintData, fields: &[TemplateField]) -> (bool, Mapping, Option<usize>) {
    let has_header = settings.has_header && data.rows.len() > 1;
    let header = if has_header { data.rows.first() } else { None };
    let width = data.rows.first().map_or(0, Vec::len);
    let find = |column: Option<usize>, text: Option<&String>| {
        header
            .zip(text.filter(|text| !text.is_empty()))
            .and_then(|(header, text)| header.iter().position(|cell| cell == text))
            .or(column.filter(|column| *column < width))
    };

    let mapping = fields
        .iter()
        .map(|field| {
            let saved = settings.columns.iter().flatten().find(|column| column.field == field.name);
            (field.name.clone(), saved.and_then(|saved| find(Some(saved.column), saved.header.as_ref())))
        })
        .collect();

    (has_header, mapping, find(settings.quantity_column, settings.quantity_header.as_ref()))
}

/// Tape length for people: millimetres up to a metre, then metres.
fn format_length(mm: f64) -> String {
    if mm < 1000.0 { format!("{} mm", mm.round()) } else { format!("{:.1} m", mm / 1000.0) }
}

/// A check of all rows running in the background, chunk by chunk.
struct CheckRun {
    done: Arc<AtomicUsize>,
    cancel: Arc<AtomicBool>,
    task: Task<ApiResult<Vec<CheckedRow>>>,
}

pub struct BulkPrintPage {
    id: i64,
    started: bool,
    template: Option<Rc<TemplateDetail>>,
    template_task: Pending<ApiResult<TemplateDetail>>,
    step: Step,
    error: Option<String>,

    source: Option<Source>,
    /// Which way of importing the first step shows.
    tab: ImportTab,
    /// The web address form on the first step.
    url: String,
    url_header_name: String,
    url_header_value: String,
    pick: Pending<Option<PathBuf>>,
    /// The engine reading the file, whether the user chose the separator or sheet for it, and
    /// the profile it is read for.
    parse: Pending<(ApiResult<PrintData>, bool, bool, Option<BulkPrintProfile>)>,
    data: Option<Rc<PrintData>>,
    has_header: bool,
    mapping: Mapping,
    quantity_column: Option<usize>,
    separator_chosen: bool,
    other_separator: String,
    /// How many times each label is printed, on top of a row's own copies.
    copies: u32,

    rows: Rc<Vec<BulkRow>>,
    checked: Vec<CheckedRow>,
    ticked: Vec<bool>,
    check: Option<CheckRun>,
    current: usize,
    only_problems: bool,
    previews: HashMap<usize, TextureHandle>,
    preview_task: Pending<(usize, ApiResult<Vec<u8>>)>,
    /// Counts the checks, so label images of an earlier file aren't shown for a new one.
    generation: u64,

    options: PrintOptions,
    submit: Pending<ApiResult<PrintJob>>,

    /// Saved profiles: the list, the one in use (its name is offered when saving again), and
    /// one that is waiting for its file to be chosen.
    profiles: Vec<BulkPrintProfile>,
    profiles_task: Pending<ApiResult<Vec<BulkPrintProfile>>>,
    pending_profile: Option<BulkPrintProfile>,
    profile_name: String,
    profile_notice: Option<String>,
    save_profile: Pending<ApiResult<BulkPrintProfile>>,
    /// What is being saved, kept to save it again once the user agrees to replace a profile of
    /// the same name; `replace_profile` is set while that question is open.
    saving_profile: Option<(String, BulkPrintProfileSettings)>,
    replace_profile: bool,
    /// The profile the user is asked about deleting, and the deletion once confirmed.
    deleting: Option<BulkPrintProfile>,
    delete_profile: Pending<ApiResult<()>>,
}

impl BulkPrintPage {
    pub fn new(id: i64) -> BulkPrintPage {
        BulkPrintPage {
            id,
            started: false,
            template: None,
            template_task: None,
            step: Step::File,
            error: None,
            source: None,
            tab: ImportTab::File,
            url: String::new(),
            url_header_name: String::new(),
            url_header_value: String::new(),
            pick: None,
            parse: None,
            data: None,
            has_header: false,
            mapping: Mapping::new(),
            quantity_column: None,
            separator_chosen: false,
            other_separator: String::new(),
            copies: 1,
            rows: Rc::new(Vec::new()),
            checked: Vec::new(),
            ticked: Vec::new(),
            check: None,
            current: 0,
            only_problems: false,
            previews: HashMap::new(),
            preview_task: None,
            generation: 0,
            options: PrintOptions::default(),
            submit: None,
            profiles: Vec::new(),
            profiles_task: None,
            pending_profile: None,
            profile_name: String::new(),
            profile_notice: None,
            save_profile: None,
            saving_profile: None,
            replace_profile: false,
            deleting: None,
            delete_profile: None,
        }
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            let (api, id) = (c.api.clone(), self.id);
            self.template_task = Some(Task::spawn(c.egui, move || api.template(id)));
            self.load_profiles(c);
        }

        self.poll(c);

        let Some(template) = self.template.clone() else {
            match &self.error {
                Some(error) => widgets::error_text(ui, error),
                None => widgets::loading(ui, &t("printing.printTemplate.loading")),
            }
            return;
        };

        widgets::page_header(ui, &tf("printing.bulk.title", &[("name", &template.name)]), |_| {});
        if widgets::print_mode_switch(ui, 1, true) == Some(0) {
            c.go(Route::Print(self.id));
        }

        self.steps(ui);
        ui.add_space(14.0);

        if let Some(error) = &self.error {
            notice(ui, error, Tone::Danger);
            ui.add_space(10.0);
        }

        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| match self.step {
            Step::File => self.file_step(ui, c, &template),
            Step::Match => self.match_step(ui, c, &template),
            Step::Check => self.check_step(ui, c, &template),
            Step::Print => self.print_step(ui, c, &template),
        });
    }

    /// The numbered steps at the top; done ones are green, the current one is blue.
    fn steps(&self, ui: &mut egui::Ui) {
        let palette = theme::palette(ui.ctx());
        let position = STEPS.iter().position(|(step, _)| *step == self.step).unwrap_or(0);

        ui.horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 8.0;
            for (index, (_, key)) in STEPS.iter().enumerate() {
                let (fill, number_color, text_color) = if index == position {
                    (palette.primary, palette.on_primary, palette.text)
                } else if index < position {
                    (palette.success_bg, palette.success_text, palette.muted)
                } else {
                    (palette.elevated, palette.muted, palette.muted)
                };

                let (rect, _) = ui.allocate_exact_size(egui::vec2(26.0, 26.0), egui::Sense::hover());
                ui.painter().circle_filled(rect.center(), 13.0, fill);
                if index > position {
                    ui.painter().circle_stroke(rect.center(), 12.5, egui::Stroke::new(1.0_f32, palette.border));
                }
                ui.painter().text(
                    rect.center(),
                    egui::Align2::CENTER_CENTER,
                    (index + 1).to_string(),
                    egui::FontId::new(12.0, face(fonts::HEADING)),
                    number_color,
                );
                ui.label(
                    egui::RichText::new(t(&format!("printing.bulk.steps.{key}")))
                        .family(face(fonts::HEADING_MEDIUM))
                        .size(14.0)
                        .color(text_color),
                );
                ui.add_space(14.0);
            }
        });
    }

    fn poll(&mut self, c: &mut Ctx) {
        if let Some(result) = finished(&mut self.template_task) {
            match result {
                Ok(template) => self.template = Some(Rc::new(template)),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        self.options.poll(c);

        if let Some(Some(path)) = finished(&mut self.pick) {
            let profile = self.pending_profile.take();
            self.read_source(c, Source::File(path), None, None, profile);
        }

        if let Some(Ok(profiles)) = finished(&mut self.profiles_task) {
            self.profiles = profiles;
        }

        if let Some(result) = finished(&mut self.save_profile) {
            match result {
                Ok(profile) => {
                    self.saving_profile = None;
                    self.profile_notice = Some(tf("printing.bulk.profiles.saved", &[("name", &profile.name)]));
                    self.load_profiles(c);
                }
                // The name is taken: ask before overwriting that profile.
                Err(error) if error.status == Some(409) => self.replace_profile = true,
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        if let Some(result) = finished(&mut self.delete_profile) {
            if let Err(error) = result {
                c.failed(&error);
            }
            self.load_profiles(c);
        }

        // A file dropped onto the window, while the first step is waiting for one.
        if self.step == Step::File
            && self.parse.is_none()
            && let Some(path) = c.egui.input(|input| input.raw.dropped_files.first().and_then(|file| file.path.clone()))
        {
            self.tab = ImportTab::File;
            let profile = self.pending_profile.take();
            self.read_source(c, Source::File(path), None, None, profile);
        }

        if let Some((result, chose_separator, chose_sheet, profile)) = finished(&mut self.parse) {
            match result {
                Ok(data) => self.file_read(c, data, chose_separator, chose_sheet, profile),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        if self.check.as_mut().is_some_and(|run| run.task.ready().is_some())
            && let Some(mut run) = self.check.take()
            && let Some(result) = run.task.take()
        {
            match result {
                Ok(mut checked) => {
                    for (row, result) in self.rows.iter().zip(checked.iter_mut()) {
                        if row.quantity_error {
                            result.errors.push(t("printing.bulk.check.quantityInvalid"));
                        }
                    }
                    self.ticked = checked.iter().map(|row| row.errors.is_empty()).collect();
                    self.checked = checked;
                }
                Err(error) if error.is_cancelled() => self.step = Step::Match,
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        if let Some((index, result)) = finished(&mut self.preview_task)
            && let Ok(image) = result.map_err(|e| e.message).and_then(|bytes| crate::images::decode(&bytes))
        {
            let name = format!("bulk:{}:{}:{index}", self.id, self.generation);
            self.previews.insert(index, c.egui.load_texture(name, image, egui::TextureOptions::LINEAR));
        }

        if let Some(result) = finished(&mut self.submit) {
            match result {
                Ok(job) => c.go(Route::Job(job.id)),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }
    }

    // --- Steps 1 and 2: reading the file -----------------------------------------------------

    fn load_profiles(&mut self, c: &Ctx) {
        let (api, id) = (c.api.clone(), self.id);
        self.profiles_task = Some(Task::spawn(c.egui, move || api.bulk_print_profiles(id)));
    }

    /// Reads the data. A profile says how its file is read; a separator or sheet the user picks
    /// on this page comes first.
    fn read_source(&mut self, c: &Ctx, source: Source, separator: Option<String>, sheet: Option<usize>, profile: Option<BulkPrintProfile>) {
        if let Source::File(path) = &source
            && std::fs::metadata(path).is_ok_and(|file| file.len() > MAX_FILE_MB * 1024 * 1024)
        {
            self.error = Some(tf("printing.bulk.file.tooLarge", &[("size", &MAX_FILE_MB.to_string())]));
            return;
        }
        if profile.is_none() {
            self.profile_notice = None;
        }
        self.error = None;
        self.source = Some(source.clone());
        let (chose_separator, chose_sheet) = (separator.is_some(), sheet.is_some());
        let separator = separator.or_else(|| profile.as_ref().and_then(|profile| profile.settings.separator.clone()));
        let sheet = sheet.or_else(|| profile.as_ref().and_then(|profile| profile.settings.sheet));
        let (api, id) = (c.api.clone(), self.id);
        self.parse = Some(Task::spawn(c.egui, move || {
            let data = match &source {
                Source::File(path) => api.parse_print_data(id, path, separator.as_deref(), sheet),
                Source::Url { url, header_name, header_value } => {
                    api.fetch_print_data(id, url, header_name, header_value, separator.as_deref(), sheet)
                }
            };
            (data, chose_separator, chose_sheet, profile)
        }));
    }

    /// Loads a saved profile: its file if it is still where it was, otherwise after the user
    /// picks it.
    fn load_profile(&mut self, c: &Ctx, profile: BulkPrintProfile) {
        self.error = None;

        self.tab = profile_tab(&profile);

        // A profile for a web address needs nothing from the user.
        if let Some(url) = profile.settings.url.clone() {
            self.pending_profile = None;
            self.url = url.clone();
            self.url_header_name = profile.settings.url_header_name.clone().unwrap_or_default();
            self.url_header_value = profile.settings.url_header_value.clone().unwrap_or_default();
            let source = Source::Url { url, header_name: self.url_header_name.clone(), header_value: self.url_header_value.clone() };
            self.read_source(c, source, None, None, Some(profile));
            return;
        }

        match profile.settings.file_path.clone().map(PathBuf::from) {
            Some(path) if path.is_file() => {
                self.pending_profile = None;
                self.read_source(c, Source::File(path), None, None, Some(profile));
            }
            _ => self.pending_profile = Some(profile),
        }
    }

    fn file_read(&mut self, c: &Ctx, data: PrintData, chose_separator: bool, chose_sheet: bool, profile: Option<BulkPrintProfile>) {
        let Some(template) = self.template.clone() else { return };
        let fields = &template.current_version.fields;

        if let Some(profile) = profile {
            let settings = &profile.settings;
            let (has_header, mapping, quantity_column) = apply_profile(settings, &data, fields);
            self.has_header = has_header;
            self.mapping = mapping;
            self.quantity_column = quantity_column;
            self.separator_chosen = settings.separator.is_some();
            self.options.apply(settings.printer_id, settings.printer_name.clone(), settings.quality.clone(), settings.cut_mode.clone());
            self.profile_name = profile.name.clone();
            self.copies = settings.copies.unwrap_or(1).clamp(1, MAX_COPIES);
            self.profile_notice = None;

            // Nothing to ask while the profile still fits the file.
            let fits = missing_fields(fields, &self.mapping).is_empty() && data.rows.len() > usize::from(has_header);
            self.data = Some(Rc::new(data));
            if fits {
                self.start_check(c, &template);
            } else {
                self.step = Step::Match;
            }
            return;
        }

        self.mapping = fields.iter().map(|field| (field.name.clone(), data.fields.get(&field.name).copied())).collect();
        self.quantity_column = data.quantity_column;
        self.has_header = data.has_header;
        self.separator_chosen = chose_separator;

        // Nothing to ask: the separator is clear and the header names every field that needs a column.
        let nothing_to_ask = !chose_separator
            && !chose_sheet
            && data.separator_detected
            && data.has_header
            && missing_fields(fields, &self.mapping).is_empty();
        self.data = Some(Rc::new(data));

        if nothing_to_ask {
            self.start_check(c, &template);
        } else {
            self.step = Step::Match;
        }
    }

    /// "Save as profile": a name and a button, under the check's summary and the print settings.
    fn save_profile_block(&mut self, ui: &mut egui::Ui, c: &Ctx) {
        let (Some(data), Some(source)) = (self.data.clone(), self.source.clone()) else { return };

        ui.add_space(4.0);
        ui.separator();
        field(ui, &t("printing.bulk.profiles.saveHeading"), |ui| {
            ui.horizontal(|ui| {
                ui.add(
                    egui::TextEdit::singleline(&mut self.profile_name)
                        .char_limit(100)
                        .hint_text(widgets::hint(ui.ctx(), t("printing.bulk.profiles.name")))
                        .margin(egui::Margin::symmetric(11, 9))
                        .desired_width(220.0),
                );
                let saving = self.save_profile.is_some();
                let label = if saving { t("printing.bulk.profiles.saving") } else { t("printing.bulk.profiles.save") };
                if widgets::button_enabled(ui, !saving && !self.profile_name.trim().is_empty(), &label).clicked() {
                    let mut settings = profile_settings(&source, &data, self.has_header, &self.mapping, self.quantity_column);
                    settings.copies = Some(self.copies);
                    let print = self.options.request(self.id, Vec::new());
                    settings.printer_id = print.printer_id;
                    settings.printer_name = print.printer_name;
                    settings.quality = print.quality;
                    settings.cut_mode = print.cut_mode;

                    self.error = None;
                    self.saving_profile = Some((self.profile_name.trim().to_string(), settings));
                    self.send_profile(c, false);
                }
            });
        });

        if self.replace_profile
            && let Some((name, _)) = &self.saving_profile
        {
            let question = tf("printing.bulk.profiles.confirmReplace", &[("name", name)]);
            match widgets::confirm(
                c.egui,
                "replace-bulk-profile",
                &t("printing.bulk.profiles.saveHeading"),
                &question,
                &t("printing.bulk.profiles.replace"),
            ) {
                Some(true) => {
                    self.replace_profile = false;
                    self.send_profile(c, true);
                }
                Some(false) => {
                    self.replace_profile = false;
                    self.saving_profile = None;
                }
                None => {}
            }
        }
        widgets::muted_small(ui, self.profile_notice.as_deref().unwrap_or(&t("printing.bulk.profiles.saveHint")));
    }

    fn send_profile(&mut self, c: &Ctx, replace: bool) {
        let Some((name, settings)) = self.saving_profile.clone() else { return };
        let (api, id) = (c.api.clone(), self.id);
        self.save_profile = Some(Task::spawn(c.egui, move || api.save_bulk_print_profile(id, &name, &settings, replace)));
    }

    /// "Or get the data from a web address": the address, an optional request header, a button.
    fn url_form(&mut self, ui: &mut egui::Ui, c: &Ctx) {
        widgets::muted_small(ui, &t("printing.bulk.url.hint"));
        ui.add_space(6.0);

        let mut load = false;
        ui.horizontal(|ui| {
            let address = ui.add(
                egui::TextEdit::singleline(&mut self.url)
                    .hint_text(widgets::hint(ui.ctx(), "https://server/api/items"))
                    .margin(egui::Margin::symmetric(11, 9))
                    .desired_width((ui.available_width() - 160.0).max(160.0)),
            );
            let ready = !self.url.trim().is_empty();
            let entered = address.lost_focus() && ui.input(|input| input.key_pressed(egui::Key::Enter));
            load = (widgets::button_enabled(ui, ready, &t("printing.bulk.url.load")).clicked() || entered) && ready;
        });
        ui.add_space(6.0);
        widgets::muted_small(ui, &t("printing.bulk.url.headerToggle"));
        ui.horizontal(|ui| {
            ui.add(
                egui::TextEdit::singleline(&mut self.url_header_name)
                    .hint_text(widgets::hint(ui.ctx(), t("printing.bulk.url.headerName")))
                    .margin(egui::Margin::symmetric(11, 9))
                    .desired_width(((ui.available_width() - 60.0) / 2.0).clamp(120.0, 240.0)),
            );
            ui.add(
                egui::TextEdit::singleline(&mut self.url_header_value)
                    .hint_text(widgets::hint(ui.ctx(), t("printing.bulk.url.headerValue")))
                    .margin(egui::Margin::symmetric(11, 9))
                    .desired_width(((ui.available_width() - 60.0) / 2.0).clamp(120.0, 240.0)),
            );
        });

        if load {
            self.pending_profile = None;
            let source = Source::Url {
                url: self.url.trim().to_string(),
                header_name: self.url_header_name.trim().to_string(),
                header_value: self.url_header_value.clone(),
            };
            self.read_source(c, source, None, None, None);
        }
    }

    /// The saved profiles under the file chooser: a click loads one, the bin deletes it.
    fn profile_list(&mut self, ui: &mut egui::Ui, c: &Ctx) {
        let palette = theme::palette(ui.ctx());
        let tab = self.tab;
        if !self.profiles.iter().any(|profile| profile_tab(profile) == tab) {
            widgets::muted_small(ui, &t("printing.bulk.profiles.empty"));
        }

        let mut load = None;
        for profile in self.profiles.iter().filter(|profile| profile_tab(profile) == tab) {
            ui.horizontal(|ui| {
                let width = ui.available_width() - 44.0;
                let (rect, response) = ui.allocate_exact_size(egui::vec2(width, 44.0), egui::Sense::click());
                let hovered = response.hovered();
                ui.painter().rect(
                    rect,
                    egui::CornerRadius::same(10),
                    if hovered { palette.primary_soft } else { palette.elevated },
                    egui::Stroke::new(1.0_f32, if hovered { palette.primary } else { palette.border }),
                    egui::StrokeKind::Inside,
                );
                let name =
                    ui.painter().layout_no_wrap(profile.name.clone(), egui::FontId::new(14.5, face(fonts::BODY_MEDIUM)), palette.text);
                let name_width = name.size().x;
                ui.painter().galley(egui::pos2(rect.left() + 14.0, rect.center().y - name.size().y / 2.0), name, palette.text);
                let settings = &profile.settings;
                let file =
                    settings.url.clone().or_else(|| settings.file_path.clone()).or_else(|| settings.file_name.clone()).unwrap_or_default();
                ui.painter().with_clip_rect(rect.shrink2(egui::vec2(12.0, 0.0))).text(
                    egui::pos2(rect.left() + 14.0 + name_width + 14.0, rect.center().y),
                    egui::Align2::LEFT_CENTER,
                    file,
                    egui::FontId::proportional(12.5),
                    palette.muted,
                );
                if response
                    .on_hover_cursor(egui::CursorIcon::PointingHand)
                    .on_hover_text(tf("printing.bulk.profiles.load", &[("name", &profile.name)]))
                    .clicked()
                {
                    load = Some(profile.clone());
                }
                if widgets::icon_button(ui, Icon::Trash, &tf("printing.bulk.profiles.delete", &[("name", &profile.name)])).clicked() {
                    self.deleting = Some(profile.clone());
                }
            });
            ui.add_space(6.0);
        }

        if let Some(profile) = load {
            self.load_profile(c, profile);
        }

        if let Some(profile) = self.deleting.clone() {
            let question = tf("printing.bulk.profiles.confirmDelete", &[("name", &profile.name)]);
            match widgets::confirm(
                c.egui,
                "delete-bulk-profile",
                &question,
                &profile.settings.file_path.clone().unwrap_or_default(),
                &t("common.delete"),
            ) {
                Some(true) => {
                    self.deleting = None;
                    if self.pending_profile.as_ref().is_some_and(|pending| pending.id == profile.id) {
                        self.pending_profile = None;
                    }
                    let api = c.api.clone();
                    self.delete_profile = Some(Task::spawn(c.egui, move || api.delete_bulk_print_profile(profile.id)));
                }
                Some(false) => self.deleting = None,
                None => {}
            }
        }
    }

    fn file_step(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateDetail) {
        // The import card takes two thirds, the saved profiles one third; stacked when narrow.
        let total = ui.available_width() - 14.0;
        if total < 860.0 {
            self.import_card(ui, c, template, total);
            ui.add_space(18.0);
            self.profiles_card(ui, c, total);
        } else {
            let gap = 20.0;
            let import_width = ((total - gap) * 2.0 / 3.0).floor();
            ui.horizontal_top(|ui| {
                ui.spacing_mut().item_spacing.x = gap;
                ui.vertical(|ui| self.import_card(ui, c, template, import_width));
                ui.vertical(|ui| self.profiles_card(ui, c, total - gap - import_width));
            });
        }
    }

    /// "Choose the data": one tab per way of importing.
    fn import_card(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateDetail, width: f32) {
        ui.allocate_ui_with_layout(egui::vec2(width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
            theme::card(ui).show(ui, |ui| {
                ui.set_width(width - 46.0);
                widgets::section_title(ui, &t("printing.bulk.file.heading"));
                let fields: Vec<String> = template.current_version.fields.iter().map(field_label).collect();
                widgets::muted(ui, &tf("printing.bulk.file.intro", &[("fields", &fields.join(", "))]));
                ui.add_space(12.0);

                if self.parse.is_some() {
                    progress(ui, None, &t("printing.bulk.file.reading"));
                    return;
                }

                let labels: Vec<String> = IMPORT_TABS.iter().map(|(_, key)| t(&format!("printing.bulk.tabs.{key}"))).collect();
                let current = IMPORT_TABS.iter().position(|(tab, _)| *tab == self.tab).unwrap_or(0);
                if let Some(clicked) = widgets::tabs(ui, &labels, current) {
                    self.tab = IMPORT_TABS[clicked].0;
                }

                match self.tab {
                    ImportTab::File => self.file_panel(ui, c),
                    ImportTab::Api => self.url_form(ui, c),
                }
            });
        });
    }

    /// The saved profiles of the open tab, in their own card.
    fn profiles_card(&mut self, ui: &mut egui::Ui, c: &Ctx, width: f32) {
        ui.allocate_ui_with_layout(egui::vec2(width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
            theme::card(ui).show(ui, |ui| {
                ui.set_width(width - 46.0);
                let tab = IMPORT_TABS.iter().find(|(tab, _)| *tab == self.tab).map_or("file", |(_, key)| key);
                widgets::section_title(ui, &tf("printing.bulk.profiles.headingFor", &[("tab", &t(&format!("printing.bulk.tabs.{tab}")))]));
                widgets::muted(ui, &t("printing.bulk.profiles.intro"));
                ui.add_space(10.0);
                self.profile_list(ui, c);
            });
        });
    }

    /// The File tab: a profile waiting for its file, and the drop zone.
    fn file_panel(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        // A profile whose file is gone (or was saved in the web app) asks for it.
        if let Some(profile) = self.pending_profile.clone() {
            let settings = &profile.settings;
            let message = match &settings.file_path {
                Some(path) => tf("printing.bulk.profiles.fileMissing", &[("path", path), ("name", &profile.name)]),
                None => tf(
                    "printing.bulk.profiles.chooseFile",
                    &[("name", &profile.name), ("file", settings.file_name.as_deref().unwrap_or("?"))],
                ),
            };
            notice(ui, &message, Tone::Info);
            ui.add_space(4.0);
            if ui.add(PillButton::new(&t("printing.bulk.profiles.cancel"), Kind::Secondary).small()).clicked() {
                self.pending_profile = None;
            }
            ui.add_space(10.0);
        }

        // The drop zone: a dashed-looking box with the button inside.
        let palette = theme::palette(ui.ctx());
        // A file held over the window: the zone lights up.
        let over = ui.ctx().input(|input| !input.raw.hovered_files.is_empty());
        egui::Frame::new()
            .fill(if over { palette.primary_soft } else { palette.subtle })
            .stroke(egui::Stroke::new(1.5_f32, if over { palette.primary } else { palette.input_border }))
            .corner_radius(egui::CornerRadius::same(14))
            .inner_margin(egui::Margin::symmetric(16, 30))
            .show(ui, |ui| {
                ui.set_width(ui.available_width());
                ui.vertical_centered(|ui| {
                    ui.label(widgets::bold(t(if over { "printing.bulk.file.dropNow" } else { "printing.bulk.file.drop" })));
                    ui.add_space(8.0);
                    if widgets::primary_button_enabled(ui, self.pick.is_none(), &t("printing.bulk.file.choose")).clicked() {
                        self.pick = Some(Task::spawn(c.egui, || {
                            crate::ui::pick_file(("Excel, CSV, Text, JSON".to_string(), &["xlsx", "xls", "csv", "txt", "tsv", "json"]))
                        }));
                    }
                    ui.add_space(6.0);
                    widgets::muted_small(ui, &t("printing.bulk.file.formats"));
                });
            });
    }

    fn match_step(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &Rc<TemplateDetail>) {
        let (Some(data), Some(source)) = (self.data.clone(), self.source.clone()) else {
            self.step = Step::File;
            return;
        };
        let fields = &template.current_version.fields;
        let busy = self.parse.is_some();
        let width = data.rows.first().map_or(0, Vec::len);
        let data_rows = data.rows.len().saturating_sub(usize::from(self.has_header));
        let header = if self.has_header { data.rows.first() } else { None };
        let column_name = |index: usize| match header.and_then(|header| header.get(index)).filter(|text| !text.is_empty()) {
            Some(text) => tf("printing.bulk.match.columnWithHeader", &[("header", text), ("letter", &column_letter(index))]),
            None => tf("printing.bulk.match.column", &[("letter", &column_letter(index))]),
        };
        let ask_separator = data.kind == "text" && !data.separator_detected && !self.separator_chosen;
        let palette = theme::palette(ui.ctx());
        // What the user picks this frame; applied after the card, when nothing borrows the data.
        let mut reread: Option<(Option<String>, Option<usize>)> = None;

        theme::card(ui).show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.spacing_mut().item_spacing.y = 10.0;
            widgets::section_title(ui, &t("printing.bulk.match.heading"));
            widgets::muted(ui, &tf("printing.bulk.match.file", &[("name", &source.name()), ("count", &data_rows.to_string())]));

            if busy {
                progress(ui, None, &t("printing.bulk.file.reading"));
            }

            if data.kind == "text" {
                if ask_separator {
                    notice(
                        ui,
                        &format!("{} {}", t("printing.bulk.match.separatorQuestion"), t("printing.bulk.match.separatorHelp")),
                        Tone::Info,
                    );
                } else {
                    ui.label(widgets::bold(t("printing.bulk.match.separatorLabel")));
                }
                ui.add_enabled_ui(!busy, |ui| {
                    ui.horizontal_wrapped(|ui| {
                        ui.spacing_mut().item_spacing.x = 16.0;
                        for (separator, key) in SEPARATORS {
                            let selected = !ask_separator && data.separator.as_deref() == Some(separator);
                            if ui.radio(selected, t(&format!("printing.bulk.separator.{key}"))).clicked() && !selected {
                                reread = Some((Some(separator.to_string()), None));
                            }
                        }
                        ui.label(t("printing.bulk.separator.other"));
                        let other = ui.add(
                            egui::TextEdit::singleline(&mut self.other_separator)
                                .char_limit(1)
                                .margin(egui::Margin::symmetric(8, 6))
                                .desired_width(24.0),
                        );
                        if other.changed() && self.other_separator.chars().count() == 1 {
                            reread = Some((Some(self.other_separator.clone()), None));
                        }
                    });
                });
            }

            if data.sheets.len() > 1 {
                field(ui, &t("printing.bulk.match.sheet"), |ui| {
                    let mut sheet = data.sheet;
                    egui::ComboBox::from_id_salt("bulk-sheet")
                        .width(260.0)
                        .selected_text(data.sheets.get(sheet).cloned().unwrap_or_default())
                        .show_ui(ui, |ui| {
                            widgets::compact_menu(ui);
                            for (index, name) in data.sheets.iter().enumerate() {
                                ui.selectable_value(&mut sheet, index, name);
                            }
                        });
                    if sheet != data.sheet && !busy {
                        reread = Some((None, Some(sheet)));
                    }
                });
            }

            // The first rows of the file, so the right separator and columns are visible at once.
            widgets::muted_small(ui, &t("printing.bulk.match.sampleHeading"));
            egui::Frame::new()
                .stroke(egui::Stroke::new(1.0_f32, palette.border))
                .corner_radius(egui::CornerRadius::same(10))
                .inner_margin(egui::Margin::symmetric(12, 8))
                .show(ui, |ui| {
                    ui.set_width(ui.available_width());
                    egui::ScrollArea::horizontal().id_salt("bulk-sample").show(ui, |ui| {
                        egui::Grid::new("bulk-sample-grid").striped(true).spacing(egui::vec2(28.0, 8.0)).show(ui, |ui| {
                            for index in 0..width {
                                ui.label(
                                    egui::RichText::new(column_name(index).to_uppercase())
                                        .family(face(fonts::HEADING))
                                        .size(12.0)
                                        .color(palette.muted),
                                );
                            }
                            ui.end_row();
                            for row in data.rows.iter().skip(usize::from(self.has_header)).take(SAMPLE_ROWS) {
                                for cell in row {
                                    ui.label(cell);
                                }
                                ui.end_row();
                            }
                        });
                    });
                });

            if !ask_separator {
                let missing = missing_fields(fields, &self.mapping);
                let question = !self.has_header || !missing.is_empty();
                egui::Frame::new()
                    .fill(if question { palette.primary_soft } else { palette.elevated })
                    .stroke(egui::Stroke::new(1.0_f32, if question { palette.primary } else { palette.border }))
                    .corner_radius(egui::CornerRadius::same(10))
                    .inner_margin(egui::Margin::symmetric(14, 12))
                    .show(ui, |ui| {
                        ui.set_width(ui.available_width());
                        ui.spacing_mut().item_spacing.y = 10.0;
                        ui.label(widgets::bold(if self.has_header {
                            t("printing.bulk.match.headerFound")
                        } else {
                            t("printing.bulk.match.columnQuestion")
                        }));
                        // JSON records always have their property names as the header.
                        ui.add_enabled(
                            data.rows.len() >= 2 && data.kind != "json",
                            egui::Checkbox::new(&mut self.has_header, t("printing.bulk.match.headerSwitch")),
                        );

                        ui.horizontal_wrapped(|ui| {
                            ui.spacing_mut().item_spacing = egui::vec2(16.0, 12.0);
                            for field_def in fields {
                                let needed = field_needs_column(field_def);
                                let label = format!("{}{}", field_label(field_def), if needed { " *" } else { "" });
                                let none_text = if needed {
                                    t("printing.bulk.match.chooseColumn")
                                } else if let Some(default) = field_def.default_value.as_deref().filter(|value| !value.is_empty()) {
                                    tf("printing.bulk.match.useDefault", &[("value", default)])
                                } else {
                                    t("printing.bulk.match.leaveEmpty")
                                };
                                let column = self.mapping.entry(field_def.name.clone()).or_default();
                                ui.allocate_ui(egui::vec2(240.0, 62.0), |ui| {
                                    field(ui, &label, |ui| column_choice(ui, &field_def.name, column, width, &none_text, &column_name));
                                });
                            }
                            ui.allocate_ui(egui::vec2(240.0, 62.0), |ui| {
                                field(ui, &t("printing.bulk.match.quantity"), |ui| {
                                    column_choice(
                                        ui,
                                        "\u{0}quantity",
                                        &mut self.quantity_column,
                                        width,
                                        &t("printing.bulk.match.quantityNone"),
                                        &column_name,
                                    )
                                });
                            });
                        });

                        if !missing.is_empty() {
                            let names: Vec<String> = missing.iter().map(|field| field_label(field)).collect();
                            widgets::muted_small(ui, &tf("printing.bulk.match.missing", &[("fields", &names.join(", "))]));
                        }
                    });
            }

            ui.add_space(4.0);
            ui.horizontal(|ui| {
                if widgets::button(ui, &t("common.back")).clicked() {
                    self.step = Step::File;
                }
                let ready = !busy && !ask_separator && missing_fields(fields, &self.mapping).is_empty() && data_rows >= 1;
                if widgets::primary_button_enabled(ui, ready, &t("printing.bulk.match.continue")).clicked() {
                    self.start_check(c, template);
                }
            });
        });

        if let Some((separator, sheet)) = reread {
            self.read_source(c, source, separator, sheet, None);
        }
    }

    // --- Step 3: checking and looking through the labels -------------------------------------

    fn start_check(&mut self, c: &Ctx, template: &TemplateDetail) {
        let Some(data) = self.data.clone() else { return };
        let rows = build_rows(&data, self.has_header, &template.current_version.fields, &self.mapping, self.quantity_column);
        let values: Vec<BTreeMap<String, String>> = rows.iter().map(|row| row.values.clone()).collect();

        self.error = None;
        self.rows = Rc::new(rows);
        self.checked.clear();
        self.ticked.clear();
        self.current = 0;
        self.only_problems = false;
        self.previews.clear();
        self.preview_task = None;
        self.generation += 1;
        self.step = Step::Check;

        let done = Arc::new(AtomicUsize::new(0));
        let cancel = Arc::new(AtomicBool::new(false));
        let (api, id, ctx) = (c.api.clone(), self.id, c.egui.clone());
        let (progress, stop) = (done.clone(), cancel.clone());
        let task = Task::spawn(c.egui, move || {
            let mut checked = Vec::with_capacity(values.len());
            for chunk in values.chunks(CHECK_CHUNK) {
                if stop.load(Ordering::Relaxed) {
                    return Err(ApiError::cancelled());
                }
                checked.extend(api.check_rows(id, chunk)?);
                progress.store(checked.len(), Ordering::Relaxed);
                ctx.request_repaint();
            }
            Ok(checked)
        });
        self.check = Some(CheckRun { done, cancel, task });
    }

    fn go(&mut self, index: usize) {
        self.current = index.min(self.rows.len().saturating_sub(1));
    }

    /// Renders the label being looked at, then its neighbours, so stepping through feels instant.
    fn load_previews(&mut self, c: &Ctx) {
        if self.preview_task.is_some() || self.rows.is_empty() {
            return;
        }
        // Keeps memory bounded when someone steps through hundreds of labels.
        if self.previews.len() > 200 {
            let current = self.current;
            self.previews.retain(|index, _| index.abs_diff(current) <= 2);
        }

        let candidates = [Some(self.current), self.current.checked_add(1), self.current.checked_sub(1)];
        let wanted = candidates.into_iter().flatten().find(|index| *index < self.rows.len() && !self.previews.contains_key(index));
        if let Some(index) = wanted {
            let (api, id, values) = (c.api.clone(), self.id, self.rows[index].values.clone());
            self.preview_task = Some(Task::spawn(c.egui, move || (index, api.preview(id, &values))));
        }
    }

    fn check_step(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateDetail) {
        let rows = self.rows.clone();
        let total = rows.len();
        let checking = self.check.is_some();

        if let Some(run) = &self.check {
            let done = run.done.load(Ordering::Relaxed);
            let cancel = run.cancel.clone();
            theme::card(ui).show(ui, |ui| {
                ui.set_width(ui.available_width());
                widgets::section_title(ui, &tf("printing.bulk.check.readingRows", &[("count", &total.to_string())]));
                ui.add_space(6.0);
                progress(
                    ui,
                    Some(if total == 0 { 1.0 } else { done as f32 / total as f32 }),
                    &tf("printing.bulk.check.checking", &[("done", &done.to_string()), ("total", &total.to_string())]),
                );
                ui.add_space(6.0);
                if widgets::button(ui, &t("common.cancel")).clicked() {
                    cancel.store(true, Ordering::Relaxed);
                }
            });
            ui.add_space(16.0);
        }

        if total == 0 {
            return;
        }

        self.load_previews(c);

        // Arrow keys step through the labels, unless the user is typing somewhere.
        if c.egui.memory(|memory| memory.focused().is_none()) {
            if ui.input(|input| input.key_pressed(egui::Key::ArrowRight)) {
                self.go(self.current + 1);
            }
            if ui.input(|input| input.key_pressed(egui::Key::ArrowLeft)) {
                self.go(self.current.saturating_sub(1));
            }
        }

        let narrow = ui.available_width() < 900.0;
        if narrow {
            // The page's scroll bar sits on the right edge.
            let width = ui.available_width() - 14.0;
            self.viewer(ui, width);
            ui.add_space(16.0);
            self.row_table(ui, template, width);
        } else {
            let viewer_width = (ui.available_width() * 0.4).clamp(300.0, 520.0);
            ui.horizontal_top(|ui| {
                ui.spacing_mut().item_spacing.x = 20.0;
                ui.allocate_ui_with_layout(egui::vec2(viewer_width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
                    self.viewer(ui, viewer_width);
                });
                // The page's scroll bar sits on the right edge.
                let table_width = ui.available_width() - 14.0;
                ui.vertical(|ui| self.row_table(ui, template, table_width));
            });
        }

        if !checking && self.checked.len() == total {
            ui.add_space(16.0);
            let label_count: u32 =
                rows.iter().zip(&self.ticked).filter(|(_, ticked)| **ticked).map(|(row, _)| row.quantity * self.copies).sum();
            let errors = self.checked.iter().filter(|row| !row.errors.is_empty()).count();

            theme::card(ui).show(ui, |ui| {
                // The page's scroll bar sits on the right edge.
                ui.set_width(ui.available_width() - 14.0);
                ui.spacing_mut().item_spacing.y = 8.0;
                let summary = if label_count == 0 {
                    t("printing.bulk.check.nothingToPrint")
                } else {
                    let version = &template.current_version;
                    format!(
                        "{} · {}",
                        tf("printing.bulk.check.summary", &[("count", &label_count.to_string())]),
                        tf(
                            "printing.bulk.check.tape",
                            &[
                                ("length", &format_length(f64::from(label_count) * version.width_mm)),
                                ("width", &tape_for_label_height(version.height_mm).to_string()),
                            ],
                        )
                    )
                };
                ui.label(widgets::heading(summary).size(16.0));
                if errors > 0 {
                    widgets::muted_small(ui, &tf("printing.bulk.check.summaryErrors", &[("count", &errors.to_string())]));
                }
                ui.horizontal(|ui| {
                    ui.label(t("printing.bulk.check.copies"));
                    ui.add(egui::DragValue::new(&mut self.copies).range(1..=MAX_COPIES));
                    if self.quantity_column.is_some() {
                        widgets::muted_small(ui, &t("printing.bulk.check.copiesHint"));
                    }
                });
                ui.horizontal(|ui| {
                    if widgets::button(ui, &t("common.back")).clicked() {
                        self.step = Step::Match;
                    }
                    if widgets::primary_button_enabled(ui, label_count > 0, &t("printing.bulk.check.continue")).clicked() {
                        self.step = Step::Print;
                    }
                });
                self.save_profile_block(ui, c);
            });
        }
    }

    /// The label being looked at, with previous/next and the row's problems.
    fn viewer(&mut self, ui: &mut egui::Ui, width: f32) {
        let total = self.rows.len();
        ui.set_width(width);
        label_preview(ui, self.previews.get(&self.current), None, false, 200.0);
        ui.add_space(8.0);

        ui.horizontal(|ui| {
            if ui
                .add(PillButton::new(&t("printing.bulk.check.previous"), Kind::Secondary).enabled(self.current > 0).icon(Icon::ArrowLeft))
                .clicked()
            {
                self.go(self.current.saturating_sub(1));
            }
            ui.label(t("printing.bulk.check.label"));
            let mut number = self.current + 1;
            if ui.add(egui::DragValue::new(&mut number).range(1..=total)).changed() {
                self.go(number.saturating_sub(1));
            }
            ui.label(tf("printing.bulk.check.ofTotal", &[("total", &total.to_string())]));
            if widgets::button_enabled(ui, self.current + 1 < total, &t("printing.bulk.check.next")).clicked() {
                self.go(self.current + 1);
            }
        });

        if let Some(result) = self.checked.get(self.current) {
            for message in &result.errors {
                ui.add_space(6.0);
                notice(ui, message, Tone::Danger);
            }
            for message in &result.warnings {
                ui.add_space(6.0);
                notice(ui, message, Tone::Info);
            }
        }
    }

    /// All rows: a tick to print, the check's result, the values. Clicking a row shows its label.
    fn row_table(&mut self, ui: &mut egui::Ui, template: &TemplateDetail, width: f32) {
        let palette = theme::palette(ui.ctx());
        let fields = &template.current_version.fields;
        let rows = self.rows.clone();
        let checking = self.check.is_some();
        let problems = self.checked.iter().filter(|row| !row.errors.is_empty() || !row.warnings.is_empty()).count();
        let visible: Vec<usize> = (0..rows.len())
            .filter(|index| {
                !self.only_problems || self.checked.get(*index).is_some_and(|row| !row.errors.is_empty() || !row.warnings.is_empty())
            })
            .collect();

        // Print · Row · Check · one column per field · Copies.
        let fixed = [56.0, 52.0];
        let copies = 70.0;
        let padding = 16.0;
        let flexible = (width - fixed.iter().sum::<f32>() - copies - padding * 2.0).max(120.0);
        let check_width = flexible * 1.6 / (1.6 + fields.len() as f32);
        let field_width = flexible / (1.6 + fields.len() as f32);
        let mut widths = vec![fixed[0], fixed[1], check_width];
        widths.extend(std::iter::repeat_n(field_width, fields.len()));
        widths.push(copies);
        let mut headers =
            vec![t("printing.bulk.check.columnPrint"), t("printing.bulk.check.columnRow"), t("printing.bulk.check.columnProblem")];
        headers.extend(fields.iter().map(field_label));
        headers.push(t("printing.bulk.check.columnCopies"));

        let mut clicked = None;
        egui::Frame::new()
            .fill(palette.elevated)
            .stroke(egui::Stroke::new(1.0_f32, palette.border))
            .corner_radius(egui::CornerRadius::same(18))
            .show(ui, |ui| {
                ui.set_width(width);
                ui.spacing_mut().item_spacing.y = 0.0;

                egui::Frame::new().inner_margin(egui::Margin::symmetric(16, 10)).show(ui, |ui| {
                    ui.add_enabled(
                        problems > 0,
                        egui::Checkbox::new(
                            &mut self.only_problems,
                            tf("printing.bulk.check.onlyProblems", &[("count", &problems.to_string())]),
                        ),
                    );
                });

                let (header, _) = ui.allocate_exact_size(egui::vec2(width, 36.0), egui::Sense::hover());
                ui.painter().rect_filled(header, egui::CornerRadius::ZERO, palette.subtle);
                let mut x = header.left() + padding;
                for (label, column_width) in headers.iter().zip(&widths) {
                    let clip = egui::Rect::from_min_size(egui::pos2(x, header.top()), egui::vec2(column_width - 8.0, header.height()));
                    ui.painter().with_clip_rect(clip).text(
                        egui::pos2(x, header.center().y),
                        egui::Align2::LEFT_CENTER,
                        label.to_uppercase(),
                        egui::FontId::new(12.0, face(fonts::HEADING)),
                        palette.muted,
                    );
                    x += column_width;
                }

                let row_height = 38.0;
                egui::ScrollArea::vertical().id_salt("bulk-rows").max_height(380.0).auto_shrink([false, true]).show_rows(
                    ui,
                    row_height,
                    visible.len(),
                    |ui, range| {
                        for &index in &visible[range] {
                            let row = &rows[index];
                            let result = self.checked.get(index);
                            let (rect, response) = ui.allocate_exact_size(egui::vec2(width, row_height), egui::Sense::click());
                            if index == self.current {
                                ui.painter().rect_filled(rect, egui::CornerRadius::ZERO, palette.primary_soft);
                                ui.painter().vline(rect.left() + 1.5, rect.y_range(), egui::Stroke::new(3.0_f32, palette.primary));
                            } else if response.hovered() {
                                ui.painter().rect_filled(rect, egui::CornerRadius::ZERO, palette.subtle);
                            }
                            ui.painter().hline(rect.x_range(), rect.bottom(), egui::Stroke::new(1.0_f32, palette.border));
                            if response.clicked() {
                                clicked = Some(index);
                            }

                            let (check_text, check_color) = match result {
                                None => ("…".to_string(), palette.muted),
                                Some(result) if !result.errors.is_empty() => (result.errors.join(" "), palette.danger),
                                Some(result) if !result.warnings.is_empty() => (result.warnings.join(" "), palette.info_text),
                                Some(_) => (t("printing.bulk.check.ok"), palette.muted),
                            };
                            let mut cells = vec![String::new(), (index + 1).to_string(), check_text];
                            cells.extend(fields.iter().map(|field| {
                                row.values.get(&field.name).cloned().unwrap_or_else(|| field.default_value.clone().unwrap_or_default())
                            }));
                            cells.push((row.quantity * self.copies).to_string());

                            let mut x = rect.left() + padding;
                            for (column, (text, column_width)) in cells.iter().zip(&widths).enumerate() {
                                let cell = egui::Rect::from_min_size(egui::pos2(x, rect.top()), egui::vec2(column_width - 8.0, row_height));
                                x += column_width;
                                let mut child = ui.new_child(
                                    egui::UiBuilder::new()
                                        .max_rect(cell)
                                        .layout(egui::Layout::left_to_right(egui::Align::Center))
                                        .id_salt(("bulk-cell", index, column)),
                                );
                                child.set_clip_rect(cell.intersect(ui.clip_rect()));

                                if column == 0 {
                                    let printable = result.is_some_and(|result| result.errors.is_empty()) && !checking;
                                    let mut ticked = self.ticked.get(index).copied().unwrap_or(false);
                                    if child.add_enabled(printable, egui::Checkbox::without_text(&mut ticked)).changed()
                                        && let Some(slot) = self.ticked.get_mut(index)
                                    {
                                        *slot = ticked;
                                    }
                                } else {
                                    let color = if column == 2 { check_color } else { palette.text };
                                    let label =
                                        child.add(egui::Label::new(egui::RichText::new(text).color(color)).truncate().selectable(false));
                                    if column == 2 && result.is_some_and(|result| !result.errors.is_empty() || !result.warnings.is_empty())
                                    {
                                        label.on_hover_text(text);
                                    }
                                }
                            }
                        }
                    },
                );
            });

        if let Some(index) = clicked {
            self.go(index);
        }
    }

    // --- Step 4: printing --------------------------------------------------------------------

    fn print_step(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateDetail) {
        let items: Vec<PrintJobItemRequest> = self
            .rows
            .iter()
            .zip(&self.ticked)
            .filter(|(_, ticked)| **ticked)
            .map(|(row, _)| PrintJobItemRequest { field_values: row.values.clone(), quantity: row.quantity * self.copies })
            .collect();
        let label_count: u32 = items.iter().map(|item| item.quantity).sum();
        let count = label_count.to_string();
        let width = ui.available_width().min(420.0);

        ui.allocate_ui_with_layout(egui::vec2(width, 0.0), egui::Layout::top_down(egui::Align::Min), |ui| {
            theme::card(ui).show(ui, |ui| {
                ui.set_width(width - 46.0);
                widgets::section_title(ui, &tf("printing.bulk.print.heading", &[("count", &count)]));
                widgets::muted(ui, &t("printing.bulk.print.note"));
                ui.add_space(8.0);

                self.options.show(ui, c, template.current_version.height_mm);
                self.save_profile_block(ui, c);

                ui.add_space(8.0);
                let submitting = self.submit.is_some();
                if widgets::button_enabled(ui, !submitting, &t("common.back")).clicked() {
                    self.step = Step::Check;
                }
                let label = if submitting {
                    t("printing.printTemplate.submitting")
                } else {
                    tf("printing.bulk.print.submit", &[("count", &count)])
                };
                ui.add_space(6.0);
                if widgets::print_button(ui, !submitting && label_count > 0, &label).clicked() {
                    let request = self.options.request(self.id, items);
                    let api = c.api.clone();
                    self.error = None;
                    self.submit = Some(Task::spawn(c.egui, move || api.create_print_job(&request)));
                }
            });
        });
    }
}

/// A dropdown of the file's columns, with a first entry for "no column".
fn column_choice(
    ui: &mut egui::Ui,
    id: &str,
    column: &mut Option<usize>,
    width: usize,
    none_text: &str,
    column_name: &impl Fn(usize) -> String,
) -> egui::Response {
    let selected = column.map(column_name).unwrap_or_else(|| none_text.to_string());
    egui::ComboBox::from_id_salt(("bulk-column", id))
        .width(ui.available_width())
        .selected_text(selected)
        .show_ui(ui, |ui| {
            widgets::compact_menu(ui);
            ui.selectable_value(column, None, none_text);
            for index in 0..width {
                ui.selectable_value(column, Some(index), column_name(index));
            }
        })
        .response
}

/// A progress bar with a status line under it; without a fraction it just runs.
pub fn progress(ui: &mut egui::Ui, fraction: Option<f32>, status: &str) {
    let palette = theme::palette(ui.ctx());
    let bar = egui::ProgressBar::new(fraction.unwrap_or(1.0)).desired_height(10.0).fill(palette.primary).animate(fraction.is_none());
    ui.add(bar);
    ui.add_space(4.0);
    widgets::muted_small(ui, status);
    if fraction.is_none() {
        ui.ctx().request_repaint();
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn field_def(name: &str, default: Option<&str>, required: bool) -> TemplateField {
        TemplateField { name: name.into(), label: None, default_value: default.map(str::to_string), required }
    }

    fn data(rows: &[&[&str]]) -> PrintData {
        PrintData { rows: rows.iter().map(|row| row.iter().map(|cell| cell.to_string()).collect()).collect(), ..Default::default() }
    }

    #[test]
    fn names_columns_like_a_spreadsheet() {
        let letters: Vec<String> = [0, 1, 25, 26, 27, 701, 702].into_iter().map(column_letter).collect();
        assert_eq!(letters, ["A", "B", "Z", "AA", "AB", "ZZ", "AAA"]);
    }

    #[test]
    fn asks_only_for_required_fields_without_a_default() {
        let fields = [field_def("name", None, true), field_def("sku", Some("none"), true), field_def("note", None, false)];
        let mut mapping = Mapping::new();

        assert_eq!(missing_fields(&fields, &mapping).iter().map(|f| f.name.as_str()).collect::<Vec<_>>(), ["name"]);
        mapping.insert("name".into(), Some(0));
        assert!(missing_fields(&fields, &mapping).is_empty());
    }

    #[test]
    fn turns_rows_into_labels() {
        let fields = [field_def("name", None, true), field_def("sku", Some("none"), true), field_def("note", None, false)];
        let mapping: Mapping = [("name".to_string(), Some(0)), ("sku".to_string(), None), ("note".to_string(), Some(2))].into();

        let rows =
            build_rows(&data(&[&["Name", "Copies", "Note"], &["Box", "3", "fragile"], &["Bag", "", ""]]), true, &fields, &mapping, Some(1));

        assert_eq!(rows.len(), 2);
        assert_eq!(rows[0].values, BTreeMap::from([("name".to_string(), "Box".to_string()), ("note".to_string(), "fragile".to_string())]));
        assert_eq!((rows[0].quantity, rows[1].quantity), (3, 1));
        assert!(!rows[0].quantity_error && !rows[1].quantity_error);
    }

    #[test]
    fn flags_a_quantity_that_is_not_a_whole_number_from_1_to_999() {
        let fields = [field_def("name", None, true)];
        let mapping: Mapping = [("name".to_string(), Some(0))].into();

        let rows = build_rows(
            &data(&[&["a", "0"], &["b", "two"], &["c", "1.5"], &["d", "1000"], &["e", " 7 "]]),
            false,
            &fields,
            &mapping,
            Some(1),
        );

        assert_eq!(rows.iter().map(|row| row.quantity_error).collect::<Vec<_>>(), [true, true, true, true, false]);
        assert_eq!(rows.iter().map(|row| row.quantity).collect::<Vec<_>>(), [1, 1, 1, 1, 7]);
    }

    #[test]
    fn a_profile_keeps_the_file_and_the_matching_and_finds_moved_columns_again() {
        let fields = [field_def("name", None, true), field_def("sku", Some("none"), true), field_def("note", None, false)];
        let mapping: Mapping = [("name".to_string(), Some(0)), ("sku".to_string(), Some(1)), ("note".to_string(), None)].into();
        let mut file = data(&[&["Bezeichnung", "Nr", "Stück"], &["Box", "1", "2"]]);
        file.kind = "text".into();
        file.separator = Some(";".into());

        let settings = profile_settings(&Source::File("/home/ada/stock.csv".into()), &file, true, &mapping, Some(2));

        assert_eq!(settings.file_path.as_deref(), Some("/home/ada/stock.csv"));
        assert_eq!((settings.file_name.as_deref(), settings.separator.as_deref(), settings.sheet), (Some("stock.csv"), Some(";"), None));
        assert_eq!(
            settings.columns.clone().unwrap(),
            [
                BulkPrintProfileColumn { field: "name".into(), column: 0, header: Some("Bezeichnung".into()) },
                BulkPrintProfileColumn { field: "sku".into(), column: 1, header: Some("Nr".into()) },
            ]
        );
        assert_eq!(settings.quantity_header.as_deref(), Some("Stück"));

        let moved = apply_profile(&settings, &data(&[&["Stück", "Extra", "Bezeichnung", "Nr"], &["2", "x", "Box", "1"]]), &fields);
        assert_eq!(
            moved,
            (true, [("name".to_string(), Some(2)), ("sku".to_string(), Some(3)), ("note".to_string(), None)].into(), Some(0))
        );

        let plain = BulkPrintProfileSettings { has_header: false, ..settings };
        let by_position = apply_profile(&plain, &data(&[&["Box"]]), &fields);
        assert_eq!(
            by_position,
            (false, [("name".to_string(), Some(0)), ("sku".to_string(), None), ("note".to_string(), None)].into(), None)
        );
    }

    #[test]
    fn a_profile_for_a_web_address_keeps_the_address_and_its_header() {
        let source =
            Source::Url { url: "https://erp.example/api/items".into(), header_name: "X-Api-Key".into(), header_value: "secret".into() };
        let mut answer = data(&[&["name"], &["Box"]]);
        answer.kind = "json".into();

        let settings = profile_settings(&source, &answer, true, &Mapping::new(), None);

        assert_eq!(source.name(), "https://erp.example/api/items");
        assert_eq!((settings.file_name, settings.file_path), (None, None));
        assert_eq!(settings.url.as_deref(), Some("https://erp.example/api/items"));
        assert_eq!((settings.url_header_name.as_deref(), settings.url_header_value.as_deref()), (Some("X-Api-Key"), Some("secret")));
        assert_eq!((settings.separator, settings.sheet), (None, None));
    }

    #[test]
    fn writes_tape_lengths_for_people() {
        assert_eq!(format_length(550.0), "550 mm");
        assert_eq!(format_length(8400.0), "8.4 m");
    }
}
