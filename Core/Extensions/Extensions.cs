
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Configuration;
using BundabergSugar.Core.Providers.Logging;

namespace BundabergSugar.Core.Extensions;

public static class LoggingExtensions {

    public static ILoggingBuilder AddDataBaseLogger(this ILoggingBuilder builder) {
        builder.AddConfiguration();
                
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, DBLoggerProvider>());

         LoggerProviderOptions.RegisterProviderOptions
            <DBLoggerConfiguration, DBLoggerProvider>(builder.Services);

        return builder;
    }


    public static ILoggingBuilder AddDataBaseLogger(this ILoggingBuilder builder, Action<DBLoggerConfiguration> configure) {
        builder.AddDataBaseLogger();
        builder.Services.Configure(configure);
        
        return builder;
    }


    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder) {
        builder.AddConfiguration();
        
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<ILoggerProvider, FileLoggerProvider>());
        
        LoggerProviderOptions.RegisterProviderOptions
            <FileLoggerConfiguration, FileLoggerProvider>(builder.Services);

        return builder;
    }


    public static ILoggingBuilder AddFileLogger(this ILoggingBuilder builder, Action<FileLoggerConfiguration> configure) {
        builder.AddFileLogger();
        builder.Services.Configure(configure);
        return builder;
    }
}


public static class StringExtensions {
    /// <summary>
    /// Removes line endings from a string.
    /// </summary>
    /// <param name="value">The string to remove line endings from.</param>
    /// <returns>The string with all line endings removed.</returns>
    public static string RemoveLineEndings(this string value) {
        return value
            .Replace("\r", string.Empty)
            .Replace("\n", string.Empty);
    }

    

    /// <summary>
    /// Returns a substring of a given string, safely handling cases where
    /// the string is null, empty, or the requested substring is out of bounds.
    /// </summary>
    /// <param name="value">The string to get a substring from.</param>
    /// <param name="start">The index of the first character to include in the substring.</param>
    /// <param name="len">The maximum length of the substring to return.</param>
    /// <param name="trim">Whether to trim the string before getting the substring.</param>
    /// <returns>The substring of the given string, or the original string if the substring is out of bounds.</returns>
    public static string SafeSubstring(this string value, int start = 0, int len = 0, bool trim = true) {
        if (string.IsNullOrEmpty(value)) return value;
        var str = trim ? value.Trim() : value;
        var maxLen = str.Length > len ? len : str.Length;
        return str[..maxLen];
    }

}



public static class LongExtensions {
    /// <summary>
    /// Converts the given number of megabytes to bytes.
    /// </summary>
    /// <param name="megabytes">The number of megabytes to convert.</param>
    /// <returns>The number of bytes represented by the given number of megabytes.</returns>
    public static long ConvertMbToBytes(this double megabytes) {
        return (long)(megabytes * 1024 * 1024);
    }

        /// <summary>
        /// Converts the given number of megabytes to bytes.
        /// </summary>
        /// <param name="megabytes">The number of megabytes to convert.</param>
        /// <returns>The number of bytes represented by the given number of megabytes.</returns>
    public static long ConvertMbToBytes(this int megabytes) {
        return megabytes * 1024 * 1024;
    }
}



public static class FileInfoExtensions {
    public static Encoding GetFileEncoding(this FileInfo fileInfo) {
         // Read the BOM
        byte[] bom = new byte[4];
        using var fs = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read);
        fs.Read(bom, 0, 4);

        // Analyze the BOM
        if (bom[0] == 0x2b && bom[1] == 0x2f && bom[2] == 0x76)
            // Encoding.UTF7;
            throw new NotSupportedException("Encoding.UTF7 is obsolete: The UTF-7 encoding is insecure and should not be used. [https://learn.microsoft.com/en-us/dotnet/fundamentals/syslib-diagnostics/syslib0001]"); 
        if (bom[0] == 0xef && bom[1] == 0xbb && bom[2] == 0xbf)
            return Encoding.UTF8;
        if (bom[0] == 0xff && bom[1] == 0xfe && bom[2] == 0x00 && bom[3] == 0x00)
            return Encoding.UTF32; // UTF-32 LE
        if (bom[0] == 0xff && bom[1] == 0xfe)
            return Encoding.Unicode; // UTF-16 LE
        if (bom[0] == 0xfe && bom[1] == 0xff)
            return Encoding.BigEndianUnicode; // UTF-16 BE
        if (bom[0] == 0x00 && bom[1] == 0x00 && bom[2] == 0xfe && bom[3] == 0xff)
            return Encoding.GetEncoding(12001); // UTF-32 BE

        // Default to ASCII encoding if BOM is not found
        return Encoding.ASCII;
   }
}