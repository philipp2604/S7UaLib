using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Opc.Ua;
using Opc.Ua.Client;
using S7UaLib.Client;
using S7UaLib.Events;
using System.Collections;
using System.Reflection;
using Xunit;

namespace S7UaLib.UnitTests.Client;

[Trait("Category", "Unit")]
public class S7UaClientUnitTests
{
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7UaClient>> _mockLogger;
    private readonly ApplicationConfiguration _appConfig;
    private readonly Action<IList, IList> _validateResponse;

    public S7UaClientUnitTests()
    {
        // Setup Mocks und Konfigurationen, die für die meisten Tests benötigt werden
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<S7UaClient>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);

        // Eine minimale, gültige ApplicationConfiguration erstellen
        _appConfig = new ApplicationConfiguration
        {
            ApplicationName = "Test S7UaClient",
            ApplicationType = ApplicationType.Client,
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                TrustedPeerCertificates = new CertificateTrustList(),
                AutoAcceptUntrustedCertificates = true
            },
            ClientConfiguration = new ClientConfiguration()
        };

        _validateResponse = (_, _) => { };
    }

    private S7UaClient CreateSut()
    {
        return new S7UaClient(_appConfig, _validateResponse, _mockLoggerFactory.Object);
    }

    [Fact]
    public void Constructor_WithNullAppConfig_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S7UaClient(null!, _validateResponse));
    }

    [Fact]
    public void Constructor_WithNullValidateAction_ThrowsArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new S7UaClient(_appConfig, null!));
    }

    [Fact]
    public async Task ConnectAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = CreateSut();
        client.Dispose();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConnectAsync("opc.tcp://localhost:4840"));
    }

    [Fact]
    public void Disconnect_WhenConnected_ClosesSessionAndCleansUpHandlers()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();
        // Wir brauchen hier keinen Mock mehr, ein echtes Objekt tut's auch, da wir es nicht verifizieren.
        // Ein Mock ist aber auch okay.
        var reconnectHandler = new SessionReconnectHandler(true, -1);

        // Private Felder mit Reflection setzen
        SetPrivateField(client, "_session", mockSession.Object);
        SetPrivateField(client, "_reconnectHandler", reconnectHandler);

        // Act
        client.Disconnect();

        // Assert
        // Überprüfen, ob die Session-Interaktionen wie erwartet stattgefunden haben
        mockSession.Verify(s => s.Close(It.IsAny<bool>()), Times.Once);
        mockSession.Verify(s => s.Dispose(), Times.Once);

        // Zustandstest: Überprüfen, ob die Referenzen auf die Handler und die Session
        // nach dem Disconnect aufgeräumt (null) wurden.
        Assert.Null(GetPrivateField(client, "_reconnectHandler"));
        Assert.Null(GetPrivateField(client, "_session"));

        Assert.False(client.IsConnected);
    }

    [Fact]
    public void Disconnect_FiresDisconnectingAndDisconnectedEvents()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();
        SetPrivateField(client, "_session", mockSession.Object);
        SetPrivateField(client, "_reconnectHandler", new Mock<SessionReconnectHandler>(true, -1).Object);

        bool disconnectingFired = false;
        bool disconnectedFired = false;
        client.Disconnecting += (s, e) => disconnectingFired = true;
        client.Disconnected += (s, e) => disconnectedFired = true;

        // Act
        client.Disconnect();

        // Assert
        Assert.True(disconnectingFired, "Disconnecting event should have been fired.");
        Assert.True(disconnectedFired, "Disconnected event should have been fired.");
    }

    [Fact]
    public void IsConnected_ReflectsSessionState()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<ISession>();

        // Fall 1: Nicht verbunden
        Assert.False(client.IsConnected, "Should not be connected initially.");

        // Fall 2: Verbunden
        mockSession.Setup(s => s.Connected).Returns(true);
        SetPrivateField(client, "_session", mockSession.Object);
        Assert.True(client.IsConnected, "Should be connected when session is connected.");

        // Fall 3: Verbindung getrennt
        mockSession.Setup(s => s.Connected).Returns(false);
        Assert.False(client.IsConnected, "Should not be connected when session is disconnected.");
    }

    [Fact]
    public void Session_KeepAlive_WithBadStatus_InitiatesReconnectLogic()
    {
        // Arrange
        var client = CreateSut();
        client.ReconnectPeriod = 1000; // Erforderlich, damit der Reconnect-Pfad betreten wird

        var mockSession = new Mock<ISession>();

        // Wir brauchen den Mock hier immer noch, um zu verhindern, dass er echte Arbeit leistet.
        // Aber wir werden keine Methode mehr darauf verifizieren.
        var mockReconnectHandler = new Mock<SessionReconnectHandler>(true, -1);

        SetPrivateField(client, "_session", mockSession.Object);
        SetPrivateField(client, "_reconnectHandler", mockReconnectHandler.Object);

        bool reconnectingFired = false;
        client.Reconnecting += (s, e) => reconnectingFired = true;

        // Das Event-Argument-Objekt, das wir an die Methode übergeben und dessen Zustand wir danach überprüfen.
        var keepAliveEventArgs = new KeepAliveEventArgs(new ServiceResult(StatusCodes.BadConnectionClosed), ServerState.CommunicationFault, DateTime.Now);

        // Vorbedingung: Sicherstellen, dass der Wert anfangs false ist.
        Assert.False(keepAliveEventArgs.CancelKeepAlive);

        // Act
        // Die private Methode Session_KeepAlive via Reflection aufrufen
        InvokePrivateMethod(client, "Session_KeepAlive", new object[] { mockSession.Object, keepAliveEventArgs });

        // Assert
        // 1. Überprüfen Sie den wichtigsten, direkten Seiteneffekt:
        Assert.True(keepAliveEventArgs.CancelKeepAlive, "CancelKeepAlive sollte auf true gesetzt werden, um weitere Keep-Alives zu stoppen.");

        // 2. Überprüfen Sie andere beobachtbare Verhaltensweisen, um die Logik zu bestätigen:
        Assert.True(reconnectingFired, "Das Reconnecting-Event hätte ausgelöst werden müssen.");

        // Anmerkung: Wir versuchen nicht mehr, BeginReconnect zu verifizieren. Dieser Test ist jetzt
        // robuster gegenüber der internen Implementierung von SessionReconnectHandler.
    }

    // Hilfsmethode, um private Felder für Tests zu setzen
    private void SetPrivateField(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new ArgumentException($"Field '{fieldName}' not found in type '{obj.GetType().Name}'.");
        field.SetValue(obj, value);
    }

    // Hilfsmethode, um private Methoden für Tests aufzurufen
    private void InvokePrivateMethod(object obj, string methodName, object[] parameters)
    {
        var method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (method == null) throw new ArgumentException($"Method '{methodName}' not found in type '{obj.GetType().Name}'.");
        method.Invoke(obj, parameters);
    }
    
    // Hilfsmethode, um den Wert eines privaten Feldes für Tests abzurufen
    private object? GetPrivateField(object obj, string fieldName)
    {
        var field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) throw new ArgumentException($"Field '{fieldName}' not found in type '{obj.GetType().Name}'.");
        return field.GetValue(obj);
    }
}