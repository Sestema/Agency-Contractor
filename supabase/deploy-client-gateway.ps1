$ErrorActionPreference = "Stop"

$ProjectRoot = Split-Path -Parent $PSScriptRoot
$ProjectRef = "tssgxhatnjvqthdiyuwo"

Write-Host "Deploying client-gateway to Supabase project $ProjectRef..." -ForegroundColor Cyan

if (-not $env:SUPABASE_ACCESS_TOKEN) {
    Write-Host ""
    Write-Host "Supabase access token is missing." -ForegroundColor Yellow
    Write-Host "Option 1: run login once in this terminal:" -ForegroundColor Yellow
    Write-Host "  npx supabase@latest login" -ForegroundColor White
    Write-Host ""
    Write-Host "Option 2: set token for current session:" -ForegroundColor Yellow
    Write-Host '  $env:SUPABASE_ACCESS_TOKEN = "your-token"' -ForegroundColor White
    Write-Host ""
    Write-Host "Token: https://supabase.com/dashboard/account/tokens" -ForegroundColor DarkGray
    exit 1
}

Push-Location $ProjectRoot
try {
    npx --yes supabase@latest functions deploy client-gateway `
        --project-ref $ProjectRef `
        --use-api

    if ($LASTEXITCODE -ne 0) {
        throw "client-gateway deploy failed with exit code $LASTEXITCODE"
    }

    Write-Host ""
    Write-Host "Done. Restart the desktop app and open Settings -> Users -> Shared folder and PCs." -ForegroundColor Green
}
finally {
    Pop-Location
}
