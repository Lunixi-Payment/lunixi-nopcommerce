#!/usr/bin/env python3
"""Fetch pinned native references; default to the published SDK from NuGet.org."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import urllib.request
import zipfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
SDK_VERSION = ET.parse(ROOT / "Directory.Build.props").findtext(".//LunixiSdkVersion")
if not SDK_VERSION:
    raise ValueError("Exact LunixiSdkVersion is required in Directory.Build.props")
NOP_URL = "https://github.com/nopSolutions/nopCommerce/releases/download/release-4.90.8/nopCommerce_4.90.8_NoSource_linux_x64.zip"
NOP_SHA256 = "054f6c6796a627c74b06f89bc2504a32755a7c0e6fe33503132cf7e85419a1c7"


def extract_safely(archive, destination):
    destination = destination.resolve()
    with zipfile.ZipFile(archive) as source:
        for item in source.infolist():
            if not (destination / item.filename).resolve().is_relative_to(destination):
                raise ValueError("Archive contains an unsafe path")
        source.extractall(destination)


def download(url, destination, expected=None):
    request = urllib.request.Request(url, headers={"User-Agent": "Lunixi-nopCommerce-build/1.0.0"})
    with urllib.request.urlopen(request, timeout=60) as response, destination.open("wb") as output:
        shutil.copyfileobj(response, output)
    digest = hashlib.sha256(destination.read_bytes()).hexdigest()
    if expected is not None and digest != expected:
        destination.unlink()
        raise ValueError("Downloaded native release checksum mismatch")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--nop-runtime", type=Path, help="Explicit trusted nopCommerce 4.90.8 NoSource directory")
    parser.add_argument("--sdk-source", type=Path, help=f"Trusted lunixi-dotnet {SDK_VERSION} source checkout")
    parser.add_argument("--sdk-package", type=Path, help=f"Explicit trusted Lunixi.Sdk.{SDK_VERSION}.nupkg instead of SDK source")
    args = parser.parse_args()
    if args.sdk_source and args.sdk_package:
        parser.error("Choose either an explicit SDK source or local package, not both")
    cache = ROOT / ".cache"
    cache.mkdir(exist_ok=True)
    native = args.nop_runtime.resolve() if args.nop_runtime else cache / "nop-4.90.8"
    if not native.exists():
        archive = cache / "nop-4.90.8.zip"
        download(NOP_URL, archive, NOP_SHA256)
        native.mkdir()
        extract_safely(archive, native)
    for filename in ("Nop.Core.dll", "Nop.Data.dll", "Nop.Services.dll", "Nop.Web.Framework.dll", "Nop.Web.dll"):
        if not (native / filename).is_file():
            raise ValueError("Native runtime directory lacks required nopCommerce assemblies")
    sdk_origin = "local-package" if args.sdk_package else "source" if args.sdk_source else "nuget"
    package_mode = args.sdk_source is None
    sdk_project = None
    package_digest = None
    if args.sdk_package:
        package = args.sdk_package.resolve()
        if package.name != f"Lunixi.Sdk.{SDK_VERSION}.nupkg" or not package.is_file():
            raise ValueError(f"Expected an explicit Lunixi.Sdk.{SDK_VERSION}.nupkg")
        package_digest = hashlib.sha256(package.read_bytes()).hexdigest()
        feed = cache / "nuget"
        feed.mkdir(exist_ok=True)
        if package != feed / package.name:
            shutil.copy2(package, feed / package.name)
    elif args.sdk_source:
        sdk = args.sdk_source.resolve()
        sdk_project = sdk / "src/Lunixi.Sdk/Lunixi.Sdk.csproj"
        if not sdk_project.is_file():
            raise ValueError("Shared SDK source project was not found")
        if ET.parse(sdk_project).findtext(".//Version") != SDK_VERSION:
            raise ValueError(f"Reviewed SDK source must declare version {SDK_VERSION}")
    (cache / "build.json").write_text(json.dumps({"nop_runtime": str(native), "sdk_origin": sdk_origin, "sdk_project": str(sdk_project) if sdk_project else None, "sdk_package": package_mode, "sdk_package_sha256": package_digest}, indent=2) + "\n")
    print("Native 4.90.8 references are ready; SDK input: " + sdk_origin + f". Registry restore requires the published Lunixi.Sdk {SDK_VERSION} and its reviewed package lock.")


if __name__ == "__main__":
    main()
