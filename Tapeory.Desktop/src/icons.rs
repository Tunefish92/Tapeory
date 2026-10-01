//! The web UI's line icons (its inline SVGs), drawn in any colour: the paths are white and egui
//! tints them.

use eframe::egui::{self, Color32, Ui};

macro_rules! svg {
    ($width:literal, $paths:literal) => {
        concat!(
            r##"<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#ffffff" stroke-width=""##,
            $width,
            r##"" stroke-linecap="round" stroke-linejoin="round">"##,
            $paths,
            "</svg>"
        )
        .as_bytes()
    };
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Icon {
    Templates,
    Printer,
    Label,
    Check,
    Ruler,
    Pencil,
    Copy,
    Trash,
    Eye,
    ImportedFile,
    Tag,
    Search,
    Globe,
    Moon,
    Chart,
    Database,
    Users,
    Download,
    Refresh,
    Usb,
    Info,
    Logout,
    Key,
    Close,
    ArrowLeft,
}

impl Icon {
    fn svg(self) -> &'static [u8] {
        match self {
            Icon::Templates => svg!("1.8", r#"<rect x="4" y="3" width="16" height="18" rx="2"/><path d="M8 8h8M8 12h8M8 16h5"/>"#),
            Icon::Printer => {
                svg!("1.8", r#"<path d="M6 9V3h12v6"/><rect x="3" y="9" width="18" height="8" rx="2"/><path d="M7 14h10v7H7z"/>"#)
            }
            Icon::Label => svg!("1.8", r#"<path d="M3 5h11l7 7-7 7H3z"/><circle cx="8" cy="9" r="1.5"/>"#),
            Icon::Check => svg!("1.8", r#"<path d="M4 12l5 5L20 6"/>"#),
            Icon::Ruler => svg!("1.8", r#"<rect x="2" y="8" width="20" height="8" rx="1.5"/><path d="M6 8v3M10 8v4M14 8v3M18 8v4"/>"#),
            Icon::Pencil => svg!("2", r#"<path d="M12 20h9"/><path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4Z"/>"#),
            Icon::Copy => svg!(
                "2",
                r#"<rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1"/>"#
            ),
            Icon::Trash => svg!(
                "2",
                r#"<path d="M3 6h18"/><path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/>"#
            ),
            Icon::Eye => svg!("2", r#"<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12Z"/><circle cx="12" cy="12" r="3"/>"#),
            Icon::ImportedFile => svg!(
                "2",
                r#"<path d="M14 3H6a2 2 0 0 0-2 2v14a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V9z"/><path d="M14 3v6h6M12 18v-6M9 15l3 3 3-3"/>"#
            ),
            Icon::Tag => svg!(
                "1.8",
                r#"<path d="M20.6 13.4 13.4 20.6a2 2 0 0 1-2.8 0L3 13V3h10l7.6 7.6a2 2 0 0 1 0 2.8z"/><circle cx="7.5" cy="7.5" r="1.5"/>"#
            ),
            Icon::Search => svg!("2", r#"<circle cx="11" cy="11" r="7"/><path d="M20 20l-3.5-3.5"/>"#),
            Icon::Globe => {
                svg!("1.8", r#"<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3a14 14 0 0 1 0 18M12 3a14 14 0 0 0 0 18"/>"#)
            }
            Icon::Moon => svg!("1.8", r#"<path d="M21 12.8A9 9 0 1 1 11.2 3a7 7 0 0 0 9.8 9.8z"/>"#),
            Icon::Chart => svg!("1.8", r#"<path d="M4 20V10M10 20V4M16 20v-7M22 20H2"/>"#),
            Icon::Database => svg!(
                "1.8",
                r#"<ellipse cx="12" cy="5" rx="8" ry="3"/><path d="M4 5v14c0 1.7 3.6 3 8 3s8-1.3 8-3V5"/><path d="M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3"/>"#
            ),
            Icon::Users => svg!(
                "1.8",
                r#"<circle cx="9" cy="8" r="3.5"/><path d="M2.5 20c0-3.6 2.9-6 6.5-6s6.5 2.4 6.5 6"/><path d="M16 4.5a3.5 3.5 0 0 1 0 7M18 14c2.2.6 3.5 2.8 3.5 6"/>"#
            ),
            Icon::Download => svg!("2", r#"<path d="M12 4v11M7 10l5 5 5-5M5 20h14"/>"#),
            Icon::Refresh => svg!("2", r#"<path d="M20 11a8 8 0 1 0-2.3 5.7M20 4v7h-7"/>"#),
            Icon::Usb => svg!(
                "1.8",
                r#"<path d="M12 3v15"/><circle cx="12" cy="19.5" r="1.5"/><path d="M12 12 7 9V6M12 14l5-3V8"/><rect x="5.5" y="4" width="3" height="2"/><circle cx="17" cy="7" r="1"/>"#
            ),
            Icon::Info => svg!("2", r#"<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5h.01"/>"#),
            Icon::Logout => svg!("2", r#"<path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3M10 8l-4 4 4 4M6 12h11"/>"#),
            Icon::Key => svg!("2", r#"<circle cx="7.5" cy="15.5" r="4.5"/><path d="M10.7 12.3 20 3M16 7l3 3M14 9l2 2"/>"#),
            Icon::Close => svg!("2", r#"<path d="M6 6l12 12M18 6 6 18"/>"#),
            Icon::ArrowLeft => svg!("2", r#"<path d="M19 12H5M11 6l-6 6 6 6"/>"#),
        }
    }

    fn uri(self) -> String {
        format!("bytes://tapeory-icon-{self:?}.svg")
    }

    /// The icon as an image of `size` points in `color`.
    pub fn image(self, size: f32, color: Color32) -> egui::Image<'static> {
        egui::Image::from_bytes(self.uri(), self.svg()).fit_to_exact_size(egui::vec2(size, size)).tint(color)
    }
}

/// Draws an icon.
pub fn icon(ui: &mut Ui, icon: Icon, size: f32, color: Color32) -> egui::Response {
    ui.add(icon.image(size, color))
}

/// Paints an icon into a rectangle (for widgets that lay themselves out).
pub fn paint(ui: &Ui, icon: Icon, rect: egui::Rect, color: Color32) {
    icon.image(rect.width(), color).paint_at(ui, rect);
}
