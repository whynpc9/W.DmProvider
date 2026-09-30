using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Sessions;
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

    // Synthetic row bytes and a local CLOB exercise the public getter path without a socket.
    // Only the lease is disposed: disposing the synthetic reader would close a fake server statement.
    private sealed class ReaderFixture : IDisposable
    {
        private readonly DmExecutionLease lease;
        internal DmDataReader Reader { get; }

        internal ReaderFixture(int cType, byte[]? value, string? clobText = null, bool sequential = false, long? clobLength = null)
        {
            var session = new DmSession();
            session.CompleteHandshakeForTests();
            lease = session.BeginExecution(DmOperationPurpose.Reader);
            var statement = Uninitialized<LegacyStatement>();
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
            Set("m_DbInfo", info);
            Set("m_ColInfo", columns);
            Set("m_RsCache", cache);
            Set("m_GetVal", new DmGetValue("UTF-8", statement, false, columns));
            Set("is_SequentialAccess", sequential);
            Set("m_SequentialSeq", sequential ? 0 : -1);
            if (clobText != null)
            {
                var clob = Uninitialized<DmClob>();
                clob.local = true;
                clob.data = clobText;
                clob.m_length = clobLength ?? clobText.Length;
                Set("m_Clobs", new ArrayList { clob, null });
            }
        }

        private void Set(string name, object value) => typeof(DmDataReader)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(Reader, value);
        private static T Uninitialized<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        public void Dispose() => lease.Dispose();
    }
}
