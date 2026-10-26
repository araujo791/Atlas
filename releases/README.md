# Experimental Atlas playbooks

The latest source builds AtlasPlaybook_v0.5.0-26300-offline-experimental.apbx.
It opts AppX family removals into offline processing and requires the patched
trusted-uninstaller-cli core from branch codex/offline-appx-servicing.
A new APBX alone cannot add offline servicing to an installed AME Wizard.
No updated Windows executable is included here; native testing is pending.

The earlier AtlasPlaybook_v0.5.0-26300-experimental.apbx is retained unchanged
as the previous Windows/OOBE artifact. It is not rebuilt by the current script.
Neither artifact is a validated 26H2 compatibility release.

Both are based on this fork's 0.5.0 source, not the unavailable hotfix attachment.
The 26100/26200/26300 allowlist, existing requirements, options and product
verification are retained. The source and new artifact are labeled experimental.

The AppX rules include the user's requested Copilot provider, Widgets,
Media Player and CrossDeviceExperienceHost actions. CrossDeviceExperienceHost
uses PowerShell and remains Windows/OOBE only; offline provisioned AppX rules
use the patched core's DISM phase. Native components and non-provisioned system
apps are not made removable by an AppX pattern.

Of the NTLite preset's 80 entries, 13 have mapped AppX actions, 3 have existing
AppX rules, and 64 native/unknown component mappings remain pending.
SecHealthUI rules respect defender-disable. See docs/ntlite-removals.md and
docs/offline-appx.md for the per-item report and runtime requirements.

Build and verify the new preview from the repository root:

    python3 scripts/import-ntlite-removals.py
    python3 scripts/build-26h2-playbook.py
    cd releases
    sha256sum -c AtlasPlaybook_v0.5.0-26300-offline-experimental.apbx.sha256

The builder requires Python 3 and Info-ZIP. It uses the public APBX password
malte, substitutes the OEM version in a temporary copy, and verifies every
archive CRC, encrypted entry, file payload, inventory and packaged configuration.
ISO processing, native DISM, measured size reduction and Windows boot/install
validation are still outstanding. See docs/26H2-validation.md.
