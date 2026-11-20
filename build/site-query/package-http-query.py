import datetime,hashlib,json,os,pathlib,re,zipfile
ROOT=pathlib.Path(__file__).resolve().parents[2]
SITE=ROOT/'build/site-query'; TEST=ROOT/'build/protocol-tests'
TARGET=pathlib.Path('<USER_HOME>/Downloads/Compressed/Updata')
EXPECTED_TARGET=str(pathlib.Path('<USER_HOME>/Downloads/Compressed/Updata'))
OLD='2b26c4bb1e1b376a629d82b487e40fc0a1e830d740bbd448b850411d1565e9c6'
NEW='b3438e14b26d8b6cb05812960dacaa8fb85111c42e0ca1a10f1a0a5df7a903fb'
def sha(data):return hashlib.sha256(data).hexdigest()
def report(path):return json.loads(path.read_text(encoding='utf-8-sig'))
with zipfile.ZipFile(SITE/'remote-http-query-final-release.zip') as z:
    assert z.testzip() is None
    names=z.namelist();assert len(names)==len(set(names)) and all(pathlib.PurePosixPath(n).name==n for n in names)
    files={name:z.read(name) for name in names}
m=json.loads(files['build-manifest.json'])
assert m['source_count']==12 and m['output']['sha256']==NEW and sha(files['ustcori_crack.exe'])==NEW
for path,expected in m['source_hashes'].items():assert sha((ROOT/path).read_bytes())==expected,path
for path,expected in m['build_input_hashes'].items():
    if '/obj/remote/' not in path:assert sha((ROOT/path).read_bytes())==expected,path
for name,expected in m['dependencies'].items():assert sha(files[name])==expected,name
for name,expected in m['runtime_config_hashes'].items():assert sha(files[name])==expected,name
assert files['ustcori_crack.exe.config']==files['WebLabClient.exe.config']
parser=report(TEST/'parser-report.json');cli=report(TEST/'cli-report.json');pause=report(TEST/'error-pause-report.json')
query=report(SITE/'http-query-regression.json');mapping=report(SITE/'lab-model-mapping-report.json');live=report(SITE/'live-http-metadata-report.json')
for item in (parser,cli,pause,query,mapping,live):assert item['passed']
for item in (cli,pause,query,mapping,live):assert item['exeSha256']==NEW
counts={'launchParser':parser['parserCases'],'configuration':len(parser['configChecks']),'cli':len(cli['checks']),'interactiveConsole':len(pause['interactiveChecks']),'redirectedInput':int(pause['redirectedStdinExitsWithoutWaiting']),'httpQuery':query['caseCount'],'modelMapping':mapping['caseCount']}
assert sum(counts.values())==64,counts
assert live['exitCode']==0 and live['formKeys']==['LabID'] and not live['StudentIDSent'] and not live['experimentStarted'] and not live['recordSubmitted']
assert len(pause['interactiveChecks'])==2 and all(c['passed'] and c['stayedOpenUntilKey'] for c in pause['interactiveChecks'])
assert query['caseCount']==17 and mapping['caseCount']==9
verification={'passed':True,'exeSha256':NEW,'localRegressionCount':64,'localRegressionCounts':counts,'parser':parser,'cli':cli,'errorPause':pause,'httpQuery':query,'modelMapping':mapping,'liveMetadata':live,'review':{'completed':True,'blockingIssuesRemaining':False,'typeConflictCounterexampleFixed':True},'scope':{'experimentStarted':False,'recordSubmitted':False,'fullExperimentRuntimeVerified':False}}
files['BUILD-NOTES.md']=(SITE/'HTTP-QUERY-NOTES.md').read_bytes()
files['startup-verification.json']=(json.dumps(verification,ensure_ascii=False,indent=2)+chr(10)).encode('utf-8')
for name in ('http-query-regression.json','lab-model-mapping-report.json','live-http-metadata-report.json','current-lab-query-response.json','current-lab-query-http.json'):
    files[name]=(SITE/name).read_bytes()
