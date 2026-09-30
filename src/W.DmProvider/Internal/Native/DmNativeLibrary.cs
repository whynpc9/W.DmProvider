using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace W.Dm.Internal.Native;

internal interface IDmNativeLibraryBackend
{
    nint Load(string absolutePath);
    bool TryGetExport(nint library, string name, out nint address);
    void Free(nint library);
}

internal sealed class DmNativeLibrary : IDisposable
{
    private readonly LibraryHandle handle;
    private readonly Dictionary<string, nint> exports;

    private DmNativeLibrary(LibraryHandle handle, Dictionary<string, nint> exports)
    {
        this.handle = handle;
        this.exports = exports;
    }

    // The path must be supplied by a trusted deployment setting. No search of
    // the working directory, DM_HOME, server data, or platform library paths.
    internal static DmNativeLibrary OpenTrustedAbsolute(string absolutePath,
        IReadOnlyCollection<string> requiredExports, IDmNativeLibraryBackend backend = null)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathFullyQualified(absolutePath) ||
            !string.Equals(Path.GetFullPath(absolutePath), absolutePath, StringComparison.Ordinal))
            throw new ArgumentException("Native library requires a canonical absolute path.", nameof(absolutePath));
        if (requiredExports == null || requiredExports.Count == 0)
            throw new ArgumentException("Required native exports must be declared.", nameof(requiredExports));
        backend ??= SystemBackend.Instance;
        nint loaded;
        try { loaded = backend.Load(absolutePath); }
        catch (BadImageFormatException)
        {
            throw new NotSupportedException("Native library architecture is incompatible with this runtime.");
        }
        catch (DllNotFoundException)
        {
            throw new NotSupportedException("Native library is unavailable for this runtime.");
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new NotSupportedException("Native library could not be loaded for this runtime.");
        }
        if (loaded == 0 || loaded == -1) throw new NotSupportedException("Native library is unavailable or incompatible with this runtime.");
        var owned = new LibraryHandle(loaded, backend);
        try
        {
            var symbols = new Dictionary<string, nint>(StringComparer.Ordinal);
            foreach (string name in requiredExports)
            {
                if (string.IsNullOrWhiteSpace(name) || !IsSymbolName(name))
                    throw new ArgumentException("Invalid native export name.", nameof(requiredExports));
                bool found;
                nint address;
                try { found = backend.TryGetExport(owned.DangerousGetHandle(), name, out address); }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    throw new NotSupportedException("Required native export is unavailable.");
                }
                if (!found || address == 0) throw new NotSupportedException("Required native export is unavailable.");
                symbols.Add(name, address);
            }
            return new DmNativeLibrary(owned, symbols);
        }
        catch
        {
            owned.Dispose();
            throw;
        }
    }

    private static bool IsSymbolName(string name)
    {
        foreach (char value in name)
            if (!(value is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '_')) return false;
        return true;
    }

    // A caller must complete the native call inside this callback. Retaining
    // the address after the callback would outlive the SafeHandle lease.
    internal TResult WithExport<TResult>(string name, Func<nint, TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        bool added = false;
        try
        {
            handle.DangerousAddRef(ref added);
            if (!exports.TryGetValue(name, out nint address))
                throw new NotSupportedException("Native export was not declared.");
            return callback(address);
        }
        finally
        {
            if (added) handle.DangerousRelease();
        }
    }

    public void Dispose() => handle.Dispose();

    private sealed class LibraryHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private readonly IDmNativeLibraryBackend backend;
        internal LibraryHandle(nint value, IDmNativeLibraryBackend backend) : base(true)
        {
            this.backend = backend;
            SetHandle(value);
        }
        protected override bool ReleaseHandle()
        {
            try { backend.Free(handle); return true; }
            catch { return false; }
        }
    }

    private sealed class SystemBackend : IDmNativeLibraryBackend
    {
        internal static readonly SystemBackend Instance = new();
        public nint Load(string absolutePath) => NativeLibrary.Load(absolutePath);
        public bool TryGetExport(nint library, string name, out nint address) => NativeLibrary.TryGetExport(library, name, out address);
        public void Free(nint library) => NativeLibrary.Free(library);
    }
}
