//! Tapeory's look (the web UI's palette from index.css) as egui visuals, light and dark.

use eframe::egui::{self, Color32, CornerRadius, Stroke, Visuals};

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum ThemeChoice {
    Light,
    Dark,
    System,
}

impl ThemeChoice {
    pub fn from_setting(value: Option<&str>) -> ThemeChoice {
        match value {
            Some("light") => ThemeChoice::Light,
            Some("dark") => ThemeChoice::Dark,
            _ => ThemeChoice::System,
        }
    }

    pub fn setting(self) -> &'static str {
        match self {
            ThemeChoice::Light => "light",
            ThemeChoice::Dark => "dark",
            ThemeChoice::System => "system",
        }
    }
}

/// The palette of the current theme, for widgets that paint their own colours.
#[derive(Clone, Copy, Debug)]
pub struct Palette {
    pub bg: Color32,
    pub elevated: Color32,
    pub subtle: Color32,
    pub header: Color32,
    pub header_text: Color32,
    pub on_header: Color32,
    pub text: Color32,
    pub muted: Color32,
    pub border: Color32,
    pub input_border: Color32,
    pub primary: Color32,
    /// The gradient's second colour (buttons, highlights).
    pub primary_ii: Color32,
    pub primary_soft: Color32,
    /// Text on the primary colour.
    pub on_primary: Color32,
    pub danger_soft: Color32,
    pub shadow: Color32,
    pub danger: Color32,
    pub danger_bg: Color32,
    pub danger_text: Color32,
    pub success_bg: Color32,
    pub success_text: Color32,
    pub info_bg: Color32,
    pub info_text: Color32,
    pub neutral_bg: Color32,
    pub neutral_text: Color32,
}

const fn rgb(hex: u32) -> Color32 {
    Color32::from_rgb((hex >> 16) as u8, (hex >> 8) as u8, hex as u8)
}

pub const LIGHT: Palette = Palette {
    bg: rgb(0xf5f6f8),
    elevated: rgb(0xffffff),
    subtle: rgb(0xeef0f4),
    header: rgb(0x12161f),
    header_text: rgb(0xffffff),
    on_header: rgb(0xa7b0c0),
    text: rgb(0x14181f),
    muted: rgb(0x62697a),
    border: rgb(0xe4e7ec),
    input_border: rgb(0xd7dbe3),
    primary: rgb(0x2563eb),
    primary_ii: rgb(0x06b6d4),
    primary_soft: Color32::from_rgba_premultiplied(4, 10, 24, 26),
    on_primary: rgb(0xffffff),
    danger_soft: Color32::from_rgba_premultiplied(18, 3, 3, 20),
    shadow: Color32::from_rgba_premultiplied(0, 0, 0, 22),
    danger: rgb(0xdc2626),
    danger_bg: rgb(0xfee2e2),
    danger_text: rgb(0x991b1b),
    success_bg: rgb(0xdcfce7),
    success_text: rgb(0x166534),
    info_bg: rgb(0xdbeafe),
    info_text: rgb(0x1e40af),
    neutral_bg: rgb(0xe5e7eb),
    neutral_text: rgb(0x374151),
};

pub const DARK: Palette = Palette {
    bg: rgb(0x0a0c11),
    elevated: rgb(0x12151d),
    subtle: rgb(0x161a23),
    header: rgb(0x05070a),
    header_text: rgb(0xf3f4f6),
    on_header: rgb(0x93a0b8),
    text: rgb(0xe7eaf0),
    muted: rgb(0x8b93a5),
    border: rgb(0x232833),
    input_border: rgb(0x2c323f),
    primary: rgb(0x4f8bff),
    primary_ii: rgb(0x22d3ee),
    primary_soft: Color32::from_rgba_premultiplied(13, 22, 41, 41),
    on_primary: rgb(0x06101f),
    danger_soft: Color32::from_rgba_premultiplied(30, 14, 14, 31),
    shadow: Color32::from_rgba_premultiplied(0, 0, 0, 90),
    danger: rgb(0xf87171),
    danger_bg: rgb(0x3a1414),
    danger_text: rgb(0xfca5a5),
    success_bg: rgb(0x14331f),
    success_text: rgb(0x86efac),
    info_bg: rgb(0x1a2b4a),
    info_text: rgb(0x93c5fd),
    neutral_bg: rgb(0x232833),
    neutral_text: rgb(0xc3c9d6),
};

