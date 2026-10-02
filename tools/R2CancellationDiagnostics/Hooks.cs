using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using W.Dm;

internal static partial class Program
{
    private sealed record Frame(short RequestOpcode, short ResponseOpcode, int SqlCode, int BodyLength);
    private sealed record InvocationSample(long SessionId, long ExecutionId, long InvocationId, short? RequestOpcode,
        string? Purpose, bool? Completed, bool? IsDisposed, int? RemainingMilliseconds);
    private sealed class Hooks : IDisposable
    {
        private readonly List<(FieldInfo Field, object? Previous)> Saved = [];
        private readonly Channel<object> Journal = Channel.CreateUnbounded<object>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Task Writer;
        private readonly long Started = Stopwatch.GetTimestamp();
        internal ConcurrentQueue<Frame> Frames { get; } = new();
        internal ConcurrentQueue<short> Sent { get; } = new();
        internal ConcurrentQueue<InvocationSample> Invocations { get; } = new();
        internal ConcurrentQueue<int> Modes { get; } = new();
        internal bool SentSupported { get; }
        internal Hooks(string journalPath)
        {
            Writer = WriteJournalAsync(journalPath);
            Bind("W.Dm.Internal.Legacy.A.DmResultProtocolTrace", "AfterFrame", args =>
            {
                var frame = new Frame((short)args[0]!, (short)args[1]!, (int)args[2]!, (int)args[3]!);
                Frames.Enqueue(frame); Journal.Writer.TryWrite(new { kind = "response", elapsed_milliseconds = Stopwatch.GetElapsedTime(Started).TotalMilliseconds, frame });
            }, required: true);
            Bind("W.Dm.Internal.Legacy.A.DmWireTestHooks", "AfterStartupNegotiatedEncryptMode", args => Modes.Enqueue((int)args[0]!), required: true);
            SentSupported = Bind("W.Dm.Internal.Legacy.A.DmWireTestHooks", "AfterFrameSent", args => {
                short opcode = (short)args[1]!; Sent.Enqueue(opcode); Capture(args[0]!, opcode); }, required: false);
            if (!SentSupported)
                Bind("W.Dm.Internal.Legacy.A.DmWireTestHooks", "AfterSendBeforeReceive", args =>
                    Capture(args[0]!, null), required: true);
        }
        private void Capture(object identity, short? opcode)
        {
            var invocation = Sample(identity, opcode); Invocations.Enqueue(invocation);
            Journal.Writer.TryWrite(new { kind = "sent", elapsed_milliseconds = Stopwatch.GetElapsedTime(Started).TotalMilliseconds, invocation });
        }
        private async Task WriteJournalAsync(string path)
        {
            await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 4096, useAsync: true);
            await using var writer = new StreamWriter(file) { AutoFlush = true };
            await foreach (object entry in Journal.Reader.ReadAllAsync().ConfigureAwait(false))
                await writer.WriteLineAsync(JsonSerializer.Serialize(entry)).ConfigureAwait(false);
        }
        private bool Bind(string type, string field, Action<object?[]> callback, bool required)
        {
            var f = typeof(DmConnection).Assembly.GetType(type)?.GetField(field, BindingFlags.NonPublic | BindingFlags.Static);
            if (f == null) { Need(!required, "required_numeric_hook_missing"); return false; }
            object? prior = f.GetValue(null); Need(prior == null, "isolated_numeric_hooks_required");
            var parameters = f.FieldType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
            var body = Expression.Invoke(Expression.Constant(callback), Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object)))));
            f.SetValue(null, Expression.Lambda(f.FieldType, body, parameters).Compile()); Saved.Add((f, prior)); return true;
        }
        private static InvocationSample Sample(object identity, short? opcode)
        {
            long Number(string name) => (long)identity.GetType().GetProperty(name)!.GetValue(identity)!;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            object? current;
            try { current = typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Sessions.DmInvocation")?.GetProperty("Current", flags)?.GetValue(null); }
            catch { current = null; }
            object? Read(object? target, string name)
            {
                try { return target?.GetType().GetProperty(name, flags)?.GetValue(target)
                    ?? target?.GetType().GetField(name, flags)?.GetValue(target); }
                catch { return null; } // Optional observation must never change wire behavior.
            }
            var purpose = Read(Read(current, "Lease"), "Purpose") as Enum;
            object? remaining = Read(Read(current, "Deadline"), "RemainingMilliseconds");
            return new InvocationSample(Number("SessionId"), Number("ExecutionId"), Number("InvocationId"), opcode,
                purpose != null && Enum.IsDefined(purpose.GetType(), purpose) ? purpose.ToString() : null,
                Read(current, "Completed") as bool?, Read(current, "IsDisposed") as bool?, remaining is int value ? value : null);
        }
        internal Task FlushAsync() { Journal.Writer.TryComplete(); return Writer; }
        public void Dispose() { foreach (var item in Saved.AsEnumerable().Reverse()) item.Field.SetValue(null, item.Previous); Journal.Writer.TryComplete(); }
    }
}
