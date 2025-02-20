namespace BundabergSugar.EDIImport.Services.Import.Enums;

public enum EdiFileStatus { 
    New,        // the file has been uploaded to the archive table successfully
    Processed,  // the file has been processed successfully
    AmendedPO,  // the file didn't need to be processed and a message has been logged
    Error,      // the file import did not complete successfully
}

public enum EdiHeader { 
    RowHeader = 0,
    Version = 39,
    PO = 20,
    EdiCustomerCode = 29,
    DeliveryDate = 35,
    DeliveryTime = 36,
    DocumentNumber = 31,
    VendorNumber = 100
};


public enum EdiDetail { 
    RowHeader = 0,
    PurchaseOrderLine = 4,
    ProductNumber = 6,
    StockCode = 8,
    AldiStockCode = 12,
    RowQuantity = 13,
    OrderUnit = 14,
    Price = 15,
};


public enum EdiSummary { 
    RowHeader = 0,
    TotalLines = 4,
    TotalValue = 6,
};



public enum EdiRowType {
    Header,
    Detail,
    Summary,
    Other
};



public static class EnumExtensions {
    
    private static readonly Dictionary<EdiFileStatus, string> Strings = new() {
        { EdiFileStatus.New, "NEW" },
        { EdiFileStatus.Processed, "PROCESSED" },
        { EdiFileStatus.AmendedPO, "AMENDEDPO" },
        { EdiFileStatus.Error, "ERROR" }
    };
    
    public static String ToStringValue(this EdiFileStatus status) {
        return Strings[status];
    }


    private static readonly Dictionary<EdiRowType, char> Chars = new() {
        { EdiRowType.Header, 'H' },
        { EdiRowType.Detail, 'D' },
        { EdiRowType.Summary, 'S' },
        { EdiRowType.Other, '\0' }
    };

    public static char ToCharValue(this EdiRowType type) {
        return Chars[type];
    }

    public static EdiRowType FromCharValue(char value) {
        return Chars.FirstOrDefault(x => x.Value == value, Chars.Last()).Key;
    }

}