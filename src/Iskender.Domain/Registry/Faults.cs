using Iskender.Core.Contracts.Context;
using Iskender.Core.Contracts.Errors;

namespace Iskender.Domain.Registry;

/// <summary>Builds a module-fault exception whose detail sits in its diagnostics, never in its message.</summary>
internal static class Faults
{
    public static GatewayException Create(ErrorCode code, params (string Key, string Value)[] values) =>
        new(GatewayError.Create(code, RequestCorrelationId.New(), OutputVisibility.None,
            diagnostics: new ErrorDiagnostics(values: values.Select(pair => KeyValuePair.Create(pair.Key, pair.Value)))));
}