pub fn palette(ctx: &egui::Context) -> Palette {
    if ctx.style().visuals.dark_mode { DARK } else { LIGHT }
}

/// Applies the palette to egui's widgets.
pub fn apply(ctx: &egui::Context, choice: ThemeChoice) {
    let dark = resolve(ctx, choice);
    let p = if dark { DARK } else { LIGHT };

    let mut visuals = if dark { Visuals::dark() } else { Visuals::light() };
    visuals.panel_fill = p.bg;
    visuals.window_fill = p.elevated;
    visuals.extreme_bg_color = p.elevated;
    visuals.faint_bg_color = p.subtle;
    visuals.override_text_color = Some(p.text);
    visuals.weak_text_color = Some(p.muted);
    visuals.hyperlink_color = p.primary;
    visuals.selection.bg_fill = p.primary.gamma_multiply(0.35);
    visuals.selection.stroke = Stroke::new(1.0_f32, p.primary);
    visuals.window_corner_radius = CornerRadius::same(18);
    visuals.window_stroke = Stroke::new(1.0_f32, p.border);
    visuals.window_shadow = egui::Shadow { offset: [0, 16], blur: 40, spread: 0, color: p.shadow };
    visuals.popup_shadow = egui::Shadow { offset: [0, 8], blur: 24, spread: 0, color: p.shadow };
    visuals.menu_corner_radius = CornerRadius::same(12);
    visuals.text_edit_bg_color = Some(p.elevated);
    visuals.text_cursor.stroke = Stroke::new(2.0_f32, p.primary);

    // Inputs and menus: 8 px corners, like the web's fields; buttons are pills (widgets.rs).
    let radius = CornerRadius::same(8);
    for widget in [
        &mut visuals.widgets.noninteractive,
        &mut visuals.widgets.inactive,
        &mut visuals.widgets.hovered,
        &mut visuals.widgets.active,
        &mut visuals.widgets.open,
    ] {
        widget.corner_radius = radius;
        widget.expansion = 0.0;
    }

    visuals.widgets.noninteractive.bg_stroke = Stroke::new(1.0_f32, p.border);
    visuals.widgets.noninteractive.fg_stroke = Stroke::new(1.0_f32, p.text);
    visuals.widgets.inactive.bg_fill = p.elevated;
    visuals.widgets.inactive.weak_bg_fill = p.elevated;
    visuals.widgets.inactive.bg_stroke = Stroke::new(1.0_f32, p.input_border);
    visuals.widgets.inactive.fg_stroke = Stroke::new(1.0_f32, p.text);
    visuals.widgets.hovered.weak_bg_fill = p.elevated;
    visuals.widgets.hovered.bg_fill = p.elevated;
    visuals.widgets.hovered.bg_stroke = Stroke::new(1.0_f32, p.primary);
    visuals.widgets.hovered.fg_stroke = Stroke::new(1.0_f32, p.primary);
    visuals.widgets.active.weak_bg_fill = p.subtle;
    visuals.widgets.active.bg_fill = p.subtle;
    visuals.widgets.active.bg_stroke = Stroke::new(1.0_f32, p.primary);
    visuals.widgets.active.fg_stroke = Stroke::new(1.0_f32, p.primary);
    visuals.widgets.open.weak_bg_fill = p.elevated;
    visuals.widgets.open.bg_stroke = Stroke::new(1.0_f32, p.primary);

    // egui keeps a light and a dark style and would switch between them when the system's theme
    // changes (Windows reports it at startup): both get Tapeory's look, and egui stays on the one
    // chosen here. The app follows the system theme itself (see `resolve`).
    ctx.set_theme(if dark { egui::Theme::Dark } else { egui::Theme::Light });
    for theme in [egui::Theme::Light, egui::Theme::Dark] {
        ctx.set_visuals_of(theme, visuals.clone());
        ctx.style_mut_of(theme, style);
    }
}

