using Xunit;
using S7UaLib.Client;
using Opc.Ua;
using System.Collections;
using System.Threading.Tasks;

namespace S7UaLib.IntegrationTests.Client;

[Trait("Category", "Integration")]
public class S7UaClientIntegrationTests
{
    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";

    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;

    public S7UaClientIntegrationTests()
    {
        _appConfig = new ApplicationConfiguration
        {
            ApplicationName = "Integration Test Client",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                TrustedPeerCertificates = new CertificateTrustList(),
                AutoAcceptUntrustedCertificates = true
            },
            ClientConfiguration = new ClientConfiguration(),
            TransportQuotas = new TransportQuotas { OperationTimeout = 15000 }
        };

        _validateResponse = ClientBase.ValidateResponse;
    }

    [Fact(Skip = "Requires a running OPC UA Server at " + _serverUrl)]
    public async Task ConnectAndDisconnect_Successfully()
    {
        // Arrange
        var client = new S7UaClient(_appConfig, _validateResponse)
        {
            AcceptUntrustedCertificates = true
        };

        bool connectedFired = false;
        bool disconnectedFired = false;

        var connectedEvent = new ManualResetEventSlim();
        var disconnectedEvent = new ManualResetEventSlim();

        client.Connected += (s, e) => {
            connectedFired = true;
            connectedEvent.Set();
        };
        client.Disconnected += (s, e) => {
            disconnectedFired = true;
            disconnectedEvent.Set();
        };

        // Act & Assert: Connect
        try
        {
            await client.ConnectAsync(_serverUrl, useSecurity: false);

            bool connectedInTime = connectedEvent.Wait(TimeSpan.FromSeconds(10));

            Assert.True(connectedInTime, "The 'Connected' event was not fired within the timeout.");
            Assert.True(client.IsConnected, "Client should be connected after ConnectAsync.");
            Assert.True(connectedFired, "Connected event flag should be true.");

            // Act & Assert: Disconnect
            client.Disconnect();

            bool disconnectedInTime = disconnectedEvent.Wait(TimeSpan.FromSeconds(5));

            Assert.True(disconnectedInTime, "The 'Disconnected' event was not fired within the timeout.");
            Assert.False(client.IsConnected, "Client should be disconnected after Disconnect.");
            Assert.True(disconnectedFired, "Disconnected event flag should be true.");
        }
        catch (ServiceResultException ex)
        {
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            client.Dispose();
        }
    }
}