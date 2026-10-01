//! The template library: search, Mine/Public/All, groups, cards or a list, thumbnails, import,
//! duplicate, export, delete. Laid out like the web UI's templates page.

use eframe::egui::{self, RichText};

use crate::api::{ApiError, ApiResult};
use crate::i18n::{t, tf};
use crate::icons::{self, Icon};
use crate::images::ImageState;
use crate::models::{TemplateDetail, TemplateSummary};
use crate::task::{Pending, Task, finished};
use crate::theme;
use crate::ui::grid::{self, Column};
use crate::ui::widgets::{self, Kind, PillButton, Tone, button, primary_button};
use crate::ui::{Ctx, Route};

#[derive(Default, Clone, Copy, PartialEq)]
enum Ownership {
    #[default]
    All,
    Mine,
    Public,
}

#[derive(Default, Clone, Copy, PartialEq)]
enum View {
    #[default]
    Cards,
    List,
}

/// The group filter: every group, one group (by key), or the templates without one.
#[derive(Clone, PartialEq, Default)]
enum GroupFilter {
    #[default]
    All,
    Group(String),
    Ungrouped,
}

#[derive(Default)]
pub struct TemplatesPage {
    started: bool,
    templates: Option<Vec<TemplateSummary>>,
    load: Pending<ApiResult<Vec<TemplateSummary>>>,
    action: Pending<ApiResult<String>>,
    import: Pending<ApiResult<TemplateDetail>>,
    search: String,
    group: GroupFilter,
    ownership: Ownership,
    sort: grid::Sort,
    deleting: Option<TemplateSummary>,
    regrouping: Option<(TemplateSummary, String)>,
    renaming: Option<(String, String)>,
    error: Option<String>,
}

/// A group of templates as listed in the filter: its name as first written, and how many.
struct Group {
    key: String,
    name: String,
    count: usize,
}

impl TemplatesPage {
    fn reload(&mut self, c: &Ctx) {
        let api = c.api.clone();
        self.load = Some(Task::spawn(c.egui, move || api.templates()));
    }

    /// Cards or list, remembered for the session like the web UI remembers it per browser.
    fn view(ctx: &egui::Context) -> View {
        ctx.data(|data| data.get_temp(egui::Id::new("templates-view"))).unwrap_or_default()
    }

    pub fn show(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        if !self.started {
            self.started = true;
            self.reload(c);
        }

        if let Some(result) = finished(&mut self.load) {
            match result {
                Ok(templates) => self.templates = Some(templates),
                Err(error) => self.error = Some(c.message(&error)),
            }
        }

        if let Some(result) = finished(&mut self.action) {
            match result {
                Ok(message) => c.toasts.success(message),
                Err(error) => c.failed(&error),
            }
            self.reload(c);
        }

        if let Some(result) = finished(&mut self.import) {
            match result {
                Ok(template) => c.go(Route::Editor(Some(template.id))),
                Err(error) => c.failed(&error),
            }
        }

        egui::ScrollArea::vertical().auto_shrink([false, false]).show(ui, |ui| {
            self.page(ui, c);
        });

        self.dialogs(c);
    }

