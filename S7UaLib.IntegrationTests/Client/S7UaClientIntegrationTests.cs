using Xunit;
using S7UaLib.Client;
using Opc.Ua;
using System.Collections;
using System.Threading.Tasks;

namespace S7UaLib.IntegrationTests.Client;

// Trait-Attribut, um Integrations- von Unit-Tests zu trennen (optional, aber gute Praxis)
[Trait("Category", "Integration")]
public class S7UaClientIntegrationTests
{
    // Die URL Ihres Test-OPC-UA-Servers. Passen Sie diese bei Bedarf an.
    private const string _serverUrl = "opc.tcp://milo.digitalpetri.com:62541/milo";

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

        // Standard-Validierungsaktion aus der OPC UA Foundation Bibliothek
        _validateResponse = ClientBase.ValidateResponse;
    }

    [Fact]
    // Dieser Test wird übersprungen, wenn kein Server läuft. Entfernen Sie 'Skip', um ihn auszuführen.
    // [Fact(Skip = "Requires a running OPC UA Server at " + ServerUrl)] 
    public async Task ConnectAndDisconnect_Successfully()
    {
        // Arrange
        var client = new S7UaClient(_appConfig, _validateResponse);
        client.AcceptUntrustedCertificates = true; // Notwendig, wenn der Server ein selbst-signiertes Zertifikat hat

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

            // Warten auf das Connected-Event (mit Timeout, um unendliches Warten zu verhindern)
            bool connectedInTime = connectedEvent.Wait(TimeSpan.FromSeconds(10));

            Assert.True(connectedInTime, "The 'Connected' event was not fired within the timeout.");
            Assert.True(client.IsConnected, "Client should be connected after ConnectAsync.");
            Assert.True(connectedFired, "Connected event flag should be true.");

            // Act & Assert: Disconnect
            client.Disconnect();

            // Warten auf das Disconnected-Event
            bool disconnectedInTime = disconnectedEvent.Wait(TimeSpan.FromSeconds(5));

            Assert.True(disconnectedInTime, "The 'Disconnected' event was not fired within the timeout.");
            Assert.False(client.IsConnected, "Client should be disconnected after Disconnect.");
            Assert.True(disconnectedFired, "Disconnected event flag should be true.");
        }
        catch (ServiceResultException ex)
        {
            // Fängt Fehler ab, wenn der Server nicht erreichbar ist, und gibt eine hilfreiche Meldung aus
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }
        finally
        {
            // Cleanup
            client.Dispose();
        }
    }
}