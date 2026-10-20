namespace ConsolidationService.UnitTests.Messaging;

/// <summary>Every branch of ConsumerFailureClassifier.Classify (docs/diagrams/09-sequence-transient-retry.md).</summary>
public class ConsumerFailureClassifierTests
{
    [Theory]
    [InlineData("08000")] // class 08: connection exception
    [InlineData("08006")]
    [InlineData("53000")] // class 53: insufficient resources
    [InlineData("53300")]
    [InlineData("57P01")] // admin shutdown
    [InlineData("57P02")] // crash shutdown
    [InlineData("57P03")] // cannot connect now
    [InlineData("57014")] // query canceled (statement/lock timeout)
    [InlineData("55P03")] // lock not available
    [InlineData("40001")] // serialization failure
    [InlineData("40P01")] // deadlock detected
    public void Classify_returns_transient_for_every_documented_transient_sqlstate(string sqlState)
    {
        var exception = new PostgresException("simulated", "ERROR", "ERROR", sqlState);

        var result = ConsumerFailureClassifier.Classify(exception);

        Assert.Equal(ConsumerFailureKind.Transient, result);
    }

    [Theory]
    [InlineData("23505")] // unique_violation — a real data conflict, never transient
    [InlineData("22003")] // numeric_value_out_of_range
    [InlineData("42601")] // syntax_error — a bug in our own SQL, never transient
    public void Classify_returns_unexpected_for_a_non_transient_sqlstate(string sqlState)
    {
        var exception = new PostgresException("simulated", "ERROR", "ERROR", sqlState);

        var result = ConsumerFailureClassifier.Classify(exception);

        Assert.Equal(ConsumerFailureKind.Unexpected, result);
    }

    [Fact]
    public void Classify_returns_transient_for_a_timeout_exception()
    {
        var result = ConsumerFailureClassifier.Classify(new TimeoutException("simulated"));

        Assert.Equal(ConsumerFailureKind.Transient, result);
    }

    [Fact]
    public void Classify_returns_transient_for_a_socket_exception()
    {
        var result = ConsumerFailureClassifier.Classify(new SocketException((int)SocketError.ConnectionRefused));

        Assert.Equal(ConsumerFailureKind.Transient, result);
    }

    [Fact]
    public void Classify_returns_transient_for_an_io_exception()
    {
        var result = ConsumerFailureClassifier.Classify(new IOException("simulated broken pipe"));

        Assert.Equal(ConsumerFailureKind.Transient, result);
    }

    [Fact]
    public void Classify_returns_unexpected_for_a_bug_like_exception()
    {
        var result = ConsumerFailureClassifier.Classify(new InvalidOperationException("simulated projector bug"));

        Assert.Equal(ConsumerFailureKind.Unexpected, result);
    }

    [Fact]
    public void Classify_walks_the_inner_exception_chain_to_find_a_transient_cause()
    {
        var transientCause = new PostgresException("simulated", "ERROR", "ERROR", "57014");
        var wrapped = new InvalidOperationException("outer wrapper", transientCause);

        var result = ConsumerFailureClassifier.Classify(wrapped);

        Assert.Equal(ConsumerFailureKind.Transient, result);
    }

    [Fact]
    public void Classify_returns_unexpected_when_no_exception_in_the_chain_is_transient()
    {
        var nonTransientCause = new PostgresException("simulated", "ERROR", "ERROR", "23505");
        var wrapped = new InvalidOperationException("outer wrapper", nonTransientCause);

        var result = ConsumerFailureClassifier.Classify(wrapped);

        Assert.Equal(ConsumerFailureKind.Unexpected, result);
    }
}
