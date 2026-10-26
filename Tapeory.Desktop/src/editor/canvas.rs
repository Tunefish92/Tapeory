//! The label canvas: draws the design like the renderer lays it out, and handles selecting,
//! moving, resizing and rotating with the mouse.

use std::collections::HashMap;

use eframe::egui::{self, Color32, CursorIcon, FontFamily, FontId, Pos2, Sense, Shape, Stroke, epaint};

use super::document::{Kind, LabelDocument, LabelObject, PT_TO_MM, two_dimensional};
use super::fit::{LINE_HEIGHT, fit};
use super::geometry::{Handle, Point, drag_handle, handles, hit, pt, rotate_handle, to_label};
use crate::api::{Api, ApiResult};
use crate::fonts::Fonts;
use crate::images::{ImageState, Images};
use crate::models::EncodedBarcode;
use crate::task::Task;
use crate::theme;

/// Screen points per millimetre at 100 % zoom (the web editor's PIXELS_PER_MM).
pub const POINTS_PER_MM: f32 = 4.0;
const MARGIN: f32 = 20.0;
const HANDLE_SIZE: f32 = 9.0;
const ROTATE_DISTANCE: f32 = 22.0;

/// Barcode encodings from the engine, per type and value, so the canvas draws what prints.
#[derive(Default)]
pub struct Barcodes {
    entries: HashMap<(String, String), BarcodeEntry>,
}

enum BarcodeEntry {
    Loading(Task<ApiResult<EncodedBarcode>>),
    Ready(EncodedBarcode),
    Failed(String),
}

impl Barcodes {
    pub fn get(&mut self, ctx: &egui::Context, api: &Api, symbology: &str, data: &str) -> Option<Result<&EncodedBarcode, &str>> {
        let key = (symbology.to_string(), data.to_string());

        if !self.entries.contains_key(&key) {
            let (api, symbology, data) = (api.clone(), symbology.to_string(), data.to_string());
            self.entries.insert(key.clone(), BarcodeEntry::Loading(Task::spawn(ctx, move || api.encode_barcode(&symbology, &data))));
        }

        let entry = self.entries.get_mut(&key).expect("inserted");
        if let BarcodeEntry::Loading(task) = entry
            && let Some(result) = task.take()
        {
            *entry = match result {
                Ok(barcode) => BarcodeEntry::Ready(barcode),
                Err(error) => BarcodeEntry::Failed(error.message),
            };
        }

        match entry {
            BarcodeEntry::Loading(_) => None,
            BarcodeEntry::Ready(barcode) => Some(Ok(barcode)),
            BarcodeEntry::Failed(message) => Some(Err(message.as_str())),
        }
    }
}

struct Drag {
    id: String,
    start: LabelObject,
    kind: DragKind,
}

enum DragKind {
    Move(Point),
    Handle(Handle),
}

#[derive(Default)]
pub struct Canvas {
    drag: Option<Drag>,
}

/// What the canvas needs besides the document.
pub struct Services<'a> {
    pub ctx: &'a egui::Context,
    pub api: &'a Api,
    pub fonts: &'a mut Fonts,
    pub images: &'a mut Images,
    pub barcodes: &'a mut Barcodes,
}

impl Canvas {
    /// Height the canvas needs to show the whole label with its margins and rotation handle.
    pub fn content_height(document: &LabelDocument, zoom: f32) -> f32 {
        document.height_mm as f32 * POINTS_PER_MM * zoom + MARGIN * 2.0 + ROTATE_DISTANCE
    }

    /// The largest zoom (in steps of `step`, between `min` and `max`) at which the whole label fits
    /// into `space`.
    pub fn fitting_zoom(document: &LabelDocument, space: egui::Vec2, step: f32, min: f32, max: f32) -> f32 {
        let per_zoom = egui::vec2(document.width_mm as f32, document.height_mm as f32) * POINTS_PER_MM;
        let room = space - egui::vec2(MARGIN * 2.0, MARGIN * 2.0 + ROTATE_DISTANCE);
        let fit = (room.x / per_zoom.x.max(1.0)).min(room.y / per_zoom.y.max(1.0));
        ((fit / step).floor() * step).clamp(min, max)
    }

