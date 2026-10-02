//! Calls to Tapeory's engine. Blocking (reqwest's blocking client), so they always run in a
//! `Task` off the UI thread. Every request carries the engine's secret; a session cookie is kept
//! for shared MySQL databases that have accounts.

use std::path::Path;
use std::time::Duration;

use reqwest::StatusCode;
use reqwest::blocking::{Client, RequestBuilder, Response, multipart};
use reqwest::header::{HeaderMap, HeaderValue};
use serde::Serialize;
use serde::de::DeserializeOwned;
use serde_json::{Value, json};

use crate::models::*;

#[derive(Clone, Debug)]
pub struct ApiError {
    pub status: Option<u16>,
    pub message: String,
}

impl ApiError {
    /// The session ended (signed out elsewhere, disabled, password reset).
    pub fn is_unauthorized(&self) -> bool {
        self.status == Some(401)
    }

    /// The user closed a file dialog: nothing to report.
    pub fn cancelled() -> ApiError {
        ApiError { status: None, message: String::new() }
    }

    pub fn is_cancelled(&self) -> bool {
        self.status.is_none() && self.message.is_empty()
    }

    pub fn io(error: std::io::Error) -> ApiError {
        ApiError { status: None, message: error.to_string() }
    }
}

impl std::fmt::Display for ApiError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.write_str(&self.message)
    }
}

pub type ApiResult<T> = Result<T, ApiError>;

#[derive(Clone)]
pub struct Api {
    base: String,
    http: Client,
}

impl Api {
    pub fn new(address: &str, token: &str) -> Api {
        let mut headers = HeaderMap::new();
        headers.insert("X-Tapeory-Token", HeaderValue::from_str(token).expect("token is ASCII"));
        headers.insert("X-Requested-With", HeaderValue::from_static("Tapeory"));

        let http =
            Client::builder().default_headers(headers).cookie_store(true).timeout(Duration::from_secs(120)).build().expect("HTTP client");

        Api { base: format!("{}/api", address.trim_end_matches('/')), http }
    }

    fn url(&self, path: &str) -> String {
        format!("{}{}", self.base, path)
    }

    fn send(&self, request: RequestBuilder) -> ApiResult<Response> {
        let response = request.send().map_err(|e| ApiError { status: None, message: format!("Tapeory's engine didn't answer: {e}") })?;

        if response.status().is_success() { Ok(response) } else { Err(problem(response)) }
    }

    fn json<T: DeserializeOwned>(&self, request: RequestBuilder) -> ApiResult<T> {
        self.send(request)?.json::<T>().map_err(|e| ApiError { status: None, message: format!("Unexpected answer from the engine: {e}") })
    }

    fn bytes(&self, request: RequestBuilder) -> ApiResult<Vec<u8>> {
        self.send(request)?.bytes().map(|b| b.to_vec()).map_err(|e| ApiError { status: None, message: e.to_string() })
    }

    pub fn get<T: DeserializeOwned>(&self, path: &str) -> ApiResult<T> {
        self.json(self.http.get(self.url(path)))
    }

    pub fn post<T: DeserializeOwned>(&self, path: &str, body: &impl Serialize) -> ApiResult<T> {
        self.json(self.http.post(self.url(path)).json(body))
    }

    pub fn put<T: DeserializeOwned>(&self, path: &str, body: &impl Serialize) -> ApiResult<T> {
        self.json(self.http.put(self.url(path)).json(body))
    }

    /// A call whose answer doesn't matter (204, or a body nobody reads).
    pub fn call(&self, method: reqwest::Method, path: &str, body: Option<&Value>) -> ApiResult<()> {
        let mut request = self.http.request(method, self.url(path));
        if let Some(body) = body {
            request = request.json(body);
        }
        self.send(request).map(|_| ())
    }

    /// Raw bytes from an engine path ("/api/…" as the engine hands out, or a path below /api).
    pub fn download(&self, path: &str) -> ApiResult<Vec<u8>> {
        let url = if let Some(rest) = path.strip_prefix("/api") { self.url(rest) } else { self.url(path) };
        self.bytes(self.http.get(url))
    }

