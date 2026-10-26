#!/usr/bin/env python3
"""Translate the reviewed AppX subset; record other NTLite items as pending."""
import json
from pathlib import Path
import re


# NTLite identifiers are not DISM/CBS identities. Do not guess component names.
APPX = {
    "bingsearch": ["Microsoft.BingSearch*"],
    "calculator": ["Microsoft.WindowsCalculator*"],
    "copilotpwa": ["Microsoft.Copilot*"],
    "microsoft.asynctextservice": ["Microsoft.AsyncTextService*"],
    "microsoft.bioenrollment": ["Microsoft.BioEnrollment*"],
    "microsoft.ecapp": ["Microsoft.ECApp*"],
    "microsoft.sechealthui": ["Microsoft.SecHealthUI*", "Microsoft.Windows.SecHealthUI*"],
    "microsoft.services.store.engagement": ["Microsoft.Services.Store.Engagement*"],
    "microsoft.windows.apprep.chxapp": ["Microsoft.Windows.Apprep.ChxApp*"],
    "microsoft.windows.narratorquickstart": ["Microsoft.Windows.NarratorQuickStart*"],
    "microsoft.windows.parentalcontrols": ["Microsoft.Windows.ParentalControls*"],
    "microsoft.windows.secureassessmentbrowser": ["Microsoft.Windows.SecureAssessmentBrowser*"],
    "microsoft.xboxgamecallableui": ["Microsoft.XboxGameCallableUI*"],
    "microsoftwindows.client.aix": ["MicrosoftWindows.Client.AIX*"],
    "outlookpwa": ["Microsoft.OutlookForWindows*"],
    "widgetsplatformruntimeapp": ["Microsoft.WidgetsPlatformRuntime*"],
}


def main():
    root = Path(__file__).resolve().parents[1]
    source = json.loads((root / "config/ntlite-removals.json").read_text(encoding="utf-8"))
    removals = source["removals"]
    ids = [item["id"] for item in removals]
    if len(ids) != len(set(ids)):
        raise ValueError("Duplicate NTLite identifiers")
    existing_text = (root / "src/playbook/Configuration/atlas/appx.yml").read_text(encoding="utf-8")
    existing = set(re.findall(r"!appx:\s*\{[^\n]*?name:\s*'([^']+)'", existing_text))
    lines = [
        "---",
        "title: NTLite AppX removals",
        "description: Experimental AppX subset of the NTLite preset; offline provisioned AppX with patched core; Windows/OOBE fallback",
        "onUpgrade: false",
        "iso: true",
        "actions:",
        "  - !writeStatus: {status: 'Applying imported NTLite AppX removals'}",
    ]
    rows = []
    mapped = covered = pending = 0
    patterns = set()
    for item in removals:
        ident = item["id"]
        names = APPX.get(ident)
        if names is None:
            pending += 1
            status = "Pending: no verified component mapping"
            detail = "Requires image inventory and a component-specific implementation"
        else:
            missing = [name for name in names if name not in existing]
            if missing:
                mapped += 1
                status = "AppX actions added (offline with patched core; untested)"
                lines.append("  # NTLite: " + ident)
                for name in missing:
                    if name in patterns:
                        continue
                    patterns.add(name)
                    option = ", option: 'defender-disable'" if ident == "microsoft.sechealthui" else ""
                    lines.append("  - !appx: {name: '" + name + "', type: family, iso: true, oobe: true" + option + "}")
            else:
                covered += 1
                status = "Existing AppX rule (offline with patched core; untested)"
            detail = ", ".join(names)
            if ident == "microsoft.sechealthui":
                detail += "; only with the existing Disable Defender option"
        rows.append("| " + ident + " | " + item["label"].replace("|", "/") +
                    " | " + status + " | " + detail + " |")

    target = root / "src/playbook/Configuration/atlas/ntlite-appx.yml"
    target.write_text("\n".join(lines) + "\n", encoding="utf-8")
    report = [
        "# Imported NTLite removal preset",
        "",
        f"Only the {len(removals)} RemoveComponents entries were imported. Registry edits, unattended",
        "setup, commands, feature enables and other preset settings were not imported.",
        "",
        "Source XML SHA-256: " + source["source_sha256"],
        "",
        f"Entries: {len(removals)}. Newly mapped AppX entries: {mapped}; already covered",
        f"AppX entries: {covered}; pending native/unknown component mappings: {pending}.",
        f"New package patterns: {len(patterns)}.",
        "",
        "The executable subset is Configuration/atlas/ntlite-appx.yml, included",
        "inside the existing AppX task after its inventory snapshot and before",
        "deprovisioning cleanup. Offline AppX processing requires the patched AME core. Online/OOBE actions remain for",
        "packages not handled by provisioning. Package identities still require inventory",
        "and execution against build 26300.9807. A mapping does not prove removal.",
        "System apps may reject uninstallation; inspect all action logs.",
        "",
        "This preset requests removals affecting accessibility, Hello, security,",
        "recovery, backup and servicing. Native component removals remain pending;",
        "they are not replaced by file deletions or service disabling. SecHealthUI",
        "removal is conditional on the existing defender-disable choice.",
        "",
        "No full NTLite-equivalent conversion or measured image reduction is claimed.",
        "Regenerate with: python3 scripts/import-ntlite-removals.py",
        "",
        "| NTLite ID | Label | Status | Mapping / next step |",
        "| --- | --- | --- | --- |",
        *rows,
        "",
    ]
    (root / "docs/ntlite-removals.md").write_text("\n".join(report), encoding="utf-8")
    print(f"{len(removals)} entries: {mapped} added, {covered} covered, {pending} pending; {len(patterns)} new patterns")


if __name__ == "__main__":
    main()
