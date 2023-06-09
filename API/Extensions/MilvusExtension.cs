namespace Realsearch.API.Extensions;

using Realsearch.API.Services;
using Realsearch.API.Services.Interfaces;

public static class MilvusExtension
{
    public static void AddMilvus(this IServiceCollection services)
    {
        services.AddSingleton<IMilvusService, MilvusService>();
    }
}