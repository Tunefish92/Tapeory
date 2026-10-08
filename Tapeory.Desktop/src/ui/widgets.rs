//! Small pieces of the interface used across screens.

use chrono::{DateTime, Local, Utc};
use eframe::egui::{self, Color32, CornerRadius, RichText, Stroke, Ui};

use crate::fonts::{self, face};
use crate::i18n::{t, tf};
use crate::icons::{self, Icon};
use crate::theme::palette;

/// Strong text: Inter SemiBold, as `<strong>` in the web UI (egui's `strong()` only changes the
/// colour).
pub fn bold(text: impl Into<String>) -> RichText {
    RichText::new(text).family(face(fonts::BODY_SEMIBOLD))
}

/// Heading text in Space Grotesk.
pub fn heading(text: impl Into<String>) -> RichText {
    RichText::new(text).family(face(fonts::HEADING))
}

/// Tapeory's icon and name, as in the web app's header.
pub fn logo(ui: &mut Ui, size: f32, color: Color32) {
    let texture = ui.ctx().memory_mut(|memory| memory.data.get_temp::<egui::TextureHandle>(egui::Id::new("tapeory-logo")));
    let texture = texture.unwrap_or_else(|| {
        let image = image::load_from_memory(include_bytes!("../../../unraid/tapeory.png")).map(|image| image.into_rgba8());
        let color_image = match image {
            Ok(rgba) => egui::ColorImage::from_rgba_unmultiplied([rgba.width() as usize, rgba.height() as usize], rgba.as_raw()),
            Err(_) => egui::ColorImage::new([1, 1], vec![Color32::TRANSPARENT]),
        };
        let texture = ui.ctx().load_texture("tapeory-logo", color_image, egui::TextureOptions::LINEAR);
        ui.ctx().memory_mut(|memory| memory.data.insert_temp(egui::Id::new("tapeory-logo"), texture.clone()));
        texture
    });

    ui.horizontal(|ui| {
        ui.spacing_mut().item_spacing.x = size * 0.45;
        ui.add(egui::Image::new(&texture).fit_to_exact_size(egui::vec2(size * 1.45, size * 1.45)));
        ui.label(RichText::new("Tapeory").family(face(fonts::HEADING_BOLD)).size(size).color(color));
    });
}

/// The setup and sign-in screens: one card in the middle of the window, over the web UI's soft
/// blue glow.
pub fn centered_card(ui: &mut Ui, id: &str, width: f32, add: impl FnOnce(&mut Ui)) {
    let p = palette(ui.ctx());
    let area = ui.max_rect();
    let screen = ui.ctx().screen_rect();
    let background = ui.new_child(egui::UiBuilder::new().max_rect(screen));
    let mut background = background;
    background.set_clip_rect(screen);
    gradient_rect(&background, screen, CornerRadius::ZERO, p.primary.gamma_multiply(0.07), Color32::TRANSPARENT);

    // Centred vertically using the card's height from the frame before.
    let id = egui::Id::new(("centered-card", id));
    let height: f32 = ui.ctx().data(|data| data.get_temp(id)).unwrap_or(420.0);
    let width = width.min(area.width() - 32.0);
    let top = ((area.height() - height) / 2.0).max(24.0);
    let rect = egui::Rect::from_min_size(egui::pos2(area.center().x - width / 2.0, area.top() + top), egui::vec2(width, height));

    egui::ScrollArea::vertical().show(ui, |ui| {
        let response = ui.scope_builder(egui::UiBuilder::new().max_rect(rect), |ui| {
            crate::theme::card(ui).inner_margin(egui::Margin::same(32)).show(ui, |ui| {
                ui.set_width(width - 66.0);
                add(ui);
            })
        });
        let measured = response.response.rect.height();
        if (measured - height).abs() > 0.5 {
            ui.ctx().data_mut(|data| data.insert_temp(id, measured));
            ui.ctx().request_repaint();
        }
    });
}

/// A row of columns sharing the width by `weights`, all as tall as the tallest (so cards made
/// with `fill_height()` line up, like the web's CSS grids).
pub fn columns(ui: &mut Ui, id: &str, weights: &[f32], mut add: impl FnMut(usize, &mut Ui)) {
    let gap = 18.0;
    let total: f32 = weights.iter().sum();
    let free = ui.available_width() - gap * (weights.len().saturating_sub(1)) as f32;
    let id = ui.id().with(("columns", id));
    let height: f32 = ui.ctx().data(|data| data.get_temp(id)).unwrap_or(0.0);
    let mut tallest = 0.0_f32;
    let natural_id = egui::Id::new("columns-natural-height");
    ui.ctx().data_mut(|data| data.insert_temp::<f32>(natural_id, 0.0));

    ui.horizontal_top(|ui| {
        ui.spacing_mut().item_spacing.x = gap;
        for (index, weight) in weights.iter().enumerate() {
            let width = (free * weight / total).floor();
            let response = ui.allocate_ui_with_layout(egui::vec2(width, height), egui::Layout::top_down(egui::Align::Min), |ui| {
                ui.set_width(width);
                add(index, ui);
            });
            tallest = tallest.max(response.response.rect.height());
        }
    });

    // Stretched cards report their own height: the row fits the tallest of those.
    let natural: f32 = ui.ctx().data_mut(|data| data.remove_temp(natural_id)).unwrap_or(0.0);
    if natural > 0.0 {
        tallest = natural;
    }

    if (tallest - height).abs() > 0.5 {
        ui.ctx().data_mut(|data| data.insert_temp(id, tallest));
        ui.ctx().request_repaint();
    }
}

