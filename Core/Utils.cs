
using System.Reflection;
using System.Security.Principal;

namespace BundabergSugar.EDIImport.Core;
 
 public static class Utils {
    
    public static bool CanWriteToDirectory(string directoryPath) {
        try {
            string testFilePath = Path.Combine(directoryPath, Path.GetRandomFileName());
            using FileStream fs = File.Create(testFilePath, 1, FileOptions.DeleteOnClose);
            return true;
        } catch {
            return false;
        }
    }

    public static bool CanWriteToFile(string filePath) {
        try {
            // Try opening the file with write access
            using FileStream fs = new(filePath, FileMode.OpenOrCreate, FileAccess.Write);
            return true;
        } catch {
            return false;
        }
    }


    public static string GetAppName() => Assembly.GetExecutingAssembly().GetName()?.Name ?? "Unknown"; 

    public static Version GetAppVersion() => Assembly.GetExecutingAssembly().GetName()?.Version ?? new Version(0, 0, 0);   


    public static string GetUserName() => OperatingSystem.IsWindows() 
        ? WindowsIdentity.GetCurrent().Name 
        : Environment.UserName; 
}