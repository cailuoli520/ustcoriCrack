import hashlib, json, pathlib, zipfile
ROOT = pathlib.Path(__file__).resolve().parents[2]
TEST = ROOT/'build/protocol-tests'
TARGET = pathlib.Path(r'<USER_HOME>\<DEPLOY_DIR>')
EXPECTED_TARGET = r'<USER_HOME>\<DEPLOY_DIR>'
OLD_EXE = '01461a8a8556cae136c454d97972ddab0680124c9b1f7d2324add6ad2f9400e6'
NEW_EXE = '2b26c4bb1e1b376a629d82b487e40fc0a1e830d740bbd448b850411d1565e9c6'
def digest(data): return hashlib.sha256(data).hexdigest()
with zipfile.ZipFile(TEST/'remote-exit-pause-release.zip') as z:
    assert z.testzip() is None
    names = z.namelist()
    assert len(names)==len(set(names)) and all(pathlib.PurePosixPath(x).name==x for x in names)
    files = {name:z.read(name) for name in names}
m = json.loads(files['build-manifest.json'])
assert m['source_count']==11 and m['output']['sha256']==NEW_EXE and digest(files['ustcori_crack.exe'])==NEW_EXE
for path, expected in m['source_hashes'].items(): assert digest((ROOT/path).read_bytes())==expected,path
for path, expected in m['build_input_hashes'].items():
    if '/obj/remote/' not in path: assert digest((ROOT/path).read_bytes())==expected,path
for name, expected in m['dependencies'].items(): assert digest(files[name])==expected,name
for name, expected in m['runtime_config_hashes'].items(): assert digest(files[name])==expected,name
assert files['ustcori_crack.exe.config']==files['WebLabClient.exe.config']
parser = json.loads((TEST/'parser-report.json').read_text(encoding='utf-8-sig'))
cli = json.loads((TEST/'cli-report.json').read_text(encoding='utf-8'))
pause = json.loads((TEST/'error-pause-report.json').read_text(encoding='utf-8'))
assert parser['passed'] and cli['passed'] and pause['passed']
assert cli['exeSha256']==NEW_EXE and pause['exeSha256']==NEW_EXE
assert len(pause['interactiveChecks'])==2 and all(x['stayedOpenUntilKey'] and x['passed'] for x in pause['interactiveChecks'])
verification = {'passed':True,'exeSha256':NEW_EXE,'parser':parser,'cli':cli,'errorPause':pause,'currentSiteStatus':'Previous build returned HTML at BizService.svc; live service was not retested in this exit-pause round.'}
files['BUILD-NOTES.md'] = (TEST/'ERROR-PAUSE-NOTES.md').read_bytes()
files['startup-verification.json'] = (json.dumps(verification,ensure_ascii=False,indent=2)+'\n').encode('utf-8')
files['error-pause-report.json'] = (TEST/'error-pause-report.json').read_bytes()
files['dependency-provenance.json'] = (ROOT/'ustocri_crack/lib/dependency-provenance.json').read_bytes()
filemap = {name:{'bytes':len(data),'sha256':digest(data)} for name,data in sorted(files.items())}
files['package-file-manifest.json'] = (json.dumps({'files':filemap,'note':'All other files; this manifest excludes itself.'},ensure_ascii=False,indent=2)+'\n').encode('utf-8')
# Preflight every user file before any replacement.
assert str(TARGET.resolve())==EXPECTED_TARGET and TARGET.is_dir()
assert digest((TARGET/'ustcori_crack.exe').read_bytes())==OLD_EXE,'Unexpected current EXE; retained'
runtime = [name for name in files if name.endswith(('.dll','.exe','.exe.config'))]
assert len(runtime)==12
configs = [name for name in runtime if name.endswith('.exe.config')]
retained_configs = {name:digest((TARGET/name).read_bytes()) for name in configs}
for name in runtime:
    if name!='ustcori_crack.exe' and name not in configs:
        assert digest((TARGET/name).read_bytes())==digest(files[name]),name
package = ROOT/'ustcori_crack-json-error-pause-fixed.zip'
with zipfile.ZipFile(package,'x',zipfile.ZIP_DEFLATED) as z:
    for name,data in sorted(files.items()): z.writestr(name,data)
with zipfile.ZipFile(package) as z: assert z.testzip() is None and len(z.namelist())==len(files)
release = ROOT/'ustocri_crack/bin/Release-error-pause-fixed'
release.mkdir(parents=True,exist_ok=False)
for name,data in files.items():
    with (release/name).open('xb') as f: f.write(data)
backup = TARGET/'.dsh-backup-error-pause-20261120-1454'
backup.mkdir(exist_ok=False)
old = (TARGET/'ustcori_crack.exe').read_bytes()
with (backup/'ustcori_crack.exe').open('xb') as f: f.write(old)
(backup/'backup-manifest.json').write_text(json.dumps({'ustcori_crack.exe':{'bytes':len(old),'sha256':digest(old)}},indent=2)+'\n',encoding='utf-8')
(TARGET/'ustcori_crack.exe').write_bytes(files['ustcori_crack.exe'])
assert digest((TARGET/'ustcori_crack.exe').read_bytes())==NEW_EXE
for name in runtime:
    expected = retained_configs[name] if name in configs else digest(files[name])
    assert digest((TARGET/name).read_bytes())==expected,name
result = {'passed':True,'package':str(package),'packageBytes':package.stat().st_size,'packageSha256':digest(package.read_bytes()),'packageFiles':len(files),'sourceHashesVerified':len(m['source_hashes']),'userTarget':str(TARGET),'runtimeFilesVerified':len(runtime),'updated':['ustcori_crack.exe'],'configurationsRetained':retained_configs,'backup':str(backup),'exeBytes':len(files['ustcori_crack.exe']),'exeSha256':NEW_EXE,'localRelease':str(release)}
(TEST/'exit-pause-deployment.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result))
