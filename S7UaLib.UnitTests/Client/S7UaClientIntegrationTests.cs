using Xunit;
using S7UaLib.Client;
using Opc.Ua;
using System.Collections;
using System.Threading.Tasks;

namespace S7UaLib.Test.Client;

// Trait-Attribut, um Integrations- von Unit-Tests zu trennen (optional, aber gute Praxis)
[Trait("Category", "Integration")]
public class S7UaClientIntegrationTests
{
    // Die URL Ihres Test-OPC-UA-Servers. Passen Sie diese bei Bedarf an.
    private const string ServerUrl = "opc.tcp://localhost:4840";

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
}