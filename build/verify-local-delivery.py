from pathlib import Path
import zipfile,hashlib,json,xml.etree.ElementTree as ET
root=Path(r'<USER_HOME>\Documents\deepseek-harness\default-workspace\ustcoriCrack')
bundle=root/'ustcori_crack-win-anycpu-release.zip'
output=root/'ustocri_crack/bin/Release';output.mkdir(parents=True,exist_ok=True)
sha=lambda data:hashlib.sha256(data).hexdigest()
with zipfile.ZipFile(bundle) as z:
    assert z.testzip() is None
    assert len(z.namelist())==len(set(z.namelist()))
    for info in z.infolist():
        assert not info.is_dir() and info.filename==Path(info.filename).name and not info.filename.startswith(('/', '\\'))
        data=z.read(info)
        dest=output/info.filename
        if dest.exists():
            assert dest.read_bytes()==data,'EXISTING_OUTPUT_DIFFERS:'+info.filename
        else:
            with dest.open('xb') as f:f.write(data)
    manifest=json.loads(z.read('build-manifest.json'))
    for name,expected in manifest['source_hashes'].items():assert sha((root/name).read_bytes())==expected,'SOURCE_TRANSFER_MISMATCH:'+name
    for name,expected in manifest['dependencies'].items():assert sha(z.read(name))==expected,'DEPENDENCY_MISMATCH:'+name
    for name,expected in manifest['runtime_config_hashes'].items():assert sha(z.read(name))==expected,'CONFIG_TRANSFER_MISMATCH:'+name
    component=z.read('USTCORi.SimExp.Instruments.Component.dll')
    lib=root/'ustocri_crack/lib/USTCORi.SimExp.Instruments.Component.dll'
    if lib.exists():assert lib.read_bytes()==component
    else:
        with lib.open('xb') as f:f.write(component)
    provenance=json.loads(z.read('dependency-provenance.json'))
    assert json.loads((root/'ustocri_crack/lib/dependency-provenance.json').read_text(encoding='utf-8'))==provenance
    for entry in provenance['files']:assert sha((root/'ustocri_crack/lib'/entry['name']).read_bytes())==entry['sha256']
    ns={'m':'http://schemas.microsoft.com/developer/msbuild/2003'}
    project=ET.parse(root/'ustocri_crack/ustcori_crack.csproj')
    required=[ref.findtext('m:HintPath',namespaces=ns) for ref in project.findall('m:ItemGroup/m:Reference',ns) if ref.findtext('m:HintPath',namespaces=ns)]
    assert len(required)==6 and all((root/'ustocri_crack'/p.replace('\\','/')).is_file() for p in required)
    exe=z.read('ustcori_crack.exe')
    assert len(exe)==64512 and sha(exe)=='89065f6dd366ad32f276f586c09393500d4747e3558894b53cbbc6c70ff30b6a'
    print(json.dumps(dict(local_transfer_hash_verified=True,output=str(output),exe_bytes=len(exe),exe_sha256=sha(exe),required_real_references_found=len(required),unchanged_csharp_sources_verified=len(manifest['source_hashes']),package_file_count=len(z.namelist()),runtime_test_performed=False),ensure_ascii=False))
