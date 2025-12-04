using EtfInsight.Portfolio.Valuation;

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration((hostingContext, config) =>
    {
        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{hostingContext.HostingEnvironment.EnvironmentName}.json", optional: true, reloadOnChange: true);
    })
    .ConfigureServices((context, services) =>
    {
        var configuration = context.Configuration;

        services.AddSingleton<IConfiguration>(configuration);

        services.AddHostedService<PortfolioValuationWorker>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
    })
    .Build();

await host.RunAsync();
