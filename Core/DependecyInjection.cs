
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using BundabergSugar.Core.Extensions;

namespace BundabergSugar.Core;

public static class DependencyInjection {

    public static IServiceCollection AddLoggingServices(this IServiceCollection services, IHostEnvironment hostEnvironment){
        //Set up Logging 
        if (hostEnvironment.IsInDevelopmentMode()) {
            services.AddLogging(logBuilder => logBuilder.AddDebug());
        }

        services.AddLogging(logBuilder => logBuilder.AddConsole());
        services.AddLogging(logBuilder => logBuilder.AddDataBaseLogger());
        services.AddLogging(logBuilder => logBuilder.AddFileLogger());
        return services;
    }
}