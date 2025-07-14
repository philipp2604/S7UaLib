# S7UaLib API Description

## Overview

S7UaLib is a .NET library designed to simplify communication with Siemens S7 PLCs (like S7-1200/1500) via their integrated OPC UA server. It abstracts away the low-level complexities of the OPC UA protocol and the specifics of the S7 address space, providing a high-level, intuitive, and task-oriented API.

The primary goal of this library is to allow developers to interact with the PLC's data structure in a way that feels natural in C#, using symbolic tag names and strongly-typed objects, rather than dealing with raw NodeIDs and manual data conversions.

The main entry point for all interactions is the `S7Service` class, located in the `S7UaLib.Services.S7` namespace.

## Key Features

- **High-Level Service (`S7Service`):** A single, easy-to-use service class that manages the connection, data storage, and communication.
- **Automatic Structure Discovery:** The library can browse the entire OPC UA address space of the S7 PLC and build a complete, hierarchical in-memory model of all available data, including:
  - Global Data Blocks (DBs)
  - Instance Data Blocks (for FBs)
  - Inputs (I), Outputs (Q), and Memory (M) areas
  - Timers (T) and Counters (C)
- **Seamless S7 Data Type Conversion:** Automatically handles the conversion between complex S7-specific data types and standard .NET types. This is a core feature that saves significant development time. Supported types include:
  - `DATE_AND_TIME` (8-byte BCD) <-> `.NET DateTime`
  - `DTL` (12-byte struct) <-> `.NET DateTime` (with nanosecond precision)
  - `S5TIME` (legacy timer format) <-> `.NET TimeSpan`
  - `TIME`, `LTIME`, `TIME_OF_DAY`, `LTIME_OF_DAY` <-> `.NET TimeSpan`
  - `DATE` <-> `.NET DateTime`
  - `COUNTER` (BCD format) <-> `.NET ushort`
  - And all primitive types (`BOOL`, `INT`, `REAL`, `STRING`, etc.) and their arrays.
- **Data Persistence:** The discovered PLC structure can be saved to a JSON file and loaded back later. This eliminates the need for a time-consuming discovery process on every application startup.
- **Data Access by Path:** Read and write variable values using their full symbolic path (e.g., `"DataBlocksGlobal.SettingsDB.MotorSpeed"`), without needing to know the underlying OPC UA `NodeId`.
- **Data Subscriptions:** Subscribe to individual variables to receive notifications when their values change on the PLC. The library manages the underlying `MonitoredItem`s and raises a simple `VariableValueChanged` event.
- **Dynamic Type Correction:** The data type (`S7DataType`) of a variable can be changed at runtime if the initial discovery was ambiguous (e.g., a `WORD` that should be treated as an `S5TIME`).

## Core Concepts

### Architecture

The library is built on a clean, three-layer architecture to separate concerns:

-   **`S7UaLib.Core`**: The foundation. It defines all shared data models, interfaces, and enumerations (like `IS7Variable`, `S7DataType`, `ApplicationConfiguration`). It is the common "language" of the ecosystem.
-   **`S7UaLib.Infrastructure`**: The engine. It contains the concrete implementations for OPC UA communication (`S7UaClient`), data type conversion, and persistence logic. These are the internal mechanics.
-   **`S7UaLib` (the main package)**: The public API. It provides the high-level `S7Service`, which orchestrates the underlying components into a simple and powerful interface for you to use.

As a user, you only need to interact with the `S7Service` from the main `S7UaLib` package.

### S7 PLC Structure Model

The library represents the PLC's memory as a hierarchical collection of C# record objects defined in `S7UaLib.Core`:

- `IS7DataBlockGlobal`: Represents a global DB. Contains a list of `IS7Variable`.
- `IS7DataBlockInstance`: Represents an instance DB. It is structured into sections like `Inputs`, `Outputs`, and `Static`, which in turn contain variables or even nested instances.
- `IS7Inputs`, `IS7Outputs`, `IS7Memory`, etc.: Represent the corresponding memory areas.
- `IS7Variable`: Represents a single tag or a member of a struct. It holds all relevant metadata, including its `NodeId`, `DisplayName`, `S7Type`, current `Value`, `StatusCode`, and any struct members if it is a `STRUCT`.

### Variable Paths

Once the structure is discovered or loaded, every variable can be accessed by a unique, human-readable string path. The path is constructed from the top-level folder down to the variable name, separated by dots.

**Examples:**
- A variable in a global DB: `"DataBlocksGlobal.MyDatabase.MyTag"`
- A variable in an instance DB: `"DataBlocksInstance.Motor_1_DB.Static.Speed"`
- A memory flag: `"Memory.MyFlag"`
- A nested struct member: `"DataBlocksGlobal.MyUdtDb.MyStructVar.Member1"`

## Getting Started: Basic Usage

Here is a complete example demonstrating the most common workflow.

