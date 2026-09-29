//! Positions on the label, in mm. Every object has an origin (x, y) at its top-left corner and
//! rotates clockwise around it, like the web editor (Konva) and the renderer.

use super::document::{Kind, LabelObject};

#[derive(Clone, Copy, PartialEq, Debug, Default)]
pub struct Point {
    pub x: f64,
    pub y: f64,
}

pub fn pt(x: f64, y: f64) -> Point {
    Point { x, y }
}

/// A point in the object's own frame → on the label.
pub fn to_label(object: &LabelObject, local: Point) -> Point {
    let (sin, cos) = object.rotation.to_radians().sin_cos();
    pt(object.x + local.x * cos - local.y * sin, object.y + local.x * sin + local.y * cos)
}

/// A point on the label → in the object's own frame.
pub fn to_local(object: &LabelObject, label: Point) -> Point {
    let (sin, cos) = object.rotation.to_radians().sin_cos();
    let (dx, dy) = (label.x - object.x, label.y - object.y);
    pt(dx * cos + dy * sin, -dx * sin + dy * cos)
}

/// Whether a point on the label is on the object (within `slop` mm of a line).
pub fn hit(object: &LabelObject, label: Point, slop: f64) -> bool {
    let local = to_local(object, label);

    match (object.kind, object.points) {
        (Kind::Line, Some([x1, y1, x2, y2])) => {
            distance_to_segment(local, pt(x1, y1), pt(x2, y2)) <= slop.max(object.stroke_width.unwrap_or(0.5))
        }
        _ => {
            let (left, top, right, bottom) = object.local_box();
            local.x >= left - slop && local.x <= right + slop && local.y >= top - slop && local.y <= bottom + slop
        }
    }
}

fn distance_to_segment(p: Point, a: Point, b: Point) -> f64 {
    let (dx, dy) = (b.x - a.x, b.y - a.y);
    let length = dx * dx + dy * dy;
    let t = if length == 0.0 { 0.0 } else { (((p.x - a.x) * dx + (p.y - a.y) * dy) / length).clamp(0.0, 1.0) };
    ((p.x - (a.x + t * dx)).powi(2) + (p.y - (a.y + t * dy)).powi(2)).sqrt()
}

/// A resize handle: which edges it moves (-1 = left/top, 1 = right/bottom, 0 = neither).
#[derive(Clone, Copy, PartialEq, Debug)]
pub enum Handle {
    Edge {
        horizontal: i8,
        vertical: i8,
    },
    /// One end of a line (0 or 1).
    LineEnd(usize),
    Rotate,
}

/// The handles of a selected object, with their positions in its own frame.
pub fn handles(object: &LabelObject) -> Vec<(Handle, Point)> {
    if let (Kind::Line, Some([x1, y1, x2, y2])) = (object.kind, object.points) {
        return vec![(Handle::LineEnd(0), pt(x1, y1)), (Handle::LineEnd(1), pt(x2, y2))];
    }

    let (w, h) = (object.w(), object.h());
    let mut list = Vec::new();
    for vertical in [-1i8, 0, 1] {
        for horizontal in [-1i8, 0, 1] {
            if horizontal == 0 && vertical == 0 {
                continue;
            }
            let x = match horizontal {
                -1 => 0.0,
                0 => w / 2.0,
                _ => w,
            };
            let y = match vertical {
                -1 => 0.0,
                0 => h / 2.0,
                _ => h,
            };
            list.push((Handle::Edge { horizontal, vertical }, pt(x, y)));
        }
    }
    list
}

/// Where the rotation handle sits, in the object's own frame, for a given distance above it.
pub fn rotate_handle(object: &LabelObject, distance_mm: f64) -> Point {
    pt(object.w() / 2.0, -distance_mm)
}

const MIN_SIZE: f64 = 2.0;

