import json, sys, urllib.parse, urllib.request
sys.stdout.reconfigure(encoding='utf-8', errors='replace')
BASE='http://<SERVER_HOST>'
def post(path, form):
    data=urllib.parse.urlencode(form).encode()
    req=urllib.request.Request(BASE+'/ServiceAPI/'+path, data=data,
        headers={'Content-Type':'application/x-www-form-urlencoded'})
    with urllib.request.urlopen(req, timeout=30) as r:
        return r.read().decode('utf-8','replace').strip()

rec=json.load(open('build/probe-real.json'))
rid=rec['rid']; print('using record', rid)

def trial(labname):
    try:
        r=post('UpdateRecord', {'UserID':'90000001','LabID':'2','LabName':labname,
            'RecordID':rid,'FileName':'90000001_20261120190000.xml',
            'Content':'AAAA','Key':'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA'})
        print(f'LabName={labname!r} -> {r}')
    except Exception as e:
        print(f'LabName={labname!r} -> ERR {e}')

trial('<LAB_NAME>.0000.2')
trial('<LAB_NAME>.0000')
trial('<LAB_NAME>')