    pub fn dragging(&self) -> bool {
        self.drag.is_some()
    }

    #[allow(clippy::too_many_arguments)]
    pub fn show(
        &mut self,
        ui: &mut egui::Ui,
        document: &mut LabelDocument,
        selected: &mut Option<String>,
        zoom: f32,
        preview: bool,
        read_only: bool,
        unprintable: Option<&crate::models::PrintArea>,
        services: &mut Services,
    ) {
        let scale = POINTS_PER_MM * zoom;
        let label_size = egui::vec2(document.width_mm as f32 * scale, document.height_mm as f32 * scale);
        // At least the visible area, so a click beside the label clears the selection.
        let size = (label_size + egui::vec2(MARGIN * 2.0, MARGIN * 2.0 + ROTATE_DISTANCE)).max(ui.available_size());
        let (response, painter) = ui.allocate_painter(size, Sense::click_and_drag());
        let origin = response.rect.min + egui::vec2(MARGIN, MARGIN + ROTATE_DISTANCE);
        let palette = theme::palette(ui.ctx());

        let to_screen = |p: Point| Pos2::new(origin.x + p.x as f32 * scale, origin.y + p.y as f32 * scale);
        let to_mm = |p: Pos2| pt(((p.x - origin.x) / scale) as f64, ((p.y - origin.y) / scale) as f64);

        // The dotted mat, then the label: white, with the web's soft shadow and a thin outline.
        let mut y = response.rect.top() + 12.0;
        while y < response.rect.bottom() {
            let mut x = response.rect.left() + 12.0;
            while x < response.rect.right() {
                painter.circle_filled(Pos2::new(x, y), 0.9, palette.border);
                x += 16.0;
            }
            y += 16.0;
        }
        let label_rect = egui::Rect::from_min_size(origin, label_size);
        painter.add(
            egui::Shadow { offset: [0, 14], blur: 30, spread: 0, color: palette.shadow }.as_shape(label_rect, egui::CornerRadius::same(2)),
        );
        painter.rect_filled(label_rect, 0.0, Color32::WHITE);
        painter.rect_stroke(label_rect, 0.0, Stroke::new(1.0_f32, Color32::from_gray(208)), egui::StrokeKind::Outside);

        let clipped = painter.with_clip_rect(label_rect.expand(1.0));
        for object in document.objects.iter().filter(|object| !object.hidden) {
            draw(&clipped, object, preview, scale, &to_screen, services);
        }

        // The printable area: what lies outside the dashed line is cut off when printing.
        if let Some(area) = unprintable.filter(|area| !preview && area.top_mm + area.right_mm + area.bottom_mm + area.left_mm > 0.0) {
            let inner = egui::Rect::from_min_max(
                to_screen(pt(area.left_mm, area.top_mm)),
                to_screen(pt(document.width_mm - area.right_mm, document.height_mm - area.bottom_mm)),
            );
            if inner.is_positive() {
                let corners = [inner.left_top(), inner.right_top(), inner.right_bottom(), inner.left_bottom(), inner.left_top()];
                let stroke = Stroke::new(1.2_f32, Color32::from_rgb(220, 38, 38).gamma_multiply(0.5));
                painter.extend(egui::Shape::dashed_line(&corners, stroke, 6.0, 4.0));
            }
        }

        let selectable = !preview && !read_only;
        let selection = selected.as_ref().and_then(|id| document.find(id)).cloned();

        if let Some(object) = &selection
            && selectable
        {
            draw_selection(&painter, object, scale, &to_screen, palette.primary);
        }

        if !selectable {
            *selected = None;
            self.drag = None;
            return;
        }

        // Which handle (if any) is under a screen position.
        let handle_at = |position: Pos2| -> Option<Handle> {
            let object = selection.as_ref().filter(|object| !object.locked)?;
            let rotate = to_screen(to_label(object, rotate_handle(object, (ROTATE_DISTANCE / scale) as f64)));
            if object.kind != Kind::Line && rotate.distance(position) <= HANDLE_SIZE {
                return Some(Handle::Rotate);
            }
            handles(object)
                .into_iter()
                .find(|(_, local)| to_screen(to_label(object, *local)).distance(position) <= HANDLE_SIZE)
                .map(|(handle, _)| handle)
        };

        let object_at = |position: Pos2| -> Option<LabelObject> {
            let slop = (4.0 / scale) as f64;
            document.objects.iter().rev().find(|object| !object.hidden && hit(object, to_mm(position), slop)).cloned()
        };

        if let Some(hover) = response.hover_pos() {
            match handle_at(hover) {
                Some(Handle::Rotate) => ui.ctx().set_cursor_icon(CursorIcon::Alias),
                Some(Handle::Edge { horizontal, vertical }) => ui.ctx().set_cursor_icon(match (horizontal, vertical) {
                    (0, _) => CursorIcon::ResizeVertical,
                    (_, 0) => CursorIcon::ResizeHorizontal,
                    (h, v) if h == v => CursorIcon::ResizeNwSe,
                    _ => CursorIcon::ResizeNeSw,
                }),
                Some(Handle::LineEnd(_)) => ui.ctx().set_cursor_icon(CursorIcon::Crosshair),
                None if object_at(hover).is_some_and(|object| !object.locked) => ui.ctx().set_cursor_icon(CursorIcon::Move),
                None => {}
            }
        }

        if response.drag_started_by(egui::PointerButton::Primary) {
            let start = ui.input(|input| input.pointer.press_origin()).or(response.interact_pointer_pos());
            if let Some(start) = start {
                if let (Some(handle), Some(object)) = (handle_at(start), &selection) {
                    self.drag = Some(Drag { id: object.id.clone(), start: object.clone(), kind: DragKind::Handle(handle) });
                } else if let Some(object) = object_at(start) {
                    *selected = Some(object.id.clone());
                    if !object.locked {
                        self.drag = Some(Drag { id: object.id.clone(), start: object, kind: DragKind::Move(to_mm(start)) });
                    }
                } else {
                    *selected = None;
                }
            }
        } else if response.clicked()
            && let Some(position) = response.interact_pointer_pos()
        {
            *selected = object_at(position).map(|object| object.id);
        }

        if let (Some(drag), Some(pointer)) = (&self.drag, response.interact_pointer_pos())
            && response.dragged()
        {
            let snap = ui.input(|input| input.modifiers.shift);
            let updated = match &drag.kind {
                DragKind::Move(from) => {
                    let now = to_mm(pointer);
                    let mut moved = drag.start.clone();
                    moved.x = round(drag.start.x + now.x - from.x);
                    moved.y = round(drag.start.y + now.y - from.y);
                    moved
                }
                DragKind::Handle(handle) => {
                    let mut resized = drag_handle(&drag.start, *handle, to_mm(pointer), snap);
                    resized.x = round(resized.x);
                    resized.y = round(resized.y);
                    resized.width = resized.width.map(round);
                    resized.height = resized.height.map(round);
                    resized
                }
            };
            if let Some(object) = document.find_mut(&drag.id) {
                *object = updated;
            }
        }

        if response.drag_stopped() {
            self.drag = None;
        }
    }
}

