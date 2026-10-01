//! A label's design: the same JSON document the web editor saves (Tapeory.Web/src/editor/types.ts),
//! in millimetres, with font sizes in points.

use serde::{Deserialize, Serialize};

use crate::models::TemplateField;

pub const FORMAT_VERSION: i32 = 1;

/// Font sizes are in points; the canvas works in mm.
pub const PT_TO_MM: f64 = 0.352778;

fn one() -> i32 {
    FORMAT_VERSION
}

#[derive(Serialize, Deserialize, Clone, PartialEq, Debug)]
#[serde(rename_all = "camelCase")]
pub struct LabelDocument {
    #[serde(default = "one")]
    pub format_version: i32,
    pub width_mm: f64,
    pub height_mm: f64,
    /// The Brother medium chosen in the editor (see media.rs).
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub media: Option<String>,
    #[serde(default)]
    pub objects: Vec<LabelObject>,
}

#[derive(Serialize, Deserialize, Clone, Copy, PartialEq, Eq, Debug)]
#[serde(rename_all = "camelCase")]
pub enum Kind {
    Text,
    DynamicField,
    Rect,
    Line,
    Ellipse,
    Barcode,
    Image,
    /// A type this version doesn't know: kept as it is, not drawn.
    #[serde(other)]
    Unknown,
}

/// One object on the label. The fields a kind doesn't use stay None and aren't saved.
#[derive(Serialize, Deserialize, Clone, PartialEq, Debug)]
#[serde(rename_all = "camelCase")]
pub struct LabelObject {
    #[serde(default)]
    pub id: String,
    #[serde(rename = "type")]
    pub kind: Kind,
    #[serde(default)]
    pub x: f64,
    #[serde(default)]
    pub y: f64,
    #[serde(default)]
    pub rotation: f64,
    #[serde(default)]
    pub locked: bool,
    #[serde(default)]
    pub hidden: bool,

    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub width: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub height: Option<f64>,

    // Text and dynamic fields.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub text: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub font_size: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub font_family: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub font_weight: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub align: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub fill: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub fit: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub field_name: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub label: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub default_value: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub required: Option<bool>,

    // Shapes and lines.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub stroke: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub stroke_width: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub corner_radius: Option<f64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub points: Option<[f64; 4]>,

    // Images.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub uploaded_file_id: Option<i64>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub url: Option<String>,

    // Barcodes.
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub symbology: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub data: Option<String>,
    #[serde(default, skip_serializing_if = "Option::is_none")]
    pub show_text: Option<bool>,
}

pub const SYMBOLOGIES: [&str; 12] =
    ["code128", "code39", "ean13", "ean8", "upca", "upce", "itf", "codabar", "qr", "datamatrix", "pdf417", "aztec"];

pub fn two_dimensional(symbology: &str) -> bool {
    matches!(symbology, "qr" | "datamatrix" | "pdf417" | "aztec")
}

pub fn new_id() -> String {
    format!("obj-{}", uuid::Uuid::new_v4().simple())
}

impl LabelObject {
    fn blank(kind: Kind) -> LabelObject {
        LabelObject {
            id: new_id(),
            kind,
            x: 5.0,
            y: 5.0,
            rotation: 0.0,
            locked: false,
            hidden: false,
            width: None,
            height: None,
            text: None,
            font_size: None,
            font_family: None,
            font_weight: None,
            align: None,
            fill: None,
            fit: None,
            field_name: None,
            label: None,
            default_value: None,
            required: None,
            stroke: None,
            stroke_width: None,
            corner_radius: None,
            points: None,
            uploaded_file_id: None,
            url: None,
            symbology: None,
            data: None,
            show_text: None,
        }
    }

    fn text_style(mut self, fit: &str) -> LabelObject {
        self.width = Some(30.0);
        self.height = Some(8.0);
        self.font_size = Some(12.0);
        self.font_family = Some("Arial".into());
        self.font_weight = Some("normal".into());
        self.align = Some("left".into());
        self.fill = Some("#000000".into());
        self.fit = Some(fit.into());
        self
    }

