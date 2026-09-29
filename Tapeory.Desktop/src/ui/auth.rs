//! Signing in and passwords, for a shared MySQL database that has accounts. On its own local
//! database the desktop app works without accounts.

use eframe::egui::{self};

use crate::api::{Api, ApiResult};
use crate::i18n::{t, tf};
use crate::models::AuthState;
use crate::task::{Pending, Task, finished};
use crate::ui::setup::notice;
use crate::ui::widgets::{self, Tone, button, field, password_input, primary_button_enabled, text_input};

const MIN_PASSWORD: usize = 8;

pub struct AuthPages {
    user_name: String,
    password: String,
    remember: bool,
    busy: Pending<ApiResult<AuthState>>,
    error: Option<String>,
    forced: PasswordForm,
}

impl Default for AuthPages {
    fn default() -> AuthPages {
        // "Stay signed in" starts ticked, as on the web.
        AuthPages {
            user_name: String::new(),
            password: String::new(),
            remember: true,
            busy: None,
            error: None,
            forced: PasswordForm::default(),
        }
    }
}

/// The change-password fields: current, new, repeat.
#[derive(Default)]
pub struct PasswordForm {
    current: String,
    new: String,
    repeat: String,
    busy: Pending<ApiResult<AuthState>>,
    error: Option<String>,
}

impl PasswordForm {
    /// The fields and buttons; returns the new state once changed, or Some(None) on cancel.
    fn show(&mut self, ui: &mut egui::Ui, api: &Api, cancel_label: &str) -> Option<Option<AuthState>> {
        if let Some(result) = finished(&mut self.busy) {
            match result {
                Ok(state) => return Some(Some(state)),
                Err(error) => self.error = Some(error.message),
            }
        }

        let busy = self.busy.is_some();
        field(ui, &t("auth.currentPassword"), |ui| password_input(ui, &mut self.current, 360.0));
        field(ui, &t("auth.newPassword"), |ui| password_input(ui, &mut self.new, 360.0));
        field(ui, &t("auth.repeatPassword"), |ui| password_input(ui, &mut self.repeat, 360.0));

        if let Some(error) = &self.error {
            notice(ui, error, Tone::Danger);
        }

        let mut outcome = None;
        ui.horizontal(|ui| {
            if button(ui, cancel_label).clicked() {
                outcome = Some(None);
            }
            if primary_button_enabled(ui, !busy, &if busy { t("auth.saving") } else { t("auth.changePassword") }).clicked() {
                if let Some(problem) = password_problem(&self.new, &self.repeat) {
                    self.error = Some(problem);
                } else {
                    let api = api.clone();
                    let (current, new) = (self.current.clone(), self.new.clone());
                    self.error = None;
                    self.busy = Some(Task::spawn(ui.ctx(), move || api.change_password(&current, &new)));
                }
            }
        });

        outcome
    }
}

fn password_problem(password: &str, repeat: &str) -> Option<String> {
    if password.chars().count() < MIN_PASSWORD {
        Some(tf("auth.passwordTooShort", &[("count", &MIN_PASSWORD.to_string())]))
    } else if password != repeat {
        Some(t("auth.passwordsDiffer"))
    } else {
        None
    }
}

fn page(ui: &mut egui::Ui, title: &str, intro: &str, add: impl FnOnce(&mut egui::Ui)) {
    widgets::centered_card(ui, title, 480.0, |ui| {
        widgets::logo(ui, 18.5, crate::theme::palette(ui.ctx()).text);
        ui.add_space(10.0);
        ui.label(widgets::heading(title).size(28.0));
        widgets::muted(ui, intro);
        ui.add_space(10.0);
        add(ui);
    });
}

impl AuthPages {
    /// The sign-in page; returns the new state once signed in.
    pub fn login(&mut self, ui: &mut egui::Ui, api: &Api) -> Option<AuthState> {
        if let Some(result) = finished(&mut self.busy) {
            match result {
                Ok(state) if state.user.is_some() => {
                    self.password.clear();
                    return Some(state);
                }
                Ok(_) => self.error = Some(t("auth.failedFallback")),
                Err(error) => self.error = Some(error.message),
            }
        }

        let busy = self.busy.is_some();

        page(ui, &t("auth.signInTitle"), &t("auth.signInIntro"), |ui| {
            field(ui, &t("auth.userName"), |ui| text_input(ui, &mut self.user_name, f32::INFINITY));
            let password = field(ui, &t("auth.password"), |ui| password_input(ui, &mut self.password, f32::INFINITY));
            ui.checkbox(&mut self.remember, t("auth.rememberMe"));

            if let Some(error) = &self.error {
                notice(ui, error, Tone::Danger);
            }

            let submit = password.lost_focus() && ui.input(|input| input.key_pressed(egui::Key::Enter));
            let label = if busy { t("auth.signingIn") } else { t("auth.signIn") };
            let clicked = ui
                .with_layout(egui::Layout::right_to_left(egui::Align::Min), |ui| primary_button_enabled(ui, !busy, &label).clicked())
                .inner;
            if (clicked || submit) && !busy {
                let api = api.clone();
                let (user, password, remember) = (self.user_name.trim().to_string(), self.password.clone(), self.remember);
                self.error = None;
                self.busy = Some(Task::spawn(ui.ctx(), move || api.login(&user, &password, remember)));
            }

            widgets::muted_small(ui, &t("auth.forgotPassword"));
        });

        None
    }

    /// After signing in with a temporary password. Some(None) means "sign out".
    pub fn forced_password(&mut self, ui: &mut egui::Ui, api: &Api) -> Option<Option<AuthState>> {
        let mut outcome = None;
        page(ui, &t("auth.forcedTitle"), &t("auth.forcedIntro"), |ui| {
            outcome = self.forced.show(ui, api, &t("auth.signOut"));
        });
        outcome
    }
}

/// "Change password" from the account menu, as a dialog. Some(Some) = changed, Some(None) = closed.
pub fn password_dialog(ctx: &egui::Context, form: &mut PasswordForm, api: &Api) -> Option<Option<AuthState>> {
    let mut outcome = None;
    egui::Window::new(t("auth.changePassword"))
        .collapsible(false)
        .resizable(false)
        .anchor(egui::Align2::CENTER_CENTER, egui::Vec2::ZERO)
        .show(ctx, |ui| {
            outcome = form.show(ui, api, &t("common.cancel"));
        });
    outcome
}
