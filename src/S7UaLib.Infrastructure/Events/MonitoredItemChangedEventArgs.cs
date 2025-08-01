﻿using Opc.Ua;
using Opc.Ua.Client;

namespace S7UaLib.Infrastructure.Events;

/// <summary>
/// Provides data for the MonitoredItemChanged event from the S7UaClient.
/// </summary>
internal class MonitoredItemChangedEventArgs(MonitoredItem monitoredItem, MonitoredItemNotification notification) : EventArgs
{
    /// <summary>
    /// Gets the MonitoredItem that triggered the notification.
    /// </summary>
    public MonitoredItem MonitoredItem { get; } = monitoredItem;

    /// <summary>
    /// Gets the notification data received from the server.
    /// </summary>
    public MonitoredItemNotification Notification { get; } = notification;
}