/// Keeps mm values tidy: two decimals.
fn round(value: f64) -> f64 {
    (value * 100.0).round() / 100.0
}

/// "#rrggbb", "#rgb" or "#rrggbbaa"; None for "transparent" or an empty value.
pub fn parse_color(value: Option<&str>) -> Option<Color32> {
    let value = value?.trim();
    let hex = value.strip_prefix('#')?;
    let digits: Vec<u8> = hex.chars().filter_map(|c| c.to_digit(16).map(|d| d as u8)).collect();

    match digits.len() {
        3 => Some(Color32::from_rgb(digits[0] * 17, digits[1] * 17, digits[2] * 17)),
        6 => Some(Color32::from_rgb(digits[0] * 16 + digits[1], digits[2] * 16 + digits[3], digits[4] * 16 + digits[5])),
        8 => Some(Color32::from_rgba_unmultiplied(
            digits[0] * 16 + digits[1],
            digits[2] * 16 + digits[3],
            digits[4] * 16 + digits[5],
            digits[6] * 16 + digits[7],
        )),
        _ => None,
    }
}

pub fn color_hex(color: Color32) -> String {
    format!("#{:02x}{:02x}{:02x}", color.r(), color.g(), color.b())
}

fn polygon(object: &LabelObject, points: &[Point], to_screen: &impl Fn(Point) -> Pos2) -> Vec<Pos2> {
    points.iter().map(|p| to_screen(to_label(object, *p))).collect()
}

