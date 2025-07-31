using Microsoft.Extensions.Logging;
using S7UaLib.Core.Events;
using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Infrastructure.Events;
using S7UaLib.Infrastructure.Ua.Converters;
using System.Collections;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// Main OPC UA client responsible for connection management, subscriptions, and events.
/// This client maintains the primary persistent connection and exposes configuration for session pools.
/// </summary>
internal class S7UaMainClient : IS7UaMainClient
{
    #region Private Fields

    private readonly ILogger<S7UaMainClient>? _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private Opc.Ua.Client.SessionReconnectHandler? _reconnectHandler;
    private Opc.Ua.Client.ISession? _session;
    private Opc.Ua.Configuration.ApplicationInstance _appInst;
    private readonly Action<IList, IList> _validateResponse;
    private bool _disposed;
    private readonly SemaphoreSlim _sessionSemaphore = new(1, 1);
    private Opc.Ua.Client.Subscription? _subscription;
    private readonly Dictionary<Opc.Ua.NodeId, Opc.Ua.Client.MonitoredItem> _monitoredItems = [];
    private readonly SemaphoreSlim _subscriptionSemaphore = new(1, 1);

    // Configuration exposure
    private Opc.Ua.ConfiguredEndpoint? _configuredEndpoint;

    #endregion Private Fields

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaMainClient"/> class.
    /// </summary>
    /// <param name="userIdentity">The user identity for authentication.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public S7UaMainClient(UserIdentity? userIdentity = null, ILoggerFactory? loggerFactory = null)
        : this(userIdentity, Opc.Ua.ClientBase.ValidateResponse, loggerFactory)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="S7UaMainClient"/> class.
    /// </summary>
    /// <param name="userIdentity">The user identity for authentication.</param>
    /// <param name="validateResponse">Response validation callback.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    public S7UaMainClient(UserIdentity? userIdentity, Action<IList, IList>? validateResponse, ILoggerFactory? loggerFactory = null)
    {
        UserIdentity = userIdentity ?? new UserIdentity();
        _validateResponse = validateResponse ?? throw new ArgumentNullException(nameof(validateResponse));
        _loggerFactory = loggerFactory;
        _appInst = new Opc.Ua.Configuration.ApplicationInstance
        {
            ApplicationType = Opc.Ua.ApplicationType.Client
        };

        if (_loggerFactory != null)
        {
            _logger = _loggerFactory.CreateLogger<S7UaMainClient>();
        }
    }

    #endregion Constructors

    #region Public Events

    #region Connection Events

    /// <inheritdoc/>
    public event EventHandler<ConnectionEventArgs>? Connecting;

    /// <inheritdoc/>
    public event EventHandler<ConnectionEventArgs>? Connected;

    /// <inheritdoc/>
    public event EventHandler<ConnectionEventArgs>? Disconnecting;

    /// <inheritdoc/>
    public event EventHandler<ConnectionEventArgs>? Disconnected;

    /// <inheritdoc/>
    public event EventHandler<ConnectionEventArgs>? Reconnecting;

    /// <inheritdoc/>
    public event EventHandler<ConnectionEventArgs>? Reconnected;

    #endregion Connection Events

    #region Subscription Events

    /// <inheritdoc/>
    public event EventHandler<MonitoredItemChangedEventArgs>? MonitoredItemChanged;

    #endregion Subscription Events

    #endregion Public Events

    #region Public Properties

    /// <inheritdoc/>
    public ApplicationConfiguration? ApplicationConfiguration { get; private set; }

    /// <inheritdoc/>
    public Opc.Ua.ApplicationConfiguration? OpcApplicationConfiguration => _appInst?.ApplicationConfiguration;

    /// <inheritdoc/>
    public Opc.Ua.ConfiguredEndpoint? ConfiguredEndpoint => _configuredEndpoint;

    /// <inheritdoc/>
    public int KeepAliveInterval { get; set; } = 5000;

