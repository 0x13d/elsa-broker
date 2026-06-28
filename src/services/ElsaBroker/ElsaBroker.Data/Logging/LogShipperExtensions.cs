using ElsaBroker.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace ElsaBroker.Data.Logging;

/// <summary>
/// DI registration helpers for <see cref="ILogShipper"/>.
/// </summary>
public static class LogShipperExtensions
{
    /// <summary>Register a custom <see cref="ILogShipper"/> implementation as a singleton.</summary>
    public static IServiceCollection AddLogShipper<T>(this IServiceCollection services)
        where T : class, ILogShipper
        => services.AddSingleton<ILogShipper, T>();

    /// <summary>Register the no-op shipper (default — events are discarded).</summary>
    public static IServiceCollection AddNullLogShipper(this IServiceCollection services)
        => services.AddSingleton<ILogShipper, NullLogShipper>();

    /// <summary>Register the console shipper (structured JSON to stdout).</summary>
    public static IServiceCollection AddConsoleLogShipper(this IServiceCollection services)
        => services.AddSingleton<ILogShipper, ConsoleLogShipper>();

    /// <summary>
    /// Register the HTTP/JSON shipper targeting a configurable sink URL.
    /// Works with Seq, Logstash, OpenSearch, or any JSON-accepting endpoint.
    /// </summary>
    public static IServiceCollection AddHttpJsonLogShipper(
        this IServiceCollection services,
        Action<HttpJsonLogShipperOptions> configure)
    {
        var options = new HttpJsonLogShipperOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<ILogShipper, HttpJsonLogShipper>();
        return services;
    }
}
