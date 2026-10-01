//! The label editor: the same documents as the web editor, drawn natively with egui.

pub mod canvas;
pub mod document;
pub mod fit;
pub mod geometry;
pub mod media;
pub mod properties;

use eframe::egui::{self, Key};

use crate::api::{ApiError, ApiResult};
use crate::i18n::{t, tf};
use crate::models::{CreateTemplateRequest, CreateVersionRequest, TemplateDetail, UpdateTemplateRequest, UploadedImage};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Kind, PillButton, Tone, button, field};
use crate::ui::{Ctx, Route};
use canvas::{Barcodes, Canvas, Services};
use document::{LabelDocument, LabelObject};

/// Makes a new object for the "Add …" buttons.
type NewObject = fn() -> LabelObject;

const MIN_ZOOM: f32 = 0.5;
const MAX_ZOOM: f32 = 6.0;
const ZOOM_STEP: f32 = 0.25;
const HISTORY_LIMIT: usize = 200;

pub struct EditorPage {
    id: Option<i64>,
    load: Pending<ApiResult<TemplateDetail>>,
    loaded: bool,
    error: Option<String>,

    name: String,
    description: String,
    category: String,
    tags: Vec<String>,
    status: String,
    can_edit: bool,
    is_public: bool,
    owner_name: Option<String>,
    source_lbx: Option<String>,
    warnings: Vec<String>,
    warnings_dismissed: bool,

    document: LabelDocument,
    committed: LabelDocument,
    undo: Vec<LabelDocument>,
    redo: Vec<LabelDocument>,
    selected: Option<String>,
    zoom: f32,
    /// The zoom was fitted to the window once the label was there (not again, so − and + stay).
    zoom_fitted: bool,
    preview: bool,
    canvas: Canvas,
    barcodes: Barcodes,

    save: Pending<ApiResult<(i64, bool)>>,
    visibility: Pending<ApiResult<TemplateDetail>>,
    duplicate: Pending<ApiResult<i64>>,
    upload: Pending<ApiResult<UploadedImage>>,
    save_error: Option<String>,
}

impl EditorPage {
    pub fn new(id: Option<i64>) -> EditorPage {
        // New labels start on the most common QL roll: DK-22210, 29 mm continuous.
        let mut document = LabelDocument::empty(62.0, 29.0);
        document.media = Some("DK-22210".into());

        EditorPage {
            id,
            load: None,
            loaded: id.is_none(),
            error: None,
            name: String::new(),
            description: String::new(),
            category: String::new(),
            tags: Vec::new(),
            status: "Draft".into(),
            can_edit: true,
            is_public: false,
            owner_name: None,
            source_lbx: None,
            warnings: Vec::new(),
            warnings_dismissed: false,
            committed: document.clone(),
            document,
            undo: Vec::new(),
            redo: Vec::new(),
            selected: None,
            zoom: 2.0,
            zoom_fitted: false,
            preview: false,
            canvas: Canvas::default(),
            barcodes: Barcodes::default(),
            save: None,
            visibility: None,
            duplicate: None,
            upload: None,
            save_error: None,
        }
    }

    fn read_only(&self) -> bool {
        !self.can_edit
    }

    fn apply_detail(&mut self, detail: &TemplateDetail) {
        self.can_edit = detail.can_edit;
        self.is_public = detail.is_public;
        self.owner_name = detail.owner_name.clone();
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        self.poll(c);
        c.fonts.ensure_families(c.egui, c.api);

        if !self.loaded {
            match &self.error {
                Some(error) => widgets::error_text(ui, error),
                None => widgets::loading(ui, &t("editor.loading")),
            }
            return;
        }

        self.shortcuts(c);
        if ui.available_height() >= 640.0 {
            // Tall enough: the details stay at the top, the tools on the right; the canvas takes
            // the height the properties leave, so editing needs no scrolling.
            self.metadata(ui, c);
            self.banners(ui, c);
            let height = ui.available_height();
            self.workspace(ui, c, height, true);
        } else {
            // A short window: everything scrolls, so the canvas keeps a usable size.
            egui::ScrollArea::vertical().id_salt("editor-page").auto_shrink([false, false]).show(ui, |ui| {
                self.metadata(ui, c);
                self.banners(ui, c);
                self.workspace(ui, c, 640.0, false);
            });
        }
        self.commit();
    }

