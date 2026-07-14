$runtimeFound = $false

# Check if dotnet is installed and has the base version 8 runtime
try {
    $runtimes = dotnet --list-runtimes 2>$null
    if ($runtimes -match "Microsoft\.NETCore\.App 8\.") {
        $runtimeFound = $true
    }
} catch { }

if ($runtimeFound) {
    Write-Host "The required .NET 8 Runtime is already installed." -ForegroundColor Green
    Exit
}

Write-Host "ModHearth requires the standard .NET 8 Runtime to function." -ForegroundColor Yellow
$response = Read-Host "Would you like to automatically download and install it now? (Y/N)"

if ($response -match "^[Yy]$") {
    Write-Host "Installing .NET 8 Runtime..." -ForegroundColor Cyan
    
    # Installs the standard runtime headless
    winget install Microsoft.DotNet.Runtime.8 -e --accept-package-agreements --accept-source-agreements
    
    Write-Host "Installation complete! You can now launch ModHearth." -ForegroundColor Green
} else {
    Write-Host "Installation cancelled. You will need to install the runtime manually." -ForegroundColor Red
}
