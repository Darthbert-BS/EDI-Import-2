using Microsoft.Extensions.Hosting;

namespace BundabergSugar.Core.Extensions;

public static class HostEnvironmentExtensions {
    
    public static bool IsInDebugMode(this IHostEnvironment env) => env.EnvironmentName.Equals("debug", StringComparison.CurrentCultureIgnoreCase);
    
    public static bool IsInTestingMode(this IHostEnvironment env) =>  env.EnvironmentName.Equals("testing", StringComparison.CurrentCultureIgnoreCase);
    
    public static bool IsInStagingMode(this IHostEnvironment env) =>  env.EnvironmentName.Equals("staging", StringComparison.CurrentCultureIgnoreCase);
    
    public static bool IsInReleaseMode(this IHostEnvironment env) =>  env.EnvironmentName.Equals("release", StringComparison.CurrentCultureIgnoreCase);
   
    public static bool IsInDevelopmentMode(this IHostEnvironment env) => env.IsInDebugMode() || env.IsDevelopment();
    
    public static bool IsInProductionMode(this IHostEnvironment env) => env.IsInReleaseMode() || env.IsProduction();
}