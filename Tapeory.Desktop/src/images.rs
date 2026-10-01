//! PNG images from the engine (thumbnails, previews, uploaded images), decoded off the UI thread
//! and kept as textures.

use std::collections::HashMap;

use eframe::egui::{self, ColorImage, TextureHandle, TextureOptions};

use crate::api::{Api, ApiResult};
use crate::task::Task;

enum Entry {
    Loading(Task<Result<ColorImage, String>>),
    Ready(TextureHandle),
    Failed,
}

#[derive(Default)]
pub struct Images {
    entries: HashMap<String, Entry>,
}

pub enum ImageState<'a> {
    Loading,
    Ready(&'a TextureHandle),
    Failed,
}

impl Images {
    /// The image for `key`, fetched with `fetch` the first time.
    pub fn get(&mut self, ctx: &egui::Context, key: &str, fetch: impl FnOnce() -> ApiResult<Vec<u8>> + Send + 'static) -> ImageState<'_> {
        if !self.entries.contains_key(key) {
            let task = Task::spawn(ctx, move || {
                let bytes = fetch().map_err(|e| e.message)?;
                decode(&bytes)
            });
            self.entries.insert(key.to_string(), Entry::Loading(task));
        }

        let entry = self.entries.get_mut(key).expect("just inserted");

        if let Entry::Loading(task) = entry
            && let Some(result) = task.take()
        {
            *entry = match result {
                Ok(image) => Entry::Ready(ctx.load_texture(key, image, TextureOptions::LINEAR)),
                Err(_) => Entry::Failed,
            };
        }

        match entry {
            Entry::Loading(_) => ImageState::Loading,
            Entry::Ready(texture) => ImageState::Ready(texture),
            Entry::Failed => ImageState::Failed,
        }
    }

    /// An image from an engine path such as /api/uploads/images/12.
    pub fn load(&mut self, ctx: &egui::Context, api: &Api, path: &str) -> ImageState<'_> {
        let api = api.clone();
        let owned = path.to_string();
        self.get(ctx, path, move || api.download(&owned))
    }
}

pub fn decode(bytes: &[u8]) -> Result<ColorImage, String> {
    let image = image::load_from_memory(bytes).map_err(|e| e.to_string())?.into_rgba8();
    let size = [image.width() as usize, image.height() as usize];
    Ok(ColorImage::from_rgba_unmultiplied(size, image.as_raw()))
}
