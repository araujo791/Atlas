# Experimental Atlas playbook

`AtlasPlaybook_v0.5.0-26300-experimental.apbx` is built from this fork's source,
with build 26300 added to the allowlist and an experimental title. Existing
26100/26200 entries, requirements, options and product verification are retained.
It has not been compared with the uploaded v0.5.0-hotfix archive.

The updated AppX rules include the user's requested Copilot provider, Widgets,
Media Player and CrossDeviceExperienceHost removals. Formatting and PowerShell
escaping from the uploaded YAML were corrected; existing deprovisioning and cache
cleanup actions were retained. In the current AME core, AppX and PowerShell actions
are not supported during offline ISO mastering. These rules run in Windows/OOBE;
they do not establish that those packages were removed from the offline image.

The NTLite preset's AppX subset is included in atlas/ntlite-appx.yml. Of its 80
removal entries, 13 have newly mapped AppX actions, 3 already have AppX rules,
and 64 native or unidentified components remain pending. See
docs/ntlite-removals.md for the complete item-by-item mapping and limitations.
These are Windows/OOBE actions. SecHealthUI actions respect defender-disable.

Rebuild from the repository root with Python 3 and the Info-ZIP `zip` command:

```sh
python3 scripts/build-26h2-playbook.py
cd releases
sha256sum -c AtlasPlaybook_v0.5.0-26300-experimental.apbx.sha256
```

The builder uses a ZIP archive with the public password `malte`, as documented
for Atlas playbooks. It replaces the OEM version placeholder in a temporary
copy, preserving the source files. It verifies all archive CRCs, encryption,
file contents, file inventory and packaged XML configuration before saving.

This is an experimental test artifact, not a published or validated 26H2
release. ISO processing, Windows execution and boot/install checks remain
outstanding; see ../docs/26H2-validation.md.
