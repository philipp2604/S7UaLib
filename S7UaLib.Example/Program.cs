using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Configuration;
using S7UaLib.Client;

namespace S7UaLib.Example;

internal class Program
{
    static async Task Main(string[] args)
    {
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder
                .AddSimpleConsole(options =>
                {
                    options.IncludeScopes = true;
                    options.SingleLine = true;
                    options.TimestampFormat = "hh:mm:ss ";
                })
                .SetMinimumLevel(LogLevel.Information);
        });

        const string appName = "S7UaLib.Example";
        const string configSectionName = "S7UaLib.Example";

        const string serverUrl = "opc.tcp://172.168.0.1:4840";

        var appInstance = new ApplicationInstance
        {
            ApplicationName = appName,
            ApplicationType = ApplicationType.Client,
            ConfigSectionName = configSectionName
        };

        var config = await appInstance.LoadApplicationConfiguration(false).ConfigureAwait(false);

        using S7UaClient client = new S7UaClient(config, ClientBase.ValidateResponse, loggerFactory);
        client.AcceptUntrustedCertificates = true;
        await client.ConnectAsync(serverUrl);
        Thread.Sleep(2000);
        client.Disconnect();
    }
}
