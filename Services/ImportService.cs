using System.Data;
using Microsoft.Extensions.Logging;
using BundabergSugar.EDIImport.Core;
using BundabergSugar.EDIImport.Models;

namespace BundabergSugar.EDIImport.Services;

public class EdiOperationResult {
    public bool Success { get; set; } = true;
    public string ErrorMessage { get; set; } = string.Empty;
}

public interface IImportService {
    Task ImportAsync();
}


internal class ImportService(IDatabaseService db, IApplicationOptions options, ILogger<ImportService> logger ) : IImportService {
    private static readonly EventId EventId = new(2000, "EDI Import Service");
    private readonly List<FileInfo> Files = [.. Directory.GetFiles(options.InputFileLocation).Select(file => new FileInfo(file))];
    

    /// <summary>
    /// Starts the import process, which involves reading the files from the input file location,
    /// loading the data into the database, and then archiving the files.
    /// </summary>
    /// <remarks>
    /// This will only run if the application is enabled. If the application is not enabled, it will simply return.
    /// Business Rules:
    /// 1. If no files are found in the input file location, it will return without doing anything.
    /// 2. If files are found, it will process each file and import the data into the database.
    /// 3. After processing all files, it will archive the files in the archive table.
    /// 4. The file Header in the first row contain information about the Customer, PO, and PO Version. 
    ///    * The PO Version is checked to see if it's a new order or an amended one. 
    ///    * If the version is not empty AND is NOT 000 it's an amended or updated order These orders are marked as such and not imported. 
    ///    * If the version is empty OR is 000 it's a new order. Check if overlapping and if so mark as error and do no not import. 
    /// 6. When processing details if the productNumber is null or empty, it will try to get the GTIN from the stockCode.
    ///    * If the GTIN is not found, it will mark the record as an error and not import.
    /// 5. Aldi EDI Files have a custom process.
    ///    * Aldi can send EDI Files with or without price, and unitOfMeasure. Also Aldi can send pallet orders.
    ///    * If unitOfMeasure is 200, then the quantity is calculated by multiplying the quantity in the order by the number of 
    //       items in the pallet. The number of items in the pallet is retrieved by queryng the database with company and stock code. 
    //       The unit of measure is then set to Pallet (CT).
    ///    * The price for Aldi items is retrieved by querying the database as well. The re are 2 mathods for retrieving the price.
    ///      If the first fails it will use the second. If both are unable to retrieve the price, then the price is set to 0.
    ///    * Finally the additionalPartNumber is set to the AldiStockCode.  
    /// 
    /// Error Rules:
    ///  1. If any of the files fails to open, it will log the error and continue to the next file.
    ///  2. If any of the files fails to archive, it will log the error and continue to the next file.
    ///  3. If any of the files fails to update the archive table, it will log the error and continue to the next file.
    ///  4. If any of the files fails to delete, it will log the error and continue to the next file.
    ///  5. If it cannot determine the Header of the row it will log the error and continue to the next file.
    ///  6. If it determines that the order is an amended or update order, it will log the error and continue to the next file. 
    ///  7. If ti determines that the order is overlapping (same order for customer) it will log the error and continue to the next file.
    ///  8. If it cannot determine the GTIN for the product number, it will log the error and continue to the next file. 
    ///  9. If it cannot determine the pallet quantity, it will log the error and continue to the next file. 
    /// 10. If it cannot save the detail data to the database, it will log the error and continue to the next file.
    /// 
    /// 
    /// </remarks>
    public async Task ImportAsync() {
        // first check if apoplication is disabled
        if (!IsApplicationEnabled(options.DisabledFileLocation)) return;

        logger.LogInformation(EventId, "Starting import process for DFMID {dfmid} and Company {company}", options.DFMID, options.Company);
        logger.LogInformation(EventId, "Found {n} Files to import.\r\n", Files.Count);
        // get the list of files to import
        if (Files.Count != 0) { // No reason to proceed if no files found
            foreach (var file in Files) {
                try {
                    using var dataFile = new EDIData(file);
                    // Save to archive
                    var result = await UpsertFileToArchiveAsync(dataFile, isUpdate: false);
                    if (result.Success) {
                        // process and import data
                        var import = await LoadEDItoTableAsync(dataFile);
                        if (!import.Success) {
                            logger.LogError(EventId, "Unable to load the order file {file}: {error}", file.FullName, import.ErrorMessage);                    
                        }
                        
                        // update the archive
                        var update = await UpsertFileToArchiveAsync(dataFile, isUpdate: true);  
                        if (import.Success && result.Success) {
                            // remove the physical file to avoid reprocessing from it 
                            dataFile.CloseReader();
                            RemovePhysicalFile(file); 
                        } 
                    }
                } catch (Exception ex) {
                    logger.LogError(EventId, ex, "Unable to load the order file {file}: {error}", file.FullName, ex.Message);            
                }
            }    
        }
        logger.LogInformation(EventId, "Done importing for DFMID {dfmid} and Company {company}\r\n", options.DFMID, options.Company);
    }


