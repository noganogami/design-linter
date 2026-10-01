# Parser profile: cc-sdd-english-v1

この profile は cc-sdd のルールを増やすものではありません。deterministic に読み取れる表現の範囲を定義します。対応外の記述は UNKNOWN になり得ます。

## Markdown

[Markdig 1.4.0](https://github.com/xoofx/markdig) の pipe table extension と位置情報 を用い、表・リスト・見出し・コードを区別します。

構文木と依存パッケージは parser 内に閉じています。validator は `DesignIR` だけを入力にします。各プロジェクトの `packages.lock.json` で依存バージョンを固定しています。

## Requirements

`### Requirement 1: Title` と直下の `#### Acceptance Criteria` の番号付きリストを読みます。数字から始まる `1. Title` などの要件見出しも扱います。受入条件のリスト番号は Markdown の表示番号として扱います。

ID は文字列 `1.1`。design の参照欄ではカンマ・読点 `、`・セミコロン・改行で区切った canonical ID を扱います。`0.3.0` から `2.1–2.9` / `2.1-2.9` のような範囲を展開します。同一親・昇順・両端明記、最大1000件に限定し、ゼロ・先頭ゼロを許容しない ASCII の正整数を使います。列挙した全 ID を存在照合し、欠番を黙って除きません。親 ID、異なる親をまたぐ範囲、自由文の参照は暗黙展開しません。コード内の数字や本文のバージョンは参照率に含めません。

## Component と traceability

`Components and Interfaces` / `Components & Interfaces` 内の以下を扱います。

- `Component`、`Req Coverage` / `Requirement Coverage` / `Requirements` を持つ summary table。
- domain/layer 見出しの下の `#### ComponentName` と `Field | Detail` の詳細表。
- `Component: Worker — Requirements: 1.1, 1.2` 形式の明示的な compact list。

同名 Component の summary と詳細を統合しますが、要件対応や layer の矛盾は UNKNOWN にします。

parserVersion `0.2.1` から、詳細見出し `#### Worker（src/worker.py）` / `#### Worker (src/worker.py)` は、`Worker` が summary の Component 名に完全一致する場合に統合します。末尾の括弧は対になり、中身が対応文字種の具体的な拡張子付きパスである必要があります。見出し全体が summary の名前と一致する場合はそちらを優先し、`Worker (Legacy)` のような修飾や summary にない名前は変更しません。元の見出しと行位置は evidence に残します。見出しのパスから File Plan の mapping を生成したり、パス接頭辞を補ったりはしません。

`Requirements Traceability` の `Requirement` と `Components / Interfaces / Flows / Contracts / Data Models / Operational Decisions` 列を解析します。Component への単純な bullet mapping `1.1 → Worker` と限定範囲 `1.1–1.3 → Worker` も対応します。未解決の非 Component 列は、参照か説明かを確定できないため `ambiguous-traceability-target` として UNKNOWN にします。明示された Component 名の未定義参照は従来どおり検査します。

名前リスト（traceability、依存、ファイル所有 Component）もカンマ・読点 `、`・セミコロン・改行で区切ります。これらの欄では区切り文字を名前の一部として扱いません。

Flow、Data Model、運用決定は、それぞれ `System Flows`、`Data Models`、`Operational Decisions` の名前付き小見出しで識別します。これらの追加列や見出しは parser の対応形式であり、cc-sdd の必須形式ではありません。

Component の `Service Interface`、`API Contract` などの小見出しは `ComponentName Service Interface` のように所有 Component 名で修飾した entity として保持します。TypeScript の `interface Name` は名前だけ抽出します。完全な言語 AST 解析・型解決・Contract 内容の検証は行いません。

IR は `uses` / `involves` の関係も表現できますが、PoC parser はそれらを自由文や図から推測しません。`providedBy` は詳細 block 内の所属関係から生成します。

## File Structure Plan

- `Path` / `File` / `Directory` 列と任意の `Component` / `Components` 列。
- `src/worker.ts — Component: Worker` または `src/worker.ts — Worker` のリスト。
- plain code block の `├──` / `└──` ツリー。コメントに明示された Component 名を対応付けます。
- `Modified Files` 内のパスは modified として保持。

相対パスを POSIX として正規化し、directory と pattern を別種として保持します。PoC のパストークンは英数字、`_ . @ { } * / -` に限定します。空白・日本語などのパスは未対応として診断します。ファイルシステム上での実在確認はしません。

directory / pattern から未記載ファイルを展開しません。具体的ファイルとの明示 mapping を確定できなければ UNKNOWN。自然言語の pattern 説明も解析診断に残します。命名規則から Component とファイルの関係を推測しません。

## Dependency

詳細 block の `Inbound: Name — purpose (P0)`、`Outbound`、`External` を解析します。`Dependencies` の下の `Direction | Component` 表も扱います。

`Dependencies: None` または summary の Key Dependencies の `None` は、依存なしの明示として扱います。空欄は依存なしと扱いません。summary の方向不明な依存は、対応する詳細宣言があれば解決し、なければ UNKNOWN とします。

Mermaid は静的依存の入力にしません。外部サービスへの edge は IR に残しますが、内部 Component の循環検出には含めません。

## 判定と差分の限界

`complete` はこの profile が対象領域を解析できたという状態です。現実の設計情報が全て記載されている保証ではありません。

未解析または未解決の関係がある領域は、semantic diff の追加・削除を抑制します。独立した領域は比較を続けます。rename は削除＋追加とし、本文の意味的変更は比較しません。

独自 template、翻訳見出し、自然言語の関係説明には UNKNOWN が多くなります。実プロジェクトの文書に適用して対応表現を増やす段階でも、UNKNOWN を減らすために推測を deterministic rule に混ぜない方針です。

## F# 移行

F# 移行時の parserVersion は `0.2.0`。旧実装との30ケースの構造比較と、行位置・code block・表・CLI の個別テストを実施しました。依存表の `Component` 列を Component 定義と誤認しない修正を追加しました。詳細は [移行記録](fsharp-migration.md) を参照してください。`0.2.1` の変更と残課題は [実文書の UNKNOWN 対応](real-document-compatibility.md) に記録しています。

## 0.3.0 の補完と原因集約

CLI は `reviewIssues` に解析診断と未解決参照を原文箇所・原因ごとにまとめます。`--bindings` で保存済みの解釈を読み、別の候補 IR を検査できます。原文 IR・通常の判定・semantic diff は変えません。[補完定義の仕様](../skills/design-linter-review/references/bindings.md) を参照。