    // Setup and accounts ---------------------------------------------------------------------

    pub fn setup_status(&self) -> ApiResult<SetupStatus> {
        self.get("/setup/status")
    }

    pub fn setup_local_database(&self) -> ApiResult<()> {
        self.call(reqwest::Method::POST, "/setup/database/local", None)
    }

    pub fn test_database(&self, request: &DatabaseSetupRequest) -> ApiResult<DatabaseTestResult> {
        self.post("/setup/database/test", request)
    }

    pub fn setup_database(&self, request: &DatabaseSetupRequest) -> ApiResult<()> {
        self.call(reqwest::Method::POST, "/setup/database", Some(&json!(request)))
    }

    pub fn auth_state(&self) -> ApiResult<AuthState> {
        self.get("/auth/state")
    }

    pub fn login(&self, user_name: &str, password: &str, remember: bool) -> ApiResult<AuthState> {
        self.post("/auth/login", &json!({ "userName": user_name, "password": password, "rememberMe": remember }))
    }

    pub fn logout(&self) -> ApiResult<()> {
        self.call(reqwest::Method::POST, "/auth/logout", None)
    }

    pub fn change_password(&self, current: &str, new: &str) -> ApiResult<AuthState> {
        self.put("/auth/password", &json!({ "currentPassword": current, "newPassword": new }))
    }

    pub fn users(&self) -> ApiResult<Vec<UserResponse>> {
        self.get("/users")
    }

    pub fn create_user(&self, user_name: &str, display_name: &str, role: &str) -> ApiResult<TemporaryPassword> {
        self.post("/users", &json!({ "userName": user_name, "displayName": display_name, "role": role }))
    }

    pub fn update_user(&self, id: i64, display_name: &str, role: &str, disabled: bool) -> ApiResult<UserResponse> {
        self.put(&format!("/users/{id}"), &json!({ "displayName": display_name, "role": role, "disabled": disabled }))
    }

    pub fn reset_password(&self, id: i64) -> ApiResult<TemporaryPassword> {
        self.post(&format!("/users/{id}/reset-password"), &json!({}))
    }

    pub fn delete_user(&self, id: i64, delete_templates: bool) -> ApiResult<()> {
        let templates = if delete_templates { "delete" } else { "transfer" };
        self.call(reqwest::Method::DELETE, &format!("/users/{id}?templates={templates}"), None)
    }

    // Templates ------------------------------------------------------------------------------

    pub fn templates(&self) -> ApiResult<Vec<TemplateSummary>> {
        self.get("/templates")
    }

    pub fn template(&self, id: i64) -> ApiResult<TemplateDetail> {
        self.get(&format!("/templates/{id}"))
    }

    pub fn create_template(&self, request: &CreateTemplateRequest) -> ApiResult<TemplateDetail> {
        self.post("/templates", request)
    }

    pub fn update_template(&self, id: i64, request: &UpdateTemplateRequest) -> ApiResult<TemplateDetail> {
        self.put(&format!("/templates/{id}"), request)
    }

    pub fn create_version(&self, id: i64, request: &CreateVersionRequest) -> ApiResult<TemplateVersion> {
        self.post(&format!("/templates/{id}/versions"), request)
    }

    pub fn set_visibility(&self, id: i64, public: bool) -> ApiResult<TemplateDetail> {
        self.put(&format!("/templates/{id}/visibility"), &json!({ "isPublic": public }))
    }

    pub fn duplicate_template(&self, id: i64, name: &str) -> ApiResult<TemplateSummary> {
        self.post(&format!("/templates/{id}/duplicate"), &json!({ "name": name }))
    }

    pub fn delete_template(&self, id: i64) -> ApiResult<()> {
        self.call(reqwest::Method::DELETE, &format!("/templates/{id}"), None)
    }

    pub fn rename_group(&self, from: &str, to: &str) -> ApiResult<()> {
        self.call(reqwest::Method::POST, "/templates/groups/rename", Some(&json!({ "from": from, "to": to })))
    }