    fn poll(&mut self, c: &mut Ctx) {
        if let Some(id) = self.id
            && !self.loaded
            && self.load.is_none()
            && self.error.is_none()
        {
            let api = c.api.clone();
            self.load = Some(Task::spawn(c.egui, move || api.template(id)));
        }

        if let Some(result) = finished(&mut self.load) {
            match result.map_err(|e| c.message(&e)).and_then(|detail| {
                let document = LabelDocument::parse(&detail.current_version.editor_json)?;
                Ok((detail, document))
            }) {
                Ok((detail, mut document)) => {
                    // The version's size is authoritative.
                    document.width_mm = detail.current_version.width_mm;
                    document.height_mm = detail.current_version.height_mm;
                    self.name = detail.name.clone();
                    self.description = detail.description.clone().unwrap_or_default();
                    self.category = detail.category.clone().unwrap_or_default();
                    self.tags = detail.tags.clone();
                    self.status = detail.status.clone();
                    self.source_lbx = detail.source_lbx_url.clone();
                    self.warnings = detail.conversion_warnings.clone();
                    self.apply_detail(&detail);
                    self.committed = document.clone();
                    self.document = document;
                    self.loaded = true;
                }
                Err(message) => self.error = Some(message),
            }
        }

        if let Some(result) = finished(&mut self.save) {
            match result {
                Ok((id, published)) => {
                    self.id = Some(id);
                    if published {
                        self.status = "Published".into();
                    }
                    self.save_error = None;
                    c.toasts.success(t("desktop.templateSaved"));
                }
                Err(error) => self.save_error = Some(c.message(&error)),
            }
        }

        if let Some(result) = finished(&mut self.visibility) {
            match result {
                Ok(detail) => self.apply_detail(&detail),
                Err(error) => c.failed(&error),
            }
        }

        if let Some(result) = finished(&mut self.duplicate) {
            match result {
                Ok(id) => c.go(Route::Editor(Some(id))),
                Err(error) => c.failed(&error),
            }
        }

        if let Some(result) = finished(&mut self.upload) {
            match result {
                Ok(image) => {
                    let mut object = LabelObject::new_image(image.id, &image.url, 20.0, 20.0);
                    object.fit_into(self.document.width_mm, self.document.height_mm);
                    self.selected = Some(object.id.clone());
                    self.document.objects.push(object);
                }
                Err(error) if error.is_cancelled() => {}
                Err(error) => c.toasts.error(tf("desktop.imageFailed", &[("message", &error.message)])),
            }
        }
    }

    /// Records a change for undo once an edit is finished (not in the middle of a drag).
    fn commit(&mut self) {
        if self.canvas.dragging() || self.document == self.committed {
            return;
        }
        self.undo.push(std::mem::replace(&mut self.committed, self.document.clone()));
        if self.undo.len() > HISTORY_LIMIT {
            self.undo.remove(0);
        }
        self.redo.clear();
    }

    fn undo(&mut self) {
        if let Some(previous) = self.undo.pop() {
            self.redo.push(std::mem::replace(&mut self.document, previous));
            self.committed = self.document.clone();
        }
    }

    fn redo(&mut self) {
        if let Some(next) = self.redo.pop() {
            self.undo.push(std::mem::replace(&mut self.document, next));
            self.committed = self.document.clone();
        }
    }

