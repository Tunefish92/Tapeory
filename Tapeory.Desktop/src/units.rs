//! Lengths are stored in millimetres and shown in the unit chosen in Settings (mm or inches).

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
pub enum Unit {
    Mm,
    Inch,
}

impl Unit {
    pub fn from_setting(value: Option<&str>) -> Unit {
        if value == Some("inch") { Unit::Inch } else { Unit::Mm }
    }

    pub fn setting(self) -> &'static str {
        match self {
            Unit::Mm => "mm",
            Unit::Inch => "inch",
        }
    }

    pub fn symbol(self) -> &'static str {
        match self {
            Unit::Mm => "mm",
            Unit::Inch => "in",
        }
    }

    pub fn to_display(self, mm: f64) -> f64 {
        match self {
            Unit::Mm => mm,
            Unit::Inch => mm / 25.4,
        }
    }

    pub fn to_mm(self, value: f64) -> f64 {
        match self {
            Unit::Mm => value,
            Unit::Inch => value * 25.4,
        }
    }

    /// The step for number fields in this unit.
    pub fn step(self) -> f64 {
        match self {
            Unit::Mm => 0.5,
            Unit::Inch => 0.01,
        }
    }

    /// "12 mm", "0.47 in".
    pub fn dimension(self, mm: f64) -> String {
        format!("{} {}", self.number(mm), self.symbol())
    }

    pub fn number(self, mm: f64) -> String {
        let value = self.to_display(mm);
        let decimals = if self == Unit::Inch { 2 } else { 1 };
        let text = format!("{value:.decimals$}");
        text.trim_end_matches('0').trim_end_matches('.').to_string()
    }
}

/// Printed tape length, e.g. "84 cm" or "2.3 m" (or inches and feet).
pub fn format_length(mm: f64, unit: Unit) -> String {
    match unit {
        Unit::Mm if mm >= 1000.0 => format!("{:.1} m", mm / 1000.0),
        Unit::Mm if mm >= 10.0 => format!("{:.0} cm", mm / 10.0),
        Unit::Mm => format!("{mm:.0} mm"),
        Unit::Inch if mm >= 304.8 => format!("{:.1} ft", mm / 304.8),
        Unit::Inch => format!("{:.1} in", mm / 25.4),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn converts_and_formats() {
        assert_eq!(Unit::Mm.dimension(12.0), "12 mm");
        assert_eq!(Unit::Inch.number(25.4), "1");
        assert_eq!(format_length(840.0, Unit::Mm), "84 cm");
        assert_eq!(format_length(2300.0, Unit::Mm), "2.3 m");
        assert!((Unit::Inch.to_mm(1.0) - 25.4).abs() < 1e-9);
    }
}
