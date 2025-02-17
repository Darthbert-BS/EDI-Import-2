namespace BundabergSugar.EDIImport.Services;

public interface IApplicationOptions {
    public string DFMID {get; set ;} 
    public string Company {get; set ;} 
    public string DisabledFileLocation { get; set; }
    public string ConnectionString { get; set; }
    public string InputFileLocation { get; set; }
    
}

public sealed class ApplicationOptions: IApplicationOptions {
    public string DFMID {get; set ;} = "15";
    public string Company {get; set ;} = "1";  // Default DFM identifier to use for this application. DFMID's are defined in [Custom].[if].[DFM_Definition]
    public string DisabledFileLocation { get; set; } = string.Empty;
    public string ConnectionString { get; set; } = string.Empty;   
    public string InputFileLocation { get; set; } = string.Empty;   
    
}