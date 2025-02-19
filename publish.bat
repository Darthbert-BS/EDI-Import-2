@echo off
setlocal

cls 

set rd=[91m 
set bl=[94m
set gr=[92m
set yl=[93m
set esc=[0m 


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
echo Error. Unknown parameter: %rd% %~1 %esc%
exit /b 1

:endParse

REM Check if required parameters are provided
if "%env%"=="" (
    echo %rd%The -env parameter is required%esc% [D=Development, S=Staging, P=Production ].
    exit /b 1
)
if "%dir%"=="" (
    echo %rd%The -dir parameter is required.%esc%
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
set src=%CD%\bin\%env%\net8.0\win-x64\publish

REM Example usage of the parameters
echo The environment is:%yl% %env% %esc%
echo The source directory is:%yl%  %src% %esc%
echo The output directory is:%yl%  %dir% %esc%

REM build and test application
set ORIGINAL_DIR=%CD%
cd "..\EDI Import 2025 Unit Tests"
echo %bl%Running Unit Tests...%esc%
REM dotnet test --verbosity:minimal --consoleLoggerParameters:ErrorsOnly
dotnet test --verbosity:quiet
REM Check if the build was successful
IF %ERRORLEVEL% NEQ 0 (
    echo %rd%Unit Tests failed! Error Level %ERRORLEVEL% %esc%
    exit /b %ERRORLEVEL%
)
echo %gr%[X]%esc% All Unit Tests Passed.

REM Build the console application
cd %ORIGINAL_DIR%
echo %bl%Building the project "EDI Import 2025.csproj"...%esc%
dotnet publish "EDI Import 2025.csproj" -c %env% -r win-x64 /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true --self-contained true --verbosity:quiet
REM Check if the build was successful
IF %ERRORLEVEL% NEQ 0 (
    echo %rd%[X]%esc% Build failed! Error Level %ERRORLEVEL%.
    exit /b %ERRORLEVEL%
)
echo %gr%[X]%esc% Project built successfully.

REM Create the target directory if it doesn't exist
IF NOT EXIST "%dir%" (
    echo %bl%Creating %dir% folder.%esc%
    mkdir "%dir%""
    echo %gr%[X]%esc% Folder created successfully.
)

REM Copy the necessary files to the target directory
echo %bl%Copying application files from %src% to %dir%\%env%...%esc%
xcopy /s /y /i /q "%src%" "%dir%/%env%/"
IF %ERRORLEVEL% NEQ 0 (
    echo %rd%[X]%esc% Error copying application files %ERRORLEVEL%.
    exit /b %ERRORLEVEL%
)
echo %gr%[X]%esc% Application files copied successfully.


echo %bl%Copying application support files from %src% to %dir%\%env%...%esc%
rem echo F|xcopy /S /Y /F /I "%cd%\appsettings.%env%.json" "%dir%\%env%\appsettings.EdiImport.json"
rem xcopy /s /y /i "%cd%\*disabled.txt" "%dir%\%env%\"
echo F|xcopy /S /Y /F /I /Q "readme.md" "%dir%\%env%\EDI Import Readme.md"
IF %ERRORLEVEL% NEQ 0 (
    echo %rd%[X]%esc% Error copying sup[port files] %ERRORLEVEL%.
    exit /b %ERRORLEVEL%
)


if /i "%env%" neq "Production" (
    echo %bl%Copying test files tp %env% %esc%
    xcopy /y /i /q "TestData\*.txt" "%dir%\%env%\TestData"
    IF %ERRORLEVEL% NEQ 0 (
        echo %rd%[X]%esc% Error copying Test data %ERRORLEVEL%.
        exit /b %ERRORLEVEL%
    )
    echo %gr%[X]%esc% Application support files copied successfully.
)


REM Notify the user that the process is complete
echo %gr%[X]%esc% Deployment to %env% completed successfully!

endlocal
pause