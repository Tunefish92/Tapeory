#Requires -Version 5.1
<#
.SYNOPSIS
    Installs frontend dependencies if needed and runs the Tapeory web app's dev server.

.DESCRIPTION
    Starts Vite on http://localhost:5173. Requires the API to be running separately (see
    run-backend.ps1) -- the dev server proxies /api requests to http://localhost:5215.
#>

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $repoRoot "Tapeory.Web"

$npmCommand = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npmCommand) {
    throw "npm was not found on PATH. Install Node.js (https://nodejs.org/) and try again."
}

Set-Location $webRoot

if (-not (Test-Path (Join-Path $webRoot "node_modules"))) {
    Write-Host "Installing frontend dependencies..." -ForegroundColor Cyan
    npm install
    if ($LASTEXITCODE -ne 0) {
        throw "npm install failed."
    }
}

Write-Host "Starting the Tapeory web app (http://localhost:5173)..." -ForegroundColor Cyan
npm run dev
