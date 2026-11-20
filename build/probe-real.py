import json, sys, urllib.parse, urllib.request, time
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
BASE = 'http://<SERVER_HOST>'
def post(path, form):
    data = urllib.parse.urlencode(form).encode()
    req = urllib.request.Request(BASE + '/ServiceAPI/' + path, data=data,
        headers={'Content-Type':'application/x-www-form-urlencoded'})
    with urllib.request.urlopen(req, timeout=30) as r:
        txt = r.read().decode('utf-8', 'replace')
    print(f'{path} -> [{txt.strip()[:200]}]')
    return txt.strip()

lab = post('FindByLABID', {'LabID': '2'})
info = json.loads(lab)
print('LABNAME =', info.get('LABNAME'), '| LabID =', info.get('LabID'))

tok = json.dumps({'UserID':'90000001','ServiceID':'开始实验','OperType':0,'LabID':2}, ensure_ascii=False, separators=(',',':'))
slot = post('ucControlMethod', {'token': tok})
print('queue slot =', slot)

rid = post('SetLabTimeRecordStart', {'USERID':'90000001','LABID':'2','ClientIP':'127.0.0.1','ClientArea':'未知','CourseID':'0000'})
print('record id =', rid)
json.dump({'slot':slot,'rid':rid,'labname':info.get('LABNAME')}, open('build/probe-real.json','w'), ensure_ascii=False)
