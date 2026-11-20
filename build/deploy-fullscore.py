#!/usr/bin/env python3
"""把满分求解版 ustcori_crack 打包并部署到实际运行目录。

安全性约定：
  · 只写 ustcori_crack.exe / 两个 .config，其余文件（官方 DLL）只做哈希核对，不动；
  · 替换前把原文件整份备份到 .dsh-backup-<tag>-<时间戳>，并写 backup-manifest.json；
  · 每个目标目录都先确认原 EXE 的哈希是已知值，认不出来就整体跳过，绝不盲写。
"""
import hashlib
import json
import pathlib
import shutil
import sys
import zipfile
from datetime import datetime

ROOT = pathlib.Path(__file__).resolve().parents[1]
SRC = ROOT / 'ustocri_crack/bin/Release-local'
TEST = ROOT / 'build/protocol-tests'

# 已知的历史 EXE 指纹：认得出才允许覆盖
KNOWN_EXE = {
    '89065f6dd366ad32f276f586c09393500d4747e3558894b53cbbc6c70ff30b6a',  # 初次发布版
    'b3438e14b26d8b6cb05812960dacaa8fb85111c42e0ca1a10f1a0a5df7a903fb',  # http-query-fixed
    'aeba8d4b108524cbec508246ae0fc8819c291ec08acc9336e239a7925f9462a8',  # 满分版初版
    '7f0feeef19e5a546e0deab99bb1a4f6925fcb38bca83fc99260e07ec51f024bc',  # TEMP 回退版
    '48d54e45708cf2ad7fe8aa1f32e2ff80e12755512079e57d97a29b32f363d271',  # 提交存档版
    'd2bb0d76b6f1f7081c221c1acd444db78ccdc9391126d6acf94a09aab35a72be',  # help + 版本号
    'b5be0ad1661433eeb8647c5d7476835f4769dca9479f485936cc65a43028debf',  # PE 完整性校验
}

# 真正会被运行的目标：C:\Ustcori\Updata 是官方 Update 目录，
# Download\Updata 与 <DEPLOY_DIR> 是用户自己放 exe 的副本
TARGETS = [
    pathlib.Path(r'C:\Ustcori\Updata'),
    pathlib.Path(r'<USER_HOME>\<DEPLOY_DIR>'),
]

CHANGED = ['ustcori_crack.exe', 'ustcori_crack.exe.config', 'WebLabClient.exe.config']


def digest(data):
    return hashlib.sha256(data).hexdigest()


def file_digest(path):
    return digest(path.read_bytes())


def main():
    exe = SRC / 'ustcori_crack.exe'
    cfg = SRC / 'ustcori_crack.exe.config'
    for p in (exe, cfg):
        if not p.is_file():
            raise SystemExit('缺少构建产物：' + str(p))

    exe_bytes = exe.read_bytes()
    cfg_bytes = cfg.read_bytes()
    exe_sha = digest(exe_bytes)
    payload = {'ustcori_crack.exe': exe_bytes,
               'ustcori_crack.exe.config': cfg_bytes,
               'WebLabClient.exe.config': cfg_bytes}

    # ---- solved-exam.xml 只是随包留档的验收样例，缺失不阻塞部署 ----
    report_path = SRC / 'solved-exam.xml'
    if not report_path.is_file():
        print('提示：未找到 solved-exam.xml（--self-test 的产物），跳过随包留档。')

    stamp = datetime.now().strftime('%Y%m%d-%H%M')
    results = []
    for target in TARGETS:
        if not target.is_dir():
            results.append({'target': str(target), 'status': 'skip', 'reason': '目录不存在'})
            continue

        current = target / 'ustcori_crack.exe'
        if current.is_file():
            before = file_digest(current)
            if before not in KNOWN_EXE:
                results.append({'target': str(target), 'status': 'skip',
                                'reason': '现有 EXE 哈希不在已知列表，拒绝覆盖', 'sha256': before})
                continue
        else:
            before = None

        if before == exe_sha:
            results.append({'target': str(target), 'status': 'already-current', 'sha256': before})
            continue

        # 官方依赖必须原样存在，否则说明这不是运行目录
        missing = [n for n in ('WebLabClient.exe', 'PackageLibrary.dll',
                               'USTCORi.SimExp.Instruments.Component.dll')
                   if not (target / n).is_file()]
        if missing:
            results.append({'target': str(target), 'status': 'skip',
                            'reason': '缺少官方依赖，疑似不是运行目录：' + ', '.join(missing)})
            continue

        backup = target / ('.dsh-backup-fullscore-' + stamp)
        backup.mkdir(exist_ok=False)
        backupmap = {}
        for name in CHANGED:
            src = target / name
            if src.is_file():
                data = src.read_bytes()
                (backup / name).write_bytes(data)
                backupmap[name] = {'bytes': len(data), 'sha256': digest(data)}
        (backup / 'backup-manifest.json').write_text(
            json.dumps(backupmap, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')

        for name, data in payload.items():
            (target / name).write_bytes(data)

        for name in CHANGED:
            assert file_digest(target / name) == digest(payload[name]), name

        results.append({'target': str(target), 'status': 'deployed', 'before': before,
                        'after': exe_sha, 'backup': str(backup), 'updated': CHANGED})

    # ---- 打包 ----
    package = ROOT / 'ustcori_crack-fullscore.zip'
    if package.exists():
        package.unlink()
    files = {'ustcori_crack.exe': exe_bytes,
             'ustcori_crack.exe.config': cfg_bytes,
             'WebLabClient.exe.config': cfg_bytes,
             'solved-exam.xml': report_path.read_bytes() if report_path.is_file() else b'',
             'BUILD-NOTES.md': (SRC / 'BUILD-NOTES.md').read_bytes() if (SRC / 'BUILD-NOTES.md').is_file() else b'',
             'build-manifest.local.json': (SRC / 'build-manifest.local.json').read_bytes()}
    manifest = {name: {'bytes': len(data), 'sha256': digest(data)}
                for name, data in sorted(files.items()) if data}
    files['package-file-manifest.json'] = (json.dumps({'files': manifest}, ensure_ascii=False, indent=2) + '\n').encode()
    with zipfile.ZipFile(package, 'w', zipfile.ZIP_DEFLATED) as z:
        for name, data in sorted(files.items()):
            if data:
                z.writestr(name, data)

    summary = {'exeSha256': exe_sha, 'exeBytes': len(exe_bytes),
               'package': str(package), 'packageSha256': file_digest(package),
               'targets': results}
    (TEST / 'fullscore-deployment.json').write_text(
        json.dumps(summary, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False, indent=2))
    return 0


if __name__ == '__main__':
    sys.exit(main())
