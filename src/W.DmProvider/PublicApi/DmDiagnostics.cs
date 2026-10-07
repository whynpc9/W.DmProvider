using System;
using System.Text.Json;

namespace W.Dm;

/// <summary>Credential-free standard diagnostics. Export callbacks run on a bounded background dispatcher.</summary>
public static class DmDiagnostics
{
    public const string ActivitySourceName = "W.DmProvider";
    public const string MeterName = "W.DmProvider";

    /// <summary>Formats only known failure metadata. Never reads Message, Data, stack or inner exceptions.</summary>
    public static string FormatException(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        DmFailureInfo info = error is DmException driver ? driver.FailureInfo :
            error is DmOperationCanceledException canceled ? canceled.FailureInfo : null;
        string type = error switch
        {
            DmCommitOutcomeUnknownException => "DmCommitOutcomeUnknownException",
            DmTimeoutException => "DmTimeoutException",
            DmOperationCanceledException => "DmOperationCanceledException",
            DmException => "DmException", OperationCanceledException => "OperationCanceledException", _ => "Unknown"
        };
        return JsonSerializer.Serialize(new
        {
            type,
            code = KnownCode(info?.ErrorCode),
            kind = KnownEnum(info?.ErrorKind), phase = KnownEnum(info?.Phase),
            operation_outcome = KnownEnum(info?.OperationOutcome), transaction_outcome = KnownEnum(info?.TransactionOutcome),
            cancel_source = KnownEnum(info?.CancelSource), server_number = info?.ServerErrorNumber,
            connection_reusable = info?.ConnectionReusable
        });
    }

    private static string KnownEnum<T>(T? value) where T : struct, Enum =>
        value.HasValue && Enum.IsDefined(value.Value) ? value.Value.ToString() : "Unknown";

    private static string KnownCode(string code) => code switch
    {
        "WDM_SERVER" or "WDM_TIMEOUT" or "WDM_CANCELED" or "WDM_TRANSPORT" or "WDM_COMMIT_UNKNOWN" or
        "WDM_POOL_WAIT_TIMEOUT" or "WDM_POOL_WAIT_CANCELED" or "WDM_POOL_WAIT_QUEUE_FULL" or "WDM_POOL_REGISTRY_FULL" or
        "WDM_POOL_LEASE_ID_EXHAUSTED" or "WDM_CONNECT_TIMEOUT" or "WDM_CONNECT_CANCELED" => code,
        _ => "Unknown"
    };
}
