# S7UaLib API Description

## Overview

S7UaLib is a .NET library designed to simplify communication with Siemens S7 PLCs (like S7-1200/1500) via their integrated OPC UA server. It abstracts away the low-level complexities of the OPC UA protocol and the specifics of the S7 address space, providing a high-level, intuitive, and task-oriented API.

The primary goal of this library is to allow developers to interact with the PLC's data structure in a way that feels natural in C#, using symbolic tag names and strongly-typed objects, rather than dealing with raw NodeIDs and manual data conversions.

The main entry point for all interactions is the `S7Service` class, located in the `S7UaLib.Services.S7` namespace.

**License Note:** S7UaLib depends on the official OPC Foundation libraries, which are licensed under the GPL v2.0. Consequently, any application using S7UaLib must also comply with the terms of the GPL v2.0 license.

## Key Features

- **High-Level Service (`S7Service`):** A single, easy-to-use service class that manages the client configuration, connection, data storage, and communication.
- **High-Performance Session Pooling:** Utilizes an internal pool of OPC UA sessions for stateless operations (browsing, reading, writing), dramatically reducing overhead and increasing throughput for high-frequency tasks.
- **Automatic Structure Discovery:** The library can browse the entire OPC UA address space of the S7 PLC and build a complete, hierarchical in-memory model of all available data, including:
  - Global Data Blocks (DBs)
  - Instance Data Blocks (for FBs)
  - Inputs (I), Outputs (Q), and Memory (M) areas
  - Timers (T) and Counters (C)
- **Manual Registration:** Manually define variables and data blocks that are not browsable on the OPC UA server, enabling access to any tag by its NodeID.
- **Seamless S7 Data Type Conversion:** Automatically handles the conversion between complex S7-specific data types and standard .NET types. This is a core feature that saves significant development time. Supported types include:
  - `DATE_AND_TIME` (8-byte BCD) <-> `.NET DateTime`
  - `DTL` (12-byte struct) <-> `.NET DateTime` (with nanosecond precision)
  - `S5TIME` (legacy timer format) <-> `.NET TimeSpan`
  - `TIME`, `LTIME`, `TIME_OF_DAY`, `LTIME_OF_DAY` <-> `.NET TimeSpan`
  - `DATE` <-> `.NET DateTime`
  - `COUNTER` (BCD format) <-> `.NET ushort`
  - And all primitive types (`BOOL`, `INT`, `REAL`, `STRING`, etc.) and their arrays.
- **Data Persistence:** The discovered PLC structure can be saved to a JSON file and loaded back later. This eliminates the need for a time-consuming discovery process on every application startup.
- **OPC UA Client Configuration Management:** The underlying OPC UA client configuration can be saved and loaded, preserving security settings across application runs.
- **Data Access by Path:** Read and write variable values using their full symbolic path (e.g., `"DataBlocksGlobal.SettingsDB.MotorSpeed"`), without needing to know the underlying OPC UA `NodeId`.
- **Data Subscriptions:** Subscribe to individual variables to receive notifications when their values change on the PLC. The library manages the underlying `MonitoredItem`s and raises a simple `VariableValueChanged` event.
- **Dynamic Type Correction:** The data type (`S7DataType`) of a variable can be changed at runtime if the initial discovery was ambiguous (e.g., a `WORD` that should be treated as an `S5TIME`).

## Core Concepts

### Architecture

The library is built on a clean, three-layer architecture to separate concerns:

-   **`S7UaLib.Core`**: The foundation. It defines all shared data models, interfaces, and enumerations (like `IS7Variable`, `S7DataType`, `ApplicationConfiguration`). It is the common "language" of the ecosystem.
-   **`S7UaLib.Infrastructure`**: The engine. It contains the concrete implementations for OPC UA communication (`S7UaClient`), data type conversion, and persistence logic. The `S7UaClient` itself is composed of a main client for persistent connections (subscriptions, status) and a session pool for high-performance stateless operations (read/write/browse).
-   **`S7UaLib` (the main package)**: The public API. It provides the high-level `S7Service`, which orchestrates the underlying components into a simple and powerful interface for you to use.

