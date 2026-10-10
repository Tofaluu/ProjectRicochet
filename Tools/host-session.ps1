<#
.SYNOPSIS
Hosts a play session: starts the game server and a Cloudflare quick tunnel, then prints (and copies)
the address friends paste into the game's Server field. Press Ctrl+C to stop both.

.NOTES
Quick tunnels get a new random address on every run and only live while this script does.
This is a stopgap until the server moves to a cloud host.
Requires cloudflared:  winget install --id Cloudflare.cloudflared
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$port = 5022

if (-not (Get-Command cloudflared -ErrorAction SilentlyContinue)) {
    Write-Host "cloudflared is not installed. Install it with:  winget install --id Cloudflare.cloudflared" -ForegroundColor Red
    exit 1
}

$logs = Join-Path ([IO.Path]::GetTempPath()) 'ricochet-session'
New-Item -ItemType Directory -Force $logs | Out-Null

Write-Host "Building the server..."
dotnet build "$root\BackendServer" -c Release -v q --nologo | Out-Null
if ($LASTEXITCODE -ne 0) { Write-Host "Server build failed." -ForegroundColor Red; exit 1 }

# Run the built exe directly (not 'dotnet run') so stopping it really stops the server.
$serverDir = "$root\BackendServer\bin\Release\net9.0"
$server = Start-Process "$serverDir\BackendServer.exe" -ArgumentList "--urls", "http://localhost:$port" `
    -WorkingDirectory $serverDir -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput "$logs\server.log" -RedirectStandardError "$logs\server.err.log"
$tunnel = $null

try {
    $deadline = (Get-Date).AddSeconds(30)
    while ($true) {
        try { Invoke-WebRequest -UseBasicParsing "http://localhost:$port/" | Out-Null; break } catch { }
        if ($server.HasExited -or (Get-Date) -gt $deadline) { throw "The server didn't start. See $logs\server.err.log" }
        Start-Sleep -Milliseconds 500
    }
    Write-Host "Server running on http://localhost:$port"

    Write-Host "Opening the tunnel..."
    # cloudflared logs to stderr, including the public address.
    $tunnel = Start-Process cloudflared -ArgumentList "tunnel", "--url", "http://localhost:$port" `
        -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput "$logs\tunnel.out.log" -RedirectStandardError "$logs\tunnel.log"

    $address = $null
    $deadline = (Get-Date).AddSeconds(45)
    while (-not $address) {
        $log = Get-Content "$logs\tunnel.log" -Raw -ErrorAction SilentlyContinue
        if ($log -match 'https://[a-z0-9-]+\.trycloudflare\.com') { $address = $Matches[0] }
        elseif ($tunnel.HasExited -or (Get-Date) -gt $deadline) { throw "The tunnel didn't open. See $logs\tunnel.log" }
        else { Start-Sleep -Milliseconds 500 }
    }

    $host_ = $address -replace '^https://', ''
    Set-Clipboard $host_
    Write-Host ""
    Write-Host "  Send this to your friends (already copied):" -ForegroundColor Green
    Write-Host "      $host_" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "  They paste it into the Server field. Everyone uses the same lobby code."
    Write-Host "  You can join from this PC with Server = localhost:$port"
    Write-Host ""
    Write-Host "Press Ctrl+C to end the session."

    while (-not $server.HasExited -and -not $tunnel.HasExited) { Start-Sleep -Seconds 2 }
    if ($server.HasExited) { Write-Host "The server stopped. See $logs\server.err.log" -ForegroundColor Red }
    if ($tunnel.HasExited) { Write-Host "The tunnel closed. See $logs\tunnel.log" -ForegroundColor Red }
}
finally {
    foreach ($p in @($tunnel, $server)) {
        if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
    }
    Write-Host "Session ended."
}
