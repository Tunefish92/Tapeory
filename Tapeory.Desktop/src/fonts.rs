//! Fonts: the interface uses Inter (one of Tapeory's bundled fonts, built into the app), and the
//! label editor loads each font a label uses from the engine, the same files the printed label is
//! rendered with, so the canvas shows what prints.

use std::collections::HashSet;
use std::sync::Arc;

use eframe::egui::{self, FontData, FontDefinitions, FontFamily};

use crate::api::Api;
use crate::task::Task;

pub const UI_FONT: &str = "Inter";
pub const UI_FONT_BOLD: &str = "Inter Bold";
/// The interface's other faces, as in the web UI: Inter for text, Space Grotesk for headings,
/// buttons and navigation. Only the interface uses these names; labels can't pick them.
pub const BODY_MEDIUM: &str = "ui:Inter Medium";
pub const BODY_SEMIBOLD: &str = "ui:Inter SemiBold";
pub const HEADING_MEDIUM: &str = "ui:Space Grotesk Medium";
pub const HEADING: &str = "ui:Space Grotesk SemiBold";
pub const HEADING_BOLD: &str = "ui:Space Grotesk Bold";

const INTERFACE_FACES: [(&str, &[u8]); 7] = [
    (UI_FONT, include_bytes!("../../Tapeory.Api/Fonts/Inter/Inter-Regular.otf")),
    (UI_FONT_BOLD, include_bytes!("../../Tapeory.Api/Fonts/Inter/Inter-Bold.otf")),
    (BODY_MEDIUM, include_bytes!("../assets/fonts/Inter-Medium.ttf")),
    (BODY_SEMIBOLD, include_bytes!("../assets/fonts/Inter-SemiBold.ttf")),
    (HEADING_MEDIUM, include_bytes!("../assets/fonts/SpaceGrotesk-Medium.ttf")),
    (HEADING, include_bytes!("../assets/fonts/SpaceGrotesk-SemiBold.ttf")),
    (HEADING_BOLD, include_bytes!("../assets/fonts/SpaceGrotesk-Bold.ttf")),
];

/// An interface face by name, e.g. `face(HEADING)`.
pub fn face(name: &str) -> FontFamily {
    FontFamily::Name(name.into())
}

/// A font family the editor can draw with, loaded or not.
#[derive(Default)]
pub struct Fonts {
    definitions: Option<FontDefinitions>,
    /// Family names (with " Bold" for bold faces) registered with egui.
    registered: HashSet<String>,
    /// Asked for, and loading or failed: not asked again.
    requested: HashSet<String>,
    loading: Vec<Task<(String, Option<Vec<u8>>)>>,
    /// Handed to egui this frame: usable from the next one, when egui has applied them.
    pending: Vec<String>,
    /// Every family the engine can print with.
    pub families: Vec<String>,
    families_task: Option<Task<Vec<String>>>,
    /// When the list was last asked for, to retry an empty one (e.g. before signing in).
    families_asked: Option<std::time::Instant>,
}

impl Fonts {
    /// The interface fonts from the first frame; Inter is also the canvas's "Inter" family.
    pub fn install_interface(&mut self, ctx: &egui::Context) {
        let mut definitions = FontDefinitions::default();

        for (name, bytes) in INTERFACE_FACES {
            definitions.font_data.insert(name.into(), Arc::new(FontData::from_static(bytes)));
            let mut faces = definitions.families.get(&FontFamily::Proportional).cloned().unwrap_or_default();
            faces.insert(0, name.into());
            definitions.families.insert(FontFamily::Name(name.into()), faces);
            self.registered.insert(name.into());
            self.requested.insert(name.into());
        }

        if let Some(proportional) = definitions.families.get_mut(&FontFamily::Proportional) {
            proportional.insert(0, UI_FONT.into());
        }

        ctx.set_fonts(definitions.clone());
        self.definitions = Some(definitions);
    }