    fn page(&mut self, ui: &mut egui::Ui, c: &mut Ctx) {
        let view = Self::view(ui.ctx());

        widgets::page_header(ui, &t("templates.title"), |ui| {
            if primary_button(ui, &t("templates.newTemplate")).clicked() {
                c.go(Route::Editor(None));
            }
            let importing = self.import.is_some();
            let label = if importing { t("templates.importing") } else { t("templates.importLbx") };
            if widgets::button_enabled(ui, !importing, &label).clicked() {
                let api = c.api.clone();
                let filter = t("desktop.importFilter");
                self.import = Some(Task::spawn(c.egui, move || {
                    let path = crate::ui::pick_file((filter, &["lbx", "json"])).ok_or_else(ApiError::cancelled)?;
                    if path.extension().is_some_and(|ext| ext.eq_ignore_ascii_case("json")) {
                        api.import_template(std::fs::read(&path).map_err(ApiError::io)?)
                    } else {
                        api.import_lbx(&path)
                    }
                }));
            }
            let options = [t("templates.viewCards"), t("templates.viewList")];
            if let Some(chosen) = widgets::segmented(ui, &options, if view == View::Cards { 0 } else { 1 }) {
                let view = if chosen == 0 { View::Cards } else { View::List };
                ui.ctx().data_mut(|data| data.insert_temp(egui::Id::new("templates-view"), view));
            }
        });

        if let Some(error) = &self.error {
            widgets::error_text(ui, error);
        }

        let Some(templates) = self.templates.clone() else {
            widgets::loading(ui, &t("templates.loading"));
            return;
        };

        if templates.is_empty() {
            theme::card(ui).show(ui, |ui| {
                widgets::muted(ui, &t("templates.empty"));
            });
            return;
        }

        widgets::search_box(ui, &mut self.search, &t("templates.searchPlaceholder"));
        ui.add_space(4.0);

        if c.auth.has_users {
            let options = [t("templates.ownership.all"), t("templates.ownership.mine"), t("templates.ownership.public")];
            let selected = match self.ownership {
                Ownership::All => 0,
                Ownership::Mine => 1,
                Ownership::Public => 2,
            };
            if let Some(chosen) = ui.horizontal(|ui| widgets::segmented(ui, &options, selected)).inner {
                self.ownership = [Ownership::All, Ownership::Mine, Ownership::Public][chosen];
                self.group = GroupFilter::All;
            }
        }

        let owned: Vec<&TemplateSummary> = templates
            .iter()
            .filter(|template| match self.ownership {
                Ownership::All => true,
                Ownership::Mine => template.is_mine,
                Ownership::Public => template.is_public,
            })
            .collect();

        let groups = groups_of(&owned);
        let ungrouped = owned.iter().filter(|template| group_of(template).is_none()).count();

        if !groups.is_empty() {
            ui.horizontal_wrapped(|ui| {
                ui.spacing_mut().item_spacing = egui::vec2(8.0, 8.0);
                if widgets::filter_chip(ui, &t("templates.allGroups"), owned.len(), None, self.group == GroupFilter::All).clicked() {
                    self.group = GroupFilter::All;
                }
                for group in &groups {
                    let selected = self.group == GroupFilter::Group(group.key.clone());
                    let (_, _, dot) = widgets::hue_colors(ui.ctx(), widgets::group_hue(&group.name));
                    if widgets::filter_chip(ui, &group.name, group.count, Some(dot), selected).clicked() {
                        self.group = if selected { GroupFilter::All } else { GroupFilter::Group(group.key.clone()) };
                    }
                }
                if ungrouped > 0 {
                    let selected = self.group == GroupFilter::Ungrouped;
                    let palette = theme::palette(ui.ctx());
                    if widgets::filter_chip(ui, &t("templates.ungrouped"), ungrouped, Some(palette.input_border), selected).clicked() {
                        self.group = if selected { GroupFilter::All } else { GroupFilter::Ungrouped };
                    }
                }
            });
        }
        ui.add_space(14.0);

        let needle = widgets::group_key(&self.search);
        let shown: Vec<&TemplateSummary> = owned
            .into_iter()
            .filter(|template| needle.is_empty() || matches(template, &needle))
            .filter(|template| match &self.group {
                GroupFilter::All => true,
                GroupFilter::Group(key) => group_of(template).is_some_and(|group| widgets::group_key(&group) == *key),
                GroupFilter::Ungrouped => group_of(template).is_none(),
            })
            .collect();

        if shown.is_empty() {
            widgets::muted(ui, &t("templates.noMatches"));
            return;
        }

        if view == View::List {
            self.list(ui, c, &shown);
            return;
        }

        // One section per group while showing every group, as on the web.
        if self.group == GroupFilter::All && !groups.is_empty() {
            for group in &groups {
                let items: Vec<&TemplateSummary> = shown
                    .iter()
                    .copied()
                    .filter(|template| group_of(template).is_some_and(|g| widgets::group_key(&g) == group.key))
                    .collect();
                if items.is_empty() {
                    continue;
                }
                let can_rename = items.iter().any(|template| template.can_edit);
                self.section_heading(ui, Some(&group.name), items.len(), can_rename);
                self.cards(ui, c, &items);
                ui.add_space(22.0);
            }
            let items: Vec<&TemplateSummary> = shown.iter().copied().filter(|template| group_of(template).is_none()).collect();
            if !items.is_empty() {
                self.section_heading(ui, None, items.len(), false);
                self.cards(ui, c, &items);
            }
        } else {
            if let GroupFilter::Group(key) = &self.group
                && let Some(group) = groups.iter().find(|group| &group.key == key)
            {
                let can_rename = shown.iter().any(|template| template.can_edit);
                self.section_heading(ui, Some(&group.name), shown.len(), can_rename);
            }
            self.cards(ui, c, &shown);
        }
    }

