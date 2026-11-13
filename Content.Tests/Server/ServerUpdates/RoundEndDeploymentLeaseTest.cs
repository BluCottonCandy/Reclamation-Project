using System;
using Content.Server.ServerUpdates;
using NUnit.Framework;

namespace Content.Tests.Server.ServerUpdates;

[TestFixture]
public sealed class RoundEndDeploymentLeaseTest
{
    private const string Ticket = "0123456789abcdef0123456789abcdef";

    [Test]
    public void SingleRequestSurvivesTheFiveHourWorkflowWaitAndExpires()
    {
        var lease = new RoundEndDeploymentLease(Ticket, TimeSpan.Zero);
        Assert.That(lease.IsActive(TimeSpan.FromHours(5)), Is.True);
        Assert.That(lease.IsActive(TimeSpan.FromMinutes(303)), Is.False);
    }

    [TestCase("")]
    [TestCase("../data")]
    [TestCase("0123456789abcdef0123456789abcdeg")]
    public void RejectInvalidTickets(string ticket)
    {
        Assert.Throws<ArgumentException>(() => new RoundEndDeploymentLease(ticket, TimeSpan.Zero));
    }
}
