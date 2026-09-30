using System.Data;
using W.Dm;
using Xunit;

namespace W.DmProvider.TypeTests;

public sealed class ParameterContractTests
{
    [Fact]
    public void ValueAssignmentDoesNotReplaceExplicitProviderType()
    {
        var parameter = new DmParameter { DmSqlType = DmDbType.VarBinary };
        parameter.Value = new byte[] { 0x00, 0xFF };
        Assert.Equal(DmDbType.VarBinary, parameter.DmSqlType);
    }

    [Fact]
    public void ResetDbTypeClearsTheExplicitSource()
    {
        var parameter = new DmParameter { DmSqlType = DmDbType.Int32, Value = 12L };
        parameter.ResetDbType();
        Assert.Equal(DmDbType.Int64, parameter.DmSqlType);
        Assert.Equal(DbType.Int64, parameter.DbType);
    }

    [Fact]
    public void DuplicateNormalizedNamesAreRejected()
    {
        using var command = new DmCommand();
        command.Parameters.Add(new DmParameter { ParameterName = ":item", Value = 1 });
        Assert.NotNull(Record.Exception(() =>
            command.Parameters.Add(new DmParameter { ParameterName = "ITEM", Value = 2 })));
        Assert.Single(command.Parameters);
    }

    [Theory]
    [InlineData(DbType.Byte, 256L)]
    [InlineData(DbType.UInt16, -1L)]
    [InlineData(DbType.UInt16, 65536L)]
    [InlineData(DbType.UInt32, -1L)]
    [InlineData(DbType.UInt64, -1L)]
    public void ExplicitUnsignedSourceRangeRejectsValuesBeforeWireWidening(DbType type, long value)
    {
        var parameter = new DmParameter { DbType = type, Value = value };
        Assert.Throws<OverflowException>(() => parameter.ValidateInputSourceRange(parameter.Value));
    }

    [Fact]
    public void ExplicitUnsignedMaximumsRemainValidAfterWireWidening()
    {
        foreach (var (type, value) in new (DbType, object)[] {
            (DbType.Byte, byte.MaxValue), (DbType.UInt16, ushort.MaxValue),
            (DbType.UInt32, uint.MaxValue), (DbType.UInt64, ulong.MaxValue) })
        {
            var parameter = new DmParameter { DbType = type, Value = value };
            parameter.ValidateInputSourceRange(parameter.Value);
            Assert.Equal(value, parameter.Value);
            Assert.Equal(type, parameter.DbType);
        }
    }

    [Fact]
    public void GenericBinaryMapsToVariableWidthAndFixedBinaryRetainsPositiveSize()
    {
        var variable = new DmParameter { DbType = DbType.Binary, Value = Array.Empty<byte>() };
        Assert.Equal(DmDbType.VarBinary, variable.ResolveType(null!).Type);
        var fixedWidth = new DmParameter { DmSqlType = DmDbType.Binary, Size = 16, Value = new byte[] { 1, 2, 3 } };
        Assert.Equal(DmDbType.Binary, fixedWidth.ResolveType(null!).Type);
        Assert.Equal(16, fixedWidth.Size);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(6)]
    public void BareIntervalUsesVerifiedDayToSecondLayout(int? fractionalScale)
        => Assert.Equal(0x696, DmCommand.ResolveIntervalDayToSecondScale(14, 0, 2, 0, fractionalScale));

    [Fact]
    public void ValidIntervalDescriptorIsPreservedAndFixedScaleConflictRejects()
    {
        Assert.Equal(0x693, DmCommand.ResolveIntervalDayToSecondScale(21, 0x693, 2, 0, null));
        Assert.Throws<InvalidOperationException>(() =>
            DmCommand.ResolveIntervalDayToSecondScale(21, 0x693, 1, 0, 6));
        Assert.Throws<NotSupportedException>(() =>
            DmCommand.ResolveIntervalDayToSecondScale(14, 0, 1, 0, null));
    }

    [Fact]
    public void ConflictingExplicitDbAndProviderTypesRejectBeforeExecution()
    {
        var parameter = new DmParameter { Value = 1, DbType = DbType.Int32,
            DmSqlType = DmDbType.VarChar };
        Assert.Throws<InvalidOperationException>(parameter.ValidateTypeConfiguration);
    }
}