/// Called by stretched cards (see `columns`) with their unstretched height.
pub fn report_natural_height(ctx: &egui::Context, height: f32) {
    let id = egui::Id::new("columns-natural-height");
    ctx.data_mut(|data| {
        let current: f32 = data.get_temp(id).unwrap_or(0.0);
        data.insert_temp(id, current.max(height));
    });
}

/// A page title with actions on the right (`.page-header`).
pub fn page_header(ui: &mut Ui, title: &str, actions: impl FnOnce(&mut Ui)) {
    ui.horizontal(|ui| {
        ui.label(heading(title).size(28.0));
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), actions);
    });
    ui.add_space(14.0);
}

/// A section title inside a card.
pub fn section_title(ui: &mut Ui, title: &str) {
    ui.label(heading(title).size(18.0));
    ui.add_space(2.0);
}

/// A card's title with its icon in a soft square, as on the web's settings and dashboard.
pub fn section(ui: &mut Ui, icon: Icon, title: &str) {
    ui.horizontal(|ui| {
        icon_badge(ui, icon, false);
        ui.add_space(4.0);
        ui.label(heading(title).size(18.0));
    });
    ui.add_space(6.0);
}

/// An icon in a rounded square: soft primary, or translucent white on a coloured card.
pub fn icon_badge(ui: &mut Ui, icon: Icon, on_color: bool) {
    let p = palette(ui.ctx());
    let (fill, color) = if on_color { (Color32::from_white_alpha(46), Color32::WHITE) } else { (p.primary_soft, p.primary) };
    let (rect, _) = ui.allocate_exact_size(egui::vec2(40.0, 40.0), egui::Sense::hover());
    ui.painter().rect_filled(rect, CornerRadius::same(12), fill);
    icons::paint(ui, icon, egui::Rect::from_center_size(rect.center(), egui::vec2(20.0, 20.0)), color);
}

#[derive(Clone, Copy, PartialEq, Eq)]
pub enum Kind {
    /// The main action: the blue-to-cyan gradient.
    Primary,
    /// Everything else: white with a border.
    Secondary,
    Danger,
}

/// A pill button as in the web UI (`.btn`, `.btn-primary`, `.btn-danger`, `.btn-sm`).
pub struct PillButton<'a> {
    text: &'a str,
    kind: Kind,
    small: bool,
    enabled: bool,
    icon: Option<Icon>,
    selected: bool,
    min_width: f32,
    large: bool,
}

impl<'a> PillButton<'a> {
    pub fn new(text: &'a str, kind: Kind) -> PillButton<'a> {
        PillButton { text, kind, small: false, enabled: true, icon: None, selected: false, min_width: 0.0, large: false }
    }

    /// At least this wide, the text centred.
    pub fn min_width(mut self, width: f32) -> Self {
        self.min_width = width;
        self
    }

    /// Bigger text and more padding: the one button a page is about (Print).
    pub fn large(mut self) -> Self {
        self.large = true;
        self
    }

    pub fn small(mut self) -> Self {
        self.small = true;
        self
    }

    pub fn enabled(mut self, enabled: bool) -> Self {
        self.enabled = enabled;
        self
    }

    pub fn icon(mut self, icon: Icon) -> Self {
        self.icon = Some(icon);
        self
    }

    /// Shown as chosen (a segmented control's current option).
    pub fn selected(mut self, selected: bool) -> Self {
        self.selected = selected;
        self
    }
}

/// How wide a normal-sized pill button with this label is, to check that it fits before adding it.
pub fn pill_width(ui: &Ui, text: &str) -> f32 {
    let font = egui::FontId::new(14.5, face(fonts::HEADING));
    ui.painter().layout_no_wrap(text.to_string(), font, Color32::PLACEHOLDER).size().x + 17.0 * 2.0
}

impl egui::Widget for PillButton<'_> {
    fn ui(self, ui: &mut Ui) -> egui::Response {
        let p = palette(ui.ctx());
        let (size, padding) = if self.large {
            (16.5, egui::vec2(24.0, 13.0))
        } else if self.small {
            (13.0, egui::vec2(12.0, 5.5))
        } else {
            (14.5, egui::vec2(17.0, 9.0))
        };
        let weight = if self.kind == Kind::Secondary && !self.selected { fonts::HEADING_MEDIUM } else { fonts::HEADING };
        let font = egui::FontId::new(size, face(weight));
        let galley = ui.painter().layout_no_wrap(self.text.to_string(), font, Color32::PLACEHOLDER);
        let icon_size = size + 2.0;
        let icon_space = if self.icon.is_some() { icon_size + if self.text.is_empty() { 0.0 } else { 7.0 } } else { 0.0 };
        let content = galley.size().x + icon_space;
        let desired = egui::vec2((content + padding.x * 2.0).max(self.min_width), galley.size().y.max(icon_size) + padding.y * 2.0);

        let sense = if self.enabled { egui::Sense::click() } else { egui::Sense::hover() };
        let (rect, response) = ui.allocate_exact_size(desired, sense);

        if ui.is_rect_visible(rect) {
            let hovered = self.enabled && response.hovered();
            let radius = CornerRadius::same((rect.height() / 2.0).min(127.0) as u8);
            let alpha = if self.enabled { 1.0 } else { 0.55 };

            let text_color = match self.kind {
                Kind::Primary => p.on_primary,
                Kind::Danger => p.danger,
                Kind::Secondary if hovered || self.selected => p.primary,
                Kind::Secondary => p.text,
            };

            match self.kind {
                Kind::Primary => {
                    if hovered {
                        ui.painter().add(
                            egui::Shadow { offset: [0, 6], blur: 16, spread: 0, color: p.primary.gamma_multiply(0.35) }
                                .as_shape(rect, radius),
                        );
                    }
                    gradient_rect(ui, rect, radius, p.primary.gamma_multiply(alpha), p.primary_ii.gamma_multiply(alpha));
                }
                Kind::Secondary => {
                    let fill = if self.selected { p.primary_soft } else { p.elevated };
                    let stroke = if hovered || self.selected { p.primary } else { p.input_border };
                    ui.painter().rect(
                        rect,
                        radius,
                        fill.gamma_multiply(alpha),
                        Stroke::new(1.0_f32, stroke.gamma_multiply(alpha)),
                        egui::StrokeKind::Inside,
                    );
                }
                Kind::Danger => {
                    let stroke = if hovered { Stroke::new(1.0_f32, p.danger) } else { Stroke::NONE };
                    ui.painter().rect(rect, radius, p.danger_soft.gamma_multiply(alpha), stroke, egui::StrokeKind::Inside);
                }
            }

            let color = text_color.gamma_multiply(alpha);
            let mut x = rect.center().x - content / 2.0;
            if let Some(icon) = self.icon {
                let icon_rect =
                    egui::Rect::from_min_size(egui::pos2(x, rect.center().y - icon_size / 2.0), egui::vec2(icon_size, icon_size));
                icons::paint(ui, icon, icon_rect, color);
                x += icon_space;
            }
            ui.painter().galley(egui::pos2(x, rect.center().y - galley.size().y / 2.0), galley, color);
        }

        if self.enabled { response.on_hover_cursor(egui::CursorIcon::PointingHand) } else { response }
    }
}

