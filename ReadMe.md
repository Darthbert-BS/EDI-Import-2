# SYSPRO SALES ORDERS / EDI IMPORT

This is a .Net 8 port of the existing EDIImport console application, with the addition of storing the EDI Files into the database as text for easier retrieval and reprocessing. The original application can be found at this [link].

Malcom Byrne needs an alteration to the EDI Import program to perform table-based archival of all EDI files. This archival will allow re-processing an EDI file using a FOCUS screen that is yet to be built.

Additionally, the application will be ported to .Net 8 to align with the latest LTS runtime.

## General description

The application is a console app that when run manually or by an automated system.

All configuration entries are stored in the appsettings.json file.

The application can be prevented to run by placing a text file named `disabled.txt` in the directory specified by the `DisabledFileLocation` setting.

If the application is not disabled, it looks into the directory specified by the 'InputFileLocation' setting for EDI files to import. The files are stored in csv format.

Each file will go through the following steps:

* The file is loaded in memory. Once loaded the text content is written to the `[dbo].[EDIImport_archive]`  table with the Status set to `New`
* The file is composed of at minimum 3 lines: Header, Details, and Summary
* A transaction is open in the database and the header is processed and stored. If the Header is an update to an existing Purchase Order the archive table is updated with the AMENDEDPO status and the 'This is an update to an existing Purchase Order: PO Number for Customer: CustNumber', and the file is skipped.
* Subsequently, Each Detail line is processed and stored in the database.  
* Lastly the Summary Line is processed and stored.
* Once all the sections are processed successfully the transaction is committed and the system moves to the next file. If there is an error during the processing of any of the lines, the exception information is stored in the text log configured  in the  `FileLogger.FilePath` entry in the `Logging` section of `appsettings.json` AND in the database specified in the `DBLogger.ConnectionString`  entry in the `Logging` section of `appsettings.json`.

### Classes and Dependencies

The application depends on the .Net Core 8 framework. Additionally it depends on the:

* `CsvHelper" Version=33.0.1`,
* `Microsoft.Data.SqlClient Version=6.0.1`,
* `Microsoft.Extensions.DependencyInjection Version=9.0.2` and
* `Microsoft.Extensions.Hosting Version=9.0.2` libraries

The main classes for the application are the:

* `ImportService`,
* `DatabaseService`,
* `EdiData`,
* `ApplicationOptions` and
* `ILogger`.  

#### DatabaseService

The `DatabaseService` implementss the IDatabaseService interface. It is responsible to maintain the connection to the database and manage the transactions. It exposes three public methods:

* `SqlTransaction GetExclusiveLock(string table = "EDIImport_lock")`, responsible to start a transaction and get an exclusive lock on the tables,
* `SqlCommand GetSqlCommand(string commandText, CommandType commandType = CommandType.StoredProcedure)` responsible to get a SqlCommandObject configured with the current transaction
* `void ReleaseExclusiveLock(bool commit = true, bool rollback = true)` responsible for committing or rolling back the current transaction in case of error, and releasing the lock.

#### ApplicationOptions

The `AppplicationOptions` class implements the IApplicatioOptions interface and is rtesponsible to provide configuration data to the services. It depends on the data stored in the `appsettings.json` and `appsettings.<ENVIRONMENT>.json` files.

#### EdiData

The EdiData stores the reader for the file and provides values for processing.

It exposes the following public properties:

* FileInfo EdiFile
* CsvReader Reader
* Content
* EdiFileStatus Status
* string ErrorMessage
* string EdiCompanyDb
* string EdiPurchaseOrder
* string EdiCustNumber
* string EdiVersion
* public int EdiBatchIdpublic
* int EdiHeaderId
* int EdiRunningQty
* int EdiRecordCount
* bool EdiIsAldi

And the following public methods:

* `string GetValue<TEnum>(TEnum attribute) where TEnum: Enum`, this is a generic method that accepts an enum of any type (EdiHeader, EdiDetail, EdiSummary) and returns the associated value.
* `EdiRowType GetRowType()` returns the type of row being read (Header,
    Detail, Summary, or Other).  
* `int GetRowQuantity()` gets an numeric quantity from the file,
* `void ResetReader()` responsible for resetting the reader at the top after a read operation has been performed.
* `void CloseReader()` responsible for closing the reader at the end of the import operation so that the physical file can be moved or deleted.

#### ImportService

The `ImportService` is the class responsible for loading and processing the EDI files.
It depends on `IDatabaseService`, `IApplicationOptions`, and `ILogger` interfaces.