As a user, you only need to interact with the `S7Service` from the main `S7UaLib` package and the data models from `S7UaLib.Core`.

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
using S7UaLib.Core.Ua.Configuration;
using S7UaLib.Services.S7;
using System;
using System.IO;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        // 1. Instantiate the S7Service.
        // For anonymous login, use a new UserIdentity().
        // For username/password, use: new S7Service(new UserIdentity("user", "pass"))
        // The maxSessions parameter configures the internal session pool size.
        var service = new S7Service(new UserIdentity(), maxSessions: 5);

        // 2. Create the OPC UA client application configuration.
        // This is a mandatory step before connecting.
        var appConfig = new ApplicationConfiguration
        {
            ApplicationName = "My S7 UA Client",
            ApplicationUri = "urn:localhost:MyS7UaClient", // Must be unique
            ProductUri = "uri:mycompany:mys7uaclient",
            SecurityConfiguration = new SecurityConfiguration(new SecurityConfigurationStores())
            {
                // For testing, accept all certificates. In production, this should be false
                // and you should manage the certificate trust list.
                AutoAcceptUntrustedCertificates = true,
                SkipDomainValidation = new() { Skip = true },
                RejectSHA1SignedCertificates = new() { Reject = false }
            }
        };
        await service.ConfigureAsync(appConfig);

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
            service.Dispose();
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

By default, a PLC tag is assigned `S7DataType.UNKNOWN`, which means no value conversion takes place. If, for example, a `WORD` should be interpreted as an `S5TIME`, you can correct this in the data store. Changes can be made persistent by saving the structure using `SaveStructureAsync`.

```csharp
using S7UaLib.Core.Enums;
// ...assuming 'service' is a connected S7Service instance with a loaded structure

var timerPath = "DataBlocksGlobal.Timers.T1_Preset";

// Let's say discovery identified it as an unknown type (raw value is a ushort)
var timerVar = service.GetVariable(timerPath);
Console.WriteLine($"Type before update: {timerVar.S7Type}, Value: {timerVar.Value}");
// Output might be: Type before update: UNKNOWN, Value: 8212

// Update the type to S5TIME using the enum from S7UaLib.Core
await service.UpdateVariableTypeAsync(timerPath, S7DataType.S5TIME);

// The service automatically reconverts the raw value
timerVar = service.GetVariable(timerPath);
Console.WriteLine($"Type after update: {timerVar.S7Type}, Value: {timerVar.Value}");
// Output should be: Type after update: S5TIME, Value: 00:00:12.3400000 (as a TimeSpan)
```

### Manually Registering Variables

If some variables or data blocks are not browsable via OPC UA, you can add them manually to the data store before reading their values.

```csharp
using S7UaLib.Core.S7.Structure;
// ...assuming 'service' is a connected S7Service instance

// 1. Register the parent data block if it doesn't exist
var db = new S7DataBlockGlobal
{
    DisplayName = "NonBrowsableDB",
    FullPath = "DataBlocksGlobal.NonBrowsableDB",
    NodeId = "ns=3;s=\"NonBrowsableDB\"" // NodeId from your PLC project
};
await service.RegisterGlobalDataBlockAsync(db);

// 2. Register the hidden variable within that data block
var hiddenVar = new S7Variable
{
    DisplayName = "MyHiddenInt",
    FullPath = "DataBlocksGlobal.NonBrowsableDB.MyHiddenInt",
    NodeId = "ns=3;s=\"NonBrowsableDB\".\"MyHiddenInt\"", // NodeId from your PLC project
    S7Type = S7DataType.INT
};
await service.RegisterVariableAsync(hiddenVar);

// 3. Now you can read/write it like any other variable
await service.ReadAllVariablesAsync();
var value = service.GetVariable("DataBlocksGlobal.NonBrowsableDB.MyHiddenInt")?.Value;
Console.WriteLine($"Value of hidden variable: {value}");
```

## `S7Service` API Reference

This section provides a complete reference for the public `S7Service` class. The event arguments and data models (`ConnectionEventArgs`, `IS7Variable`, `S7DataType`, etc.) are defined in the `S7UaLib.Core` library.

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
- `UserIdentity UserIdentity { get; }`
  > Gets the identity information of the user for authentication (e.g., username/password).

### Methods

#### Configuration Methods

- `Task ConfigureAsync(ApplicationConfiguration appConfig)`
  > Configures the underlying OPC UA client application. This must be called before connecting.
- `void SaveConfiguration(string filePath)`
  > Saves the client's current OPC UA configuration to a file.
- `Task LoadConfigurationAsync(string filePath)`
  > Loads the client's OPC UA configuration from a file.
- `Task AddTrustedCertificateAsync(X509Certificate2 certificate, CancellationToken cancellationToken = default)`
  > Adds a certificate to the trusted certificate store.

#### Connection Methods

- `Task ConnectAsync(string serverUrl, bool useSecurity = true, CancellationToken cancellationToken = default)`
  > Asynchronously connects to the specified S7 UA server endpoint.
- `Task DisconnectAsync(bool leaveChannelOpen = false, CancellationToken cancellationToken = default)`
  > Disconnects from the S7 UA server.

#### Structure Discovery & Registration Methods

