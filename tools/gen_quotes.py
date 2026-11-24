#!/usr/bin/env python3
"""shared/quotes.tsv から各プラットフォーム用の名言データを生成する。

使い方:  python3 tools/gen_quotes.py
- windows/src/Quotes.cs
- android/app/src/main/java/com/focusguard/app/Quotes.kt
- web/index.html の  /* QUOTES:BEGIN */ 〜 /* QUOTES:END */  の間
を書き換えます。名言を追加・修正したら tsv を編集してこのスクリプトを実行してください。
"""
import json
import pathlib
import re
import sys

ROOT = pathlib.Path(__file__).resolve().parent.parent
TSV = ROOT / "shared" / "quotes.tsv"


def load():
    quotes = []
    for n, line in enumerate(TSV.read_text(encoding="utf-8").splitlines(), 1):
        if not line.strip() or line.startswith("#"):
            continue
        parts = line.split("\t")
        if len(parts) != 2 or not parts[0].strip() or not parts[1].strip():
            sys.exit(f"quotes.tsv:{n}: 「本文<TAB>出典」の形式にしてください")
        text, author = parts[0].strip(), parts[1].strip()
        for s in (text, author):
            if any(c in s for c in '\\"$'):
                sys.exit(f"quotes.tsv:{n}: \\ \" $ は使えません")
        quotes.append((text, author))
    return quotes


def q(s):
    return json.dumps(s, ensure_ascii=False)


HEADER = "自動生成ファイルです。編集は shared/quotes.tsv で行い、tools/gen_quotes.py を実行してください。"


def gen_cs(quotes):
    rows = ",\n".join(f"            new Quote({q(t)}, {q(a)})" for t, a in quotes)
    return f"""// {HEADER}
using System;
using System.Text;
using System.Text.RegularExpressions;

namespace FocusGuard
{{
    public sealed class Quote
    {{
        public readonly string Text;
        public readonly string Author;
        public Quote(string text, string author) {{ Text = text; Author = author; }}
    }}

    public static class Quotes
    {{
        private static readonly Random Rnd = new Random();
        private static readonly Regex Ignored = new Regex(@"[\\s、。，．,.!?！？「」『』（）()・…:：;；""'“”‘’—–\\-]");

        public static readonly Quote[] All = new[]
        {{
{rows}
        }};

        public static Quote Pick()
        {{
            lock (Rnd) {{ return All[Rnd.Next(All.Length)]; }}
        }}

        /// <summary>句読点・空白・全角半角の違いを無視した比較用の文字列</summary>
        public static string Normalize(string s)
        {{
            if (string.IsNullOrEmpty(s)) return "";
            return Ignored.Replace(s.Normalize(NormalizationForm.FormKC), "");
        }}

        public static bool Matches(string typed, Quote target)
        {{
            return Normalize(typed) == Normalize(target.Text);
        }}

        /// <summary>入力欄の下に出す案内文</summary>
        public static string Progress(string typed, Quote target)
        {{
            string a = Normalize(typed), b = Normalize(target.Text);
            if (a.Length == 0) return "句読点とスペースは打たなくても大丈夫です";
            if (a == b) return "一致しました";
            int n = 0;
            while (n < a.Length && n < b.Length && a[n] == b[n]) n++;
            if (n == a.Length) return string.Format("ここまで合っています（{{0}} / {{1}} 文字）", n, b.Length);
            return string.Format("{{0}} 文字目から違います（{{1}} / {{2}} 文字まで一致）", n + 1, n, b.Length);
        }}
    }}
}}
"""


def gen_kt(quotes):
    rows = ",\n".join(f"        Quote({q(t)}, {q(a)})" for t, a in quotes)
    return f"""// {HEADER}
package com.focusguard.app

import java.text.Normalizer

data class Quote(val text: String, val author: String)

object Quotes {{
    private val ignored = Regex("[\\\\s、。，．,.!?！？「」『』（）()・…:：;；\\"'“”‘’—–\\\\-]")

    val all: List<Quote> = listOf(
{rows}
    )

    fun pick(): Quote = all.random()

    /** 句読点・空白・全角半角の違いを無視した比較用の文字列 */
    fun normalize(s: String?): String =
        if (s.isNullOrEmpty()) "" else ignored.replace(Normalizer.normalize(s, Normalizer.Form.NFKC), "")

    fun matches(typed: String?, target: Quote): Boolean = normalize(typed) == normalize(target.text)

    /** 入力欄の下に出す案内文 */
    fun progress(typed: String?, target: Quote): String {{
        val a = normalize(typed)
        val b = normalize(target.text)
        if (a.isEmpty()) return "句読点とスペースは打たなくても大丈夫です"
        if (a == b) return "一致しました"
        var n = 0
        while (n < a.length && n < b.length && a[n] == b[n]) n++
        return if (n == a.length) "ここまで合っています（$n / ${{b.length}} 文字）"
        else "${{n + 1}} 文字目から違います（$n / ${{b.length}} 文字まで一致）"
    }}
}}
"""


def gen_js(quotes):
    rows = ",\n".join(f"      [{q(t)}, {q(a)}]" for t, a in quotes)
    return f"""/* QUOTES:BEGIN */
    // {HEADER}
    const QUOTES = [
{rows}
    ];
    /* QUOTES:END */"""


def main():
    quotes = load()
    (ROOT / "windows" / "src" / "Quotes.cs").write_text("﻿" + gen_cs(quotes), encoding="utf-8")
    (ROOT / "android" / "app" / "src" / "main" / "java" / "com" / "focusguard" / "app" / "Quotes.kt").write_text(gen_kt(quotes), encoding="utf-8")
    html_path = ROOT / "web" / "index.html"
    html = html_path.read_text(encoding="utf-8")
    pat = re.compile(r"/\* QUOTES:BEGIN \*/.*?/\* QUOTES:END \*/", re.S)
    if not pat.search(html):
        sys.exit("web/index.html に /* QUOTES:BEGIN */ 〜 /* QUOTES:END */ がありません")
    html_path.write_text(pat.sub(lambda m: gen_js(quotes), html, count=1), encoding="utf-8")
    print(f"{len(quotes)} 件の名言を書き出しました")


if __name__ == "__main__":
    main()
