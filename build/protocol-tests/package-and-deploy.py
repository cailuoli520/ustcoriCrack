import hashlib,json,pathlib,shutil,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
TEST=ROOT/'build/protocol-tests'
TARGET=pathlib.Path(r'<USER_HOME>\<DEPLOY_DIR>')
EXPECTED_TARGET=r'<USER_HOME>\<DEPLOY_DIR>'
OLD_EXE='89065f6dd366ad32f276f586c09393500d4747e3558894b53cbbc6c70ff30b6a'
NEW_EXE='01461a8a8556cae136c454d97972ddab0680124c9b1f7d2324add6ad2f9400e6'
def digest(data): return hashlib.sha256(data).hexdigest()
base=TEST/'remote-base-release.zip'
with zipfile.ZipFile(base) as z:
 assert z.testzip() is None
 names=z.namelist();assert len(names)==len(set(names)) and all(pathlib.PurePosixPath(x).name==x for x in names)
 files={name:z.read(name) for name in names}
m=json.loads(files['build-manifest.json'])
assert m['source_count']==11 and m['output']['sha256']==NEW_EXE and digest(files['ustcori_crack.exe'])==NEW_EXE
for path,expected in m['source_hashes'].items(): assert digest((ROOT/path).read_bytes())==expected,path
for path,expected in m['build_input_hashes'].items():
 if '/obj/remote/' not in path: assert digest((ROOT/path).read_bytes())==expected,path
for name,expected in m['dependencies'].items(): assert digest(files[name])==expected,name
for name,expected in m['runtime_config_hashes'].items(): assert digest(files[name])==expected,name
assert files['ustcori_crack.exe.config']==files['WebLabClient.exe.config']
files['BUILD-NOTES.md']=(TEST/'JSON-STARTUP-NOTES.md').read_bytes()
files['startup-verification.json']=(TEST/'startup-verification.json').read_bytes()
files['dependency-provenance.json']=(ROOT/'ustocri_crack/lib/dependency-provenance.json').read_bytes()
filemap={name:{'bytes':len(data),'sha256':digest(data)} for name,data in sorted(files.items())}
files['package-file-manifest.json']=(json.dumps({'files':filemap,'note':'Manifest lists all other package files; it excludes itself.'},ensure_ascii=False,indent=2)+'\n').encode('utf-8')
package=ROOT/'ustcori_crack-json-startup-fixed.zip'
with zipfile.ZipFile(package,'x',zipfile.ZIP_DEFLATED) as z:
 for name,data in sorted(files.items()): z.writestr(name,data)
with zipfile.ZipFile(package) as z: assert z.testzip() is None and len(z.namelist())==18
release=ROOT/'ustocri_crack/bin/Release-json-startup-fixed'
release.mkdir(parents=True,exist_ok=False)
for name,data in files.items():
 with (release/name).open('xb') as f: f.write(data)
assert str(TARGET.resolve())==EXPECTED_TARGET and TARGET.is_dir(),str(TARGET.resolve())
exe=TARGET/'ustcori_crack.exe'
assert exe.is_file() and digest(exe.read_bytes())==OLD_EXE,'Unexpected user EXE; retained without overwriting'
runtime=[name for name in files if name.endswith(('.dll','.exe','.exe.config'))]
assert len(runtime)==12
changed=['ustcori_crack.exe','ustcori_crack.exe.config','WebLabClient.exe.config']
for name in runtime:
 if name not in changed: assert (TARGET/name).is_file() and digest((TARGET/name).read_bytes())==digest(files[name]),'Existing dependency differs: '+name
backup=TARGET/'.dsh-backup-json-startup-20261120-1430'
backup.mkdir(exist_ok=False)
backupmap={}
for name in changed:
 current=(TARGET/name).read_bytes()
 with (backup/name).open('xb') as f: f.write(current)
 backupmap[name]={'bytes':len(current),'sha256':digest(current)}
(backup/'backup-manifest.json').write_text(json.dumps(backupmap,indent=2)+'\n',encoding='utf-8')
for name in changed:
 # Original has been read and verified above; back it up before replacement.
 (TARGET/name).write_bytes(files[name])
for name in runtime: assert digest((TARGET/name).read_bytes())==digest(files[name]),name
result={'passed':True,'package':str(package),'packageBytes':package.stat().st_size,'packageSha256':digest(package.read_bytes()),'packageFiles':len(files),'sourceHashesVerified':len(m['source_hashes']),'userTarget':str(TARGET),'runtimeFilesVerified':len(runtime),'updated':changed,'backup':str(backup),'exeBytes':len(files['ustcori_crack.exe']),'exeSha256':NEW_EXE,'localRelease':str(release)}
(TEST/'deployment-verification.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
print(json.dumps(result))
