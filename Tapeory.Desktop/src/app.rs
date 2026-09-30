//! The app: starts the engine, then shows the setup, the sign-in, or the screens.

use std::path::PathBuf;

use eframe::egui::{self, RichText};
use serde_json::json;

use crate::api::{Api, ApiResult};
use crate::editor::EditorPage;
use crate::engine::{self, Engine};
use crate::fonts::{self, Fonts, face};
use crate::i18n::{self, t};
use crate::icons::Icon;
use crate::images::Images;
use crate::models::{AppSettings, AuthState, SetupStatus};
use crate::task::{Pending, Task, finished};
use crate::theme::{self, ThemeChoice};
use crate::ui::{self, Ctx, Route, Toasts, widgets};
use crate::units::Unit;

enum Stage {
    /// The engine is starting.
    Starting(Task<Result<Engine, String>>),
    Failed(String),
    Running,
}

/// The screen being shown, with its state.
enum Screen {
    Dashboard(Box<ui::dashboard::DashboardPage>),
    Templates(Box<ui::templates::TemplatesPage>),
    Editor(Box<EditorPage>),
    Print(ui::print::PrintPage),
    Jobs(ui::jobs::JobsPage),
    Job(ui::jobs::JobPage),
    Printers(ui::printers::PrintersPage),
    Settings(Box<ui::settings::SettingsPage>),
}

impl Screen {
    fn for_route(route: &Route) -> Screen {
        match route {
            Route::Dashboard => Screen::Dashboard(Default::default()),
            Route::Templates => Screen::Templates(Default::default()),
            Route::Editor(id) => Screen::Editor(Box::new(EditorPage::new(*id))),
            Route::Print(id) => Screen::Print(ui::print::PrintPage::new(*id)),
            Route::Jobs => Screen::Jobs(Default::default()),
            Route::Job(id) => Screen::Job(ui::jobs::JobPage::new(*id)),
            Route::Printers => Screen::Printers(Default::default()),
            Route::Settings => Screen::Settings(Default::default()),
        }
    }
}

pub struct App {
    data_folder: PathBuf,
    stage: Stage,
    engine: Option<Engine>,
    api: Option<Api>,
    fonts: Fonts,
    images: Images,
    toasts: Toasts,

    setup: Option<SetupStatus>,
    setup_task: Pending<ApiResult<SetupStatus>>,
    setup_page: ui::setup::SetupPage,
    auth: Option<AuthState>,
    auth_task: Pending<ApiResult<AuthState>>,
    auth_pages: ui::auth::AuthPages,
    settings_task: Pending<ApiResult<AppSettings>>,

    theme: ThemeChoice,
    unit: Unit,
    route: Route,
    screen: Screen,
    account_menu_open: bool,
    changing_password: Option<ui::auth::PasswordForm>,
    /// Files being saved in the background (see `Ctx::save_file`).
    files: Vec<Task<ApiResult<String>>>,
}

impl App {
    pub fn new(cc: &eframe::CreationContext<'_>) -> App {
        // The renderer works: from here on, errors don't mean "try another renderer".
        crate::STARTED.store(true, std::sync::atomic::Ordering::SeqCst);
        let data_folder = engine::data_folder();
        let folder = data_folder.clone();
        let task = Task::spawn(&cc.egui_ctx, move || Engine::start(&folder));

        egui_extras::install_image_loaders(&cc.egui_ctx);
        theme::apply(&cc.egui_ctx, ThemeChoice::System);
        let mut fonts = Fonts::default();
        fonts.install_interface(&cc.egui_ctx);

        App {
            data_folder,
            stage: Stage::Starting(task),
            engine: None,
            api: None,
            fonts,
            images: Images::default(),
            toasts: Toasts::default(),
            setup: None,
            setup_task: None,
            setup_page: Default::default(),
            auth: None,
            auth_task: None,
            auth_pages: Default::default(),
            settings_task: None,
            theme: ThemeChoice::System,
            unit: Unit::Mm,
            route: Route::Dashboard,
            screen: Screen::Dashboard(Default::default()),
            account_menu_open: false,
            changing_password: None,
            files: Vec::new(),
        }
    }

    fn start_engine(&mut self, ctx: &egui::Context) {
        let folder = self.data_folder.clone();
        self.stage = Stage::Starting(Task::spawn(ctx, move || Engine::start(&folder)));
    }

    fn refresh_setup(&mut self, ctx: &egui::Context) {
        if let Some(api) = self.api.clone() {
            self.setup_task = Some(Task::spawn(ctx, move || api.setup_status()));
        }
    }

