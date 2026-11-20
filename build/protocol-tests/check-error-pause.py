import base64, ctypes, hashlib, json, pathlib, subprocess, sys, threading
from ctypes import wintypes as w
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
ROOT = pathlib.Path(__file__).resolve().parent
RUNTIME = ROOT / 'runtime'
EXE = RUNTIME / 'ustcori_crack.exe'
k = ctypes.WinDLL('kernel32', use_last_error=True)
class COORD(ctypes.Structure):
    _fields_ = [('X', w.SHORT), ('Y', w.SHORT)]
class SMALL_RECT(ctypes.Structure):
    _fields_ = [('Left', w.SHORT), ('Top', w.SHORT), ('Right', w.SHORT), ('Bottom', w.SHORT)]
class CSBI(ctypes.Structure):
    _fields_ = [('dwSize', COORD), ('dwCursorPosition', COORD), ('wAttributes', w.WORD), ('srWindow', SMALL_RECT), ('dwMaximumWindowSize', COORD)]
class KEY_EVENT(ctypes.Structure):
    _fields_ = [('bKeyDown', w.BOOL), ('wRepeatCount', w.WORD), ('wVirtualKeyCode', w.WORD), ('wVirtualScanCode', w.WORD), ('UnicodeChar', w.WCHAR), ('dwControlKeyState', w.DWORD)]
class EVENT(ctypes.Union):
    _fields_ = [('KeyEvent', KEY_EVENT), ('padding', w.DWORD * 4)]
class INPUT_RECORD(ctypes.Structure):
    _fields_ = [('EventType', w.WORD), ('Event', EVENT)]
k.AttachConsole.argtypes = [w.DWORD]; k.AttachConsole.restype = w.BOOL
k.FreeConsole.argtypes = []; k.FreeConsole.restype = w.BOOL
k.GetStdHandle.argtypes = [w.DWORD]; k.GetStdHandle.restype = w.HANDLE
k.WriteConsoleInputW.argtypes = [w.HANDLE, ctypes.POINTER(INPUT_RECORD), w.DWORD, ctypes.POINTER(w.DWORD)]; k.WriteConsoleInputW.restype = w.BOOL
k.GetConsoleScreenBufferInfo.argtypes = [w.HANDLE, ctypes.POINTER(CSBI)]; k.GetConsoleScreenBufferInfo.restype = w.BOOL
k.ReadConsoleOutputCharacterW.argtypes = [w.HANDLE, w.LPWSTR, w.DWORD, COORD, ctypes.POINTER(w.DWORD)]; k.ReadConsoleOutputCharacterW.restype = w.BOOL
k.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]; k.WaitForSingleObject.restype = w.DWORD
k.CreateFileW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, w.LPVOID, w.DWORD, w.DWORD, w.HANDLE]; k.CreateFileW.restype = w.HANDLE
k.CloseHandle.argtypes = [w.HANDLE]; k.CloseHandle.restype = w.BOOL

def console_handle(name):
    # Inherited redirected stdio survives AttachConsole; open the actual console.
    handle = k.CreateFileW(name, 0xc0000000, 3, None, 3, 0, None)
    checked(handle != ctypes.c_void_p(-1).value, 'CreateFileW '+name)
    return handle

def checked(ok, name):
    if not ok: raise ctypes.WinError(ctypes.get_last_error(), name)

def send(text):
    items = []
    for ch in text:
        for down in [True, False]:
            rec = INPUT_RECORD(); rec.EventType = 1
            rec.Event.KeyEvent = KEY_EVENT(down, 1, 13 if ch == '\r' else ord(ch.upper()), 0, ch, 0)
            items.append(rec)
    array = (INPUT_RECORD * len(items))(*items); count = w.DWORD()
    handle = console_handle('CONIN$')
    try:
        checked(k.WriteConsoleInputW(handle, array, len(items), ctypes.byref(count)), 'WriteConsoleInputW')
        assert count.value == len(items)
    finally: k.CloseHandle(handle)

