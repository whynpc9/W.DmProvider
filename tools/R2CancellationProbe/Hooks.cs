using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;
using W.Dm;

internal static partial class Program
{
    private sealed record SentFrame(long SessionId, short Opcode);
    private sealed class Hooks : IDisposable
    {
        private readonly List<(FieldInfo Field, object? Previous)> Saved = [];
        internal ConcurrentQueue<SentFrame> Sent { get; } = new();
        internal ConcurrentQueue<int> Modes { get; } = new();
        internal ConcurrentQueue<short> Responses { get; } = new();
        internal Action<SentFrame>? OnSent;
        internal Action<long>? BeforeCommitAck;
        internal Hooks()
        {
            Bind("W.Dm.Internal.Legacy.A.DmWireTestHooks", "AfterFrameSent", args => {
                var frame = new SentFrame(IdentitySession(args[0]!), (short)args[1]!); Sent.Enqueue(frame); Volatile.Read(ref OnSent)?.Invoke(frame); });
            Bind("W.Dm.Internal.Legacy.A.DmWireTestHooks", "AfterStartupNegotiatedEncryptMode", args => Modes.Enqueue((int)args[0]!));
            Bind("W.Dm.Internal.Legacy.A.DmResultProtocolTrace", "AfterFrame", args => Responses.Enqueue((short)args[0]!));
            Bind("W.Dm.Internal.Transport.DmTransportTestHooks", "BeforeControlAck", args => {
                if (Convert.ToInt32(args[1]) == 0) Volatile.Read(ref BeforeCommitAck)?.Invoke(IdentitySession(args[0]!)); });
        }
        private void Bind(string type, string field, Action<object?[]> callback)
        {
            var f = typeof(DmConnection).Assembly.GetType(type)?.GetField(field, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new ProbeFailure("numeric_hook_missing");
            object? prior = f.GetValue(null); Need(prior == null, "isolated_hooks_required");
            var parameters = f.FieldType.GetMethod("Invoke")!.GetParameters().Select(p => Expression.Parameter(p.ParameterType)).ToArray();
            var body = Expression.Invoke(Expression.Constant(callback), Expression.NewArrayInit(typeof(object), parameters.Select(p => Expression.Convert(p, typeof(object)))));
            f.SetValue(null, Expression.Lambda(f.FieldType, body, parameters).Compile()); Saved.Add((f, prior));
        }
        internal long TcpCreated => ReadCounter("CreatedTcpSockets");
        internal long SendCount => ReadWireCounter("SendCount");
        private static long ReadCounter(string name) => (long)(typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Transport.DmTransportTestHooks")?
            .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? throw new ProbeFailure("transport_counter_missing"));
        private static long ReadWireCounter(string name) => (long)(typeof(DmConnection).Assembly.GetType("W.Dm.Internal.Legacy.A.DmWireTestHooks")?
            .GetProperty(name, BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) ?? throw new ProbeFailure("wire_counter_missing"));
        internal int Executions(long session) => Sent.Count(f => f.SessionId == session && f.Opcode is 6 or 13);
        internal int Commits(long session) => Sent.Count(f => f.SessionId == session && f.Opcode == 8);
        public void Dispose() { foreach (var item in Saved.AsEnumerable().Reverse()) item.Field.SetValue(null, item.Previous); }
    }
    private static long IdentitySession(object identity) => (long)identity.GetType().GetProperty("SessionId")!.GetValue(identity)!;
    private static long Session(DmConnection c)
    {
        object captured = typeof(DmConnection).GetProperty("Session", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(c)!;
        return (long)captured.GetType().GetProperty("SessionId", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(captured)!;
    }
}
