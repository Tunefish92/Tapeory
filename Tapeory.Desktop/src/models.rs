//! The engine's JSON, as the web UI's api/*.ts modules describe it (camelCase on the wire).

use std::collections::BTreeMap;

use chrono::{DateTime, Utc};
use serde::{Deserialize, Serialize};

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct SetupStatus {
    pub configured: bool,
    pub desktop: bool,
}

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct DatabaseSetupRequest {
    pub host: String,
    pub port: u16,
    pub database: String,
    pub user: String,
    pub password: String,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct DatabaseTestResult {
    pub is_success: bool,
    pub error_message: Option<String>,
    pub database_exists: bool,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct Health {
    pub status: String,
    pub storage_path: String,
    pub database_connected: bool,
}

#[derive(Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct CurrentUser {
    pub id: i64,
    pub user_name: String,
    pub display_name: String,
    pub role: String,
    pub must_change_password: bool,
}

impl CurrentUser {
    pub fn is_admin(&self) -> bool {
        self.role == "Admin"
    }
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct AuthState {
    pub has_users: bool,
    pub user: Option<CurrentUser>,
    pub desktop: bool,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct UserResponse {
    pub id: i64,
    pub user_name: String,
    pub display_name: String,
    pub role: String,
    pub disabled: bool,
    pub must_change_password: bool,
    pub created_at: Option<DateTime<Utc>>,
    pub last_login_at: Option<DateTime<Utc>>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct TemporaryPassword {
    pub user: UserResponse,
    pub temporary_password: String,
}

#[derive(Deserialize, Serialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct TemplateField {
    pub name: String,
    pub label: Option<String>,
    pub default_value: Option<String>,
    pub required: bool,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct TemplateVersion {
    pub version_number: i32,
    pub width_mm: f64,
    pub height_mm: f64,
    pub editor_json: String,
    pub fields: Vec<TemplateField>,
    pub preview_image_url: Option<String>,
    pub created_at: Option<DateTime<Utc>>,
}

fn yes() -> bool {
    true
}

#[derive(Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct TemplateSummary {
    pub id: i64,
    pub name: String,
    #[serde(default)]
    pub description: Option<String>,
    #[serde(default)]
    pub category: Option<String>,
    #[serde(default)]
    pub tags: Vec<String>,
    #[serde(default)]
    pub status: String,
    #[serde(default)]
    pub current_version_number: i32,
    #[serde(default)]
    pub width_mm: f64,
    #[serde(default)]
    pub height_mm: f64,
    #[serde(default)]
    pub updated_at: Option<DateTime<Utc>>,
    /// Set when the template was imported from a P-touch Editor file.
    #[serde(default)]
    pub source_lbx_url: Option<String>,
    #[serde(default = "yes")]
    pub is_public: bool,
    #[serde(default)]
    pub owner_name: Option<String>,
    #[serde(default = "yes")]
    pub can_edit: bool,
    #[serde(default)]
    pub is_mine: bool,
}

#[derive(Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct TemplateDetail {
    pub id: i64,
    pub name: String,
    #[serde(default)]
    pub description: Option<String>,
    #[serde(default)]
    pub category: Option<String>,
    #[serde(default)]
    pub tags: Vec<String>,
    #[serde(default)]
    pub status: String,
    #[serde(default)]
    pub source_lbx_url: Option<String>,
    #[serde(default)]
    pub conversion_warnings: Vec<String>,
    pub current_version: TemplateVersion,
    #[serde(default = "yes")]
    pub is_public: bool,
    #[serde(default)]
    pub owner_name: Option<String>,
    #[serde(default = "yes")]
    pub can_edit: bool,
}

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct CreateTemplateRequest {
    pub name: String,
    pub description: Option<String>,
    pub category: Option<String>,
    pub tags: Option<Vec<String>>,
    pub width_mm: f64,
    pub height_mm: f64,
    pub editor_json: String,
    pub fields: Vec<TemplateField>,
}

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct UpdateTemplateRequest {
    pub name: String,
    pub description: Option<String>,
    pub category: Option<String>,
    pub tags: Option<Vec<String>>,
    pub status: Option<String>,
}

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct CreateVersionRequest {
    pub width_mm: f64,
    pub height_mm: f64,
    pub editor_json: String,
    pub fields: Vec<TemplateField>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct UploadedImage {
    pub id: i64,
    pub url: String,
    pub original_file_name: String,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct EncodedBarcode {
    pub columns: usize,
    pub rows: usize,
    pub two_dimensional: bool,
    pub modules: String,
    pub text: String,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct PrintResolution {
    pub quality: String,
    pub horizontal_dpi: i32,
    pub vertical_dpi: i32,
}

#[derive(Deserialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct Printer {
    pub id: i64,
    pub name: String,
    #[serde(default)]
    pub model: Option<String>,
    pub connection_type: String,
    #[serde(default)]
    pub address: Option<String>,
    #[serde(default)]
    pub port: i32,
    #[serde(default)]
    pub print_server_address: Option<String>,
    #[serde(default)]
    pub usb_identifier: Option<String>,
    #[serde(default)]
    pub queue_name: Option<String>,
    #[serde(default)]
    pub label_media_width_mm: Option<f64>,
    #[serde(default)]
    pub label_media_height_mm: Option<f64>,
    #[serde(default)]
    pub is_default: bool,
    #[serde(default)]
    pub enabled: bool,
    #[serde(default)]
    pub last_connection_status: String,
    #[serde(default)]
    pub last_error_message: Option<String>,
    #[serde(default)]
    pub resolutions: Vec<PrintResolution>,
    #[serde(default)]
    pub cut_modes: Vec<String>,
    #[serde(default)]
    pub computer_name: Option<String>,
    #[serde(default = "yes")]
    pub on_this_computer: bool,
}

#[derive(Serialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase")]
pub struct PrinterRequest {
    pub name: String,
    pub model: Option<String>,
    pub connection_type: String,
    pub address: Option<String>,
    pub port: Option<i32>,
    pub print_server_address: Option<String>,
    pub usb_identifier: Option<String>,
    pub queue_name: Option<String>,
    pub label_media_width_mm: Option<f64>,
    pub label_media_height_mm: Option<f64>,
    pub enabled: bool,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct PrinterModel {
    pub name: String,
    pub family: String,
    pub network: bool,
    pub dpi: i32,
    pub high_resolution: bool,
    pub two_color: bool,
    pub cut_modes: Vec<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct UsbPrinter {
    pub identifier: String,
    pub name: String,
    pub model: Option<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct ActionResult {
    pub is_success: bool,
    pub error_message: Option<String>,
    pub loaded_tape_mm: Option<f64>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct PrinterStatus {
    pub available: bool,
    pub loaded_tape_mm: Option<f64>,
    pub display: Option<String>,
    pub problem: Option<String>,
}

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct PrintJobItemRequest {
    pub field_values: BTreeMap<String, String>,
    pub quantity: u32,
}

#[derive(Serialize, Clone, Debug)]
#[serde(rename_all = "camelCase")]
pub struct CreatePrintJobRequest {
    pub template_id: i64,
    pub printer_id: Option<i64>,
    pub printer_name: Option<String>,
    pub items: Vec<PrintJobItemRequest>,
    pub quality: Option<String>,
    pub cut_mode: Option<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct PrintJobItem {
    pub id: i64,
    pub field_values: BTreeMap<String, String>,
    pub quantity: u32,
    pub status: String,
    pub error_message: Option<String>,
    pub preview_url: Option<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct PrintJob {
    pub id: i64,
    pub template_id: i64,
    pub template_name: String,
    pub template_deleted: bool,
    pub template_version_number: i32,
    pub printer_id: Option<i64>,
    pub printer_name: Option<String>,
    pub printed_by: Option<String>,
    pub quality: Option<String>,
    pub cut_mode: Option<String>,
    pub status: String,
    pub error_message: Option<String>,
    pub created_at: Option<DateTime<Utc>>,
    pub completed_at: Option<DateTime<Utc>>,
    pub items: Vec<PrintJobItem>,
}

impl PrintJob {
    pub fn label_count(&self) -> u32 {
        self.items.iter().map(|item| item.quantity).sum()
    }

    pub fn in_progress(&self) -> bool {
        matches!(self.status.as_str(), "Queued" | "Processing" | "Sending" | "Printing")
    }

    /// Rows that are through: printed, failed or left out.
    pub fn finished_rows(&self) -> usize {
        self.items.iter().filter(|item| matches!(item.status.as_str(), "Completed" | "Failed" | "Cancelled")).count()
    }

    /// Rows that didn't come out of the printer and can be printed again.
    pub fn unprinted_rows(&self) -> usize {
        self.items.iter().filter(|item| matches!(item.status.as_str(), "Failed" | "Cancelled")).count()
    }
}

/// The margins of a label (in mm) that the printer can't print on.
#[derive(Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct PrintArea {
    pub top_mm: f64,
    pub right_mm: f64,
    pub bottom_mm: f64,
    pub left_mm: f64,
}

#[derive(Serialize, Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct BulkPrintProfileColumn {
    pub field: String,
    /// The column's position in the file, from 0.
    pub column: usize,
    /// The column's header text, when the file has a header row.
    pub header: Option<String>,
}

/// Everything a bulk print needs to run again.
#[derive(Serialize, Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct BulkPrintProfileSettings {
    pub file_name: Option<String>,
    /// Where the file is on the computer that saved the profile (the web app can't know it).
    pub file_path: Option<String>,
    pub separator: Option<String>,
    pub sheet: Option<usize>,
    pub has_header: bool,
    pub columns: Option<Vec<BulkPrintProfileColumn>>,
    pub quantity_column: Option<usize>,
    pub quantity_header: Option<String>,
    pub printer_id: Option<i64>,
    pub printer_name: Option<String>,
    pub quality: Option<String>,
    pub cut_mode: Option<String>,
    /// How many times each label is printed (on top of a copies column).
    pub copies: Option<u32>,
    /// The web address the data comes from, instead of a file, with its optional request header.
    pub url: Option<String>,
    pub url_header_name: Option<String>,
    pub url_header_value: Option<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct BulkPrintProfile {
    pub id: i64,
    pub name: String,
    pub settings: BulkPrintProfileSettings,
}

/// A data file (Excel, CSV, text) as the engine read it for bulk printing.
#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct PrintData {
    /// "text", "spreadsheet", or "json" (records whose property names are always the header).
    pub kind: String,
    pub sheets: Vec<String>,
    pub sheet: usize,
    /// "," ";" "|" …, "tab", "space", or "none" for one value per line.
    pub separator: Option<String>,
    /// False when the file doesn't say clearly which separator it uses: ask the user.
    pub separator_detected: bool,
    /// The first row names the columns and isn't a label itself.
    pub has_header: bool,
    /// Every row, the header included; all rows have the same number of cells.
    pub rows: Vec<Vec<String>>,
    /// Field name → column index, for the fields matched by the header.
    pub fields: BTreeMap<String, usize>,
    pub quantity_column: Option<usize>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct CheckedRow {
    /// Why this row can't be printed.
    pub errors: Vec<String>,
    /// What may look wrong on the label; the row can still be printed.
    pub warnings: Vec<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct CheckedRows {
    pub rows: Vec<CheckedRow>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct DeleteAllResult {
    pub deleted: i32,
    pub skipped_in_progress: i32,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct DashboardStats {
    pub template_count: i64,
    pub printer_count: i64,
    pub print_job_count: i64,
    pub completed_print_job_count: i64,
    pub failed_print_job_count: i64,
    pub labels_printed: i64,
    pub total_printed_length_mm: f64,
    pub total_printed_area_mm2: f64,
    pub most_printed_template_name: Option<String>,
    pub most_printed_template_count: i64,
    pub last_printed_at: Option<DateTime<Utc>>,
    pub stats_since: Option<DateTime<Utc>>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct UpdateCheck {
    pub current_version: String,
    pub latest_version: Option<String>,
    pub update_available: bool,
    pub release_url: Option<String>,
    pub error_message: Option<String>,
    /// The release's files; the desktop app downloads its own from these.
    #[serde(default)]
    pub assets: Option<Vec<ReleaseAsset>>,
}

#[derive(Deserialize, Clone, Debug, Default, PartialEq)]
#[serde(rename_all = "camelCase", default)]
pub struct ReleaseAsset {
    pub name: String,
    pub download_url: String,
    pub size: u64,
    pub sha256: Option<String>,
}

#[derive(Deserialize, Serialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct AppSettings {
    pub language: Option<String>,
    pub theme: Option<String>,
    pub unit: Option<String>,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct Backup {
    pub file_name: String,
    pub size_bytes: i64,
    pub created_at: Option<DateTime<Utc>>,
    pub before_restore: bool,
}

#[derive(Deserialize, Clone, Debug, Default)]
#[serde(rename_all = "camelCase", default)]
pub struct RestoreResult {
    pub restored_file_name: String,
    pub restored_templates: Option<i32>,
}
