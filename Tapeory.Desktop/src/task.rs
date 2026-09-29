//! Work that runs off the UI thread (every call to the engine), polled once per frame.

use std::sync::mpsc::{Receiver, channel};

use eframe::egui::Context;

/// The result of a function running on a background thread; asks for a repaint when it's done.
pub struct Task<T> {
    receiver: Receiver<T>,
    value: Option<T>,
}

impl<T: Send + 'static> Task<T> {
    pub fn spawn(ctx: &Context, work: impl FnOnce() -> T + Send + 'static) -> Self {
        let (sender, receiver) = channel();
        let ctx = ctx.clone();

        std::thread::spawn(move || {
            let _ = sender.send(work());
            ctx.request_repaint();
        });

        Self { receiver, value: None }
    }

    /// The result, once it's there.
    pub fn ready(&mut self) -> Option<&T> {
        if self.value.is_none()
            && let Ok(value) = self.receiver.try_recv()
        {
            self.value = Some(value);
        }

        self.value.as_ref()
    }

    /// Takes the result out, once it's there.
    pub fn take(&mut self) -> Option<T> {
        self.ready();
        self.value.take()
    }
}

/// A task that may or may not be running, the usual way screens hold one.
pub type Pending<T> = Option<Task<T>>;

/// Takes a finished task's result out of a `Pending`, leaving it empty.
pub fn finished<T: Send + 'static>(pending: &mut Pending<T>) -> Option<T> {
    let result = pending.as_mut()?.take();

    if result.is_some() {
        *pending = None;
    }

    result
}