/// A rounded rectangle filled with a diagonal gradient (the web's 135° `linear-gradient`),
/// anti-aliased like egui's own shapes.
pub fn gradient_rect(ui: &Ui, rect: egui::Rect, radius: CornerRadius, from: Color32, to: Color32) {
    ui.painter().add(gradient_shape(ui.ctx(), rect, radius, from, to));
}

pub fn gradient_shape(ctx: &egui::Context, rect: egui::Rect, radius: CornerRadius, from: Color32, to: Color32) -> egui::Shape {
    let (font_size, discs) = ctx.fonts(|fonts| (fonts.font_image_size(), fonts.texture_atlas().lock().prepared_discs()));
    let mut tessellator =
        egui::epaint::Tessellator::new(ctx.pixels_per_point(), egui::epaint::TessellationOptions::default(), font_size, discs);
    let mut mesh = egui::epaint::Mesh::default();
    tessellator.tessellate_rect(&egui::epaint::RectShape::filled(rect, radius, Color32::WHITE), &mut mesh);

    for vertex in &mut mesh.vertices {
        let t = (((vertex.pos.x - rect.left()) / rect.width().max(1.0) + (vertex.pos.y - rect.top()) / rect.height().max(1.0)) / 2.0)
            .clamp(0.0, 1.0);
        let color = lerp_color(from, to, t);
        // The tessellator fades the edge through the vertex alpha: keep that fade.
        vertex.color = color.gamma_multiply(vertex.color.a() as f32 / 255.0);
    }

    egui::Shape::mesh(mesh)
}

/// A card filled with the primary gradient (the dashboard's highlighted figure).
pub fn gradient_card<R>(ui: &mut Ui, add: impl FnOnce(&mut Ui) -> R) -> R {
    let p = palette(ui.ctx());
    let background = ui.painter().add(egui::Shape::Noop);
    let response = crate::theme::card(ui).fill(Color32::TRANSPARENT).stroke(Stroke::NONE).fill_height().show(ui, add);
    ui.painter().set(background, gradient_shape(ui.ctx(), response.response.rect, CornerRadius::same(18), p.primary, p.primary_ii));
    response.inner
}

fn lerp_color(from: Color32, to: Color32, t: f32) -> Color32 {
    let mix = |a: u8, b: u8| (a as f32 + (b as f32 - a as f32) * t).round() as u8;
    Color32::from_rgba_premultiplied(mix(from.r(), to.r()), mix(from.g(), to.g()), mix(from.b(), to.b()), mix(from.a(), to.a()))
}

/// The button that sends labels to the printer: large, full width, with the printer icon.
pub fn print_button(ui: &mut Ui, enabled: bool, text: &str) -> egui::Response {
    let width = ui.available_width();
    ui.add(PillButton::new(text, Kind::Primary).large().icon(Icon::Printer).min_width(width).enabled(enabled))
}

