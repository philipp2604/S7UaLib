using S7UaLib.Core.Enums;

namespace S7UaLib.Infrastructure.Ua.Converters;

public static class UaStatusCodeConverter
{
    public static StatusCode Convert(Opc.Ua.StatusCode statusCode)
    {
        return Opc.Ua.StatusCode.IsGood(statusCode)
            ? StatusCode.Good
            : Opc.Ua.StatusCode.IsBad(statusCode) ? StatusCode.Bad : StatusCode.Uncertain;
    }
}