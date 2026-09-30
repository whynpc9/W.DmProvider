using System.Data.Common;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

internal static class ProbeSafety
{
    internal const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    internal static object Sql(string? value) => new { chars = value?.Length, sha256 = value == null ? null : Hash(value) };
    internal static FieldInfo? StatementField(DbCommand command) => command.GetType().GetField("m_Stmt", Members);
    internal static object? Statement(DbCommand command) => StatementField(command)?.GetValue(command);
    internal static object? TransactionStatement(DbTransaction transaction) => transaction.GetType().GetProperty("Stmt", Members)?.GetValue(transaction);

    internal static DbCommand? Owner(object? statement)
    {
        if (statement == null) return null;
        var getter = statement.GetType().GetMethods(Members).FirstOrDefault(method => method.Name == "f" &&
            method.GetParameters().Length == 0 && typeof(DbCommand).IsAssignableFrom(method.ReturnType));
        if (getter != null) return getter.Invoke(statement, null) as DbCommand;
        return statement.GetType().GetFields(Members).Where(field => typeof(DbCommand).IsAssignableFrom(field.FieldType))
            .Select(field => field.GetValue(statement) as DbCommand).FirstOrDefault(value => value != null);
    }

    internal static object Snapshot(DbCommand command, DbTransaction transaction, DbDataReader? reader = null)
    {
        try
        {
            object? commandStatement = Statement(command);
            object? readerStatement = reader?.GetType().GetField("m_Statement", Members)?.GetValue(reader);
            object? statement = commandStatement ?? readerStatement;
            string holder = commandStatement != null ? "command" : readerStatement != null ? "reader" : "none";
            bool? readerOpen = reader == null ? null : !reader.IsClosed;
            object? lease = reader?.GetType().GetField("executionLease", Members)?.GetValue(reader);
            object? capturedSession = lease?.GetType().GetProperty("Session", Members)?.GetValue(lease);
            object? capturedIdentity = lease?.GetType().GetProperty("Identity", Members)?.GetValue(lease);
            object? IdentityValue(string name) => capturedIdentity?.GetType().GetProperty(name, Members)?.GetValue(capturedIdentity);
            object? physical = statement?.GetType().GetMethods(Members).FirstOrDefault(method => method.Name == "G" &&
                method.GetParameters().Length == 0)?.Invoke(statement, null);
            object? physicalSession = physical?.GetType().GetProperty("Session", Members)?.GetValue(physical);
            DbCommand? owner = Owner(statement);
            object? Invoke(string name, Type returnType) => statement?.GetType().GetMethods(Members)
                .FirstOrDefault(method => method.Name == name && method.ReturnType == returnType && method.GetParameters().Length == 0)
                ?.Invoke(statement, null);
            string? actual = Invoke("C", typeof(string)) as string;
            return new { available = StatementField(command) != null, caller_sql = Sql(command.CommandText),
                parameter_count = command.Parameters.Count, statement_present = statement != null,
                holder, reader_open = readerOpen, dual_statement_reference = commandStatement != null && readerStatement != null,
                captured_session_available = capturedSession != null,
                captured_session_matches_statement = capturedSession == null ? (bool?)null : ReferenceEquals(capturedSession, physicalSession),
                captured_operation = capturedIdentity == null ? null : new { session_id = IdentityValue("SessionId"),
                    lease_generation = IdentityValue("LeaseGeneration"), execution_id = IdentityValue("ExecutionId"),
                    invocation_id = IdentityValue("InvocationId") },
                statement_disposed = Invoke("P", typeof(bool)) as bool?,
                inherited_transaction_statement = statement != null && ReferenceEquals(statement, TransactionStatement(transaction)),
                owner_available = owner != null, owner_is_caller = owner == null ? (bool?)null : ReferenceEquals(owner, command),
                owner_sql = Sql(owner?.CommandText), actual_statement_sql = Sql(actual),
                owner_timeout = owner?.CommandTimeout, caller_timeout = command.CommandTimeout,
                owner_command_type = owner?.CommandType.ToString(), caller_command_type = command.CommandType.ToString(),
                prepared = Invoke("b", typeof(bool)) as bool? };
        }
        catch (Exception ex) { return new { available = false, failure = Failure(ex) }; }
    }

    internal static bool ClearInheritedStatement(DbCommand command, DbTransaction transaction)
    {
        FieldInfo field = StatementField(command) ?? throw new InvalidOperationException("statement_field_unavailable");
        object? statement = field.GetValue(command);
        if (statement == null || !ReferenceEquals(statement, TransactionStatement(transaction))) return false;
        field.SetValue(command, null); // Causal probe only: preserve SQL, parameters, transaction, and physical connection.
        return true;
    }

    internal static object Failure(Exception exception)
    {
        var chain = new List<object>();
        for (Exception? current = exception; current != null && chain.Count < 8; current = current.InnerException)
        {
            int? number = null;
            if (current.GetType().GetProperty("Number")?.GetValue(current) is int value) number = value;
            var frames = new StackTrace(current, true).GetFrames() ?? [];
            chain.Add(new { type = current.GetType().FullName, number,
                frames = frames.Take(10).Select(frame => new { declaring_type = frame.GetMethod()?.DeclaringType?.FullName,
                    method = frame.GetMethod()?.Name, line = frame.GetFileLineNumber() }).ToArray() });
        }
        return new { exception_chain = chain };
    }
}
