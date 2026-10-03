using System.Buffers.Binary;
using System.Data;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using W.Dm;
using Xunit;

namespace W.DmProvider.LobTests;

// Independent synthetic row bytes; this does not establish actual server ROWID behavior.
[Trait("Category", "Contract")]
[Trait("Feature", "StreamingLobTypes")]
public sealed class ReaderTypeCompatibilityTests
{
    private static JsonDocument Vectors()
    {
        var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "r3-reader-types", "vectors.json")));
        Assert.Equal(new[] { 8, 12 }, document.RootElement.GetProperty("rowid_vectors").EnumerateArray()
            .Select(value => Convert.FromHexString(value.GetProperty("raw_hex").GetString()!).Length).Order().ToArray());
        Assert.Equal(new[] { "0:GB18030", "0:UTF-8", "54:GB18030", "54:UTF-8" }, document.RootElement.GetProperty("text_vectors").EnumerateArray()
            .Select(value => value.GetProperty("ctype").GetInt32() + ":" + value.GetProperty("encoding").GetString()).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(new[] { 0, 1, 2 }, document.RootElement.GetProperty("getbytes_rejection_controls").EnumerateArray()
            .Select(value => value.GetProperty("ctype").GetInt32()).Order().ToArray());
        return document;
    }

    private static ushort[] Units(JsonElement values) => values.EnumerateArray()
        .Select(value => Convert.ToUInt16(value.GetString()!.Replace("0x", "", StringComparison.Ordinal), 16)).ToArray();
    private static ushort[] Units(ReadOnlySpan<char> chars)
    {
        ushort[] result = new ushort[chars.Length];
        for (int index = 0; index < chars.Length; index++) result[index] = chars[index];
        return result;
    }

    [Fact]
    public async Task RowIdGetCharsUsesIndependentRenderedGoldenIncludingPartialAndZeroReads()
    {
        using var vectors = Vectors();
        foreach (var vector in vectors.RootElement.GetProperty("rowid_vectors").EnumerateArray())
        {
            Assert.Equal(28, vector.GetProperty("ctype").GetInt32());
            await using var row = new Row(28, Convert.FromHexString(vector.GetProperty("raw_hex").GetString()!));
            string rendered = vector.GetProperty("rendered").GetString()!;
            Assert.Equal(vector.GetProperty("utf16_count").GetInt32(), row.Reader.GetChars(0, 3, null!, 17, 1));
            char[] complete = new char[rendered.Length];
            Assert.Equal(complete.Length, row.Reader.GetChars(0, 0, complete, 0, complete.Length));
            Assert.Equal(rendered, new string(complete));
            Assert.Equal(Units(vector.GetProperty("utf16_code_units_hex")), Units(complete));
            foreach (var range in vector.GetProperty("partial_ranges").EnumerateArray())
            {
                int requested = range.GetProperty("request_length").GetInt32();
                char[] target = new char[requested + 2];
                int expected = range.GetProperty("expected_count").GetInt32();
                Assert.Equal(expected, row.Reader.GetChars(0, range.GetProperty("field_offset").GetInt64(), target, 1, requested));
                Assert.Equal(range.GetProperty("expected_rendered").GetString(), new string(target, 1, expected));
            }
            char[] zero = ['!'];
            Assert.Equal(0, row.Reader.GetChars(0, 0, zero, zero.Length, 0));
            Assert.Equal('!', zero[0]); row.AssertNoIo();
        }
    }

    [Fact]
    public async Task RowIdTextReaderRefusesRawBytesInsteadOfDecodingThemAsText()
    {
        using var vectors = Vectors();
        foreach (var vector in vectors.RootElement.GetProperty("rowid_vectors").EnumerateArray())
        {
            await using var row = new Row(28, Convert.FromHexString(vector.GetProperty("raw_hex").GetString()!));
            Assert.Throws<InvalidCastException>(() => row.Reader.GetTextReader(0));
            row.AssertNoIo();
        }
    }

    [Fact]
    public async Task NullRowIdCharReadsRetain6081WithoutNetworkIo()
    {
        await using var row = new Row(28, null);
        Assert.Equal(6081, Assert.Throws<DmException>(() => row.Reader.GetChars(0, 0, null!, 0, 0)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => row.Reader.GetChars(0, 0, new char[1], 0, 1)).Number);
        Assert.Equal(6081, Assert.Throws<DmException>(() => row.Reader.GetChars(0, 0, new char[1], 1, 0)).Number);
        row.AssertNoIo();
    }

    [Fact]
    public async Task SequentialRowIdKeepsLengthProbePositionAnd6097BeforeNextColumn()
    {
        using var vectors = Vectors();
        foreach (var vector in vectors.RootElement.GetProperty("rowid_vectors").EnumerateArray())
        {
            await using var row = new Row(28, Convert.FromHexString(vector.GetProperty("raw_hex").GetString()!), sequential: true);
            string rendered = vector.GetProperty("rendered").GetString()!;
            Assert.Equal(rendered.Length, row.Reader.GetChars(0, 0, null!, 0, 0));
            char[] first = new char[2];
            Assert.Equal(2, row.Reader.GetChars(0, 0, first, 0, 2));
            Assert.Equal(rendered[..2], new string(first));
            Assert.Equal(6097, Assert.Throws<DmException>(() => row.Reader.GetChars(0, 1, new char[1], 0, 1)).Number);
            Assert.Equal(rendered.Length, row.Reader.GetChars(0, 0, null!, 0, 0));
            char[] next = new char[2];
            Assert.Equal(2, row.Reader.GetChars(0, 2, next, 0, 2));
            Assert.Equal(rendered.Substring(2, 2), new string(next));
            Assert.Equal(42, row.Reader.GetInt32(1));
            Assert.Equal(6097, Assert.Throws<DmException>(() => row.Reader.GetChars(0, 0, null!, 0, 0)).Number);
            row.AssertNoIo();
        }
    }

    [Fact]
    public async Task ActualTextTypesPreserveIndependentUtf16GoldenAcrossOffsetsAndOneCharAsyncReads()
    {
        using var vectors = Vectors();
        foreach (var vector in vectors.RootElement.GetProperty("text_vectors").EnumerateArray())
        {
            int type = vector.GetProperty("ctype").GetInt32();
            Assert.Contains(type, new[] { 0, 54 });
            string encoding = vector.GetProperty("encoding").GetString()!;
            byte[] bytes = Convert.FromHexString(vector.GetProperty("encoded_bytes_hex").GetString()!);
            ushort[] expectedUnits = Units(vector.GetProperty("utf16_code_units_hex"));
            await using var row = new Row(type, bytes, charset: encoding);
            Assert.Equal(vector.GetProperty("utf16_count").GetInt32(), expectedUnits.Length);
            Assert.Equal(vector.GetProperty("utf16_count").GetInt32(), row.Reader.GetChars(0, 0, null!, 0, 0));
            foreach (var range in vector.GetProperty("partial_ranges").EnumerateArray())
            {
                int requested = range.GetProperty("request_length").GetInt32();
                char[] target = new char[requested + 1];
                int count = (int)row.Reader.GetChars(0, range.GetProperty("field_offset").GetInt64(), target, 1, requested);
                Assert.Equal(range.GetProperty("expected_count").GetInt32(), count);
                Assert.Equal(Units(range.GetProperty("expected_utf16_code_units_hex")), Units(target.AsSpan(1, count)));
            }
            using var text = row.Reader.GetTextReader(0);
            var actual = new List<ushort>(); char[] one = new char[1];
            while (await text.ReadAsync(one.AsMemory()) != 0) actual.Add(one[0]);
            Assert.Equal(expectedUnits, actual.ToArray());
            row.AssertNoIo();
        }
    }

    [Fact]
    public async Task Type54GetBytesPreservesTheIndependentEncodedPayload()
    {
        using var vectors = Vectors();
        foreach (var vector in vectors.RootElement.GetProperty("text_vectors").EnumerateArray().Where(v => v.GetProperty("ctype").GetInt32() == 54))
        {
            byte[] bytes = Convert.FromHexString(vector.GetProperty("encoded_bytes_hex").GetString()!);
            await using var row = new Row(54, bytes, charset: vector.GetProperty("encoding").GetString()!);
            Assert.Equal(bytes.Length, row.Reader.GetBytes(0, 0, null!, 0, 0));
            byte[] copy = new byte[bytes.Length];
            Assert.Equal(bytes.Length, row.Reader.GetBytes(0, 0, copy, 0, copy.Length));
            Assert.Equal(bytes, copy); row.AssertNoIo();
        }
    }

    [Fact]
    public async Task TypesZeroOneTwoRetainBinaryGetterRefusalWithoutNetworkIo()
    {
        using var vectors = Vectors();
        var reference = vectors.RootElement.GetProperty("text_vectors").EnumerateArray().First();
        byte[] bytes = Convert.FromHexString(reference.GetProperty("encoded_bytes_hex").GetString()!);
        foreach (var control in vectors.RootElement.GetProperty("getbytes_rejection_controls").EnumerateArray())
        {
            int type = control.GetProperty("ctype").GetInt32();
            Assert.Contains(type, new[] { 0, 1, 2 });
            Assert.Equal("GetBytes", control.GetProperty("operation").GetString());
            Assert.Equal("InvalidCastException", control.GetProperty("expected_exception").GetString());
            await using var row = new Row(type, bytes, charset: reference.GetProperty("encoding").GetString()!);
            Assert.Throws<InvalidCastException>(() => row.Reader.GetBytes(0, 0, null!, 0, 0));
            Assert.Throws<InvalidCastException>(() => row.Reader.GetBytes(0, 0, new byte[1], 0, 1));
            Assert.Throws<InvalidCastException>(() => row.Reader.GetBytes(0, 0, new byte[1], 1, 0));
            row.AssertNoIo();
        }
    }

    private sealed class Row : IAsyncDisposable
    {
        internal OutputLobFixture Database { get; } = new();
        internal DmDataReader Reader { get; }
        internal Row(int type, byte[]? payload, bool sequential = false, string charset = "UTF-8")
        {
            var instance = Database.Instance;
            instance.ConnProperty.ServerEncoding = charset;
            var statement = (W.Dm.Internal.Legacy.A.A)RuntimeHelpers.GetUninitializedObject(typeof(W.Dm.Internal.Legacy.A.A));
            Set(statement, "__t02_field_04000923", instance);
            Set(statement, "__t02_field_04000924", instance.GetCsi());
            Set(statement, "__t02_field_04000933", new DmCommand("synthetic_type_row", Database.Connection));
            Set(statement, "__t02_field_04000925", new W.Dm.Internal.Legacy.A.b());
            Set(statement, "__t02_field_04000926", new W.Dm.Internal.Legacy.A.b());
            var info = new DmInfo(instance);
            info.SetColumnsInfo([new DmColumn(instance) { type = type, name = "SYNTHETIC_VALUE" },
                new DmColumn(instance) { type = 7, prec = 4, name = "FOLLOWING_INTEGER" }]);
            info.SetHasResultSet(true); info.SetRowCount(1);
            Set(statement, "__t02_field_04000927", info);
            byte[] following = new byte[4]; BinaryPrimitives.WriteInt32LittleEndian(following, 42);
            var cache = new DmResultSetCache(statement, 2, 1)
            { datas = [[[], payload!, following]], datasStartPos = 0 };
            Set(statement, "__t02_field_04000928", cache);
            Reader = new DmDataReader(cache, info, sequential ? CommandBehavior.SequentialAccess : CommandBehavior.Default);
            Reader.AttachExecutionLease(Database.Lease, ownsLease: false);
            Assert.True(Reader.Read());
        }
        internal void AssertNoIo()
        { Assert.Empty(Database.Channel.Commands); Assert.Equal(0, Database.Channel.SynchronousCalls); }
        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);
        public ValueTask DisposeAsync() => Database.DisposeAsync();
    }
}
