//! The properties of the selected object, as in the web editor's PropertiesPanel: a header with
//! the order, duplicate and delete buttons, then boxed sections side by side.

use eframe::egui::{self, Color32, RichText};

use super::canvas::{Barcodes, color_hex, parse_color};
use super::document::{Kind, LabelObject, Reorder, SYMBOLOGIES, two_dimensional};
use crate::api::Api;
use crate::fonts::{self, face};
use crate::i18n::{t, tf};
use crate::theme;
use crate::ui::widgets::{self, Kind as ButtonKind, PillButton};
use crate::units::Unit;

pub enum Action {
    Reorder(Reorder),
    Duplicate,
    Delete,
}

pub struct Context<'a> {
    pub ctx: &'a egui::Context,
    pub api: &'a Api,
    pub unit: Unit,
    pub families: &'a [String],
    pub field_names: Vec<String>,
    pub barcodes: &'a mut Barcodes,
}

#[derive(Clone, Copy)]
enum Section {
    TextContent,
    FieldContent,
    Font,
    Fit,
    BarcodeContent,
    BarcodeAppearance,
    ShapeAppearance,
    LineAppearance,
    Layout { sized: bool },
    Options,
}

pub fn show(ui: &mut egui::Ui, object: &mut LabelObject, c: &mut Context) -> Option<Action> {
    let mut action = None;

    let title = t(match object.kind {
        Kind::Text => "editor.properties.typeText",
        Kind::DynamicField => "editor.properties.typeDynamicField",
        Kind::Rect => "editor.properties.typeRect",
        Kind::Line => "editor.properties.typeLine",
        Kind::Image => "editor.properties.typeImage",
        Kind::Ellipse => "editor.properties.typeEllipse",
        Kind::Barcode => "editor.properties.typeBarcode",
        Kind::Unknown => "editor.properties.selectPrompt",
    });

    ui.horizontal_wrapped(|ui| {
        ui.label(widgets::heading(title).size(18.0));
        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
            ui.spacing_mut().item_spacing.x = 6.0;
            if ui.add(PillButton::new(&t("editor.properties.delete"), ButtonKind::Danger)).clicked() {
                action = Some(Action::Delete);
            }
            if ui.add(PillButton::new(&t("editor.properties.duplicate"), ButtonKind::Secondary)).clicked() {
                action = Some(Action::Duplicate);
            }
            for (key, reorder) in [
                ("editor.properties.sendToBack", Reorder::Back),
                ("editor.properties.backward", Reorder::Backward),
                ("editor.properties.forward", Reorder::Forward),
                ("editor.properties.bringToFront", Reorder::Front),
            ] {
                if ui.add(PillButton::new(&t(key), ButtonKind::Secondary)).clicked() {
                    action = Some(Action::Reorder(reorder));
                }
            }
        });
    });
    ui.separator();

    let sections: Vec<Section> = match object.kind {
        Kind::Text => vec![Section::TextContent, Section::Font, Section::Layout { sized: true }, Section::Fit],
        Kind::DynamicField => vec![Section::FieldContent, Section::Font, Section::Layout { sized: true }, Section::Fit],
        Kind::Barcode => vec![Section::BarcodeContent, Section::BarcodeAppearance, Section::Layout { sized: true }, Section::Options],
        Kind::Rect | Kind::Ellipse => vec![Section::ShapeAppearance, Section::Layout { sized: true }, Section::Options],
        Kind::Line => vec![Section::LineAppearance, Section::Layout { sized: false }, Section::Options],
        Kind::Image => vec![Section::Layout { sized: true }, Section::Options],
        Kind::Unknown => vec![],
    };

    // As many sections side by side as fit at 200 points or more (the web's auto-fit grid).
    let per_row = ((ui.available_width() + 18.0) / (200.0 + 18.0)).floor().clamp(1.0, sections.len().max(1) as f32) as usize;
    for (row, chunk) in sections.chunks(per_row).enumerate() {
        widgets::columns(ui, &format!("properties-{row}"), &vec![1.0; per_row], |index, ui| {
            if let Some(section) = chunk.get(index) {
                section_box(ui, *section, object, c);
            }
        });
        ui.add_space(12.0);
    }

    action
}