    fn section_heading(&mut self, ui: &mut egui::Ui, group: Option<&str>, count: usize, can_rename: bool) {
        let palette = theme::palette(ui.ctx());
        ui.horizontal(|ui| {
            let dot = match group {
                Some(name) => widgets::hue_colors(ui.ctx(), widgets::group_hue(name)).2,
                None => palette.input_border,
            };
            let (rect, _) = ui.allocate_exact_size(egui::vec2(14.0, 14.0), egui::Sense::hover());
            ui.painter().circle_filled(rect.center(), 6.5, dot.gamma_multiply(0.25));
            ui.painter().circle_filled(rect.center(), 5.0, dot);
            ui.label(widgets::heading(group.map(str::to_string).unwrap_or_else(|| t("templates.ungrouped"))).size(20.0));
            widgets::count_bubble(ui, count);
            if let Some(name) = group
                && can_rename
                && widgets::icon_button(ui, Icon::Pencil, &t("templates.renameGroup")).clicked()
            {
                self.renaming = Some((name.to_string(), name.to_string()));
            }
        });
        ui.add_space(8.0);
    }

    /// The cards, as many columns of at least 270 points as fit (the web's auto-fill grid).
    fn cards(&mut self, ui: &mut egui::Ui, c: &mut Ctx, templates: &[&TemplateSummary]) {
        let gap = 18.0;
        let columns = ((ui.available_width() + gap) / (270.0 + gap)).floor().max(1.0) as usize;
        let card_width = ((ui.available_width() - gap * (columns - 1) as f32) / columns as f32).floor();

        for (row, chunk) in templates.chunks(columns).enumerate() {
            let id = format!("cards-{}-{row}", chunk[0].id);
            widgets::columns(ui, &id, &vec![1.0; columns], |index, ui| {
                if let Some(template) = chunk.get(index) {
                    self.card(ui, c, template, card_width);
                }
            });
            ui.add_space(gap);
        }
    }

