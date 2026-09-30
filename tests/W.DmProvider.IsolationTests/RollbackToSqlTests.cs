using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.IsolationTests;

public sealed class RollbackToSqlTests
{
    [Theory]
    [InlineData("ROLLBACK TO sp")]
    [InlineData("ROLLBACK TO SAVEPOINT sp")]
    [InlineData("ROLLBACK TO \"sp\"")]
    [InlineData("rollback to savepoint \"sp\";")]
    [InlineData("ROLLBACK TO \"sp\"\"quote\";")]
    [InlineData("/*lead*/ rollback/**/to/**/savepoint /*mid*/ \"sp\" /*tail*/; --end")]
    [InlineData("ROLLBACK TO sp -- COMMIT is a comment")]
    [InlineData("ROLLBACK TO \"sp; COMMIT\"")]
    public void ExactlyOneSavepointRollbackAcceptsQuotedAndUnquotedNames(string sql)
        => Assert.True(DmParameterBinding.IsStrictRollbackToSavepoint(sql));

    [Theory]
    [InlineData("ROLLBACK")]
    [InlineData("ROLLBACK;")]
    [InlineData("ROLLBACK TO")]
    [InlineData("ROLLBACK TO SAVEPOINT")]
    [InlineData("ROLLBACK TO ''")]
    [InlineData("ROLLBACK TO \"\"")]
    [InlineData("ROLLBACK TO sp extra")]
    [InlineData("ROLLBACK TO sp; COMMIT")]
    [InlineData("ROLLBACK TO SAVEPOINT \"sp\"; COMMIT")]
    [InlineData("ROLLBACK TO sp --comment\n COMMIT")]
    [InlineData("ROLLBACK TO :p0")]
    [InlineData("ROLLBACK TO [sp]")]
    [InlineData("ROLLBACK TO `sp`")]
    [InlineData("ROLLBACK TO (sp)")]
    [InlineData("ROLLBACK TO \"unterminated")]
    [InlineData("ROLLBACK TO sp /*unterminated")]
    [InlineData("ROLLBACK TO \"sp\0\"")]
    public void MissingNameOrControlEscapeCannotPassTheSavepointGate(string sql)
        => Assert.False(DmParameterBinding.IsStrictRollbackToSavepoint(sql));
}
