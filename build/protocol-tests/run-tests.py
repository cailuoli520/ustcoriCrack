import base64, copy, hashlib, json, pathlib, subprocess, sys, threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import quote
sys.stdout.reconfigure(encoding='utf-8',errors='replace')
def decode_output(raw):
    try: return raw.decode('utf-8')
    except UnicodeDecodeError: return raw.decode('gb18030',errors='replace')
ROOT=pathlib.Path(__file__).resolve().parent
RUNTIME=ROOT/'runtime'
EXE=RUNTIME/'ustcori_crack.exe'
PS=pathlib.Path(r'C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe')
BASE={'Diff':'Labori://','ExamOrLabID':'2','SiteAdress':'http://127.0.0.1:8000','StudentID':'TEST_STUDENT','Whtn':'320*240*3*3','UserType':'3','Clientip':'127.0.0.1','ClienTarea':'未知','Language':'ZH-CN','CourseID':'0000','SysID':'1','Port':'false','Mode':'2'}
def link(data,slash=True,escaped=False):
    payload=data if isinstance(data,str) else json.dumps(data,ensure_ascii=False,separators=(',',':'))
    encoded=base64.b64encode(payload.encode('utf-8')).decode('ascii')
    if escaped: encoded=quote(encoded,safe='')
    return 'labori://'+encoded+('/' if slash else '')
CASES=[]
def good(name,data,uri,new=True,json_format=True,slash=True,escaped=False):
    CASES.append({'name':name,'link':link(data,slash,escaped),'fail':False,'service':uri+'BizService.svc','expect':{'LabId':'2','StudentId':'TEST_STUDENT','SiteBaseUri':uri,'UseNewHall':new,'IsJson':json_format}})
def bad(name,data=None,raw=None):
    CASES.append({'name':name,'link':raw if raw is not None else link(data),'fail':True})
good('user-json-shape',BASE,'http://127.0.0.1:8000/')
good('no-trailing-delimiter',BASE,'http://127.0.0.1:8000/',slash=False)
c=copy.deepcopy(BASE);c['SiteAddress']=c.pop('SiteAdress');good('correct-address-spelling',c,'http://127.0.0.1:8000/')
c=copy.deepcopy(BASE);c['siteadress']=c.pop('SiteAdress');good('case-insensitive-address',c,'http://127.0.0.1:8000/')
c=copy.deepcopy(BASE);c['SiteAdress']='https://localhost:8443/labroot/';good('https-port-prefix',c,'https://localhost:8443/labroot/')
c['Port']='5001';good('explicit-port-override',c,'https://localhost:5001/labroot/')
for port in [False,'undefined','null','',None]:
 c=copy.deepcopy(BASE);c['SiteAdress']='http://localhost:8386/prefix';c['Port']=port;good('non-port-'+str(port),c,'http://localhost:8386/prefix/')
c=copy.deepcopy(BASE);c.pop('Port');good('missing-port',c,'http://127.0.0.1:8000/')
c=copy.deepcopy(BASE);c.pop('Language');c.pop('CourseID');good('json-without-new-hall-fields',c,'http://127.0.0.1:8000/',new=False)
good('legacy-five','/2/127.0.0.1/8386/TEST_STUDENT','http://127.0.0.1:8386/',new=False,json_format=False)
good('legacy-twelve','/2/127.0.0.1/8386/TEST_STUDENT/-/-/3/127.0.0.1/area/ZH-CN/0000','http://127.0.0.1:8386/',json_format=False)
plus=None
for i in range(1024):
 c=copy.deepcopy(BASE);c['ClienTarea']=chr(0x4e00+i)
 if '+' in link(c): plus=c;break
assert plus is not None
good('base64-plus-preserved',plus,'http://127.0.0.1:8000/')
good('percent-encoded-base64',plus,'http://127.0.0.1:8000/',escaped=True)
c=copy.deepcopy(BASE);c['SiteAddress']=c['SiteAdress'];good('matching-address-aliases',c,'http://127.0.0.1:8000/')
c=copy.deepcopy(BASE);c['SiteAddress']='http://localhost';bad('conflicting-address-aliases',c)
for field in ['ExamOrLabID','StudentID','SiteAdress']:
 c=copy.deepcopy(BASE);c.pop(field);bad('missing-'+field,c)
