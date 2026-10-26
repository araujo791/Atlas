# Offline AppX preview

The source now opts AppX family removal actions into ISO processing with
iso: true, including the imported NTLite AppX subset. Parent task includes also
opt in, and oobe: true preserves the online/OOBE path for those tasks.
PowerShell commands and cache-clearing actions retain their existing non-ISO
behavior.

**Requires the patched trusted-uninstaller-cli core from
codex/offline-appx-servicing.** A new APBX alone does not add this capability
to an already installed AME Wizard. The existing unpatched engine rejects
offline AppX actions. No updated Windows executable is provided here.

The patched core mounts each retained WIM image and removes inventoried
provisioned AppX packages using DISM. It verifies provisioning removal and
aborts on servicing errors, then continues the WIM/ESD rebuild.
It does not delete arbitrary component files or bypass servicing dependencies.

Build the separate preview:

    python3 scripts/import-ntlite-removals.py
    python3 scripts/build-26h2-playbook.py

Output: releases/AtlasPlaybook_v0.5.0-26300-offline-experimental.apbx
Public password: malte.

The previous AtlasPlaybook_v0.5.0-26300-experimental.apbx is retained as the
earlier Windows/OOBE artifact. The build script now outputs the offline preview;
it does not overwrite that older artifact.

Of the NTLite preset's 80 entries, 13 have mapped AppX actions, 3 are covered
by existing AppX rules, and 64 native/unknown components remain pending.
Even mapped system apps are not necessarily provisioned or removable on the
target image. No-match rules must be read as no removal. SecHealthUI rules
remain conditional on defender-disable.

Portable validation does not establish successful native DISM servicing.
Validate a disposable ISO copy in Windows, inspect every edition's inventories
and logs, measure WIM/ISO size before and after, and boot/install the result.
The complete patched core build and those tests are pending.
