using DefaultArguments.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RemotePythonExecution.Services;
using Serilog;
using Serilog.Core;
using ServiceFileCreator.Extensions;
using ServiceFileCreator.Model;
using System;
using System.Threading.Tasks;

namespace RemotePythonExecution
{
    internal class Program
    {

        static async Task Main(string[] args)
        {
            IHost host = Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, config) =>
                {
                    config.Sources.Clear();
                    config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                })

                .ConfigureServices((context, services) =>
                {
                    services.AddAdamDefaultArgumentsParser(args);

                    AppSettings options = new();
                    services.Configure<AppSettings>(context.Configuration.GetRequiredSection(nameof(AppSettings)));
                    
                    services.AddLogging(loggingBuilder =>
                    {
                        Logger logger = new LoggerConfiguration()
                            .ReadFrom.Configuration(context.Configuration)
                            .CreateLogger();

                        loggingBuilder.ClearProviders();
                        loggingBuilder.AddSerilog(logger, dispose: true);
                    });

                    services.Configure<HostOptions>(option =>
                    {
                        option.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost;
                        option.ShutdownTimeout = TimeSpan.FromSeconds(5);
                    });

                    services.AddAdamServiceFileCreator();
                    services.AddHostedService<RemotePythonExecutionService>();
                })
                .Build();

            host.UseAdamServiceFileCreator(projectType: ProjectType.DotnetProject);
            await host.ParseAndRunAsync();

     
        }
    }
}
