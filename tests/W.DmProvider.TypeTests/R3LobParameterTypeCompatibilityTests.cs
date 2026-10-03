using System.Data;
using W.Dm;
using W.Dm.Internal.Execution;
using W.Dm.Internal.Types;
using Xunit;

namespace W.DmProvider.TypeTests;

[Trait("Category", "Contract")]
[Trait("Feature", "LobParameterTypes")]
public sealed class R3LobParameterTypeCompatibilityTests
{
    [Theory]
    [InlineData(DbType.String, DmDbType.Clob, false)]
    [InlineData(DbType.String, DmDbType.Clob, true)]
    [InlineData(DbType.String, DmDbType.Text, false)]
    [InlineData(DbType.String, DmDbType.Text, true)]
    [InlineData(DbType.AnsiString, DmDbType.Clob, false)]
    [InlineData(DbType.AnsiString, DmDbType.Clob, true)]
    [InlineData(DbType.AnsiString, DmDbType.Text, false)]
    [InlineData(DbType.AnsiString, DmDbType.Text, true)]
    public void ExplicitTextLobSurvivesCloneAndPlanCapture(DbType dbType, DmDbType dmType,
        bool providerTypeFirst)
    {
        // Small, fixed Unicode input: AnsiString describes the caller's explicit
        // type; the explicit LOB type still selects the existing server codec.
        const string supplied = "A\u4E2D\U0001F642Z";
        var parameter = Create(dbType, dmType, providerTypeFirst, supplied);
        parameter.ValidateTypeConfiguration();
        AssertSnapshot(parameter.Clone(), dbType, dmType, supplied);
        AssertCaptured(parameter, dbType, dmType, supplied);
    }

    [Theory]
    [InlineData(DbType.StringFixedLength, DmDbType.Clob)]
    [InlineData(DbType.StringFixedLength, DmDbType.Text)]
    [InlineData(DbType.AnsiStringFixedLength, DmDbType.Clob)]
    [InlineData(DbType.AnsiStringFixedLength, DmDbType.Text)]
    [InlineData(DbType.Binary, DmDbType.Clob)]
    [InlineData(DbType.Binary, DmDbType.Text)]
    [InlineData(DbType.AnsiString, DmDbType.Blob)]
    [InlineData(DbType.Int32, DmDbType.Clob)]
    public void ConflictingExplicitTypesRemainRejectedBeforePlanCreation(DbType dbType,
        DmDbType dmType)
    {
        foreach (bool providerTypeFirst in new[] { false, true })
        {
            var parameter = Create(dbType, dmType, providerTypeFirst, "small");
            Assert.Throws<InvalidOperationException>(parameter.ValidateTypeConfiguration);
            Assert.Throws<InvalidOperationException>(parameter.Clone().ValidateTypeConfiguration);
            using var command = new DmCommand("SELECT :p FROM DUAL");
            command.Parameters.Add(parameter);
            Assert.Throws<InvalidOperationException>(() => DmCommandPlan.Capture(
                command.CommandText, CommandType.Text, 30, null!, null!,
                (DmParameterCollection)command.Parameters));
        }
    }

    [Fact]
    public void ExistingExplicitBinaryBlobCompatibilityRemainsValid()
    {
        byte[] supplied = [0x00, 0x7F, 0xFF];
        foreach (bool providerTypeFirst in new[] { false, true })
        {
            var parameter = Create(DbType.Binary, DmDbType.Blob, providerTypeFirst, supplied);
            parameter.ValidateTypeConfiguration();
            AssertSnapshot(parameter.Clone(), DbType.Binary, DmDbType.Blob, supplied);
            AssertCaptured(parameter, DbType.Binary, DmDbType.Blob, supplied);
        }
    }

    private static DmParameter Create(DbType dbType, DmDbType dmType,
        bool providerTypeFirst, object value)
    {
        var parameter = new DmParameter { ParameterName = "p", Value = value, Size = -1 };
        if (providerTypeFirst)
        {
            parameter.DmSqlType = dmType;
            parameter.DbType = dbType;
        }
        else
        {
            parameter.DbType = dbType;
            parameter.DmSqlType = dmType;
        }
        return parameter;
    }

    private static void AssertCaptured(DmParameter parameter, DbType dbType,
        DmDbType dmType, object supplied)
    {
        using var command = new DmCommand("SELECT :p FROM DUAL");
        command.Parameters.Add(parameter);
        using var plan = DmCommandPlan.Capture(command.CommandText, CommandType.Text, 30,
            null!, null!, (DmParameterCollection)command.Parameters);
        AssertSnapshot(Assert.IsType<DmParameter>(plan.Parameters[0]), dbType, dmType, supplied);
        var metadata = Assert.Single(plan.ParameterMetadata);
        Assert.Equal(dbType, metadata.DbType);
        Assert.Equal(dmType, metadata.DmSqlType);
        Assert.Equal(DmParameterTypeSource.ExplicitDmSqlType, metadata.Source);
        Assert.True(metadata.ExplicitSize);
        Assert.Equal(-1, metadata.Size);
        AssertSnapshot(parameter, dbType, dmType, supplied);
    }

    private static void AssertSnapshot(DmParameter parameter, DbType dbType,
        DmDbType dmType, object supplied)
    {
        Assert.Equal(dbType, parameter.DbType);
        Assert.Equal(dmType, parameter.DmSqlType);
        Assert.Equal(-1, parameter.Size);
        Assert.Equal((dmType, DmParameterTypeSource.ExplicitDmSqlType), parameter.ResolveType(null!));
        if (supplied is byte[] bytes)
            Assert.Equal(bytes, Assert.IsType<byte[]>(parameter.Value));
        else
            Assert.Same(supplied, parameter.Value);
    }
}
