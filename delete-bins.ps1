# Get the location where this script is saved
$currentDir = $PSScriptRoot

try {
    Write-Host "Cleaning bin and obj folders in: $currentDir" -ForegroundColor Cyan

    # Find only directories named bin or obj and remove them
    # ErrorAction Stop ensures the catch block is triggered for non-terminating errors
    $folders = Get-ChildItem -Path $currentDir -Include bin, obj -Recurse -Directory -ErrorAction Stop

    foreach ($folder in $folders) {
        try {
            Write-Host "Deleting: $($folder.FullName)" -ForegroundColor Yellow
            $folder | Remove-Item -Recurse -Force -ErrorAction Stop
        }
        catch {
            Write-Host "Failed to delete: $($folder.FullName). Reason: $($_.Exception.Message)" -ForegroundColor Red
        }
    }

    Write-Host "Cleanup process finished!" -ForegroundColor Green
}
catch {
    Write-Host "A critical error occurred during the search: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host "`nPress Enter to exit..."
Read-Host