/// Single label or bulk print: a large switch at the top of both print pages. Returns the option
/// clicked (0 = single, 1 = bulk), if it isn't the current one.
pub fn print_mode_switch(ui: &mut Ui, selected: usize, bulk_available: bool) -> Option<usize> {
    let p = palette(ui.ctx());
    let mut clicked = None;

    ui.horizontal(|ui| {
        ui.spacing_mut().item_spacing.x = 12.0;
        let width = ((ui.available_width() - 12.0) / 2.0).min(310.0);

        for (index, key) in ["single", "bulk"].into_iter().enumerate() {
            let available = index == 0 || bulk_available;
            let active = index == selected;
            let sense = if available && !active { egui::Sense::click() } else { egui::Sense::hover() };
            let (rect, response) = ui.allocate_exact_size(egui::vec2(width, 64.0), sense);
            let hovered = available && !active && response.hovered();
            let alpha = if available { 1.0 } else { 0.55 };

            ui.painter().rect(
                rect,
                CornerRadius::same(16),
                (if active { p.primary_soft } else { p.elevated }).gamma_multiply(alpha),
                Stroke::new(2.0_f32, if active || hovered { p.primary } else { p.border }),
                egui::StrokeKind::Inside,
            );
            ui.painter().text(
                rect.left_top() + egui::vec2(16.0, 12.0),
                egui::Align2::LEFT_TOP,
                t(&format!("printing.mode.{key}")),
                egui::FontId::new(16.5, face(fonts::HEADING)),
                (if active { p.primary } else { p.text }).gamma_multiply(alpha),
            );
            ui.painter().with_clip_rect(rect.shrink(8.0)).text(
                rect.left_top() + egui::vec2(16.0, 36.0),
                egui::Align2::LEFT_TOP,
                t(&format!("printing.mode.{key}Hint")),
                egui::FontId::proportional(13.0),
                p.muted.gamma_multiply(alpha),
            );

            if !available {
                response.on_hover_text(t("printing.mode.bulkNeedsFields"));
            } else if response.on_hover_cursor(egui::CursorIcon::PointingHand).clicked() {
                clicked = Some(index);
            }
        }
    });
    ui.add_space(16.0);
    clicked
}

/// Tabs with an underline under the current one. Returns the tab clicked, if any.
pub fn tabs(ui: &mut Ui, labels: &[String], selected: usize) -> Option<usize> {
    let p = palette(ui.ctx());
    let mut clicked = None;

    let row = ui
        .horizontal(|ui| {
            ui.spacing_mut().item_spacing.x = 4.0;
            for (index, label) in labels.iter().enumerate() {
                let active = index == selected;
                let galley =
                    ui.painter().layout_no_wrap(label.clone(), egui::FontId::new(14.5, face(fonts::HEADING)), Color32::PLACEHOLDER);
                let (rect, response) = ui.allocate_exact_size(galley.size() + egui::vec2(34.0, 20.0), egui::Sense::click());
                if response.hovered() && !active {
                    ui.painter().rect_filled(rect, CornerRadius { nw: 8, ne: 8, sw: 0, se: 0 }, p.subtle);
                }
                let color = if active {
                    p.primary
                } else if response.hovered() {
                    p.text
                } else {
                    p.muted
                };
                ui.painter().galley(rect.center() - galley.size() / 2.0, galley, color);
                if active {
                    ui.painter().hline(rect.x_range(), rect.bottom() + 1.0, Stroke::new(2.0_f32, p.primary));
                }
                if response.on_hover_cursor(egui::CursorIcon::PointingHand).clicked() {
                    clicked = Some(index);
                }
            }
        })
        .response;
    ui.painter().hline(ui.max_rect().x_range(), row.rect.bottom() + 2.0, Stroke::new(1.0_f32, p.border));
    ui.add_space(14.0);
    clicked
}

/// The main action on a page: the gradient pill.
pub fn primary_button(ui: &mut Ui, text: &str) -> egui::Response {
    ui.add(PillButton::new(text, Kind::Primary))
}

pub fn primary_button_enabled(ui: &mut Ui, enabled: bool, text: &str) -> egui::Response {
    ui.add(PillButton::new(text, Kind::Primary).enabled(enabled))
}

/// A destructive action.
pub fn danger_button(ui: &mut Ui, text: &str) -> egui::Response {
    ui.add(PillButton::new(text, Kind::Danger))
}

/// A secondary pill button.
pub fn button(ui: &mut Ui, text: &str) -> egui::Response {
    ui.add(PillButton::new(text, Kind::Secondary))
}

pub fn button_enabled(ui: &mut Ui, enabled: bool, text: &str) -> egui::Response {
    ui.add(PillButton::new(text, Kind::Secondary).enabled(enabled))
}

/// A round icon-only button (delete, duplicate, view), with the name as its tooltip.
pub fn icon_button(ui: &mut Ui, icon: Icon, tooltip: &str) -> egui::Response {
    let p = palette(ui.ctx());
    let (rect, response) = ui.allocate_exact_size(egui::vec2(34.0, 34.0), egui::Sense::click());
    let color = if response.hovered() { p.primary } else { p.muted };
    if response.hovered() {
        ui.painter().circle_filled(rect.center(), 17.0, p.subtle);
    }
    icons::paint(ui, icon, egui::Rect::from_center_size(rect.center(), egui::vec2(18.0, 18.0)), color);
    response.on_hover_text(tooltip).on_hover_cursor(egui::CursorIcon::PointingHand)
}

