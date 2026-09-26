#Requires -Version 5.1
<#
.SYNOPSIS
    Starts a local MySQL container (if Docker is available) and runs the Tapeory API.

.DESCRIPTION
    The MySQL container uses database "tapeory", user "tapeory", password "tapeory" on
    localhost:3306. On the first run the web UI asks for these in its setup screen and saves
    them to Tapeory.Api/local-storage/config/database.json; after that, database migrations
    apply automatically on startup. The API listens on http://localhost:5215, which
    Tapeory.Web's Vite dev server already proxies /api requests to.
#>

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

$dockerCommand = Get-Command docker -ErrorAction SilentlyContinue

if ($dockerCommand) {
    Write-Host "Starting local MySQL via Docker Compose..." -ForegroundColor Cyan
    docker compose -f docker-compose.dev.yml up -d --wait mysql

    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Docker Compose failed to start MySQL. Continuing anyway -- if you have your own MySQL server, enter its details in the setup screen on first run (or set ConnectionStrings__Default yourself)."
    }
}
else {
    Write-Warning "Docker was not found on PATH. Make sure a MySQL 8 server is reachable and enter its details in the setup screen on first run, or set the ConnectionStrings__Default environment variable yourself."
}

Write-Host "Starting the Tapeory API (http://localhost:5215)..." -ForegroundColor Cyan
dotnet run --project (Join-Path $repoRoot "Tapeory.Api\Tapeory.Api.csproj")