    fn shortcuts(&mut self, c: &mut Ctx) {
        let (command, shift, keys) = c.egui.input(|input| {
            let keys: Vec<Key> = [
                Key::Delete,
                Key::Backspace,
                Key::Z,
                Key::Y,
                Key::D,
                Key::S,
                Key::Escape,
                Key::ArrowLeft,
                Key::ArrowRight,
                Key::ArrowUp,
                Key::ArrowDown,
            ]
            .into_iter()
            .filter(|key| input.key_pressed(*key))
            .collect();
            (input.modifiers.command, input.modifiers.shift, keys)
        });

        if keys.is_empty() {
            return;
        }

        if command && keys.contains(&Key::S) && !self.read_only() {
            self.start_save(c, false);
            return;
        }

        // While typing in a field, keys belong to the field.
        if c.egui.wants_keyboard_input() || self.read_only() || self.preview {
            return;
        }

        for key in keys {
            match key {
                Key::Z if command && shift => self.redo(),
                Key::Z if command => self.undo(),
                Key::Y if command => self.redo(),
                Key::D if command => {
                    if let Some(id) = self.selected.clone() {
                        self.selected = self.document.duplicate(&id);
                    }
                }
                Key::Delete | Key::Backspace => {
                    if let Some(id) = self.selected.take() {
                        self.document.remove(&id);
                    }
                }
                Key::Escape => self.selected = None,
                Key::ArrowLeft | Key::ArrowRight | Key::ArrowUp | Key::ArrowDown => {
                    let step = if shift { 5.0 } else { 0.5 };
                    if let Some(object) = self.selected.clone().and_then(|id| self.document.find_mut(&id))
                        && !object.locked
                    {
                        match key {
                            Key::ArrowLeft => object.x -= step,
                            Key::ArrowRight => object.x += step,
                            Key::ArrowUp => object.y -= step,
                            _ => object.y += step,
                        }
                    }
                }
                _ => {}
            }
        }
    }

    fn start_save(&mut self, c: &mut Ctx, publish: bool) {
        if self.name.trim().is_empty() {
            self.save_error = Some(t("editor.nameRequired"));
            return;
        }
        if self.save.is_some() {
            return;
        }

        let api = c.api.clone();
        let id = self.id;
        let document = self.document.clone();
        let (name, description, category, tags) = (
            self.name.trim().to_string(),
            Some(self.description.trim().to_string()).filter(|d| !d.is_empty()),
            Some(self.category.trim().to_string()).filter(|g| !g.is_empty()),
            self.tags.clone(),
        );

        self.save_error = None;
        self.save = Some(Task::spawn(c.egui, move || {
            let fields = document.fields();
            let json = document.to_json();
            let id = match id {
                None => {
                    let created = api.create_template(&CreateTemplateRequest {
                        name: name.clone(),
                        description: description.clone(),
                        category: category.clone(),
                        tags: Some(tags.clone()),
                        width_mm: document.width_mm,
                        height_mm: document.height_mm,
                        editor_json: json,
                        fields,
                    })?;
                    created.id
                }
                Some(id) => {
                    api.create_version(
                        id,
                        &CreateVersionRequest { width_mm: document.width_mm, height_mm: document.height_mm, editor_json: json, fields },
                    )?;
                    id
                }
            };

            // Name, group, description (and publishing) are saved separately from the design.
            api.update_template(
                id,
                &UpdateTemplateRequest { name, description, category, tags: Some(tags), status: publish.then(|| "Published".to_string()) },
            )?;

            Ok((id, publish))
        }));
    }

    fn metadata(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        let read_only = self.read_only();
        theme::card(ui).inner_margin(egui::Margin::symmetric(22, 16)).show(ui, |ui| {
            // Name, group and description share the width the other fields leave, in one row like
            // the web's; when that gets too narrow (small window, longer words), the rest moves to a
            // second row. egui can't wrap whole fields, so the rest's width from the last frame decides.
            let rest_id = ui.id().with("metadata-rest");
            let rest_width: f32 = ui.ctx().data(|data| data.get_temp(rest_id)).unwrap_or(620.0);
            let gap = ui.spacing().item_spacing.x;
            let available = ui.available_width();
            let one_row = (available - rest_width - gap * 3.0) / 3.0 >= 140.0;
            let text_width =
                if one_row { ((available - rest_width - gap * 3.0) / 3.0).min(260.0) } else { ((available - gap * 2.0) / 3.0).min(360.0) };

            let mut measured = None;
            ui.horizontal_top(|ui| {
                ui.add_enabled_ui(!read_only, |ui| {
                    field(ui, &t("editor.metadataName"), |ui| widgets::text_input(ui, &mut self.name, text_width));
                });
                ui.add_enabled_ui(!read_only, |ui| {
                    field(ui, &t("editor.metadataCategory"), |ui| {
                        ui.add(
                            egui::TextEdit::singleline(&mut self.category)
                                .margin(egui::Margin::symmetric(11, 9))
                                .hint_text(crate::ui::widgets::hint(ui.ctx(), t("templates.groupPlaceholder")))
                                .desired_width(text_width - widgets::INPUT_MARGIN),
                        )
                    });
                });
                ui.add_enabled_ui(!read_only, |ui| {
                    field(ui, &t("editor.metadataDescription"), |ui| widgets::text_input(ui, &mut self.description, text_width));
                });
                if one_row {
                    measured = Some(self.metadata_rest(ui, c, read_only));
                }
            });
            if !one_row {
                measured = Some(ui.horizontal_top(|ui| self.metadata_rest(ui, c, read_only)).inner);
            }

            if let Some(width) = measured
                && (width - rest_width).abs() > 0.5
            {
                ui.ctx().data_mut(|data| data.insert_temp(rest_id, width));
                ui.ctx().request_repaint();
            }
        });
        ui.add_space(14.0);
    }

