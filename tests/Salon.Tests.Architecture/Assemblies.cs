using System.Reflection;

namespace Salon.Tests.Architecture;

internal static class Assemblies
{
    public static readonly Assembly Core = typeof(Salon.Core.Domain.BaseEntity).Assembly;
    public static readonly Assembly Data = typeof(Salon.Data.SalonDbContext).Assembly;
    public static readonly Assembly Services = typeof(Salon.Services.ServiceCollectionExtensions).Assembly;
    public static readonly Assembly WebFramework = typeof(Salon.Web.Framework.Controllers.SalonControllerBase).Assembly;
    public static readonly Assembly Web = typeof(Program).Assembly;
    public static readonly Assembly[] Plugins =
    [
        typeof(Salon.Plugin.Import.CsvClientImportProvider).Assembly,
        typeof(Salon.Plugin.Payments.Manual.ManualPaymentProvider).Assembly,
        typeof(Salon.Plugin.Sms.Log.LogSmsProvider).Assembly,
    ];
}