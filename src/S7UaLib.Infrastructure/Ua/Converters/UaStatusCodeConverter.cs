using S7UaLib.Core.Enums;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