```csharp
using S7UaLib.Core.Ua;
using S7UaLib.Services.S7;
using System;
using System.IO;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 1. Create the ApplicationConfiguration using the simplified model
        var appConfig = new ApplicationConfiguration
        {
            ApplicationName = "My S7 UA Client",
            ApplicationUri = "urn:localhost:MyS7UaClient", // Must be unique
            ProductUri = "uri:mycompany:mys7uaclient",
            // For testing, accept all certificates. In production, this should be false
            // and you should manage the certificate trust list.
            AutoAcceptUntrustedCertificates = true
        };

        // 2. Instantiate the S7Service
        var service = new S7Service(appConfig);

        var serverUrl = "opc.tcp://192.168.0.1:4840";
        var structureFilePath = "plc_structure.json";

        try
        {
            // 3. Connect to the PLC
            Console.WriteLine($"Connecting to {serverUrl}...");
            await service.ConnectAsync(serverUrl, useSecurity: false);
            Console.WriteLine("Connected successfully!");

            // 4. Discover structure (or load from file if it exists)
            if (File.Exists(structureFilePath))
            {
                Console.WriteLine("Loading structure from file...");
                await service.LoadStructureAsync(structureFilePath);
            }
            else
            {
                Console.WriteLine("Discovering PLC structure...");
                await service.DiscoverStructureAsync();
                Console.WriteLine("Discovery complete. Saving structure to file...");
                await service.SaveStructureAsync(structureFilePath);
            }

            // 5. Read a variable by its full path
            var speedVarPath = "DataBlocksGlobal.MachineData.Speed";
            var speedVariable = service.GetVariable(speedVarPath);

            if (speedVariable != null)
            {
                Console.WriteLine($"Initial value of '{speedVariable.DisplayName}': {speedVariable.Value} (Status: {speedVariable.StatusCode})");
            }

            // Refresh all values from the PLC
            await service.ReadAllVariablesAsync();

            speedVariable = service.GetVariable(speedVarPath);
            if (speedVariable != null)
            {
                Console.WriteLine($"Refreshed value of '{speedVariable.DisplayName}': {speedVariable.Value}");
            }

            // 6. Write a new value to a variable
            var setpointVarPath = "DataBlocksGlobal.MachineData.Setpoint";
            Console.WriteLine($"Writing value 123.45 to '{setpointVarPath}'...");
            bool writeSuccess = await service.WriteVariableAsync(setpointVarPath, 123.45f);
            Console.WriteLine(writeSuccess ? "Write successful!" : "Write failed.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"An error occurred: {ex.Message}");
        }
        finally
        {
            // 7. Disconnect when done
            if (service.IsConnected)
            {
                Console.WriteLine("Disconnecting...");
                await service.DisconnectAsync();
            }
        }
    }
}
```

## Advanced Usage

### Subscribing to Variable Changes

S7UaLib makes it easy to monitor tags for changes.

```csharp
using S7UaLib.Core.Events;
// ...assuming 'service' is a connected S7Service instance

// Subscribe to the VariableValueChanged event
service.VariableValueChanged += (object? sender, VariableValueChangedEventArgs args) =>
{
    // This event may be raised on a background thread!
    // Ensure your handler is thread-safe.
    Console.WriteLine(
        $"[CHANGE] Path: {args.NewVariable.FullPath}\n" +
        $"         Old Value: {args.OldVariable.Value}\n" +
        $"         New Value: {args.NewVariable.Value}"
    );
};

// Subscribe to a specific variable with a 500ms sampling interval
var tagToWatch = "DataBlocksGlobal.MachineData.StatusWord";
bool subscribed = await service.SubscribeToVariableAsync(tagToWatch, samplingInterval: 500);

if (subscribed)
{
    Console.WriteLine($"Successfully subscribed to '{tagToWatch}'. Waiting for changes...");
}

// To stop receiving updates:
// await service.UnsubscribeFromVariableAsync(tagToWatch);
```

### Correcting Data Types

Sometimes, a PLC tag might be represented as a generic type like `WORD` but actually contains a specific data format like `S5TIME`. You can correct this in the data store.

```csharp
using S7UaLib.Core.Enums;
// ...assuming 'service' is a connected S7Service instance

var timerPath = "DataBlocksGlobal.Timers.T1_Preset";

// Let's say discovery identified it as WORD
var timerVar = service.GetVariable(timerPath);
Console.WriteLine($"Type before update: {timerVar.S7Type}, Value: {timerVar.Value}");
// Output might be: Type before update: WORD, Value: 8212

// Update the type to S5TIME using the enum from S7UaLib.Core
await service.UpdateVariableTypeAsync(timerPath, S7DataType.S5TIME);

// The service automatically reconverts the raw value
timerVar = service.GetVariable(timerPath);
Console.WriteLine($"Type after update: {timerVar.S7Type}, Value: {timerVar.Value}");
// Output should be: Type after update: S5TIME, Value: 00:00:12.3400000 (as a TimeSpan)
```