    fn card(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateSummary, width: f32) {
        let palette = theme::palette(ui.ctx());

        theme::card(ui).inner_margin(egui::Margin::same(0)).fill_height().show(ui, |ui| {
            ui.set_width(width);
            ui.spacing_mut().item_spacing.y = 0.0;

            // The rendered label on a dotted background, the label height in the corner.
            let (rect, preview) = ui.allocate_exact_size(egui::vec2(width, 180.0), egui::Sense::click());
            ui.painter().rect_filled(rect, egui::CornerRadius { nw: 18, ne: 18, sw: 0, se: 0 }, palette.subtle);
            dots(ui, rect.shrink(6.0), palette.border);
            ui.painter().hline(rect.x_range(), rect.bottom(), egui::Stroke::new(1.0_f32, palette.border));
            thumbnail(ui, c, template, rect.shrink2(egui::vec2(20.0, 30.0)), preview.hovered());
            height_badge(ui, rect, template.height_mm);
            if preview.clicked() {
                c.go(if template.can_edit { Route::Editor(Some(template.id)) } else { Route::Print(template.id) });
            }
            let preview = preview.on_hover_cursor(egui::CursorIcon::PointingHand);
            drop(preview);

            egui::Frame::new().inner_margin(egui::Margin::symmetric(18, 16)).show(ui, |ui| {
                ui.set_width(width - 36.0);
                ui.spacing_mut().item_spacing = egui::vec2(6.0, 6.0);

                ui.horizontal(|ui| {
                    let status = t(&format!("templates.status.{}", template.status.to_lowercase()));
                    let pill_width = 30.0 + status.len() as f32 * 7.0;
                    let name = widgets::heading(&template.name).size(17.0);
                    let width = (ui.available_width() - pill_width).max(40.0);
                    ui.allocate_ui_with_layout(egui::vec2(width, 24.0), egui::Layout::left_to_right(egui::Align::Center), |ui| {
                        ui.add(egui::Label::new(name).truncate()).on_hover_text(&template.name);
                    });
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        widgets::pill(ui, &status, if template.status == "Published" { Tone::Success } else { Tone::Neutral });
                    });
                });
                ui.add_space(6.0);

                ui.spacing_mut().interact_size.y = 24.0;
                ui.horizontal_wrapped(|ui| {
                    match group_of(template) {
                        Some(group) => {
                            if widgets::group_chip(ui, &group, template.can_edit).on_hover_text(t("templates.changeGroup")).clicked() {
                                self.regrouping = Some((template.clone(), group));
                            }
                        }
                        None if template.can_edit && chip_button(ui, &format!("+ {}", t("templates.groupLabel"))).clicked() => {
                            self.regrouping = Some((template.clone(), String::new()));
                        }
                        None => {}
                    }
                    if c.auth.has_users {
                        access_chip(ui, template);
                    }
                    widgets::meta_chip(
                        ui,
                        &format!("{}×{}{}", c.unit.number(template.width_mm), c.unit.number(template.height_mm), c.unit.symbol()),
                    );
                    widgets::meta_chip(
                        ui,
                        &t(if template.source_lbx_url.is_some() { "templates.sourceImported" } else { "templates.sourceNative" }),
                    );
                });

                ui.spacing_mut().interact_size.y = 38.0;
                ui.add_space(10.0);
                ui.separator();
                ui.add_space(4.0);

                // The date on its own line: next to four actions it wouldn't fit a narrow card.
                widgets::muted_small(ui, &tf("templates.updatedOn", &[("date", &widgets::date(&template.updated_at))]));
                ui.allocate_ui_with_layout(
                    egui::vec2(ui.available_width(), 34.0),
                    egui::Layout::right_to_left(egui::Align::Center),
                    |ui| {
                        ui.spacing_mut().item_spacing.x = 4.0;
                        if ui.add(PillButton::new(&t("templates.print"), Kind::Primary).small()).clicked() {
                            c.go(Route::Print(template.id));
                        }
                        if template.can_edit && ui.add(PillButton::new(&t("common.edit"), Kind::Secondary).small()).clicked() {
                            c.go(Route::Editor(Some(template.id)));
                        }
                        self.icon_actions(ui, c, template);
                    },
                );
            });
        });
    }

    /// Export, duplicate and delete, as icons (right to left).
    fn icon_actions(&mut self, ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateSummary) {
        if widgets::icon_button(ui, Icon::Download, &t("desktop.exportTemplate")).clicked() {
            let id = template.id;
            c.save_file(format!("{}.tapeory.json", template.name), move |api| api.export_template(id));
        }
        if widgets::icon_button(ui, Icon::Copy, &t("templates.duplicate")).clicked() {
            let api = c.api.clone();
            let (id, name) = (template.id, tf("templates.copyName", &[("name", &template.name)]));
            self.action = Some(Task::spawn(c.egui, move || {
                api.duplicate_template(id, &name).map(|copy| tf("templates.duplicated", &[("name", &copy.name)]))
            }));
        }
        if template.can_edit && widgets::icon_button(ui, Icon::Trash, &t("common.delete")).clicked() {
            self.deleting = Some(template.clone());
        }
    }

    /// The list view: a sortable table, as the web's.
    fn list(&mut self, ui: &mut egui::Ui, c: &mut Ctx, templates: &[&TemplateSummary]) {
        let columns = [
            Column::new(t("templates.columnName"), 3.0),
            Column::new(t("templates.columnCategory"), 1.3).hide_below(620.0),
            // Wide enough for the longest status ("Veröffentlicht").
            Column::new(t("templates.columnStatus"), 1.5),
            Column::new(t("templates.columnSource"), 1.5).hide_below(900.0),
            Column::new(t("templates.columnSize"), 1.1).hide_below(720.0),
            Column::new(t("templates.columnUpdated"), 1.3).hide_below(800.0),
            Column::fixed(196.0),
        ];

        let mut rows: Vec<&TemplateSummary> = templates.to_vec();
        if let Some((column, ascending)) = self.sort {
            rows.sort_by(|a, b| {
                let order = match column {
                    0 => a.name.to_lowercase().cmp(&b.name.to_lowercase()),
                    1 => group_of(a).unwrap_or_default().to_lowercase().cmp(&group_of(b).unwrap_or_default().to_lowercase()),
                    2 => a.status.cmp(&b.status),
                    3 => a.source_lbx_url.is_some().cmp(&b.source_lbx_url.is_some()),
                    4 => (a.width_mm, a.height_mm).partial_cmp(&(b.width_mm, b.height_mm)).unwrap_or(std::cmp::Ordering::Equal),
                    _ => a.updated_at.cmp(&b.updated_at),
                };
                if ascending { order } else { order.reverse() }
            });
        }

        let footer = tf("templates.gridCount", &[("count", &rows.len().to_string())]);
        let palette = theme::palette(ui.ctx());
        let mut sort = self.sort;
        let clicked = grid::show(ui, &columns, &mut sort, rows.len(), 68.0, Some(&footer), |ui, row, column| {
            let template = rows[row];
            match column {
                0 => {
                    let (rect, _) = ui.allocate_exact_size(egui::vec2(64.0, 40.0), egui::Sense::hover());
                    ui.painter().rect_filled(rect, egui::CornerRadius::same(6), palette.subtle);
                    thumbnail(ui, c, template, rect.shrink(3.0), false);
                    ui.vertical(|ui| {
                        ui.spacing_mut().item_spacing.y = 1.0;
                        ui.add_space(8.0);
                        grid::title(ui, &template.name);
                        let subtitle =
                            template.description.clone().filter(|text| !text.trim().is_empty()).unwrap_or_else(|| {
                                tf("templates.versionShort", &[("version", &template.current_version_number.to_string())])
                            });
                        ui.add(egui::Label::new(RichText::new(subtitle).size(12.5).color(palette.muted)).truncate());
                    });
                }
                1 => match group_of(template) {
                    Some(group) => {
                        widgets::group_chip(ui, &group, false);
                    }
                    None => {
                        grid::muted(ui, &t("templates.ungrouped"));
                    }
                },
                2 => {
                    let status = t(&format!("templates.status.{}", template.status.to_lowercase()));
                    widgets::pill(ui, &status, if template.status == "Published" { Tone::Success } else { Tone::Neutral });
                }
                3 => {
                    let imported = template.source_lbx_url.is_some();
                    icons::icon(ui, if imported { Icon::ImportedFile } else { Icon::Tag }, 16.0, palette.muted);
                    grid::muted(ui, &t(if imported { "templates.sourceImported" } else { "templates.sourceNative" }));
                }
                4 => {
                    ui.label(format!("{}×{}{}", c.unit.number(template.width_mm), c.unit.number(template.height_mm), c.unit.symbol()));
                }
                5 => {
                    grid::muted(ui, &widgets::relative(&template.updated_at)).on_hover_text(widgets::date_time(&template.updated_at));
                }
                _ => {
                    ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                        ui.spacing_mut().item_spacing.x = 2.0;
                        self.icon_actions(ui, c, template);
                        if widgets::icon_button(ui, Icon::Printer, &t("templates.print")).clicked() {
                            c.go(Route::Print(template.id));
                        }
                        if template.can_edit && widgets::icon_button(ui, Icon::Pencil, &t("common.edit")).clicked() {
                            c.go(Route::Editor(Some(template.id)));
                        }
                    });
                }
            }
        });

        self.sort = sort;

        if let Some(row) = clicked {
            let template = rows[row];
            c.go(if template.can_edit { Route::Editor(Some(template.id)) } else { Route::Print(template.id) });
        }
    }

    fn dialogs(&mut self, c: &mut Ctx) {
        if let Some(template) = self.deleting.clone() {
            let text = tf("templates.confirmDelete", &[("name", &template.name)]);
            match widgets::confirm(c.egui, "delete-template", &t("common.delete"), &text, &t("common.delete")) {
                Some(true) => {
                    let api = c.api.clone();
                    let message = tf("templates.deleted", &[("name", &template.name)]);
                    self.action = Some(Task::spawn(c.egui, move || api.delete_template(template.id).map(|_| message)));
                    self.deleting = None;
                }
                Some(false) => self.deleting = None,
                None => {}
            }
        }

        if let Some((from, to)) = &mut self.renaming {
            let mut outcome = None;
            widgets::dialog(c.egui, "rename-group", &t("templates.renameGroup"), 420.0, |ui| {
                ui.label(tf("templates.renameGroupLabel", &[("group", from.as_str())]));
                ui.add(egui::TextEdit::singleline(to).margin(egui::Margin::symmetric(11, 9)).desired_width(f32::INFINITY));
                ui.add_space(4.0);
                ui.horizontal(|ui| {
                    if primary_button(ui, &t("common.save")).clicked() {
                        outcome = Some(true);
                    }
                    if button(ui, &t("common.cancel")).clicked() {
                        outcome = Some(false);
                    }
                });
            });

            match outcome {
                Some(true) => {
                    let (api, from, to) = (c.api.clone(), from.clone(), to.trim().to_string());
                    let message = tf("templates.groupRenamed", &[("from", &from), ("to", &to), ("name", &to)]);
                    self.group = if to.is_empty() { GroupFilter::All } else { GroupFilter::Group(widgets::group_key(&to)) };
                    self.action = Some(Task::spawn(c.egui, move || api.rename_group(&from, &to).map(|_| message)));
                    self.renaming = None;
                }
                Some(false) => self.renaming = None,
                None => {}
            }
        }

        if let Some((template, group)) = &mut self.regrouping {
            let mut outcome = None;
            let had_group = group_of(template).is_some();
            widgets::dialog(c.egui, "change-group", &t("templates.changeGroup"), 420.0, |ui| {
                ui.label(widgets::bold(&template.name));
                ui.add(
                    egui::TextEdit::singleline(group)
                        .margin(egui::Margin::symmetric(11, 9))
                        .hint_text(crate::ui::widgets::hint(ui.ctx(), t("templates.groupPlaceholder")))
                        .desired_width(f32::INFINITY),
                );
                ui.add_space(4.0);
                ui.horizontal(|ui| {
                    if primary_button(ui, &t("common.save")).clicked() {
                        outcome = Some(Some(group.trim().to_string()));
                    }
                    if had_group && button(ui, &t("templates.removeFromGroup")).clicked() {
                        outcome = Some(Some(String::new()));
                    }
                    if button(ui, &t("common.cancel")).clicked() {
                        outcome = Some(None);
                    }
                });
            });

            match outcome {
                Some(Some(group)) => {
                    let api = c.api.clone();
                    let template = template.clone();
                    let message = if group.is_empty() { t("templates.groupRemoved") } else { t("templates.groupUpdated") };
                    self.action = Some(Task::spawn(c.egui, move || {
                        let request = crate::models::UpdateTemplateRequest {
                            name: template.name.clone(),
                            description: template.description.clone(),
                            category: (!group.is_empty()).then_some(group),
                            tags: Some(template.tags.clone()),
                            status: None,
                        };
                        api.update_template(template.id, &request).map(|_| message)
                    }));
                    self.regrouping = None;
                }
                Some(None) => self.regrouping = None,
                None => {}
            }
        }
    }
}

