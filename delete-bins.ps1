# Get the location where this script is saved
$currentDir = $PSScriptRoot

Write-Host "Cleaning bin and obj folders in: $currentDir" -ForegroundColor Cyan

# Find only directories named bin or obj and remove them
Get-ChildItem -Path $currentDir -Include bin,obj -Recurse -Directory | ForEach-Object {
    Write-Host "Deleting: $($_.FullName)" -ForegroundColor Yellow
    $_ | Remove-Item -Recurse -Force
}

Write-Host "Cleanup complete!" -ForegroundColor Green