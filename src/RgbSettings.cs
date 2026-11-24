using System;
using System.IO;
using System.Security.Cryptography;

namespace Caraxes
{
    internal sealed class RgbSettings
    {
        internal const int BrightnessMaximum = 4;
        const int PayloadOffset = 8;
        // The keyboard replaces bytes beyond the stock app's 136-byte prefix.
        // Keep the entire payload, including inactive profiles, byte-for-byte.
        const int PayloadLength = HidDevice.DataLength - PayloadOffset;
        const int EffectOffset = PayloadOffset + 13;
        const int BrightnessOffset = PayloadOffset + 47;
        const byte Blossoming = 0x09;
        const byte DarkMode = 0x15;
        readonly byte[] reply;

        internal RgbSettings(byte[] reply)
        {
            if (reply.Length != HidDevice.DataLength || reply[0] != 6 || reply[1] != 0x83 || reply[2] != 0xb6 ||
                reply[3] != 0 || reply[4] != 0 || reply[5] != 0 || reply[6] != 0 || reply[7] != 0 ||
                reply[14] != 0x5a || reply[15] != 0xa5 || reply[137] != 0x5a || reply[138] != 0xa5)
                throw new InvalidDataException("Unexpected Redragon configuration response.");
            this.reply = reply;
        }

        // Verified on USB against the board's Fn2 + minus shortcut for effect 09.
        // The stock configuration includes 0…4 and one observed raw value 5.
        internal int? Brightness
        {
            get
            {
                int raw = reply[BrightnessOffset] & 0x0f;
                return reply[PayloadOffset] == 0 && reply[EffectOffset] == Blossoming && raw <= 5 ? (int?)raw : null;
            }
        }

        internal byte[] WithBrightness(int level)
        {
            if (!Brightness.HasValue || level < 0 || level > 5)
                throw new InvalidDataException("Unsupported RGB brightness field or level.");
            var report = WriteReport();
            report[BrightnessOffset] = (byte)((reply[BrightnessOffset] & 0xf0) | level);
            return report;
        }

        // The verified device was dark in this mode and lit again after selecting Blossoming.
        internal bool CanRecoverLighting
        {
            get
            {
                int raw = reply[BrightnessOffset] & 0x0f;
                return reply[PayloadOffset] == 0 && reply[EffectOffset] == DarkMode && raw > 0 && raw <= 5;
            }
        }

        internal byte[] WithLightingMode(bool recover)
        {
            if (recover ? !CanRecoverLighting : !Brightness.HasValue)
                throw new InvalidDataException("Unsupported lighting mode change.");
            var report = WriteReport();
            report[EffectOffset] = recover ? Blossoming : DarkMode;
            return report;
        }

        internal string LightingRecoveryKey(string identity)
        {
            return Fingerprint(identity, CanRecoverLighting ? WithLightingMode(true) : WriteReport());
        }

        internal byte[] WriteReport()
        {
            var report = new byte[HidDevice.DataLength];
            report[0] = 6;
            report[1] = 3;
            report[2] = 0xb6;
            Array.Copy(reply, PayloadOffset, report, PayloadOffset, PayloadLength);
            return report;
        }

        internal bool MatchesWrite(byte[] report)
        {
            for (int index = PayloadOffset; index < PayloadOffset + PayloadLength; index++)
                if (reply[index] != report[index]) return false;
            return true;
        }

        internal string RestorationKey(string identity)
        {
            return Fingerprint(identity, WithBrightness(0));
        }

        static string Fingerprint(string identity, byte[] fingerprint)
        {
            using (var sha = SHA256.Create())
            {
                byte[] path = System.Text.Encoding.UTF8.GetBytes(identity.ToUpperInvariant());
                sha.TransformBlock(path, 0, path.Length, path, 0);
                sha.TransformFinalBlock(fingerprint, 0, fingerprint.Length);
                return Convert.ToBase64String(sha.Hash);
            }
        }
    }
}
