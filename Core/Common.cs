using System.Reflection;
using System.Security.Principal;

namespace BundabergSugar.Core;
 
 public static class Common {
    
    /// <summary>
    /// Checks if the current process has write access to the given directory.
    /// </summary>
    /// <param name="directoryPath">The path to the directory to check.</param>
    /// <returns>true if the process has write access, false otherwise.</returns>
    
    
    public static bool CanWriteToDirectory(string directoryPath) {
        try {
            string testFilePath = Path.Combine(directoryPath, Path.GetRandomFileName());
            using FileStream fs = File.Create(testFilePath, 1, FileOptions.DeleteOnClose);
            return true;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Checks if the current process has write access to the given file.
    /// </summary>
    /// <param name="filePath">The path to the file to check.</param>
    /// <returns>true if the process has write access, false otherwise.</returns>
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


    /// <summary>
    /// Gets the name of the user running the application.
    /// If running on Windows, uses WindowsIdentity to get the username.
    /// If running on a non-Windows platform, uses Environment.UserName.
    /// </summary>
    /// <returns>The username of the user running the application.</returns>
    public static string GetUserName() => OperatingSystem.IsWindows() 
        ? WindowsIdentity.GetCurrent().Name 
        : Environment.UserName; 
}