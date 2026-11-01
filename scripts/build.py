#!/usr/bin/env python3
"""Build an installable plugin ZIP with a private, fully bundled SDK vendor context."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SDK_VERSION = ET.parse(ROOT / "Directory.Build.props").findtext(".//LunixiSdkVersion")
if not SDK_VERSION:
    raise ValueError("Exact LunixiSdkVersion is required in Directory.Build.props")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--dotnet", default=os.environ.get("DOTNET_EXE", "dotnet"))
    parser.add_argument("--restore", action="store_true", help="Restore pinned dependencies before building")
    args = parser.parse_args()
    config = json.loads((ROOT / ".cache/build.json").read_text())
    env = os.environ.copy()
    for name in ("LunixiSdkProject", "UseLunixiSdkPackage", "LunixiSdkLocalFeed"):
        env.pop(name, None)
    env["NOP_RUNTIME_PATH"] = config["nop_runtime"]
    env["LUNIXI_SDK_PROJECT"] = config["sdk_project"] or ""
    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    origin = config.get("sdk_origin")
    if origin not in ("nuget", "source", "local-package"):
        raise ValueError("Run bootstrap again to select the SDK source explicitly")
    if origin == "local-package":
        artifact = ROOT / f".cache/nuget/Lunixi.Sdk.{SDK_VERSION}.nupkg"
        if hashlib.sha256(artifact.read_bytes()).hexdigest() != config["sdk_package_sha256"]:
            raise ValueError("Explicit SDK artifact changed; bootstrap the reviewed artifact again")
        # Keep same-version local artifacts isolated from an unrelated global NuGet cache.
        env["NUGET_PACKAGES"] = str(ROOT / ".cache/package-mode-packages")
        env["LunixiSdkLocalFeed"] = str(ROOT / ".cache/nuget")
    elif origin == "nuget":
        env["NUGET_PACKAGES"] = env.get("NUGET_PACKAGES") or str(ROOT / ".cache/registry-mode-packages")
    properties = ["-p:UseLunixiSdkPackage=" + str(config["sdk_package"]).lower()]
    project = "src/Lunixi.NopCommerce.Gateway/Lunixi.NopCommerce.Gateway.csproj"
    if args.restore:
        subprocess.run([args.dotnet, "restore", project, *( ["--force-evaluate"] if origin == "local-package" else ["--locked-mode"] ), *properties], cwd=ROOT, env=env, check=True)
    subprocess.run([args.dotnet, "build", project, "-c", "Release", "--no-restore", *properties], cwd=ROOT, env=env, check=True)
    artifacts = ROOT / "artifacts"
    staging = artifacts / "staging"
    if staging.exists():
        shutil.rmtree(staging)
    destination = staging / "Plugins/Payments.Lunixi"
    vendor = destination / "Vendor"
    vendor.mkdir(parents=True)
    native_output = ROOT / "src/Nop.Plugin.Payments.Lunixi/bin/Release/net9.0"
    bridge_output = ROOT / "src/Lunixi.NopCommerce.Gateway/bin/Release/net9.0"
    shutil.copy2(native_output / "Nop.Plugin.Payments.Lunixi.dll", destination)
    source = ROOT / "src/Nop.Plugin.Payments.Lunixi"
    for filename in ("plugin.json", "logo.png"):
        shutil.copy2(source / filename, destination)
    for directory in ("Assets", "Views"):
        shutil.copytree(source / directory, destination / directory)
    for filename in ("Lunixi.NopCommerce.Gateway.dll", "Lunixi.NopCommerce.Gateway.deps.json", "Lunixi.Sdk.dll", "NSec.Cryptography.dll"):
        shutil.copy2(bridge_output / filename, vendor)
    shutil.copytree(bridge_output / "runtimes", vendor / "runtimes")
    notices = destination / "ThirdPartyNotices"
    notices.mkdir()
    packages = Path(env.get("NUGET_PACKAGES", str(Path.home() / ".nuget/packages")))
    for name, version in (("nsec.cryptography", "25.4.0"), ("libsodium", "1.0.20.1")):
        for filename in ("LICENSE", "NOTICE", "AUTHORS"):
            candidate = packages / name / version / filename
            if candidate.exists():
                shutil.copy2(candidate, notices / (name + "-" + filename))
    for filename in ("LICENSE", "README.md", "CHANGELOG.md", "SECURITY.md"):
        shutil.copy2(ROOT / filename, destination)
    readme = destination / "README.md"
    readme.write_text(readme.read_text().replace("src/Nop.Plugin.Payments.Lunixi/Assets/", "Assets/"))
    shutil.copytree(ROOT / "docs", destination / "docs")
    if config["sdk_project"]:
        shutil.copy2(Path(config["sdk_project"]).parents[2] / "LICENSE", notices / "Lunixi.Sdk-LICENSE")
    else:
        shutil.copy2(packages / f"lunixi.sdk/{SDK_VERSION}/LICENSE", notices / "Lunixi.Sdk-LICENSE")
    metadata = json.loads((destination / "plugin.json").read_text())
    if metadata["SystemName"] != "Payments.Lunixi" or metadata["Version"] != "1.0.0" or metadata["SupportedVersions"] != ["4.90"]:
        raise ValueError("Plugin manifest drift")
    target = artifacts / "Lunixi.Payments.nopCommerce.4.90-1.0.0.zip"
    with zipfile.ZipFile(target, "w", zipfile.ZIP_DEFLATED) as archive:
        for file in sorted(destination.rglob("*")):
            if file.is_file():
                archive.write(file, "Payments.Lunixi/" + file.relative_to(destination).as_posix())
    digest = hashlib.sha256(target.read_bytes()).hexdigest()
    (artifacts / (target.name + ".sha256")).write_text(digest + "  " + target.name + "\n")
    print("Built " + str(target.relative_to(ROOT)) + "; merchant installation requires no NuGet restore or .NET SDK.")


if __name__ == "__main__":
    main()
