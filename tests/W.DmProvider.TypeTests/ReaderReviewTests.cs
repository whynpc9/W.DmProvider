using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
using W.Dm.Internal.Types;
using Xunit;
using LegacyStatement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.TypeTests;

public sealed class ReaderReviewTests
{
    [Fact]
    public void Int32NullUsesTheSameNullErrorAsOtherIntegerGetters()
    {
        using var fixture = new ReaderFixture(7, null);
        Assert.True(fixture.Reader.IsDBNull(0));
        Assert.Same(DBNull.Value, fixture.Reader.GetValue(0));
        var expected = Assert.Throws<DmException>(() => fixture.Reader.GetInt64(0));
        Assert.Equal(expected.Number, Assert.Throws<DmException>(() => fixture.Reader.GetInt32(0)).Number);
        Assert.Equal(expected.Number, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<int>(0)).Number);
    }

    [Fact]
    public void ARealIntegerZeroIsStillZero()
    {
        using var fixture = new ReaderFixture(7, new byte[4]);
        Assert.False(fixture.Reader.IsDBNull(0));
        Assert.Equal(0, fixture.Reader.GetInt32(0));
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(0));
    }

    [Theory]
    [InlineData("")]
    [InlineData("A中e\u0301文")]
    [InlineData("汉字é🙂tail")]
    public void ClobNullBufferReturnsFullUtf16LengthRegardlessOfRequestedSlice(string text)
    {
        using var fixture = new ReaderFixture(19, [], text);
        Assert.Equal(text.Length, fixture.Reader.GetChars(0, 3, null!, 17, 1));
        Assert.Equal(text.Length, fixture.Reader.GetChars(0, 0, null!, 0, 0));
    }

    [Fact]
    public void ClobCopiesPartialCharactersAndAcceptsZeroLengthAtBufferEnd()
    {
        using var fixture = new ReaderFixture(19, [], "汉字é🙂tail");
        var buffer = new char[5];
        Assert.Equal(3, fixture.Reader.GetChars(0, 1, buffer, 1, 3));
        Assert.Equal("字é\ud83d", new string(buffer, 1, 3));
        Assert.Equal(0, fixture.Reader.GetChars(0, 0, buffer, buffer.Length, 0));
        Assert.Throws<IndexOutOfRangeException>(() => fixture.Reader.GetChars(0, 0, buffer, -1, 1));
        Assert.Throws<ArgumentException>(() => fixture.Reader.GetChars(0, 0, buffer, 1, int.MaxValue));
        Assert.Throws<DmException>(() => fixture.Reader.GetChars(0, -1, buffer, 0, 1));
    }

    [Fact]
    public void SequentialClobLengthProbePreservesTheCharacterPosition()
    {
        using var fixture = new ReaderFixture(19, [], "汉字abc", sequential: true);
        Assert.Equal(5, fixture.Reader.GetChars(0, 0, null!, 0, 0));
        Assert.Equal(2, fixture.Reader.GetChars(0, 0, new char[2], 0, 2));
        Assert.Throws<DmException>(() => fixture.Reader.GetChars(0, 0, new char[1], 0, 1));
        Assert.Equal(5, fixture.Reader.GetChars(0, 0, null!, 0, 0));
        Assert.Equal(3, fixture.Reader.GetChars(0, 2, new char[3], 0, 3));
        Assert.Equal(0, fixture.Reader.GetInt32(1));
        Assert.Throws<DmException>(() => fixture.Reader.GetChars(0, 0, null!, 0, 0));
    }

    [Fact]
    public void SequentialClobForwardGapAdvancesToTheCopiedEnd()
    {
        using var fixture = new ReaderFixture(19, [], "A中e\u0301文", sequential: true);
        Assert.Equal(1, fixture.Reader.GetChars(0, 2, new char[1], 0, 1));
        Assert.Throws<DmException>(() => fixture.Reader.GetChars(0, 1, new char[1], 0, 1));
        Assert.Equal(2, fixture.Reader.GetChars(0, 3, new char[2], 0, 2));
    }

    [Fact]
    public void ClobLengthProbeRejectsAnUnmaterializableLengthWithoutReturningAPrefix()
    {
        using var fixture = new ReaderFixture(19, [], "short", clobLength: (long)int.MaxValue + 1);
        Assert.Throws<NotSupportedException>(() => fixture.Reader.GetChars(0, 0, null!, 0, 0));
    }

    [Fact]
    public void SequentialGenericIntegerReadsEachColumnOnceAndRejectsBackwardAccess()
    {
        using var fixture = new ReaderFixture(7, BitConverter.GetBytes(42), sequential: true);
        Assert.Equal(42, fixture.Reader.GetFieldValue<int>(0));
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<int>(0)).Number);
    }

    [Fact]
    public void SequentialGenericIntegerNullRaises6081AndAllowsTheNextColumn()
    {
        using var fixture = new ReaderFixture(7, null, sequential: true);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<int>(0)).Number);
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<int>(0)).Number);
    }

    [Fact]
    public void SequentialGenericFallbackReadsStringThenFollowingInteger()
    {
        using var fixture = new ReaderFixture(2, System.Text.Encoding.UTF8.GetBytes("A中"), sequential: true);
        Assert.Equal("A中", fixture.Reader.GetFieldValue<string>(0));
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<string>(0)).Number);
    }

    [Fact]
    public void SequentialGenericFallbackNullRaises6081AndAllowsTheNextColumn()
    {
        using var fixture = new ReaderFixture(2, null, sequential: true);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<string>(0)).Number);
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
    }

    [Fact]
    public void SequentialGenericDmDecimalReadsExactValueAndFollowingColumn()
    {
        DmDecimal exact = DmDecimal.Parse("123.45");
        using var fixture = new ReaderFixture(9, DmNumericCodec.EncodeDecimal(exact), sequential: true);
        Assert.Equal(exact, fixture.Reader.GetFieldValue<DmDecimal>(0));
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<DmDecimal>(0)).Number);
    }

    [Fact]
    public void SequentialGenericDmDecimalNullRaises6081AndAllowsTheNextColumn()
    {
        using var fixture = new ReaderFixture(9, null, sequential: true);
        Assert.Equal(6081, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<DmDecimal>(0)).Number);
        Assert.Equal(0, fixture.Reader.GetFieldValue<int>(1));
        Assert.Equal(6097, Assert.Throws<DmException>(() => fixture.Reader.GetFieldValue<DmDecimal>(0)).Number);
    }

    // Synthetic row bytes and a local CLOB exercise the public getter path without a socket.
    // Only the lease is disposed: disposing the synthetic reader would close a fake server statement.
    internal sealed class ReaderFixture : IDisposable
    {
        private readonly DmExecutionLease lease;
        internal DmDataReader Reader { get; }

        internal ReaderFixture(int cType, byte[]? value, string? clobText = null, bool sequential = false, long? clobLength = null)
        {
            var session = new DmSession();
            session.CompleteHandshakeForTests();
            lease = session.BeginExecution(DmOperationPurpose.Reader);
            var physical = Uninitialized<DmConnInstance>();
            var property = new DmConnection().ConnProperty;
            property.ServerEncoding = "UTF-8";
            property.msgVersion = 21;
            SetField(physical, "m_ConnPro", property);
            SetField(physical, "<Session>k__BackingField", session);
            var statement = Uninitialized<LegacyStatement>();
            SetField(statement, "__t02_field_04000923", physical);
            var column = Uninitialized<DmColumn>();
            column.SetCType(cType);
            var following = Uninitialized<DmColumn>();
            following.SetCType(7);
            DmColumn[] columns = [column, following];
            var info = new DmInfo();
            info.SetColumnsInfo(columns);
            var cache = Uninitialized<DmResultSetCache>();
            cache.colNum = 2;
            cache.currentPos = cache.datasOffset = 0;
            cache.totalRowCount = 1;
            cache.datas = [[[], value!, new byte[4]]];
            Reader = Uninitialized<DmDataReader>();
            Reader.m_Statement = statement;
            Set("executionLease", lease);
            Set("m_Conn", physical);
            Set("m_Clobs", new ArrayList { null, null });
            Set("m_DbInfo", info);
            Set("m_ColInfo", columns);
            Set("m_RsCache", cache);
            Set("m_GetVal", new DmGetValue("UTF-8", statement, false, columns));
            Set("is_SequentialAccess", sequential);
            // Match normal field initialization. A preloaded local CLOB represents a
            // current column, while ordinary rows start before the first column.
            Set("skipCol", true);
            Set("m_SequentialSeq", sequential && clobText != null ? 0 : -1);
            if (clobText != null)
            {
                var clob = Uninitialized<DmClob>();
                clob.local = true;
                clob.data = clobText;
                clob.m_length = clobLength ?? clobText.Length;
                Set("m_Clobs", new ArrayList { clob, null });
            }
        }

        private void Set(string name, object value) => SetField(Reader, name, value);
        private static void SetField(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);
        private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        public void Dispose() => lease.Dispose();
    }
}
