using System.IO;
using W.Dm;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class SqlCommentReviewTests
{
    [Fact]
    public void CommitAfterTheFirstCommentCloseRemainsAStatementHead()
    {
        var heads = DmParameterBinding.ScanTopLevelStatements("/* /* */ COMMIT -- */");
        Assert.Equal(new DmSqlStatementHead("COMMIT", "", ""), Assert.Single(heads));
    }

    [Fact]
    public void RollbackCommentCannotHideTheFollowingCommit()
    {
        const string sql = "ROLLBACK TO sp /* /* */ ; COMMIT -- */";
        var heads = DmParameterBinding.ScanTopLevelStatements(sql);
        Assert.Collection(heads,
            head => Assert.Equal(new DmSqlStatementHead("ROLLBACK", "TO", "SP"), head),
            head => Assert.Equal(new DmSqlStatementHead("COMMIT", "", ""), head));
        Assert.False(DmParameterBinding.IsStrictRollbackToSavepoint(sql));
    }

    [Theory]
    [InlineData("SELECT /* :ignored /* ? */ :value -- */", "value")]
    [InlineData("SELECT /* :ignored /* ? */ ? -- */", "")]
    public void MarkerAfterTheFirstCommentCloseRemainsVisible(string sql, string parameterName)
    {
        using var command = new DmCommand();
        var parameter = new DmParameter { ParameterName = parameterName, Value = 1 };
        command.Parameters.Add(parameter);
        var binding = DmParameterBinding.Create(sql, command.Parameters);
        Assert.Equal(1, binding.MarkerCount);
        Assert.Same(parameter, binding.ResolveServerParameter(0, parameterName, 1));
    }

    [Theory]
    [InlineData("SELECT /* :ignored ? */ :value -- /* :ignored ?")]
    [InlineData("SELECT '/* /* */ :ignored ?', \"/* /* */ :ignored ?\", q'[/* /* */ :ignored ?]', [/* /* */ :ignored ?], :value")]
    public void OrdinaryCommentsAndQuotedCommentTextKeepMarkersHidden(string sql)
    {
        using var command = new DmCommand();
        command.Parameters.Add(new DmParameter { ParameterName = "value", Value = 1 });
        Assert.Equal(1, DmParameterBinding.Create(sql, command.Parameters).MarkerCount);
        Assert.Equal(new DmSqlStatementHead("SELECT", "", ""),
            Assert.Single(DmParameterBinding.ScanTopLevelStatements(sql)));
    }

    [Theory]
    [InlineData("SELECT /* unterminated")]
    [InlineData("SELECT /* /* unterminated")]
    public void UnterminatedBlockCommentStillRejectsTheSql(string sql)
    {
        using var command = new DmCommand();
        Assert.Throws<InvalidDataException>(() => DmParameterBinding.Create(sql, command.Parameters));
        Assert.Throws<InvalidDataException>(() => DmParameterBinding.ScanTopLevelStatements(sql));
    }
}