/// A rectangle with rounded corners, as points in the object's frame.
fn rounded_rect(w: f64, h: f64, radius: f64) -> Vec<Point> {
    let r = radius.clamp(0.0, w.min(h) / 2.0);
    if r <= 0.0 {
        return vec![pt(0.0, 0.0), pt(w, 0.0), pt(w, h), pt(0.0, h)];
    }

    let mut points = Vec::new();
    let corners = [(w - r, r, -90.0), (w - r, h - r, 0.0), (r, h - r, 90.0), (r, r, 180.0)];
    for (cx, cy, start) in corners {
        for step in 0..=8 {
            let angle = (start + step as f64 * 90.0 / 8.0_f64).to_radians();
            points.push(pt(cx + r * angle.cos(), cy + r * angle.sin()));
        }
    }
    points
}

fn ellipse(w: f64, h: f64) -> Vec<Point> {
    (0..64)
        .map(|step| {
            let angle = step as f64 / 64.0 * std::f64::consts::TAU;
            pt(w / 2.0 + w / 2.0 * angle.cos(), h / 2.0 + h / 2.0 * angle.sin())
        })
        .collect()
}

fn fill_and_stroke(painter: &egui::Painter, points: Vec<Pos2>, object: &LabelObject, scale: f32) {
    if let Some(fill) = parse_color(object.fill.as_deref()) {
        painter.add(Shape::convex_polygon(points.clone(), fill, Stroke::NONE));
    }
    if let Some(stroke) = parse_color(object.stroke.as_deref()) {
        let width = object.stroke_width.unwrap_or(0.0) as f32 * scale;
        if width > 0.0 {
            painter.add(Shape::closed_line(points, Stroke::new(width, stroke)));
        }
    }
}

fn draw(painter: &egui::Painter, object: &LabelObject, preview: bool, scale: f32, to_screen: &impl Fn(Point) -> Pos2, s: &mut Services) {
    match object.kind {
        Kind::Rect => {
            let points = rounded_rect(object.w(), object.h(), object.corner_radius.unwrap_or(0.0));
            fill_and_stroke(painter, polygon(object, &points, to_screen), object, scale);
        }
        Kind::Ellipse => fill_and_stroke(painter, polygon(object, &ellipse(object.w(), object.h()), to_screen), object, scale),
        Kind::Line => {
            if let (Some([x1, y1, x2, y2]), Some(color)) = (object.points, parse_color(object.stroke.as_deref())) {
                let width = (object.stroke_width.unwrap_or(0.5) as f32 * scale).max(0.5);
                let from = to_screen(to_label(object, pt(x1, y1)));
                let to = to_screen(to_label(object, pt(x2, y2)));
                painter.line_segment([from, to], Stroke::new(width, color));
            }
        }
        Kind::Text => {
            let text = object.text.clone().unwrap_or_default();
            draw_text(painter, object, &text, scale, to_screen, s);
        }
        Kind::DynamicField => {
            let name = object.field_name.clone().unwrap_or_default();
            let text = if preview {
                object.default_value.clone().filter(|value| !value.is_empty()).unwrap_or_else(|| format!("[{name}]"))
            } else {
                format!("{{{{{name}}}}}")
            };
            draw_text(painter, object, &text, scale, to_screen, s);
        }
        Kind::Barcode => draw_barcode(painter, object, scale, to_screen, s),
        Kind::Image => {
            let Some(url) = object.url.clone() else { return };
            if let ImageState::Ready(texture) = s.images.load(s.ctx, s.api, &url) {
                let corners =
                    polygon(object, &[pt(0.0, 0.0), pt(object.w(), 0.0), pt(object.w(), object.h()), pt(0.0, object.h())], to_screen);
                let uvs = [Pos2::new(0.0, 0.0), Pos2::new(1.0, 0.0), Pos2::new(1.0, 1.0), Pos2::new(0.0, 1.0)];
                let mut mesh = epaint::Mesh::with_texture(texture.id());
                for (position, uv) in corners.into_iter().zip(uvs) {
                    mesh.vertices.push(epaint::Vertex { pos: position, uv, color: Color32::WHITE });
                }
                mesh.indices = vec![0, 1, 2, 0, 2, 3];
                painter.add(Shape::mesh(mesh));
            }
        }
        Kind::Unknown => {}
    }
}

