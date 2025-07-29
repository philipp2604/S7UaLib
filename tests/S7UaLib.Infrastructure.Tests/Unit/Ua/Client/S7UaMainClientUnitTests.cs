using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.Ua.Client;
using S7UaLib.TestHelpers;
using System.Collections;

namespace S7UaLib.Infrastructure.Tests.Unit.Ua.Client;

[Trait("Category", "Unit")]
public class S7UaMainClientUnitTests
{
    private readonly Mock<ILoggerFactory> _mockLoggerFactory;
    private readonly Mock<ILogger<S7UaMainClient>> _mockLogger;
    private readonly Action<IList, IList> _validateResponse;
    private readonly Core.Ua.UserIdentity _userIdentity = new();

    public S7UaMainClientUnitTests()
    {
        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLogger = new Mock<ILogger<S7UaMainClient>>();
        _mockLoggerFactory.Setup(f => f.CreateLogger(It.IsAny<string>())).Returns(_mockLogger.Object);

        _validateResponse = (_, _) => { };
    }

    private S7UaMainClient CreateSut()
    {
        return new S7UaMainClient(_userIdentity, _validateResponse, _mockLoggerFactory.Object);
    }

    private class TempDirectory : IDisposable
    {
        public string Path { get; }

        public TempDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(Path))
                {
                    Directory.Delete(Path, true);
                }
            }
            catch (IOException)
            {
                // Suppress exceptions during cleanup
            }
        }
    }

    #region Configuration Tests

    [Fact]
    public async Task ConfigureAsync_WhenDisposed_ThrowsObjectDisposedException()
    {
        // Arrange
        var client = CreateSut();
        client.Dispose();
        var appConfig = new ApplicationConfiguration();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => client.ConfigureAsync(appConfig));
    }

    [Fact]
    public async Task ConfigureAsync_WithValidConfig_CreatesAndSetsApplicationInstance()
    {
        // Arrange
        var client = CreateSut();
        var appConfig = new ApplicationConfiguration
        {
            ApplicationName = "TestApp",
            ApplicationUri = "urn:test",
            ProductUri = "urn:test:prod"
        };
        appConfig.SecurityConfiguration.SecurityConfigurationStores.SubjectName = "CN=TestCert";

        // Act
        await client.ConfigureAsync(appConfig);

        // Assert
        Assert.True(client.IsConfigured);
        Assert.NotNull(client.ApplicationConfiguration);
        Assert.Same(appConfig, client.ApplicationConfiguration);

        var opcAppConfig = client.OpcApplicationConfiguration;
        Assert.NotNull(opcAppConfig);
        Assert.Equal("TestApp", opcAppConfig.ApplicationName);
        Assert.Equal("urn:test", opcAppConfig.ApplicationUri);
        Assert.Equal("CN=TestCert", opcAppConfig.SecurityConfiguration.ApplicationCertificate.SubjectName);
    }

    [Fact]
    public void SaveConfiguration_WhenNotConfigured_ThrowsArgumentNullException()
    {
        // Arrange
        var client = CreateSut();

        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => client.SaveConfiguration("test.xml"));
    }

    [Fact]
    public async Task SaveAndLoadConfiguration_Integration_WorksCorrectly()
    {
        // Arrange
        using var tempDir = new TempDirectory();
        var configFilePath = Path.Combine(tempDir.Path, "config.xml");

        var saveClient = CreateSut();
        var appConfigToSave = new ApplicationConfiguration
        {
            ApplicationName = "SavedApp",
            ApplicationUri = "urn:saved",
            ProductUri = "urn:prod:saved",
            ClientConfiguration = { SessionTimeout = 99000 }
        };
        appConfigToSave.SecurityConfiguration.SecurityConfigurationStores.AppRoot = Path.Combine(tempDir.Path, "certs");

        await saveClient.ConfigureAsync(appConfigToSave);

        // Act 1: Save
        saveClient.SaveConfiguration(configFilePath);

        // Assert 1: File exists and contains correct data
        Assert.True(File.Exists(configFilePath));
        var fileContent = await File.ReadAllTextAsync(configFilePath);
        Assert.Contains("<ApplicationName>SavedApp</ApplicationName>", fileContent);
        Assert.Contains("<DefaultSessionTimeout>99000</DefaultSessionTimeout>", fileContent);

        // Arrange 2: Create new client and load configuration
        var loadClient = CreateSut();
        // A base configuration is required before loading.
        await loadClient.ConfigureAsync(new ApplicationConfiguration { ApplicationName = "InitialApp" });

        // Act 2: Load
        await loadClient.LoadConfigurationAsync(configFilePath);

        // Assert 2: Configuration is updated
        Assert.Equal("SavedApp", loadClient.ApplicationConfiguration!.ApplicationName);
        Assert.Equal((uint)99000, loadClient.ApplicationConfiguration.ClientConfiguration.SessionTimeout);
        Assert.Equal("urn:saved", loadClient.OpcApplicationConfiguration!.ApplicationUri);
    }

    #endregion Configuration Tests

    #region Connection and Reconnection Tests

    [Fact]
    public async Task ConnectAsync_WhenNotConfigured_ThrowsArgumentNullException()
    {
        // Arrange
        var client = CreateSut();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => client.ConnectAsync("opc.tcp://localhost"));
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_ClosesSessionAndFiresEvents()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        var reconnectHandler = new Mock<Opc.Ua.Client.SessionReconnectHandler>(true, -1);
        bool disconnectingFired = false;
        bool disconnectedFired = false;
        client.Disconnecting += (s, e) => disconnectingFired = true;
        client.Disconnected += (s, e) => disconnectedFired = true;

        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_reconnectHandler", reconnectHandler.Object);

        // Act
        await client.DisconnectAsync();

        // Assert
        mockSession.Verify(s => s.Close(It.IsAny<bool>()), Times.Once);
        mockSession.Verify(s => s.Dispose(), Times.Once);

        Assert.Null(PrivateFieldHelpers.GetPrivateField(client, "_reconnectHandler"));

        Assert.False(client.IsConnected);
        Assert.True(disconnectingFired, "Disconnecting event should have fired.");
        Assert.True(disconnectedFired, "Disconnected event should have fired.");
    }

    [Fact]
    public void Session_KeepAlive_WithBadStatus_InitiatesReconnectLogic()
    {
        // Arrange
        var client = CreateSut();
        client.ReconnectPeriod = 1000;
        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        var mockReconnectHandler = new Mock<Opc.Ua.Client.SessionReconnectHandler>(true, -1);
        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        PrivateFieldHelpers.SetPrivateField(client, "_reconnectHandler", mockReconnectHandler.Object);

        bool reconnectingFired = false;
        client.Reconnecting += (s, e) => reconnectingFired = true;

        var keepAliveEventArgs = new Opc.Ua.Client.KeepAliveEventArgs(new Opc.Ua.ServiceResult(Opc.Ua.StatusCodes.BadConnectionClosed), Opc.Ua.ServerState.CommunicationFault, DateTime.Now);

        // Act
        PrivateMethodHelpers.InvokePrivateMethod(client, "Session_KeepAlive", [mockSession.Object, keepAliveEventArgs]);

        // Assert
        Assert.True(keepAliveEventArgs.CancelKeepAlive, "KeepAlive should be cancelled to stop further pings.");
        Assert.True(reconnectingFired, "Reconnecting event should have been fired.");
    }

    [Fact]
    public void IsConnected_ReflectsSessionState()
    {
        // Arrange
        var client = CreateSut();
        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        Assert.False(client.IsConnected, "Should not be connected initially.");

        // Act & Assert
        mockSession.Setup(s => s.Connected).Returns(true);
        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);
        Assert.True(client.IsConnected, "Should be connected when session is connected.");

        // Act & Assert
        mockSession.Setup(s => s.Connected).Returns(false);
        Assert.False(client.IsConnected, "Should not be connected when session is disconnected.");
    }

    #endregion Connection and Reconnection Tests

    #region Subscription Tests

    [Fact]
    public async Task CreateSubscriptionAsync_WhenNotConnected_ReturnsFalseAndLogsError()
    {
        // Arrange
        var client = CreateSut();
        // Client is not connected by default

        // Act
        var result = await client.CreateSubscriptionAsync();

        // Assert
        Assert.False(result);
        _mockLogger.Verify(
            log => log.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Cannot create subscription; session is not connected.")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSubscriptionAsync_WhenCalledFirstTime_CreatesAndRegistersSubscription()
    {
        // Arrange
        var mockClient = new Mock<S7UaMainClient>(_userIdentity, _validateResponse, _mockLoggerFactory.Object) { CallBase = true };
        var client = mockClient.Object;
        var mockSession = new Mock<Opc.Ua.Client.ISession>();
        mockSession.SetupGet(s => s.Connected).Returns(true);
        PrivateFieldHelpers.SetPrivateField(client, "_session", mockSession.Object);

        var mockSubscription = new Mock<Opc.Ua.Client.Subscription>();
        // Mock the protected virtual methods to control the behavior without real server interaction.
        mockClient.Protected().Setup<Opc.Ua.Client.Subscription>("CreateNewSubscription", ItExpr.IsAny<int>()).Returns(mockSubscription.Object);
        mockClient.Protected().Setup<Task>("CreateSubscriptionOnServerAsync", ItExpr.IsAny<Opc.Ua.Client.Subscription>()).Returns(Task.CompletedTask);

        // Act
        var result = await client.CreateSubscriptionAsync();

        // Assert
        Assert.True(result);
        mockSession.Verify(s => s.AddSubscription(mockSubscription.Object), Times.Once);
        mockClient.Protected().Verify("CreateSubscriptionOnServerAsync", Times.Once(), mockSubscription.Object);
    }

    [Fact]
    public async Task SubscribeToVariableAsync_WithValidSubscription_AddsItemAndAppliesChanges()
    {
        // Arrange
        var mockClient = new Mock<S7UaMainClient>(_userIdentity, _validateResponse, _mockLoggerFactory.Object) { CallBase = true };
        var client = mockClient.Object;
        var variable = new S7Variable { NodeId = new Opc.Ua.NodeId("ns=2;s=Var1").ToString(), DisplayName = "Var1" };
        var subscription = new Opc.Ua.Client.Subscription(); // Use a real subscription to check item addition
        PrivateFieldHelpers.SetPrivateField(client, "_subscription", subscription);

        mockClient.Protected().Setup<Task>("ApplySubscriptionChangesAsync", subscription).Returns(Task.CompletedTask).Verifiable();

        // Act
        var result = await client.SubscribeToVariableAsync(variable);

        // Assert
        Assert.True(result);
        Assert.Single(subscription.MonitoredItems);
        Assert.Equal(variable.NodeId, subscription.MonitoredItems.First().StartNodeId.ToString());
        mockClient.Protected().Verify("ApplySubscriptionChangesAsync", Times.Once(), subscription);
    }

    [Fact]
    public async Task UnsubscribeFromVariableAsync_WhenSubscribed_RemovesItemAndAppliesChanges()
    {
        // Arrange
        var mockClient = new Mock<S7UaMainClient>(_userIdentity, _validateResponse, _mockLoggerFactory.Object) { CallBase = true };
        var client = mockClient.Object;
        var variable = new S7Variable { NodeId = new Opc.Ua.NodeId("ns=2;s=Var1").ToString() };

        var subscription = new Opc.Ua.Client.Subscription();
        var monitoredItem = new Opc.Ua.Client.MonitoredItem { StartNodeId = variable.NodeId };
        subscription.AddItem(monitoredItem);
        var monitoredItemsDict = (Dictionary<Opc.Ua.NodeId, Opc.Ua.Client.MonitoredItem>)PrivateFieldHelpers.GetPrivateField(client, "_monitoredItems")!;
        monitoredItemsDict[variable.NodeId] = monitoredItem;
        PrivateFieldHelpers.SetPrivateField(client, "_subscription", subscription);

        Assert.Single(subscription.MonitoredItems);

        mockClient.Protected().Setup<Task>("ApplySubscriptionChangesAsync", subscription).Returns(Task.CompletedTask).Verifiable();

        // Act
        var result = await client.UnsubscribeFromVariableAsync(variable);

        // Assert
        Assert.True(result);
        Assert.Empty(subscription.MonitoredItems);
        Assert.Empty(monitoredItemsDict);
        mockClient.Protected().Verify("ApplySubscriptionChangesAsync", Times.Once(), subscription);
    }

    [Fact]
    public void MonitoredItem_Notification_FiresMonitoredItemChangedEvent()
    {
        // Arrange
        var client = CreateSut();
        MonitoredItemChangedEventArgs? receivedArgs = null;
        client.MonitoredItemChanged += (sender, e) => receivedArgs = e;

        var mockItem = new Mock<Opc.Ua.Client.MonitoredItem>();

        var notification = new Opc.Ua.MonitoredItemNotification
        {
            Value = new Opc.Ua.DataValue(new Opc.Ua.Variant(123))
        };

        var eventArgs = (Opc.Ua.Client.MonitoredItemNotificationEventArgs)Activator.CreateInstance(
            typeof(Opc.Ua.Client.MonitoredItemNotificationEventArgs),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
            null, // binder
            [notification], // constructor arguments
            null)!; // culture

        // Act
        PrivateMethodHelpers.InvokePrivateMethod(client, "OnMonitoredItemNotification", [mockItem.Object, eventArgs]);

        // Assert
        Assert.NotNull(receivedArgs);
        Assert.Same(mockItem.Object, receivedArgs.MonitoredItem);
        Assert.Same(notification, receivedArgs.Notification);
        Assert.Equal(123, (int)receivedArgs.Notification.Value.Value!);
    }

    #endregion Subscription Tests
}