    pub fn new_text() -> LabelObject {
        let mut object = LabelObject::blank(Kind::Text).text_style("none");
        object.text = Some("Text".into());
        object
    }

    pub fn new_field(name: &str) -> LabelObject {
        // Values arrive at print time and can be any length, so fields keep to their box.
        let mut object = LabelObject::blank(Kind::DynamicField).text_style("shrink");
        object.field_name = Some(name.into());
        object.label = Some("Field".into());
        object.default_value = Some(String::new());
        object.required = Some(true);
        object
    }

    pub fn new_rect() -> LabelObject {
        let mut object = LabelObject::blank(Kind::Rect);
        object.width = Some(20.0);
        object.height = Some(12.0);
        object.fill = Some("transparent".into());
        object.stroke = Some("#000000".into());
        object.stroke_width = Some(0.5);
        object.corner_radius = Some(0.0);
        object
    }

    pub fn new_ellipse() -> LabelObject {
        let mut object = LabelObject::blank(Kind::Ellipse);
        object.width = Some(12.0);
        object.height = Some(12.0);
        object.fill = Some("transparent".into());
        object.stroke = Some("#000000".into());
        object.stroke_width = Some(0.5);
        object
    }

    pub fn new_line() -> LabelObject {
        let mut object = LabelObject::blank(Kind::Line);
        object.points = Some([0.0, 0.0, 20.0, 0.0]);
        object.stroke = Some("#000000".into());
        object.stroke_width = Some(0.5);
        object
    }

    pub fn new_barcode() -> LabelObject {
        let mut object = LabelObject::blank(Kind::Barcode);
        object.x = 2.0;
        object.y = 1.0;
        object.width = Some(30.0);
        object.height = Some(8.0);
        object.symbology = Some("code128".into());
        object.data = Some("12345678".into());
        object.field_name = Some(String::new());
        object.show_text = Some(true);
        object.fill = Some("#000000".into());
        object
    }

    pub fn new_image(uploaded_file_id: i64, url: &str, width: f64, height: f64) -> LabelObject {
        let mut object = LabelObject::blank(Kind::Image);
        object.uploaded_file_id = Some(uploaded_file_id);
        object.url = Some(url.into());
        object.width = Some(width);
        object.height = Some(height);
        object
    }

    /// Moves and shrinks a new object so it lies inside a label of this size: the default boxes
    /// are higher than a narrow tape. Round shapes and images keep their proportions.
    pub fn fit_into(&mut self, label_width: f64, label_height: f64) {
        const EDGE: f64 = 1.0;
        let (room_x, room_y) = ((label_width - 2.0 * EDGE).max(1.0), (label_height - 2.0 * EDGE).max(1.0));

        if let Some([x1, y1, x2, _]) = self.points {
            let length = (x2 - x1).abs().min(room_x);
            self.points = Some([x1, y1, x1 + length, y1]);
            self.x = self.x.min(label_width - EDGE - length).max(EDGE);
            self.y = self.y.min(label_height / 2.0);
            return;
        }

        let (Some(width), Some(height)) = (self.width, self.height) else { return };
        let (width, height) = if matches!(self.kind, Kind::Ellipse | Kind::Image) {
            let scale = (room_x / width).min(room_y / height).min(1.0);
            (width * scale, height * scale)
        } else {
            (width.min(room_x), height.min(room_y))
        };
        self.width = Some(width);
        self.height = Some(height);
        self.x = self.x.min(label_width - EDGE - width).max(EDGE);
        self.y = self.y.min(label_height - EDGE - height).max(EDGE);
    }

    pub fn w(&self) -> f64 {
        self.width.unwrap_or(0.0)
    }

    pub fn h(&self) -> f64 {
        self.height.unwrap_or(0.0)
    }

    pub fn bold(&self) -> bool {
        self.font_weight.as_deref() == Some("bold")
    }

