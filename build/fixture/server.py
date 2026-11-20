#!/usr/bin/env python3
"""本地仿真服务端：完整复刻 ServiceAPI 契约，端到端验证提交链路。

实现真实站点的四个接口，把客户端送来的原始 POST 表单逐条落盘：

  FindByLABID           -> LABINFO JSON（字段与线上一致）
  ucControlMethod       -> 排队号
  SetLabTimeRecordStart -> 记录 ID
  UpdateRecord          -> 记录 ID，并把 Content/Key/FileName 原样存档

存档交给 C# 侧的 --verify-fixture 处理：那边的 LitJson 能真正解密，
可以验证「服务端收到的 Content 解密后 == 本地求解出的 XML」。

只监听 127.0.0.1，只写本地文件，不接触任何真实站点。
"""
import base64
import json
import pathlib
import sys
import threading
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import parse_qs

sys.stdout.reconfigure(encoding='utf-8', errors='replace')
ROOT = pathlib.Path(__file__).resolve().parent
LAB_PACKAGE = ROOT / 'live.lab'
ARCHIVE = ROOT / 'captured.json'
LAB_NAME = '<LAB_NAME>'
LAB_ID = 2
RECORD_ID = 880001
QUEUE_ID = 70001

captured = []


class Handler(BaseHTTPRequestHandler):
    protocol_version = 'HTTP/1.1'

    def _send(self, body, ctype='text/html; charset=utf-8', code=200):
        data = body.encode('utf-8') if isinstance(body, str) else body
        self.send_response(code)
        self.send_header('Content-Type', ctype)      # 故意照抄站点：连数字也标 text/html
        self.send_header('Content-Length', str(len(data)))
        self.end_headers()
        self.wfile.write(data)

    def do_GET(self):
        if self.path.startswith('/Upload/lab/'):
            if not LAB_PACKAGE.is_file():
                self._send('missing', code=404)
                return
            data = LAB_PACKAGE.read_bytes()
            self.send_response(200)
            self.send_header('Content-Type', 'application/octet-stream')
            self.send_header('Content-Length', str(len(data)))
            self.end_headers()
            self.wfile.write(data)
            return
        self._send('not found', code=404)

    def do_POST(self):
        length = int(self.headers.get('Content-Length', 0))
        raw = self.rfile.read(length)
        form = {k: v[0] for k, v in
                parse_qs(raw.decode('utf-8', 'replace'), keep_blank_values=True).items()}
        path = self.path.split('?')[0]
        captured.append({'path': path, 'form': form})
        ARCHIVE.write_text(json.dumps(captured, ensure_ascii=False, indent=2), encoding='utf-8')
        print('  <- %s  %s' % (path, {k: (v[:40] + '…' if len(v) > 40 else v)
                                      for k, v in form.items()}))

        if path.endswith('/FindByLABID'):
            self._send(json.dumps({
                'LabID': LAB_ID, 'LabName': LAB_NAME, 'UpdateTime': '2020/4/2 2:19:41',
                'CreateTime': '', 'SyllabusID': '', 'CourseID': '', 'Introduction': 0,
                'CourseName': '', 'SyllabusName': '', 'LabUpdateTime': '',
                'ConfigUpdateTime': '', 'VideoUpdateTime': '', 'CourseAdminID': '',
                'LabFileUrl': LAB_NAME + '.lab', 'LabUpTime': '', 'SourceFileUrl': '',
                'SourceUpTime': '', 'ReportFileUrl': '', 'ReportUpTime': '',
            }, ensure_ascii=False), 'text/json; charset=utf-8')
            return

        if path.endswith('/ucControlMethod'):
            token = json.loads(form.get('token', '{}'))
            self._send('0' if token.get('OperType') == 1 else str(QUEUE_ID))
            return

        if path.endswith('/SetLabTimeRecordStart'):
            self._send(str(RECORD_ID))
            return

        if path.endswith('/UpdateRecord'):
            self._send(str(RECORD_ID))
            return

        self._send('unknown endpoint', code=404)

    def log_message(self, *a):
        pass


def main():
    keep = '--keep-open' in sys.argv
    server = ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    port = server.server_port
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    print('fixture listening on 127.0.0.1:%d' % port)

    payload = json.dumps({
        'Diff': 'Labori://', 'ExamOrLabID': str(LAB_ID),
        'SiteAdress': 'http://127.0.0.1:%d' % port,
        'StudentID': '90000001', 'Whtn': '320*240*3*3', 'UserType': '3',
        'Clientip': '127.0.0.1', 'ClienTarea': '未知', 'Language': 'ZH-CN',
        'CourseID': '0000', 'SysID': '1', 'Port': 'false', 'Mode': '2',
    }, ensure_ascii=False, separators=(',', ':'))
    link = 'labori://' + base64.b64encode(payload.encode('utf-8')).decode('ascii') + '/'
    (ROOT / 'fixture-link.txt').write_text(link, encoding='utf-8')
    (ROOT / 'fixture-port.txt').write_text(str(port), encoding='utf-8')
    print('link written (%d chars)' % len(link))

    if keep:
        print('ready; press Ctrl+C to stop')
        try:
            threading.Event().wait()
        except KeyboardInterrupt:
            pass
    else:
        threading.Event().wait(0.1)

    server.shutdown()
    ARCHIVE.write_text(json.dumps(captured, ensure_ascii=False, indent=2), encoding='utf-8')
    print('captured %d requests -> %s' % (len(captured), ARCHIVE))


if __name__ == '__main__':
    main()