c=copy.deepcopy(BASE);c['Port']='not-a-port';bad('invalid-port',c)
c=copy.deepcopy(BASE);c['Port']='65536';bad('out-of-range-port',c)
c=copy.deepcopy(BASE);c['SiteAdress']='file:///tmp';bad('non-http-site',c)
c=copy.deepcopy(BASE);c['SiteAdress']='http://localhost/?x=1';bad('site-query',c)
bad('empty-input',raw='');bad('homepage-input',raw='http://127.0.0.1:8000/');bad('bad-base64',raw='labori://!!!!/');bad('legacy-too-short','/2/localhost')
(ROOT/'cases.json').write_text(json.dumps(CASES,ensure_ascii=False,indent=2),encoding='utf-8')
p=subprocess.run([str(PS),'-NoProfile','-ExecutionPolicy','Bypass','-File',str(ROOT/'check-parser.ps1'),'-RuntimePath',str(RUNTIME),'-CasesPath',str(ROOT/'cases.json'),'-ReportPath',str(ROOT/'parser-report.json')],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=35)
print(decode_output(p.stdout))
if p.returncode: sys.exit(p.returncode)
records=[]
class Handler(BaseHTTPRequestHandler):
 def do_POST(self):
  body=self.rfile.read(int(self.headers.get('Content-Length',0)))
  records.append({'path':self.path,'soapAction':self.headers.get('SOAPAction'),'bodyBytes':len(body),'hasReadMethod':self.path.endswith('/FindByLABID') or b'FindByLABID' in body,'hasWriteMethod':any(x in body for x in [b'ucControlMethod',b'UpdateRecord',b'SetLabTimeRecordStart'])})
  self.send_response(503);self.send_header('Content-Type','text/html');self.end_headers();self.wfile.write(b'<html>fixture service unavailable</html>')
 def log_message(self,*a): pass
server=ThreadingHTTPServer(('127.0.0.1',0),Handler)
thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
port=server.server_port
fixture=copy.deepcopy(BASE);fixture['SiteAdress']='http://127.0.0.1:'+str(port)+'/fixture'
checks=[]
try:
 before=len(records)
 r=subprocess.run([str(EXE),'--check-launch',link(fixture)],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=10)
 output=decode_output(r.stdout)
 assert r.returncode==0 and len(records)==before and ('/fixture/ServiceAPI/FindByLABID' in output),output
 checks.append({'name':'check-launch-no-network','exitCode':r.returncode,'passed':True,'output':output})
 r=subprocess.run([str(EXE),'--metadata-only',link(fixture)],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=35)
 output=decode_output(r.stdout)
 assert r.returncode==2 and records and 'NullReferenceException' not in output and 'aryun.ustcori.com' not in output,output
 assert all(x['path']=='/fixture/ServiceAPI/FindByLABID' and not x['hasWriteMethod'] for x in records),records
 checks.append({'name':'service-failure-clean-exit','exitCode':r.returncode,'passed':True,'output':output,'requests':records})
 r=subprocess.run([str(EXE),'--check-launch','labori://!!!!/'],cwd=RUNTIME,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,timeout=10)
 output=decode_output(r.stdout)
 assert r.returncode==1 and 'NullReferenceException' not in output,output
 checks.append({'name':'invalid-link-clean-exit','exitCode':r.returncode,'passed':True,'output':output})
finally:
 server.shutdown();server.server_close();thread.join(timeout=3)
report={'passed':True,'parserCases':len(CASES),'exeSha256':hashlib.sha256(EXE.read_bytes()).hexdigest(),'checks':checks,'livePlatformContacted':False,'experimentStarted':False,'recordSubmitted':False}
(ROOT/'cli-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps({'ALL_TESTS_PASS':True,'parserCases':len(CASES),'cliCases':len(checks),'fixtureRequests':len(records),'exeSha256':report['exeSha256']},ensure_ascii=False))