/// The object after dragging `handle` from its state at the start (`start`) to `pointer`.
pub fn drag_handle(start: &LabelObject, handle: Handle, pointer: Point, snap_rotation: bool) -> LabelObject {
    let mut object = start.clone();
    let local = to_local(start, pointer);

    match handle {
        Handle::LineEnd(end) => {
            if let Some(points) = object.points.as_mut() {
                points[end * 2] = local.x;
                points[end * 2 + 1] = local.y;
            }
        }
        Handle::Edge { horizontal, vertical } => {
            let (w, h) = (start.w(), start.h());
            let (mut left, mut right, mut top, mut bottom) = (0.0, w, 0.0, h);
            match horizontal {
                -1 => left = local.x.min(w - MIN_SIZE),
                1 => right = local.x.max(MIN_SIZE),
                _ => {}
            }
            match vertical {
                -1 => top = local.y.min(h - MIN_SIZE),
                1 => bottom = local.y.max(MIN_SIZE),
                _ => {}
            }
            let origin = to_label(start, pt(left, top));
            object.x = origin.x;
            object.y = origin.y;
            object.width = Some(right - left);
            object.height = Some(bottom - top);
        }
        Handle::Rotate => {
            // Around the centre, which stays where it is.
            let (w, h) = (start.w(), start.h());
            let center = to_label(start, pt(w / 2.0, h / 2.0));
            let mut angle = (pointer.y - center.y).atan2(pointer.x - center.x).to_degrees() + 90.0;
            if snap_rotation {
                angle = (angle / 15.0).round() * 15.0;
            }
            angle = angle.rem_euclid(360.0);
            if angle > 180.0 {
                angle -= 360.0;
            }
            object.rotation = (angle * 100.0).round() / 100.0;
            let (sin, cos) = object.rotation.to_radians().sin_cos();
            object.x = center.x - (w / 2.0 * cos - h / 2.0 * sin);
            object.y = center.y - (w / 2.0 * sin + h / 2.0 * cos);
        }
    }

    object
}

#[cfg(test)]
mod tests {
    use super::*;

    fn close(a: Point, b: Point) -> bool {
        (a.x - b.x).abs() < 1e-9 && (a.y - b.y).abs() < 1e-9
    }

    #[test]
    fn transforms_round_trip_and_rotate_clockwise() {
        let mut object = LabelObject::new_rect();
        object.x = 10.0;
        object.y = 5.0;
        object.rotation = 90.0;

        // 90° clockwise: the local x axis points down the label.
        assert!(close(to_label(&object, pt(4.0, 0.0)), pt(10.0, 9.0)));
        assert!(close(to_local(&object, to_label(&object, pt(3.0, 2.0))), pt(3.0, 2.0)));
    }

    #[test]
    fn hits_boxes_and_lines() {
        let rect = LabelObject::new_rect();
        assert!(hit(&rect, pt(10.0, 10.0), 0.0));
        assert!(!hit(&rect, pt(30.0, 10.0), 0.0));

        let line = LabelObject::new_line();
        assert!(hit(&line, pt(15.0, 5.2), 0.5));
        assert!(!hit(&line, pt(15.0, 8.0), 0.5));
    }

    #[test]
    fn resizing_from_the_left_keeps_the_right_edge() {
        let rect = LabelObject::new_rect(); // x 5, width 20
        let resized = drag_handle(&rect, Handle::Edge { horizontal: -1, vertical: 0 }, pt(10.0, 0.0), false);

        assert_eq!(resized.x, 10.0);
        assert_eq!(resized.width, Some(15.0));
        assert_eq!(resized.x + resized.w(), 25.0);
    }

    #[test]
    fn rotating_keeps_the_centre() {
        let rect = LabelObject::new_rect(); // 5,5 20×12 → centre 15,11
        let rotated = drag_handle(&rect, Handle::Rotate, pt(30.0, 11.0), true);

        assert_eq!(rotated.rotation, 90.0);
        assert!(close(to_label(&rotated, pt(10.0, 6.0)), pt(15.0, 11.0)));
    }
}
