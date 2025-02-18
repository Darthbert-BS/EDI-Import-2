@echo off
setlocal

set "env="
set "dir="

REM Check if the parameter is provided
:parseParams
if "%~1"=="" goto endParse
if "%~1"=="-env" (
    set "env=%~2"
    shift
    shift
    goto parseParams
)

if "%~1"=="-dir" (
    set "dir=%~2"
    shift
    shift
    goto parseParams
)

REM Handle unknown parameters
echo Unknown parameter: %~1
exit /b 1

:endParse

REM Check if required parameters are provided
if "%env%"=="" (
    echo The -env parameter is required [D=Development, S=Staging, P=Production ].
    exit /b 1
)
if "%dir%"=="" (
    echo The -dir parameter is required.
    exit /b 1
)

REM Validate the environment parameter
set "env=%env:~0,1%"
if /i "%env%"=="S" (
    set "env=Staging"
) else if /i "%env%"=="D" (
    set "env=Development"
) else if /i "%env%"=="P" (
    set "env=Production"
) else (
    echo Invalid environment parameter. Must be D or Development, S or Staging, P or Production.
    exit /b 1
)

REM Sets the source directory
set src=%cd%\bin\%env%\net8.0\win-x64\publish\.

REM Example usage of the parameters
echo The environment is: %env%
echo The source directory is: %src%
echo The output directory is: %dir%

REM Build the console application
echo Building the project...
dotnet publish -c %env% -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true --self-contained true

REM Check if the build was successful
IF %ERRORLEVEL% NEQ 0 (
    echo Build failed!
    exit /b %ERRORLEVEL%
)

REM Create the target directory if it doesn't exist
IF NOT EXIST "%dir%" (
    mkdir "%dir%""
)

REM Copy the necessary files to the target directory
echo Copying application files from %src% to "%dir%\%env%\"...
xcopy /s /y /i "%src%" "%dir%\%env%\"

echo Copying settings files
rem echo F|xcopy /S /Y /F /I "%cd%\appsettings.%env%.json" "%dir%\%env%\appsettings.EdiImport.json"
rem xcopy /s /y /i "%cd%\*disabled.txt" "%dir%\%env%\"
echo F|xcopy /S /Y /F /I "%cd%\readme.md" "%dir%\%env%\EDI Import Readme.md"

if /i "%env%" neq "Production" (
    echo Copying test files tp %env%
    xcopy /y /i "%cd%\TestData\*.txt" "%dir%\%env%\TestData\"
)

REM Notify the user that the process is complete
echo Deployment to %env% completed successfully!

endlocal
pause


REM C:\Windows\Microsoft.NET\Framework\v4.0.30319\msbuild.exe "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import.sln" /p:Configuration=Staging
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\EDIImport.exe" "\\Titan\c$\Program Files\BBS\FOCUS\EDIImport.exe"
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\EDIImport.exe.config" "\\Titan\c$\Program Files\BBS\FOCUS\EDIImport.exe.config"
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\log4net.dll" "\\Titan\c$\Program Files\BBS\FOCUS\log4net.dll"
REM xcopy "D:\SourceTree\syspro_repository\Applications\EDI Import\EDI Import\bin\Staging\CsvHelper.dll" "\\Titan\c$\Program Files\BBS\FOCUS\CsvHelper.dll"