files['error-pause-report.json']=(TEST/'error-pause-report.json').read_bytes()
files['dependency-provenance.json']=(ROOT/'ustocri_crack/lib/dependency-provenance.json').read_bytes()
filemap={name:{'bytes':len(data),'sha256':sha(data)} for name,data in sorted(files.items())}
files['package-file-manifest.json']=(json.dumps({'files':filemap,'note':'Every other package file; excludes this manifest.'},ensure_ascii=False,indent=2)+chr(10)).encode('utf-8')
assert str(TARGET.resolve())==EXPECTED_TARGET and TARGET.is_dir()
exe=TARGET/'ustcori_crack.exe';assert not exe.is_symlink() and exe.resolve()==pathlib.Path(EXPECTED_TARGET)/'ustcori_crack.exe'
old=exe.read_bytes();assert sha(old)==OLD,'Unexpected current EXE; no replacement'
runtime=[name for name in files if name.endswith(('.dll','.exe','.exe.config'))];assert len(runtime)==12
configs=[name for name in runtime if name.endswith('.exe.config')]
oldconfigs={name:(TARGET/name).read_bytes() for name in configs}
retained={name:sha(data) for name,data in oldconfigs.items()}
for name in runtime:
    assert not (TARGET/name).is_symlink(),name
    if name!='ustcori_crack.exe' and name not in configs:assert sha((TARGET/name).read_bytes())==sha(files[name]),name
package=ROOT/'ustcori_crack-http-query-fixed.zip';assert not package.exists()
release=ROOT/'ustocri_crack/bin/Release-http-query-fixed';assert not release.exists()
backup=TARGET/('.dsh-backup-http-query-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));assert not backup.exists()
tmp=TARGET/'ustcori_crack.exe.dsh-new';assert tmp.parent.resolve()==TARGET.resolve() and not tmp.exists()
with zipfile.ZipFile(package,'x',zipfile.ZIP_DEFLATED) as z:
    for name,data in sorted(files.items()):z.writestr(name,data)
with zipfile.ZipFile(package) as z:assert z.testzip() is None and len(z.namelist())==len(files)
release.mkdir(parents=True,exist_ok=False)
for name,data in files.items():
    with (release/name).open('xb') as f:f.write(data)
backup.mkdir(exist_ok=False)
originals={'ustcori_crack.exe':old,**oldconfigs}
for name,data in originals.items():
    with (backup/name).open('xb') as f:f.write(data)
(backup/'backup-manifest.json').write_text(json.dumps({name:{'bytes':len(data),'sha256':sha(data)} for name,data in originals.items()},indent=2)+chr(10),encoding='utf-8')
with tmp.open('xb') as f:f.write(files['ustcori_crack.exe'])
assert tmp.resolve()==pathlib.Path(EXPECTED_TARGET)/'ustcori_crack.exe.dsh-new'
os.replace(tmp,exe)
assert sha(exe.read_bytes())==NEW
for name in runtime:
    expected=retained[name] if name in configs else sha(files[name])
    assert sha((TARGET/name).read_bytes())==expected,name
result={'passed':True,'package':str(package),'packageBytes':package.stat().st_size,'packageSha256':sha(package.read_bytes()),'packageFiles':len(files),'sourceHashesVerified':len(m['source_hashes']),'userTarget':str(TARGET),'runtimeFilesVerified':len(runtime),'updated':['ustcori_crack.exe'],'configurationsRetained':retained,'backup':str(backup),'exeBytes':len(files['ustcori_crack.exe']),'exeSha256':NEW,'localRelease':str(release),'localRegressionCount':64,'buildWarningCount':len(re.findall(r': warning ',files['build.log'].decode('utf-8')))}
(SITE/'http-query-deployment.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+chr(10),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
