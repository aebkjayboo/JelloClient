using System.Diagnostics;
using JelloClient.Services;

namespace JelloClient.Roblox.Memory;

/// Reads the running client's live instance tree, the way the engine itself walks it.
///
/// This is the C# form of the walker that was worked out as a Python script: with the
/// offset table for the build, the DataModel is reached from the module's FakeDataModel
/// pointer, and every instance carries a class descriptor, a name, and a children vector.
/// Reading these instead of guessing at memory by value is what made the walk reliable.
///
/// It is read only. Nothing here changes the client; it is the safe half of the memory
/// work, and it is genuinely useful on its own - Jello can read the live game and GUI state
/// rather than inferring it from the log.
internal sealed class DataModelReader
{
    private readonly ProcessMemory _memory;
    private readonly RobloxOffsets _offsets;

    /// The underlying reader, for callers that need raw field reads beyond the tree walk.
    public ProcessMemory Memory => _memory;

    private readonly int _childrenStart;
    private readonly int _childrenEnd;
    private readonly int _classDescriptor;
    private readonly int _className;
    private readonly int _nameContainer;

    public sealed record Instance(IntPtr Address, string ClassName, string Name);

    private DataModelReader(ProcessMemory memory, RobloxOffsets offsets)
    {
        _memory = memory;
        _offsets = offsets;

        _childrenStart = (int)offsets.Get("Instance", "ChildrenStart");
        _childrenEnd = (int)offsets.Get("Instance", "ChildrenEnd");
        _classDescriptor = (int)offsets.Get("Instance", "ClassDescriptor");
        _className = (int)offsets.Get("Instance", "ClassName");
        _nameContainer = (int)offsets.Get("Instance", "NameContainer");
    }

    /// Attaches to the first running RobloxPlayerBeta, fetching the matching offsets.
    /// Returns null when Roblox is not running or its build has no published offsets.
    public static async Task<DataModelReader?> AttachAsync(CancellationToken token = default)
    {
        const string ident = "DataModelReader::Attach";

        var process = Process.GetProcessesByName("RobloxPlayerBeta").FirstOrDefault();

        if (process is null)
        {
            return null;
        }

        string version = VersionOf(process);

        if (version is "")
        {
            Log.Write(ident, "Could not tell which build Roblox is, so no offsets can be matched");
            return null;
        }

        var offsets = await RobloxOffsets.LoadAsync(version, token);

        if (offsets is null || !offsets.Has("Instance") || !offsets.Has("FakeDataModel"))
        {
            return null;
        }

        var memory = ProcessMemory.Open(process.Id);

        if (memory is null || memory.ModuleBase == IntPtr.Zero)
        {
            memory?.Dispose();
            return null;
        }

        Log.Write(ident, $"Attached to Roblox {process.Id}, build {version}");

        return new DataModelReader(memory, offsets);
    }

    /// The build id is the name of the folder the client runs from.
    private static string VersionOf(Process process)
    {
        try
        {
            string? folder = Path.GetDirectoryName(process.MainModule?.FileName);
            string name = Path.GetFileName(folder ?? "");

            return name.StartsWith("version-", StringComparison.Ordinal) ? name : "";
        }
        catch (Exception)
        {
            // MainModule throws for a process Jello cannot fully query; that is not fatal.
            return "";
        }
    }

    public IntPtr DataModel()
    {
        IntPtr fakePointer = _memory.Offset(_memory.ModuleBase, (int)_offsets.Get("FakeDataModel", "Pointer"));
        IntPtr fake = _memory.ReadPointer(fakePointer);

        return _memory.ReadPointer(_memory.Offset(fake, (int)_offsets.Get("FakeDataModel", "RealDataModel")));
    }

    /// The class name lives behind two hops: the instance points at a shared descriptor,
    /// and the descriptor points at the name string. Missing the second hop is what makes
    /// a walk come back as nothing but blanks.
    public string ClassName(IntPtr instance)
    {
        IntPtr descriptor = _memory.ReadPointer(_memory.Offset(instance, _classDescriptor));
        IntPtr namePointer = _memory.ReadPointer(_memory.Offset(descriptor, _className));

        return _memory.ReadStdString(namePointer);
    }

    public string Name(IntPtr instance) =>
        _memory.ReadStdString(_memory.ReadPointer(_memory.Offset(instance, _nameContainer)));

    /// The children are a vector of shared pointers: an object pointer then a control
    /// block, sixteen bytes each.
    public IReadOnlyList<IntPtr> Children(IntPtr instance)
    {
        IntPtr container = _memory.ReadPointer(_memory.Offset(instance, _childrenStart));

        if (container == IntPtr.Zero)
        {
            return Array.Empty<IntPtr>();
        }

        IntPtr start = _memory.ReadPointer(container);
        IntPtr end = _memory.ReadPointer(_memory.Offset(container, _childrenEnd));

        long span = end.ToInt64() - start.ToInt64();

        if (start == IntPtr.Zero || span <= 0 || span > 0x200000)
        {
            return Array.Empty<IntPtr>();
        }

        byte[] raw = _memory.Read(start, (int)span);
        var children = new List<IntPtr>();

        for (int i = 0; i + 8 <= raw.Length; i += 16)
        {
            long pointer = BitConverter.ToInt64(raw, i);

            if (pointer != 0)
            {
                children.Add(new IntPtr(pointer));
            }
        }

        return children;
    }

    public IntPtr Service(string className)
    {
        foreach (IntPtr child in Children(DataModel()))
        {
            if (ClassName(child) == className)
            {
                return child;
            }
        }

        return IntPtr.Zero;
    }

    /// Walks depth-first from a root, yielding each instance once. Bounded by depth and a
    /// visited set so a malformed tree cannot spin forever.
    public IEnumerable<(Instance Instance, int Depth)> Walk(IntPtr root, int maxDepth)
    {
        if (root == IntPtr.Zero)
        {
            yield break;
        }

        var seen = new HashSet<long>();
        var stack = new Stack<(IntPtr Node, int Depth)>();
        stack.Push((root, 0));

        while (stack.Count > 0)
        {
            var (node, depth) = stack.Pop();

            if (depth > maxDepth || !seen.Add(node.ToInt64()) || seen.Count > 500_000)
            {
                continue;
            }

            string className = ClassName(node);

            if (className.Length == 0)
            {
                continue;
            }

            yield return (new Instance(node, className, Name(node)), depth);

            var children = Children(node);

            // Pushed in reverse so the tree reads top-down when popped.
            for (int i = children.Count - 1; i >= 0; i--)
            {
                stack.Push((children[i], depth + 1));
            }
        }
    }

    public void Dispose() => _memory.Dispose();
}
