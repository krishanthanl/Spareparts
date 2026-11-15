# Keep SDK and NuGet caches inside this checkout in restricted environments.
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '../.local/dotnet'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '../.local/nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
& dotnet @args
exit $LASTEXITCODE
