using S7UaLib.Core.S7.Structure;
using S7UaLib.Core.Ua;
using S7UaLib.Infrastructure.Serialization.Json.Converters;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;

namespace S7UaLib.Infrastructure.Serialization.Json;

/// <summary>
/// Provides serialization options and configuration for handling S7 structure elements and related types.
/// </summary>
/// <remarks>This class defines a set of <see cref="JsonSerializerOptions"/> tailored for serializing and
/// deserializing S7-related data structures, including support for polymorphism and custom converters. The
/// configuration ensures proper handling of derived types and null value conditions, making it suitable for scenarios
/// involving complex S7 data models.</remarks>
internal static class S7StructureSerializer
{
    public static JsonSerializerOptions Options { get; }

    static S7StructureSerializer()
    {
        Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters =
            {
                new NodeIdJsonConverter(),
                new TypeJsonConverter()
            },
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers = { ConfigurePolymorphism }
            }
        };
    }

    private static void ConfigurePolymorphism(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Type == typeof(IS7Variable))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes = { new JsonDerivedType(typeof(S7Variable), nameof(S7Variable)) }
            };
        }

        if (typeInfo.Type == typeof(IS7StructureElement))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7StructureElement), nameof(S7StructureElement)),
                    new JsonDerivedType(typeof(S7DataBlockGlobal), nameof(S7DataBlockGlobal)),
                    new JsonDerivedType(typeof(S7Counters), nameof(S7Counters)),
                    new JsonDerivedType(typeof(S7Inputs), nameof(S7Inputs)),
                    new JsonDerivedType(typeof(S7Memory), nameof(S7Memory)),
                    new JsonDerivedType(typeof(S7Outputs), nameof(S7Outputs)),
                    new JsonDerivedType(typeof(S7Timers), nameof(S7Timers))
                }
            };
        }

        if (typeInfo.Type == typeof(IUaNode))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7StructureElement), nameof(S7StructureElement)),
                    new JsonDerivedType(typeof(S7DataBlockGlobal), nameof(S7DataBlockGlobal)),
                    new JsonDerivedType(typeof(S7DataBlockInstance), nameof(S7DataBlockInstance)),
                    new JsonDerivedType(typeof(S7InstanceDbSection), nameof(S7InstanceDbSection)),
                    new JsonDerivedType(typeof(S7Variable), nameof(S7Variable)),
                    new JsonDerivedType(typeof(S7Counters), nameof(S7Counters)),
                    new JsonDerivedType(typeof(S7Inputs), nameof(S7Inputs)),
                    new JsonDerivedType(typeof(S7Memory), nameof(S7Memory)),
                    new JsonDerivedType(typeof(S7Outputs), nameof(S7Outputs)),
                    new JsonDerivedType(typeof(S7Timers), nameof(S7Timers))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7DataBlockGlobal))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7DataBlockGlobal), nameof(S7DataBlockGlobal))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7DataBlockInstance))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7DataBlockInstance), nameof(S7DataBlockInstance))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7InstanceDbSection))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7InstanceDbSection), nameof(S7InstanceDbSection))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7Inputs))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7Inputs), nameof(S7Inputs))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7Outputs))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7Outputs), nameof(S7Outputs))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7Memory))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7Memory), nameof(S7Memory))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7Timers))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7Timers), nameof(S7Timers))
                }
            };
        }

        if (typeInfo.Type == typeof(IS7Counters))
        {
            typeInfo.PolymorphismOptions = new JsonPolymorphismOptions
            {
                DerivedTypes =
                {
                    new JsonDerivedType(typeof(S7Counters), nameof(S7Counters))
                }
            };
        }
    }
}