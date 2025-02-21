# A script to build and publish the application
param (
    [string]$Environment = "Production",
    [string]$Output,
    [string]$Project = ".\EDI Import 2025.csproj",
    [string]$UnitTestProject = "..\EDI Import 2025 Unit Tests\EDI Import 2025 Unit Tests.csproj",
    [string]$Architecture = "win-x64",
    [switch]$Verbose,
    [switch]$SkipTests,
    [switch]$Help
)

function ShowHelp {
    Write-Host "Usage: .\publish.ps1 -AppEnv <Environment> -AppOutput <OutputDirectory> -ProjectPath <ProjectPath> -Runtime <Runtime> [-Help]"
    Write-Host "Parameters:"
    Write-Host "  -Environment  The environment to publish to. Valid options are: Debug, Testing, Release."
    Write-Host "  -Output       The output directory for the published files."
    Write-Host "  -ProjectPath  The path to the project file (.csproj) to publish."
    Write-Host "  -Verbose      Display detailed build output."
    Write-Host "  -Help         Display this help message."
    exit 0
}

function ErrorHandler {
    param (
        [string]$Message,
        [string]$Details = ""
    )
    Write-Host "`e[91mError: $Message`e[0m"
    if ($Details -ne "") {
        Write-Host "`e[93m$Details`e[0m"
    }
    exit 1
}

function CheckParams {
    param (
        [string]$env,
        [string]$output
    )

    # Check if required parameters are provided
    if ([string]::IsNullOrEmpty($env)) {
        ErrorHandler "The --environment or -e parameter is required" "Valid options are: Development, Staging, Production."
    }
    if ([string]::IsNullOrEmpty($output)) {
        ErrorHandler "The --output or -o parameter is required. A valid path must be provided."
    }
    
    # Validate the environment parameter
    $AppEnv = $env.Substring(0, 1).ToUpper()
    switch ($AppEnv) {
        "T" { $AppEnv = "Testing" }
        "D" { $AppEnv = "Debug" }
        "R" { $AppEnv = "Release" }
        default { ErrorHandler "Invalid environment parameter. Must be D=Debug, T=Testing, R=Release." }
    }
    return $AppEnv
}

function RunUnitTest {
    $Verbosity = "--v:q"
    if ($Verbose) {
        $Verbosity = "--v:n"
    }
    if (!$SkipTests) {
        Write-Host "Running unit tests..."
        dotnet test $UnitTestProject $Verbosity
        if ($LASTEXITCODE -ne 0) {
            ErrorHandler "`e[91m[ ]`e[0m dotnet test failed with exit code `e[91m$LASTEXITCODE`e[0m"
        } else {
            Write-Host "`e[92m[X]`e[0m Unit tests passed"
        }
    }
}

function BuildApplication {
    $Verbosity = "--v:q"
    if ($Verbose) {
        $Verbosity = "--v:n"
    }
    Write-Host "Building application..."
    dotnet publish $Project -c $ValidatedEnv -r $Architecture /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true --self-contained true $Verbosity
    if ($LASTEXITCODE -ne 0) {
        ErrorHandler "`e[91m[ ]`e[0m dotnet build failed with exit code `e[91m$LASTEXITCODE`e[0m"
    } else {
        Write-Host "`e[92m[X]`e[0m Build succeeded"
    }
}

function CopyArtifacts {
    $Destination = "$Output\$ValidatedEnv"
    $Source=".\bin\$ValidatedEnv\net8.0\$Architecture\publish"

    if (-not (Test-Path -Path $Destination -PathType Container)) {
        try {
            New-Item -Path $Destination -ItemType Directory -ErrorAction Stop
            Write-Host "`e[92m[X]`e[0m  Created destination directory: $Destination"
        } catch {
            ErrorHandler "`e[91m[ ]`e[0m Failed to create destination directory." $_.Exception.Message

        }
    }
    
    Write-Host "Copying artifacts to $Destination..."
    Copy-Item -Path "$Source\*" -Destination $Destination -Recurse -Force -ErrorAction Stop
    Write-Host "`e[92m[X]`e[0m Application artifacts copied successfully"

    Write-Host "Copying application settings to $Destination..."
    Copy-Item -Path ".\appsettings.json" -Destination "$Destination\appsettings.$Environment.json"
    Copy-Item -Path ".\readme.md" -Destination $Destination
    Write-Host "`e[92m[X]`e[0m Application settings copied successfully"

    if ($ValidatedEnv -ne "Production") {
        $Source = Join-Path -Path ".\" -ChildPath "TestData"
        $Destination = Join-Path -Path $Destination -ChildPath "TestData"
        $txtFiles = Get-ChildItem -Path $source -Filter "*.txt" -Recurse
        try {
            if (-not (Test-Path -Path $Destination -PathType Container)) {
                New-Item -Path $Destination -ItemType Directory -ErrorAction Stop
                Write-Host "`e[92m[X]`e[0m  Created destination directory: $Destination"
            }

            Write-Host "Copying application test data from $Source to $Destination..."
            foreach ($file in $txtFiles) {
                $basename = $file.BaseName + $file.Extension;
                $destFilePath = Join-Path -Path $Destination -ChildPath "$BaseName"
                Copy-Item -Path "$file" -Destination $destFilePath -Force -ErrorAction Stop
                if ($Verbose){ 
                    Write-Host "Copied $($file.FullName) to $Destination"
                }
            }
            Write-Host "`e[92m[X]`e[0m Application Test Data copied successfully"
        } catch {
            ErrorHandler "`e[91m[ ]`e[0m Failed to copy application test data." $_.Exception.Message
        }
    }   
    
}

# Show help if -Help switch is present
if ($Help) {
    ShowHelp
}

# Parse and validate parameters
$ValidatedEnv = CheckParams -env $Environment -output $Output
Write-Host "`e[92m[X]`e[0m Validated environment: $ValidatedEnv  Architecture: $Architecture" 

# Run dotnet tests
RunUnitTest

# Run dotnet publish
BuildApplication

# Copy artifacts
CopyArtifacts

if ($LASTEXITCODE -ne 0) {
    ErrorHandler "`e[91m[ ]`e[0m dotnet publish failed with exit code $LASTEXITCODE"
} else {
    Write-Host "`e[92m[X]`e[0m Publish succeeded"
}