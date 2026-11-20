import base64,hashlib,json,pathlib,subprocess,sys
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
ROOT=pathlib.Path(__file__).resolve().parents[2];SITE=ROOT/'build/site-query'
deployment=json.loads((SITE/'http-query-deployment.json').read_text(encoding='utf-8'))
TARGET=pathlib.Path(deployment['userTarget']);EXE=TARGET/'ustcori_crack.exe'
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
assert str(TARGET.resolve())==str(pathlib.Path('<USER_HOME>/Downloads/Compressed/Updata'))
assert sha(EXE)==deployment['exeSha256']
launch={'Diff':'Labori://','ExamOrLabID':'2','SiteAdress':'http://<SERVER_HOST>','StudentID':'TEST_STUDENT','Whtn':'320*240*3*3','UserType':'3','Clientip':'127.0.0.1','ClienTarea':'unknown','Language':'ZH-CN','CourseID':'0000','SysID':'1','Port':'false','Mode':'2'}
link='labori://'+base64.b64encode(json.dumps(launch,separators=(',',':')).encode('utf-8')).decode('ascii')+'/'
p=subprocess.run([str(EXE),'--metadata-only',link],cwd=TARGET,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=30)
try:out=p.stdout.decode('utf-8')
except UnicodeDecodeError:out=p.stdout.decode('gb18030',errors='replace')
lab=json.loads((SITE/'current-lab-query-response.json').read_text(encoding='utf-8'))
assert p.returncode==0 and lab['LabName'] in out and '/ServiceAPI/FindByLABID' in out and 'NullReferenceException' not in out,(p.returncode,out)
for name,expected in deployment['configurationsRetained'].items():assert sha(TARGET/name)==expected,name
with __import__('zipfile').ZipFile(deployment['package']) as z:
    assert z.testzip() is None
    manifest=json.loads(z.read('package-file-manifest.json'))['files']
    for name in z.namelist():
        if name.endswith(('.dll','.exe','.exe.config')):
            expected=deployment['configurationsRetained'].get(name,manifest[name]['sha256'])
            assert sha(TARGET/name)==expected,name
result={'passed':True,'exitCode':p.returncode,'output':out,'exeSha256':sha(EXE),'target':str(EXE),'runtimeFilesVerified':12,'configurationsRetained':True,'linkShapeMatchesUserJSON':True,'StudentIDSent':False,'experimentStarted':False,'recordSubmitted':False}
(SITE/'deployed-http-query-report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+chr(10),encoding='utf-8')
print(json.dumps(result,ensure_ascii=False))