    /// <inheritdoc/>
    public int ReconnectPeriod { get; set; } = 1000;

    /// <inheritdoc/>
    public int ReconnectPeriodExponentialBackoff { get; set; } = -1;

    /// <inheritdoc/>
    public UserIdentity UserIdentity { get; }

    /// <inheritdoc/>
    public bool IsConnected => _session?.Connected == true;

    /// <inheritdoc/>
    public bool IsConfigured => ApplicationConfiguration != null && _appInst != null;

    #endregion Public Properties

    #region Public Methods

    #region Configuration Methods

    /// <inheritdoc/>
    public async Task ConfigureAsync(ApplicationConfiguration appConfig)
    {
        ThrowIfDisposed();
        await BuildClientAsync(appConfig);
    }

    /// <inheritdoc/>
    public void SaveConfiguration(string filePath)
    {
        ThrowIfDisposed();
        ThrowIfNotConfigured();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        _appInst?.ApplicationConfiguration.SaveToFile(filePath);
    }

    /// <inheritdoc/>
    public async Task LoadConfigurationAsync(string filePath)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath, nameof(filePath));
        ArgumentNullException.ThrowIfNull(_appInst, nameof(_appInst));

        await _appInst.LoadApplicationConfiguration(filePath, false);

        var appConfig = new ApplicationConfiguration()
        {
            ApplicationName = _appInst.ApplicationConfiguration.ApplicationName,
            ApplicationUri = _appInst.ApplicationConfiguration.ApplicationUri,
            ProductUri = _appInst.ApplicationConfiguration.ProductUri,
            TransportQuotas = new TransportQuotas()
            {
                ChannelLifetime = (uint)_appInst.ApplicationConfiguration.TransportQuotas.ChannelLifetime,
                MaxStringLength = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxStringLength,
                MaxByteStringLength = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxByteStringLength,
                MaxArrayLength = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxArrayLength,
                MaxBufferSize = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxBufferSize,
                MaxDecoderRecoveries = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxDecoderRecoveries,
                MaxEncodingNestingLevels = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxEncodingNestingLevels,
                MaxMessageSize = (uint)_appInst.ApplicationConfiguration.TransportQuotas.MaxMessageSize,
                OperationTimeout = (uint)_appInst.ApplicationConfiguration.TransportQuotas.OperationTimeout,
                SecurityTokenLifetime = (uint)_appInst.ApplicationConfiguration.TransportQuotas.SecurityTokenLifetime
            },
            ClientConfiguration = new ClientConfiguration()
            {
                WellKnownDiscoveryUrls = [.. _appInst.ApplicationConfiguration.ClientConfiguration.WellKnownDiscoveryUrls],
                SessionTimeout = (uint)_appInst.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout,
                MinSubscriptionLifetime = (uint)_appInst.ApplicationConfiguration.ClientConfiguration.MinSubscriptionLifetime,
                OperationLimits = new OperationLimits()
                {
                    MaxMonitoredItemsPerCall = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxMonitoredItemsPerCall,
                    MaxNodesPerBrowse = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerBrowse,
                    MaxNodesPerHistoryReadData = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerHistoryReadData,
                    MaxNodesPerHistoryReadEvents = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerHistoryReadEvents,
                    MaxNodesPerHistoryUpdateData = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerHistoryUpdateData,
                    MaxNodesPerHistoryUpdateEvents = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerHistoryUpdateEvents,
                    MaxNodesPerMethodCall = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerMethodCall,
                    MaxNodesPerNodeManagement = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerNodeManagement,
                    MaxNodesPerRead = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerRead,
                    MaxNodesPerRegisterNodes = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerRegisterNodes,
                    MaxNodesPerTranslateBrowsePathsToNodeIds = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds,
                    MaxNodesPerWrite = _appInst.ApplicationConfiguration.ClientConfiguration.OperationLimits.MaxNodesPerWrite
                }
            },
            SecurityConfiguration = new SecurityConfiguration(
                new SecurityConfigurationStores(
                    _appInst.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificate.SubjectName,
                    _appInst.ApplicationConfiguration.SecurityConfiguration.TrustedPeerCertificates.StorePath,
                    _appInst.ApplicationConfiguration.SecurityConfiguration.ApplicationCertificates.StorePath,
                    _appInst.ApplicationConfiguration.SecurityConfiguration.TrustedIssuerCertificates.StorePath,
                    _appInst.ApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.StorePath))
            {
                SendCertificateChain = _appInst.ApplicationConfiguration.SecurityConfiguration.SendCertificateChain,
                AddAppCertToTrustedStore = _appInst.ApplicationConfiguration.SecurityConfiguration.AddAppCertToTrustedStore,
                AutoAcceptUntrustedCertificates = _appInst.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates,
                MaxRejectedCertificates = (uint)_appInst.ApplicationConfiguration.SecurityConfiguration.MaxRejectedCertificates,
                MinCertificateKeySize = _appInst.ApplicationConfiguration.SecurityConfiguration.MinimumCertificateKeySize,
                RejectSHA1SignedCertificates = new SHA1Validation()
                {
                    Reject = _appInst.ApplicationConfiguration.Extensions
                    .Find(x => x.Name == "RejectSHA1SignedCertificates") is XmlElement xmlElement && xmlElement.FirstChild is XmlNode rejectNode && bool.TryParse(rejectNode.InnerText, out var rejectSHA1SignedCertificates) && rejectSHA1SignedCertificates
                },
                RejectUnknownRevocationStatus = _appInst.ApplicationConfiguration.SecurityConfiguration.RejectUnknownRevocationStatus,
                SuppressNonceValidationErrors = _appInst.ApplicationConfiguration.SecurityConfiguration.SuppressNonceValidationErrors,
                UseValidatedCertificates = _appInst.ApplicationConfiguration.SecurityConfiguration.UseValidatedCertificates,
                SkipDomainValidation = new DomainValidation()
                {
                    Skip = _appInst.ApplicationConfiguration.Extensions
                    .Find(x => x.Name == "SkipDomainValidation") is XmlElement xmlElement2 && xmlElement2.FirstChild is XmlNode skipNode && bool.TryParse(skipNode.InnerText, out var skipDomainValidation) && skipDomainValidation
                }
            }
        };

        _appInst.ApplicationConfiguration.SecurityConfiguration.RejectSHA1SignedCertificates = appConfig.SecurityConfiguration.RejectSHA1SignedCertificates.Reject;
        await _appInst.ApplicationConfiguration.CertificateValidator.Update(_appInst.ApplicationConfiguration);
        ApplicationConfiguration = appConfig;
        _appInst.ApplicationConfiguration.CertificateValidator.CertificateValidation += Client_CertificateValidation;
    }

    /// <inheritdoc/>
    public async Task AddTrustedCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConfigured();

        await _appInst!.AddOwnCertificateToTrustedStoreAsync(certificate, cancellationToken);
        _appInst.ApplicationConfiguration.SecurityConfiguration.AddTrustedPeer(certificate.GetRawCertData());
        await _appInst.ApplicationConfiguration.CertificateValidator.UpdateAsync(_appInst.ApplicationConfiguration.SecurityConfiguration);
    }

    #endregion Configuration Methods

    #region Connection Methods

    /// <inheritdoc/>
    public async Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ThrowIfNotConfigured();
        ArgumentException.ThrowIfNullOrEmpty(serverUrl, nameof(serverUrl));

        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_session?.Connected == true)
            {
                _logger?.LogWarning("Already connected to the server.");
                return;
            }

            OnConnecting(ConnectionEventArgs.Empty);

            var endpointDescription = Opc.Ua.Client.CoreClientUtils.SelectEndpoint(_appInst!.ApplicationConfiguration, serverUrl, useSecurity);

            // Use the exact same certificate validation logic as the original S7UaClient
            var serverCert = endpointDescription.ServerCertificate;
            var serverCertId = new Opc.Ua.CertificateIdentifier(serverCert);
            await _appInst.ApplicationConfiguration.CertificateValidator.ValidateAsync(serverCertId.Certificate, cancellationToken).ConfigureAwait(false);

            var endpointConfig = Opc.Ua.EndpointConfiguration.Create(_appInst!.ApplicationConfiguration);
            var endpoint = new Opc.Ua.ConfiguredEndpoint(null, endpointDescription, endpointConfig);
            _configuredEndpoint = endpoint; // Store for session pool use

            // Use the exact same domain validation logic as the original S7UaClient
            if (_appInst.ApplicationConfiguration.Extensions.Find(x => x.Name == "DomainValidation") is XmlElement xmlElement)
            {
                if (xmlElement.FirstChild is XmlNode skipNode)
                {
                    if (bool.TryParse(skipNode.InnerText, out var skipDomainValidation))
                    {
                        if (!skipDomainValidation)
                        {
                            try
                            {
                                _appInst.ApplicationConfiguration.CertificateValidator.ValidateDomains(serverCertId.Certificate, endpoint);
                            }
                            catch (Opc.Ua.ServiceResultException ex)
                            {
                                if (ex.StatusCode == Opc.Ua.StatusCodes.BadCertificateHostNameInvalid)
                                {
                                    _logger?.LogError("Bad certificate, host name invalid.");
                                    throw;
                                }
                            }
                        }
                    }
                }
            }

            var identity = UserIdentity.Username == null && UserIdentity.Password == null
                ? new Opc.Ua.UserIdentity()
                : new Opc.Ua.UserIdentity(UserIdentity.Username, UserIdentity.Password);

            var sessionFactory = Opc.Ua.Client.TraceableSessionFactory.Instance;
            var session = await sessionFactory.CreateAsync(
                _appInst!.ApplicationConfiguration,
                endpoint,
                true,
                false,
                _appInst!.ApplicationConfiguration.ApplicationName,
                (uint)_appInst!.ApplicationConfiguration.ClientConfiguration.DefaultSessionTimeout,
                identity,
                null,
                cancellationToken
            ).ConfigureAwait(false);

            if (session?.Connected != true)
            {
                _logger?.LogError("Failed to connect to the S7 UA server.");
                return;
            }

            _session = session;

            _session.KeepAliveInterval = KeepAliveInterval;
            _session.DeleteSubscriptionsOnClose = false;
            _session.TransferSubscriptionsOnReconnect = true;

            _session.KeepAlive += Session_KeepAlive;

            _reconnectHandler = new Opc.Ua.Client.SessionReconnectHandler(true, ReconnectPeriodExponentialBackoff);

            OnConnected(ConnectionEventArgs.Empty);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _sessionSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            DisconnectCore(leaveChannelOpen);
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    #endregion Connection Methods

    #region Subscription Methods

    /// <inheritdoc/>
    public async Task<bool> CreateSubscriptionAsync(int publishingInterval = 100)
    {
        ThrowIfDisposed();
        if (!IsConnected || _session is null)
        {
            _logger?.LogError("Cannot create subscription; session is not connected.");
            return false;
        }

        await _subscriptionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_subscription != null)
            {
                return true;
            }

            _subscription = CreateNewSubscription(publishingInterval);

            _session.AddSubscription(_subscription);
            await CreateSubscriptionOnServerAsync(_subscription).ConfigureAwait(false);

            _logger?.LogInformation("Subscription created successfully with PublishingInterval={interval}ms.", publishingInterval);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create subscription.");
            return false;
        }
        finally
        {
            _subscriptionSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> SubscribeToVariableAsync(IS7Variable variable)
    {
        ThrowIfDisposed();
        if (_subscription is null)
        {
            _logger?.LogError("Cannot subscribe variable '{name}'. Subscription does not exist.", variable.DisplayName);
            return false;
        }
        if (variable.NodeId is null)
        {
            _logger?.LogWarning("Cannot subscribe variable '{name}'. NodeId is null.", variable.DisplayName);
            return false;
        }

        await _subscriptionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_monitoredItems.ContainsKey(variable.NodeId))
            {
                _logger?.LogInformation("Variable '{name}' is already subscribed.", variable.DisplayName);
                return true;
            }

            var item = new Opc.Ua.Client.MonitoredItem(_subscription.DefaultItem)
            {
                DisplayName = variable.DisplayName,
                StartNodeId = variable.NodeId,
                AttributeId = Opc.Ua.Attributes.Value,
                SamplingInterval = (int)variable.SamplingInterval,
                QueueSize = 1,
                DiscardOldest = true,
                MonitoringMode = Opc.Ua.MonitoringMode.Reporting
            };

            item.Notification += OnMonitoredItemNotification;

            _monitoredItems.Add(variable.NodeId, item);
            _subscription.AddItem(item);
            await ApplySubscriptionChangesAsync(_subscription).ConfigureAwait(false);

            _logger?.LogDebug("Variable '{name}' ({nodeId}) subscribed.", variable.DisplayName, variable.NodeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to subscribe to variable '{name}'.", variable.DisplayName);
            if (variable.NodeId is not null) _monitoredItems.Remove(variable.NodeId);
            return false;
        }
        finally
        {
            _subscriptionSemaphore.Release();
        }
    }

    /// <inheritdoc/>
    public async Task<bool> UnsubscribeFromVariableAsync(IS7Variable variable)
    {
        ThrowIfDisposed();
        if (_subscription is null || variable.NodeId is null || !_monitoredItems.TryGetValue(variable.NodeId, out var item))
        {
            _logger?.LogWarning("Cannot unsubscribe variable '{name}'. It is not currently subscribed.", variable.DisplayName);
            return false;
        }

        await _subscriptionSemaphore.WaitAsync().ConfigureAwait(false);
        try
        {
            item.Notification -= OnMonitoredItemNotification;

            _subscription.RemoveItem(item);
            _monitoredItems.Remove(variable.NodeId);
            await ApplySubscriptionChangesAsync(_subscription).ConfigureAwait(false);

            _logger?.LogDebug("Variable '{name}' ({nodeId}) unsubscribed.", variable.DisplayName, variable.NodeId);
            return true;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to unsubscribe from variable '{name}'.", variable.DisplayName);
            return false;
        }
        finally
        {
            _subscriptionSemaphore.Release();
        }
    }

    #endregion Subscription Methods

    #endregion Public Methods

    #region Private Methods

    internal async Task BuildClientAsync(ApplicationConfiguration appConfig)
    {
        _appInst = new()
        {
            ApplicationName = appConfig.ApplicationName,
            ApplicationType = Opc.Ua.ApplicationType.Client
        };

        var build = _appInst.Build(appConfig.ApplicationUri, appConfig.ProductUri);

        build.SetChannelLifetime((int)appConfig.TransportQuotas.ChannelLifetime)
                .SetMaxStringLength((int)appConfig.TransportQuotas.MaxStringLength)
                .SetMaxByteStringLength((int)appConfig.TransportQuotas.MaxByteStringLength)
                .SetMaxArrayLength((int)appConfig.TransportQuotas.MaxArrayLength)
                .SetMaxBufferSize((int)appConfig.TransportQuotas.MaxBufferSize)
                .SetMaxDecoderRecoveries((int)appConfig.TransportQuotas.MaxDecoderRecoveries)
                .SetMaxEncodingNestingLevels((int)appConfig.TransportQuotas.MaxEncodingNestingLevels)
                .SetMaxMessageSize((int)appConfig.TransportQuotas.MaxMessageSize)
                .SetOperationTimeout((int)appConfig.TransportQuotas.OperationTimeout)
                .SetSecurityTokenLifetime((int)appConfig.TransportQuotas.SecurityTokenLifetime);

        var clientBuild = build.AsClient();

        foreach (var uri in appConfig.ClientConfiguration.WellKnownDiscoveryUrls)
        {
            clientBuild.AddWellKnownDiscoveryUrls(uri);
        }

        clientBuild.SetClientOperationLimits(
                new Opc.Ua.OperationLimits()
                {
                    MaxMonitoredItemsPerCall = appConfig.ClientConfiguration.OperationLimits.MaxMonitoredItemsPerCall,
                    MaxNodesPerBrowse = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerBrowse,
                    MaxNodesPerHistoryReadData = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerHistoryReadData,
                    MaxNodesPerHistoryReadEvents = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerHistoryReadEvents,
                    MaxNodesPerHistoryUpdateData = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerHistoryUpdateData,
                    MaxNodesPerHistoryUpdateEvents = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerHistoryUpdateEvents,
                    MaxNodesPerMethodCall = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerMethodCall,
                    MaxNodesPerNodeManagement = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerNodeManagement,
                    MaxNodesPerRead = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerRead,
                    MaxNodesPerRegisterNodes = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerRegisterNodes,
                    MaxNodesPerTranslateBrowsePathsToNodeIds = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerTranslateBrowsePathsToNodeIds,
                    MaxNodesPerWrite = appConfig.ClientConfiguration.OperationLimits.MaxNodesPerWrite
                });

        var finalBuild = clientBuild.SetDefaultSessionTimeout((int)appConfig.ClientConfiguration.SessionTimeout)
            .SetMinSubscriptionLifetime((int)appConfig.ClientConfiguration.MinSubscriptionLifetime)
        .AddSecurityConfigurationStores(
            appConfig.SecurityConfiguration.SecurityConfigurationStores.SubjectName,
            appConfig.SecurityConfiguration.SecurityConfigurationStores.AppRoot,
            appConfig.SecurityConfiguration.SecurityConfigurationStores.TrustedRoot,
            appConfig.SecurityConfiguration.SecurityConfigurationStores.IssuerRoot,
            appConfig.SecurityConfiguration.SecurityConfigurationStores.RejectedRoot)
        .SetSendCertificateChain(appConfig.SecurityConfiguration.SendCertificateChain)
        .SetAddAppCertToTrustedStore(appConfig.SecurityConfiguration.AddAppCertToTrustedStore)
        .SetAutoAcceptUntrustedCertificates(appConfig.SecurityConfiguration.AutoAcceptUntrustedCertificates)
        .SetMaxRejectedCertificates((int)appConfig.SecurityConfiguration.MaxRejectedCertificates)
        .SetMinimumCertificateKeySize((ushort)appConfig.SecurityConfiguration.MinCertificateKeySize)
        .SetRejectSHA1SignedCertificates(appConfig.SecurityConfiguration.RejectSHA1SignedCertificates.Reject)
        .SetRejectUnknownRevocationStatus(appConfig.SecurityConfiguration.RejectUnknownRevocationStatus)
        .SetSuppressNonceValidationErrors(appConfig.SecurityConfiguration.SuppressNonceValidationErrors)
        .SetUseValidatedCertificates(appConfig.SecurityConfiguration.UseValidatedCertificates)
        .AddExtension<DomainValidation>(new XmlQualifiedName("SkipDomainValidation"), appConfig.SecurityConfiguration.SkipDomainValidation)
        .AddExtension<SHA1Validation>(new XmlQualifiedName("RejectSHA1SignedCertificates"), appConfig.SecurityConfiguration.RejectSHA1SignedCertificates);

        await finalBuild.Create();

        if (!await _appInst.CheckApplicationInstanceCertificates(false).ConfigureAwait(false))
        {
            throw new SystemException("Application instance certificate invalid!");
        }

        ApplicationConfiguration = appConfig;
        _appInst.ApplicationConfiguration.CertificateValidator.CertificateValidation += Client_CertificateValidation;
    }

    private void DisconnectCore(bool leaveChannelOpen = false)
    {
        if (_session != null)
        {
            OnDisconnecting(ConnectionEventArgs.Empty);

            if (_subscription != null)
            {
                _session.RemoveSubscription(_subscription);
                _subscription.Dispose();
                _subscription = null;
                _monitoredItems.Clear();
            }

            _session.KeepAlive -= Session_KeepAlive;
            _reconnectHandler?.Dispose();
            _reconnectHandler = null;

            _session.Close(!leaveChannelOpen);

            if (leaveChannelOpen)
            {
                _session.DetachChannel();
            }

            _session.Dispose();
            _session = null;

            OnDisconnected(ConnectionEventArgs.Empty);
        }
    }

    #region Subscription Helpers

    protected virtual Task CreateSubscriptionOnServerAsync(Opc.Ua.Client.Subscription subscription)
    {
        return subscription.CreateAsync();
    }

    protected virtual Opc.Ua.Client.Subscription CreateNewSubscription(int publishingInterval)
    {
        return _session is null
            ? throw new InvalidOperationException("Session is not available to create a subscription.")
            : new Opc.Ua.Client.Subscription(_session.DefaultSubscription)
            {
                PublishingInterval = publishingInterval,
                LifetimeCount = 600,
                MaxNotificationsPerPublish = 1000,
                TimestampsToReturn = Opc.Ua.TimestampsToReturn.Both
            };
    }

    protected virtual Task ApplySubscriptionChangesAsync(Opc.Ua.Client.Subscription subscription)
    {
        return subscription.ApplyChangesAsync();
    }

    #endregion Subscription Helpers

    #region Event Callbacks - Copied exactly from original S7UaClient

    private void Session_KeepAlive(Opc.Ua.Client.ISession session, Opc.Ua.Client.KeepAliveEventArgs e)
    {
        if (_session?.Equals(session) != true)
        {
            return;
        }

        if (Opc.Ua.ServiceResult.IsBad(e.Status))
        {
            if (ReconnectPeriod <= 0)
            {
                return;
            }

            OnReconnecting(new ConnectionEventArgs(UaStatusCodeConverter.Convert(e.Status.StatusCode)));

            var state = _reconnectHandler?.BeginReconnect(_session, ReconnectPeriod, Client_ReconnectComplete);

            switch (state)
            {
                case Opc.Ua.Client.SessionReconnectHandler.ReconnectState.Triggered:
                    _logger?.LogInformation("Reconnection triggered.");
                    break;

                case Opc.Ua.Client.SessionReconnectHandler.ReconnectState.Ready:
                    _logger?.LogWarning("Reconnection handler is in 'Ready' state after BeginReconnect attempt. This might indicate automatic reconnection could not be triggered.");
                    break;

                case Opc.Ua.Client.SessionReconnectHandler.ReconnectState.Reconnecting:
                    _logger?.LogWarning("Reconnection in progress...");
                    break;
            }

            e.CancelKeepAlive = true;
        }
    }

    private void Client_ReconnectComplete(object? sender, EventArgs e)
    {
        if (!Object.ReferenceEquals(sender, _reconnectHandler))
        {
            return;
        }

        _sessionSemaphore.Wait();
        try
        {
            if (_reconnectHandler?.Session != null)
            {
                if (!Object.ReferenceEquals(_session, _reconnectHandler.Session))
                {
                    _logger?.LogInformation("Reconnected to S7 UA server with a new session.");
                    var oldSession = _session;
                    _session = _reconnectHandler.Session;
                    Opc.Ua.Utils.SilentDispose(oldSession);
                }
                else
                {
                    _logger?.LogInformation("Reconnected to S7 UA server with the same session.");
                }
            }
            else
            {
                _logger?.LogInformation("KeepAlive recovered - Reconnection stopped.");
            }

            OnReconnected(new ConnectionEventArgs());
        }
        finally
        {
            _sessionSemaphore.Release();
        }
    }

    private void Client_CertificateValidation(Opc.Ua.CertificateValidator certificateValidator, Opc.Ua.CertificateValidationEventArgs e)
    {
        var accepted = false;

        Opc.Ua.ServiceResult error = e.Error;
        _logger?.LogWarning("Certificate validation error: {error}", error.ToLongString());

        if (error.StatusCode == Opc.Ua.StatusCodes.BadCertificateUntrusted)
        {
            if (_appInst!.ApplicationConfiguration.SecurityConfiguration.AutoAcceptUntrustedCertificates)
            {
                accepted = true;
            }
        }

        if (accepted)
        {
            _logger?.LogWarning("Accepting untrusted certificate. Subject: {subject}", e.Certificate.Subject);
            e.Accept = true;
        }
        else
        {
            _logger?.LogWarning("Rejecting untrusted certificate. Subject: {subject}", e.Certificate.Subject);
            e.Accept = false;
            _appInst!.ApplicationConfiguration.SecurityConfiguration.RejectedCertificateStore.OpenStore().Add(e.Certificate);
        }
    }

    #endregion Event Callbacks - Copied exactly from original S7UaClient

    #region Event Dispatchers

    private void OnConnecting(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Connecting to S7 UA server...");
        Connecting?.Invoke(this, e);
    }

    private void OnConnected(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Connected to S7 UA server successfully.");
        Connected?.Invoke(this, e);
    }

    private void OnDisconnecting(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Disconnecting from S7 UA server...");
        Disconnecting?.Invoke(this, e);
    }

    private void OnDisconnected(ConnectionEventArgs e)
    {
        _logger?.LogInformation("Disconnected from S7 UA server.");
        Disconnected?.Invoke(this, e);
    }

    private void OnReconnecting(ConnectionEventArgs e)
    {
        Reconnecting?.Invoke(this, e);
    }

    private void OnReconnected(ConnectionEventArgs e)
    {
        Reconnected?.Invoke(this, e);
    }

    private void OnMonitoredItemNotification(Opc.Ua.Client.MonitoredItem monitoredItem, Opc.Ua.Client.MonitoredItemNotificationEventArgs e)
    {
        if (e.NotificationValue is Opc.Ua.MonitoredItemNotification notification)
        {
            MonitoredItemChanged?.Invoke(this, new MonitoredItemChangedEventArgs(monitoredItem, notification));
        }
    }

    #endregion Event Dispatchers

    #region Helpers

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void ThrowIfNotConfigured()
    {
        ArgumentNullException.ThrowIfNull(_appInst);
        ArgumentNullException.ThrowIfNull(_appInst.ApplicationConfiguration);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(_appInst.ApplicationConfiguration.ApplicationName);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(_appInst.ApplicationConfiguration.ApplicationUri);
        ArgumentNullException.ThrowIfNullOrWhiteSpace(_appInst.ApplicationConfiguration.ProductUri);
    }

    #endregion Helpers

    #endregion Private Methods

    #region Dispose

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (_session != null)
            {
                if (_session.Connected)
                    DisconnectCore();
                _session.Dispose();
            }

            if (_appInst?.ApplicationConfiguration != null)
            {
                _appInst.ApplicationConfiguration.CertificateValidator.CertificateValidation -= Client_CertificateValidation;
            }

            _sessionSemaphore.Dispose();
            _subscriptionSemaphore.Dispose();

            _disposed = true;
        }
    }

    ~S7UaMainClient()
    {
        Dispose(false);
    }

    #endregion Dispose
}