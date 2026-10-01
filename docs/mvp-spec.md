# MVP 仕様

この文書は現行の F# PoC の仕様。次段階の EARS モデル生成・検証器・optional skill は [拡張仕様](verification-design.md)、F# の配布方針は [言語と配布](language-and-distribution.md) を参照。

## 目的と境界

設計の機械的な整合性確認を自動化し、reviewer が責務分割、boundary、要件解釈、trade-off、将来変更に集中できるようにする。

PoC の責務:

- cc-sdd Markdown を根拠位置付きの Design IR に変換する。
- Requirement coverage と Component / File Structure Plan の整合性を検査する。
- 明示的な依存を抽出し、循環を検出する。
- 変更前後の構造を比較する。
- 根拠箇所に絞った Human Review Focus を生成する。

対象外: Code Graph、LLM 実行、全 Contract の構文検証、依存方向・禁止依存の自動判定、tasks 検証、DB、Alloy、TLA+、文書の自動修復、spec.json の承認更新。

Mechanical PASS は設計承認でも cc-sdd review gate 全体の通過でもない。

## 入出力

必須入力は現在の `requirements.md` と `design.md`。差分比較時は変更前の両ファイルも指定する。変更前の片方だけで比較しない。baseline がなければ semantic diff は「未実行」であり「変更なし」ではない。

baseline は Git 履歴などから取得する過去のスナップショットであり、cc-sdd の追加成果物ではない。PoC の CLI は Git 履歴の自動取得を行わず、取得済みのファイルパスを受け取る。

出力は同じ report model から生成する JSON と Markdown。入力は変更しない。CLI 引数と終了コードは [README](../README.md) を参照する。

レポートには以下を含む。

- 適用 rule profile と cc-sdd revision、入力識別子、parser version。
- Mechanical Checks、解析範囲、FAIL / UNKNOWN の件数と根拠。
- Semantic Changes と比較できなかった範囲。
- Human Review Focus 最大3件、全診断への参照。
- AI Review は `NOT_RUN`。AI の推測による issue を表示しない。
- JSON は現在の Design IR 全体も含む。Markdown はレビューに必要な要約と根拠を表示する。

## Parser contract

初期 profile は調査 revision の標準的な英語見出しと構造を対象とする。本文は日本語でも扱える。翻訳見出しや独自 template は未対応箇所を診断し、黙って標準として解釈しない。

- Markdown AST を用い、見出し、表、箇条書き、コードブロックを区別する。
- 要件見出しの数字と受入条件の番号から canonical `N.M` を得る。ID は文字列として保持する。
- 日付、バージョン、コード例中の数字を要件 ID と認識しない。
- Traceability table、Component summary、詳細 block の Requirements を読む。summary-only Component と節の並べ替えを許容する。
- 同じ Component の summary と詳細 block は根拠を残して統合する。曖昧な同名定義や矛盾は診断する。
- bullet mapping は明示的に要件 ID と設計要素が対応しているものを扱う。自由文の意味を推測しない。
- ファイルツリー、パスのリスト・表、Modified Files を読む。directory と pattern を実ファイルと区別する。
- 明示的な名前・参照で対応を確定する。名前の類似や `PaymentService` → `payment-service.ts` の命名推測は使わない。
- 明示された Flow、Contract、Data Model、運用決定への参照は一般の設計要素として保持する。内容の十分性は判定しない。
- 未対応構文、曖昧な名前、解決できない対応を解析診断に残す。

## Design IR

通常の型付き in-memory データ構造とする。F# record と判定状態の discriminated union を使用する。

実装は F#。interface / contract は `entities` の種別、関係は `relations` で表す。実装済み parser の詳細は [parser profile](parser-profile.md) を参照。