    pub fn thumbnail(&self, id: i64, version: i32) -> ApiResult<Vec<u8>> {
        self.bytes(self.http.get(self.url(&format!("/templates/{id}/thumbnail?v={version}"))))
    }

    pub fn preview(&self, id: i64, field_values: &std::collections::BTreeMap<String, String>) -> ApiResult<Vec<u8>> {
        self.bytes(
            self.http.post(self.url(&format!("/templates/{id}/preview"))).json(&json!({ "fieldValues": field_values, "format": "png" })),
        )
    }

    pub fn export_template(&self, id: i64) -> ApiResult<Vec<u8>> {
        self.bytes(self.http.get(self.url(&format!("/templates/{id}/export"))))
    }

    pub fn import_template(&self, json_bytes: Vec<u8>) -> ApiResult<TemplateDetail> {
        let body: Value = serde_json::from_slice(&json_bytes)
            .map_err(|e| ApiError { status: None, message: format!("That isn't a Tapeory template file: {e}") })?;
        self.post("/templates/import", &body)
    }

    pub fn import_lbx(&self, path: &Path) -> ApiResult<TemplateDetail> {
        self.upload("/templates/import-lbx", path)
    }

    pub fn upload_image(&self, path: &Path) -> ApiResult<UploadedImage> {
        self.upload("/uploads/images", path)
    }

    fn upload<T: DeserializeOwned>(&self, path: &str, file: &Path) -> ApiResult<T> {
        let bytes = std::fs::read(file).map_err(|e| ApiError { status: None, message: e.to_string() })?;
        let name = file.file_name().and_then(|n| n.to_str()).unwrap_or("file").to_string();
        let mime = mime_for(&name);
        let part =
            multipart::Part::bytes(bytes).file_name(name).mime_str(mime).map_err(|e| ApiError { status: None, message: e.to_string() })?;
        let form = multipart::Form::new().part("file", part);
        self.json(self.http.post(self.url(path)).multipart(form))
    }

    pub fn encode_barcode(&self, symbology: &str, data: &str) -> ApiResult<EncodedBarcode> {
        self.post("/barcodes/encode", &json!({ "symbology": symbology, "data": data }))
    }

    pub fn fonts(&self) -> ApiResult<Vec<String>> {
        self.get("/fonts")
    }

    pub fn font_file(&self, family: &str, bold: bool) -> ApiResult<Vec<u8>> {
        let mut url = format!("/fonts/file?family={}", urlencoding::encode(family));
        if bold {
            url.push_str("&weight=bold");
        }
        self.bytes(self.http.get(self.url(&url)))
    }

    // Printing -------------------------------------------------------------------------------

    pub fn printers(&self) -> ApiResult<Vec<Printer>> {
        self.get("/printers")
    }

    pub fn printer_models(&self) -> ApiResult<Vec<PrinterModel>> {
        self.get("/printers/models")
    }

    pub fn usb_printers(&self) -> ApiResult<Vec<UsbPrinter>> {
        self.get("/printers/usb")
    }

    pub fn save_printer(&self, id: Option<i64>, request: &PrinterRequest) -> ApiResult<Printer> {
        match id {
            Some(id) => self.put(&format!("/printers/{id}"), request),
            None => self.post("/printers", request),
        }
    }

    pub fn delete_printer(&self, id: i64) -> ApiResult<()> {
        self.call(reqwest::Method::DELETE, &format!("/printers/{id}"), None)
    }

    pub fn set_default_printer(&self, id: i64) -> ApiResult<()> {
        self.call(reqwest::Method::PUT, &format!("/printers/{id}/default"), None)
    }

    pub fn test_connection(&self, id: i64) -> ApiResult<ActionResult> {
        self.post(&format!("/printers/{id}/test-connection"), &json!({}))
    }

    pub fn test_print(&self, id: i64) -> ApiResult<ActionResult> {
        self.post(&format!("/printers/{id}/test-print"), &json!({}))
    }

    pub fn printer_status(&self, id: i64) -> ApiResult<PrinterStatus> {
        self.get(&format!("/printers/{id}/status"))
    }

