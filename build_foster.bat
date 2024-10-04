@echo off

if not defined DevEnvDir (
    call vcvarsall x64
)

cd "Foster.Framework/Platform"

if "%1" == "reset" (
   rmdir /S /Q "build"
)

mkdir build
cd build
call cmake ../
call cmake --build .

move /Y "..\libs\win-x64\Debug\FosterPlatform.dll" "..\libs\win-x64\FosterPlatform.dll" 
rmdir /S /Q "..\libs\win-x64\Debug"

:failed

cd ..

cd ..\..
