@echo off
setlocal

set TARGET_CONF=Staging
echo Configuiration=%TARGET_CONF% 

REM Set the target directory
set TARGET_DIR=D:\temp\EDI Import

REM Get the current directory as the project directory
set PROJECT_DIR=%cd%
echo %PROJECT_DIR%

REM Build the console application
echo Building the project...
REM dotnet build --configuration Staging 

dotnet publish -c %TARGET_CONF% -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true --self-contained true

REM Check if the build was successful
IF %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)

REM Create the target directory if it doesn't exist
IF NOT EXIST "%TARGET_DIR%" (
    mkdir "%TARGET_DIR%""
)

REM Copy the necessary files to the target directory
Set SRC="%PROJECT_DIR%\bin\%TARGET_CONF%\net8.0\win-x64\publish\*"
Set TGT="%TARGET_DIR%" 

REM Create the target directory if it doesn't exist
IF NOT EXIST %TGT% (
    mkdir %TGT%
    mkdir %TGT%\TestData\Input
    echo Directory %TGT% created
)
echo Copying application files from %SRC% to %TGT%...
xcopy /s /y /i %SRC% %TGT%

Set SRC="%PROJECT_DIR%\appsettings.%TARGET_CONF%.json"
echo Copying support files from %SRC% to %TGT%...
xcopy /s /y /i %SRC% %TGT%

Set SRC="%PROJECT_DIR%\_Disabled.txt"
echo Copying support files from %SRC% to %TGT%...
xcopy /s /y /i %SRC% %TGT%

Set SRC="%PROJECT_DIR%\readme.md"
echo Copying support files from %SRC% to %TGT%...
xcopy /s /y /i %SRC% %TGT%

Set SRC="%PROJECT_DIR%\TestData\*.txt"
Set TGT="%TARGET_DIR%\TestData\*.txt"
echo Copying support files from %SRC% to %TGT%...
xcopy /s /y /i %SRC% %TGT%

REM Notify the user that the process is complete
echo Deployment complete!

endlocal
pause


REM C:\Windows\Microsoft.NET\Framework\v4.0.30319\msbuild.exe "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import.sln" /p:Configuration=Staging
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\EDIImport.exe" "\\Titan\c$\Program Files\BBS\FOCUS\EDIImport.exe"
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\EDIImport.exe.config" "\\Titan\c$\Program Files\BBS\FOCUS\EDIImport.exe.config"
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\log4net.dll" "\\Titan\c$\Program Files\BBS\FOCUS\log4net.dll"
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\CsvHelper.dll" "\\Titan\c$\Program Files\BBS\FOCUS\CsvHelper.dll"