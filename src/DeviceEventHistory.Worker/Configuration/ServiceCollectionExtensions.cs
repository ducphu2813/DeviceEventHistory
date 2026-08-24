using DeviceEventHistory.Application.Metadata;
using DeviceEventHistory.Infrastructure.Metadata;
using DeviceEventHistory.Infrastructure.MongoDb.Configuration;
using DeviceEventHistory.Infrastructure.RfidRawLog.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DeviceEventHistory.Worker.Configuration;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddDeviceEventHistoryConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<WorkerOptions>()
            .Bind(configuration.GetSection(WorkerOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<WorkerOptions>, WorkerOptionsValidator>();

        services.AddOptions<RfidRawLogOptions>()
            .Bind(configuration.GetSection(RfidRawLogOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<RfidRawLogOptions>, RfidRawLogOptionsValidator>();

        services.AddOptions<MongoDbOptions>()
            .Bind(configuration.GetSection(MongoDbOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IConfigureOptions<MongoDbOptions>, ConfigurationOptionsRegistration>();
        services.AddSingleton<IValidateOptions<MongoDbOptions>, MongoDbOptionsValidator>();

        services.AddOptions<IngestionOptions>()
            .Bind(configuration.GetSection(IngestionOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<IngestionOptions>, IngestionOptionsValidator>();

        services.AddSingleton<ConfigurationRedactor>();
        services.AddSingleton<ConfigurationDeviceMetadataResolver>(serviceProvider =>
            new ConfigurationDeviceMetadataResolver(
                serviceProvider.GetRequiredService<IOptions<RfidRawLogOptions>>().Value));
        services.AddSingleton<IDeviceMetadataResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<ConfigurationDeviceMetadataResolver>());

        return services;
    }
}
