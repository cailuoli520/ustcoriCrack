from pathlib import Path
import sys,zipfile,hashlib,json
root=Path(__file__).resolve().parents[1]
target=Path(sys.argv[1]).resolve()
expected=Path(r'<USER_HOME>\<DEPLOY_DIR>').resolve()
assert target==expected and target.is_dir(),'UNEXPECTED_RUNTIME_DIRECTORY'
bundle=root/'ustcori_crack-win-anycpu-release.zip'
assert hashlib.sha256(bundle.read_bytes()).hexdigest()=='0a992dbcd71008c92c6a02f551ac0081ec9f1ba3d096bdc32baa9941f9d2e46b'
with zipfile.ZipFile(bundle) as z:
    assert z.testzip() is None
    names=[n for n in z.namelist() if n.lower().endswith(('.dll','.exe','.exe.config'))]
    assert len(names)==12 and all(Path(n).name==n for n in names)
    conflicts=[]
    for name in names:
        p=target/name
        if p.exists() and (not p.is_file() or p.read_bytes()!=z.read(name)):conflicts.append(name)
    if conflicts:raise SystemExit('EXISTING_RUNTIME_FILES_DIFFER: '+json.dumps(conflicts))
    added=[];unchanged=[]
    for name in names:
        p=target/name;data=z.read(name)
        if p.exists():unchanged.append(name)
        else:
            with p.open('xb') as f:f.write(data)
            added.append(name)
        assert hashlib.sha256(p.read_bytes()).digest()==hashlib.sha256(data).digest()
    print(json.dumps(dict(runtime_directory=str(target),added=added,unchanged=unchanged,total_verified_runtime_files=len(names),original_files_overwritten=False),ensure_ascii=False))
