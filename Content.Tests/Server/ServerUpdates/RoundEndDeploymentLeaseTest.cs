using System;
using Content.Server.ServerUpdates;
using NUnit.Framework;

namespace Content.Tests.Server.ServerUpdates;

[TestFixture]
public sealed class RoundEndDeploymentLeaseTest
{
    private const string Ticket = "0123456789abcdef0123456789abcdef";

    [Test]
    public void CancelledRunnerCannotLeaveAnIndefiniteShutdownRequest()
    {
        var lease = new RoundEndDeploymentLease(Ticket, TimeSpan.Zero);
        Assert.That(lease.IsActive(TimeSpan.FromSeconds(179)), Is.True);
        Assert.That(lease.IsActive(TimeSpan.FromSeconds(180)), Is.False);
        Assert.Throws<InvalidOperationException>(() => lease.Renew(TimeSpan.FromSeconds(180)));
    }

    [Test]
    public void HeartbeatsKeepALongRoundEligible()
    {
        var lease = new RoundEndDeploymentLease(Ticket, TimeSpan.Zero);
        for (var second = 30; second <= 18000; second += 30)
        {
            Assert.That(lease.IsActive(TimeSpan.FromSeconds(second)), Is.True);
            lease.Renew(TimeSpan.FromSeconds(second));
        }
        Assert.That(lease.IsActive(TimeSpan.FromSeconds(18180)), Is.False);
    }

    [TestCase("")]
    [TestCase("../data")]
    [TestCase("0123456789abcdef0123456789abcdeg")]
    public void RejectInvalidTickets(string ticket)
    {
        Assert.Throws<ArgumentException>(() => new RoundEndDeploymentLease(ticket, TimeSpan.Zero));
    }
}