- `Task DiscoverStructureAsync(CancellationToken cancellationToken = default)`
  > Discovers the entire structure of the OPC UA server and populates the internal data store. Throws an `InvalidOperationException` if not connected.
- `Task<bool> RegisterGlobalDataBlockAsync(IS7DataBlockGlobal dataBlock, CancellationToken cancellationToken = default)`
  > Registers a new global data block manually in the data store's structure.
- `Task<bool> RegisterVariableAsync(IS7Variable variable, CancellationToken cancellationToken = default)`
  > Registers a new variable manually in the data store's structure. The parent element must already exist.

#### Data Access Methods
- `IReadOnlyList<IS7DataBlockGlobal> GetGlobalDataBlocks()`
  > Gets all cached global data blocks.
- `IReadOnlyList<IS7DataBlockInstance> GetInstanceDataBlocks()`
  > Gets all cached instance data blocks.
- `IS7Inputs? GetInputs()`
  > Gets the cached inputs area.
- `IS7Outputs? GetOutputs()`
  > Gets the cached outputs area.
- `IS7Memory? GetMemory()`
  > Gets the cached memory area.
- `IS7Timers? GetTimers()`
  > Gets the cached timers area.
- `IS7Counters? GetCounters()`
  > Gets the cached counters area.
- `IReadOnlyList<IS7Variable> FindVariablesWhere(Func<IS7Variable, bool> predicate)`
  > Filters and returns variables from the internal cache based on a predicate.
- `IS7Variable? GetVariable(string fullPath)`
  > Retrieves a variable from the data store by its full symbolic path. Returns the `IS7Variable` if found; otherwise, `null`.

#### Variable Read/Write Methods
- `Task ReadAllVariablesAsync(CancellationToken cancellationToken = default)`
  > Reads the values of all discovered variables from the PLC. Raises the `VariableValueChanged` event for any variable whose value has changed.
- `Task<bool> WriteVariableAsync(string fullPath, object value)`
  > Writes a value to a variable specified by its full symbolic path. The library handles the necessary type conversion. Returns `true` on success.
- `Task<bool> UpdateVariableTypeAsync(string fullPath, S7DataType newType, CancellationToken cancellationToken = default)`
  > Updates the S7 data type of a variable in the data store and attempts to reconvert its current raw value. Returns `true` if the variable was found and updated.

#### Subscription Methods

- `Task<bool> SubscribeToVariableAsync(string fullPath, uint samplingInterval = 500, CancellationToken cancellationToken = default)`
  > Subscribes to a variable to receive value changes from the server. Will create the main subscription on the first call. Returns `true` on success.
- `Task<bool> SubscribeToAllConfiguredVariablesAsync(CancellationToken cancellationToken = default)`
  > Subscribes to all variables that have the `IsSubscribed` flag set to `true` (typically from a loaded structure file). Returns `true` if all subscriptions were successful.
- `Task<bool> UnsubscribeFromVariableAsync(string fullPath, CancellationToken cancellationToken = default)`
  > Unsubscribes from a variable to stop receiving value changes. Returns `true` on success.

#### UDT and Custom Type Methods

- `void RegisterUdtConverter<T>(IUdtConverter<T> converter) where T : class`
  > Registers a custom UDT converter for a specific UDT type with strong typing. This converter will be used to convert between PLC UDT structure members and user-defined C# objects.
- `void RegisterUdtConverter(string udtName, IS7TypeConverter converter)`
  > Registers a custom converter for a specific UDT type. This converter will be used instead of the generic UDT converter for variables of this type.
- `void RegisterUdtConverter<T>(string udtName) where T : IS7TypeConverter, new()`
  > Registers a custom converter instance for a specific UDT type.
- `Task<UdtDefinition?> DiscoverUdtDefinitionAsync(string udtTypeName, CancellationToken cancellationToken = default)`
  > Discovers the structure definition of a specific UDT type from the PLC.
- `Task<IReadOnlyList<string>> GetAvailableUdtTypesAsync(CancellationToken cancellationToken = default)`
  > Discovers all available UDT types from the PLC.
- `IReadOnlyDictionary<string, UdtDefinition> GetDiscoveredUdts()`
  > Gets all currently discovered UDT definitions.
- `IUdtTypeRegistry GetUdtTypeRegistry()`
  > Gets the UDT type registry containing all discovered UDT definitions and registered custom converters.

#### Persistence Methods

- `Task SaveStructureAsync(string filePath)`
  > Saves the current entire PLC structure from the data store to a JSON file. This includes all discovered elements and their assigned data types.
- `Task LoadStructureAsync(string filePath)`
  > Loads the PLC structure from a JSON file into the data store, bypassing the need for discovery. After loading, the internal cache is automatically rebuilt.