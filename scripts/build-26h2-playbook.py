#!/usr/bin/env python3
"""Package the experimental playbook using the documented APBX ZIP format."""
import hashlib
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.etree.ElementTree as ET
import zipfile


def main():
    root = Path(__file__).resolve().parents[1]
    source = root / "src" / "playbook"
    config = ET.parse(source / "playbook.conf").getroot()
    builds = [item.text for item in config.findall("./SupportedBuilds/string")]
    if "26300" not in builds or "experimental" not in config.findtext("Title", ""):
        raise RuntimeError("Expected explicit experimental build 26300 configuration")
    if not config.findall("./Requirements/Requirement") or config.find("ProductCode") is None:
        raise RuntimeError("Playbook requirements and product verification must be retained")
    if not shutil.which("zip"):
        raise RuntimeError("Install the zip command before building")

    output = root / "releases" / "AtlasPlaybook_v0.5.0-26300-offline-experimental.apbx"
    output.parent.mkdir(exist_ok=True)
    with tempfile.TemporaryDirectory(prefix="atlas-apbx-") as temporary:
        temporary = Path(temporary)
        staging = temporary / "playbook"
        staging.mkdir()
        for path in sorted(source.rglob("*")):
            if path.is_symlink():
                raise RuntimeError("Unexpected symbolic link: " + str(path))
            if not path.is_file():
                continue
            if path.name.startswith("local-build.") or path.suffix == ".apbx":
                continue
            relative = path.relative_to(source)
            target = staging / relative
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(path, target)

        # Match the repository builder's OEM version replacement in staging only.
        oem = staging / "Configuration/tweaks/misc/config-oem-information.yml"
        text = oem.read_text(encoding="utf-8-sig")
        if "AtlasVersionUndefined" not in text:
            raise RuntimeError("OEM version placeholder missing")
        text = text.replace("AtlasVersionUndefined", "v" + config.findtext("Version"))
        oem.write_text(text, encoding="utf-8")

        archive = temporary / "playbook.zip"
        # 'malte' is the public format password used by Atlas/AME playbooks.
        # It is not a credential or a confidentiality mechanism.
        subprocess.run(
            ["zip", "-q", "-r", "-P", "malte", str(archive), "."],
            cwd=staging, check=True,
        )
        with zipfile.ZipFile(archive) as packaged:
            packaged.setpassword(b"malte")
            if packaged.testzip() is not None:
                raise RuntimeError("Archive CRC verification failed")
            expected = {str(p.relative_to(staging)).replace("\\", "/"): p
                        for p in staging.rglob("*") if p.is_file()}
            actual = {item.filename for item in packaged.infolist() if not item.is_dir()}
            if actual != set(expected):
                raise RuntimeError("Archive file inventory does not match staging")
            for name, path in expected.items():
                info = packaged.getinfo(name)
                if not info.flag_bits & 1:
                    raise RuntimeError("Unencrypted archive entry: " + name)
                if packaged.read(name) != path.read_bytes():
                    raise RuntimeError("Archive payload mismatch: " + name)
            packaged_config = ET.fromstring(packaged.read("playbook.conf"))
            if ET.tostring(packaged_config) != ET.tostring(config):
                raise RuntimeError("Packaged configuration changed")
        shutil.copyfile(archive, output)

    digest = hashlib.sha256(output.read_bytes()).hexdigest()
    output.with_suffix(".apbx.sha256").write_text(
        digest + "  " + output.name + "\n", encoding="ascii"
    )
    print("Created:", output)
    print("Files verified:", len(expected))
    print("Bytes:", output.stat().st_size)
    print("SHA-256:", digest)


if __name__ == "__main__":
    main()
