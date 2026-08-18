#!/usr/bin/env python3
"""整理 JSON 檔案的命令列工具：格式化縮排、排序 key、或壓縮成單行。

終端機只顯示進度條，不會印出 JSON 內容。

用法範例：
    python json_formatter.py data.json -o out.json       # 輸出到新檔案
    python json_formatter.py data.json -i                # 原地覆寫
    python json_formatter.py *.json -i                   # 批次處理多個檔案
    python json_formatter.py data.json --minify -i       # 壓縮成單行
    python json_formatter.py data.json --sort-keys -n 4 -i  # 排序 key，縮排 4 格
"""
import argparse
import json
import sys
from pathlib import Path


def format_json(text: str, indent: int, sort_keys: bool, minify: bool) -> str:
    data = json.loads(text)
    if minify:
        return json.dumps(data, ensure_ascii=False, sort_keys=sort_keys, separators=(",", ":"))
    return json.dumps(data, ensure_ascii=False, sort_keys=sort_keys, indent=indent)


def print_progress(current: int, total: int, label: str, bar_width: int = 30):
    ratio = current / total
    filled = int(bar_width * ratio)
    bar = "=" * filled + ">" * (1 if filled < bar_width else 0) + " " * (bar_width - filled - (1 if filled < bar_width else 0))
    label = label if len(label) <= 40 else "..." + label[-37:]
    sys.stdout.write(f"\r進度: [{bar}] {current}/{total} ({ratio*100:5.1f}%) {label}".ljust(90))
    sys.stdout.flush()
    if current == total:
        sys.stdout.write("\n")


def process_file(path: Path, args) -> bool:
    try:
        text = path.read_text(encoding="utf-8")
        result = format_json(text, args.indent, args.sort_keys, args.minify)
    except json.JSONDecodeError as e:
        sys.stdout.write("\n")
        print(f"[錯誤] {path}: JSON 解析失敗 - {e}", file=sys.stderr)
        return False
    except OSError as e:
        sys.stdout.write("\n")
        print(f"[錯誤] {path}: 讀取失敗 - {e}", file=sys.stderr)
        return False

    if args.in_place:
        path.write_text(result + "\n", encoding="utf-8")
    elif args.output:
        Path(args.output).write_text(result + "\n", encoding="utf-8")
    return True


def main():
    parser = argparse.ArgumentParser(description="整理（格式化/排序/壓縮）JSON 檔案")
    parser.add_argument("files", nargs="+", help="要處理的 JSON 檔案路徑（可多個）")
    parser.add_argument("-i", "--in-place", action="store_true", help="原地覆寫檔案")
    parser.add_argument("-o", "--output", help="輸出檔案路徑（僅適用於單一輸入檔）")
    parser.add_argument("-n", "--indent", type=int, default=2, help="縮排空格數（預設 2）")
    parser.add_argument("--sort-keys", action="store_true", help="依 key 名稱排序")
    parser.add_argument("--minify", action="store_true", help="壓縮成單行（忽略縮排設定）")
    args = parser.parse_args()

    if not args.in_place and not args.output:
        parser.error("請指定 -i（原地覆寫）或 -o（輸出檔案），否則不會寫入任何結果")
    if args.output and len(args.files) > 1:
        parser.error("--output 只能用於單一輸入檔案，多檔案請改用 -i 原地覆寫")

    ok = True
    total = len(args.files)
    for idx, f in enumerate(args.files, start=1):
        path = Path(f)
        print_progress(idx - 1, total, path.name)
        if not path.exists():
            sys.stdout.write("\n")
            print(f"[錯誤] 找不到檔案: {f}", file=sys.stderr)
            ok = False
            continue
        if not process_file(path, args):
            ok = False
        print_progress(idx, total, path.name)

    sys.exit(0 if ok else 1)


if __name__ == "__main__":
    main()