    /// Size, visibility and the print button; returns how wide they were.
    fn metadata_rest(&mut self, ui: &mut egui::Ui, c: &mut Ctx, read_only: bool) -> f32 {
        let start = ui.cursor().left();
        ui.add_enabled_ui(!read_only, |ui| self.size_fields(ui, c));

        if c.auth.has_users && self.id.is_some() && !read_only {
            ui.vertical(|ui| {
                ui.add_space(19.0);
                let options = [t("templates.private"), t("templates.public")];
                if let Some(chosen) = widgets::segmented(ui, &options, usize::from(self.is_public)) {
                    let public = chosen == 1;
                    if self.is_public != public && self.visibility.is_none() {
                        let (api, id) = (c.api.clone(), self.id.unwrap_or_default());
                        self.visibility = Some(Task::spawn(c.egui, move || api.set_visibility(id, public)));
                    }
                }
            })
            .response
            .on_hover_text(t("editor.visibility"));
        }

        if let Some(id) = self.id {
            ui.vertical(|ui| {
                ui.add_space(22.0);
                if button(ui, &t("editor.print")).clicked() {
                    c.go(Route::Print(id));
                }
            });
        }
        ui.min_rect().right() - start
    }

    /// Height from Brother's official media, plus the length; die-cut labels fix both.
    fn size_fields(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        let unit = c.unit;
        let preset = media::find(self.document.width_mm, self.document.height_mm, self.document.media.as_deref());
        let label = |item: &media::MediaPreset| -> String {
            let code = item.code.map(|code| format!(" · {code}")).unwrap_or_default();
            if item.round {
                format!("Ø {}{code}", unit.dimension(item.height_mm))
            } else if let Some(width) = item.width_mm {
                format!("{} × {} {}{code}", unit.number(item.height_mm), unit.number(width), unit.symbol())
            } else {
                format!("{}{code}", unit.dimension(item.height_mm))
            }
        };

        field(ui, &t("editor.metadataHeight").replace("{{unit}}", unit.symbol()), |ui| {
            let shown = preset
                .as_ref()
                .map(label)
                .unwrap_or_else(|| tf("editor.mediaCustom", &[("size", &unit.dimension(self.document.height_mm))]));
            egui::ComboBox::from_id_salt("media").width(220.0).selected_text(shown).show_ui(ui, |ui| {
                crate::ui::widgets::compact_menu(ui);
                for group in media::MediaGroup::ALL {
                    ui.label(widgets::bold(t(group.key())));
                    for item in media::presets().into_iter().filter(|item| item.group == group) {
                        let selected = preset.as_ref().is_some_and(|p| p.id == item.id);
                        if ui.selectable_label(selected, label(&item)).clicked() {
                            self.document.height_mm = item.height_mm;
                            if let Some(width) = item.width_mm {
                                self.document.width_mm = width;
                            }
                            self.document.media = Some(item.id.clone());
                        }
                    }
                }
            });
        });

        let die_cut = preset.as_ref().is_some_and(|p| p.width_mm.is_some());
        field(ui, &t("editor.metadataWidth").replace("{{unit}}", unit.symbol()), |ui| {
            ui.add_enabled_ui(!die_cut, |ui| {
                widgets::length(ui, &mut self.document.width_mm, unit, 5.0);
            })
            .response
            .on_disabled_hover_text(t("editor.widthFixedByLabel"));
        });
    }

