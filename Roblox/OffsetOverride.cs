namespace JelloClient.Roblox;

/// Shared wording for a pasted offset table, so the two override paths - the memory
/// layout the Lab walker reads, and the FastFlag address map live flags write through -
/// judge and describe a paste the same way.
///
/// The judgement is deliberately soft: a stamped table for a different build is still
/// handed back and used, because overriding is the person's own explicit choice, but the
/// status says plainly that it looks outdated so the choice is an informed one.
internal static class OffsetOverride
{
    public sealed record Check(bool Valid, string? Version, string Message);

    public static Check Empty(string what) =>
        new(false, null, $"Empty. {what} is fetched for your build, the usual way.");

    public static Check Invalid(string reason) =>
        new(false, null, reason);

    public static Check Judge(string what, string count, string? version, string? installed)
    {
        if (version is null)
        {
            return new Check(true, null,
                $"In use: {count}. This paste carries no build stamp, so Jello cannot tell whether it matches "
                + $"{installed ?? "your client"}.");
        }

        if (installed is not null && !string.Equals(version, installed, StringComparison.OrdinalIgnoreCase))
        {
            return new Check(true, version,
                $"In use, but it looks outdated: this table is for {version}, and {installed} is installed. "
                + "Offsets from another build point at unrelated memory - update the paste, or turn the override off.");
        }

        return new Check(true, version, $"In use: {count}, stamped {version}.");
    }
}