    fn refresh_auth(&mut self, ctx: &egui::Context) {
        if let Some(api) = self.api.clone() {
            self.auth_task = Some(Task::spawn(ctx, move || api.auth_state()));
        }
    }

    fn load_settings(&mut self, ctx: &egui::Context) {
        if let Some(api) = self.api.clone() {
            self.settings_task = Some(Task::spawn(ctx, move || api.settings()));
        }
    }

    fn apply_settings(&mut self, ctx: &egui::Context, settings: &AppSettings) {
        if let Some(language) = settings.language.as_deref() {
            i18n::set_language(language);
        }
        self.theme = ThemeChoice::from_setting(settings.theme.as_deref());
        theme::apply(ctx, self.theme);
        self.unit = Unit::from_setting(settings.unit.as_deref());
    }

    fn navigate(&mut self, route: Route) {
        self.screen = Screen::for_route(&route);
        self.route = route;
        self.account_menu_open = false;
    }

    /// Everything that's pending: engine start, setup status, session, settings.
    fn poll(&mut self, ctx: &egui::Context) {
        if let Stage::Starting(task) = &mut self.stage
            && let Some(result) = task.take()
        {
            match result {
                Ok(engine) => {
                    let api = Api::new(&engine.address, &engine.token);
                    self.api = Some(api);
                    self.engine = Some(engine);
                    self.stage = Stage::Running;
                    self.refresh_setup(ctx);
                }
                Err(message) => self.stage = Stage::Failed(message),
            }
        }

        if let Some(result) = finished(&mut self.setup_task) {
            match result {
                Ok(status) => {
                    let configured = status.configured;
                    self.setup = Some(status);
                    if configured {
                        self.refresh_auth(ctx);
                        self.load_settings(ctx);
                        if let Some(api) = &self.api {
                            self.fonts.start(ctx, api);
                        }
                    }
                }
                Err(error) => self.stage = Stage::Failed(error.message),
            }
        }

        if let Some(result) = finished(&mut self.auth_task) {
            match result {
                Ok(state) => self.auth = Some(state),
                Err(error) => self.toasts.error(error.message),
            }
        }

        if let Some(Ok(settings)) = finished(&mut self.settings_task) {
            self.apply_settings(ctx, &settings);
        }

        self.fonts.poll(ctx);

        // "Follow system": switch when the system's theme changes.
        if self.theme == ThemeChoice::System && theme::resolve(ctx, self.theme) != ctx.style().visuals.dark_mode {
            theme::apply(ctx, self.theme);
        }
    }

    fn top_bar(&mut self, ctx: &egui::Context) {
        let palette = theme::palette(ctx);
        let auth = self.auth.clone().unwrap_or_default();
        let can_administer = !auth.has_users || auth.user.as_ref().is_some_and(|user| user.is_admin());
        let mut go = None;
        let mut sign_out = false;

        egui::TopBottomPanel::top("top-bar")
            .exact_height(64.0)
            .frame(egui::Frame::new().fill(palette.header).inner_margin(egui::Margin::symmetric(0, 0)))
            .show(ctx, |ui| {
                let bottom = ui.max_rect().bottom();
                ui.painter().hline(ui.max_rect().x_range(), bottom - 0.5, egui::Stroke::new(1.0_f32, egui::Color32::from_white_alpha(20)));
                content_width(ui, |ui| {
                    ui.horizontal_centered(|ui| {
                        widgets::logo(ui, 18.5, palette.header_text);

                        ui.with_layout(egui::Layout::right_to_left(egui::Align::Center), |ui| {
                            if let Some(user) = &auth.user {
                                let button = account_button(ui, &user.display_name);
                                if button.clicked() {
                                    self.account_menu_open = !self.account_menu_open;
                                }

                                if self.account_menu_open {
                                    let area = egui::Area::new(egui::Id::new("account-menu"))
                                        .order(egui::Order::Foreground)
                                        .fixed_pos(button.rect.right_bottom() + egui::vec2(-250.0, 8.0))
                                        .show(ui.ctx(), |ui| {
                                            theme::card(ui).inner_margin(egui::Margin::same(14)).show(ui, |ui| {
                                                ui.set_width(222.0);
                                                ui.label(widgets::bold(&user.display_name));
                                                let role = t(if user.is_admin() { "auth.roleAdmin" } else { "auth.roleUser" });
                                                widgets::muted_small(ui, &format!("{} · {role}", user.user_name));
                                                ui.separator();
                                                if menu_item(ui, Icon::Key, &t("auth.changePassword")).clicked() {
                                                    self.changing_password = Some(Default::default());
                                                    self.account_menu_open = false;
                                                }
                                                if menu_item(ui, Icon::Logout, &t("auth.signOut")).clicked() {
                                                    sign_out = true;
                                                    self.account_menu_open = false;
                                                }
                                            });
                                        });
                                    if area.response.clicked_elsewhere() && !button.clicked() {
                                        self.account_menu_open = false;
                                    }
                                }
                                ui.add_space(14.0);
                            }

                            let items = [
                                (Route::Settings, "nav.settings", true),
                                (Route::Printers, "nav.printers", can_administer),
                                (Route::Jobs, "nav.printJobs", true),
                                (Route::Templates, "nav.templates", true),
                                (Route::Dashboard, "nav.dashboard", true),
                            ];

                            for (route, key, shown) in items {
                                if shown && nav_link(ui, &t(key), same_section(&self.route, &route)).clicked() {
                                    go = Some(route);
                                }
                            }
                        });
                    });
                });
            });

        if let Some(route) = go {
            self.navigate(route);
        }

        if sign_out && let Some(api) = self.api.clone() {
            self.auth_task = Some(Task::spawn(ctx, move || {
                let _ = api.logout();
                api.auth_state()
            }));
        }
    }
}