It has one public async method:

* `Task ImportAsync` starts the import process, which involves reading the files from the input file location, loading the data into the database, and then archiving the files. This will only run if the application is enabled. If the application is not enabled, it will simply return.
  
It implements the following Business Rules:

1. If no files are found in the input file location, it will return without doing anything.
2. If files are found, it will process each file and import the data into the database.
3. After processing all files, it will archive the files in the archive table.
4. The file Header in the first row contain information about the Customer, PO, and PO Version.
   * The PO Version is checked to see if it's a new order or an amended one.
   * If the version is not empty AND is NOT 000 it's an amended or updated order These orders are marked as such and not imported.
   * If the version is empty OR is 000 it's a new order. Check if overlapping and if so mark as error and do no not import.
5. When processing details if the productNumber is null or empty, it will try to get the GTIN from the stockCode.
   * If the GTIN is not found, it will mark the record as an error and not import.
6. Aldi EDI Files have a custom process.
   * Aldi can send EDI Files with or without price, and unitOfMeasure, and can send pallet orders.
   * If unitOfMeasure is 200, then the quantity is calculated by multiplying the quantity in the order by the number of items in the pallet. The number of items in the pallet is retrieved by querying the database with company and stock code. The unit of measure is then set to Pallet (CT).
   * The price for Aldi items is retrieved by querying the database as well. There are 2 methods for retrieving the price. If the first fails it will use the second. If both are unable to retrieve the price, then the price is set to 0.
   * Finally, the additionalPartNumber is set to the AldiStockCode.  

Error Rules:

1. If any of the files fails to open, it will log the error and continue to the next file.
2. If any of the files fails to archive, it will log the error and continue to the next file.
3. If any of the files fails to update the archive table, it will log the error and continue to the next file.
4. If any of the files fails to delete, it will log the error and continue to the next file.
5. If it cannot determine the Header of the row it will log the error and continue to the next file.
6. If it determines that the order is an amended or update order, it will log the error and continue to the next file.
7. If it determines that the order is overlapping (same order for customer) it will log the error and continue to the next file.
8. If it cannot determine the GTIN for the product number, it will log the error and continue to the next file.
9. If it cannot determine the pallet quantity, it will log the error and continue to the next file.
10. If it cannot save the detail data to the database, it will log the error and continue to the next file.

#### Logger

The loggers implement the Microsoft ILogger interface. The previous version used Log4Net but, due to some issues with using the database adapter with .Net Core 8, that dependency was scrapped, and a custom File and DB Loggers were implemented.

The `File Logger` logs events to a text file. It can be configures in a similar fashion to Log4Net in the `appsettings.json` with the ability to filter log events, and customize where the log file is kept. It also allows to set maximum size for log files and automatic backups.

The `Database Logger` can be configured similarly and logs to the table and fields specified in the `appsettings.json` and it also has the ability to filter log events.

### New Requirements

### File Re-Execution

The purpose of the archival is to allow for a re-running of an EDI file.
A new record shall be placed in [EDIImport_archive] and the EDI Import program will identify it by the status "NEW". It will process this record as if it was picked up as a file in the file system.
Re-processing an EDI file will eventually be requested in an DotNet app, and the app will copy a "PROCESSED" or "ERROR" record into a NEW record.
When completed, the NEW record will be updated to a status of PROCESSED or ERROR.
The File will be stored as a text field in the EDIImport_archive.

The File will have the following statuses:

1. `NEW`: A file not yet processed.
2. `PROCESSED`: A file that was processed successfully.
3. `AMENDEDPO`: A file that was found to be an amended PO. Information about PO Number and Customer number is stored in the `ErrorMessage` field. Does not get processed.
4. `ERROR`: A file that was not processed successfully because of some kind of error. The specific error message is stored in the `ErrorMessage` field.  

## Tasks List

### Modify existing database schema

The archival table is specified by the following SQL:

```sql
CREATE TABLE [dbo].[EDIImport_archive] (
    [ArchiveId] int IDENTITY(1, 1) NOT NULL,
    [FileName] varchar(100) NOT NULL,
    [FileDate] datetime NOT NULL,
    [Status] varchar(20) NOT NULL,
    [ErrorMessage] varchar(1000) NULL,
    [CustomerPoNumber] varchar(100) NULL,
    [FileContent] varchar(max) NULL
) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY]
```

### Port the existing app to .Net 8

The application will be ported to .Net 8. Refactoring should be done for improved resiliency and performance.

