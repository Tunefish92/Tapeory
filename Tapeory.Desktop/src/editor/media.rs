//! Brother's official label media (Tapeory.Web/src/editor/brotherMedia.ts). "Height" is the size
//! across the tape or roll; die-cut labels also fix the length (width).

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum MediaGroup {
    Tze,
    Hse,
    DkContinuous,
    DkDieCut,
}

impl MediaGroup {
    pub const ALL: [MediaGroup; 4] = [MediaGroup::Tze, MediaGroup::Hse, MediaGroup::DkContinuous, MediaGroup::DkDieCut];

    pub fn key(self) -> &'static str {
        match self {
            MediaGroup::Tze => "editor.mediaGroup.tze",
            MediaGroup::Hse => "editor.mediaGroup.hse",
            MediaGroup::DkContinuous => "editor.mediaGroup.dkContinuous",
            MediaGroup::DkDieCut => "editor.mediaGroup.dkDieCut",
        }
    }
}

#[derive(Clone, Debug)]
pub struct MediaPreset {
    pub id: String,
    pub group: MediaGroup,
    pub code: Option<&'static str>,
    pub height_mm: f64,
    pub width_mm: Option<f64>,
    pub round: bool,
}

fn tze(height: f64) -> MediaPreset {
    MediaPreset { id: format!("tze-{height}"), group: MediaGroup::Tze, code: None, height_mm: height, width_mm: None, round: false }
}

fn hse(code: &'static str, height: f64) -> MediaPreset {
    MediaPreset { id: code.into(), group: MediaGroup::Hse, code: Some(code), height_mm: height, width_mm: None, round: false }
}

fn dk(code: &'static str, height: f64) -> MediaPreset {
    MediaPreset { id: code.into(), group: MediaGroup::DkContinuous, code: Some(code), height_mm: height, width_mm: None, round: false }
}

fn die_cut(code: Option<&'static str>, height: f64, width: f64, round: bool) -> MediaPreset {
    MediaPreset {
        id: code.map(str::to_string).unwrap_or_else(|| format!("dk-{height}x{width}")),
        group: MediaGroup::DkDieCut,
        code,
        height_mm: height,
        width_mm: Some(width),
        round,
    }
}

pub fn presets() -> Vec<MediaPreset> {
    vec![
        tze(3.5),
        tze(6.0),
        tze(9.0),
        tze(12.0),
        tze(18.0),
        tze(24.0),
        tze(36.0),
        hse("HSe-211E", 5.2),
        hse("HSe-221E", 9.0),
        hse("HSe-231E", 11.2),
        hse("HSe-241E", 17.7),
        hse("HSe-251E", 21.0),
        hse("HSe-261E", 31.0),
        hse("HSe-211", 5.8),
        hse("HSe-221", 8.8),
        hse("HSe-231", 11.7),
        hse("HSe-241", 17.7),
        hse("HSe-251", 23.6),
        dk("DK-22214", 12.0),
        dk("DK-22210", 29.0),
        dk("DK-22225", 38.0),
        dk("DK-22223", 50.0),
        dk("DK-N55224", 54.0),
        dk("DK-22205", 62.0),
        dk("DK-22251", 62.0),
        dk("DK-22243", 102.0),
        dk("DK-22246", 103.6),
        die_cut(Some("DK-11219"), 12.0, 12.0, true),
        die_cut(Some("DK-11204"), 17.0, 54.0, false),
        die_cut(Some("DK-11203"), 17.0, 87.0, false),
        die_cut(Some("DK-11221"), 23.0, 23.0, false),
        die_cut(Some("DK-11218"), 24.0, 24.0, true),
        die_cut(Some("DK-11209"), 62.0, 29.0, false),
        die_cut(Some("DK-11201"), 29.0, 90.0, false),
        die_cut(Some("DK-11208"), 38.0, 90.0, false),
        die_cut(None, 29.0, 42.0, false),
        die_cut(None, 39.0, 48.0, false),
        die_cut(None, 52.0, 29.0, false),
        die_cut(None, 54.0, 29.0, false),
        die_cut(Some("DK-11207"), 58.0, 58.0, true),
        die_cut(Some("DK-11234"), 60.0, 86.0, false),
        die_cut(Some("DK-11202"), 62.0, 100.0, false),
        die_cut(Some("DK-11240"), 102.0, 50.0, false),
        die_cut(Some("DK-11241"), 102.0, 152.0, false),
        die_cut(Some("DK-11247"), 103.0, 164.0, false),
    ]
}

fn same(a: f64, b: f64) -> bool {
    (a - b).abs() < 0.05
}

fn matches(preset: &MediaPreset, width: f64, height: f64) -> bool {
    same(preset.height_mm, height) && preset.width_mm.is_none_or(|w| same(w, width))
}

/// The preset a label uses: the remembered one if its size still matches, else the first official
/// medium of that size (die-cut first). None for a non-standard size.
pub fn find(width: f64, height: f64, media: Option<&str>) -> Option<MediaPreset> {
    let all = presets();

    if let Some(remembered) = media.and_then(|id| all.iter().find(|preset| preset.id == id))
        && matches(remembered, width, height)
    {
        return Some(remembered.clone());
    }

    all.iter()
        .find(|preset| preset.width_mm.is_some() && matches(preset, width, height))
        .or_else(|| all.iter().find(|preset| preset.width_mm.is_none() && matches(preset, width, height)))
        .cloned()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn finds_presets_like_the_web_editor() {
        assert_eq!(find(62.0, 29.0, None).unwrap().id, "DK-22210");
        assert_eq!(find(29.0, 62.0, None).unwrap().id, "DK-11209");
        assert_eq!(find(62.0, 29.0, Some("DK-22210")).unwrap().id, "DK-22210");
        assert_eq!(find(100.0, 62.0, None).unwrap().id, "DK-11202");
        assert_eq!(find(50.0, 12.0, Some("tze-12")).unwrap().id, "tze-12");
        assert!(find(50.0, 13.0, None).is_none());
    }
}
