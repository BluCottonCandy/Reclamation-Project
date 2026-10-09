using System;
using System.Linq;

namespace Content.Server.ServerUpdates;

/// <summary>A deployment may stop the server only while its workflow is still checking in.</summary>
public sealed class RoundEndDeploymentLease
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(3);
    public string Ticket { get; }
    public TimeSpan ExpiresAt { get; private set; }

    public RoundEndDeploymentLease(string ticket, TimeSpan now)
    {
        if (ticket.Length != 32 || ticket.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Deployment ticket must contain 32 hexadecimal characters.", nameof(ticket));
        Ticket = ticket;
        ExpiresAt = now + Lifetime;
    }

    public bool IsActive(TimeSpan now) => now < ExpiresAt;

    public void Renew(TimeSpan now)
    {
        if (!IsActive(now))
            throw new InvalidOperationException("Deployment request expired; start a new workflow run.");
        ExpiresAt = now + Lifetime;
    }
}
