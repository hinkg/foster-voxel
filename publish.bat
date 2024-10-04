@echo off

rmdir /S /Q Game\bin
dotnet publish Game/Game.csproj -p:Configuration=Release