    fn banners(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if self.read_only() {
            ui.horizontal(|ui| {
                let text = match &self.owner_name {
                    Some(owner) => tf("editor.readOnlyOwned", &[("name", owner)]),
                    None => t("editor.readOnlyShared"),
                };
                notice(ui, &text, Tone::Info);
                if let Some(id) = self.id
                    && widgets::primary_button_enabled(ui, self.duplicate.is_none(), &t("editor.duplicateToEdit")).clicked()
                {
                    let api = c.api.clone();
                    let name = tf("templates.copyName", &[("name", &self.name)]);
                    self.duplicate = Some(Task::spawn(c.egui, move || api.duplicate_template(id, &name).map(|copy| copy.id)));
                }
            });
            ui.add_space(6.0);
        }

        if let Some(url) = self.source_lbx.clone() {
            ui.horizontal(|ui| {
                notice(ui, &t("editor.importedBanner"), Tone::Neutral);
                if ui.link(t("editor.downloadOriginal")).clicked() {
                    save_download(c, &url, &format!("{}.lbx", self.name));
                }
            });
        }

        if !self.warnings.is_empty() && !self.warnings_dismissed {
            theme::card(ui).show(ui, |ui| {
                ui.set_min_width(ui.available_width());
                ui.label(widgets::bold(tf("editor.warningsHeading", &[("count", &self.warnings.len().to_string())])));
                for warning in &self.warnings {
                    ui.label(format!("• {warning}"));
                }
                if ui.small_button(t("editor.dismiss")).clicked() {
                    self.warnings_dismissed = true;
                }
            });
        }
    }

    /// The canvas and properties on the left, the tools on the right.
    fn workspace(&mut self, ui: &mut egui::Ui, c: &mut Ctx, height: f32, fit: bool) {
        const SIDEBAR: f32 = 220.0;
        const GAP: f32 = 18.0;
        ui.horizontal_top(|ui| {
            ui.spacing_mut().item_spacing.x = GAP;
            let main = (ui.available_width() - SIDEBAR - GAP).max(300.0);
            ui.allocate_ui_with_layout(egui::vec2(main, height), egui::Layout::top_down(egui::Align::Min), |ui| {
                ui.set_width(main);
                if fit {
                    egui::ScrollArea::vertical().id_salt("editor-page").auto_shrink([false, false]).show(ui, |ui| self.body(ui, c, true));
                } else {
                    self.body(ui, c, false);
                }
            });
            ui.allocate_ui_with_layout(egui::vec2(SIDEBAR, height), egui::Layout::top_down(egui::Align::Min), |ui| {
                ui.set_width(SIDEBAR);
                if fit {
                    // Where notices above leave too little height for all the tools, they scroll,
                    // so Save and Publish stay within reach.
                    egui::ScrollArea::vertical().id_salt("editor-tools").max_height(height).show(ui, |ui| self.toolbar(ui, c));
                } else {
                    self.toolbar(ui, c);
                }
            });
        });
    }

