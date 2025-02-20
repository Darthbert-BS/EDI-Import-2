/*
 *  ***************************************************************
 *  *                                                             *
 *  *  Copyright (c) 2014 Bundaberg Sugar Ltd, Australia          *
 *  *                                                             *
 *  *   All Rights Reserved.                                      *
 *  *                                                             *
 *  ***************************************************************
 *
 *  Authors :     Malcolm Byrne / Maxine Harwood      14-OCT-2014
 *
 *  Program:      EDI Import
 *
 *  Synopsis:     Command line utility to import EDI order information into SysPro Custom Database.
 *
 *  Libraries:    CsvHelper   http://joshclose.github.io/CsvHelper/
 *
 *  Deploy:       C:\Program Files\BBS\Focus\EDIImport\EDIImport.exe
 * 
 *  Log Files:    C:\ProgramData\BBS\focus\log\EDIImport\EDIImport.log
 *                
 * CHANGES
 * ==============================================================================================
 * 09-DEC-2014  1.0.343.1  John Chen  Modify insertEDIHeader() to handle [DeliveryTime] and [DocNumber].                
 * 
 * 10-DEC-2014  1.0.344.1  John Chen  Rename RequestedShipDate to RequestedDeliveryDate. 
 *
 * 29-DEC-2014  1.0.363.1  Malcolm Byrne  New input path.
 *
 * 07-JUL-2015  1.1.188.1  John Chen  Added function to transfer StockCode to GTIN. 
 * 
 * 03-AUG-2015  1.1.215.1  John Chen  Added new functions to get Unit Price for ALDI from the Contract.
 * 
 * 03-AUG-2015  1.1.215.2  John Chen  Added new functions to calculate total value for ALDI.
 * 
 * 04-AUG-2015  1.1.216.1  John Chen  Calculate unpalleted quantities and values for ALDI orders (order unit may be pallet).
 * 
 * 05-AUG-2015  1.1.217.1  John Chen  Added ALDI product code handling (the info is needed in ALDI invoices) and stopped getting unit price and value.
 * 
 * 07-AUG-2015  1.1.219.1  John Chen  Enhanced error handling. Place sent-in StockCode to AdditonalPartNumber for other customers.
 * 
 * 18-SEP-2015  1.1.261.1  John Chen  Enhanced error handling. Archive error messages to a folder and log the error.
 * 
 * 16-MAY-2023  1.9.136.1  Malcolm Byrne   Added new pricing method for ALDI V2
 * 
 * 14-02-2025   2.0.0.0    Alberto Bonfiglio  Ported application to .Net8, changed moving files to archive folder to storing as text in a table 
 *
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using BundabergSugar.Core;
using BundabergSugar.EDIImport.Services;

namespace BundabergSugar.EDIImport;

internal class Program {
    private static readonly EventId AppEventId = new(1000, "Main");

        /// <summary>
        /// The Main function is the entry point of the application.
        /// </summary>
        /// <param name="args">The command line arguments.</param>
        /// <remarks>
        /// The program will exit with a code of 1 if an error occurs. Otherwise, it will exit with a code of 0.
        /// </remarks>
    static async Task Main(string[] args) {
        bool interactiveMode = false;
        try {

        #region Command Line Arguments
            CheckEnvironment(args);
            interactiveMode = IsInteractive(args);    
            ShowVersion(args);
            ShowHelp(args);

        #endregion

        #region Configure the application            
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            ConfigurationManager config = builder.Configuration;
            IHostEnvironment env = builder.Environment;
            IServiceCollection services = builder.Services;

            // Setting up the application configuration
            config.Sources.Clear();
            config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            if (!env.IsProduction()) {
                config.AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);
            }
            config.AddEnvironmentVariables();

            //Setting up Logging 
            services.AddLoggingServices(env);
            
            // Setting up the dependency injection Services
            services.AddServices(config);

        #endregion

        #region Build and run the application 
            using IHost host = builder.Build();
           
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            var appOptions = host.Services.GetRequiredService<IApplicationOptions>();
            try {
                logger.LogInformation(AppEventId, "Starting App: {assembly.Name}, Version: {assembly.Version}, Environment: {app}", 
                    Common.GetAppName(), Common.GetAppVersion(), appOptions.Environment);

                logger.LogInformation(AppEventId, "Connecting to: {conn}", appOptions.ConnectionString);

                logger.LogInformation(AppEventId, "Program Started. User ID = {userName}", Common.GetUserName());                

                var ImportService = host.Services.GetRequiredService<Services.Import.IImportService>();
                await ImportService.ImportAsync();    
            
                logger.LogInformation(AppEventId, "Program Completed.");

                WaitForInput(interactiveMode);   
                
                Environment.Exit(0);   
            
            } catch (Exception ex) {
                logger.LogCritical(AppEventId, ex, "An error occurred creating the Application. {ex.msg}", ex.Message);
                WaitForInput(interactiveMode);   
                Environment.Exit(1);   
            }

        #endregion 

        } catch (Exception ex) {
            Console.WriteLine(ex.Message);
            WaitForInput(interactiveMode);
            Environment.Exit(1);   
        }
    }



    /// <summary>
    /// Checks if there is an environment parameter passed as a command line argument.
    /// If no environment argument is found and DOTNET_ENVIRONMENT IS NOT set, sets DOTNET_ENVIRONMENT to production.
    /// If an environment argument is passed, check for valididy and sets DOTNET_ENVIRONMENT to the value passed.
    /// If the enviroment variable is invalid, returns an error and exits the app .
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    private static void CheckEnvironment(string[] args) {
        string environment = string.Empty;        
        var environments =  new[] { "development", "staging", "production" };
        
        if (args.Length == 0) {
            // Set the environment variable
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"))) {
                Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", "production");    
            }
            return;
        }

        // check if there is an environment parameter    
        for (int i = 0; i < args.Length; i++) {
            if ((args[i] == "--environment" || args[i] == "-e") && i + 1 < args.Length) {
                environment = args[i + 1].Trim().ToLower();
                if (Array.Exists(environments, e => e == environment)) {
                    Environment.SetEnvironmentVariable("DOTNET_ENVIRONMENT", environment);                
                    return;
                }
                Console.WriteLine($"Invalid Environment: {environment}");
                Environment.Exit(1);
            }
        }
    }


    /// <summary>
    /// Checks if the --interactive or -i flag was passed as a command line argument.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    /// <returns>True if the --interactive or -i flag was passed, false otherwise.</returns>
    private static bool IsInteractive(string[] args) {
        return args.Contains("--interactive") || args.Contains( "-i");
    }


    /// <summary>
    /// Waits for the user to press a key before exiting, displaying a message.
    /// <para>
    /// This method is used to prevent the console window from closing immediately
    /// when the application is run manually. It is called after the application
    /// has completed.
    /// </para>
    /// </summary>
    /// <param name="enabled">True to wait for a key press, false otherwise.</param>
    private static void WaitForInput(bool enabled = false) {
        if (enabled) {
            Console.WriteLine("Press any key to continue...");
            Console.Read();            
            Console.WriteLine("Goodbye!");
        }
    }


    /// <summary>
    /// Show the version of the application and exit.
    /// <para>
    /// This method is called when either the --version or -v argument is passed
    /// to the application. It displays the name, build version, environment,
    /// current user, and interactive mode settings and then exits with 0.
    /// </para>
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    public static void ShowVersion(string[] args){
        if (args.Contains("--version") || args.Contains( "-v")) {
            Console.WriteLine($"Name: {Common.GetAppName()}");
            Console.WriteLine($"Build: {Common.GetAppVersion()}");
            Console.WriteLine($"Environment: {Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")}");
            Console.WriteLine($"Current User: {Common.GetUserName()}");
            Console.WriteLine($"Interactive mode: {IsInteractive(args)}");
            WaitForInput(true);
            Environment.Exit(0);
        }
    }


    /// <summary>
    /// Shows the help for the application, including the command line arguments,
    /// the application options, and the logger options.
    /// </summary>
    /// <param name="args">The command line arguments.</param>
    ///
    public static void ShowHelp(string[] args){
        if (args.Contains("--help") || args.Contains( "-h")) {
            Console.WriteLine("EDI Import Configuration Help.");
            Console.WriteLine($"App version: {Common.GetAppVersion()}");
            Console.WriteLine("");

            Console.WriteLine("Command Line Arguments.");
            Console.WriteLine("  --environment (-e): Sets the environment variable DOTNET_ENVIRONMENT to the value passed (development, staging, production).");
            Console.WriteLine("  --interactive (-i): Pauses execution at the end for troubleshooting.");
            Console.WriteLine("  --help        (-h): Shows the help.");
            Console.WriteLine("  --version     (-v): Shows the current version.");
            Console.WriteLine("");

            Console.WriteLine("Application Options.");
            Console.WriteLine("Values for configuring the application appsettings.<ENVIRONMENT>.json file. Remember production is the default and uses appsettings.json.");            
            Console.WriteLine("  ApplicationOptions");
            Console.WriteLine("    Environment: The running environment, Can be any of Development, Staging, Production");
            Console.WriteLine("    ConnectionString: The connection string to the database. ");
            Console.WriteLine("        Example: server=SYSPRO;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true; MultipleActiveResultSets=true");
            Console.WriteLine("    Disabled: If true, the application will be disabled. Default is false");
            Console.WriteLine("    DisabledFileLocation: An alternative method to disable the application."); 
            Console.WriteLine("        Set to the path of the disabled.txt file. E.g.: C:/Program Files/BBS/FOCUS/EDI Import/disabled.txt");
            Console.WriteLine("    DFMID: The DFM ID. Default is 15");
            Console.WriteLine("    Company: The company ID. Default is 2");
            Console.WriteLine("    InputFileLocation: The location of the input files.");
            Console.WriteLine("        Example: C:/Program Files/BBS/FOCUS/EDI Import/Input/");
            Console.WriteLine("");

            Console.WriteLine("Logger Options.");
            Console.WriteLine("Values for configuring the application Loggers");            
            Console.WriteLine("  FileLogger");
            Console.WriteLine("    FilePath: The path where to write tj=he log file.");
            Console.WriteLine("        Example: C:/Program Files/BBS/FOCUS/EDI Import/");
            Console.WriteLine("    FileName: The log file name. Default is EdiImport.log");
            Console.WriteLine("    MaxFileSizeMB: The maximum size of the log file inn Megabytes. Default is 10. Set to 0 to disable chunking.");
            Console.WriteLine("  DBLogger");
            Console.WriteLine("    ConnectionString: The connection string to the database. ");
            Console.WriteLine("        Example: server=SYSPRO;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true; MultipleActiveResultSets=true");
            
            Console.WriteLine("    TargetName: The name of the target table or Stored procedure. ");
            Console.WriteLine("        Example: [dbo].[EventLog]");
            Console.WriteLine("    TargetType: The type of the target: Table or StoredProcedure. ");
            Console.WriteLine("        StoredProcedure is not implemented yet");
            Console.WriteLine("    Parameters: An array of {FieldType, FieldName} parameters. ");
            Console.WriteLine("        FieldName is the name of the column and @parameter to update. Case sensitive.");
            Console.WriteLine("        FieldType is the type of data to store. Can be any of the following: [timeStamp, logLevel, system, subSystem, message, exception, stackTrace]. Case sensitive.");
            Console.WriteLine("");

            WaitForInput(true);
            Environment.Exit(0); // exits successfully
        };
    }


}
