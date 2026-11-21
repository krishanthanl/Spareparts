# Applies pending EF Core migrations to a PostgreSQL database.
# Usage: ./scripts/migrate-db.ps1            (prompts for the connection URL)
#        $env:DATABASE_URL = "postgres://..."; ./scripts/migrate-db.ps1
# Accepts a postgres:// URL and converts it to the Npgsql key=value format.

$ErrorActionPreference = 'Stop'

$url = $env:DATABASE_URL
if ([string]::IsNullOrWhiteSpace($url)) {
    $secure = Read-Host -AsSecureString 'Postgres connection URL (postgres://user:pass@host:port/db?sslmode=require)'
    $url = [System.Net.NetworkCredential]::new('', $secure).Password
}
if ([string]::IsNullOrWhiteSpace($url)) { throw 'No connection URL given.' }

$uri = [Uri]$url
if ($uri.Scheme -notin 'postgres', 'postgresql') { throw "Expected a postgres:// URL, got '$($uri.Scheme)://'." }

$userInfo = $uri.UserInfo.Split(':', 2)
$user = [Uri]::UnescapeDataString($userInfo[0])
$pass = if ($userInfo.Count -gt 1) { [Uri]::UnescapeDataString($userInfo[1]) } else { '' }
$port = if ($uri.Port -gt 0) { $uri.Port } else { 5432 }
$db = $uri.AbsolutePath.TrimStart('/')
if ([string]::IsNullOrWhiteSpace($db)) { $db = 'postgres' }

$sslMode = 'Require'
foreach ($pair in $uri.Query.TrimStart('?').Split('&', [StringSplitOptions]::RemoveEmptyEntries)) {
    $k, $v = $pair.Split('=', 2)
    if ($k -eq 'sslmode' -and $v) { $sslMode = (Get-Culture).TextInfo.ToTitleCase($v.ToLower()) -replace '-', '' }
}

$env:ConnectionStrings__MigrationConnection =
    "Host=$($uri.Host);Port=$port;Database=$db;Username=$user;Password=$pass;SSL Mode=$sslMode"

$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    Write-Host "Restoring dotnet tools..."
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'dotnet tool restore failed.' }

    Write-Host "Applying migrations to $($uri.Host)/$db ..."
    dotnet ef database update `
        --project src/backend/Infrastructure `
        --startup-project src/backend/Infrastructure
    if ($LASTEXITCODE -ne 0) { throw 'dotnet ef database update failed.' }

    Write-Host 'Migrations applied.' -ForegroundColor Green
}
finally {
    Remove-Item Env:ConnectionStrings__MigrationConnection -ErrorAction SilentlyContinue
    Pop-Location
}
