#!/usr/bin/env python3
"""按参数生成 labori 启动链接（JSON 格式）。

目标服务器完全由链接里的 SiteAdress / Port 决定，程序本身没有写死地址。
用法：

    python build/make-link.py --site http://127.0.0.1:61620
    python build/make-link.py --site http://<实验平台地址> --student <学号> --course <课程ID>
    python build/make-link.py --site https://lab.example.edu/labroot --port 8443 --mode 2

参数说明（括号内为线上实际取值）：
    --site      站点基址，必填。可带路径前缀，如 https://host/labroot
    --port      覆盖端口。留空 = 用 site 里的端口；写 false/undefined/null = 同样不覆盖
    --lab       实验 ID（2）
    --student   学号（JSON 里的 StudentID）
    --course    课程 ID
    --mode      实验模式（2）
    --sys       系统 ID（1）
    --user-type 用户类型（3）
    --client-ip 客户端 IP（127.0.0.1）
    --area      客户端地区（未知）
    --lang      语言（ZH-CN）
"""
import argparse
import base64
import json
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='replace')


def main():
    p = argparse.ArgumentParser(description='生成 labori 启动链接')
    p.add_argument('--site', required=True, help='站点基址，例如 http://127.0.0.1:61620')
    p.add_argument('--port', default='false')
    p.add_argument('--lab', default='2')
    p.add_argument('--student', default='90000001')
    p.add_argument('--course', default='0000')
    p.add_argument('--mode', default='2')
    p.add_argument('--sys', default='1')
    p.add_argument('--user-type', dest='user_type', default='3')
    p.add_argument('--client-ip', dest='client_ip', default='127.0.0.1')
    p.add_argument('--area', default='未知')
    p.add_argument('--lang', default='ZH-CN')
    a = p.parse_args()

    payload = {
        'Diff': 'Labori://',
        'ExamOrLabID': a.lab,
        'SiteAdress': a.site,
        'StudentID': a.student,
        'Whtn': '320*240*3*3',
        'UserType': a.user_type,
        'Clientip': a.client_ip,
        'ClienTarea': a.area,
        'Language': a.lang,
        'CourseID': a.course,
        'SysID': a.sys,
        'Port': a.port,
        'Mode': a.mode,
    }

    # 键名、顺序与站点生成的完全一致；分隔符不留空格，否则 base64 会不同
    text = json.dumps(payload, ensure_ascii=False, separators=(',', ':'))
    link = 'labori://' + base64.b64encode(text.encode('utf-8')).decode('ascii') + '/'

    print('明文 :', text)
    print('长度 :', len(link))
    print('链接 :', link)

    # 自检：能否原样解析回来
    body = link[len('labori://'):].rstrip('/')
    padded = body + '=' * (-len(body) % 4)
    back = json.loads(base64.b64decode(padded).decode('utf-8'))
    assert back == payload, '往返不一致'
    print('自检 : 解析往返一致')


if __name__ == '__main__':
    main()
