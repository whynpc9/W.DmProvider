using W.Dm.Internal.Native;
using Xunit;

namespace W.DmProvider.SecurityTests;

public sealed class NativeLibraryTests
{
    private static readonly string CanonicalPath = Path.Combine(Path.GetTempPath(), "wdm-t07-native-placeholder");

    [Theory]
    [InlineData("relative.so")]
    [InlineData("../parent.so")]
    public void UntrustedRelativePathRejectsBeforeLoad(string path)
    {
        var fake = new FakeBackend();
        Assert.Throws<ArgumentException>(() => DmNativeLibrary.OpenTrustedAbsolute(path, ["required_symbol"], fake));
        Assert.Equal(0, fake.LoadCount);
        Assert.Equal(0, fake.FreeCount);
    }

    [Fact]
    public void MissingLibraryReportsSanitizedFailureWithoutFreeingAbsentHandle()
    {
        var fake = new FakeBackend { LoadFailure = new DllNotFoundException("synthetic-private-marker") };
        var error = Assert.Throws<NotSupportedException>(() =>
            DmNativeLibrary.OpenTrustedAbsolute(CanonicalPath, ["required_symbol"], fake));
        Assert.True(!error.Message.Contains("synthetic-private-marker", StringComparison.Ordinal));
        Assert.Equal(1, fake.LoadCount);
        Assert.Equal(0, fake.FreeCount);
    }

    [Fact]
    public void WrongArchitectureReportsSanitizedFailure()
    {
        var fake = new FakeBackend { LoadFailure = new BadImageFormatException("synthetic-private-marker") };
        var error = Assert.Throws<NotSupportedException>(() =>
            DmNativeLibrary.OpenTrustedAbsolute(CanonicalPath, ["required_symbol"], fake));
        Assert.True(!error.Message.Contains("synthetic-private-marker", StringComparison.Ordinal));
        Assert.Equal(0, fake.FreeCount);
    }

    [Fact]
    public void MissingExportReleasesLoadedLibraryExactlyOnce()
    {
        var fake = new FakeBackend { HasExport = false };
        Assert.Throws<NotSupportedException>(() =>
            DmNativeLibrary.OpenTrustedAbsolute(CanonicalPath, ["required_symbol"], fake));
        Assert.Equal(1, fake.LoadCount);
        Assert.Equal(1, fake.FreeCount);
    }

    [Fact]
    public void DeclaredExportIsScopedAndDisposeIsIdempotent()
    {
        var fake = new FakeBackend();
        using var library = DmNativeLibrary.OpenTrustedAbsolute(CanonicalPath, ["required_symbol"], fake);
        bool callbackCalled = library.WithExport("required_symbol", address => address == (nint)456);
        Assert.True(callbackCalled);
        Assert.Throws<NotSupportedException>(() => library.WithExport("unknown_symbol", _ => true));
        library.Dispose();
        library.Dispose();
        Assert.Equal(1, fake.FreeCount);
        Assert.Throws<ObjectDisposedException>(() => library.WithExport("required_symbol", _ => true));
    }

    [Fact]
    public void NativeInitializationFailureStillReleasesHandleOnce()
    {
        var fake = new FakeBackend();
        using var library = DmNativeLibrary.OpenTrustedAbsolute(CanonicalPath, ["required_symbol"], fake);
        Assert.Throws<InvalidOperationException>(() => library.WithExport<bool>("required_symbol", _ =>
            throw new InvalidOperationException("synthetic failure")));
        library.Dispose();
        Assert.Equal(1, fake.FreeCount);
    }

    [Fact]
    public async Task DisposeWaitsForActiveExportCallbackBeforeFreeing()
    {
        var fake = new FakeBackend();
        using var library = DmNativeLibrary.OpenTrustedAbsolute(CanonicalPath, ["required_symbol"], fake);
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        Task<bool> pending = Task.Run(() => library.WithExport("required_symbol", address =>
        {
            entered.Set();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException("barrier");
            return address == (nint)456;
        }));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(3)));
        library.Dispose();
        Assert.Equal(0, fake.FreeCount);
        release.Set();
        Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(1, fake.FreeCount);
    }

    private sealed class FakeBackend : IDmNativeLibraryBackend
    {
        private int loadCount;
        private int freeCount;
        public int LoadCount => Volatile.Read(ref loadCount);
        public int FreeCount => Volatile.Read(ref freeCount);
        public Exception? LoadFailure { get; init; }
        public bool HasExport { get; init; } = true;
        public nint Load(string absolutePath)
        {
            Interlocked.Increment(ref loadCount);
            if (LoadFailure != null) throw LoadFailure;
            return (nint)123;
        }
        public bool TryGetExport(nint library, string name, out nint address)
        {
            address = HasExport ? (nint)456 : 0;
            return HasExport;
        }
        public void Free(nint library) => Interlocked.Increment(ref freeCount);
    }
}
