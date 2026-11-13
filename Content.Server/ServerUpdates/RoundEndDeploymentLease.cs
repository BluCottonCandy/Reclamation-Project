using System;
using System.Linq;

namespace Content.Server.ServerUpdates;

/// <summary>A single deployment request expires if its round boundary takes too long.</summary>
public sealed class RoundEndDeploymentLease
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(5) + TimeSpan.FromMinutes(3);
    public string Ticket { get; }
    public TimeSpan ExpiresAt { get; }

    public RoundEndDeploymentLease(string ticket, TimeSpan now)
    {
        if (ticket.Length != 32 || ticket.Any(c => !char.IsAsciiHexDigit(c)))
            throw new ArgumentException("Deployment ticket must contain 32 hexadecimal characters.", nameof(ticket));
        Ticket = ticket;
        ExpiresAt = now + Lifetime;
    }

    public bool IsActive(TimeSpan now) => now < ExpiresAt;
}