/// Measures with the font the label prints with: the width of `text` at a size of 1 (scaled).
fn measurer<'a>(ctx: &'a egui::Context, family: &'a FontFamily) -> impl FnMut(&str, f64) -> f64 + 'a {
    const REFERENCE: f32 = 24.0;
    let mut cache: HashMap<String, f64> = HashMap::new();
    move |text: &str, size: f64| {
        let width = *cache.entry(text.to_string()).or_insert_with(|| {
            ctx.fonts(|fonts| {
                fonts.layout_no_wrap(text.to_string(), FontId::new(REFERENCE, family.clone()), Color32::BLACK).size().x as f64
            }) / REFERENCE as f64
        });
        width * size
    }
}

fn draw_text(painter: &egui::Painter, object: &LabelObject, text: &str, scale: f32, to_screen: &impl Fn(Point) -> Pos2, s: &mut Services) {
    let family_name = object.font_family.clone().unwrap_or_else(|| "Arial".into());
    let family = s.fonts.family(s.ctx, s.api, &family_name, object.bold());
    let color = parse_color(object.fill.as_deref()).unwrap_or(Color32::BLACK);
    let size_mm = object.font_size.unwrap_or(12.0) * PT_TO_MM;
    let fit_mode = object.fit.clone().unwrap_or_else(|| "none".into());

    let mut measure = measurer(s.ctx, &family);
    let fitted = fit(text, object.w(), object.h(), size_mm, &fit_mode, &mut measure);
    let line_height = fitted.font_size * LINE_HEIGHT;
    let pixels = (fitted.font_size as f32 * scale).max(1.0);
    let angle = object.rotation.to_radians() as f32;

    for (index, line) in fitted.lines.iter().enumerate() {
        let galley = painter.layout_no_wrap(line.clone(), FontId::new(pixels, family.clone()), color);
        let width = galley.size().x as f64 / scale as f64;
        let height = galley.size().y as f64 / scale as f64;
        let x = match object.align.as_deref() {
            Some("center") => (object.w() - width) / 2.0,
            Some("right") => object.w() - width,
            _ => 0.0,
        };
        let y = index as f64 * line_height + (line_height - height) / 2.0;
        let position = to_screen(to_label(object, pt(x, y)));
        painter.add(epaint::TextShape::new(position, galley, color).with_angle(angle));
    }
}

