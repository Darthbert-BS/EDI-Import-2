using Microsoft.Extensions.DependencyInjection;
using BundabergSugar.EDIImport.Services.Database;
using BundabergSugar.EDIImport.Services.Import;
using Microsoft.Extensions.Configuration;

namespace BundabergSugar.EDIImport.Services;

public static class DependencyInjection {

    public static IServiceCollection AddServices(this IServiceCollection services, IConfigurationManager config) {
        
        //Configuring the application options
        ApplicationOptions appOptions = new();  
        config.GetSection(nameof(ApplicationOptions)).Bind(appOptions);
        
        services.AddSingleton<IApplicationOptions>(appOptions);
        services.AddSingleton<IDatabaseService, DatabaseService>();
        services.AddSingleton<IImportService, ImportService>();
        
        return services;
    }
}