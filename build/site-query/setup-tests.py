import hashlib,json,pathlib,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
SITE=ROOT/'build/site-query'
RUNTIME=ROOT/'build/protocol-tests/runtime'
def sha(b): return hashlib.sha256(b).hexdigest()
with zipfile.ZipFile(SITE/'remote-http-query-final-release.zip') as z:
    assert z.testzip() is None
    names=z.namelist();assert len(names)==len(set(names)) and all(pathlib.PurePosixPath(x).name==x for x in names)
    files={name:z.read(name) for name in names}
m=json.loads(files['build-manifest.json']);assert m['source_count']==12
for path,expected in m['source_hashes'].items():assert sha((ROOT/path).read_bytes())==expected,path
for name,expected in m['dependencies'].items():assert sha(files[name])==expected,name
for name,expected in m['runtime_config_hashes'].items():assert sha(files[name])==expected,name
assert sha(files['ustcori_crack.exe'])==m['output']['sha256']
RUNTIME.mkdir(parents=True,exist_ok=True)
previous={}
for name,data in files.items():
    target=RUNTIME/name
    if target.exists():previous[name]=sha(target.read_bytes())
    target.write_bytes(data)
report={'passed':True,'exeSha256':m['output']['sha256'],'exeBytes':len(files['ustcori_crack.exe']),'sourceCount':m['source_count'],'testRuntime':str(RUNTIME),'previous':previous}
(SITE/'test-runtime-setup.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
print(json.dumps({k:v for k,v in report.items() if k!='previous'}))