#[derive(Clone, Copy)]
pub enum Tone {
    Success,
    Danger,
    Info,
    Neutral,
}

fn tone_colors(p: &crate::theme::Palette, tone: Tone) -> (Color32, Color32) {
    match tone {
        Tone::Success => (p.success_bg, p.success_text),
        Tone::Danger => (p.danger_bg, p.danger_text),
        Tone::Info => (p.info_bg, p.info_text),
        Tone::Neutral => (p.neutral_bg, p.neutral_text),
    }
}

/// A status pill with a dot (`.status-pill`).
pub fn pill(ui: &mut Ui, text: &str, tone: Tone) {
    let p = palette(ui.ctx());
    let (bg, fg) = tone_colors(&p, tone);
    let galley = ui.painter().layout_no_wrap(text.to_string(), egui::FontId::new(12.5, face(fonts::HEADING)), fg);
    let size = egui::vec2(galley.size().x + 10.5 + 6.0 + 10.5, galley.size().y + 6.0);
    let (rect, _) = ui.allocate_exact_size(size, egui::Sense::hover());
    ui.painter().rect_filled(rect, CornerRadius::same(99), bg);
    ui.painter().circle_filled(egui::pos2(rect.left() + 10.5 + 3.0, rect.center().y), 3.0, fg);
    ui.painter().galley(egui::pos2(rect.left() + 10.5 + 6.0 + 6.0, rect.center().y - galley.size().y / 2.0), galley, fg);
}

/// A tag-like chip without a dot (visibility on template cards).
pub fn chip(ui: &mut Ui, text: &str, tone: Tone) {
    let p = palette(ui.ctx());
    let (bg, fg) = tone_colors(&p, tone);
    let galley = ui.painter().layout_no_wrap(text.to_string(), egui::FontId::new(12.5, egui::FontFamily::Proportional), fg);
    let (rect, _) = ui.allocate_exact_size(galley.size() + egui::vec2(18.0, 7.0), egui::Sense::hover());
    ui.painter().rect_filled(rect, CornerRadius::same(99), bg);
    ui.painter().galley(rect.min + egui::vec2(9.0, 3.5), galley, fg);
}

/// A small round count ("3") next to a heading.
pub fn count_bubble(ui: &mut Ui, count: usize) {
    let p = palette(ui.ctx());
    let galley = ui.painter().layout_no_wrap(count.to_string(), egui::FontId::new(12.0, face(fonts::HEADING)), p.muted);
    let size = egui::vec2((galley.size().x + 14.0).max(22.0), 22.0);
    let (rect, _) = ui.allocate_exact_size(size, egui::Sense::hover());
    ui.painter().rect_filled(rect, CornerRadius::same(99), p.neutral_bg);
    ui.painter().galley(rect.center() - galley.size() / 2.0, galley, p.muted);
}

/// The tone of a print job or item status.
pub fn status_tone(status: &str) -> Tone {
    match status {
        "Completed" => Tone::Success,
        "Failed" => Tone::Danger,
        "Queued" | "Processing" | "Sending" | "Printing" => Tone::Info,
        _ => Tone::Neutral,
    }
}

pub fn status_text(status: &str) -> String {
    let key = format!("printing.status.{}", status.to_lowercase());
    let text = t(&key);
    if text == key { status.to_string() } else { text }
}

/// A whole job's status: a stopped job is "Stopped", while its unprinted rows say "Not printed".
pub fn job_status_text(status: &str) -> String {
    let key = format!("printing.jobStatus.{}", status.to_lowercase());
    let text = t(&key);
    if text == key { status_text(status) } else { text }
}

pub fn error_text(ui: &mut Ui, message: &str) {
    let p = palette(ui.ctx());
    ui.label(RichText::new(message).color(p.danger));
}

pub fn muted(ui: &mut Ui, text: &str) {
    let p = palette(ui.ctx());
    ui.label(RichText::new(text).color(p.muted));
}

pub fn muted_small(ui: &mut Ui, text: &str) {
    let p = palette(ui.ctx());
    ui.label(RichText::new(text).color(p.muted).size(12.5));
}

pub fn loading(ui: &mut Ui, text: &str) {
    ui.horizontal(|ui| {
        ui.spinner();
        muted(ui, text);
    });
}

/// A labelled field: the label above, the widget below.
pub fn field<R>(ui: &mut Ui, label: &str, add: impl FnOnce(&mut Ui) -> R) -> R {
    ui.vertical(|ui| {
        ui.spacing_mut().item_spacing.y = 5.0;
        let p = palette(ui.ctx());
        ui.label(RichText::new(label).family(face(fonts::BODY_MEDIUM)).color(p.muted).size(13.0));
        add(ui)
    })
    .inner
}

/// A one-line text box with a sensible width.
/// Placeholder text in a text box, in the muted colour (egui would draw it like real text).
pub fn hint(ctx: &egui::Context, text: impl Into<String>) -> RichText {
    RichText::new(text).color(palette(ctx).muted)
}