fn group_of(template: &TemplateSummary) -> Option<String> {
    template.category.as_ref().map(|group| group.trim().to_string()).filter(|group| !group.is_empty())
}

/// The groups in use, by key, sorted by name.
fn groups_of(templates: &[&TemplateSummary]) -> Vec<Group> {
    let mut groups: Vec<Group> = Vec::new();
    for template in templates {
        let Some(name) = group_of(template) else { continue };
        let key = widgets::group_key(&name);
        match groups.iter_mut().find(|group| group.key == key) {
            Some(group) => group.count += 1,
            None => groups.push(Group { key, name, count: 1 }),
        }
    }
    groups.sort_by_key(|group| group.name.to_lowercase());
    groups
}

/// Search in the name, description, group and tags, ignoring case and accents.
fn matches(template: &TemplateSummary, needle: &str) -> bool {
    let fields = [Some(template.name.as_str()), template.description.as_deref(), template.category.as_deref()];
    fields.iter().flatten().any(|text| widgets::group_key(text).contains(needle))
        || template.tags.iter().any(|tag| widgets::group_key(tag).contains(needle))
}

/// The label's rendering, fitted into `rect` on white, a little larger while hovered.
fn thumbnail(ui: &mut egui::Ui, c: &mut Ctx, template: &TemplateSummary, rect: egui::Rect, hovered: bool) {
    let palette = theme::palette(ui.ctx());
    let api = c.api.clone();
    let (id, version) = (template.id, template.current_version_number);
    match c.images.get(c.egui, &format!("thumb:{id}:{version}"), move || api.thumbnail(id, version)) {
        ImageState::Ready(texture) => {
            let size = texture.size_vec2();
            let scale = (rect.width() / size.x).min(rect.height() / size.y).min(3.0) * if hovered { 1.04 } else { 1.0 };
            let image_rect = egui::Rect::from_center_size(rect.center(), size * scale);
            ui.painter().add(
                egui::Shadow { offset: [0, 6], blur: 16, spread: 0, color: palette.shadow }
                    .as_shape(image_rect, egui::CornerRadius::same(3)),
            );
            ui.painter().rect_filled(image_rect, egui::CornerRadius::same(3), egui::Color32::WHITE);
            ui.painter().image(
                texture.id(),
                image_rect,
                egui::Rect::from_min_max(egui::pos2(0.0, 0.0), egui::pos2(1.0, 1.0)),
                egui::Color32::WHITE,
            );
        }
        ImageState::Loading => {
            // A label-shaped placeholder until the rendering arrives.
            let aspect = (template.width_mm / template.height_mm.max(1.0)) as f32;
            let size = egui::vec2(rect.width().min(rect.height() * aspect), (rect.width() / aspect).min(rect.height()));
            ui.painter().rect_filled(egui::Rect::from_center_size(rect.center(), size), egui::CornerRadius::same(3), palette.border);
        }
        ImageState::Failed => {
            ui.painter().text(
                rect.center(),
                egui::Align2::CENTER_CENTER,
                t("templates.previewUnavailable"),
                egui::FontId::proportional(13.0),
                palette.muted,
            );
        }
    }
}