    /// The object's box in its own (unrotated) frame: lines span their points.
    pub fn local_box(&self) -> (f64, f64, f64, f64) {
        match (self.kind, self.points) {
            (Kind::Line, Some([x1, y1, x2, y2])) => (x1.min(x2), y1.min(y2), x1.max(x2), y1.max(y2)),
            _ => (0.0, 0.0, self.w(), self.h()),
        }
    }
}

impl LabelDocument {
    pub fn empty(width_mm: f64, height_mm: f64) -> LabelDocument {
        LabelDocument { format_version: FORMAT_VERSION, width_mm, height_mm, media: None, objects: Vec::new() }
    }

    /// Parses a saved document, giving every object a unique id (imported .lbx documents come
    /// from the engine, which doesn't need them).
    pub fn parse(json: &str) -> Result<LabelDocument, String> {
        let mut document: LabelDocument = serde_json::from_str(json).map_err(|e| e.to_string())?;
        let mut seen = std::collections::HashSet::new();
        for object in &mut document.objects {
            if object.id.is_empty() || !seen.insert(object.id.clone()) {
                object.id = new_id();
                seen.insert(object.id.clone());
            }
        }
        Ok(document)
    }

    pub fn to_json(&self) -> String {
        serde_json::to_string(self).unwrap_or_default()
    }

    pub fn find(&self, id: &str) -> Option<&LabelObject> {
        self.objects.iter().find(|object| object.id == id)
    }

    pub fn find_mut(&mut self, id: &str) -> Option<&mut LabelObject> {
        self.objects.iter_mut().find(|object| object.id == id)
    }

    pub fn remove(&mut self, id: &str) {
        self.objects.retain(|object| object.id != id);
    }

    /// A copy, 5 mm down and right; returns its id.
    pub fn duplicate(&mut self, id: &str) -> Option<String> {
        let mut copy = self.find(id)?.clone();
        copy.id = new_id();
        copy.x += 5.0;
        copy.y += 5.0;
        let new = copy.id.clone();
        self.objects.push(copy);
        Some(new)
    }

    pub fn reorder(&mut self, id: &str, direction: Reorder) {
        let Some(index) = self.objects.iter().position(|object| object.id == id) else { return };
        let object = self.objects.remove(index);
        let target = match direction {
            Reorder::Front => self.objects.len(),
            Reorder::Back => 0,
            Reorder::Forward => (index + 1).min(self.objects.len()),
            Reorder::Backward => index.saturating_sub(1),
        };
        self.objects.insert(target, object);
    }

    /// The template's fields: every dynamic field, plus fields only a barcode uses, by name.
    /// A barcode sharing a dynamic field's name uses that field (label and default included).
    pub fn fields(&self) -> Vec<TemplateField> {
        let mut fields: Vec<TemplateField> = Vec::new();

        for object in self.objects.iter().filter(|o| o.kind == Kind::DynamicField) {
            let name = object.field_name.clone().unwrap_or_default();
            if !fields.iter().any(|field| field.name == name) {
                fields.push(TemplateField {
                    name,
                    label: object.label.clone().filter(|label| !label.is_empty()),
                    default_value: object.default_value.clone().filter(|value| !value.is_empty()),
                    required: object.required.unwrap_or(false),
                });
            }
        }

        for object in self.objects.iter().filter(|o| o.kind == Kind::Barcode) {
            let name = object.field_name.clone().unwrap_or_default().trim().to_string();
            if !name.is_empty() && !fields.iter().any(|field| field.name == name) {
                fields.push(TemplateField {
                    name,
                    label: None,
                    default_value: object.data.clone().filter(|value| !value.is_empty()),
                    required: false,
                });
            }
        }

        fields
    }
}

