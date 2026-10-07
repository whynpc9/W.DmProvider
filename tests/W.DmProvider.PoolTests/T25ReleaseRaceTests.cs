using System.Data.Common;
using System.Reflection;
using W.Dm;
using Xunit;

namespace W.DmProvider.PoolTests;

[Collection("Pool API hooks")]
[Trait("Category", "Contract")]
[Trait("Feature", "T25CallerBoundary")]
public sealed class T25ReleaseRaceTests
{
    [Fact]
    public async Task DisposeBetweenReleaseGateAndNullSetterCannotReplaceSuccessfulResult()
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        var inner = new SetterBarrierCommand();
        var shortcut = new DmDataSourceCommand(source, "synthetic", inner);
        Task<int> call = Task.Run(() => shortcut.ExecuteNonQueryAsync());
        try
        {
            await inner.NullSetterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            // The actual public execution has already closed its physical
            // connection, while Release's rebinding has not completed yet.
            Assert.Equal(0, source.Snapshot.PhysicalCount);
            Assert.True(handshake.Channels.Single().IsClosed);
            Assert.Equal(1, ReadActive(shortcut));
            await shortcut.DisposeAsync();
            inner.ReleaseSetter.TrySetResult();
            Assert.Equal(1, await call.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, ReadActive(shortcut));
            Assert.Null(ReadConnection(shortcut));
            Assert.Equal(0, source.Snapshot.PhysicalCount);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => shortcut.ExecuteNonQueryAsync());
        }
        finally
        {
            inner.ReleaseSetter.TrySetResult();
            try { await call.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            await shortcut.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("other_object", true)]
    [InlineData("not_ode", true)]
    [InlineData("dm_command", false)]
    public async Task ReleaseDoesNotHideUnrelatedSetterFailures(string failureKind, bool disposeShortcut)
    {
        using var handshake = new SyntheticPoolHandshake();
        await using var source = new DmDataSource(PoolApiSettings.Text);
        Exception expected = failureKind switch
        {
            "other_object" => new ObjectDisposedException("AnotherObject"),
            "dm_command" => new ObjectDisposedException(nameof(DmCommand)),
            _ => new InvalidOperationException("Synthetic unrelated setter failure.")
        };
        var inner = new SetterBarrierCommand { SetterFailure = expected };
        var shortcut = new DmDataSourceCommand(source, "synthetic", inner);
        Task<int> call = Task.Run(() => shortcut.ExecuteNonQueryAsync());
        try
        {
            await inner.NullSetterEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, source.Snapshot.PhysicalCount);
            Assert.Equal(1, ReadActive(shortcut));
            if (disposeShortcut) await shortcut.DisposeAsync();
            inner.ReleaseSetter.TrySetResult();
            Exception? actual = await Record.ExceptionAsync(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Same(expected, actual);
            Assert.Equal(0, ReadActive(shortcut));
            Assert.Null(ReadConnection(shortcut));
            Assert.Equal(0, source.Snapshot.PhysicalCount);
        }
        finally
        {
            inner.ReleaseSetter.TrySetResult();
            try { await call.WaitAsync(TimeSpan.FromSeconds(5)); } catch { }
            await shortcut.DisposeAsync();
        }
    }

    private static int ReadActive(DmDataSourceCommand command) =>
        (int)typeof(DmDataSourceCommand).GetField("active", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(command)!;
    private static object? ReadConnection(DmDataSourceCommand command) =>
        typeof(DmDataSourceCommand).GetField("activeConnection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(command);

    private sealed class SetterBarrierCommand : DmCommand
    {
        internal readonly TaskCompletionSource NullSetterEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource ReleaseSetter = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Exception? SetterFailure;
        protected override DbConnection DbConnection
        {
            get => base.DbConnection;
            set
            {
                if (value == null)
                {
                    NullSetterEntered.TrySetResult();
                    ReleaseSetter.Task.GetAwaiter().GetResult();
                    if (SetterFailure != null) throw SetterFailure;
                }
                base.DbConnection = value;
            }
        }
        public override Task<int> ExecuteNonQueryAsync(CancellationToken token)
        { token.ThrowIfCancellationRequested(); return Task.FromResult(1); }
    }
}