fn section_box(ui: &mut egui::Ui, section: Section, object: &mut LabelObject, c: &mut Context) {
    let palette = theme::palette(ui.ctx());
    let title = match section {
        Section::TextContent | Section::FieldContent | Section::BarcodeContent => "editor.properties.sectionContent",
        Section::Font => "editor.properties.sectionFont",
        Section::Fit => "editor.properties.sectionFit",
        Section::BarcodeAppearance | Section::ShapeAppearance | Section::LineAppearance => "editor.properties.sectionAppearance",
        Section::Layout { .. } => "editor.properties.sectionLayout",
        Section::Options => "editor.properties.sectionOptions",
    };

    theme::card(ui)
        .fill(palette.subtle)
        .stroke(egui::Stroke::new(1.0_f32, palette.border))
        .inner_margin(egui::Margin::same(14))
        .fill_height()
        .show(ui, |ui| {
            ui.spacing_mut().item_spacing.y = 6.0;
            // Narrower padding in dropdowns and number fields: in a half-width column the default
            // leaves too little room for "Left" and the arrow, and the whole row would grow.
            ui.spacing_mut().button_padding.x = 8.0;
            ui.label(RichText::new(t(title).to_uppercase()).family(face(fonts::HEADING)).size(12.5).color(palette.muted));
            match section {
                Section::TextContent => {
                    field(ui, &t("editor.properties.text"), |ui| {
                        edit(&mut object.text, String::new(), |text| {
                            ui.add(
                                egui::TextEdit::multiline(text)
                                    .margin(egui::Margin::symmetric(11, 9))
                                    .desired_rows(3)
                                    .desired_width(f32::INFINITY),
                            );
                        });
                    });
                }
                Section::FieldContent => {
                    field(ui, &t("editor.properties.fieldName"), |ui| edit(&mut object.field_name, String::new(), |value| text(ui, value)));
                    field(ui, &t("editor.properties.label"), |ui| edit(&mut object.label, String::new(), |value| text(ui, value)));
                    field(ui, &t("editor.properties.defaultValue"), |ui| {
                        edit(&mut object.default_value, String::new(), |value| text(ui, value))
                    });
                    edit(&mut object.required, false, |required| _ = ui.checkbox(required, t("editor.properties.required")));
                }
                Section::Font => font(ui, object, c),
                Section::Fit => fit(ui, object),
                Section::BarcodeContent => barcode_content(ui, object, c),
                Section::BarcodeAppearance => {
                    let symbology = object.symbology.clone().unwrap_or_default();
                    if !two_dimensional(&symbology) {
                        edit(&mut object.show_text, true, |show| _ = ui.checkbox(show, t("editor.properties.barcodeShowText")));
                    }
                    field(ui, &t("editor.properties.color"), |ui| color(ui, &mut object.fill));
                    widgets::muted_small(ui, &t("editor.properties.barcodeSizeHint"));
                }
                Section::ShapeAppearance => {
                    pair(
                        ui,
                        |ui| field(ui, &t("editor.properties.fill"), |ui| fill(ui, &mut object.fill)),
                        |ui| field(ui, &t("editor.properties.stroke"), |ui| color(ui, &mut object.stroke)),
                    );
                    let unit = c.unit;
                    let rect = object.kind == Kind::Rect;
                    pair(
                        ui,
                        |ui| {
                            field(ui, &with_unit("editor.properties.strokeWidth", unit), |ui| {
                                let mut value = object.stroke_width.unwrap_or(0.0);
                                if length(ui, &mut value, unit, 0.0) {
                                    object.stroke_width = Some(value);
                                }
                            })
                        },
                        |ui| {
                            if rect {
                                field(ui, &with_unit("editor.properties.cornerRadius", unit), |ui| {
                                    let mut value = object.corner_radius.unwrap_or(0.0);
                                    if length(ui, &mut value, unit, 0.0) {
                                        object.corner_radius = Some(value);
                                    }
                                })
                            }
                        },
                    );
                }
                Section::LineAppearance => {
                    let unit = c.unit;
                    pair(
                        ui,
                        |ui| field(ui, &t("editor.properties.stroke"), |ui| color(ui, &mut object.stroke)),
                        |ui| {
                            field(ui, &with_unit("editor.properties.strokeWidth", unit), |ui| {
                                let mut value = object.stroke_width.unwrap_or(0.5);
                                if length(ui, &mut value, unit, 0.05) {
                                    object.stroke_width = Some(value);
                                }
                            })
                        },
                    );
                }
                Section::Layout { sized } => layout(ui, object, c.unit, sized),
                Section::Options => options(ui, object),
            }
        });
}