/// The dotted pattern behind the thumbnails (`radial-gradient` dots in the web UI).
fn dots(ui: &egui::Ui, rect: egui::Rect, color: egui::Color32) {
    let step = 16.0;
    let mut y = rect.top() + step / 2.0;
    while y < rect.bottom() {
        let mut x = rect.left() + step / 2.0;
        while x < rect.right() {
            ui.painter().circle_filled(egui::pos2(x, y), 1.0, color);
            x += step;
        }
        y += step;
    }
}

/// The label height in the preview's corner: on tape printers, the tape to load.
fn height_badge(ui: &egui::Ui, preview: egui::Rect, height_mm: f64) {
    let (bg, fg, dot) = widgets::hue_colors(ui.ctx(), 152.0);
    let number = {
        let text = format!("{height_mm:.1}");
        text.strip_suffix(".0").map(str::to_string).unwrap_or(text)
    };
    let big = ui.painter().layout_no_wrap(number, egui::FontId::new(13.5, crate::fonts::face(crate::fonts::HEADING)), fg);
    let small = ui.painter().layout_no_wrap("mm".into(), egui::FontId::new(10.5, crate::fonts::face(crate::fonts::HEADING)), fg);
    let size = egui::vec2(big.size().x + small.size().x + 1.0 + 20.0, big.size().y + 6.0);
    let rect = egui::Rect::from_min_size(egui::pos2(preview.right() - 10.0 - size.x, preview.top() + 10.0), size);
    ui.painter().rect(
        rect,
        egui::CornerRadius::same(99),
        bg,
        egui::Stroke::new(1.0_f32, dot.gamma_multiply(0.35)),
        egui::StrokeKind::Outside,
    );
    let baseline = rect.top() + 3.0;
    let big_width = big.size().x;
    ui.painter().galley(egui::pos2(rect.left() + 10.0, baseline), big, fg);
    ui.painter().galley(egui::pos2(rect.left() + 10.0 + big_width + 1.0, baseline + 3.0), small, fg);
}

