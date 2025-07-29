# S7UaLib.Infrastructure ⚙️
The internal engine room for the S7UaLib ecosystem. This library provides the concrete implementations for the abstractions defined in `S7UaLib.Core`. It brings the data models and contracts to life with a high-performance, pooled OPC UA client, a rich set of data type converters, a dynamic in-memory data store, and advanced serialization logic.

This package contains the "how" – the logic for connecting, browsing, reading, writing, and converting data. It is a required dependency for the high-level `S7UaLib` package and is **not intended for direct use by end-users**.

[![.NET 8 (LTS) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-8-build-and-test.yml)
[![.NET 9 (Latest) Build & Test](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml/badge.svg)](https://github.com/philipp2604/S7UaLib/actions/workflows/dotnet-9-build-and-test.yml)  
[![Language](https://img.shields.io/badge/language-C%23-blue.svg)](https://docs.microsoft.com/en-us/dotnet/csharp/)
[![License](https://img.shields.io/badge/License-GPL_v2-blue.svg)](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html)
[![GitHub issues](https://img.shields.io/github/issues/philipp2604/S7UaLib)](https://github.com/philipp2604/S7UaLib/issues)  
[![NuGet Version](https://img.shields.io/nuget/v/philipp2604.S7UaLib.svg?style=flat-square&logo=nuget)](https://www.nuget.org/packages/philipp2604.S7UaLib/)
[![Ask DeepWiki](https://deepwiki.com/badge.svg)](https://deepwiki.com/philipp2604/S7UaLib)

## ✨ Key Features

-   **⚡ High-Performance, Pooled OPC UA Client Architecture**: A sophisticated client design that separates stateful and stateless operations for maximum efficiency and scalability.
    -   **`S7UaMainClient`**: The stateful core that manages the primary, persistent connection, handles session lifecycle (keep-alives, automatic reconnection), and manages all OPC UA subscriptions.
    -   **`S7UaSessionPool`**: A dedicated pool of pre-connected, stateless sessions for high-throughput, concurrent operations. It enables massive parallelism for browsing, reading, and writing without the overhead of creating new connections for each request.
    -   **`S7UaClient`**: The central orchestrator that intelligently delegates operations, routing stateful requests (like creating a subscription) to the main client and stateless requests (like reading a variable) to the session pool.

-   **🔄 Comprehensive S7 Data Type Conversion**: A rich collection of type converters that seamlessly handle complex S7-specific formats.
    -   BCD-based types like `DATE_AND_TIME`, legacy `S5TIME`, and `COUNTER`.
    -   Modern nanosecond-precision types like `DTL` and `LTIME`.
    -   Date and time-of-day types like `DATE`, `TIME_OF_DAY`, and `LTIME_OF_DAY`.
    -   A powerful meta-converter (`S7ElementwiseArrayConverter`) for handling arrays of complex types (e.g., `ARRAY OF DTL`, `ARRAY OF DATE_AND_TIME`), which correctly converts each element individually.

-   **💾 Dynamic In-Memory Data Store (`S7DataStore`)**: A thread-safe, in-memory cache for holding the entire discovered PLC structure. It enables fast, path-based variable lookups, uses immutable data patterns for safe state updates, and supports dynamic registration of new variables and data blocks at runtime.

-   **📄 Advanced JSON Serialization (`S7StructureSerializer`)**: A purpose-built `System.Text.Json` configuration for saving and loading the complete, discovered PLC structure. It correctly handles the polymorphic nature of the data models (`IS7Variable`, `IS7StructureElement`, etc.).

## 🚀 Getting Started

### Installation

This package is a dependency for other `S7UaLib` libraries and will typically be installed automatically. However, you can install it manually using the .NET CLI:

```bash
dotnet add package philipp2604.S7UaLib.Infrastructure
```

## 🎯 What This Library Is (and Isn't)

-   **✔️ IT IS:** The concrete implementation layer. It contains the working code that communicates with the PLC and manages data.
-   **❌ IT IS NOT:** The public-facing API. Its components are complex and require careful coordination. For a simple, unified interface, you should use the main `S7UaLib` package, which orchestrates these components for you.

## 🤝 Contributing

Contributions are welcome! Please refer to the main [S7UaLib repository](https://github.com/philipp2604/S7UaLib) for guidelines on reporting issues or submitting pull requests.

## ⚖️ License

This project is licensed under the **GNU General Public License v2.0 (GPL-2.0)**.

This is a direct consequence of the dependency on the official `OPCFoundation.NetStandard.Opc.Ua` packages, which are licensed under GPL 2.0. Any project using S7UaLib.Infrastructure must therefore also comply with the terms of the GPL 2.0 license, which generally means that if you distribute your application, you must also make the source code available.

Please review the [license terms](https://www.gnu.org/licenses/old-licenses/gpl-2.0.en.html) carefully before integrating this library into your project.