    /// The tools, one below the other: add objects, undo/redo, zoom, preview, save, publish.
    fn toolbar(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        let read_only = self.read_only();
        theme::card(ui).inner_margin(egui::Margin::same(14)).show(ui, |ui| {
            let width = ui.available_width();
            let half = ((width - 8.0) / 2.0).floor();
            ui.spacing_mut().item_spacing = egui::vec2(8.0, 8.0);

            if !read_only {
                let editing = !self.preview;
                let adds: [(&str, NewObject); 6] = [
                    ("editor.toolbar.addText", LabelObject::new_text),
                    ("editor.toolbar.addField", || LabelObject::new_field("fieldName")),
                    ("editor.toolbar.addRectangle", LabelObject::new_rect),
                    ("editor.toolbar.addEllipse", LabelObject::new_ellipse),
                    ("editor.toolbar.addLine", LabelObject::new_line),
                    ("editor.toolbar.addBarcode", LabelObject::new_barcode),
                ];
                for (key, make) in adds {
                    if ui.add(PillButton::new(&t(key), Kind::Secondary).enabled(editing).min_width(width)).clicked() {
                        let mut object = make();
                        object.fit_into(self.document.width_mm, self.document.height_mm);
                        // Text starts in a font this computer has: Arial where it's installed
                        // (Windows), otherwise the bundled Inter.
                        if let Some(family) = &mut object.font_family
                            && !c.fonts.families.is_empty()
                            && !c.fonts.families.iter().any(|known| known == family)
                        {
                            *family = c.fonts.families.iter().find(|known| *known == "Inter").unwrap_or(&c.fonts.families[0]).clone();
                        }
                        self.selected = Some(object.id.clone());
                        self.document.objects.push(object);
                    }
                }
                let image_label = t("editor.toolbar.addImage");
                let image = PillButton::new(&image_label, Kind::Secondary).enabled(editing && self.upload.is_none());
                if ui.add(image.min_width(width)).clicked() {
                    let api = c.api.clone();
                    let filter = t("desktop.imagesFilter");
                    self.upload = Some(Task::spawn(c.egui, move || {
                        let formats = ["png", "jpg", "jpeg", "webp", "svg", "tif", "tiff", "bmp"];
                        let path = crate::ui::pick_file((filter, &formats)).ok_or_else(ApiError::cancelled)?;
                        api.upload_image(&path)
                    }));
                }
                ui.separator();
                // Side by side, or one below the other where the labels are too long for that
                // ("Rückgängig", "Wiederholen").
                let (undo_label, redo_label) = (t("editor.toolbar.undo"), t("editor.toolbar.redo"));
                let side_by_side = widgets::pill_width(ui, &undo_label).max(widgets::pill_width(ui, &redo_label)) <= half;
                let each = if side_by_side { half } else { width };
                ui.horizontal_wrapped(|ui| {
                    ui.spacing_mut().item_spacing = egui::vec2(8.0, 8.0);
                    let undo = PillButton::new(&undo_label, Kind::Secondary).enabled(!self.undo.is_empty());
                    if ui.add(undo.min_width(each)).clicked() {
                        self.undo();
                    }
                    let redo = PillButton::new(&redo_label, Kind::Secondary).enabled(!self.redo.is_empty());
                    if ui.add(redo.min_width(each)).clicked() {
                        self.redo();
                    }
                });
                ui.separator();
            }

            ui.horizontal(|ui| {
                if ui.add(PillButton::new("−", Kind::Secondary)).on_hover_text(t("editor.toolbar.zoomOut")).clicked() {
                    self.zoom = (self.zoom - ZOOM_STEP).max(MIN_ZOOM);
                }
                // What's left after the "+" button (as wide as "−") and the spacing.
                let middle = (ui.available_width() - 43.0 - ui.spacing().item_spacing.x).max(0.0);
                ui.allocate_ui_with_layout(
                    egui::vec2(middle, 38.0),
                    egui::Layout::centered_and_justified(egui::Direction::LeftToRight),
                    |ui| {
                        ui.label(widgets::heading(format!("{:.0}%", self.zoom * 100.0)).size(15.0).color(theme::palette(ui.ctx()).muted));
                    },
                );
                if ui.add(PillButton::new("+", Kind::Secondary)).on_hover_text(t("editor.toolbar.zoomIn")).clicked() {
                    self.zoom = (self.zoom + ZOOM_STEP).min(MAX_ZOOM);
                }
            });
            ui.separator();

            let preview_label = if self.preview { t("editor.toolbar.backToEditing") } else { t("editor.toolbar.preview") };
            if ui.add(PillButton::new(&preview_label, Kind::Secondary).selected(self.preview).min_width(width)).clicked() {
                self.preview = !self.preview;
                self.selected = None;
            }

            if !read_only {
                let saving = self.save.is_some();
                let label = if saving { t("editor.toolbar.saving") } else { t("editor.toolbar.save") };
                if ui.add(PillButton::new(&label, Kind::Primary).enabled(!saving).min_width(width)).clicked() {
                    self.start_save(c, false);
                }
                if self.id.is_some()
                    && ui.add(PillButton::new(&t("editor.toolbar.publish"), Kind::Secondary).enabled(!saving).min_width(width)).clicked()
                {
                    self.start_save(c, true);
                }
            }

            if let Some(error) = &self.save_error {
                widgets::error_text(ui, error);
            }
        });
    }

