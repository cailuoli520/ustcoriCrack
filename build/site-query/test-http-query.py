import base64,copy,hashlib,json,pathlib,subprocess,sys,threading
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from urllib.parse import parse_qs
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
ROOT=pathlib.Path(__file__).resolve().parents[2]
SITE=ROOT/'build/site-query'; RUNTIME=ROOT/'build/protocol-tests/runtime';EXE=RUNTIME/'ustcori_crack.exe'
BASE={'Diff':'Labori://','ExamOrLabID':'2','SiteAdress':'','StudentID':'TEST_STUDENT','Language':'ZH-CN','CourseID':'0000','SysID':'1','Port':'false','Mode':'2'}
LAB=json.loads((SITE/'current-lab-query-response.json').read_text(encoding='utf-8'))
def link(data):return 'labori://'+base64.b64encode(json.dumps(data,ensure_ascii=False,separators=(',',':')).encode('utf-8')).decode('ascii')+'/'
def decode(raw):
    try:return raw.decode('utf-8')
    except UnicodeDecodeError:return raw.decode('gb18030',errors='replace')
records=[];mode={'status':200,'type':'application/json','body':b'null','headers':{}}
class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        raw=self.rfile.read(int(self.headers.get('Content-Length',0)))
        records.append({'method':'POST','path':self.path,'body':raw.decode('utf-8'),'form':parse_qs(raw.decode('utf-8')),'soapAction':self.headers.get('SOAPAction'),'contentType':self.headers.get('Content-Type'),'accept':self.headers.get('Accept')})
        self.send_response(mode['status']);self.send_header('Content-Type',mode['type'])
        for key,value in mode['headers'].items():self.send_header(key,value)
        self.end_headers()
        try:self.wfile.write(mode['body'])
        except (ConnectionResetError,BrokenPipeError):pass
    def do_GET(self):
        records.append({'method':'GET','path':self.path});self.send_response(500);self.end_headers()
    def log_message(self,*args):pass
server=ThreadingHTTPServer(('127.0.0.1',0),Handler);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
fixture=copy.deepcopy(BASE);fixture['SiteAdress']='http://127.0.0.1:'+str(server.server_port)+'/query-root'
checks=[]
def run_case(name,body,status=200,ctype='application/json',headers=None,exit_code=2,data=None):
    mode.update(status=status,type=ctype,body=body if isinstance(body,bytes) else json.dumps(body,ensure_ascii=False).encode('utf-8'),headers=headers or {})
    before=len(records);payload=data or fixture
    p=subprocess.run([str(EXE),'--metadata-only',link(payload)],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=12)
    out=decode(p.stdout);new=records[before:]
    assert p.returncode==exit_code,(name,p.returncode,out)
    assert 'NullReferenceException' not in out and 'aryun.ustcori.com' not in out,(name,out)
    assert len(new)==1 and new[0]['method']=='POST' and new[0]['path']=='/query-root/ServiceAPI/FindByLABID',(name,new)
    assert new[0]['form']=={'LabID':[payload['ExamOrLabID']]} and new[0]['soapAction'] is None,(name,new)
    assert new[0]['contentType'].startswith('application/x-www-form-urlencoded'),new
    if exit_code==0:assert LAB['LabName'] in out,(name,out)
    checks.append({'name':name,'passed':True,'exitCode':p.returncode,'output':out,'requests':new})
try:
    run_case('current-server-json-object',LAB,exit_code=0)
    uppercase=copy.deepcopy(LAB);uppercase['LABNAME']=uppercase.pop('LabName');run_case('legacy-labname-case-alias',uppercase,exit_code=0)
    run_case('null-lab-response',None)
    run_case('empty-response',b'')
    run_case('empty-json-object',{})
    run_case('unexpected-wrapper',{'Data':LAB})
    run_case('unexpected-json-array',[LAB])
    run_case('malformed-json',b'{broken')
    run_case('html-error-page',b'<html><title>Error</title></html>',ctype='text/html')
    different=copy.deepcopy(LAB);different['LabID']=3;run_case('wrong-lab-id',different)
    missing=copy.deepcopy(LAB);missing['LabFileUrl']=None;run_case('missing-file-url',missing)
    run_case('http404',b'not found',status=404,ctype='text/plain')
    run_case('redirect-not-followed',b'',status=302,headers={'Location':'/Login/Error'})
    odd=copy.deepcopy(fixture);odd['ExamOrLabID']='2+&= /未知';run_case('form-value-is-percent-encoded',LAB,exit_code=0,data=odd)
    run_case('response-size-limit',b' '*524289)
    other=copy.deepcopy(fixture);other['SysID']='4';before=len(records)
    p=subprocess.run([str(EXE),'--metadata-only',link(other)],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=10)
    assert p.returncode==2 and len(records)==before and 'SysID=1' in decode(p.stdout)
    checks.append({'name':'other-system-does-not-query-lab','passed':True,'exitCode':p.returncode,'requests':[]})
    mode.update(status=503,type='text/html',body=b'<html>fixture unavailable</html>',headers={});before=len(records)
    legacy='/2/127.0.0.1/'+str(server.server_port)+'/TEST_STUDENT'
    oldlink='labori://'+base64.b64encode(legacy.encode()).decode()+'/'
    p=subprocess.run([str(EXE),'--metadata-only',oldlink],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=15);out=decode(p.stdout);new=records[before:]
    assert p.returncode==2 and len(new)==1 and new[0]['path']=='/BizService.svc' and 'FindByLABID' in new[0]['body'] and new[0]['soapAction'],(p.returncode,out,new)
    assert 'NullReferenceException' not in out
    checks.append({'name':'legacy-link-still-uses-wcf','passed':True,'exitCode':p.returncode,'requests':new,'output':out})
    report={'passed':True,'checks':checks,'caseCount':len(checks),'fixtureRequests':len(records),'exeSha256':hashlib.sha256(EXE.read_bytes()).hexdigest(),'livePlatformContacted':False,'experimentStarted':False,'recordSubmitted':False}
    (SITE/'http-query-regression.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({k:v for k,v in report.items() if k!='checks'},ensure_ascii=False))
finally:server.shutdown();server.server_close();thread.join(timeout=3)
# Only the one verified read-only laboratory metadata operation is exercised live.
live=copy.deepcopy(BASE);live['SiteAdress']='http://<SERVER_HOST>'
p=subprocess.run([str(EXE),'--metadata-only',link(live)],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=30);out=decode(p.stdout)
result={'passed':p.returncode==0 and LAB['LabName'] in out and '/ServiceAPI/FindByLABID' in out and 'NullReferenceException' not in out,'exitCode':p.returncode,'output':out,'exeSha256':hashlib.sha256(EXE.read_bytes()).hexdigest(),'endpoint':'http://<SERVER_HOST>/ServiceAPI/FindByLABID','formKeys':['LabID'],'StudentIDSent':False,'experimentStarted':False,'recordSubmitted':False}
(SITE/'live-http-metadata-report.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8');print(json.dumps(result,ensure_ascii=False));assert result['passed'],result