/// Edits an optional property through its default, storing it only when the user changes it
/// (so selecting an object doesn't count as an edit).
fn edit<T: Clone + PartialEq>(slot: &mut Option<T>, default: T, change: impl FnOnce(&mut T)) {
    let mut value = slot.clone().unwrap_or(default);
    let before = value.clone();
    change(&mut value);
    if value != before {
        *slot = Some(value);
    }
}

/// A label above its input, as in the web's properties fields.
fn field(ui: &mut egui::Ui, label: &str, add: impl FnOnce(&mut egui::Ui)) {
    let palette = theme::palette(ui.ctx());
    ui.vertical(|ui| {
        ui.spacing_mut().item_spacing.y = 3.0;
        ui.add(egui::Label::new(RichText::new(label).family(face(fonts::BODY_MEDIUM)).size(13.0).color(palette.muted)).truncate());
        add(ui);
    });
}

/// Two fields next to each other.
fn pair(ui: &mut egui::Ui, left: impl FnOnce(&mut egui::Ui), right: impl FnOnce(&mut egui::Ui)) {
    ui.columns(2, |columns| {
        columns[0].set_width(columns[0].available_width());
        left(&mut columns[0]);
        right(&mut columns[1]);
    });
}

fn text(ui: &mut egui::Ui, value: &mut String) {
    ui.add(egui::TextEdit::singleline(value).margin(egui::Margin::symmetric(11, 9)).desired_width(f32::INFINITY));
}

/// "Width (mm)" with the chosen unit.
fn with_unit(key: &str, unit: Unit) -> String {
    t(key).replace("(mm)", &format!("({})", unit.symbol()))
}

/// A length in mm, edited in the chosen unit. Returns whether it changed.
fn length(ui: &mut egui::Ui, value: &mut f64, unit: Unit, min: f64) -> bool {
    let mut shown = unit.to_display(*value);
    let response = ui.add_sized(
        egui::vec2(ui.available_width(), 38.0),
        egui::DragValue::new(&mut shown)
            .speed(unit.step() / 2.0)
            .range(unit.to_display(min)..=unit.to_display(2000.0))
            .max_decimals(if unit == Unit::Inch { 3 } else { 2 }),
    );
    if response.changed() {
        *value = unit.to_mm(shown);
    }
    response.changed()
}

fn layout(ui: &mut egui::Ui, object: &mut LabelObject, unit: Unit, sized: bool) {
    pair(
        ui,
        |ui| field(ui, &with_unit("editor.properties.x", unit), |ui| _ = length(ui, &mut object.x, unit, -1000.0)),
        |ui| field(ui, &with_unit("editor.properties.y", unit), |ui| _ = length(ui, &mut object.y, unit, -1000.0)),
    );
    if sized {
        let (mut width, mut height) = (object.w(), object.h());
        pair(
            ui,
            |ui| {
                field(ui, &with_unit("editor.properties.width", unit), |ui| {
                    if length(ui, &mut width, unit, 1.0) {
                        object.width = Some(width);
                    }
                })
            },
            |ui| {
                field(ui, &with_unit("editor.properties.height", unit), |ui| {
                    if length(ui, &mut height, unit, 1.0) {
                        object.height = Some(height);
                    }
                })
            },
        );
    }
    pair(
        ui,
        |ui| {
            field(ui, &t("editor.properties.rotation"), |ui| {
                ui.add_sized(
                    egui::vec2(ui.available_width(), 38.0),
                    egui::DragValue::new(&mut object.rotation).speed(1.0).range(-360.0..=360.0),
                );
            })
        },
        |_| {},
    );
}

