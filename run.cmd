@echo off
dotnet run --project "%~dp0src\AvaloniaBuilder" -c Release -- %*
