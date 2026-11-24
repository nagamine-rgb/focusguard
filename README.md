# FocusGuard — Windows & Android デジタルデトックス ブラウザブロッカー

Windows PC と Android スマホのブラウザを **段階的な強度（Lv.1〜Lv.3）** と **時間帯スケジュール** で一括ブロックできる個人向けツールです。  
同期バックエンドには **Supabase**（テーブル1つ＋RPC関数3つ）を使用し、外出先からでもスマホ⇔PC間で即座にブロックを指示できます。

---

## 強度レベル（3段階）

| レベル | 名称 | PC / スマホでの動作 |
| :--- | :--- | :--- |
| **Lv.1** | 気づき（ソフト） | ブラウザを開くと**15秒カウントダウン画面**を表示。待ち切れば5分だけ一時許可可能（タブは維持）。 |
| **Lv.2** | ひと手間（ミドル） | 起動をブロック。10分だけ許可するには**ランダムな確認フレーズの手入力**（貼付不可）が必要。 |
| **Lv.3** | 強制ブロック（ハード） | ブラウザを検知次第**即座に終了**。終了時刻まで停止ボタン・レベル引き下げもロック。 |

---

## 構成ファイル

- `windows/FocusGuard.exe` — Windows 常駐アプリ（トレイ常駐＋内蔵リモコン＋3段階ブロックUI）
- `windows/build.ps1` — Windows標準の .NET Framework (`csc.exe`) だけで再ビルドするスクリプト
- `supabase/schema.sql` — Supabase SQL Editor に貼って実行する同期用スキーマ＆RPC
- `web/index.html` — スマホ／PC 両対応の Web リモコン画面（単一HTML）

---

## 1. まずはローカルで今すぐ動作確認（Windows PC）

1. `windows/FocusGuard.exe` をダブルクリックして起動します。
2. **「リモコン」タブ**で Lv.1〜Lv.3 と時間を選んで **「この内容でブロック開始」** をクリック。
3. Chrome や Edge などのブラウザを開くと、選んだ強度のブロックが発動します。
   - ウィンドウの `×` で閉じてもタスクトレイに常駐し続けます。
   - **「同期・設定」タブ** → **「ローカルWeb UIを開く」** (`http://localhost:8787/`) から Web 版リモコンも確認できます。

---

## 2. Supabase で外出先のスマホからも同期する手順

1. お使いの Supabase プロジェクトの **SQL Editor** を開き、`supabase/schema.sql` の内容をすべて貼り付けて **Run** します。
2. Supabase の **Project Settings > API** から以下を控えます。
   - `Project URL` (`https://xxxx.supabase.co`)
   - `anon public` key
3. `FocusGuard.exe` の **「同期・設定」タブ**で：
   - **「Supabase クラウド同期」** を選択
   - `Supabase URL` と `Anon Key` を貼り付け（`Sync Key` は自動生成されたもの、または任意の8文字以上の文字列）
   - **「Supabase 接続テスト」** で成功を確認し、**「設定をすべて保存」** をクリック
4. スマホから操作する場合：
   - `web/index.html` を GitHub Pages / Cloudflare Pages / Vercel などの無料静的ホスティングに置くだけでスマホ用リモコンになります。
   - PCアプリの **「スマホ設定用ハッシュをコピー」** でコピーした文字列をホスティングURLの末尾に付けてスマホで開くと、1タップで接続設定が完了します。

---

## 3. Android スマホ用アプリのインストール＆設定

GitHub Actions で自動ビルドされた APK が **GitHub Releases** に公開されます（Android Studio 不要）。

1. スマホでリポジトリの **Releases（Latest）** を開き、`FocusGuard.apk` をダウンロードしてインストールします（初回のみ「不明なアプリのインストール」を許可）。
2. FocusGuard アプリを開き、画面上部の案内に従って以下をオンにします：
   - **ユーザー補助（Accessibility）**: 「FocusGuard ブラウザブロック」を ON
   - **他のアプリの上に重ねて表示**: 許可を ON
   - （推奨）**バッテリー制限なし**を ON
3. 画面下の **Supabase 接続設定** に、PC アプリの「スマホ設定用ハッシュをコピー」でコピーした文字列を貼り付けて **「URL / ハッシュから一括取り込み」** を押せば同期完了です！

---

## Lv.2 の確認に出る名言を編集する

Lv.2 の解除やブロック停止のときは、内蔵の名言（約100件）からランダムに1つが表示され、それを手入力します。句読点・スペース・全角半角の違いは無視して照合します。

1. `shared/quotes.tsv` を編集します（1行に「本文<TAB>出典」）。
2. `python3 tools/gen_quotes.py` を実行すると、`web/index.html`・`windows/src/Quotes.cs`・`android/.../Quotes.kt` が書き換わります。
