//! A table in a card with sortable columns, as the web UI's DataGrid (template list view, print
//! history).

use eframe::egui::{self, Color32, CornerRadius, RichText, Ui};

use crate::fonts::{self, face};
use crate::theme;

pub struct Column {
    pub label: String,
    /// Share of the width left after the fixed columns.
    pub weight: f32,
    /// A fixed width instead (actions).
    pub fixed: Option<f32>,
    pub sortable: bool,
    /// Left out when the table is narrower than this.
    pub hide_below: f32,
}

impl Column {
    pub fn new(label: impl Into<String>, weight: f32) -> Column {
        Column { label: label.into(), weight, fixed: None, sortable: true, hide_below: 0.0 }
    }

    pub fn fixed(width: f32) -> Column {
        Column { label: String::new(), weight: 0.0, fixed: Some(width), sortable: false, hide_below: 0.0 }
    }

    pub fn hide_below(mut self, width: f32) -> Column {
        self.hide_below = width;
        self
    }
}

/// The column sorted by and the direction (true = ascending).
pub type Sort = Option<(usize, bool)>;

/// Draws the grid. `cell(ui, row, column)` fills one cell; returns the row whose background was
/// clicked (to open it), if any.
pub fn show(
    ui: &mut Ui,
    columns: &[Column],
    sort: &mut Sort,
    rows: usize,
    row_height: f32,
    footer: Option<&str>,
    mut cell: impl FnMut(&mut Ui, usize, usize),
) -> Option<usize> {
    let palette = theme::palette(ui.ctx());
    let mut clicked = None;

    egui::Frame::new()
        .fill(palette.elevated)
        .stroke(egui::Stroke::new(1.0_f32, palette.border))
        .corner_radius(CornerRadius::same(18))
        .shadow(egui::Shadow { offset: [0, 1], blur: 3, spread: 0, color: palette.shadow.gamma_multiply(0.5) })
        .show(ui, |ui| {
            ui.set_width(ui.available_width());
            ui.spacing_mut().item_spacing.y = 0.0;
            let width = ui.available_width();
            let shown: Vec<usize> = (0..columns.len()).filter(|&index| width >= columns[index].hide_below).collect();

            let fixed: f32 = shown.iter().filter_map(|&index| columns[index].fixed).sum();
            let weights: f32 = shown.iter().filter(|&&index| columns[index].fixed.is_none()).map(|&index| columns[index].weight).sum();
            let padding = 20.0;
            let flexible = (width - fixed - padding * 2.0).max(0.0);
            let widths: Vec<f32> =
                shown.iter().map(|&index| columns[index].fixed.unwrap_or(flexible * columns[index].weight / weights.max(0.001))).collect();

            // Header: uppercase Space Grotesk on the subtle background, with sort arrows.
            let (header, _) = ui.allocate_exact_size(egui::vec2(width, 46.0), egui::Sense::hover());
            ui.painter().rect_filled(header, CornerRadius { nw: 18, ne: 18, sw: 0, se: 0 }, palette.subtle);
            ui.painter().hline(header.x_range(), header.bottom(), egui::Stroke::new(1.0_f32, palette.border));
            let mut x = header.left() + padding;
            for (position, &index) in shown.iter().enumerate() {
                let column = &columns[index];
                let rect = egui::Rect::from_min_size(egui::pos2(x, header.top()), egui::vec2(widths[position], header.height()));
                x += widths[position];
                if column.label.is_empty() {
                    continue;
                }

                let active = sort.is_some_and(|(sorted, _)| sorted == index);
                let color = if active { palette.text } else { palette.muted };
                let galley = ui.painter().layout_no_wrap(column.label.to_uppercase(), egui::FontId::new(12.0, face(fonts::HEADING)), color);
                let text_pos = egui::pos2(rect.left(), rect.center().y - galley.size().y / 2.0);
                let text_width = galley.size().x;
                ui.painter().galley(text_pos, galley, color);

                if column.sortable {
                    let arrows = egui::pos2(text_pos.x + text_width + 8.0, rect.center().y);
                    let (up, down) = match sort {
                        Some((sorted, ascending)) if *sorted == index => {
                            if *ascending {
                                (palette.text, palette.border)
                            } else {
                                (palette.border, palette.text)
                            }
                        }
                        _ => (palette.input_border, palette.input_border),
                    };
                    triangle(ui, arrows + egui::vec2(0.0, -3.5), true, up);
                    triangle(ui, arrows + egui::vec2(0.0, 3.5), false, down);

                    let hit = egui::Rect::from_min_max(rect.min, egui::pos2(arrows.x + 8.0, rect.max.y));
                    let response = ui.interact(hit, ui.id().with(("sort", index)), egui::Sense::click());
                    if response.clicked() {
                        // Ascending, descending, then back to the original order.
                        *sort = match *sort {
                            Some((sorted, true)) if sorted == index => Some((index, false)),
                            Some((sorted, false)) if sorted == index => None,
                            _ => Some((index, true)),
                        };
                    }
                    response.on_hover_cursor(egui::CursorIcon::PointingHand);
                }
            }

            for row in 0..rows {
                let (rect, response) = ui.allocate_exact_size(egui::vec2(width, row_height), egui::Sense::click());
                if response.hovered() {
                    ui.painter().rect_filled(rect, CornerRadius::ZERO, palette.subtle);
                }
                if response.clicked() {
                    clicked = Some(row);
                }
                if row + 1 < rows || footer.is_some() {
                    ui.painter().hline(rect.x_range(), rect.bottom(), egui::Stroke::new(1.0_f32, palette.border));
                }

                let mut x = rect.left() + padding;
                for (position, &index) in shown.iter().enumerate() {
                    let cell_rect = egui::Rect::from_min_size(egui::pos2(x, rect.top()), egui::vec2(widths[position] - 10.0, row_height));
                    x += widths[position];
                    let mut child = ui.new_child(
                        egui::UiBuilder::new()
                            .max_rect(cell_rect)
                            .layout(egui::Layout::left_to_right(egui::Align::Center))
                            .id_salt(("cell", row, index)),
                    );
                    child.set_clip_rect(cell_rect.intersect(ui.clip_rect()));
                    cell(&mut child, row, index);
                }
            }

            if let Some(footer) = footer {
                let (rect, _) = ui.allocate_exact_size(egui::vec2(width, 42.0), egui::Sense::hover());
                ui.painter().rect_filled(rect, CornerRadius { nw: 0, ne: 0, sw: 18, se: 18 }, palette.subtle);
                ui.painter().text(
                    egui::pos2(rect.left() + padding, rect.center().y),
                    egui::Align2::LEFT_CENTER,
                    footer,
                    egui::FontId::new(13.0, egui::FontFamily::Proportional),
                    palette.muted,
                );
            }
        });

    clicked
}

fn triangle(ui: &Ui, center: egui::Pos2, up: bool, color: Color32) {
    let (w, h) = (4.0, 3.2);
    let points = if up {
        vec![center + egui::vec2(0.0, -h / 2.0), center + egui::vec2(w, h / 2.0), center + egui::vec2(-w, h / 2.0)]
    } else {
        vec![center + egui::vec2(-w, -h / 2.0), center + egui::vec2(w, -h / 2.0), center + egui::vec2(0.0, h / 2.0)]
    };
    ui.painter().add(egui::Shape::convex_polygon(points, color, egui::Stroke::NONE));
}

/// The first line of a cell: strong text that doesn't wrap.
pub fn title(ui: &mut Ui, text: &str) {
    ui.add(egui::Label::new(crate::ui::widgets::bold(text)).truncate());
}

/// Muted text that doesn't wrap.
pub fn muted(ui: &mut Ui, text: &str) -> egui::Response {
    let palette = theme::palette(ui.ctx());
    ui.add(egui::Label::new(RichText::new(text).color(palette.muted)).truncate())
}
