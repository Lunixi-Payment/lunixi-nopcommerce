#!/usr/bin/env python3
"""Fetch checksum-pinned native references; use the shared SDK from trusted source or an explicit package."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import tempfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
NOP_URL = "https://github.com/nopSolutions/nopCommerce/releases/download/release-4.90.8/nopCommerce_4.90.8_NoSource_linux_x64.zip"
NOP_SHA256 = "054f6c6796a627c74b06f89bc2504a32755a7c0e6fe33503132cf7e85419a1c7"
SDK_URL = "https://codeload.github.com/Lunixi-Payment/lunixi-dotnet/zip/refs/tags/v0.1.0"


def extract_safely(archive, destination):
    destination = destination.resolve()
    with zipfile.ZipFile(archive) as source:
        for item in source.infolist():
            if not (destination / item.filename).resolve().is_relative_to(destination):
                raise ValueError("Archive contains an unsafe path")
        source.extractall(destination)


def download(url, destination, expected=None):
    request = urllib.request.Request(url, headers={"User-Agent": "Lunixi-nopCommerce-build/0.1.0"})
    with urllib.request.urlopen(request, timeout=60) as response, destination.open("wb") as output:
        shutil.copyfileobj(response, output)
    digest = hashlib.sha256(destination.read_bytes()).hexdigest()
    if expected is not None and digest != expected:
        destination.unlink()
        raise ValueError("Downloaded native release checksum mismatch")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--nop-runtime", type=Path, help="Explicit trusted nopCommerce 4.90.8 NoSource directory")
    parser.add_argument("--sdk-source", type=Path, help="Trusted lunixi-dotnet 0.1.0 source checkout")
    parser.add_argument("--sdk-package", type=Path, help="Explicit trusted Lunixi.Sdk.0.1.0.nupkg instead of SDK source")
    args = parser.parse_args()
    if args.sdk_source and args.sdk_package:
        parser.error("Choose SDK source or a local SDK package")
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
    package_mode = args.sdk_package is not None
    sdk_project = None
    package_digest = None
    if package_mode:
        package = args.sdk_package.resolve()
        if package.name != "Lunixi.Sdk.0.1.0.nupkg" or not package.is_file():
            raise ValueError("Expected an explicit Lunixi.Sdk.0.1.0.nupkg")
        package_digest = hashlib.sha256(package.read_bytes()).hexdigest()
        feed = cache / "nuget"
        feed.mkdir(exist_ok=True)
        if package != feed / package.name:
            shutil.copy2(package, feed / package.name)
    else:
        sdk = args.sdk_source.resolve() if args.sdk_source else cache / "lunixi-dotnet"
        if not sdk.exists():
            # GitHub source availability is independent of NuGet.org publication.
            # A maintainer can pass --sdk-source before the GitHub version tag exists.
            archive = cache / "lunixi-dotnet-0.1.0.zip"
            download(SDK_URL, archive)
            with tempfile.TemporaryDirectory(dir=cache) as directory:
                extract_safely(archive, Path(directory))
                candidates = list(Path(directory).glob("*/src/Lunixi.Sdk/Lunixi.Sdk.csproj"))
                if len(candidates) != 1:
                    raise ValueError("Unexpected SDK source archive layout")
                shutil.copytree(candidates[0].parents[2], sdk)
        sdk_project = sdk / "src/Lunixi.Sdk/Lunixi.Sdk.csproj"
        if not sdk_project.is_file():
            raise ValueError("Shared SDK source project was not found")
    (cache / "build.json").write_text(json.dumps({"nop_runtime": str(native), "sdk_project": str(sdk_project) if sdk_project else None, "sdk_package": package_mode, "sdk_package_sha256": package_digest}, indent=2) + "\n")
    print("Native 4.90.8 references and shared SDK build input are ready. No merchant credentials were read.")


if __name__ == "__main__":
    main()
