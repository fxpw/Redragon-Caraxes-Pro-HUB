using System;
using System.IO;
using System.Text;

namespace Caraxes
{
    internal sealed class KeyTestLog : IDisposable
    {
        const long MaxBytes = 512 * 1024;
        readonly string path;
        StreamWriter writer;
        bool active;

        internal KeyTestLog(string storage) { path = Path.Combine(storage, "key-test.log"); }

        internal void SetActive(bool enabled)
        {
            if (active == enabled) return;
            if (enabled)
            {
                active = true;
                Write("test opened");
            }
            else
            {
                Write("test closed");
                active = false;
                CloseWriter();
            }
        }

        internal void Write(string entry)
        {
            if (!active) return;
            try
            {
                if (writer != null && writer.BaseStream.Position >= MaxBytes) CloseWriter();
                if (writer == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (File.Exists(path) && new FileInfo(path).Length >= MaxBytes)
                    {
                        string previous = Path.ChangeExtension(path, ".previous.log");
                        File.Delete(previous);
                        File.Move(path, previous);
                    }
                    writer = new StreamWriter(path, true, new UTF8Encoding(false)) { AutoFlush = true };
                }
                writer.WriteLine("{0:O} {1}", DateTime.UtcNow, entry);
            }
            catch (IOException) { CloseWriter(); }
            catch (UnauthorizedAccessException) { CloseWriter(); }
        }

        void CloseWriter()
        {
            if (writer == null) return;
            try { writer.Dispose(); }
            catch (IOException) { }
            finally { writer = null; }
        }

        public void Dispose() { SetActive(false); }
    }
}
