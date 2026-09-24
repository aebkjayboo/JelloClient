namespace JelloClient.Services;

/// The current announcement pushed from the server, and a signal when it changes. The live
/// link sets it from a background thread; the launcher subscribes and shows it on the UI
/// thread. Deliberately UI-free so nothing here depends on WPF.
public static class Announcements
{
    public sealed record Notice(string Id, string Level, string Text);

    public static Notice? Current { get; private set; }

    public static event Action? Changed;

    public static void Set(string id, string level, string text)
    {
        Current = new Notice(id, level, text);
        Changed?.Invoke();
    }

    public static void Clear()
    {
        if (Current is null)
        {
            return;
        }

        Current = null;
        Changed?.Invoke();
    }
}
