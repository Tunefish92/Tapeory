//! Fitting text into its box, exactly as the engine's TextFitter.cs and the web editor's fitText.ts
//! do it, so the canvas shows the lines and size that print.

/// Line spacing as a multiple of the font size (same as the renderer).
pub const LINE_HEIGHT: f64 = 1.2;

/// Smallest size shrinking may go to: about 1 pt, in mm.
const MIN_FONT_SIZE_MM: f64 = 0.35;
const EPSILON: f64 = 0.001;

pub struct Fitted {
    pub font_size: f64,
    pub lines: Vec<String>,
}

/// Greedy word wrap; a word wider than the box gets its own line rather than being cut.
fn wrap(paragraph: &str, width: f64, size: f64, measure: &mut dyn FnMut(&str, f64) -> f64) -> Vec<String> {
    let words: Vec<&str> = paragraph.split(' ').filter(|word| !word.is_empty()).collect();
    let Some((first, rest)) = words.split_first() else { return vec![String::new()] };

    let mut lines = Vec::new();
    let mut current = first.to_string();

    for word in rest {
        let candidate = format!("{current} {word}");
        if measure(&candidate, size) <= width + EPSILON {
            current = candidate;
        } else {
            lines.push(std::mem::replace(&mut current, word.to_string()));
        }
    }

    lines.push(current);
    lines
}

/// `mode` is "none" (as typed, may overflow), "shrink" (smaller until it fits) or "wrap" (wrap at
/// spaces, shrink only if still needed). `measure(text, size)` gives the width at `size`.
pub fn fit(text: &str, width: f64, height: f64, font_size: f64, mode: &str, measure: &mut dyn FnMut(&str, f64) -> f64) -> Fitted {
    let paragraphs: Vec<String> = text.replace("\r\n", "\n").split('\n').map(str::to_string).collect();

    if !matches!(mode, "shrink" | "wrap") || width <= 0.0 || height <= 0.0 || font_size <= 0.0 {
        return Fitted { font_size, lines: paragraphs };
    }

    let wrapping = mode == "wrap";
    let layout = |size: f64, measure: &mut dyn FnMut(&str, f64) -> f64| -> Vec<String> {
        if wrapping { paragraphs.iter().flat_map(|p| wrap(p, width, size, &mut *measure)).collect() } else { paragraphs.clone() }
    };
    let fits = |lines: &[String], size: f64, measure: &mut dyn FnMut(&str, f64) -> f64| {
        lines.len() as f64 * size * LINE_HEIGHT <= height + EPSILON && lines.iter().all(|line| measure(line, size) <= width + EPSILON)
    };

    let full = layout(font_size, measure);
    if fits(&full, font_size, measure) {
        return Fitted { font_size, lines: full };
    }

    // Smaller text never needs more lines, so the largest size that fits is found by bisection.
    let mut low = MIN_FONT_SIZE_MM.min(font_size);
    let mut high = font_size;
    let mut best = layout(low, measure);

    for _ in 0..20 {
        let mid = (low + high) / 2.0;
        let lines = layout(mid, measure);
        if fits(&lines, mid, measure) {
            low = mid;
            best = lines;
        } else {
            high = mid;
        }
    }

    Fitted { font_size: low, lines: best }
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Every character is 0.5 × the font size wide.
    fn measure(text: &str, size: f64) -> f64 {
        text.chars().count() as f64 * size * 0.5
    }

    #[test]
    fn leaves_text_alone_without_a_fit_mode() {
        let fitted = fit("a\nb", 10.0, 10.0, 5.0, "none", &mut measure);
        assert_eq!(fitted.lines, vec!["a", "b"]);
        assert_eq!(fitted.font_size, 5.0);
    }

    #[test]
    fn shrinks_until_the_text_fits() {
        let fitted = fit("abcdefghij", 10.0, 10.0, 4.0, "shrink", &mut measure);
        assert!(fitted.font_size <= 2.0 + 1e-3);
        assert!(fitted.font_size > 1.9);
        assert_eq!(fitted.lines, vec!["abcdefghij"]);
    }

    #[test]
    fn wraps_at_spaces_before_shrinking() {
        let fitted = fit("aa bb cc", 5.0, 20.0, 2.0, "wrap", &mut measure);
        assert_eq!(fitted.lines, vec!["aa bb", "cc"]);
        assert_eq!(fitted.font_size, 2.0);
    }
}
