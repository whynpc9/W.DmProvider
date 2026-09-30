using System.Reflection;
using System.Runtime.CompilerServices;
using W.Dm;
using W.Dm.Internal.Legacy.A;
using W.Dm.Internal.Sessions;
using W.Dm.parser;
using Xunit;
using LegacyBuffer = W.Dm.Internal.Legacy.A.b;
using LegacyStatement = W.Dm.Internal.Legacy.A.A;

namespace W.DmProvider.SessionTests;

public sealed class WireOwnershipTests
{
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    // Deliberately named and typed from the legacy API surface. This is not a
    // source-regex mirror: missing or newly unguarded entrypoints fail review.
    private static readonly (string Name, Type[] Parameters)[] LegacyExchanges =
    [
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer)]), // negotiation
        ("a", [typeof(LegacyBuffer), typeof(LegacyBuffer)]), // login
        ("A", [typeof(LegacyStatement), typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(bool).MakeByRefType()]),
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(LegacyStatement)]),
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(LegacyStatement), typeof(string), typeof(bool), typeof(int)]),
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(LegacyStatement), typeof(string), typeof(bool), typeof(int), typeof(bool)]),
        ("A", [typeof(LegacyStatement), typeof(DmInfo)]),
        ("A", [typeof(int), typeof(LegacyStatement), typeof(DmParameterInternal[])]),
        ("A", [typeof(LegacyStatement), typeof(DmResultSetCache), typeof(short), typeof(long), typeof(long)]),
        ("__t02_method_06000A82", [typeof(LegacyBuffer), typeof(LegacyBuffer)]), // commit
        ("b", [typeof(LegacyBuffer), typeof(LegacyBuffer)]), // rollback
        ("A", [typeof(LegacyStatement)]), // statement close
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(LegacyStatement), typeof(string)]),
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(LegacyStatement), typeof(short), typeof(byte[]), typeof(int), typeof(int)]),
        ("A", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(LegacyStatement), typeof(int), typeof(byte[]), typeof(int), typeof(byte[])]),
        ("A", [typeof(LegacyStatement), typeof(short)]),
        ("A", [typeof(DmResultSetCache)]),
        ("a", [typeof(DmResultSetCache)]),
        ("A", [typeof(LegacyStatement), typeof(DmInfo), typeof(short)]),
        ("A", [typeof(LegacyStatement), typeof(List<SQLProcessor.Parameter>)]),
        ("a", [typeof(LegacyBuffer), typeof(LegacyBuffer), typeof(int)]) // session control
    ];

    private static B UninitializedWireAdapter(DmSession session)
    {
        var instance = (DmConnInstance)RuntimeHelpers.GetUninitializedObject(typeof(DmConnInstance));
        typeof(DmConnInstance).GetField("<Session>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(instance, session);
        var adapter = (B)RuntimeHelpers.GetUninitializedObject(typeof(B));
        typeof(B).GetField("__t02_field_04000ABA", BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(adapter, instance);
        return adapter;
    }

    private static object?[] DefaultArguments(ParameterInfo[] parameters) =>
        parameters.Select(p => p.ParameterType == typeof(bool).MakeByRefType() ? (object?)false :
            p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray();

    [Fact]
    public void EveryLegacyAndGenericMessageEntryRejectsMissingInvocationBeforeEncoding()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        var adapter = UninitializedWireAdapter(session);
        Assert.Equal(21, LegacyExchanges.Length);
        foreach (var (name, signature) in LegacyExchanges)
        {
            var method = typeof(B).GetMethod(name, InstanceMethods, null, signature, null);
            Assert.NotNull(method);
            var error = Assert.Throws<TargetInvocationException>(() => method!.Invoke(adapter, DefaultArguments(method.GetParameters())));
            Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Contains("Wire exchange has no current session owner", error.InnerException!.Message);
            Assert.Equal(DmPhysicalSessionState.Ready, session.State);
        }

        var generic = typeof(B).GetMethods(InstanceMethods).Single(m =>
            m.Name == "A" && m.IsGenericMethodDefinition &&
            m.GetParameters().Length == 1 &&
            m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(MSG<>));
        var closed = generic.MakeGenericMethod(typeof(int));
        var genericError = Assert.Throws<TargetInvocationException>(() => closed.Invoke(adapter, [null]));
        Assert.IsType<InvalidOperationException>(genericError.InnerException);
        Assert.Contains("Wire exchange has no current session owner", genericError.InnerException!.Message);
        Assert.Equal(DmPhysicalSessionState.Ready, session.State);
    }

    [Fact]
    public void EveryRawTransportEntryRejectsMissingExchangeBeforeSocketAccess()
    {
        var session = new DmSession();
        session.CompleteHandshakeForTests();
        var transport = (D)RuntimeHelpers.GetUninitializedObject(typeof(D));
        typeof(D).GetField("wireSession", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(transport, session);

        Assert.Throws<InvalidOperationException>(() => transport.A([], 0, 0));
        Assert.Throws<InvalidOperationException>(() => transport.__t02_method_06000A4D(null!, 0, false, false));
        Assert.Throws<InvalidOperationException>(() => transport.a([], 0, 0));
        Assert.Throws<InvalidOperationException>(() => transport.B([], 0, 0));
        Assert.Throws<InvalidOperationException>(() => transport.ConfigureReadTimeout(0));
        Assert.Throws<InvalidOperationException>(() => transport.__t02_method_06000A63());
        Assert.Equal(DmPhysicalSessionState.Ready, session.State);
    }
}