    /// Where a label of this size can't be printed: the limits of its tape or roll.
    pub fn print_area(&self, width_mm: f64, height_mm: f64, media: Option<&str>) -> ApiResult<PrintArea> {
        let mut path = format!("/printers/print-area?widthMm={width_mm}&heightMm={height_mm}");
        if let Some(media) = media {
            path.push_str(&format!("&media={}", urlencoding::encode(media)));
        }
        self.get(&path)
    }

    pub fn create_print_job(&self, request: &CreatePrintJobRequest) -> ApiResult<PrintJob> {
        self.post("/print-jobs", request)
    }

    pub fn print_jobs(&self) -> ApiResult<Vec<PrintJob>> {
        self.get("/print-jobs")
    }

    pub fn print_job(&self, id: i64) -> ApiResult<PrintJob> {
        self.get(&format!("/print-jobs/{id}"))
    }

    /// Stops a job: a waiting one at once, a printing one after the labels already at the printer.
    pub fn cancel_print_job(&self, id: i64) -> ApiResult<PrintJob> {
        self.post(&format!("/print-jobs/{id}/cancel"), &json!({}))
    }

    /// Creates a new job with the rows of a finished job that weren't printed.
    pub fn reprint_unprinted(&self, id: i64) -> ApiResult<PrintJob> {
        self.post(&format!("/print-jobs/{id}/reprint-unprinted"), &json!({}))
    }

    /// Reads a data file for bulk printing and matches its columns to the template's fields.
    /// `separator` and `sheet` are the user's choices; left out, they are detected.
    pub fn parse_print_data(&self, template_id: i64, file: &Path, separator: Option<&str>, sheet: Option<usize>) -> ApiResult<PrintData> {
        let bytes = std::fs::read(file).map_err(ApiError::io)?;
        let name = file.file_name().and_then(|n| n.to_str()).unwrap_or("file").to_string();
        let part = multipart::Part::bytes(bytes)
            .file_name(name)
            .mime_str("application/octet-stream")
            .map_err(|e| ApiError { status: None, message: e.to_string() })?;
        let mut form =
            multipart::Form::new().part("file", part).text("templateId", template_id.to_string()).text("locale", crate::i18n::language());
        if let Some(separator) = separator {
            form = form.text("separator", separator.to_string());
        }
        if let Some(sheet) = sheet {
            form = form.text("sheet", sheet.to_string());
        }
        self.json(self.http.post(self.url("/print-data/parse")).multipart(form))
    }

    /// The signed-in account's saved bulk print setups for a template.
    pub fn bulk_print_profiles(&self, template_id: i64) -> ApiResult<Vec<BulkPrintProfile>> {
        self.get(&format!("/templates/{template_id}/bulk-print-profiles"))
    }

    /// Saves a profile. A name that is taken answers 409 unless `replace` is set.
    pub fn save_bulk_print_profile(
        &self,
        template_id: i64,
        name: &str,
        settings: &BulkPrintProfileSettings,
        replace: bool,
    ) -> ApiResult<BulkPrintProfile> {
        self.put(
            &format!("/templates/{template_id}/bulk-print-profiles"),
            &json!({ "name": name, "settings": settings, "replace": replace }),
        )
    }

    pub fn delete_bulk_print_profile(&self, id: i64) -> ApiResult<()> {
        self.call(reqwest::Method::DELETE, &format!("/bulk-print-profiles/{id}"), None)
    }

    /// Lets the engine get the data from a web address (a REST endpoint, or a file over HTTP)
    /// and read it like a file.
    pub fn fetch_print_data(
        &self,
        template_id: i64,
        url: &str,
        header_name: &str,
        header_value: &str,
        separator: Option<&str>,
        sheet: Option<usize>,
    ) -> ApiResult<PrintData> {
        self.post(
            "/print-data/fetch",
            &json!({
                "templateId": template_id,
                "url": url,
                "headerName": Some(header_name).filter(|name| !name.is_empty()),
                "headerValue": Some(header_value).filter(|value| !value.is_empty()),
                "separator": separator,
                "sheet": sheet,
                "locale": crate::i18n::language(),
            }),
        )
    }