/// Whether the choice means dark right now (for "follow system", the system's current theme).
pub fn resolve(ctx: &egui::Context, choice: ThemeChoice) -> bool {
    match choice {
        ThemeChoice::Light => false,
        ThemeChoice::Dark => true,
        ThemeChoice::System => ctx.system_theme().map(|theme| theme == egui::Theme::Dark).unwrap_or(false),
    }
}

/// Tapeory's sizes and text styles.
fn style(style: &mut egui::Style) {
    {
        use egui::{FontId, TextStyle};
        let face = crate::fonts::face;
        style.text_styles = [
            (TextStyle::Small, FontId::new(12.5, egui::FontFamily::Proportional)),
            (TextStyle::Body, FontId::new(15.0, egui::FontFamily::Proportional)),
            (TextStyle::Button, FontId::new(14.5, face(crate::fonts::HEADING_MEDIUM))),
            (TextStyle::Heading, FontId::new(28.0, face(crate::fonts::HEADING))),
            (TextStyle::Monospace, FontId::new(13.5, egui::FontFamily::Monospace)),
        ]
        .into();
        style.spacing.item_spacing = egui::vec2(10.0, 10.0);
        style.spacing.button_padding = egui::vec2(14.0, 8.0);
        style.spacing.interact_size.y = 38.0;
        style.spacing.combo_width = 200.0;
        style.spacing.icon_width = 18.0;
        style.spacing.icon_width_inner = 10.0;
        style.spacing.icon_spacing = 8.0;
        style.spacing.menu_margin = egui::Margin::same(8);
        style.spacing.window_margin = egui::Margin::same(24);
        style.spacing.scroll = egui::style::ScrollStyle::floating();
    }
}

/// A card: the elevated, bordered box most content sits in (`.card` in the web UI). Its content
/// is always laid out top to bottom, also inside a row.
pub struct Card(egui::Frame, bool);

impl Card {
    /// As tall as the space it's given (cards in a row of `widgets::columns` line up).
    pub fn fill_height(self) -> Card {
        Card(self.0, true)
    }

    pub fn fill(self, color: Color32) -> Card {
        Card(self.0.fill(color), self.1)
    }

    pub fn stroke(self, stroke: Stroke) -> Card {
        Card(self.0.stroke(stroke), self.1)
    }

    pub fn inner_margin(self, margin: impl Into<egui::Margin>) -> Card {
        Card(self.0.inner_margin(margin), self.1)
    }

    pub fn show<R>(self, ui: &mut egui::Ui, add: impl FnOnce(&mut egui::Ui) -> R) -> egui::InnerResponse<R> {
        let margins = self.0.total_margin().sum().y;
        let height = if self.1 { ui.available_height() - margins } else { 0.0 };
        let response = self.0.show(ui, |ui| {
            // Block-level like the web's cards: as wide as the space it's in.
            ui.set_min_width(ui.available_width());
            ui.set_min_height(height);
            ui.vertical(add)
        });
        // Its height without stretching, so a row of cards can also get shorter again.
        if self.1 {
            crate::ui::widgets::report_natural_height(ui.ctx(), response.inner.response.rect.height() + margins);
        }
        egui::InnerResponse::new(response.inner.inner, response.response)
    }
}

/// A card: the elevated, bordered box most content sits in.
pub fn card(ui: &egui::Ui) -> Card {
    let p = palette(ui.ctx());
    Card(
        egui::Frame::new()
            .fill(p.elevated)
            .stroke(Stroke::new(1.0_f32, p.border))
            .corner_radius(CornerRadius::same(18))
            .shadow(egui::Shadow { offset: [0, 1], blur: 3, spread: 0, color: p.shadow.gamma_multiply(0.5) })
            .inner_margin(egui::Margin::same(22)),
        false,
    )
}