    /// The canvas across the full width, the selected object's properties below it (as on the web).
    fn body(&mut self, ui: &mut egui::Ui, c: &mut Ctx, fit: bool) {
        let read_only = self.read_only();
        let palette = theme::palette(ui.ctx());

        // The canvas is as tall as the label needs, but leaves room for the properties below
        // (their height from the last frame); a larger label scrolls inside the canvas.
        let properties_id = ui.id().with("properties-height");
        let properties: f32 =
            if self.preview || read_only { 0.0 } else { ui.ctx().data(|data| data.get_temp(properties_id)).unwrap_or(260.0) };
        // Between canvas and properties: the 18 point gap plus egui's spacing around it.
        let between = 18.0 + ui.spacing().item_spacing.y * 2.0;
        // On opening: the largest zoom (up to 200 %) at which the whole label fits, leaving room
        // for a selected object's properties.
        if !self.zoom_fitted {
            self.zoom_fitted = true;
            let space = egui::vec2(ui.available_width() - 2.0, ui.available_height() - properties.max(380.0) - between);
            self.zoom = Canvas::fitting_zoom(&self.document, space, ZOOM_STEP, MIN_ZOOM, 2.0);
        }
        let needed = Canvas::content_height(&self.document, self.zoom) + 2.0;
        let height = if fit { needed.min(ui.available_height() - properties - between).max(140.0) } else { needed.clamp(200.0, 460.0) };

        let (rect, _) = ui.allocate_exact_size(egui::vec2(ui.available_width(), height), egui::Sense::hover());
        ui.painter().rect(
            rect,
            egui::CornerRadius::same(18),
            palette.subtle,
            egui::Stroke::new(1.0_f32, palette.border),
            egui::StrokeKind::Inside,
        );
        let mut canvas_ui = ui.new_child(egui::UiBuilder::new().max_rect(rect.shrink(1.0)));
        canvas_ui.set_clip_rect(rect.shrink(1.0).intersect(ui.clip_rect()));
        egui::ScrollArea::both().id_salt("canvas").auto_shrink([false, false]).show(&mut canvas_ui, |ui| {
            let mut services = Services { ctx: c.egui, api: c.api, fonts: c.fonts, images: c.images, barcodes: &mut self.barcodes };
            self.canvas.show(ui, &mut self.document, &mut self.selected, self.zoom, self.preview, read_only, &mut services);
        });
        ui.add_space(18.0);

        if self.preview || read_only {
            return;
        }

        let card = theme::card(ui).inner_margin(egui::Margin::symmetric(22, 16)).show(ui, |ui| {
            let field_names: Vec<String> = self.document.fields().into_iter().map(|field| field.name).collect();
            let families = c.fonts.families.clone();
            let Some(id) = self.selected.clone() else {
                widgets::muted(ui, &t("editor.properties.selectPrompt"));
                return;
            };
            let mut context = properties::Context {
                ctx: c.egui,
                api: c.api,
                unit: c.unit,
                families: &families,
                field_names,
                barcodes: &mut self.barcodes,
            };
            let Some(object) = self.document.find_mut(&id) else { return };
            match properties::show(ui, object, &mut context) {
                Some(properties::Action::Reorder(direction)) => self.document.reorder(&id, direction),
                Some(properties::Action::Duplicate) => self.selected = self.document.duplicate(&id),
                Some(properties::Action::Delete) => {
                    self.document.remove(&id);
                    self.selected = None;
                }
                None => {}
            }
        });

        let measured = card.response.rect.height();
        if (measured - properties).abs() > 0.5 {
            ui.ctx().data_mut(|data| data.insert_temp(properties_id, measured));
            ui.ctx().request_repaint();
        }
    }
}

/// Saves an engine file (e.g. the original .lbx) wherever the user picks.
fn save_download(c: &mut Ctx, path: &str, suggested: &str) {
    let path = path.to_string();
    c.save_file(suggested.to_string(), move |api| api.download(&path));
}