#[derive(Clone, Copy)]
pub enum Reorder {
    Front,
    Back,
    Forward,
    Backward,
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn reads_and_writes_the_web_editors_documents() {
        let json = r##"{"formatVersion":1,"widthMm":62,"heightMm":29,"media":"DK-22210","objects":[
            {"id":"a","type":"text","x":1,"y":2,"rotation":0,"locked":false,"hidden":false,"text":"Hi","width":30,"height":8,
             "fontSize":12,"fontFamily":"Inter","fontWeight":"bold","align":"left","fill":"#000000","fit":"none"},
            {"type":"line","x":0,"y":0,"points":[0,0,20,0],"stroke":"#000000","strokeWidth":0.5},
            {"id":"a","type":"futureThing","x":0,"y":0}]}"##;

        let document = LabelDocument::parse(json).unwrap();

        assert_eq!(document.objects.len(), 3);
        assert!(document.objects[0].bold());
        assert_ne!(document.objects[1].id, "");
        assert_ne!(document.objects[2].id, "a", "duplicate ids are replaced");
        assert_eq!(document.objects[2].kind, Kind::Unknown);

        let again = LabelDocument::parse(&document.to_json()).unwrap();
        assert_eq!(again.objects[0].text.as_deref(), Some("Hi"));
        assert!(!document.to_json().contains("uploadedFileId"));
    }

    #[test]
    fn new_objects_are_placed_inside_a_narrow_label() {
        // 9 mm tape, 40 mm long: every default box is higher than that.
        for mut object in [
            LabelObject::new_text(),
            LabelObject::new_field("name"),
            LabelObject::new_rect(),
            LabelObject::new_ellipse(),
            LabelObject::new_barcode(),
            LabelObject::new_image(1, "/x", 20.0, 20.0),
        ] {
            object.fit_into(40.0, 9.0);
            assert!(object.x >= 1.0 && object.x + object.w() <= 39.0, "{:?} across", object.kind);
            assert!(object.y >= 1.0 && object.y + object.h() <= 8.0, "{:?} down", object.kind);
        }

        let mut ellipse = LabelObject::new_ellipse();
        ellipse.fit_into(40.0, 9.0);
        assert_eq!(ellipse.w(), ellipse.h(), "a circle stays round");

        let mut line = LabelObject::new_line();
        line.fit_into(12.0, 9.0);
        let [x1, _, x2, _] = line.points.unwrap();
        assert!(line.x + (x2 - x1) <= 11.0 && line.y <= 4.5);

        // A label with room for the defaults keeps them.
        let mut text = LabelObject::new_text();
        text.fit_into(62.0, 29.0);
        assert_eq!((text.x, text.y, text.w(), text.h()), (5.0, 5.0, 30.0, 8.0));
    }

    #[test]
    fn collects_fields_from_dynamic_fields_and_barcodes() {
        let mut document = LabelDocument::empty(62.0, 29.0);
        let mut field = LabelObject::new_field("name");
        field.label = Some("Name".into());
        document.objects.push(field);
        let mut barcode = LabelObject::new_barcode();
        barcode.field_name = Some("sku".into());
        barcode.data = Some("123".into());
        document.objects.push(barcode);
        let mut shared = LabelObject::new_barcode();
        shared.field_name = Some("name".into());
        document.objects.push(shared);

        let fields = document.fields();

        assert_eq!(fields.len(), 2);
        assert_eq!(fields[0].label.as_deref(), Some("Name"));
        assert_eq!(fields[1].name, "sku");
        assert_eq!(fields[1].default_value.as_deref(), Some("123"));
    }

    #[test]
    fn reorders_duplicates_and_removes() {
        let mut document = LabelDocument::empty(62.0, 29.0);
        document.objects.push(LabelObject::new_rect());
        document.objects.push(LabelObject::new_ellipse());
        let first = document.objects[0].id.clone();

        document.reorder(&first, Reorder::Front);
        assert_eq!(document.objects[1].id, first);

        let copy = document.duplicate(&first).unwrap();
        assert_eq!(document.find(&copy).unwrap().x, 10.0);

        document.remove(&first);
        assert!(document.find(&first).is_none());
    }
}