/// Width of a one-line text box's frame beyond its text (`desired_width` doesn't include it).
pub const INPUT_MARGIN: f32 = 22.0;

pub fn text_input(ui: &mut Ui, value: &mut String, width: f32) -> egui::Response {
    ui.add(egui::TextEdit::singleline(value).margin(egui::Margin::symmetric(11, 9)).desired_width(width - INPUT_MARGIN))
}

pub fn password_input(ui: &mut Ui, value: &mut String, width: f32) -> egui::Response {
    ui.add(egui::TextEdit::singleline(value).margin(egui::Margin::symmetric(11, 9)).password(true).desired_width(width - INPUT_MARGIN))
}

/// "27 Sep 2026, 14:05" in local time.
pub fn date_time(value: &Option<DateTime<Utc>>) -> String {
    match value {
        Some(value) => value.with_timezone(&Local).format("%Y-%m-%d %H:%M").to_string(),
        None => "—".to_string(),
    }
}

/// The date alone, in local time.
pub fn date(value: &Option<DateTime<Utc>>) -> String {
    match value {
        Some(value) => value.with_timezone(&Local).format("%Y-%m-%d").to_string(),
        None => "—".to_string(),
    }
}

/// "now", "5 minutes ago", "3 days ago".
pub fn relative(value: &Option<DateTime<Utc>>) -> String {
    let Some(value) = value else { return "—".to_string() };
    let seconds = (Utc::now() - *value).num_seconds().max(0);

    let (count, key) = match seconds {
        0..=59 => return t("desktop.time.now"),
        60..=3599 => (seconds / 60, "desktop.time.minutesAgo"),
        3600..=86_399 => (seconds / 3600, "desktop.time.hoursAgo"),
        _ => (seconds / 86_400, "desktop.time.daysAgo"),
    };

    tf(key, &[("count", &count.to_string())])
}

/// A modal dialog in the middle of the window over a dimmed page, like the web UI's.
pub fn dialog(ctx: &egui::Context, id: &str, title: &str, width: f32, add: impl FnOnce(&mut Ui)) {
    let p = palette(ctx);
    let screen = ctx.screen_rect();

    // The dim layer also swallows clicks on the page behind.
    egui::Area::new(egui::Id::new(("dialog-backdrop", id))).order(egui::Order::Middle).fixed_pos(screen.min).show(ctx, |ui| {
        let (rect, _) = ui.allocate_exact_size(screen.size(), egui::Sense::click());
        ui.painter().rect_filled(rect, CornerRadius::ZERO, Color32::from_black_alpha(90));
    });

    egui::Window::new(title)
        .id(egui::Id::new(id))
        .title_bar(false)
        .collapsible(false)
        .resizable(false)
        .order(egui::Order::Foreground)
        .frame(
            egui::Frame::new()
                .fill(p.elevated)
                .stroke(Stroke::new(1.0_f32, p.border))
                .corner_radius(CornerRadius::same(18))
                .shadow(egui::Shadow { offset: [0, 24], blur: 56, spread: 0, color: p.shadow })
                .inner_margin(egui::Margin::same(26)),
        )
        .anchor(egui::Align2::CENTER_CENTER, egui::Vec2::ZERO)
        .show(ctx, |ui| {
            ui.set_width(width.min(screen.width() - 80.0));
            ui.label(heading(title).size(20.0));
            ui.add_space(6.0);
            add(ui);
        });
}

/// A confirmation dialog: returns Some(true) when confirmed, Some(false) when cancelled.
pub fn confirm(ctx: &egui::Context, id: &str, title: &str, text: &str, confirm_label: &str) -> Option<bool> {
    let mut answer = None;

    dialog(ctx, id, title, 420.0, |ui| {
        ui.label(text);
        ui.add_space(10.0);
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Min), |ui| {
            if ui.add(PillButton::new(confirm_label, Kind::Danger)).clicked() {
                answer = Some(true);
            }
            if button(ui, &t("common.cancel")).clicked() {
                answer = Some(false);
            }
        });
    });

    if ctx.input(|input| input.key_pressed(egui::Key::Escape)) {
        answer = Some(false);
    }

    answer
}

/// A number field for a length stored in mm, shown in the chosen unit.
pub fn length(ui: &mut Ui, value: &mut f64, unit: crate::units::Unit, min: f64) -> egui::Response {
    let mut shown = unit.to_display(*value);
    let response = ui.add(
        egui::DragValue::new(&mut shown)
            .speed(unit.step() / 2.0)
            .range(unit.to_display(min)..=unit.to_display(2000.0))
            .max_decimals(if unit == crate::units::Unit::Inch { 3 } else { 2 })
            .suffix(format!(" {}", unit.symbol())),
    );
    if response.changed() {
        *value = unit.to_mm(shown);
    }
    response
}

/// HSL (degrees, percent, percent) to a colour, for the web's `hsl()` group colours.
pub fn hsl(hue: f32, saturation: f32, lightness: f32) -> Color32 {
    let (s, l) = (saturation / 100.0, lightness / 100.0);
    let chroma = (1.0 - (2.0 * l - 1.0).abs()) * s;
    let h = (hue.rem_euclid(360.0)) / 60.0;
    let x = chroma * (1.0 - (h % 2.0 - 1.0).abs());
    let (r, g, b) = match h as u32 {
        0 => (chroma, x, 0.0),
        1 => (x, chroma, 0.0),
        2 => (0.0, chroma, x),
        3 => (0.0, x, chroma),
        4 => (x, 0.0, chroma),
        _ => (chroma, 0.0, x),
    };
    let m = l - chroma / 2.0;
    let channel = |value: f32| ((value + m) * 255.0).round().clamp(0.0, 255.0) as u8;
    Color32::from_rgb(channel(r), channel(g), channel(b))
}