fn draw_barcode(painter: &egui::Painter, object: &LabelObject, scale: f32, to_screen: &impl Fn(Point) -> Pos2, s: &mut Services) {
    let symbology = object.symbology.clone().unwrap_or_else(|| "code128".into());
    let data = object.data.clone().unwrap_or_default();
    let color = parse_color(object.fill.as_deref()).unwrap_or(Color32::BLACK);
    let (w, h) = (object.w(), object.h());

    let encoded = s.barcodes.get(s.ctx, s.api, &symbology, &data).map(|result| result.cloned().map_err(str::to_string));

    match encoded {
        None => {}
        Some(Err(_)) => {
            // Can't be encoded: a crossed-out box, like the web editor.
            let grey = Stroke::new((w.min(h) * 0.03) as f32 * scale, Color32::from_gray(153));
            let corners = polygon(object, &[pt(0.0, 0.0), pt(w, 0.0), pt(w, h), pt(0.0, h)], to_screen);
            painter.add(Shape::closed_line(corners.clone(), grey));
            painter.line_segment([corners[0], corners[2]], grey);
            painter.line_segment([corners[1], corners[3]], grey);
        }
        Some(Ok(barcode)) => {
            let two_d = barcode.two_dimensional || two_dimensional(&symbology);
            let text_height = if !two_d && object.show_text.unwrap_or(true) { (h * 0.25).min(4.0) } else { 0.0 };
            let bars_height = h - text_height;
            let columns = barcode.columns.max(1) as f64;
            let rows = barcode.rows.max(1) as f64;
            let module = if two_d { (w / columns).min(bars_height / rows) } else { w / columns };
            let module_height = if two_d { module } else { bars_height };
            let left = (w - module * columns) / 2.0;
            let top = if two_d { (bars_height - module * rows) / 2.0 } else { 0.0 };
            let modules = barcode.modules.as_bytes();

            for row in 0..barcode.rows {
                let mut column = 0;
                while column < barcode.columns {
                    if modules.get(row * barcode.columns + column) != Some(&b'1') {
                        column += 1;
                        continue;
                    }
                    let start = column;
                    while column < barcode.columns && modules.get(row * barcode.columns + column) == Some(&b'1') {
                        column += 1;
                    }
                    let (x0, x1) = (left + start as f64 * module, left + column as f64 * module);
                    let (y0, y1) = (top + row as f64 * module_height, top + (row + 1) as f64 * module_height);
                    let quad = polygon(object, &[pt(x0, y0), pt(x1, y0), pt(x1, y1), pt(x0, y1)], to_screen);
                    painter.add(Shape::convex_polygon(quad, color, Stroke::NONE));
                }
            }

            if text_height > 0.0 && !barcode.text.is_empty() {
                let pixels = (text_height * 0.8) as f32 * scale;
                let family = s.fonts.family(s.ctx, s.api, "Arial", false);
                let galley = painter.layout_no_wrap(barcode.text.clone(), FontId::new(pixels.max(1.0), family), color);
                let width = galley.size().x as f64 / scale as f64;
                let position = to_screen(to_label(object, pt((w - width) / 2.0, bars_height + text_height * 0.1)));
                painter.add(epaint::TextShape::new(position, galley, color).with_angle(object.rotation.to_radians() as f32));
            }
        }
    }
}

fn draw_selection(painter: &egui::Painter, object: &LabelObject, scale: f32, to_screen: &impl Fn(Point) -> Pos2, color: Color32) {
    let (left, top, right, bottom) = object.local_box();
    let outline = polygon(object, &[pt(left, top), pt(right, top), pt(right, bottom), pt(left, bottom)], to_screen);
    painter.add(Shape::closed_line(outline, Stroke::new(1.5_f32, color)));

    if object.locked {
        return;
    }

    for (_, local) in handles(object) {
        let center = to_screen(to_label(object, local));
        let rect = egui::Rect::from_center_size(center, egui::vec2(HANDLE_SIZE, HANDLE_SIZE));
        painter.rect_filled(rect, 2.0, Color32::WHITE);
        painter.rect_stroke(rect, 2.0, Stroke::new(1.5_f32, color), egui::StrokeKind::Middle);
    }

    if object.kind != Kind::Line {
        let top_center = to_screen(to_label(object, pt(object.w() / 2.0, 0.0)));
        let rotate = to_screen(to_label(object, rotate_handle(object, (ROTATE_DISTANCE / scale) as f64)));
        painter.line_segment([top_center, rotate], Stroke::new(1.0_f32, color));
        painter.circle(rotate, HANDLE_SIZE / 2.0 + 1.0, Color32::WHITE, Stroke::new(1.5_f32, color));
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn parses_colors() {
        assert_eq!(parse_color(Some("#ff0000")), Some(Color32::from_rgb(255, 0, 0)));
        assert_eq!(parse_color(Some("#0f0")), Some(Color32::from_rgb(0, 255, 0)));
        assert_eq!(parse_color(Some("transparent")), None);
        assert_eq!(color_hex(Color32::from_rgb(1, 2, 255)), "#0102ff");
    }
}
