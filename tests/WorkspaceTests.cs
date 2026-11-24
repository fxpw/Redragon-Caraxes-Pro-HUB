using System;
using System.Collections.Generic;
using System.IO;
using Caraxes;

static class WorkspaceTests
{
    static void Main()
    {
        CheckFn(new byte[] { 3, 0x20, 0, 1 }, 250, true);
        CheckFn(new byte[] { 3, 0x20, 0, 0 }, 250, false);
        CheckFn(new byte[] { 3, 0x20, 1, 1 }, 251, true);
        CheckFn(new byte[] { 3, 0, 0, 0 }, 251, false);
        PhysicalKeys.DecodeFn(new byte[] { 3, 0x21, 0, 1 }, (key, down) => { throw new Exception("Unknown report decoded"); });

        var pixels = new Dictionary<string, object> { { "27", "#ff0000" }, { "65", "#00ff00" }, { "32", "#0000ff" }, { "250", "#ffffff" } };
        byte[] frame = CanvasFrame.Encode(pixels);
        var expected = new byte[382];
        expected[0] = 8;
        expected[1] = 0x0a;
        expected[2] = 0x7a;
        expected[3] = 1;
        expected[4 + 3 * 1] = 255;
        expected[4 + 3 * 9 + 1] = 255;
        expected[4 + 3 * 35 + 2] = 255;
        Check(Convert.ToBase64String(frame) == Convert.ToBase64String(expected), "RGB order, slots, and excluded Fn1");
        pixels["32"] = "#xyz123";
        Reject(() => CanvasFrame.Encode(pixels));

        CheckLayers();
        Console.WriteLine("Workspace report and assignment checks passed.");
    }

    static void CheckLayers()
    {
        var caps = new Dictionary<string, object> { { "87", Assignment(38, true) } };
        var second = new Dictionary<string, object> { { "key", 165 }, { "bindings", new Dictionary<string, object> { { "87", Assignment(116, false) } } } };
        var profile = new Dictionary<string, object> {
            { "base", new Dictionary<string, object> { { "65", Assignment(72, false) } } },
            { "layers", new object[] {
                new Dictionary<string, object> { { "key", 32 }, { "bindings", caps } }, second,
                new Dictionary<string, object> { { "key", 251 }, { "bindings", new Dictionary<string, object>() } },
                new Dictionary<string, object> { { "key", 9 }, { "bindings", new Dictionary<string, object> { { "87", Assignment(179, false) } } } }
            } }
        };

        using (var remap = new LayerRemap())
        {
            remap.SetProfile(LayerRemap.Parse(profile));
            var output = new List<int>();
            Func<int, bool, bool> send = (vk, preserve) => { output.Add(vk); return true; };

            Check(!remap.Process(20, true, send), "The old Caps key is available after changing the trigger");
            Check(!remap.Process(20, false, send), "Caps release passes through");
            Check(remap.Process(32, true, send), "Space activates the edited layer");
            Check(remap.Process(87, true, send) && output[0] == 38, "W resolves to ArrowUp");
            Check(remap.Process(87, false, send), "Consume the remapped key release");
            Check(remap.Process(65, true, send) && output[1] == 72, "Inherit the base assignment");
            Check(remap.Process(65, false, send), "Consume the inherited key release");
            Check(remap.Process(32, false, send) && output.Count == 2, "A layer chord does not emit Space on release");
            Check(remap.Process(32, true, send) && remap.Process(32, false, send) && output.Count == 2, "An isolated layer tap emits no Space");

            Check(remap.Process(165, true, send), "Right Alt can activate a different layer");
            Check(remap.Process(87, true, send) && output[2] == 116, "The second layer has its own action");
            Check(remap.Process(165, false, send), "Release the selected modifier");
            Check(remap.Process(87, true, send) && output.Count == 3, "Repeat stays suppressed after layer release");
            Check(remap.Process(87, false, send), "Finish the suppressed repeat");
            Check(remap.Process(9, true, send) && remap.Process(87, true, send) && output[3] == 179, "A new fourth layer emits the selected media action");
            Check(remap.Process(87, false, send) && remap.Process(9, false, send), "New layer releases are paired");
            remap.FnChanged(251, true);
            Check(!remap.Process(65, true, send) && output.Count == 4, "Stock Fn commands bypass Windows remapping");
            Check(remap.Process(32, true, send) && remap.Process(32, false, send) && output.Count == 4,
                "A Windows-visible layer key stays reserved during a hardware Fn layer");
            remap.FnChanged(251, false);
            Check(!remap.Process(65, false, send), "Release a key that passed through");
            Check(remap.Process(65, true, send) && output[4] == 72, "Base mapping resumes after physical Fn release");
            remap.SetProfile(null);
            Check(remap.Process(65, false, send), "Disabling the profile preserves the captured key pair");
            remap.SetProfile(LayerRemap.Parse(profile));
            Check(remap.Process(32, true, send), "Capture a modifier before loss of test scope");
            remap.SetProfile(null);
            Check(remap.Process(32, false, send) && output.Count == 5, "Release a layer key silently after loss of focus");
        }

        second["key"] = 32;
        Reject(() => LayerRemap.Parse(profile));
        second["key"] = 0;
        caps["20"] = Assignment(38, true);
        Reject(() => LayerRemap.Parse(profile));
    }

    static object Assignment(int key, bool preserve)
    {
        return new Dictionary<string, object> { { "key", key }, { "preserve", preserve } };
    }

    static void CheckFn(byte[] packet, int key, bool down)
    {
        int events = 0;
        PhysicalKeys.DecodeFn(packet, (vk, held) => { events++; Check(vk == key && held == down, "Fn event"); });
        Check(events == 1, "One event per verified Fn report");
    }

    static void Check(bool result, string message)
    {
        if (!result) throw new Exception(message);
    }

    static void Reject(Action action)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new Exception("Expected invalid input to be rejected.");
    }
}