EDI Import should provide error codes and messages in this table as well as the log file.

## Testing

Testing should be done on the server TITAN.

A collection of recent EDI log files are in the `TestData` subdirectory.

Testing the data persistence can be done with these queries:

```sql
    SELECT Top(50) * FROM [Custom].[dbo].[EventLog] Order By EventID DESC;
    SELECT Top(50) * FROM [Custom].[dbo].[EventLog]  WHERE Level = 'ERROR' Order By EventID DESC;
    SELECT Top(50) * FROM [Custom].[dbo].[EDIImport_archive] ORDER BY ArchiveId DESC;

    WITH BatchIDs AS (
        SELECT BatchID 
        FROM [Custom].[if].[DFM_BatchHeader] 
        WHERE BatchRequestor = '<REQUESTOR>'
    )
    SELECT * FROM [Custom].[if].[SORTOI_EDIDetail] 
        WHERE BatchID IN (SELECT BatchID FROM BatchIDs)
        ORDER BY BatchID;
```

After testing, data cleanup can be done by running the following SQL statements and replacing `<REQUESTOR>` with the username of the contest running the app (e.g.: BUNDY\yourusername) :

```sql
    WITH BatchIDs AS (
        SELECT BatchID 
        FROM [Custom].[if].[DFM_BatchHeader] 
        WHERE BatchRequestor = '<REQUESTOR>'
    )
    -- Delete from SORTOI_EDIDetail
    DELETE FROM [Custom].[if].[SORTOI_EDIDetail]
    WHERE BatchID IN (SELECT BatchID FROM BatchIDs);

    -- Delete from SORTOI_EDIHeader
    DELETE FROM [if].[SORTOI_EDIHeader] 
    WHERE BatchID IN (SELECT BatchID FROM BatchIDs);

    -- Delete from DFM_Task
    DELETE FROM [if].[DFM_Task] 
    WHERE BatchID IN (SELECT BatchID FROM BatchIDs);

    -- Delete from DFM_BatchHeader
    DELETE FROM [Custom].[if].[DFM_BatchHeader] 
    WHERE BatchRequestor = '<REQUESTOR>';

    -- Delete from EDIImport_larchive
    DELETE FROM [Custom].[dbo].[EDIImport_archive];

```

## Deployment Instructions

The application is run non a timed task automatically on the testing and production server.
The application has `Development`, `Staging`, and `Production` environment settings.

To publish the app for Staging or Production there is a batch file [here](./publish.bat)

The file accepts two parameters, '-env' and '-dir'. The `-env` parameter can be one of `D or Development`, `S or Staging`, or `P or Production`. the `-dir` parameter accepts any well formatted directory including network paths. If the directory does not exist, it will create it.

To use it open a terminal window, navigate to the root directory of the project, and run the following command:

```sh
./publish.bat -env S -dir "\\Titan\c$\Program Files\BBS\FOCUS"
```

## Running the application

The application can be run either inside a scheduled task or as a standalone console app.

When running the application will load the necessary configuration parameters from the `appsetting.json` file, or from the `appsetting.<ENVIRONMENT>.json` file if an enviromnent was specified.

The environment can be specified by either configuring the server variable `DOTNET_ENVIRONMENT` or by adding an argument `--environment or -e` to the application.

Valid environments are `development`, `staging`, and `production`. If no environment is specified, the application defaults to production.

When specifying an enviroment other than `production` a corresponding `appsetting.<ENVIRONMENT>.json` file has to be present in the app root directory, or the app will default to the values in the `appsetting.json` file.

If no `appsetting.json` file is found the application will terminate.

An `interactive` flag has been added to the argument list to help in troubleshooting issues. When set it will prevent the console from closing until a key is pressed. It can be activated by adding the '--interactive' or `-i` argument to the command line.

A quick help can be displayed by using the `--help`or `-h` argument flag.

The pplication build can be displayed by using the `--version`or `-v` argument flag.

Any argument that is not recognized is simply ignored.

An example of how to run the application in staging is as follows:  

```sh
"EDI Import.exe" -e Staging
"EDI Import.exe" --environment Staging

--interactive mode
"EDI Import.exe" --e Development -i
```

## Additional information

### Staging Values

The staging server is TITAN.

On Titan the application should be placed in the `C:/Program Files/BBS/FOCUS/EDI Import` directory. The server Task is already preconfigured and runs at a 10 minute interval.

Other important values are:

