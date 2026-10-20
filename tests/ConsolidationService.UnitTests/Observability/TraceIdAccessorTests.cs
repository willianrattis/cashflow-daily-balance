namespace ConsolidationService.UnitTests.Observability;

public class TraceIdAccessorTests
{
    [Fact]
    public void GetTraceId_falls_back_to_traceIdentifier_when_no_activity_is_current()
    {
        Activity.Current = null;
        var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-fallback-1" };

        Assert.Equal("trace-fallback-1", TraceIdAccessor.GetTraceId(httpContext));
    }

    [Fact]
    public void GetTraceId_prefers_the_current_w3c_activity_traceId()
    {
        using var activity = new Activity("test-activity");
        activity.SetIdFormat(ActivityIdFormat.W3C);
        activity.Start();
        try
        {
            var httpContext = new DefaultHttpContext { TraceIdentifier = "trace-fallback-2" };

            var traceId = TraceIdAccessor.GetTraceId(httpContext);

            Assert.Equal(activity.TraceId.ToHexString(), traceId);
            Assert.NotEqual("trace-fallback-2", traceId);
        }
        finally
        {
            activity.Stop();
            Activity.Current = null;
        }
    }
}
