#!/usr/bin/env python3
"""Build a complete runtime, never a three-file overlay. Does not contact a lab service."""
import argparse
import hashlib
import json
import pathlib
import shutil
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = pathlib.Path(__file__).resolve().parents[1]
MAIN_SHA256 = 'b5be0ad1661433eeb8647c5d7476835f4769dca9479f485936cc65a43028debf'
ASSEMBLIES = {
    'ustcori_crack.exe': ('ustcori_crack', '1.3.0.0', 'null'),
    'WebLabClient.exe': ('WebLabClient', '1.1.7507.17612', 'null'),
    'USTCORi.WebLabClient.Model.dll': ('USTCORi.WebLabClient.Model', '1.0.0.1', 'null'),
    'USTCORi.SimExp.Instruments.Component.dll': ('USTCORi.SimExp.Instruments.Component', '1.0.0.0', 'null'),
    'USTCORI.CommonLib.Security.dll': ('USTCORI.CommonLib.Security', '1.0.0.0', 'null'),
    'PackageLibrary.dll': ('PackageLibrary', '1.0.0.1', 'null'),
    'Newtonsoft.Json.dll': ('Newtonsoft.Json', '3.5.0.0', '30ad4fe6b2a6aeed'),
    'ICSharpCode.SharpZipLib.dll': ('ICSharpCode.SharpZipLib', '0.86.0.518', '1b03e6acf1164f73'),
    'Unity3DWPFConnection.dll': ('Unity3DWPFConnection', '1.0.0.0', 'null'),
    'WpfAnimatedGif.dll': ('WpfAnimatedGif', '1.4.17.0', 'null'),
}


def digest(data):
    return hashlib.sha256(data).hexdigest()


def validate_config(data):
    config = ET.fromstring(data)
    endpoints = config.findall('./system.serviceModel/client/endpoint')
    expected = {
        'http://127.0.0.1/BizService.svc',
        'http://127.0.0.1/WLService.svc',
        'http://127.0.0.1/FileTransfer.svc',
    }
    if len(endpoints) != 3 or {e.get('address') for e in endpoints} != expected:
        raise ValueError('RELEASE_CONFIG_REQUIRES_THREE_LOOPBACK_ENDPOINTS')
    return data


def build(runtime, dependencies, stage, output):
    provenance = json.loads((ROOT / 'ustocri_crack/lib/dependency-provenance.json').read_text(encoding='utf-8-sig'))
    pinned = {f['name']: f for f in provenance['files'] if f['name'] in ASSEMBLIES}
    if len(pinned) != 9:
        raise ValueError('EXPECTED_NINE_PINNED_DEPENDENCIES')
    payload = {}
    for name in ASSEMBLIES:
        source = runtime / name if name == 'ustcori_crack.exe' else dependencies / name
        data = source.read_bytes()
        expected = MAIN_SHA256 if name == 'ustcori_crack.exe' else pinned[name]['sha256']
        if digest(data) != expected:
            raise ValueError('PINNED_FILE_HASH_MISMATCH:' + name)
        if name != 'ustcori_crack.exe' and len(data) != pinned[name]['bytes']:
            raise ValueError('PINNED_FILE_SIZE_MISMATCH:' + name)
        payload[name] = data
    # Always take clean publication configs, not configs mutated by a user launch.
    for name in ('ustcori_crack.exe.config', 'WebLabClient.exe.config'):
        payload[name] = validate_config((ROOT / name).read_bytes())
    payload['check-runtime.ps1'] = (ROOT / 'build/check-runtime.ps1').read_bytes()
    payload['check-runtime.cmd'] = (
        '@echo off\r\n'
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0check-runtime.ps1" -RuntimePath "%~dp0." %*\r\n'
        'exit /b %errorlevel%\r\n'
    ).encode('ascii')
    payload['README-RUNTIME.md'] = (ROOT / 'docs/runtime.md').read_bytes()
    manifest = {'schemaVersion': 1, 'packageVersion': '1.3.0-runtime-fix',
                'runtimeRequirement': 'Windows with .NET Framework 4.8 or 4.8.1',
                'dependencyCount': 9,
                'notes': ['WpfAnimatedGif is required by WPF BAML resources.',
                          'Configs are mutable during normal launch; use -StrictPackage before first launch.'],
                'files': []}
    for name, data in sorted(payload.items()):
        entry = {'name': name, 'bytes': len(data), 'sha256': digest(data),
                 'mutable': name.endswith('.config')}
        if name in ASSEMBLIES:
            simple, version, token = ASSEMBLIES[name]
            entry['assemblyFullName'] = f'{simple}, Version={version}, Culture=neutral, PublicKeyToken={token}'
        manifest['files'].append(entry)
    payload['runtime-manifest.json'] = (json.dumps(manifest, ensure_ascii=False, indent=2) + '\n').encode('utf-8')
    if stage.exists() or output.exists():
        raise ValueError('NEW_STAGE_AND_OUTPUT_REQUIRED')
    stage.mkdir(parents=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    for name, data in payload.items():
        (stage / name).write_bytes(data)
    with zipfile.ZipFile(output, 'w', compression=zipfile.ZIP_DEFLATED, compresslevel=9) as archive:
        for name, data in sorted(payload.items()):
            info = zipfile.ZipInfo(name, date_time=(2026, 11, 20, 0, 0, 0))
            info.compress_type = zipfile.ZIP_DEFLATED
            archive.writestr(info, data)
    with zipfile.ZipFile(output) as archive:
        if set(archive.namelist()) != set(payload):
            raise ValueError('ARCHIVE_MEMBER_MISMATCH')
        for name, data in payload.items():
            if archive.read(name) != data:
                raise ValueError('ARCHIVE_ROUNDTRIP_MISMATCH:' + name)
    print(json.dumps({'passed': True, 'files': len(payload), 'dependencies': 9,
                      'exeSha256': MAIN_SHA256, 'zipBytes': output.stat().st_size,
                      'zipSha256': digest(output.read_bytes())}, ensure_ascii=False))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--runtime', type=pathlib.Path, default=ROOT)
    parser.add_argument('--dependencies', type=pathlib.Path, default=ROOT)
    parser.add_argument('--stage', type=pathlib.Path, required=True)
    parser.add_argument('--output', type=pathlib.Path, required=True)
    args = parser.parse_args()
    return build(args.runtime.resolve(), args.dependencies.resolve(), args.stage.resolve(), args.output.resolve())


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (OSError, ValueError, ET.ParseError) as error:
        print('RUNTIME_PACKAGE_FAIL:' + str(error), file=sys.stderr)
        sys.exit(1)
