using Opc.Ua;
using S7UaLib.Client;
using S7UaLib.S7.Structure;
using System.Collections;

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

    #region Connection Tests

    [Fact]
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

        client.Connected += (s, e) =>
        {
            connectedFired = true;
            connectedEvent.Set();
        };
        client.Disconnected += (s, e) =>
        {
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

    #endregion Connection Tests

    #region Helper Methods

    private async Task<S7UaClient> CreateAndConnectClientAsync()
    {
        var client = new S7UaClient(_appConfig, _validateResponse)
        {
            AcceptUntrustedCertificates = true
        };

        try
        {
            await client.ConnectAsync(_serverUrl, useSecurity: false);
        }
        catch (ServiceResultException ex)
        {
            Assert.Fail($"Failed to connect to the server at '{_serverUrl}'. Ensure the server is running. Error: {ex.Message}");
        }

        Assert.True(client.IsConnected, "Client-Setup failed, could not connect to server..");
        return client;
    }

    #endregion Helper Methods

    #region Structure Discovery and Browsing Tests

    [Fact]
    public async Task GetAllInstanceDataBlocks_ReturnsDataFromRealServer()
    {
        // Arrange
        using var client = await CreateAndConnectClientAsync();

        // Act
        var instanceDbs = client.GetAllInstanceDataBlocks();

        // Assert
        Assert.NotNull(instanceDbs);
        Assert.True(instanceDbs.Count > 0, "It was expected to find at least one instance data block.");

        Assert.Contains(instanceDbs, db => db.DisplayName == "FunctionBlock_InstDB");
    }

    [Fact]
    public async Task GetInputs_And_DiscoverVariables_ReturnsPopulatedElement()
    {
        // Arrange
        using var client = await CreateAndConnectClientAsync();

        // Act
        var inputsShell = client.GetInputs();

        // Assert
        Assert.NotNull(inputsShell);
        Assert.False(inputsShell.Variables?.Any(), "Variables list should be empty before discovery.");

        // Act
        var populatedInputs = client.DiscoverVariablesOfElement(inputsShell);

        // Assert
        Assert.NotNull(populatedInputs);
        Assert.NotNull(populatedInputs.Variables);
        Assert.True(populatedInputs.Variables.Count > 0, "At least one variable was expected in global Inputs.");

        Assert.Contains(populatedInputs.Variables, v => v.DisplayName == "TestInput");
    }

    [Fact]
    public async Task DiscoverElement_WithRealInstanceDb_ReturnsFullyPopulatedDb()
    {
        // Arrange
        using var client = await CreateAndConnectClientAsync();

        var instanceDbs = client.GetAllInstanceDataBlocks();

        var dbShell = instanceDbs.FirstOrDefault(db => db.DisplayName == "FunctionBlock_InstDB");
        Assert.NotNull(dbShell);

        Assert.Null(dbShell.Outputs);
        Assert.Null(dbShell.Inputs);

        // Act
        var discoveredElement = client.DiscoverElement(dbShell);

        // Assert
        Assert.NotNull(discoveredElement);
        Assert.IsType<S7DataBlockInstance>(discoveredElement);
        var populatedDb = (S7DataBlockInstance)discoveredElement;

        Assert.NotNull(populatedDb.Outputs);
        Assert.NotNull(populatedDb.Inputs);

        Assert.NotEmpty(populatedDb.Outputs.Variables);
        Assert.NotEmpty(populatedDb.Inputs.Variables);

        Assert.Contains(populatedDb.Inputs.Variables, v => v.DisplayName == "Function_InputBool");
    }

    #endregion Structure Discovery and Browsing Tests
}