/// A group's key: trimmed, lower case, without accents (the web's `groupKey`).
pub fn group_key(group: &str) -> String {
    group
        .trim()
        .chars()
        .map(|c| match c {
            'à' | 'á' | 'â' | 'ã' | 'ä' | 'å' | 'À' | 'Á' | 'Â' | 'Ã' | 'Ä' | 'Å' => 'a',
            'è' | 'é' | 'ê' | 'ë' | 'È' | 'É' | 'Ê' | 'Ë' => 'e',
            'ì' | 'í' | 'î' | 'ï' | 'Ì' | 'Í' | 'Î' | 'Ï' => 'i',
            'ò' | 'ó' | 'ô' | 'õ' | 'ö' | 'Ò' | 'Ó' | 'Ô' | 'Õ' | 'Ö' => 'o',
            'ù' | 'ú' | 'û' | 'ü' | 'Ù' | 'Ú' | 'Û' | 'Ü' => 'u',
            'ç' | 'Ç' => 'c',
            'ñ' | 'Ñ' => 'n',
            'ý' | 'ÿ' | 'Ý' => 'y',
            other => other,
        })
        .flat_map(char::to_lowercase)
        .collect()
}

/// The group's hue, the same number the web UI hashes from its name.
pub fn group_hue(group: &str) -> f32 {
    let mut hash: i32 = 0;
    for c in group_key(group).chars() {
        hash = hash.wrapping_mul(31).wrapping_add(c as i32);
    }
    ((hash as i64).abs() % 360) as f32
}

/// Background, text and dot colour for a group (or the green tape-height badge at hue 152).
pub fn hue_colors(ctx: &egui::Context, hue: f32) -> (Color32, Color32, Color32) {
    if ctx.style().visuals.dark_mode {
        (hsl(hue, 70.0, 20.0), hsl(hue, 55.0, 78.0), hsl(hue, 70.0, 62.0))
    } else {
        (hsl(hue, 70.0, 93.0), hsl(hue, 55.0, 30.0), hsl(hue, 70.0, 50.0))
    }
}

/// A group's chip in its colour; clickable when `clickable`.
pub fn group_chip(ui: &mut Ui, group: &str, clickable: bool) -> egui::Response {
    let (bg, fg, dot) = hue_colors(ui.ctx(), group_hue(group));
    let galley = ui.painter().layout_no_wrap(group.to_string(), egui::FontId::new(12.5, face(fonts::HEADING)), fg);
    let (rect, response) =
        ui.allocate_exact_size(galley.size() + egui::vec2(20.0, 7.0), if clickable { egui::Sense::click() } else { egui::Sense::hover() });
    ui.painter().rect_filled(rect, CornerRadius::same(99), bg);
    if clickable && response.hovered() {
        ui.painter().rect_stroke(
            rect.expand(1.5),
            CornerRadius::same(99),
            Stroke::new(3.0_f32, dot.gamma_multiply(0.25)),
            egui::StrokeKind::Outside,
        );
    }
    ui.painter().galley(rect.min + egui::vec2(10.0, 3.5), galley, fg);
    if clickable { response.on_hover_cursor(egui::CursorIcon::PointingHand) } else { response }
}

/// A neutral chip with a thin border (sizes, sources).
pub fn meta_chip(ui: &mut Ui, text: &str) {
    let p = palette(ui.ctx());
    let galley = ui.painter().layout_no_wrap(text.to_string(), egui::FontId::new(12.5, egui::FontFamily::Proportional), p.muted);
    let (rect, _) = ui.allocate_exact_size(galley.size() + egui::vec2(18.0, 7.0), egui::Sense::hover());
    ui.painter().rect(rect, CornerRadius::same(99), p.subtle, Stroke::new(1.0_f32, p.border), egui::StrokeKind::Inside);
    ui.painter().galley(rect.min + egui::vec2(9.0, 3.5), galley, p.muted);
}