fn options(ui: &mut egui::Ui, object: &mut LabelObject) {
    // Wraps in a narrow section rather than widening it.
    ui.horizontal_wrapped(|ui| {
        ui.spacing_mut().item_spacing.x = 14.0;
        ui.checkbox(&mut object.locked, t("editor.properties.locked"));
        ui.checkbox(&mut object.hidden, t("editor.properties.hidden"));
    });
}

fn color(ui: &mut egui::Ui, value: &mut Option<String>) {
    let mut chosen = parse_color(value.as_deref()).unwrap_or(Color32::BLACK);
    let button = egui::Frame::new()
        .fill(theme::palette(ui.ctx()).elevated)
        .stroke(egui::Stroke::new(1.0_f32, theme::palette(ui.ctx()).input_border))
        .corner_radius(egui::CornerRadius::same(8))
        .inner_margin(egui::Margin::same(4));
    button.show(ui, |ui| {
        ui.spacing_mut().interact_size = egui::vec2(40.0, 24.0);
        if ui.color_edit_button_srgba(&mut chosen).changed() {
            *value = Some(color_hex(chosen));
        }
    });
}

/// Fill: a colour, or none.
fn fill(ui: &mut egui::Ui, value: &mut Option<String>) {
    ui.horizontal(|ui| {
        let mut filled = parse_color(value.as_deref()).is_some();
        if ui.checkbox(&mut filled, "").changed() {
            *value = Some(if filled { "#000000".into() } else { "transparent".into() });
        }
        if parse_color(value.as_deref()).is_some() {
            color(ui, value);
        }
    });
}

fn font(ui: &mut egui::Ui, object: &mut LabelObject, c: &mut Context) {
    field(ui, &t("editor.properties.fontFamily"), |ui| {
        edit(&mut object.font_family, "Arial".to_string(), |family| {
            egui::ComboBox::from_id_salt("font-family").width(ui.available_width()).selected_text(family.clone()).show_ui(ui, |ui| {
                crate::ui::widgets::compact_menu(ui);
                if !c.families.contains(family) {
                    ui.selectable_value(family, family.clone(), tf("editor.properties.fontNotInstalled", &[("font", family)]));
                }
                for name in c.families {
                    ui.selectable_value(family, name.clone(), name);
                }
            });
        });
    });
    widgets::muted_small(ui, "Aa Bb Cc 123");

    pair(
        ui,
        |ui| {
            field(ui, &t("editor.properties.fontSize"), |ui| {
                edit(&mut object.font_size, 12.0, |size| {
                    ui.add_sized(egui::vec2(ui.available_width(), 38.0), egui::DragValue::new(size).speed(0.5).range(1.0..=400.0));
                });
            })
        },
        |ui| {
            field(ui, &t("editor.properties.align"), |ui| {
                let shown = |value: &str| {
                    t(match value {
                        "center" => "editor.properties.alignCenter",
                        "right" => "editor.properties.alignRight",
                        _ => "editor.properties.alignLeft",
                    })
                };
                edit(&mut object.align, "left".to_string(), |align| {
                    egui::ComboBox::from_id_salt("align").width(ui.available_width()).selected_text(shown(align)).show_ui(ui, |ui| {
                        crate::ui::widgets::compact_menu(ui);
                        for value in ["left", "center", "right"] {
                            ui.selectable_value(align, value.to_string(), shown(value));
                        }
                    });
                });
            })
        },
    );

    let mut bold = object.bold();
    pair(
        ui,
        |ui| field(ui, &t("editor.properties.color"), |ui| color(ui, &mut object.fill)),
        |ui| {
            ui.add_space(24.0);
            if ui.checkbox(&mut bold, t("editor.properties.bold")).changed() {
                object.font_weight = Some(if bold { "bold".into() } else { "normal".into() });
            }
        },
    );
}

