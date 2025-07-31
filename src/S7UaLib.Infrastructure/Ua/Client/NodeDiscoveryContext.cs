using S7UaLib.Core.Ua;

namespace S7UaLib.Infrastructure.Ua.Client;

/// <summary>
/// Context for node discovery operations, encapsulating browsing parameters and node creation logic.
/// </summary>
internal record NodeDiscoveryContext(
    Opc.Ua.NodeClass NodeClassMask,
    Func<Opc.Ua.ReferenceDescription, bool> Filter,
    Func<Opc.Ua.ReferenceDescription, IUaNode> NodeFactory,
    PathBuilder? PathBuilder = null
);