| Entity | 内容 |
|---|---|
| Requirement | canonical ID、本文、受入条件 |
| Component | 名前、domain/layer、責務、内部／既存／外部の区別 |
| Flow | 名前、明示された参加 Component |
| Interface / Contract | 名前、種類、明示された提供元。PoC は完全な構文解析を行わない |
| DesignElement | Data Model・運用決定などの参照可能な要素 |
| FileEntry | パス、file / directory / pattern、責務、new / modified / unspecified |
| Boundary | owns、out-of-boundary、allowed dependencies、revalidation triggers の本文 |
| Relationship | 種類、始点、終点、解決状態 |
| ParseDiagnostic | 未対応構文、曖昧さ、解析不能範囲 |

```text
Requirement -> realizedBy -> Component / Flow / Contract / DesignElement
Component -> dependsOn -> Component / ExternalSystem
Component -> uses -> Interface / Contract
Component -> implementedAt -> FileEntry
Flow -> involves -> Component
Interface -> providedBy -> Component
```

全 entity / relationship は `file`, `startLine`, `endLine`, `section` を含む evidence を持つ。参照は `resolved | unresolved | ambiguous` を持つ。領域別に `complete | partial | unsupported` の解析状態を保存する。ここで complete は対応構文の解析完了であり、文書に現実の依存が全て書かれている保証ではない。

## 判定状態

| 状態 | 意味 |
|---|---|
| PASS | 対象範囲を解析し、検査条件を満たした |
| FAIL | 根拠のある既存ルール違反を確定した |
| UNKNOWN | 情報・解析能力が足りず判定できない |
| REVIEW_REQUIRED | 構造上の事実は得られたが妥当性判断が必要 |
| NOT_APPLICABLE | 適用対象がないことを確認した |

未実行は判定状態と別に保持する。必須検査が UNKNOWN / REVIEW_REQUIRED のとき全体 PASS にしない。FAIL があっても他の独立した検査と差分生成は続け、再実行の負荷を減らす。

各 finding は rule ID、status、message、evidence、出典を含む。出典は cc-sdd rule とユーザー指定の解析機能を区別する。

## Mechanical rules

### M1: Requirement coverage

根拠: 調査文書 S1, S3, S5, S7。

- ID の参照率と、有効な設計要素への対応率を別々に出す。
- 全体を解析でき、要件が未参照／未対応なら FAIL。
- design の明示的な requirement 参照が requirements に存在しなければ FAIL。
- mapping の対象が確実に存在しなければ FAIL。parser 未対応により存在を確認できない場合は UNKNOWN。
- 親 ID `1` を `1.1, 1.2, ...` に暗黙展開しない。粒度不明は UNKNOWN。
- Requirement 抽出ゼロを `0 / 0 PASS` にしない。
- Contract、Flow、Data Model、運用決定への明示対応も数える。

### M2: File Plan と Component の整合性

根拠: S1, S3, S6。

- File Structure Plan が空または placeholder のみなら FAIL。
- 必須の具体的なパスに代わる TBD / TODO / template placeholder は FAIL。任意の Open Questions 内の TODO は一律に違反としない。
- Component とパスの明示関係を照合する。完全に読めた個別ファイル形式で欠落が確定すれば FAIL。
- directory＋pattern は許容する。明示的に導出できる場合だけ対応済みとし、暗黙の対応は UNKNOWN。
- File Plan に明記された Component が存在しなければ FAIL。ただし単なるファイル責務文を Component 宣言とは扱わない。
- 外部サービスを内部実装ファイルの欠落として報告しない。
- Boundary の空欄・placeholder-only 検査を軽量な補助検査として行う。所有範囲とファイル責務の意味的整合性は未検査と表示する。

### M3: Dependency graph と循環

抽出形式の根拠: S5, S6。循環検出はユーザー指定の解析機能。