/// Options in a pill-shaped track, the chosen one raised (`.view-toggle`). Returns the option
/// clicked.
pub fn segmented(ui: &mut Ui, options: &[String], selected: usize) -> Option<usize> {
    let p = palette(ui.ctx());
    let mut clicked = None;
    egui::Frame::new()
        .fill(p.subtle)
        .stroke(Stroke::new(1.0_f32, p.border))
        .corner_radius(CornerRadius::same(99))
        .inner_margin(egui::Margin::same(4))
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                ui.spacing_mut().item_spacing.x = 2.0;
                // In a right-to-left row (page header actions) the options go in reversed.
                let mut order: Vec<usize> = (0..options.len()).collect();
                if ui.layout().main_dir() == egui::Direction::RightToLeft {
                    order.reverse();
                }
                for index in order {
                    let option = &options[index];
                    let active = index == selected;
                    let font = egui::FontId::new(14.0, face(if active { fonts::HEADING } else { fonts::HEADING_MEDIUM }));
                    let galley = ui.painter().layout_no_wrap(option.clone(), font, Color32::PLACEHOLDER);
                    let (rect, response) = ui.allocate_exact_size(galley.size() + egui::vec2(28.0, 14.0), egui::Sense::click());
                    if active {
                        ui.painter().add(
                            egui::Shadow { offset: [0, 1], blur: 3, spread: 0, color: p.shadow }.as_shape(rect, CornerRadius::same(99)),
                        );
                        ui.painter().rect_filled(rect, CornerRadius::same(99), p.elevated);
                    }
                    let color = if active || response.hovered() { p.text } else { p.muted };
                    ui.painter().galley(rect.center() - galley.size() / 2.0, galley, color);
                    if response.clicked() {
                        clicked = Some(index);
                    }
                    response.on_hover_cursor(egui::CursorIcon::PointingHand);
                }
            });
        });
    clicked
}

/// A filter chip: optional coloured dot, the name, a count bubble; highlighted when chosen.
pub fn filter_chip(ui: &mut Ui, text: &str, count: usize, dot: Option<Color32>, selected: bool) -> egui::Response {
    let p = palette(ui.ctx());
    let font = egui::FontId::new(14.0, face(fonts::HEADING_MEDIUM));
    let text_color = if selected { p.primary } else { p.text };
    let galley = ui.painter().layout_no_wrap(text.to_string(), font, text_color);
    let count_galley = ui.painter().layout_no_wrap(count.to_string(), egui::FontId::new(11.5, face(fonts::HEADING)), p.muted);
    let dot_space = if dot.is_some() { 16.0 } else { 0.0 };
    let count_size = egui::vec2(count_galley.size().x + 12.0, 18.0);
    let size = egui::vec2(14.0 + dot_space + galley.size().x + 8.0 + count_size.x + 12.0, 36.0);
    let (rect, response) = ui.allocate_exact_size(size, egui::Sense::click());

    let (fill, stroke) = if selected {
        (p.primary_soft, p.primary)
    } else if response.hovered() {
        (p.elevated, p.primary)
    } else {
        (p.elevated, p.input_border)
    };
    ui.painter().rect(rect, CornerRadius::same(99), fill, Stroke::new(1.0_f32, stroke), egui::StrokeKind::Inside);

    let mut x = rect.left() + 14.0;
    if let Some(dot) = dot {
        ui.painter().circle_filled(egui::pos2(x + 4.0, rect.center().y), 5.0, dot.gamma_multiply(0.25));
        ui.painter().circle_filled(egui::pos2(x + 4.0, rect.center().y), 4.0, dot);
        x += dot_space;
    }
    let galley_width = galley.size().x;
    ui.painter().galley(egui::pos2(x, rect.center().y - galley.size().y / 2.0), galley, text_color);
    x += galley_width + 8.0;
    let bubble = egui::Rect::from_min_size(egui::pos2(x, rect.center().y - count_size.y / 2.0), count_size);
    ui.painter().rect_filled(bubble, CornerRadius::same(99), if selected { p.elevated } else { p.subtle });
    ui.painter().galley(bubble.center() - count_galley.size() / 2.0, count_galley, p.muted);

    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// The big rounded search field with a magnifier and a clear button (`.template-search`).
pub fn search_box(ui: &mut Ui, value: &mut String, hint: &str) -> egui::Response {
    let p = palette(ui.ctx());
    let id = ui.id().with("search-box");
    let focused = ui.ctx().memory(|memory| memory.has_focus(id));
    let stroke = if focused { p.primary } else { p.input_border };

    egui::Frame::new()
        .fill(p.elevated)
        .stroke(Stroke::new(1.0_f32, stroke))
        .corner_radius(CornerRadius::same(99))
        .inner_margin(egui::Margin { left: 16, right: 10, top: 6, bottom: 6 })
        .show(ui, |ui| {
            ui.horizontal(|ui| {
                icons::icon(ui, Icon::Search, 18.0, p.muted);
                // The clear button and the gap before it.
                let clear_width = if value.is_empty() { 0.0 } else { 34.0 + ui.spacing().item_spacing.x };
                let response = ui.add(
                    egui::TextEdit::singleline(value)
                        .id(id)
                        .frame(false)
                        .font(egui::FontId::new(16.0, egui::FontFamily::Proportional))
                        .hint_text(RichText::new(hint).color(p.muted))
                        .margin(egui::Margin::symmetric(4, 8))
                        .desired_width(ui.available_width() - clear_width),
                );
                if response.has_focus() && ui.input(|input| input.key_pressed(egui::Key::Escape)) {
                    value.clear();
                }
                if !value.is_empty() && icon_button(ui, Icon::Close, &t("templates.clearSearch")).clicked() {
                    value.clear();
                }
                response
            })
            .inner
        })
        .inner
}

/// Tighter rows inside a dropdown menu than in forms.
pub fn compact_menu(ui: &mut Ui) {
    ui.spacing_mut().interact_size.y = 30.0;
    ui.spacing_mut().item_spacing.y = 2.0;
    ui.spacing_mut().button_padding = egui::vec2(10.0, 5.0);
}
