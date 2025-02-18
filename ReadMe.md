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
* The file is composed of at minimum 3 lines: Header, Detais, and Summary
* A transaction is open in the database and the header is processed and stored. If the Header is an update to an existing Purchase Order the archive table is updated with the AMENDEDPO status and the 'This is an update to an existing Purchase Order : PO Number for Customer : CustNumber', and the file is skipped. 
* Subsequently, Each Detail line is processed and stored in the database.  
* Lastly the Summary Line is processed and stored.
* Once all the sections are processed successfully the transaction is committed and the system moves to the next file. If there is an error during the processing of any of the lines, the exception information is stored in the text log  configured  in the  `FileLogger.FilePath` entry in the `Logging` section of `appsettings.json` AND in the database specified in the `DBLogger.ConnectionString`  entry in the `Logging` section of `appsettings.json`.

## New Requirements

### File Re-Execution

The purpose of the archival is to allow for a re-running of an EDI file.
A new record shall be placed in [EDIImport_archive] and the EDI Import program will identify it by  the status "NEW". It will process this record as if it was picked up as a file in the file system.
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

The application will be ported to .Net 8. Refacttoring should be done for improved resiliency and performance.

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

The file accepts two parameters, '-env' and '-dir'. The `-env` parameter can be one of `D or Development`, `S or Staging`, or `P or Production`. the `-dir` parameter accepts any well formatted directory including network paths. If the directory does not exists, it will create it.

To use it open a terminal window, navigate to the root directory of the project, and run the following command:

```sh
./publish.bat -env S -dir "\\Titan\c$\Program Files\BBS\FOCUS"
```

## Running the application

The application can be run either inside a scheduled task or as a standalone console app.

When running the application will load the necessary configuration parameters from the `appsetting.json` file, or from the `appsetting.<ENVIRONMENT>.json` file if an enviromnent was specified.

The environment can be specified by either configuring the server variable `DOTNET_ENVIRONMENT` or by adding an argument `--environment or -e` to the application.

Valid environments are `development`, `staging`, and `production`. If no environment is specified, the application defaults to production.

When specifying an enviromnet other than `production` a corresponding `appsetting.<ENVIRONMENT>.json` file has top be present in the app root directory, or the app will default to the values in the `appsetting.json` file.

If no `appsetting.json` file is found the application will terminate.

An example of how to run the application in staging is as follows:  
```sh
"EDI Import.exe" -e Staging
or 
"EDI Import.exe" --environment Staging

```


## Additional information

Notes from the previous version can be found [here](./_notes.txt)


[Link]: https://github.com/Bundaberg-Sugar-Ltd/syspro-repos/tree/develop/Applications/EDI%20Import