    /// Checks rows the way a print job would: required fields, barcodes, and a trial render.
    pub fn check_rows(&self, template_id: i64, rows: &[std::collections::BTreeMap<String, String>]) -> ApiResult<Vec<CheckedRow>> {
        self.post::<CheckedRows>(&format!("/templates/{template_id}/check-rows"), &json!({ "rows": rows })).map(|checked| checked.rows)
    }

    pub fn delete_print_job(&self, id: i64) -> ApiResult<()> {
        self.call(reqwest::Method::DELETE, &format!("/print-jobs/{id}"), None)
    }

    pub fn delete_all_print_jobs(&self) -> ApiResult<DeleteAllResult> {
        self.json(self.http.delete(self.url("/print-jobs")))
    }

    // Settings, statistics, backups ----------------------------------------------------------

    pub fn health(&self) -> ApiResult<Health> {
        self.get("/health")
    }

    pub fn stats(&self) -> ApiResult<DashboardStats> {
        self.get("/stats")
    }

    pub fn reset_stats(&self, restore: bool) -> ApiResult<DashboardStats> {
        let request = if restore { self.http.delete(self.url("/stats/reset")) } else { self.http.post(self.url("/stats/reset")) };
        self.json(request)
    }

    pub fn settings(&self) -> ApiResult<AppSettings> {
        self.get("/settings")
    }

    pub fn update_settings(&self, patch: &Value) -> ApiResult<AppSettings> {
        self.put("/settings", patch)
    }

    pub fn check_updates(&self, refresh: bool) -> ApiResult<UpdateCheck> {
        self.get(if refresh { "/updates?refresh=true" } else { "/updates" })
    }

    pub fn backups(&self, kind: &str) -> ApiResult<Vec<Backup>> {
        self.get(&format!("/backups/{kind}"))
    }

    pub fn create_backup(&self, kind: &str) -> ApiResult<Backup> {
        self.post(&format!("/backups/{kind}"), &json!({}))
    }

    pub fn restore_backup(&self, kind: &str, file: &str) -> ApiResult<RestoreResult> {
        self.post(&format!("/backups/{kind}/{}/restore", urlencoding::encode(file)), &json!({}))
    }

    pub fn delete_backup(&self, kind: &str, file: &str) -> ApiResult<()> {
        self.call(reqwest::Method::DELETE, &format!("/backups/{kind}/{}", urlencoding::encode(file)), None)
    }

    pub fn download_backup(&self, kind: &str, file: &str) -> ApiResult<Vec<u8>> {
        self.bytes(self.http.get(self.url(&format!("/backups/{kind}/{}", urlencoding::encode(file)))))
    }
}

/// The engine's reason (problem details: detail, title or field errors), else the status.
fn problem(response: Response) -> ApiError {
    let status = response.status();
    let body: Option<Value> = response.json().ok();
    let from_body = body.as_ref().and_then(|problem| {
        let errors = problem
            .get("errors")
            .and_then(Value::as_object)
            .map(|errors| errors.values().filter_map(Value::as_array).flatten().filter_map(Value::as_str).collect::<Vec<_>>().join(" "));

        problem
            .get("detail")
            .and_then(Value::as_str)
            .map(str::to_string)
            .or_else(|| errors.filter(|text| !text.is_empty()))
            .or_else(|| problem.get("title").and_then(Value::as_str).map(str::to_string))
    });

    let message = from_body.unwrap_or_else(|| match status {
        StatusCode::FORBIDDEN => "Not allowed.".to_string(),
        _ => format!("Request failed with status {}.", status.as_u16()),
    });

    ApiError { status: Some(status.as_u16()), message }
}

fn mime_for(name: &str) -> &'static str {
    let lower = name.to_lowercase();
    match lower.rsplit('.').next().unwrap_or("") {
        "png" => "image/png",
        "jpg" | "jpeg" => "image/jpeg",
        "webp" => "image/webp",
        "svg" => "image/svg+xml",
        "tif" | "tiff" => "image/tiff",
        "bmp" => "image/bmp",
        _ => "application/octet-stream",
    }
}
