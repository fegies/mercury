using appcore.Infra;
using Npgsql;
using Xunit;

namespace appcore.Tests.Infrastructure;

public class DbEventStoreConcurrencyTests
{
	[Fact]
	public void Translates_the_concurrency_assert_error()
	{
		var conflict = DbEventStore.TranslateConcurrencyError(new PostgresException(
			"mercury_err_concurrency_conflict", "ERROR", "ERROR", "P0001"));

		Assert.IsType<ConcurrencyConflictException>(conflict);
	}

	[Theory]
	[InlineData("mercury_err_concurrency_conflict", "P0002")]
	[InlineData("some other assert failure", "P0001")]
	[InlineData("deadlock detected", "40P01")]
	[InlineData("relation does not exist", "42P01")]
	public void Leaves_every_other_error_untouched(string message, string sqlState)
	{
		var translated = DbEventStore.TranslateConcurrencyError(new PostgresException(
			message, "ERROR", "ERROR", sqlState));

		Assert.Null(translated);
	}
}
