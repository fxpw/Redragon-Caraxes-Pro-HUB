using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;

namespace Caraxes
{
    // Called serially by the desktop host on a worker thread.
    internal sealed class Keyboard
    {
        readonly string undoPath;
        readonly string lightingUndoPath;
        HidDevice device;
        RgbSettings snapshot;
        int generation;
        string issue;
        Undo previous;
        string lightingUndoKey;
        DateTime nextReadUtc;
        LightingEffect effect;
        HidDevice.Session effectSession;
        byte[] effectOriginal;

        internal Keyboard(string storage)
        {
            undoPath = Path.Combine(storage, "brightness-undo.json");
            lightingUndoPath = Path.Combine(storage, "lighting-mode-undo.txt");
            try
            {
                if (File.Exists(lightingUndoPath) && new FileInfo(lightingUndoPath).Length == 44)
                    lightingUndoKey = File.ReadAllText(lightingUndoPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (!File.Exists(undoPath)) return;
            try
            {
                if (new FileInfo(undoPath).Length > 4096) return;
                var saved = new JavaScriptSerializer().Deserialize<Undo>(File.ReadAllText(undoPath));
                if (saved != null && saved.version == 1 && saved.key != null && saved.key.Length == 44 &&
                    ValidLevel(saved.before) && ValidLevel(saved.after) && saved.before != saved.after)
                    previous = saved;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (ArgumentException) { }
            catch (InvalidOperationException) { }
        }

        static bool ValidLevel(int level) { return level >= 0 && level <= 5; }

        public sealed class Undo
        {
            public int version { get; set; }
            public string key { get; set; }
            public int before { get; set; }
            public int after { get; set; }
        }

        internal object State(bool receiverPresent)
        {
            return new {
                connection = device != null ? "usb" : issue == null ? "disconnected" : "unavailable",
                receiverPresent,
                generation,
                brightness = snapshot == null ? null : snapshot.Brightness,
                canvasAvailable = device != null && device.HasCanvas && snapshot != null && snapshot.Brightness.HasValue,
                effect = effect == null ? null : effect.Mode,
                previous = CanRestore() ? (int?)previous.before : null,
                lightingRecoveryAvailable = snapshot != null && snapshot.CanRecoverLighting,
                lightingUndoAvailable = CanUndoLightingRecovery(),
                issue = issue ?? (snapshot != null && !snapshot.Brightness.HasValue && !snapshot.CanRecoverLighting ? "unsupportedEffect" : null)
            };
        }

        internal void Scan(bool refresh, bool reconnect)
        {
            try
            {
                bool changed = UpdateConnection(reconnect);
                if (device != null && (changed || refresh || (snapshot == null && DateTime.UtcNow >= nextReadUtc)))
                {
                    nextReadUtc = DateTime.UtcNow.AddSeconds(3);
                    using (var session = device.Open()) Read(session);
                }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is InvalidDataException ||
                    error is System.ComponentModel.Win32Exception || error is InvalidOperationException)) throw;
                snapshot = null;
                issue = error.Message;
                if (refresh) throw;
            }
        }

        bool UpdateConnection(bool reconnect)
        {
            HidDevice next;
            try { next = HidDevice.Find(); }
            catch { SetDevice(null); throw; }
            string oldId = device == null ? null : device.Identity;
            string newId = next == null ? null : next.Identity;
            if (!reconnect && String.Equals(oldId, newId, StringComparison.OrdinalIgnoreCase))
            {
                if (next == null) issue = null;
                return false;
            }
            SetDevice(next);
            return true;
        }

        void SetDevice(HidDevice next)
        {
            ReleaseEffect();
            device = next;
            generation++;
            snapshot = null;
            issue = null;
        }

        void Read(HidDevice.Session session)
        {
            snapshot = null;
            try
            {
                snapshot = new RgbSettings(session.GetSettings());
                issue = null;
            }
            catch (Exception error)
            {
                issue = error.Message;
                throw;
            }
        }

        bool CanRestore()
        {
            return previous != null && device != null && snapshot != null && snapshot.Brightness == previous.after &&
                snapshot.RestorationKey(device.Identity) == previous.key;
        }

        bool CanUndoLightingRecovery()
        {
            return lightingUndoKey != null && device != null && snapshot != null && snapshot.Brightness.HasValue &&
                snapshot.LightingRecoveryKey(device.Identity) == lightingUndoKey;
        }

        internal void ChangeLightingMode(int requestedGeneration, bool restoreLighting)
        {
            Scan(false, false);
            if (device == null || generation != requestedGeneration)
                throw new InvalidOperationException("The USB connection changed. Read the keyboard again.");
            StopEffect();
            using (var session = device.Open())
            {
                Read(session);
                if (restoreLighting ? !snapshot.CanRecoverLighting : !CanUndoLightingRecovery())
                    throw new InvalidOperationException("The stored lighting mode changed. Read the keyboard again.");
                byte[] backup = snapshot.WriteReport();
                byte[] target = snapshot.WithLightingMode(restoreLighting);
                string oldUndo = lightingUndoKey;
                if (restoreLighting) PersistLightingUndo(snapshot.LightingRecoveryKey(device.Identity));
                try { WriteWithRollback(session, target, backup); }
                catch
                {
                    if (restoreLighting && issue != "rollbackUnverified") PersistLightingUndo(oldUndo);
                    throw;
                }
                if (!restoreLighting) PersistLightingUndo(null);
            }
        }

        internal void ChangeBrightness(int requestedGeneration, int? level, int? expected)
        {
            Scan(false, false);
            if (device == null || requestedGeneration != generation)
                throw new InvalidOperationException("The USB connection changed. Read the keyboard again.");
            StopEffect();

            HidDevice.Session opened;
            try { opened = device.Open(); }
            catch (Exception error)
            {
                snapshot = null;
                issue = error.Message;
                throw;
            }
            using (var session = opened)
            {
                Read(session);
                if (!snapshot.Brightness.HasValue) throw new InvalidOperationException("This RGB effect is unavailable for editing.");
                int current = snapshot.Brightness.Value;
                Undo earlier = previous;
                bool restore = !level.HasValue;
                if (level.HasValue)
                {
                    if (current != expected) throw new InvalidOperationException("Brightness changed on the keyboard. Check the current value and apply again.");
                    if (current == level.Value) return;
                    Persist(new Undo { version = 1, key = snapshot.RestorationKey(device.Identity), before = current, after = level.Value });
                }
                else
                {
                    if (!CanRestore()) throw new InvalidOperationException("The keyboard configuration changed since the last write.");
                    level = previous.before;
                }

                byte[] target = snapshot.WithBrightness(level.Value);
                byte[] backup = snapshot.WithBrightness(current);
                try { WriteWithRollback(session, target, backup); }
                catch
                {
                    if (!restore && issue != "rollbackUnverified") Persist(earlier);
                    throw;
                }
                if (restore) Persist(null);
            }
        }

        void WriteWithRollback(HidDevice.Session session, byte[] target, byte[] backup)
        {
            try { WriteAndVerify(session, target); }
            catch
            {
                // The original handles bind rollback to the same physical connection.
                try
                {
                    Read(session);
                    if (!snapshot.MatchesWrite(backup)) WriteAndVerify(session, backup);
                }
                catch
                {
                    snapshot = null;
                    issue = "rollbackUnverified";
                    throw new IOException("Write and rollback could not be verified. Read the keyboard again.");
                }
                throw;
            }
        }

        void WriteAndVerify(HidDevice.Session session, byte[] report)
        {
            session.SetSettings(report);
            Thread.Sleep(80);
            Read(session);
            if (!snapshot.MatchesWrite(report)) throw new IOException("The keyboard configuration differs from the requested write.");
        }

        internal void PreviewDrawing(int requestedGeneration, byte[] frame)
        {
            Scan(false, false);
            if (device == null || !device.HasCanvas || generation != requestedGeneration)
                throw new InvalidOperationException("The USB connection changed. Read the keyboard again.");
            StopEffect();
            using (var session = device.Open())
            {
                Read(session);
                if (!snapshot.Brightness.HasValue) throw new InvalidOperationException("Select the supported stock lighting effect before previewing.");
                byte[] original = snapshot.WithBrightness(snapshot.Brightness.Value);

                bool shown = false;
                try
                {
                    for (int count = 0; count < 20; count++)
                    {
                        session.ShowCanvas(frame);
                        shown = true;
                        Thread.Sleep(250);
                    }
                }
                finally
                {
                    if (shown)
                    {
                        // The live frame provides no color readback. Verify the
                        // stored profile before and after reactivating it.
                        RestoreLighting(session, original);
                    }
                }
            }
        }

        internal bool EffectRunning { get { return effect != null; } }

        internal void StartEffect(int requestedGeneration, string mode)
        {
            Scan(false, false);
            if (device == null || !device.HasCanvas || generation != requestedGeneration)
                throw new InvalidOperationException("The USB connection changed. Read the keyboard again.");
            StopEffect();
            effectSession = device.Open();
            try
            {
                Read(effectSession);
                if (!snapshot.Brightness.HasValue) throw new InvalidOperationException("Select Blossoming on the keyboard first.");
                effectOriginal = snapshot.WithBrightness(snapshot.Brightness.Value);
                effect = new LightingEffect(mode);
                effectSession.ShowCanvas(effect.Frame());
            }
            catch
            {
                if (effect != null) StopEffect();
                else ReleaseEffect();
                throw;
            }
        }

        internal void EffectFrame()
        {
            if (effect == null) return;
            try { effectSession.ShowCanvas(effect.Frame()); }
            catch (Exception error)
            {
                try { StopEffect(); }
                catch (Exception restore)
                {
                    issue = restore.Message;
                    throw;
                }
                issue = error.Message;
                throw;
            }
        }

        internal void StopEffect()
        {
            if (effect == null) return;
            try { RestoreLighting(effectSession, effectOriginal); }
            finally { ReleaseEffect(); }
        }

        void RestoreLighting(HidDevice.Session session, byte[] original)
        {
            Read(session);
            if (!snapshot.MatchesWrite(original)) throw new IOException("The stored settings changed during the preview.");
            WriteAndVerify(session, original);
        }

        void ReleaseEffect()
        {
            var current = effect;
            effect = null;
            try { if (current != null) current.Dispose(); }
            finally
            {
                if (effectSession != null) effectSession.Dispose();
                effectSession = null;
                effectOriginal = null;
            }
        }

        void Persist(Undo value)
        {
            if (value == null)
            {
                File.Delete(undoPath);
                previous = null;
                return;
            }
            SaveUndo(undoPath, new JavaScriptSerializer().Serialize(value));
            previous = value;
        }

        void PersistLightingUndo(string key)
        {
            if (key == null) File.Delete(lightingUndoPath);
            else SaveUndo(lightingUndoPath, key);
            lightingUndoKey = key;
        }

        static void SaveUndo(string path, string contents)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);
        }
    }
}