    /// Starts loading the list of families the engine can print with.
    pub fn start(&mut self, ctx: &egui::Context, api: &Api) {
        let api = api.clone();
        self.families_asked = Some(std::time::Instant::now());
        self.families_task = Some(Task::spawn(ctx, move || api.fonts().unwrap_or_default()));
    }

    /// Loads the list again while it's empty (at most every few seconds): it may have been asked
    /// for before signing in, when the engine refuses it.
    pub fn ensure_families(&mut self, ctx: &egui::Context, api: &Api) {
        let due = self.families_asked.is_none_or(|asked| asked.elapsed() > std::time::Duration::from_secs(5));
        if self.families.is_empty() && self.families_task.is_none() && due {
            self.start(ctx, api);
        }
    }

    /// Called every frame: installs fonts that have arrived.
    pub fn poll(&mut self, ctx: &egui::Context) {
        // egui applies new fonts at the start of a frame: the ones handed over last frame are
        // there now. Drawing with a family egui doesn't have yet would panic.
        self.registered.extend(self.pending.drain(..));

        if let Some(families) = self.families_task.as_mut().and_then(Task::take) {
            self.families = families;
            self.families_task = None;
        }

        let mut arrived = Vec::new();
        self.loading.retain_mut(|task| match task.take() {
            Some(result) => {
                arrived.push(result);
                false
            }
            None => true,
        });

        if arrived.is_empty() {
            return;
        }

        let definitions = self.definitions.get_or_insert_with(FontDefinitions::default);

        for (name, bytes) in arrived {
            let Some(bytes) = bytes else { continue };
            definitions.font_data.insert(name.clone(), Arc::new(FontData::from_owned(bytes)));

            // The face itself, then egui's own fonts for characters it lacks.
            let mut faces = definitions.families.get(&FontFamily::Proportional).cloned().unwrap_or_default();
            faces.retain(|face| face != &name);
            faces.insert(0, name.clone());
            definitions.families.insert(FontFamily::Name(name.clone().into()), faces);

            self.pending.push(name);
        }

        ctx.set_fonts(definitions.clone());
        ctx.request_repaint();
    }

    /// The egui family to draw `family` with (bold if asked and loaded), loading it if needed;
    /// the interface font until it has arrived.
    pub fn family(&mut self, ctx: &egui::Context, api: &Api, family: &str, bold: bool) -> FontFamily {
        let wanted = face_name(family, bold);

        if self.registered.contains(&wanted) {
            return FontFamily::Name(wanted.into());
        }

        self.request(ctx, api, family, bold);

        let regular = face_name(family, false);
        if self.registered.contains(&regular) { FontFamily::Name(regular.into()) } else { FontFamily::Proportional }
    }

    fn request(&mut self, ctx: &egui::Context, api: &Api, family: &str, bold: bool) {
        let name = face_name(family, bold);
        if !self.requested.insert(name.clone()) {
            return;
        }

        let api = api.clone();
        let family = family.to_string();
        self.loading.push(Task::spawn(ctx, move || {
            let bytes = api.font_file(&family, bold).ok().filter(|bytes| is_font(bytes) && egui_can_use(bytes));
            (name, bytes)
        }));
    }
}

fn face_name(family: &str, bold: bool) -> String {
    if bold { format!("{family} Bold") } else { family.to_string() }
}

/// Whether egui can draw with the font: it parses and has a usable size (egui panics otherwise).
fn egui_can_use(bytes: &[u8]) -> bool {
    use ab_glyph::Font;
    ab_glyph::FontRef::try_from_slice_and_index(bytes, 0).is_ok_and(|font| font.units_per_em().is_some())
}

/// TrueType, OpenType or TrueType collection data (egui can't read WOFF).
fn is_font(bytes: &[u8]) -> bool {
    matches!(bytes.get(..4), Some([0, 1, 0, 0]) | Some(b"OTTO") | Some(b"true") | Some(b"ttcf"))
}
