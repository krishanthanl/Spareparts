param([int]$Port = 5432)
$ErrorActionPreference = 'Stop'
$securePassword = Read-Host 'Local PostgreSQL runtime password (APP_DB_PASSWORD)' -AsSecureString
$password = [Net.NetworkCredential]::new('', $securePassword).Password
if ([string]::IsNullOrWhiteSpace($password)) { throw 'A password is required.' }
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
$connection['Host'] = 'localhost'
$connection['Port'] = $Port
$connection['Database'] = 'spareparts'
$connection['Username'] = 'spareparts_app'
$connection['Password'] = $password
$connection['Maximum Pool Size'] = 10
$connection['Minimum Pool Size'] = 0
$connection['Timeout'] = 5
$projectPath = Join-Path $PSScriptRoot '../src/backend/Api'
try {
    @{ 'ConnectionStrings:DefaultConnection' = $connection.ConnectionString } | ConvertTo-Json | & dotnet user-secrets set --project $projectPath
    if ($LASTEXITCODE -ne 0) { throw 'Could not save API configuration.' }
} finally {
    $password = $null
    $connection.Clear()
    $securePassword.Dispose()
}