/// Lays `add` out in the middle, at most 1240 points wide like the web UI's pages.
fn content_width<R>(ui: &mut egui::Ui, add: impl FnOnce(&mut egui::Ui) -> R) -> R {
    let available = ui.available_rect_before_wrap();
    let side = ((available.width() - 1240.0) / 2.0).max(0.0) + 32.0_f32.min(available.width() * 0.03).max(16.0);
    let rect = egui::Rect::from_min_max(
        egui::pos2(available.left() + side, available.top()),
        egui::pos2(available.right() - side, available.bottom()),
    );
    ui.scope_builder(egui::UiBuilder::new().max_rect(rect).layout(egui::Layout::top_down(egui::Align::Min)), add).inner
}

/// A header link with the gradient underline when it's the current page.
fn nav_link(ui: &mut egui::Ui, text: &str, active: bool) -> egui::Response {
    let palette = theme::palette(ui.ctx());
    let font = egui::FontId::new(15.0, face(if active { fonts::HEADING } else { fonts::HEADING_MEDIUM }));
    let galley = ui.painter().layout_no_wrap(text.to_string(), font, egui::Color32::PLACEHOLDER);
    let (rect, response) = ui.allocate_exact_size(galley.size() + egui::vec2(4.0, 14.0), egui::Sense::click());
    let color = if active || response.hovered() { palette.header_text } else { palette.on_header };
    ui.painter().galley(egui::pos2(rect.left() + 2.0, rect.center().y - galley.size().y / 2.0), galley, color);
    if active || response.hovered() {
        let line = egui::Rect::from_min_max(egui::pos2(rect.left(), rect.bottom() - 2.0), egui::pos2(rect.right(), rect.bottom()));
        widgets::gradient_rect(ui, line, egui::CornerRadius::same(1), palette.primary, palette.primary_ii);
    }
    ui.add_space(12.0);
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// The account button: an avatar with the initial, then the name.
fn account_button(ui: &mut egui::Ui, name: &str) -> egui::Response {
    let palette = theme::palette(ui.ctx());
    let font = egui::FontId::new(14.5, face(fonts::HEADING));
    let galley = ui.painter().layout_no_wrap(name.to_string(), font.clone(), palette.header_text);
    let size = egui::vec2(6.0 + 28.0 + 8.0 + galley.size().x + 14.0, 38.0);
    let (rect, response) = ui.allocate_exact_size(size, egui::Sense::click());
    let border = if response.hovered() { palette.primary_ii } else { egui::Color32::from_white_alpha(46) };
    ui.painter().rect(
        rect,
        egui::CornerRadius::same(19),
        egui::Color32::from_white_alpha(10),
        egui::Stroke::new(1.0_f32, border),
        egui::StrokeKind::Inside,
    );

    let avatar = egui::Rect::from_center_size(egui::pos2(rect.left() + 6.0 + 14.0, rect.center().y), egui::vec2(28.0, 28.0));
    widgets::gradient_rect(ui, avatar, egui::CornerRadius::same(14), palette.primary, palette.primary_ii);
    let initial = name.chars().next().map(|c| c.to_uppercase().to_string()).unwrap_or_default();
    ui.painter().text(
        avatar.center(),
        egui::Align2::CENTER_CENTER,
        initial,
        egui::FontId::new(13.5, face(fonts::HEADING_BOLD)),
        egui::Color32::WHITE,
    );
    ui.painter().galley(egui::pos2(avatar.right() + 8.0, rect.center().y - galley.size().y / 2.0), galley, palette.header_text);
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// A row in a small menu: icon and text, highlighted on hover.
fn menu_item(ui: &mut egui::Ui, icon: Icon, text: &str) -> egui::Response {
    let palette = theme::palette(ui.ctx());
    let (rect, response) = ui.allocate_exact_size(egui::vec2(ui.available_width(), 34.0), egui::Sense::click());
    if response.hovered() {
        ui.painter().rect_filled(rect, egui::CornerRadius::same(8), palette.subtle);
    }
    let color = if response.hovered() { palette.primary } else { palette.text };
    crate::icons::paint(
        ui,
        icon,
        egui::Rect::from_center_size(egui::pos2(rect.left() + 18.0, rect.center().y), egui::vec2(17.0, 17.0)),
        color,
    );
    ui.painter().text(
        egui::pos2(rect.left() + 36.0, rect.center().y),
        egui::Align2::LEFT_CENTER,
        text,
        egui::FontId::new(14.5, face(fonts::BODY_MEDIUM)),
        color,
    );
    response.on_hover_cursor(egui::CursorIcon::PointingHand)
}

/// The editor and print form belong to Templates, a job to the print history.
fn same_section(current: &Route, item: &Route) -> bool {
    match (current, item) {
        (Route::Editor(_) | Route::Print(_), Route::Templates) => true,
        (Route::Job(_), Route::Jobs) => true,
        _ => current == item,
    }
}

impl eframe::App for App {
    fn update(&mut self, ctx: &egui::Context, _frame: &mut eframe::Frame) {
        self.poll(ctx);

        match &self.stage {
            Stage::Starting(_) => {
                centered(ctx, |ui| {
                    ui.spinner();
                    ui.add_space(8.0);
                    ui.label(RichText::new(t("desktop.starting")).size(16.0));
                });
                return;
            }
            Stage::Failed(message) => {
                let message = message.clone();
                let mut retry = false;
                centered(ctx, |ui| {
                    ui.label(widgets::bold(t("desktop.engineFailedTitle")).size(20.0));
                    ui.add_space(6.0);
                    ui.label(&message);
                    ui.add_space(12.0);
                    retry = widgets::primary_button(ui, &t("desktop.retry")).clicked();
                });
                if retry {
                    self.engine = None;
                    self.api = None;
                    self.start_engine(ctx);
                }
                return;
            }
            Stage::Running => {}
        }

        let Some(api) = self.api.clone() else { return };

        // The database first.
        match &self.setup {
            None => {
                centered(ctx, |ui| {
                    ui.spinner();
                });
                return;
            }
            Some(status) if !status.configured => {
                let desktop = status.desktop;
                let done = egui::CentralPanel::default().show(ctx, |ui| self.setup_page.show(ui, &api, desktop, &mut self.toasts)).inner;
                if done {
                    self.toasts.success(t("setup.completed"));
                    self.refresh_setup(ctx);
                }
                self.toasts.show(ctx);
                return;
            }
            Some(_) => {}
        }

        // Then the session, when the database has accounts.
        let Some(auth) = self.auth.clone() else {
            centered(ctx, |ui| {
                ui.spinner();
            });
            return;
        };

        if auth.has_users && auth.user.is_none() {
            if let Some(state) = egui::CentralPanel::default().show(ctx, |ui| self.auth_pages.login(ui, &api)).inner {
                self.auth = Some(state);
                self.load_settings(ctx);
            }
            return;
        }

        if auth.user.as_ref().is_some_and(|user| user.must_change_password) {
            if let Some(outcome) = egui::CentralPanel::default().show(ctx, |ui| self.auth_pages.forced_password(ui, &api)).inner {
                match outcome {
                    Some(state) => self.auth = Some(state),
                    None => {
                        let api = api.clone();
                        self.auth_task = Some(Task::spawn(ctx, move || {
                            let _ = api.logout();
                            api.auth_state()
                        }));
                    }
                }
            }
            return;
        }

        self.top_bar(ctx);

        let mut navigate = None;
        let mut session_ended = false;
        let mut settings_saved = None;

        footer(ctx);

        egui::CentralPanel::default()
            .frame(egui::Frame::new().fill(theme::palette(ctx).bg).inner_margin(egui::Margin::symmetric(0, 28)))
            .show(ctx, |ui| {
                content_width(ui, |ui| {
                    let mut c = Ctx {
                        egui: ctx,
                        api: &api,
                        fonts: &mut self.fonts,
                        images: &mut self.images,
                        unit: self.unit,
                        auth: &auth,
                        toasts: &mut self.toasts,
                        navigate: &mut navigate,
                        session_ended: &mut session_ended,
                        settings_saved: &mut settings_saved,
                        files: &mut self.files,
                    };

                    match &mut self.screen {
                        Screen::Dashboard(page) => page.show(ui, &mut c),
                        Screen::Templates(page) => page.show(ui, &mut c),
                        Screen::Editor(page) => page.show(ui, &mut c),
                        Screen::Print(page) => page.show(ui, &mut c),
                        Screen::Jobs(page) => page.show(ui, &mut c),
                        Screen::Job(page) => page.show(ui, &mut c),
                        Screen::Printers(page) => page.show(ui, &mut c),
                        Screen::Settings(page) => page.show(ui, &mut c),
                    }
                })
            });

        if let Some(form) = &mut self.changing_password {
            match ui::auth::password_dialog(ctx, form, &api) {
                Some(Some(state)) => {
                    self.toasts.success(t("auth.passwordChanged"));
                    self.auth = Some(state);
                    self.changing_password = None;
                }
                Some(None) => self.changing_password = None,
                None => {}
            }
        }

        let mut saved = Vec::new();
        self.files.retain_mut(|task| match task.take() {
            Some(result) => {
                saved.push(result);
                false
            }
            None => true,
        });
        for result in saved {
            match result {
                Ok(message) => self.toasts.success(message),
                Err(error) if error.is_unauthorized() => session_ended = true,
                Err(error) if !error.is_cancelled() => self.toasts.error(error.message),
                Err(_) => {}
            }
        }

        if let Some(settings) = settings_saved {
            self.apply_settings(ctx, &settings);
        }

        if session_ended {
            self.refresh_auth(ctx);
        }

        if let Some(route) = navigate {
            self.navigate(route);
        }

        self.toasts.show(ctx);
    }
}

impl Drop for App {
    fn drop(&mut self) {
        // Stops the engine (see Engine's Drop).
        self.engine.take();
    }
}

/// "© 2026 by Tunefish", as under every page of the web UI.
fn footer(ctx: &egui::Context) {
    let palette = theme::palette(ctx);
    egui::TopBottomPanel::bottom("footer").frame(egui::Frame::new().fill(palette.bg).inner_margin(egui::Margin::symmetric(16, 14))).show(
        ctx,
        |ui| {
            let top = ui.max_rect().top() - 14.0;
            ui.painter().hline(ctx.screen_rect().x_range(), top + 0.5, egui::Stroke::new(1.0_f32, palette.border));
            ui.vertical_centered(|ui| {
                ui.horizontal(|ui| {
                    let year = chrono::Local::now().format("%Y").to_string();
                    let text_width = 130.0;
                    ui.add_space(((ui.available_width() - text_width) / 2.0).max(0.0));
                    ui.spacing_mut().item_spacing.x = 4.0;
                    ui.label(RichText::new(format!("© {year} by")).size(12.5).color(palette.muted));
                    ui.hyperlink_to(
                        widgets::bold("Tunefish").size(12.5).color(palette.muted).underline(),
                        "https://github.com/Tunefish92?tab=repositories",
                    );
                });
            });
        },
    );
}

fn centered(ctx: &egui::Context, add: impl FnOnce(&mut egui::Ui)) {
    egui::CentralPanel::default().show(ctx, |ui| {
        ui.with_layout(egui::Layout::centered_and_justified(egui::Direction::TopDown), |ui| {
            ui.vertical_centered(|ui| {
                ui.add_space(ui.available_height() / 3.0);
                add(ui);
            });
        });
    });
}

/// Settings changes are saved on the engine; this builds the patch.
pub fn settings_patch(language: Option<&str>, theme: Option<ThemeChoice>, unit: Option<Unit>) -> serde_json::Value {
    let mut patch = json!({});
    if let Some(language) = language {
        patch["language"] = json!(language);
    }
    if let Some(theme) = theme {
        patch["theme"] = json!(theme.setting());
    }
    if let Some(unit) = unit {
        patch["unit"] = json!(unit.setting());
    }
    patch
}
