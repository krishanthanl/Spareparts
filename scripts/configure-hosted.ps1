# Saves a hosted PostgreSQL connection (for example Prisma Postgres) to the API's user-secrets.
# Paste the DIRECT TCP url (postgres://user:password@host:5432/db?sslmode=require), not a prisma:// url.
# The value is read as a secure string, never echoed, and sent to user-secrets through standard input.
$ErrorActionPreference = 'Stop'
$secureUrl = Read-Host 'Hosted PostgreSQL direct connection URL (postgres://...)' -AsSecureString
$url = [Net.NetworkCredential]::new('', $secureUrl).Password
$connection = [System.Data.Common.DbConnectionStringBuilder]::new()
try {
    if ([string]::IsNullOrWhiteSpace($url)) { throw 'A connection URL is required.' }
    if ($url -match '^prisma(\+postgres)?://') { throw 'That is a Prisma proxy URL. Use the direct postgres:// connection from the Prisma console.' }
    $uri = [Uri]$url.Trim()
    if ($uri.Scheme -notin 'postgres', 'postgresql') { throw 'The URL must start with postgres:// or postgresql://.' }
    $userInfo = $uri.UserInfo.Split(':', 2)
    if ($userInfo.Count -ne 2) { throw 'The URL must include a user name and password.' }
    $database = [Uri]::UnescapeDataString($uri.AbsolutePath.TrimStart('/'))
    if ([string]::IsNullOrWhiteSpace($database)) { $database = 'postgres' }

    $connection['Host'] = $uri.Host
    $connection['Port'] = if ($uri.Port -gt 0) { $uri.Port } else { 5432 }
    $connection['Database'] = $database
    $connection['Username'] = [Uri]::UnescapeDataString($userInfo[0])
    $connection['Password'] = [Uri]::UnescapeDataString($userInfo[1])
    $connection['SSL Mode'] = 'VerifyFull'
    $connection['Maximum Pool Size'] = 5
    $connection['Minimum Pool Size'] = 0
    $connection['Timeout'] = 15

    $projectPath = Join-Path $PSScriptRoot '../src/backend/Api'
    @{
        'ConnectionStrings:DefaultConnection'   = $connection.ConnectionString
        'ConnectionStrings:MigrationConnection' = $connection.ConnectionString
    } | ConvertTo-Json | & dotnet user-secrets set --project $projectPath | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not save API configuration.' }
    Write-Host "Saved connection to $($uri.Host) (database '$database') in user-secrets."
} finally {
    $url = $null
    $connection.Clear()
    $secureUrl.Dispose()
}
