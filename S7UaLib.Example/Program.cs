using Microsoft.Extensions.Logging;
using Opc.Ua;
using S7UaLib.Events;
using S7UaLib.S7.Types; // Hinzufügen für S7DataType
using S7UaLib.Services;
using System.Collections;

namespace S7UaLib.Example;

internal static class Program
{
    // --- Configuration ---

    private const string _serverUrl = "opc.tcp://172.168.0.1:4840";
    private const string _configFilePath = "s7_config.json";

    private static readonly ApplicationConfiguration _appConfig = new()
    {
        ApplicationName = "S7UaLib Console Example",
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

    private static readonly Action<IList, IList> _validateResponse = ClientBase.ValidateResponse;

    private static S7Service? _service;

    public static async Task Main()
    {
        Console.WriteLine("--- S7UaLib Sample Application ---");

        // Set up logging
        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        // Initialize services
        _service = new S7Service(_appConfig, _validateResponse, loggerFactory);

        try
        {
            // 1. Connect to the server
            Console.WriteLine($"Connecting to server: {_serverUrl}...");
            await _service.ConnectAsync(_serverUrl, useSecurity: false);
            Console.WriteLine("Connection established successfully.");

            // 2. Load or discover PLC structure
            if (File.Exists(_configFilePath))
            {
                Console.WriteLine($"Loading structure from '{_configFilePath}'...");
                await _service.LoadStructureAsync(_configFilePath);
                Console.WriteLine("Structure loaded successfully.");

                Console.WriteLine("Subscribing to all configured variables.");
                var result = await _service.SubscribeToAllConfiguredVariablesAsync();
                if(result)
                {
                    Console.WriteLine("Subscribed successfully to all configured variables.");
                }
                else
                {
                    Console.WriteLine("Could not subscribe to all configured variables.");
                }
            }
            else
            {
                Console.WriteLine("No configuration file found. Performing discovery...");
                await _service.DiscoverStructureAsync();
                Console.WriteLine("Discovery complete. Saving structure...");
                await _service.SaveStructureAsync(_configFilePath);
                Console.WriteLine($"Structure saved to '{_configFilePath}'.");
            }

            Console.WriteLine("Performing initial read of all variables...");
            await _service.ReadAllVariablesAsync();
            Console.WriteLine("Initial read complete.");

            // Subscribe to the value changed event
            _service.VariableValueChanged += OnVariableValueChanged;

            // 3. Start the main loop for user commands
            await MainLoop();
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"A critical error occurred: {ex.Message}");
            Console.ResetColor();
        }
        finally
        {
            // Clean up
            Console.WriteLine("Saving structure...");
            await _service.SaveStructureAsync(_configFilePath);
            if (_service?.IsConnected == true)
            {
                Console.WriteLine("Disconnecting...");
                await _service.DisconnectAsync();
            }
            if (_service != null)
            {
                _service.VariableValueChanged -= OnVariableValueChanged;
                _service.Dispose();
            }
        }
    }

    private static async Task MainLoop()
    {
        ShowHelp();
        bool running = true;
        while (running)
        {
            Console.Write("> ");
            var input = Console.ReadLine()?.Trim() ?? string.Empty;
            var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            var command = parts[0].ToLower();

            try
            {
                switch (command)
                {
                    case "help":
                        ShowHelp();
                        break;

                    case "list-gdb":
                        ListGlobalDataBlocks();
                        break;

                    case "list-idb":
                        ListInstanceDataBlocks();
                        break;

                    case "read":
                        ReadVariable(parts);
                        break;

                    case "subscribe":
                        await SubscribeToVariableAsync(parts);
                        break;

                    case "unsubscribe":
                        await UnsubscribeFromVariableAsync(parts);
                        break;

                    case "write":
                        await WriteVariableAsync(parts);
                        break;

                    case "refresh":
                        await RefreshAllVariables();
                        break;

                    case "exit":
                        running = false;
                        break;

                    default:
                        Console.WriteLine($"Unknown command: '{command}'. Type 'help' for a list of commands.");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Error executing command: {ex.Message}");
                Console.ResetColor();
            }
        }
    }

    private static void OnVariableValueChanged(object? sender, VariableValueChangedEventArgs e)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("\n--- Value Change Detected ---");
        Console.WriteLine($"  Path:      {e.NewVariable.FullPath}");
        Console.WriteLine($"  Old Value: {e.OldVariable.Value ?? "null"}");
        Console.WriteLine($"  New Value: {e.NewVariable.Value ?? "null"}");
        Console.ResetColor();
        Console.Write("> "); // Restores the prompt
    }