## `IS7Service` API Reference

This section provides a complete reference for the public `IS7Service` interface. The event arguments and data models (`ConnectionEventArgs`, `IS7Variable`, `S7DataType`, etc.) are defined in the `S7UaLib.Core` library.

### Events

- `event EventHandler<ConnectionEventArgs>? Connecting`
  > Occurs when a connection attempt to the server is initiated.
- `event EventHandler<ConnectionEventArgs>? Connected`
  > Occurs when a connection to the server has been successfully established.
- `event EventHandler<ConnectionEventArgs>? Disconnecting`
  > Occurs when a disconnection from the server is initiated.
- `event EventHandler<ConnectionEventArgs>? Disconnected`
  > Occurs when the client has been disconnected from the server.
- `event EventHandler<ConnectionEventArgs>? Reconnecting`
  > Occurs when the client is attempting to reconnect to the server after a connection loss.
- `event EventHandler<ConnectionEventArgs>? Reconnected`
  > Occurs when the client has successfully reconnected to the server.
- `event EventHandler<VariableValueChangedEventArgs>? VariableValueChanged`
  > Occurs when a variable's value changes after a read operation or subscription update.
  > **IMPORTANT:** This event may be raised on a background thread. Subscribers must ensure their event handling logic is thread-safe.

### Properties

- `bool IsConnected { get; }`
  > Gets a value indicating whether the connection is currently active and valid.
- `int KeepAliveInterval { get; set; }`
  > Gets or sets the interval, in milliseconds, at which keep-alive messages are sent to maintain a connection.
- `int ReconnectPeriod { get; set; }`
  > Gets or sets the time interval, in milliseconds, between automatic reconnection attempts. A value of -1 disables automatic reconnection.
- `int ReconnectPeriodExponentialBackoff { get; set; }`
  > Gets or sets the maximum reconnect period for exponential backoff, in milliseconds. A value of -1 disables exponential backoff.
- `uint SessionTimeout { get; set; }`
  > Gets or sets the session timeout in milliseconds after which the session is considered invalid after the last communication.
- `bool AcceptUntrustedCertificates { get; set; }`
  > Gets or sets a value indicating whether untrusted SSL/TLS certificates are accepted. This is now managed via the `ApplicationConfiguration` object passed to the constructor.
- `UserIdentity UserIdentity { get; set; }`
  > Gets or sets the identity information of the user for authentication (e.g., username/password).

### Methods

#### Connection Methods

- `Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)`
  > Asynchronously connects to the specified S7 UA server endpoint.
- `Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default)`
  > Disconnects from the S7 UA server.

#### Structure Discovery Methods

- `Task DiscoverStructureAsync(CancellationToken cancellationToken = default)`
  > Discovers the entire structure of the OPC UA server and populates the internal data store. Throws an `InvalidOperationException` if not connected.

#### Variables Access and Manipulation Methods

- `Task ReadAllVariablesAsync(CancellationToken cancellationToken = default)`
  > Reads the values of all discovered variables from the PLC. Raises the `VariableValueChanged` event for any variable whose value has changed.
- `Task<bool> WriteVariableAsync(string fullPath, object value)`
  > Writes a value to a variable specified by its full symbolic path. The library handles the necessary type conversion. Returns `true` on success.
- `Task<bool> UpdateVariableTypeAsync(string fullPath, S7DataType newType, CancellationToken cancellationToken = default)`
  > Updates the S7 data type of a variable in the data store and attempts to reconvert its current raw value. Returns `true` if the variable was found and updated.
- `IS7Variable? GetVariable(string fullPath)`
  > Retrieves a variable from the data store by its full symbolic path. Returns the `IS7Variable` if found; otherwise, `null`.

#### Subscription Methods

- `Task<bool> SubscribeToVariableAsync(string fullPath, uint samplingInterval = 500, CancellationToken cancellationToken = default)`
  > Subscribes to a variable to receive value changes from the server. Will create the main subscription on the first call. Returns `true` on success.
- `Task<bool> SubscribeToAllConfiguredVariablesAsync(CancellationToken cancellationToken = default)`
  > Subscribes to all variables that have the `IsSubscribed` flag set to `true` (typically from a loaded structure file). Returns `true` if all subscriptions were successful.
- `Task<bool> UnsubscribeFromVariableAsync(string fullPath, CancellationToken cancellationToken = default)`
  > Unsubscribes from a variable to stop receiving value changes. Returns `true` on success.

#### Persistence Methods

- `Task SaveStructureAsync(string filePath)`
  > Saves the current entire PLC structure from the data store to a JSON file. This includes all discovered elements and their assigned data types.
- `Task LoadStructureAsync(string filePath)`
  > Loads the PLC structure from a JSON file into the data store, bypassing the need for discovery. After loading, the internal cache is automatically rebuilt.