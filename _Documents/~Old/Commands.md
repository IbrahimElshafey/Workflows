# Migration Commands
Add-Migration -Name "Initial" -verbose

Add-Migration -Context "EngineDataContext" -Name "Initial" -Project Workflow.Engine.Data.Sqlite -Verbose -StartupProject Workflow.Engine.Service


# Force Migration

# Update DataBase
Update-Database -verbose
Update-Database -Context "EngineDataContext" -StartupProject Workflow.Engine.Service

# Remove-Migration 
Remove-Migration
Remove-Migration -Project Workflow.Engine.Data.SqlServer -Verbose -StartupProject Workflow.Engine.Service

# Commands Page
https://learn.microsoft.com/en-us/ef/core/cli/powershell


# Nuget
dotnet nuget push Workflows.Handler.1.0.0.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
dotnet nuget push Workflows.AspNetService.1.0.0.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
dotnet nuget push Workflows.Publisher.1.0.0.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json

Nuget cache C:\Users\Administrator\.nuget\packages

Admin123 postgress