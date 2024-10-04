@echo off

rmdir /S /Q ".\Game\bin\Debug\net8.0\win-x64\Saves"
rmdir /S /Q ".\Game\bin\x64\Debug\net8.0\win-x64\Saves"
dotnet run --project Game/Game.csproj -defaultconfig -enter