    private static void ShowHelp()
    {
        Console.WriteLine("\nAvailable Commands:");
        Console.WriteLine("  help                            - Shows this help message.");
        Console.WriteLine("  list-gdb                        - Lists all global data blocks.");
        Console.WriteLine("  list-idb                        - Lists all instance data blocks.");
        Console.WriteLine("  read <path>                     - Reads a variable (e.g., read DataBlocksGlobal.DB1.MyVar).");
        Console.WriteLine("  write <path> <value>            - Writes a value to a string variable (e.g., write ...TestString 'Hello').");
        Console.WriteLine("  subscribe <path>                - Subscribes to real-time changes for a variable.");
        Console.WriteLine("  unsubscribe <path>              - Unsubscribes from a variable.");
        Console.WriteLine("  refresh                         - Re-reads all variables from the PLC.");
        Console.WriteLine("  exit                            - Exits the application.");
        Console.WriteLine();
    }

    private static void ListGlobalDataBlocks()
    {
        Console.WriteLine("\n--- Global Data Blocks ---");
        // TODO: Implement logic to get and display from _service.DataStore.DataBlocksGlobal
        Console.WriteLine("TODO");
    }

    private static void ListInstanceDataBlocks()
    {
        Console.WriteLine("\n--- Instance Data Blocks ---");
        // TODO: Implement logic to get and display from _service.DataStore.DataBlocksInstance
        Console.WriteLine("TODO");
    }

    private static void ReadVariable(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Error: Please provide a path. Example: read DataBlocksGlobal.DB1.MyVar");
            return;
        }
        string path = parts[1];
        var variable = _service?.GetVariable(path);

        if (variable == null)
        {
            Console.WriteLine($"Variable with path '{path}' not found.");
            return;
        }

        Console.WriteLine($"\n--- Variable: {variable.FullPath} ---");
        Console.WriteLine($"  Value:        {variable.Value ?? "null"}");
        Console.WriteLine($"  S7 Type:      {variable.S7Type}");
        Console.WriteLine($"  .NET Type:    {variable.SystemType?.Name ?? "unknown"}");
        Console.WriteLine($"  Subscribed:   {variable.IsSubscribed}");
        Console.WriteLine($"  Status:       {variable.StatusCode}");
    }

    private static async Task SubscribeToVariableAsync(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Error: Please provide a path. Example: subscribe DataBlocksGlobal.DB1.MyVar");
            return;
        }
        string path = parts[1];

        Console.WriteLine($"Subscribing to '{path}'...");
        bool success = await _service!.SubscribeToVariableAsync(path);

        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Successfully subscribed. You will now receive notifications for this variable.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to subscribe. Check if the path is correct.");
            Console.ResetColor();
        }
    }

    private static async Task UnsubscribeFromVariableAsync(string[] parts)
    {
        if (parts.Length < 2)
        {
            Console.WriteLine("Error: Please provide a path. Example: unsubscribe DataBlocksGlobal.DB1.MyVar");
            return;
        }
        string path = parts[1];

        Console.WriteLine($"Unsubscribing from '{path}'...");
        bool success = await _service!.UnsubscribeFromVariableAsync(path);

        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Successfully unsubscribed.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Failed to unsubscribe. Was the variable subscribed?");
            Console.ResetColor();
        }
    }

    private static async Task WriteVariableAsync(string[] parts)
    {
        if (parts.Length < 3)
        {
            Console.WriteLine("Error: Please provide a path and a value. Example: write DataBlocksGlobal.DB1.MyString 'Hello World'");
            return;
        }
        string path = parts[1];
        // Join all remaining parts to allow for values with spaces (if enclosed in quotes)
        string valueStr = string.Join(" ", parts.Skip(2));

        object valueToWrite = valueStr;

        var variable = _service?.GetVariable(path);
        // This example is simplified to only write strings. A real application would need type conversion.
        if (variable?.S7Type != S7DataType.STRING && variable?.S7Type != S7DataType.WSTRING)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Error: This example only supports writing to STRING or WSTRING variables for simplicity.");
            Console.ResetColor();
            return;
        }

        Console.WriteLine($"Writing value '{valueToWrite}' to variable '{path}'...");
        bool success = await _service!.WriteVariableAsync(path, valueToWrite);

        if (success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Write successful.");
            Console.ResetColor();
        }
        else
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Write failed. Check path and value.");
            Console.ResetColor();
        }
    }

    private static async Task RefreshAllVariables()
    {
        Console.WriteLine("Refreshing all variables from the server...");
        await _service!.ReadAllVariablesAsync();
        Console.WriteLine("Refresh complete. Changes (if any) have been displayed.");
    }
}