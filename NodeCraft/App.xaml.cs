using System;
using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NLog;
using NLog.Config;
using NLog.Extensions.Logging;
using NLog.Targets;
using NodeCraft.Flow;
using NodeCraft.Pages;
using NodeCraft.Plugins;

namespace NodeCraft
{
    public partial class App : Application
    {
        public static PluginLoadReport PluginLoadReport { get; private set; }
            = new PluginLoadReport(Array.Empty<PluginLoadResult>());

        public static IServiceProvider Services { get; private set; }

        public string StartupGraphFilePath { get; private set; }

        private IConfiguration _configuration;

        protected override void OnStartup(StartupEventArgs e)
        {
            _configuration = BuildConfiguration();
            StartupGraphFilePath = StartupGraphPathResolver.TryResolve(e.Args);

            var services = new ServiceCollection();
            services.AddLogging(builder =>
            {
                if (_configuration.GetSection("NLog").Exists())
                {
                    try
                    {
                        builder.AddConfiguration(_configuration.GetSection("Logging"));
                        builder.AddNLog(_configuration);
                        return;
                    }
                    catch
                    {
                        // NLog 段解析失败时回退到程序化默认目标。
                    }
                }

                builder.AddNLog(BuildFallbackLoggingConfiguration());
            });
            services.AddSingleton<IConfiguration>(_configuration);
            services.AddTransient<PluginLoader>(provider => new PluginLoader(
                NodeExecutorFactory.Registry,
                new Version(1, 0),
                provider.GetRequiredService<ILoggerFactory>()));
            services.AddTransient<FlowPage>();
            services.AddTransient<MainWindow>();

            Services = services.BuildServiceProvider();
            PluginLoadReport = Services.GetRequiredService<PluginLoader>().LoadAll(
                Path.Combine(AppContext.BaseDirectory, "Plugins"));

            var window = Services.GetRequiredService<MainWindow>();
            MainWindow = window;
            window.Show();
            base.OnStartup(e);
        }

        protected override void OnExit(ExitEventArgs e)
        {
            (Services as IDisposable)?.Dispose();
            LogManager.Shutdown();
            base.OnExit(e);
        }

        private static IConfiguration BuildConfiguration()
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true);
            try
            {
                return builder.Build();
            }
            catch
            {
                // appsettings.json 缺失或格式错误时用空配置，走程序化兜底。
                return new ConfigurationBuilder().Build();
            }
        }

        private static LoggingConfiguration BuildFallbackLoggingConfiguration()
        {
            var configuration = new LoggingConfiguration();
            var fileTarget = new FileTarget("nodecraft-fallback-file")
            {
                FileName = Path.Combine(DefaultLogDirectory(), "nodecraft-${shortdate}.log"),
                Layout = "${longdate}|${level:uppercase=true}|${logger}|${message}${onexception:inner= |${exception:format=tostring}}",
                KeepFileOpen = false,
                Encoding = System.Text.Encoding.UTF8,
                ArchiveEvery = FileArchivePeriod.Day,
                MaxArchiveFiles = 30,
            };
            configuration.AddTarget(fileTarget);
            configuration.AddRule(NLog.LogLevel.Debug, NLog.LogLevel.Fatal, fileTarget);
            return configuration;
        }

        private static string DefaultLogDirectory()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "NodeCraft",
                "Logs");
        }
    }
}
