#!/usr/bin/env python3
"""Regression: restore a public source export with no pre-existing cache/feed/assets."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
IGNORED = shutil.ignore_patterns('.git', '.cache', 'artifacts', 'bin', 'obj', 'TestResults', '__pycache__')


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--nop-runtime', type=Path, required=True)
    parser.add_argument('--sdk-source', type=Path, required=True)
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    with tempfile.TemporaryDirectory(prefix='lunixi-nop-clean-') as temporary:
        sandbox = Path(temporary)
        plugin = sandbox / 'plugin'
        sdk = sandbox / 'sdk'
        shutil.copytree(ROOT, plugin, ignore=IGNORED)
        shutil.copytree(args.sdk_source.resolve(), sdk, ignore=IGNORED)
        if (plugin / '.cache').exists():
            raise RuntimeError('Regression fixture must begin without any local feed/cache')
        subprocess.run(['python3', 'scripts/bootstrap.py', '--nop-runtime', str(args.nop_runtime.resolve()),
                        '--sdk-source', str(sdk)], cwd=plugin, check=True)
        if not (plugin / '.cache/nuget').is_dir():
            raise RuntimeError('Source-mode bootstrap did not initialize its configured local feed')
        env = os.environ.copy()
        env['NOP_RUNTIME_PATH'] = str(args.nop_runtime.resolve())
        env['LUNIXI_SDK_PROJECT'] = str(sdk / 'src/Lunixi.Sdk/Lunixi.Sdk.csproj')
        env['NUGET_PACKAGES'] = str(sandbox / 'empty-package-cache')
        env['DOTNET_CLI_TELEMETRY_OPTOUT'] = '1'
        subprocess.run([args.dotnet, 'restore', 'tests/Lunixi.NopCommerce.Tests/Lunixi.NopCommerce.Tests.csproj',
                        '--locked-mode'], cwd=plugin, env=env, check=True)
    print('Clean-export locked restore passed with an initially empty package cache and no pre-existing local feed.')


if __name__ == '__main__':
    main()
