using System;
using System.IO;
using Caraxes;

static class RgbSettingsTests
{
    static void Main()
    {
        // Nonzero synthetic values across all banks catch accidental copying of
        // only the stock utility's 136-byte prefix.
        var original = new byte[1032];
        for (int index = 8; index < original.Length; index++) original[index] = (byte)(index % 251);
        original[0] = 6;
        original[1] = 0x83;
        original[2] = 0xb6;
        original[8] = 0;
        original[14] = original[137] = 0x5a;
        original[15] = original[138] = 0xa5;
        original[21] = 9;
        original[55] = 0x45;

        var before = new RgbSettings(original);
        Check(before.Brightness == 5, "Read a stored level outside the selector range");
        foreach (int level in new[] { 0, 1, 2, 3, 4, 5 })
        {
            byte[] write = before.WithBrightness(level);
            Check(write.Length == original.Length && write[0] == 6 && write[1] == 3 && write[2] == 0xb6, "Write header");
            for (int index = 3; index < write.Length; index++)
                Check(write[index] == (index == 55 ? 0x40 | level : original[index]), "Preserve byte " + index);

            var reply = (byte[])write.Clone();
            reply[1] = 0x83;
            var after = new RgbSettings(reply);
            Check(after.Brightness == level && after.MatchesWrite(write), "Verify intended write");
            Check(before.RestorationKey("test") == after.RestorationKey("TEST"), "Undo identity ignores case and brightness");
            Check(before.RestorationKey("other") != after.RestorationKey("test"), "Undo identity distinguishes devices");
            reply[200] ^= 1;
            Check(!after.MatchesWrite(write), "Reject changed inactive profile");
            Check(before.RestorationKey("test") != after.RestorationKey("test"), "Reject stale undo after profile change");
        }

        var unsupported = (byte[])original.Clone();
        unsupported[21] = 8;
        Check(new RgbSettings(unsupported).Brightness == null, "Report an unverified effect");
        Check(!new RgbSettings(unsupported).CanRecoverLighting, "Reject recovery on an unverified effect");
        var dark = (byte[])original.Clone();
        dark[21] = 0x15;
        var recoverable = new RgbSettings(dark);
        Check(recoverable.CanRecoverLighting && !recoverable.Brightness.HasValue, "Identify verified recovery mode");
        byte[] recovery = recoverable.WithLightingMode(true);
        Check(before.MatchesWrite(recovery), "Recover only the selected lighting mode");
        Check(recoverable.MatchesWrite(before.WithLightingMode(false)), "Restore the exact original mode profile");
        Check(recoverable.LightingRecoveryKey("test") == before.LightingRecoveryKey("TEST"), "Bind recovery undo before writing");
        Check(recoverable.LightingRecoveryKey("other") != before.LightingRecoveryKey("test"), "Bind recovery undo to the exact device");
        dark[55] = 0x40;
        Check(!recoverable.CanRecoverLighting, "Withhold recovery when the saved effect is not lit");
        Reject(() => new RgbSettings(unsupported).WithBrightness(2));
        Reject(() => before.WithBrightness(6));
        Reject(() => new RgbSettings(new byte[1032]));
        Console.WriteLine("RGB settings regression checks passed.");
    }

    static void Check(bool result, string message)
    {
        if (!result) throw new Exception(message);
    }

    static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected an invalid RGB report or value to be rejected.");
    }
}
