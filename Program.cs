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
using BundabergSugar.EDIImport.Core;
using BundabergSugar.EDIImport.Services;

namespace BundabergSugar.EDIImport;

internal class Program {
    private static readonly EventId AppEventId = new(1000, "Main");

    static async Task Main(string[] args) {
        try {
        #region Configure the application            
            Console.WriteLine($"ENVIRONMENT: {Environment.GetEnvironmentVariable("ENVIRONMENT")}");
          
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
            builder.Configuration.Sources.Clear();
            
            IHostEnvironment env = builder.Environment;
            builder.Configuration.AddEnvironmentVariables();
            builder.Configuration
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", true, true);

            var configurationManager = builder.Configuration;
            var services = builder.Services;

            //Setting up Logging 
            if (env.IsDevelopment()) {
               services.AddLogging(logBuilder => logBuilder.AddDebug());
            } else {
               services.AddLogging(logBuilder => logBuilder.AddConsole());
            }
            services.AddLogging(logBuilder => logBuilder.AddDataBaseLogger());
            services.AddLogging(logBuilder => logBuilder.AddFileLogger());
           
            //Configuring the application options
            ApplicationOptions appOptions = new();  
            builder.Configuration.GetSection(nameof(ApplicationOptions)).Bind(appOptions);
            services.AddSingleton<IApplicationOptions>(appOptions);

            // Setting up the dependency injection Services
            services.AddSingleton<IDatabaseService, DatabaseService>();
            services.AddSingleton<IImportService, ImportService>();

        #endregion

        #region Build and run the application 
            using IHost host = builder.Build();
           
            var logger = host.Services.GetRequiredService<ILogger<Program>>();
            try {
                logger.LogInformation(AppEventId, "Starting App: {assembly.Name}, Version: {assembly.Version}, Environment: {env}", 
                    Utils.GetAppName(), Utils.GetAppVersion(), env.EnvironmentName);

                logger.LogInformation(AppEventId, "Program Started. User ID = {userName}", Utils.GetUserName());                

                var ImportService = host.Services.GetRequiredService<IImportService>();
                await ImportService.ImportAsync();    
            
                logger.LogInformation(AppEventId, "Program Completed.\r\n");
            
            } catch (Exception ex) {
                logger.LogError(AppEventId, ex, "An error occurred creating the Application. {ex.msg}\r\n", ex.Message);
            }

        #endregion 

        } catch (Exception ex) {
            Task.Delay(15000).Wait();
            Console.WriteLine(ex.Message);
        }
    }

}