def screen():
    output = console_handle('CONOUT$'); info = CSBI()
    try:
        checked(k.GetConsoleScreenBufferInfo(output, ctypes.byref(info)), 'GetConsoleScreenBufferInfo')
        length = min(info.dwSize.X * (info.dwCursorPosition.Y + 2), 12000)
        buf = ctypes.create_unicode_buffer(length + 1); count = w.DWORD()
        checked(k.ReadConsoleOutputCharacterW(output, buf, length, COORD(0, 0), ctypes.byref(count)), 'ReadConsoleOutputCharacterW')
        return buf.value
    finally: k.CloseHandle(output)

def interactive(name, args, input_text, expected_exit, wait_ms):
    proc = subprocess.Popen([str(EXE), *args], cwd=RUNTIME, creationflags=subprocess.CREATE_NEW_CONSOLE)
    try:
        k.FreeConsole()
        # CreateProcess returns before the child's console registration is ready.
        initial_state = k.WaitForSingleObject(int(proc._handle), 1000)
        assert initial_state == 258, (name, 'exited-before-console-attach', proc.poll())
        checked(k.AttachConsole(proc.pid), 'AttachConsole')
        if input_text is not None: send(input_text + '\r')
        state = k.WaitForSingleObject(int(proc._handle), wait_ms)
        text = screen()
        assert state == 258 and '按任意键关闭窗口' in text, (name, state, text)
        send('x')
        code = proc.wait(timeout=6)
        assert code == expected_exit, (name, code, text)
        return {'name': name, 'passed': True, 'stayedOpenUntilKey': True, 'exitCodeAfterKey': code, 'consoleText': text.rstrip()}
    finally:
        k.FreeConsole()
        if proc.poll() is None: proc.kill(); proc.wait(timeout=6)

records = []
class Handler(BaseHTTPRequestHandler):
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        records.append({'path': self.path, 'hasReadMethod': self.path.endswith('/FindByLABID') or b'FindByLABID' in body, 'hasWriteMethod': any(x in body for x in [b'UpdateRecord', b'SetLabTimeRecordStart', b'ucControlMethod'])})
        self.send_response(503); self.send_header('Content-Type', 'text/html'); self.end_headers()
        self.wfile.write(b'<html>local fixture unavailable</html>')
    def log_message(self, *args): pass
server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
thread = threading.Thread(target=server.serve_forever, daemon=True); thread.start()
try:
    checks = [interactive('invalid-link-interactive-holds', [], 'labori://!!!!/', 1, 2000)]
    data = {'Diff':'Labori://', 'ExamOrLabID':'2', 'SiteAdress':'http://127.0.0.1:'+str(server.server_port)+'/pause-fixture', 'StudentID':'TEST_STUDENT', 'Language':'ZH-CN', 'CourseID':'0000', 'Port':'false'}
    link = 'labori://' + base64.b64encode(json.dumps(data).encode('utf-8')).decode('ascii') + '/'
    checks.append(interactive('service-error-interactive-holds', [link], None, 2, 5000))
    assert len(records) == 1 and records[0]['hasReadMethod'] and not records[0]['hasWriteMethod']
    noninteractive = subprocess.run([str(EXE)], input=b'labori://!!!!/\r\n', cwd=RUNTIME, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=6)
    assert noninteractive.returncode == 1
    log = (RUNTIME/'startup-error.log').read_text(encoding='utf-8-sig')
    assert 'ExitCode=1' in log and 'ExitCode=2' in log and '/pause-fixture/ServiceAPI/FindByLABID' in log
    report = {'passed':True, 'exeSha256':hashlib.sha256(EXE.read_bytes()).hexdigest(), 'interactiveChecks':checks, 'redirectedStdinExitsWithoutWaiting':True, 'errorLogCreated':True, 'fixtureRequests':records, 'livePlatformContacted':False, 'experimentStarted':False, 'recordSubmitted':False}
    (ROOT/'error-pause-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
    print(json.dumps({key:value for key,value in report.items() if key != 'interactiveChecks'}, ensure_ascii=False))
    print('INTERACTIVE_CONSOLE_HOLD_PASS', len(checks))
finally:
    server.shutdown(); server.server_close(); thread.join(timeout=3)