fn fit(ui: &mut egui::Ui, object: &mut LabelObject) {
    let current = object.fit.clone().unwrap_or_else(|| "none".into());
    let mut fixed = current != "none";
    if ui.checkbox(&mut fixed, t("editor.properties.fixedSize")).changed() {
        object.fit = Some(if fixed { "shrink".into() } else { "none".into() });
    }
    if fixed {
        let mode = object.fit.get_or_insert_with(|| "shrink".into());
        ui.horizontal_wrapped(|ui| {
            ui.radio_value(mode, "shrink".to_string(), t("editor.properties.fitShrink"));
            ui.radio_value(mode, "wrap".to_string(), t("editor.properties.fitWrap"));
        });
    }
    let hint = match object.fit.as_deref() {
        Some("shrink") => "editor.properties.fitShrinkHint",
        Some("wrap") => "editor.properties.fitWrapHint",
        _ => "editor.properties.fixedSizeHint",
    };
    widgets::muted_small(ui, &t(hint));
    ui.separator();
    options(ui, object);
}

fn barcode_content(ui: &mut egui::Ui, object: &mut LabelObject, c: &mut Context) {
    field(ui, &t("editor.properties.barcodeType"), |ui| {
        edit(&mut object.symbology, "code128".to_string(), |symbology| {
            egui::ComboBox::from_id_salt("symbology")
                .width(ui.available_width())
                .selected_text(t(&format!("editor.barcode.{symbology}")))
                .show_ui(ui, |ui| {
                    crate::ui::widgets::compact_menu(ui);
                    for kind in SYMBOLOGIES {
                        ui.selectable_value(symbology, kind.to_string(), t(&format!("editor.barcode.{kind}")));
                    }
                });
        });
    });

    let mut name_value = object.field_name.clone().unwrap_or_default();
    let name_before = name_value.clone();
    let field_name = &mut name_value;
    let mut from_field = !field_name.is_empty();
    field(ui, &t("editor.properties.barcodeSource"), |ui| {
        let options = [t("editor.properties.barcodeSourceFixed"), t("editor.properties.barcodeSourceField")];
        if let Some(chosen) = widgets::segmented(ui, &options, usize::from(from_field)) {
            let wanted = chosen == 1;
            if wanted != from_field {
                from_field = wanted;
                *field_name = if from_field { c.field_names.first().cloned().unwrap_or_else(|| "code".into()) } else { String::new() };
            }
        }
    });
    if from_field {
        field(ui, &t("editor.properties.fieldName"), |ui| text(ui, field_name));
        if !c.field_names.is_empty() {
            ui.horizontal_wrapped(|ui| {
                for name in &c.field_names {
                    if ui.add(PillButton::new(name, ButtonKind::Secondary).small()).clicked() {
                        *field_name = name.clone();
                    }
                }
            });
        }
    }

    if name_value != name_before {
        object.field_name = Some(name_value);
    }

    let value_label = t(if from_field { "editor.properties.defaultValue" } else { "editor.properties.barcodeValue" });
    field(ui, &value_label, |ui| edit(&mut object.data, String::new(), |value| text(ui, value)));

    let symbology = object.symbology.clone().unwrap_or_default();
    let data = object.data.clone().unwrap_or_default();
    if let Some(Err(message)) = c.barcodes.get(c.ctx, c.api, &symbology, &data) {
        widgets::error_text(ui, message);
    }
    if from_field {
        widgets::muted_small(ui, &t("editor.properties.barcodeFieldHint"));
    }
}