/// "Public · by Alex" / "Private".
fn access_chip(ui: &mut egui::Ui, template: &TemplateSummary) {
    let owner = if template.is_mine {
        String::new()
    } else {
        match &template.owner_name {
            Some(name) => format!(" · {}", tf("templates.byOwner", &[("name", name)])),
            None => format!(" · {}", t("templates.shared")),
        }
    };
    let visibility = t(if template.is_public { "templates.public" } else { "templates.private" });
    widgets::chip(ui, &format!("{visibility}{owner}"), if template.is_public { Tone::Info } else { Tone::Neutral });
}

/// A dashed "+ Group" chip for a template without one.
fn chip_button(ui: &mut egui::Ui, text: &str) -> egui::Response {
    let palette = theme::palette(ui.ctx());
    let galley = ui.painter().layout_no_wrap(text.to_string(), egui::FontId::new(12.5, egui::FontFamily::Proportional), palette.muted);
    let (rect, response) = ui.allocate_exact_size(galley.size() + egui::vec2(18.0, 7.0), egui::Sense::click());
    let color = if response.hovered() { palette.primary } else { palette.muted };
    ui.painter().rect_stroke(rect, egui::CornerRadius::same(99), egui::Stroke::new(1.0_f32, color), egui::StrokeKind::Inside);
    ui.painter().galley(rect.min + egui::vec2(9.0, 3.5), galley, color);
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}