* The `ConnectionString` should be `"server=TITAN;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true;  MultipleActiveResultSets=true` for both the Application and the Database Logger,
* The logging directory should point to `C:/ProgramData/BBS/Focus`.
* The InputFileLocation should be set to: `C:/ProgramData/BBS/Focus/EDIImport/Input`. Syspro PO data lives in `D:/data/edi/in/po`. The system will process the POs and place the files to be processed in the `C:/ProgramData/BBS/Focus/EDIImport/Input` directory.

An example of appsetting.json for staging is as follows:

```json
{
  "ApplicationOptions": {
    "Environment": "Staging",
    "ConnectionString": "server=TITAN;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true; MultipleActiveResultSets=true",
    "Disabled":false,
    "DisabledFileLocation": "C:/Program Files/BBS/FOCUS/EDI Import/disabled.txt",
    "DFMID": "15",
    "Company": "2",
    "InputFileLocation" : "C:/ProgramData/BBS/Focus/EDIImport/Input/"
  },
    
  "Logging": {
    "FileLogger": {
      "FilePath": "C:/ProgramData/BBS/Focus/",
      "FileName": "EdiImport.log",
      "MaxFileSizeMB":"10"
    },

    "DBLogger": {
      "ConnectionString": "server=TITAN;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true; MultipleActiveResultSets=true",
      "TargetName": "[dbo].[EventLog]",
      "TargetType": "Table",
      "Parameters": [
        {
          "FieldName": "Timestamp", 
          "FieldType": "timeStamp"
        },
        {
          "FieldName": "Level", 
          "FieldType": "logLevel"
        },
        {
          "FieldName": "System", 
          "FieldType": "system"
        },
        {
          "FieldName": "SubSystem", 
          "FieldType": "subSystem"
        },
        {
          "FieldName": "Message", 
          "FieldType": "message"
        },
        {
          "FieldName": "Data1", 
          "FieldType": "exception"
        },
        {
          "FieldName": "Data2", 
          "FieldType": "stackTrace"
        }
      ]
    }
  }
}
```

### Production Values

The production server is SIRUS.

On SIRUS the application should be placed in the `C:/Program Files/BBS/FOCUS/EDI Import` directory. The server Task is already preconfigured and runs at a 10 minutes interval.

Other important values are: 

* The `ConnectionString` should be `"server=SIRUS;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true;  MultipleActiveResultSets=true` for both the Application and the Database Logger,
* The logging directory should point to `C:/ProgramData/BBS/Focus`.
* The InputFileLocation should be set to: `C:/ProgramData/BBS/Focus/EDIImport/Input`. Syspro PO data lives in `D:/data/edi/in/po`. The system will process the POs and place the files to be processed in the `C:/ProgramData/BBS/Focus/EDIImport/Input` directory.

An example of appsetting.json for staging is as follows:

```json
{
  "ApplicationOptions": {
    "Environment": "Staging",
    "ConnectionString": "server=SIRUS;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true; MultipleActiveResultSets=true",
    "Disabled":false,
    "DisabledFileLocation": "C:/Program Files/BBS/FOCUS/EDI Import/disabled.txt",
    "DFMID": "15",
    "Company": "2",
    "InputFileLocation" : "C:/ProgramData/BBS/Focus/EDIImport/Input/"
  },
    
  "Logging": {
    "FileLogger": {
      "FilePath": "C:/ProgramData/BBS/Focus/",
      "FileName": "EdiImport.log",
      "MaxFileSizeMB":"10"
    },

    "DBLogger": {
      "ConnectionString": "server=SIRUS;Initial Catalog=Custom; Integrated Security=True; TrustServerCertificate=true; MultipleActiveResultSets=true",
      "TargetName": "[dbo].[EventLog]",
      "TargetType": "Table",
      "Parameters": [
        {
          "FieldName": "Timestamp", 
          "FieldType": "timeStamp"
        },
        {
          "FieldName": "Level", 
          "FieldType": "logLevel"
        },
        {
          "FieldName": "System", 
          "FieldType": "system"
        },
        {
          "FieldName": "SubSystem", 
          "FieldType": "subSystem"
        },
        {
          "FieldName": "Message", 
          "FieldType": "message"
        },
        {
          "FieldName": "Data1", 
          "FieldType": "exception"
        },
        {
          "FieldName": "Data2", 
          "FieldType": "stackTrace"
        }
      ]
    }
  }
}
```
Notes from the previous version can be found [here](./_notes.txt)

[Link]: https://github.com/Bundaberg-Sugar-Ltd/syspro-repos/tree/develop/Applications/EDI%20Import