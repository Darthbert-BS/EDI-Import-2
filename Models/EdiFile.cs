using System.Globalization;
using BundabergSugar.EDIImport.Core;
using CsvHelper;
using CsvHelper.Configuration;

namespace BundabergSugar.EDIImport.Models;

internal class EDIData: IDisposable {
    private readonly FileInfo _fileInformation;
    private readonly StreamReader _streamReader;
    private CsvReader _reader;
    private bool _disposed = false;
    private readonly CsvConfiguration _csvConfig = new(CultureInfo.InvariantCulture) {
        Delimiter = "|",
        HasHeaderRecord = false,
        DetectColumnCountChanges = false
    };
    private readonly string _ediPO = string.Empty;
    private readonly string _ediCustNo = string.Empty;
    private readonly string _ediVersion = string.Empty;
    private readonly string _ediFileContent = string.Empty;
    

    public FileInfo EdiFile { get => _fileInformation ; }
    public CsvReader Reader { get => _reader;}
    public string Content { get => _ediFileContent;}
    public EdiFileStatus Status { get; set; } = EdiFileStatus.New;
    public string ErrorMessage { get; set; } = string.Empty;

    public string EdiCompanyDb {get; set; } = string.Empty;
    public string EdiPurchaseOrder {get => _ediPO;}
    public string EdiCustNumber {get => _ediCustNo;}
    public string EdiVersion {get => _ediVersion;}
    

    public int EdiBatchId { get; set; } = 0;
    public int EdiHeaderId { get; set; } = 0;
    public int EdiRunningQty { get; set; } = 0;
    public int EdiRecordCount { get; set; } = 0;
    
    public bool EdiIsAldi { get; set; } = false;


    /// <summary>
        /// Constructor for EDIData.
        /// <para>
        /// This object is used to read and parse EDI files. It opens the file, reads the header record
        /// and stores the Purchase Order, Customer Number and Version for later use.
        /// </para>
        /// <para>
        /// The file is left open for the life of the object so that it can be read again later.
        /// </para>
        /// </summary>
        /// <param name="file">The file to open and read.</param>
    public EDIData (FileInfo file) {
        try {
            _fileInformation = file;
            _ediFileContent = File.ReadAllText(_fileInformation.FullName);

            // opens the reader and gets the first row data for the common variables
            _streamReader = File.OpenText(_fileInformation.FullName);
            _reader = new CsvReader(_streamReader, _csvConfig, leaveOpen: true);
            _reader.Read();    
            if (GetRowType() != EdiRowType.Header) {
                throw new InvalidDataException($"File {file.Name} does not contain a header record.");
            }
            _ediPO = _reader.GetField<string>((int)EdiHeader.PO) ?? "Unknown";
            _ediCustNo = _reader.GetField<string>((int)EdiHeader.EdiCustomerCode) ?? "Unknown";
            _ediVersion = _reader.GetField<string>((int)EdiHeader.Version) ?? string.Empty;

            // reset the reader for readyiong for processing it
            ResetReader();
        } catch (Exception e) {
            Console.WriteLine($"Error opening file: {file} {e.Message}");
            throw;
        }
    }


    // TODO: Error handler
    public string GetValue<TEnum>(TEnum attribute) where TEnum: Enum { 
        var value = Convert.ToInt32(attribute);
        return _reader.GetField<string>(value) ?? string.Empty; 
    }


    public EdiRowType GetRowType()  {
        char indicator = _reader.GetField<char>((int)EdiHeader.RowHeader); 
        return EnumExtensions.FromCharValue(indicator);
    }


    public int GetRowQuantity() {
        // Use sytring to get the value because it would crash for empty fields
        var value = _reader.GetField<string>((int)EdiDetail.RowQuantity) ?? "0"; 
        if (string.IsNullOrEmpty(value)) { return 0; }

        // PFD send quantities as 10.00 decimal
        // Coles send quantities as 10 integer
        // So parse as decimal always and convert to integer at the end
        var result = decimal.TryParse(value, out decimal qty);

        // If the parsing failed return 0
        return result ? (int)qty : 0;
    } 


    /// <summary>
    /// Resets the reader to the beginning of the file, discarding any 
    ///  buffered data. This is useful when you need to read the file 
    ///  again from the beginning, such as when re-processing a file.
    /// </summary>
    public void ResetReader() {
        _reader.Dispose();
        _streamReader.BaseStream.Seek(0, SeekOrigin.Begin);
        _streamReader.DiscardBufferedData();
        _reader = new CsvReader(_streamReader, _csvConfig, leaveOpen: false); 
    }

    public void CloseReader() {
        _reader.Dispose();
        _streamReader.Close();
        _streamReader.Dispose();
    }




    protected virtual void Dispose(bool disposing) {
        if (!_disposed) {
            if (disposing) {
                // Dispose managed resources
                _reader.Dispose();
                _streamReader.Dispose();
                // Deletes the file after it has been processed
                //_fileInformation.Delete();
            }
            // Dispose unmanaged resources if any
            _disposed = true;
        }
    }


    public void Dispose() {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~EDIData() {
        Dispose(false);
    }
}