- A の `Outbound: B` は A → B、A の `Inbound: B` は B → A に正規化する。
- External は外部 entity として保持する。内部 graph の未定義 Component と混同しない。
- Key Dependencies の方向を確定できなければ UNKNOWN。Mermaid の flow edge は依存に変換しない。
- 同じ edge の重複は統合し、複数の根拠を保持する。
- 強連結成分と自己ループで循環を検出し、代表的な閉路と根拠を示す。
- 循環ありは REVIEW_REQUIRED。PoC は循環禁止や任意の layer 順を新設しない。
- 循環なしは「抽出した依存グラフで循環なし」。解析不能な依存があれば全体結果は UNKNOWN。
- 依存の未記載を依存なしと断定しない。明示的な依存なしの記述と区別する。

## Semantic diff

正規化した entity と relationship の集合を比較する。

- Added / Removed Components
- Added / Removed Dependencies
- Changed Requirement Mapping: before / after の対応先
- Added / Removed Requirements: mapping 変更と区別
- Added / Removed Files
- Changed Component–File Mapping

改行、表の順序、同じ関係の重複だけでは差分を出さない。安定 ID のない rename は削除＋追加とし、推測しない。片側の解析が不完全な領域は削除／追加を断定せず、比較不能として提示する。

責務文、trade-off、Contract 本文の意味的変更は PoC の対象外。構造差分ゼロは設計判断の変更なしを保証しないことをレポートに記す。

## Human Review Focus

LLM を用いず、確認できた事実を根拠とする問いを生成する。意味的な欠陥を発見したと装わない。

優先順位:

1. FAIL / UNKNOWN の解消に必要な箇所。
2. 要件対応、依存、Component 配置の変更に対する問い。
3. 変更箇所の責務境界・trade-off に対する問い。

同じ原因をまとめ、主要表示は最大3件。全診断は詳細に残し、省略件数と参照を表示する。差分や根拠がないときに3件を埋めるための問いを作らない。意味的レビューが未実施であることは明示する。

例: 「要件 2.3 の対応先を PaymentService から PaymentRetryWorker に変えた解釈は妥当か？」に、変更前後の mapping と受入条件へのリンクを添える。

## AI Review との接続境界

PoC は AI を実行しない。将来は Mechanical PASS 後にだけ独立した AI Review を実行する。出力には最大3件の重要指摘、影響、改善案、Requirement ID、設計箇所を要求する。AI の GO/NO-GO 推奨、人間の承認、mechanical 結果を別に保持する。

この条件は品質判断の AI Review に対するもの。今後の optional skill による未解決 binding の補完は別工程であり、Mechanical PASS 前にも利用できる。検証結果そのものは CLI が生成する。

## MVP architecture

```text
requirements.md + design.md
  -> Markdown AST
  -> cc-sdd parser
  -> Design IR + 解析診断
      -> mechanical rules
      -> semantic diff <- 変更前の両文書から生成した IR
  -> report model
  -> Human Review Focus
  -> JSON / Markdown
```

実装モジュール:

```text
src/DesignLinter/
  Domain.fs        # IR・判定型・JSON
  Markdown.fs      # Markdig AST adapter
  Parser.fs        # cc-sdd profile → IR
  Rules.fs         # IR のみを検査
  SemanticDiff.fs  # 前後の IR を比較
  Report.fs        # Human Review Focus・Markdown
  Program.fs       # 引数・入出力・終了コード
```

validator は Markdown に依存しない。IR は rule に依存しない。Focus は IR・finding・diff を入力にし、parser の内部実装に依存しない。

## 0.3.0 の追加機能

`reviewIssues` と `inputFingerprint` を JSON に追加し、`--bindings` 指定時は `supplementalReview` を付加する。原文の `status` / `checks` / `designIR` / `semanticDiff` は保持する。補完結果は条件付きであり、意味解釈の確認状態と機械検査を分離する。詳細は [補完定義の契約](../skills/design-linter-review/references/bindings.md)。実装は `ReviewIssues.fs`（原因集約）と `Bindings.fs`（補完の入力検査・候補 IR）に分離している。