    /// <summary>
    /// Determines whether the application is enabled or disabled by checking the disabled status flag OR 
    /// for the existence of a file specified in the DisabledFileLocation parameter.
    /// The boolean flag takes precedence do if it is set to true the application will be disabled.
    /// If it's set to false, then application will check for the existence of the file specified in the DisabledFileLocation parameter.
    /// If found the application will be disabled.
    /// The parameter can either be a fully qualified path e.g. C:/temp/disabled.text or just a filename. In the latter case the application will 
    /// // look for the file in the current directory
    /// </summary>
    /// <param name="disabledFileLocation">The location of the file which disables the application if it exists.</param>
    /// <returns>true if the application is enabled, false otherwise</returns>
    public bool IsApplicationEnabled(string disabledFileLocation) {
        try {
            // Takes precedence
            if (options.Disabled == true) { 
                logger.LogInformation(EventId, "Application has been disabled. Please change the Disabled flag to false to enable this application.");
                return false;
            }

            // no need to check if there is no parameter set   
            if (string.IsNullOrEmpty(disabledFileLocation)) return true;

            // A parameter was found. Check if there is a match    
            var file = Path.IsPathRooted(disabledFileLocation) 
                ? disabledFileLocation 
                : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, disabledFileLocation);

            if (File.Exists(file)) {
                logger.LogInformation(EventId, "Located {file}. Application has been disabled. Remove or rename the file to enable this application.", disabledFileLocation);
                return false;
            }
            return true;
        }
        catch (Exception ex) {
            logger.LogInformation(EventId, ex, "Unable to determine Application status. Disabling file location: {disabledFileLocation}. Error: {error}", disabledFileLocation, ex.Message);        
            return false;
        }
    }




    /// <summary>
    /// Deletes the physical file after it has been processed.
    /// If deletion fails, logs an error
    /// </summary>
    /// <param name="file">The file to delete</param>
    private void RemovePhysicalFile(FileInfo file) {  
        try {
            file.Delete();
        }   catch (Exception ex) {
            logger.LogError(EventId, ex, "Error deleting file {file}: {error}", file.FullName, ex.Message);            
        }
    }


    /// <summary>
    /// Upserts the given EDI file data into the archive table. 
    /// Will update the record if it already exists, otherwise it will insert a new one.
    /// </summary>
    /// <param name="dataFile">The EDI file data to upsert</param>
    /// <returns>An EdiOperationResult indicating success or failure of the operation</returns>
    private async Task<EdiOperationResult> UpsertFileToArchiveAsync(EDIData dataFile, bool isUpdate = false) {
        var result = new EdiOperationResult();
        logger.LogInformation(EventId, "{action} file in archive table {file}{rn}", 
            isUpdate ? "Updating" : "Inserting", dataFile.EdiFile.FullName, (isUpdate ? ".\r\n" : "."));
        
        db.GetExclusiveLock();
        try { 
            var sql = !isUpdate 
                ? @"INSERT INTO [dbo].[EDIImport_archive] 
                        ([FileName], [FileDate], [CustomerPoNumber], [FileContent], [Status], [ErrorMessage])
                    VALUES 
                        (@filename, @filedate, @po, @content, @status, @message)
                   "
                : @"UPDATE [dbo].[EDIImport_archive] 
                    SET [Status] = @status, [ErrorMessage] = @message
                    WHERE [FileName] = @filename AND 
                          [FileDate] = @filedate AND 
                          [CustomerPoNumber] = @po
                    ";
        
            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@filename", dataFile.EdiFile.Name);
            cmd.Parameters.AddWithValue("@filedate", dataFile.EdiFile.CreationTimeUtc);
            cmd.Parameters.AddWithValue("@status", dataFile.Status.ToString());
            cmd.Parameters.AddWithValue("@message", dataFile.ErrorMessage);
            cmd.Parameters.AddWithValue("@po", dataFile.EdiPurchaseOrder);
            if (!isUpdate){
                cmd.Parameters.AddWithValue("@content", dataFile.Content);
            }

            await cmd.ExecuteScalarAsync();
            
        } catch (Exception e) {
            logger.LogError(EventId, "Error {action} file {file}: {error} \r\n", 
                isUpdate ? "updating" : "inserting", dataFile.EdiFile.FullName, e.Message)
            ;
            result.Success = false;
            result.ErrorMessage = e.Message;

        } finally {
            db.ReleaseExclusiveLock();
        };
        return result;
    }


    /// <summary>
    /// Loads the EDI file specified by <paramref name="file"/> into the SQL table.
    /// </summary>
    /// <param name="file">The EDI file to load.</param>
    /// <returns>True if the file was loaded successfully, false otherwise.</returns>
    private async Task<EdiOperationResult> LoadEDItoTableAsync(EDIData dataFile) {
        logger.LogInformation(EventId, "Opening the EDI file {file}", dataFile.EdiFile.FullName);
        
        var result = new EdiOperationResult();
        db.GetExclusiveLock();
        try {
            logger.LogInformation(EventId, "Inserting EDI data to SQL Table...");
            while (await dataFile.Reader.ReadAsync()) { 
                dataFile.EdiRecordCount++;

                switch (dataFile.GetRowType()) {
                    case EdiRowType.Header:
                        await ProcessEDIHeaderAsync(dataFile);
                        break;

                    case EdiRowType.Detail:
                        await ProcessEDIDetailsAsync(dataFile);
                        break;

                    case EdiRowType.Summary:        
                        await ProcessEDISummaryAsync(dataFile);
                        break;

                    case EdiRowType.Other:
                    default:
                        dataFile.ErrorMessage = $"Unexpected row indicator [{dataFile.GetValue(EdiHeader.RowHeader)}] at line [{dataFile.EdiRecordCount}] in file {dataFile.EdiFile.FullName}";
                        dataFile.Status = EdiFileStatus.Error;    
                        throw new InvalidDataException(dataFile.ErrorMessage);
                }
            }
            dataFile.ErrorMessage = string.Empty;
            dataFile.Status = EdiFileStatus.Processed;    

            logger.LogInformation(EventId, "{rows} rows inserted for filename {file}", dataFile.EdiRecordCount, dataFile.EdiFile.FullName);
            logger.LogInformation(EventId, "File {file} has been imported successfully", dataFile.EdiFile.FullName);

        } catch (InvalidDataException e) {
            // we trap this exception to preserve the error messages fromm exceptions in the inner methods
            logger.LogError(EventId, e, "{error}", e.Message);
            result.ErrorMessage = e.Message;
            result.Success = false;

        } catch (Exception e) {
            logger.LogError(EventId, e, "Error importing file {file}: {error}", dataFile.EdiFile.FullName, e.Message);
            result.ErrorMessage = e.Message;
            result.Success = false;

        } finally {
            db.ReleaseExclusiveLock();
        }

        return result;
    }



    #region Header Processing
    /// <summary>
    /// Processes a single EDI Header record from a CSV file. The record is assumed to be a valid header record.
    /// </summary>
    /// <param name="ediFile">The EDI file containing the header record being processed.</param>
    /// <returns>The ID of the inserted header.</returns>
    /// <exception cref="ArgumentException">Thrown if the header record is an overlapping message.</exception>
    /// <exception cref="Exception">Thrown if there is an error importing or parsing the header record.</exception>
    private async Task ProcessEDIHeaderAsync(EDIData dataFile) {
        try { 
            // Get the database and IsAldi properties once rather than repeatedly
            dataFile.EdiCompanyDb = await GetDatabaseNameAsync(options.Company);
            dataFile.EdiIsAldi = await CheckIsAldiAsync(dataFile);    
            if (await CheckEdiVersionAsync(dataFile)) {
                // Open a new Batch
                dataFile.EdiBatchId = await CreateEDIBatchAsync();
                // Insert header details
                dataFile.EdiHeaderId = await InsertEDIHeaderAsync(dataFile);
            }
        } catch (InvalidDataException) {
            // The error concerns data. just bubble the error up
            throw;

        } catch (Exception e) {
            dataFile.Status = EdiFileStatus.Error;
            dataFile.ErrorMessage = e.Message;
            logger.LogError(EventId, e, "Error importing or parsing EDI Header: {error}", e.Message);
            throw;
        }
    }


    /// <summary>
    /// Retrieves the name of the database associated with the given company ID.
    /// </summary>
    /// <param name="company">The ID of the company.</param>
    /// <returns>The name of the associated database.</returns>
    /// <exception cref="Exception">Thrown if the database name cannot be found.</exception>
    private async Task<string> GetDatabaseNameAsync(string company) {
        try {
            var sql = @"SELECT DatabaseName FROM [dbo].[CompanyDetails] WHERE CompanyID = @companyID";
        
            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@companyID", int.Parse(company));
            string value = await cmd.ExecuteScalarAsync() as string ?? string.Empty;
            if (string.IsNullOrEmpty(value)) {
                throw new Exception($"Could not find database name for company {company}");
            }
            return value;
        } catch (Exception e) {
            logger.LogError(EventId, e, "Error importing or parsing EDI Details: {error}", e.Message);
            throw;
        }
    }


    /// <summary>
    /// Checks if the customer is an ALDI customer.
    /// </summary>
    /// <param name="dataFile">The EDI file data containing the customer information.</param>
    /// <returns>True if the customer is an ALDI customer, false otherwise.</returns>
    /// <exception cref="Exception">Thrown if there is an error checking if the customer is an ALDI customer.</exception>
    private async Task<bool>CheckIsAldiAsync(EDIData dataFile) {
        try {
            string sql = $@"SELECT Customer 
                            FROM [{dataFile.EdiCompanyDb}].[dbo].[ArMultAddress+] 
                            WHERE EdiSenderCode = @ediCustomer";

            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@ediCustomer", dataFile.EdiCustNumber);
            string customerCode = (await cmd.ExecuteScalarAsync() as string) ?? string.Empty;
            return !string.IsNullOrEmpty(customerCode) && customerCode[..4] == "ALDI";
            
        } catch (Exception e) {
            logger.LogError(EventId, e, "Error Checking if the customer is ALDI: {error}", e.Message);
            throw;
        }
    }


    /// <summary>
    /// Checks the EDI version for the given <paramref name="dataFile"/>.
    /// If the version is not null or not 000 then is an update to an existing Purchase Order.
    /// Update the archive table and stop processing.
    /// If the version is an overlapping message, throw an exception.
    /// </summary>
    /// <param name="dataFile">The EDI file data to check.</param>
    /// <returns>True if the version is valid, false otherwise.</returns>
    /// <exception cref="InvalidDataException">Thrown if the version is an update or overlapping message.</exception>
    private async Task<bool> CheckEdiVersionAsync(EDIData dataFile) {
        if (!string.IsNullOrEmpty(dataFile.EdiVersion) && dataFile.EdiVersion != "000") {
            dataFile.Status = EdiFileStatus.AmendedPO;
            dataFile.ErrorMessage = $"This is an update to an existing Purchase Order : {dataFile.EdiPurchaseOrder} for Customer : {dataFile.EdiCustNumber}";
            throw new InvalidDataException(dataFile.ErrorMessage);
        }

        if (string.IsNullOrEmpty(dataFile.EdiVersion) || dataFile.EdiVersion == "000") {
            var isOverlapping = await IsOverlappingPoAsync(dataFile.EdiCustNumber, dataFile.EdiPurchaseOrder);
            if (isOverlapping) {
                dataFile.Status = EdiFileStatus.Error;
                dataFile.ErrorMessage = $"Overlapping EDI Customer: {dataFile.EdiCustNumber} PO: {dataFile.EdiPurchaseOrder}";
                throw new InvalidDataException(dataFile.ErrorMessage);
            }
        }
        return true;
    }


    /// <summary>
    /// Creates a new batch in the database and returns the Batch ID
    /// </summary>
    /// <returns>The ID of the newly created batch</returns>
    /// <exception cref="Exception">If there is an error creating a new batch</exception>
    private async Task<int>  CreateEDIBatchAsync() {
        try {
            using var cmd = db.GetSqlCommand("[if].[BatchInsert]");
            cmd.Parameters.AddWithValue("@DFMID", options.DFMID);
            cmd.Parameters.AddWithValue("@company", options.Company);
            cmd.Parameters.AddWithValue("@requestor", Utils.GetUserName());
            cmd.Parameters.AddWithValue("@source", "EDIImport");
            cmd.Parameters.Add("@batchID", SqlDbType.Int);
            cmd.Parameters["@batchID"].Direction = ParameterDirection.Output;

            await cmd.ExecuteNonQueryAsync();

            //TODO Add better error handling here
            int batchId = (int)cmd.Parameters["@batchID"].Value;

            logger.LogInformation(EventId, "Added new batch {batch}", batchId.ToString());

            return batchId;

        } catch(Exception ex) {
            logger.LogError(EventId, ex,  "Error creating batch: {error}", ex.Message);
            throw;
        }
    }


    /// <summary>
    /// Checks the SORTOI_EDIHeader table to see if an overlapping purchase order exists
    /// </summary>
    /// <param name="customer">The customer to check for</param>
    /// <param name="po">The Purchase Order to check for</param>
    /// <returns>true if the PO exists already, false otherwise</returns>
    private async Task<bool> IsOverlappingPoAsync(string customer, string po) {
        try {
            var sql = @"SELECT COUNT(1)
                        FROM [Custom].[if].[SORTOI_EDIHeader]
                        WHERE [Customer] = @customer AND 
                                [CustomerPoNumber] = @po";

            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@customer", customer);
            cmd.Parameters.AddWithValue("@po", po);

            return Convert.ToInt32(await cmd.ExecuteScalarAsync()) != 0;
            
        } catch  (Exception ex) {
            logger.LogError(EventId, ex, "Error checking for overlapping PO: {po} form customer: {customer}", po, customer);
            throw;
        }
    }


    /// <summary>
    /// Inserts the EDI header information into the database.
    /// </summary>
    /// <param name="ediFile">The EDI file to be inserted.</param>
    /// <param name="batchID">The batch ID to associate with the EDI file.</param>
    /// <returns>The ID of the inserted header.</returns>
    private async Task<int> InsertEDIHeaderAsync(EDIData dataFile) {
        try {
            using var cmd = db.GetSqlCommand("[if].[EDIHeaderInsert]");
            cmd.Parameters.AddWithValue("@BatchID", dataFile.EdiBatchId);
            cmd.Parameters.AddWithValue("@PoNumber", dataFile.EdiPurchaseOrder);
            cmd.Parameters.AddWithValue("@Customer", dataFile.EdiCustNumber);
            cmd.Parameters.AddWithValue("@RequestedDeliveryDate", dataFile.GetValue(EdiHeader.DeliveryDate));
            cmd.Parameters.AddWithValue("@DocNumber", dataFile.GetValue(EdiHeader.DocumentNumber));
            cmd.Parameters.AddWithValue("@DeliveryTime", dataFile.GetValue(EdiHeader.DeliveryTime));
            cmd.Parameters.AddWithValue("@VendorNumber", dataFile.GetValue(EdiHeader.VendorNumber));
            
            cmd.Parameters.Add("@headerId", SqlDbType.Int);
            cmd.Parameters["@headerId"].Direction = ParameterDirection.Output;
    
            var result = await cmd.ExecuteNonQueryAsync();

            //TODO Add better error handling here
            int headerId = (int)cmd.Parameters["@headerId"].Value;

            logger.LogInformation(EventId, "Inserted header {h} for Customer: {customer}, PO: {po}, delivery date: {deliverydate}",  
                headerId.ToString(), dataFile.EdiCustNumber,  
                dataFile.EdiPurchaseOrder, dataFile.GetValue(EdiHeader.DeliveryDate)
            );
            return headerId;

        } catch (Exception ex) {     
            logger.LogError(EventId, "Error inserting header: {error}", ex.Message);
            throw;
        }
    }

    #endregion



    #region Detail Processing

    /// <summary>
    /// Processes the EDI details, inserting the information into the database and updating the batch record
    /// </summary>
    /// <param name="dataFile">The EDI file to process</param>
    /// <param name="batchID">The ID of the batch to update</param>
    /// <exception cref="Exception">If an error occurs while processing the EDI details</exception>
    private async Task ProcessEDIDetailsAsync(EDIData dataFile) {
        // Skip lines in the EDI which have zero quantity - found during Coles Scenario #3
        if (dataFile.GetRowQuantity() == 0) return;

        await InsertEDIDetailsAsync(dataFile);
    }

  
    /// <summary>
    /// Inserts a detail record into the database.
    /// </summary>
    /// <param name="dataFile">The EDI file containing the detail record to insert.</param>
    /// <exception cref="Exception">If an error occurs while inserting the detail record.</exception>
    private async Task InsertEDIDetailsAsync(EDIData dataFile) {
        try {
            var custPoLineNo = dataFile.GetValue(EdiDetail.PurchaseOrderLine); // csv.GetField<string>(4);     // As a string to preserve leading zeros
            var stockCode = dataFile.GetValue(EdiDetail.StockCode); //csv.GetField<string>(8); // was additionalPartNumber

            var productNumber = dataFile.GetValue(EdiDetail.ProductNumber).Trim() ; //csv.GetField<string>(6);
            productNumber ??= await GetGTINFromStockCodeAsync(dataFile);

            int quantity = dataFile.GetRowQuantity();
         
            string unitOfMeasure = dataFile.GetValue(EdiDetail.OrderUnit);     //var uom = csv.GetField<string>(14);
            double price = double.Parse(dataFile.GetValue(EdiDetail.Price));
            
            // If Aldi Gets special values
            if (dataFile.EdiIsAldi == true) {
                // Get Unit Price for ALDI (NOT USED SINCE 05/08/2015) 
                // Readded 04/07/2018 to stop warning message in syspro, regarding zero dollar value
                price = await GetAldiUnitPriceAsync(dataFile);
                // Get ALDI Product Code
                stockCode = dataFile.GetValue(EdiDetail.AldiStockCode);    

                // Get Pallet Capacity if unit of measure is Pallet (200}
                if (unitOfMeasure == "200") { 
                    int palletCapacity = await GetPalletCapacityAsync(dataFile);
                    quantity *= palletCapacity;
                    unitOfMeasure = "CT";                    
                }
            }

            // Add to running quantity
            dataFile.EdiRunningQty += quantity;

            using var cmd = db.GetSqlCommand("[if].[EDIDetailInsert]", CommandType.StoredProcedure);
            cmd.Parameters.AddWithValue("@BatchID", dataFile.EdiBatchId);
            cmd.Parameters.AddWithValue("@HeaderID", dataFile.EdiHeaderId);
            cmd.Parameters.AddWithValue("@PartNumber", productNumber);
            cmd.Parameters.AddWithValue("@Qty", quantity);
            cmd.Parameters.AddWithValue("@UOM", unitOfMeasure);
            cmd.Parameters.AddWithValue("@Price", price);
            cmd.Parameters.AddWithValue("@purchaseOrderLine", int.Parse(custPoLineNo));
            cmd.Parameters.AddWithValue("@additionalPartNumber", stockCode);
            cmd.Parameters.AddWithValue("@CustPOLineNo", custPoLineNo);

            var result = await cmd.ExecuteNonQueryAsync();
            
            logger.LogInformation(EventId,"Inserted Detail. PartNumber: {partNo} Qty: {qty} UOM: {unit} Price: {price}  POLineNo: {poline}", 
                productNumber, quantity, unitOfMeasure, price, custPoLineNo);
        
        } catch (InvalidDataException) {
            // Invalid data exception should come from the inner methods and shiould just be trickled up
            // No reason to log it again
            throw;
        } catch (Exception e) {
            logger.LogError(EventId, e, "Error Parsing EDI details at line {line}, {error}", dataFile.GetValue(EdiDetail.PurchaseOrderLine), e.Message);
            dataFile.ErrorMessage = e.Message;
            dataFile.Status = EdiFileStatus.Error;
            throw;
        }
    }

  
    private async Task<int> GetPalletCapacityAsync(EDIData dataFile) {
        try {
            var stockCode  = dataFile.GetValue(EdiDetail.StockCode);
            
            string sql = $@"SELECT CONVERT(decimal(8, 0), ROUND(ConvFactAltUom, 0))
                            FROM [{dataFile.EdiCompanyDb}].[dbo].[InvMaster] 
                            WHERE StockCode = @stockCode";
            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@stockCode", stockCode);

            decimal? result = (await cmd.ExecuteScalarAsync()) as decimal?
                ?? throw new InvalidDataException($"Could not find stock code: {stockCode} while checking the pallet capacity");

            return (int)result;
        } catch (InvalidDataException e) {
            logger.LogError(EventId, e, "Error Checking the pallet capacity: {error}", e.Message);
            dataFile.ErrorMessage = e.Message;
            dataFile.Status = EdiFileStatus.Error;  
            throw;
        } catch (Exception e) {
            logger.LogError(EventId, e, "Error Checking the pallet capacity: {error}", e.Message);
            throw;
        }
    }


    /// <summary>
    /// Retrieves the GTIN for the given stock code.
    /// </summary>
    /// <param name="dataFile">The EDI data containing the stock code.</param>
    /// <returns>The GTIN associated with the stock code.</returns>
    /// <exception cref="InvalidDataException">Thrown if the GTIN is not found for the given stock code.</exception>
    /// <exception cref="Exception">Thrown if an error occurs while retrieving the GTIN.</exception>
    private async Task<string> GetGTINFromStockCodeAsync(EDIData dataFile) {
        try  {
            var stockCode = dataFile.GetValue(EdiDetail.StockCode);
            var poLine  = dataFile.GetValue(EdiDetail.StockCode);

            var sql = $@"SELECT AlternateKey1 
                        FROM [{dataFile.EdiCompanyDb}].[dbo].[InvMaster] 
                        WHERE StockCode = @stockCode";
            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@stockCode", stockCode);
            string gtin = await cmd.ExecuteScalarAsync() as string ?? string.Empty;
            if (string.IsNullOrEmpty(gtin)) {
                throw new InvalidDataException($"No valid GTIN/StockCode is listed in PO line: {poLine}");
            }
            logger.LogInformation(EventId, "Transferred StockCode {stockCode} to GTIN: {gtin}", stockCode, gtin);
            return gtin;

        } catch (InvalidDataException e) {
            logger.LogError(EventId, e, "Error retrieving GTIN: {error}", e.Message);
            dataFile.ErrorMessage = e.Message;
            dataFile.Status = EdiFileStatus.Error;
            throw;
        } catch (Exception e) {
            logger.LogError(EventId, e, "Error retrieving GTIN: {error}", e.Message);
            throw;
        }
    }
        


    /// <summary>
    /// Gets the unit price for the given <paramref name="dataFile"/>.
    /// This method is only used for ALDI customers.
    /// </summary>
    /// <param name="dataFile">The EDI data containing the customer information.</param>
    /// <returns type="Task<double>" >The unit price associated with the customer and stock code.</returns>
    /// <exception cref="Exception">Thrown if an error occurs while retrieving the unit price.</exception>
    /// <remarks>
    ///  The original code performs a toString() operation on the ExecuteScalar result, which throws a NullReferenceException if the result is null.
    ///  We look at the returned value and if it is null, we raise an exception and proceed to the alternative method.
    ///  This will trap any exceptions as well as the conversion error.
    ///  If the alternative method fails it returns 0
    /// </remarks>        
    private async Task<double> GetAldiUnitPriceAsync(EDIData dataFile) {
        try {
            string sql = $@"SELECT CONVERT(decimal(8, 2), ROUND(cp.FixedPrice, 2)) AS Price 
                            FROM [{dataFile.EdiCompanyDb}].[dbo].[BSL_ContractPrice] cp
                            INNER JOIN [{dataFile.EdiCompanyDb}].[dbo].[BSL_ContractMaster] cm
                                ON cm.ContractId = cp.ContractId
                            INNER JOIN [{dataFile.EdiCompanyDb}].[dbo].[ArMultAddress+] adr
                                ON adr.PricingCode = cp.PricingCode
                            INNER JOIN [{dataFile.EdiCompanyDb}].[dbo].[ArCustomer] c
                                ON c.Customer = adr.Customer AND
                                c.BuyingGroup1 = cm.BuyingGroup
                            WHERE CONVERT(date, GETDATE()) >= cm.ContractStartDate AND 
                                    CONVERT(date, GETDATE()) <= cm.ContractEndDate AND
                                    adr.EdiSenderCode = @ediCustomer AND
                                    cp.StockCode = @stockCode";

            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@ediCustomer", dataFile.EdiCustNumber);
            cmd.Parameters.AddWithValue("@stockCode", dataFile.GetValue(EdiDetail.StockCode));

            decimal? result = await cmd.ExecuteScalarAsync() as decimal?  
                ?? throw new NullReferenceException($"Could not find unit price for customer: {dataFile.EdiCustNumber} and stock code: {dataFile.GetValue(EdiDetail.StockCode)}");

            return (double)result;
            
       } catch {
            return await GetAlternativeAldiUnitPrice(dataFile);
       }
   }


    /// <summary>
    /// Use a less stringent method to get the unit price.  
    /// This method is used if the normal method fails to find a unit price.
    /// </summary>
    /// <param name="dataFile">The edi file data to get the unit price for.</param>
    /// <returns>The unit price as a double.</returns>
    /// <exception cref="InvalidDataException">If no unit price can be found.</exception>
    /// <remarks>
    ///  The original code performs a toString() operation on the ExecuteScalar result, which throws a NullReferenceException if the result is null.
    ///  We look at the returned value and if it is null, we raise an exception, Log thye issue and return 0
    ///  This will trap any exceptions as well as the conversion error.
    /// </remarks>        /// 
    private async Task<double> GetAlternativeAldiUnitPrice(EDIData dataFile) {
        try {
            string sql = $@"SELECT top 1 CONVERT(decimal(8, 2), ROUND(cp.FixedPrice, 2)) AS Price
                            FROM [{dataFile.EdiCompanyDb}].[dbo].BSL_ContractPrice cp 
                            INNER JOIN [{dataFile.EdiCompanyDb}].[dbo].[InvMaster] im ON im.StockCode = cp.StockCode 
                            WHERE ( im.StockCode = @stockCode or im.AlternateKey1 = @stockCode )
                            ORDER BY ContractPriceId DESC";

            using var cmd = db.GetSqlCommand(sql, CommandType.Text);
            cmd.Parameters.AddWithValue("@stockCode", dataFile.GetValue(EdiDetail.StockCode));

            decimal? result = await cmd.ExecuteScalarAsync() as decimal?
                ?? throw new NullReferenceException($"Could not find Unit Price for Customer: {dataFile.EdiCustNumber}, Stock Code: {dataFile.GetValue(EdiDetail.StockCode)}");
            
            return (double)result;    

        } catch (NullReferenceException e) {
            logger.LogInformation(EventId, e, "{msg}", e.Message);
           
        } catch (Exception e) {
            logger.LogInformation(EventId, e, "Unable to get Unit Price for Customer: {cust}, Stock Code: {code}, error: {err}", 
                dataFile.EdiCustNumber, dataFile.GetValue(EdiDetail.StockCode), e.Message
            );
        }
        return 0;
    }

    #endregion



    #region Summary Processing
    /// <summary>
    /// Processes the EDI Summary record, inserting the summary information into the header
    /// and marking the batch as complete.
    /// </summary>
    /// <param name="dataFile">The EDI file to process</param>
    /// <param name="runningQty">The running quantity of the items in the batch</param>
    private async Task ProcessEDISummaryAsync(EDIData dataFile) {
        await InsertEDIHeaderSummaryAsync(dataFile);
        await CompleteEDIBatchAsync(dataFile.EdiBatchId);   
    }


    /// <summary>
    /// Updates the provided header with summary information including the number or rows in the record, total quantity and total value based on the summary detail provided in the csv file
    /// </summary>
    private async Task InsertEDIHeaderSummaryAsync(EDIData dataFile) {
        try {
            // Insert summary information into respective header.
            //var totalLines = dataFile.GetValue(EdiSummary.TotalLines);
            // var totalQty = csv.GetField<string>(5);  // Can't rely on this field since Coles Scenario #3 - change to sum the individual qty
            //var totalVal = dataFile.GetValue(EdiSummary.TotalValue);
            
            // Calculate Total Value from lines for ALDI (NOT USED SINCE 05/08/2015)
            //if(IsAldi(dbc, dbt, ediCustomerCode, company)) {
            //    totalVal = totalValue.ToString();
            //}

            using var cmd = db.GetSqlCommand("[if].[EDIHeaderUpdateTotals]");
            cmd.Parameters.AddWithValue("@batchID", dataFile.EdiBatchId);
            cmd.Parameters.AddWithValue("@headerID", dataFile.EdiHeaderId);
            cmd.Parameters.AddWithValue("@totalLines", dataFile.GetValue(EdiSummary.TotalLines));
            cmd.Parameters.AddWithValue("@totalQty", dataFile.EdiRunningQty);
            cmd.Parameters.AddWithValue("@totalVal", dataFile.GetValue(EdiSummary.TotalValue));

            var result = await cmd.ExecuteNonQueryAsync();

            logger.LogInformation(EventId, "Updated header {id} Total Lines: {lines}, Total Qty: {qty}, Total Val: {val}", 
                dataFile.EdiHeaderId, dataFile.GetValue(EdiSummary.TotalLines), dataFile.EdiRunningQty.ToString(), dataFile.GetValue(EdiSummary.TotalValue)
            );

        } catch (Exception ex) {
            logger.LogError(EventId, ex,  "Error updating header {h}: {m}", dataFile.EdiHeaderId, ex.Message);
            throw;
        }

    }


  
    /// <summary>
    /// Updates the batch in [if].[DFM_BatchHeader] and marks the batch as complete
    /// </summary>
    private async Task<bool> CompleteEDIBatchAsync(int BatchId) {
        try {
            using var cmd = db.GetSqlCommand("[if].[BatchInsertComplete]");
            cmd.Parameters.AddWithValue("@batchID", BatchId);
            var result = await cmd.ExecuteNonQueryAsync();
            logger.LogInformation(EventId, "BatchID: {id} completed successfuly.", BatchId);
            return true;
        } catch (Exception ex) {
            logger.LogError(EventId, ex, "Error completing batch: {error}", ex.Message);
            throw;    
        }
    }


    #endregion
}



