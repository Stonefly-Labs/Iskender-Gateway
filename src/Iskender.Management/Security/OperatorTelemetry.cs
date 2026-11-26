using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Iskender.Domain.Identity;

namespace Iskender.Management.Security;

/// <summary>
/// The identity activities and metrics, from an <c>ActivitySource</c> and a <c>Meter</c> named <c>Iskender.Identity</c>
/// (operator-security contract, Diagnostics rules 3 and 4, FR-037). Tags and labels are only purpose, outcome, reason
/// class, and route template. Each host's service provider owns one instance and its meter, so no count lives in a static
/// field (hosting contract Static state). Owned by ISK-0010.
/// </summary>
internal sealed class OperatorTelemetry : IDisposable
{
    public const string SourceName = "Iskender.Identity";
    public const string RedeemActivity = "iskender.identity.redeem";
    public const string AuthorizeActivity = "iskender.identity.authorize";
    public const string PurposeLabel = "purpose";
    public const string OutcomeLabel = "outcome";
    public const string ReasonLabel = "reason";
    public const string RouteLabel = "route";
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";
    public const string None = "none";

    /// <summary>The instruments' names, kinds, and labels, as the contract lists them.</summary>
    public static ImmutableArray<(string Name, string Kind, ImmutableArray<string> Labels)> Instruments { get; } =
    [
        ("iskender.identity.redemptions", "counter", [PurposeLabel, OutcomeLabel, ReasonLabel]),
        ("iskender.identity.admin.denials", "counter", [RouteLabel, ReasonLabel]),
        ("iskender.identity.tokens.issued", "counter", [PurposeLabel]),
        ("iskender.identity.tokens.superseded", "counter", [PurposeLabel]),
    ];

    private static readonly ActivitySource Source = new(SourceName);

    private readonly Meter _meter = new(SourceName);
    private readonly Counter<long> _redemptions;
    private readonly Counter<long> _denials;
    private readonly Counter<long> _issued;
    private readonly Counter<long> _superseded;

    public OperatorTelemetry()
    {
        _redemptions = _meter.CreateCounter<long>(Instruments[0].Name, "{redemption}", "Operator token redemptions, by purpose, outcome, and reason class.");
        _denials = _meter.CreateCounter<long>(Instruments[1].Name, "{request}", "Denied admin requests, by route template and reason.");
        _issued = _meter.CreateCounter<long>(Instruments[2].Name, "{token}", "Operator tokens issued, by purpose.");
        _superseded = _meter.CreateCounter<long>(Instruments[3].Name, "{token}", "Operator tokens superseded, by purpose.");
    }

    /// <summary>Starts <c>iskender.identity.redeem</c> for a redemption at <paramref name="purpose"/>'s route.</summary>
    public static Activity? StartRedeem(OperatorTokenPurpose purpose) =>
        Source.StartActivity(RedeemActivity, ActivityKind.Internal, parentContext: default, [new(PurposeLabel, purpose.ToCode())]);

    /// <summary>Starts <c>iskender.identity.authorize</c> for <paramref name="route"/>, a route template.</summary>
    public static Activity? StartAuthorize(string route) =>
        Source.StartActivity(AuthorizeActivity, ActivityKind.Internal, parentContext: default, [new(RouteLabel, route)]);

    /// <summary>Sets the outcome and reason tags, then stops <paramref name="activity"/>.</summary>
    public static void End(Activity? activity, string outcome, string reason)
    {
        if (activity is null)
        {
            return;
        }

        activity.SetTag(OutcomeLabel, outcome);
        activity.SetTag(ReasonLabel, reason);
        activity.Stop();
    }

    /// <summary>Counts a redemption: <c>succeeded</c> with reason <c>none</c>, or <c>failed</c> with its reason class.</summary>
    public void RecordRedemption(OperatorTokenPurpose purpose, RedemptionFailureReason? reason) =>
        _redemptions.Add(
            1,
            new(PurposeLabel, purpose.ToCode()),
            new(OutcomeLabel, reason is null ? Succeeded : Failed),
            new(ReasonLabel, reason?.ToCode() ?? None));

    /// <summary>Counts a denied admin request on <paramref name="route"/>, a route template.</summary>
    public void RecordDenial(string route, string reason) =>
        _denials.Add(1, new(RouteLabel, route), new(ReasonLabel, OperatorDenials.Require(reason)));

    /// <summary>Counts an issued token and the tokens it superseded.</summary>
    public void RecordIssued(OperatorTokenPurpose purpose, int superseded)
    {
        _issued.Add(1, new KeyValuePair<string, object?>(PurposeLabel, purpose.ToCode()));
        if (superseded > 0)
        {
            _superseded.Add(superseded, new KeyValuePair<string, object?>(PurposeLabel, purpose.ToCode()));
        }
    }

    public void Dispose() => _meter.Dispose();
}
