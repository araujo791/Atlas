# Experimental Windows build 26300 support

This fork adds build 26300 to the explicit playbook allowlist for compatibility
testing. Existing build entries 26100 and 26200 are retained. The title and short
description mark the changes as experimental; this is not a validated release.

Target image supplied by the user:
`26300.9807.261013-1834.26H2_GE_RELEASE_SVC_PROD3_CLIENTMULTI_X64FRE_PT-BR`,
created with UUP dump. The image has not yet been provided or inspected. Source
version 0.5.0 in this repository has not been compared with the previously
uploaded `AtlasPlaybook_v0.5.0-hotfix.apbx` archive.

## Build a test playbook with the checks retained

For the experimental artifact retained in this repository, run from the root:

```sh
python3 scripts/build-26h2-playbook.py
```

This requires Python 3 and Info-ZIP. It preserves the configuration checks,
packages the playbook with the public password, and verifies the resulting
archive contents. Output and validation limits are documented in
`releases/README.md`.

Use PowerShell and 7-Zip, from `src/playbook`:

```powershell
& ../dependencies/local-build.ps1 -DontOpenPbLocation -FileName "Atlas-26300-experimental"
```

Do not use `build-playbook.sh`, `build-playbook.cmd`, or the current APBX CI
workflow to establish supported-build validation: they pass
`-Removals WinverRequirement, Verification`, which strips the supported-build
allowlist and product verification from the packaged configuration. The direct
command above retains those fields and the existing runtime requirements.
An unsigned/local build may require the application's documented developer
testing mechanism; do not disable verification just to declare it release-ready.

## Validation still required

- Compare the source with the intended hotfix archive before replacing it.
- Record the input ISO SHA-256 and inspect WIM/ESD image metadata for build,
  revision, architecture, language, edition and index.
- With the updated AME core, process a copy of the ISO using this playbook and
  record selected feature options and all action errors.
- Check the generated image, boot it in a disposable Windows VM, finish
  installation/OOBE and verify retained apps, drivers and Windows servicing.
- Validate component CAB packages and scripts against the actual image.
  Adding a build to the allowlist does not validate component removal.
- Publish a compatibility release only after recording those results and
  building the complete Windows application successfully.

Do not replace the core's missing CAB resources with Atlas CAB packages. They
are separate resources with different identities and purposes.
