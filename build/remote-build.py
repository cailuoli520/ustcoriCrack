#!/usr/bin/env python3
"""Build the checked-in C# sources against genuine .NET Framework references."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import xml.etree.ElementTree as ET
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = Path(os.environ.get("USTCORI_BUILD_ROOT", "/opt/dsh-build/ustcoriCrack"))
PROJECT = ROOT / "ustocri_crack" / "ustcori_crack.csproj"
NS = {"m": "http://schemas.microsoft.com/developer/msbuild/2003"}

def sha256(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()

def is_managed_reference(path):
    """Require an assembly manifest, not just CLR metadata in a netmodule."""
    try:
        data = path.read_bytes()
        if data[:2] != b"MZ":
            return False
        pe = struct.unpack_from("<I", data, 0x3c)[0]
        if data[pe:pe + 4] != b"PE\0\0":
            return False
        sections = struct.unpack_from("<H", data, pe + 6)[0]
        optional_size = struct.unpack_from("<H", data, pe + 20)[0]
        optional = pe + 24
        magic = struct.unpack_from("<H", data, optional)[0]
        if magic not in (0x10b, 0x20b):
            return False
        directories = optional + (96 if magic == 0x10b else 112)
        clr_rva, clr_size = struct.unpack_from("<II", data, directories + 14 * 8)
        if not clr_rva or clr_size < 72:
            return False
        def rva_offset(rva):
            for i in range(sections):
                at = optional + optional_size + i * 40
                size, address, raw_size, raw_offset = struct.unpack_from("<IIII", data, at + 8)
                if address <= rva < address + max(size, raw_size):
                    return raw_offset + rva - address
            raise ValueError("REFERENCE_RVA_OUTSIDE_SECTIONS")
        clr = rva_offset(clr_rva)
        metadata = rva_offset(struct.unpack_from("<I", data, clr + 8)[0])
        if data[metadata:metadata + 4] != b"BSJB":
            return False
        version_length = struct.unpack_from("<I", data, metadata + 12)[0]
        at = (metadata + 16 + version_length + 3) & ~3
        stream_count = struct.unpack_from("<H", data, at + 2)[0]
        at += 4
        for _ in range(stream_count):
            offset = struct.unpack_from("<I", data, at)[0]
            name_start = at + 8
            name_end = data.index(0, name_start)
            name = data[name_start:name_end]
            at = (name_end + 1 + 3) & ~3
            if name in (b"#~", b"#-"):
                tables = metadata + offset
                valid = struct.unpack_from("<Q", data, tables + 8)[0]
                if not valid & (1 << 32):
                    return False
                preceding = (valid & ((1 << 32) - 1)).bit_count()
                return struct.unpack_from("<I", data, tables + 24 + preceding * 4)[0] > 0
        return False
    except (OSError, struct.error, IndexError, ValueError):
        return False

def pe_info(path):
    data = path.read_bytes()
    if data[:2] != b"MZ":
        raise ValueError("OUTPUT_NOT_A_WINDOWS_PE")
    pe = struct.unpack_from("<I", data, 0x3c)[0]
    if data[pe:pe + 4] != b"PE\0\0":
        raise ValueError("INVALID_PE_SIGNATURE")
    machine, sections = struct.unpack_from("<HH", data, pe + 4)
    optional_size = struct.unpack_from("<H", data, pe + 20)[0]
    optional = pe + 24
    magic = struct.unpack_from("<H", data, optional)[0]
    directories = optional + (96 if magic == 0x10b else 112)
    clr_rva, clr_size = struct.unpack_from("<II", data, directories + 14 * 8)
    if not clr_rva or clr_size < 72:
        raise ValueError("OUTPUT_HAS_NO_CLR_HEADER")
    def rva_offset(rva):
        section_headers = optional + optional_size
        for i in range(sections):
            at = section_headers + i * 40
            virtual_size, virtual_address, raw_size, raw_offset = struct.unpack_from("<IIII", data, at + 8)
            if virtual_address <= rva < virtual_address + max(virtual_size, raw_size):
                return raw_offset + rva - virtual_address
        raise ValueError("PE_RVA_OUTSIDE_SECTIONS")
    clr = rva_offset(clr_rva)
    metadata_rva = struct.unpack_from("<I", data, clr + 8)[0]
    flags, entry_token = struct.unpack_from("<II", data, clr + 16)
    metadata = rva_offset(metadata_rva)
    if data[metadata:metadata + 4] != b"BSJB":
        raise ValueError("INVALID_CLR_METADATA_SIGNATURE")
    version_length = struct.unpack_from("<I", data, metadata + 12)[0]
    metadata_version = data[metadata + 16:metadata + 16 + version_length].rstrip(b"\0").decode("utf-8")
    if magic != 0x10b or machine != 0x014c:
        raise ValueError("OUTPUT_IS_NOT_ANYCPU_PE32")
    if not (flags & 1) or flags & (2 | 0x10 | 0x20000):
        raise ValueError("OUTPUT_IS_NOT_ANYCPU_MANAGED_EXECUTABLE")
    if (entry_token >> 24) != 0x06 or not (entry_token & 0x00ffffff):
        raise ValueError("OUTPUT_ENTRYPOINT_IS_NOT_A_METHODDEF")
    return {"machine": hex(machine), "clr_flags": hex(flags), "entry_point_token": hex(entry_token), "metadata_version": metadata_version, "platform": "Windows AnyCPU"}

def main():
    tree = ET.parse(PROJECT)
    target = tree.findtext("m:PropertyGroup/m:TargetFrameworkVersion", namespaces=NS)
    name = tree.findtext("m:PropertyGroup/m:AssemblyName", namespaces=NS)
    if target != "v4.8":
        raise SystemExit("REFERENCE_PACK_DOES_NOT_MATCH_PROJECT_TARGET: " + str(target))
    project_dir = PROJECT.parent
    sources = [project_dir / n.attrib["Include"].replace("\\", "/") for n in tree.findall("m:ItemGroup/m:Compile", NS)]
    dependencies = []
    for ref in tree.findall("m:ItemGroup/m:Reference", NS):
        hint = ref.findtext("m:HintPath", namespaces=NS)
        if hint:
            dependencies.append(project_dir / hint.replace("\\", "/"))
    missing = [str(p.relative_to(ROOT)) for p in sources + dependencies if not p.is_file()]
    if missing:
        print(json.dumps({"missing_real_inputs": missing}, ensure_ascii=False, indent=2))
        return 2
    ref_root = BASE / "tools/netfx48/build/.NETFramework/v4.8"
    framework_refs = sorted(p for p in ref_root.glob("*.dll") if is_managed_reference(p))
    dotnet = BASE / "tools/dotnet/dotnet"
    compilers = sorted((BASE / "tools/dotnet/sdk").glob("*/Roslyn/bincore/csc.dll"))
    if not framework_refs or not dotnet.is_file() or not compilers:
        raise SystemExit("REMOTE_TOOLCHAIN_INCOMPLETE")
    compiler = BASE / "tools/dotnet/sdk/10.0.404/Roslyn/bincore/csc.dll"
    if not compiler.is_file():
        raise SystemExit("VERIFIED_SDK_10_0_404_NOT_FOUND")
    output = project_dir / "bin/Release"
    output.mkdir(parents=True, exist_ok=True)
    obj = project_dir / "obj/remote"
    obj.mkdir(parents=True, exist_ok=True)
    attributes = obj / ".NETFramework,Version=v4.8.AssemblyAttributes.cs"
    attributes.write_text('[assembly: global::System.Runtime.Versioning.TargetFrameworkAttribute(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]\n', encoding="utf-8")
    exe = output / (name + ".exe")
    args = ["/nostdlib+", "/target:exe", "/main:ustcori_crack.Program", "/platform:anycpu", "/langversion:7.3", "/codepage:65001", "/optimize+", "/debug-", "/deterministic+", "/warn:4", "/filealign:512", "/define:TRACE", "/utf8output", '/out:"' + str(exe) + '"', '/win32icon:"' + str(project_dir / "favicon.ico") + '"']
    references = framework_refs + dependencies
    # Include genuine additional client assemblies if supplied in lib.
    for extra in sorted((project_dir / "lib").glob("*.dll")):
        if extra not in references and is_managed_reference(extra):
            references.append(extra)
    args += ['/reference:"' + str(p) + '"' for p in references]
    args += ['"' + str(p) + '"' for p in sources + [attributes]]
    response = obj / "release.rsp"
    response.write_text("\n".join(args) + "\n", encoding="utf-8")
    env = dict(os.environ, DOTNET_ROOT=str(dotnet.parent), DOTNET_CLI_HOME=str(BASE / "dotnet-home"), DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1", DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_NOLOGO="1", DOTNET_SYSTEM_GLOBALIZATION_INVARIANT="1")
    result = subprocess.run([str(dotnet), "exec", str(compiler), "/noconfig", "@" + str(response)], cwd=project_dir, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    log = obj / "build.log"
    log.write_text(result.stdout, encoding="utf-8")
    print(result.stdout, end="")
    if result.returncode:
        return result.returncode
    if not exe.is_file():
        raise SystemExit("COMPILER_RETURNED_SUCCESS_WITHOUT_OUTPUT")
    shutil.copy2(project_dir / "app.config", output / (name + ".exe.config"))
    runtime_inputs = sorted((project_dir / "lib").glob("*.dll")) + sorted((project_dir / "lib").glob("*.exe"))
    runtime_configs = sorted((project_dir / "lib").glob("*.exe.config"))
    for dependency in runtime_inputs + runtime_configs:
        shutil.copy2(dependency, output / dependency.name)
    # Keep the two derived runtime configurations consistent; vendor input stays intact.
    if (output / "WebLabClient.exe.config").is_file():
        shutil.copy2(project_dir / "app.config", output / "WebLabClient.exe.config")
    manifest = {"target_framework": ".NETFramework,Version=v4.8", "configuration": "Release", "compiler": str(compiler), "source_count": len(sources), "build_script_sha256": sha256(Path(__file__).resolve()), "language_version": "7.3", "debug_symbols": False, "source_hashes": {str(p.relative_to(ROOT)): sha256(p) for p in sources}, "build_input_hashes": {str(p.relative_to(ROOT)): sha256(p) for p in [PROJECT, project_dir / "app.config", project_dir / "favicon.ico", attributes, response]}, "framework_reference_hashes": {p.name: sha256(p) for p in framework_refs}, "dependencies": {p.name: sha256(p) for p in runtime_inputs}, "runtime_config_hashes": {p.name: sha256(output / p.name) for p in runtime_configs}, "runtime_config_source_hashes": {p.name: sha256(p) for p in runtime_configs}, "output": {"name": exe.name, "bytes": exe.stat().st_size, "sha256": sha256(exe), **pe_info(exe)}, "runtime_test_performed": False, "build_only": True}
    (output / "build-manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    bundle = ROOT / "ustcori_crack-win-anycpu-release.zip"
    with zipfile.ZipFile(bundle, "w", zipfile.ZIP_DEFLATED) as z:
        package_files = [exe, output / (name + ".exe.config"), output / "build-manifest.json"]
        package_files += [output / dependency.name for dependency in runtime_inputs + runtime_configs]
        for path in sorted(set(package_files)):
            z.write(path, path.name)
        z.write(log, "build.log")
    print(json.dumps({"build": "success", "manifest": manifest, "bundle": str(bundle), "bundle_sha256": sha256(bundle)}, ensure_ascii=False, indent=2))
    return 0

if __name__ == "__main__":
    sys.exit(main())
