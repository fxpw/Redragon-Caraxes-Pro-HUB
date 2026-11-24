# Caraxes Pro HUB

Read HANDOFF.md for verified hardware evidence, the reference source map, and
the next milestone. Keep this project in its own repository. The sibling
FURYCUBE-G9-PRO repository is a source reference.

Use the independent C# Windows backend and Vue UI architecture from G9 HUB.
Keep the device protocol behind the backend. Preserve third-party notices
when reusing code. Use model-specific capabilities and label unverified modes
and fields as such. Keep installer binaries, raw captures, and machine-local
identifiers out of version control.

Verify the exact K644 Pro device/interface and current setting readback before
adding hardware writes. For each supported write, verify the resulting value
and preserve the previous setting for restoration. Test USB first, then 2.4 GHz
and Bluetooth separately where the hardware exposes configuration. Report
fixture, Windows UI, and physical keyboard checks distinctly.
