# 実装計画とテスト戦略

この文書は現行 MVP の受入条件とテスト戦略をまとめる。後続の EARS モデル・検証器の作業は [次の実装計画](roadmap.md) で管理する。F# 移植の互換性と検証結果は [移行記録](fsharp-migration.md) を参照。

MVP の5 increment を実装済み。各 mechanical rule の unit test、parser fixture、semantic diff、CLI 統合テストで検証する。AI 接続や大規模な rule framework は含めない。

## Increment 1: parser と IR

- [x] F# の最小構成とテスト実行環境を用意する。
- [x] IR、evidence、解析診断を定義する。
- [x] Markdown AST と requirements / design の parser を分離する。
- [x] 標準 template に基づく小さな fixture を作成し、出典 revision を記録する。

完了条件: 要件・Component・明示 mapping・File Plan・依存宣言を根拠位置付きで抽出できる。未対応箇所が消えずに診断される。

## Increment 2: coverage と File Plan

- [x] M1 Requirement coverage を実装する。
- [x] M2 Component / File Plan consistency を実装する。
- [x] Boundary の空欄・placeholder-only 補助検査を実装する。
- [x] FAIL と UNKNOWN を区別する結果 model を用意する。

完了条件: 正常・違反・判定不能を各 rule の unit test で再現できる。directory pattern や summary-only Component を一律 FAIL にしない。

## Increment 3: Dependency graph と循環

- [x] Inbound / Outbound / External を正規化する。
- [x] 強連結成分と自己ループを検出する。
- [x] 未解決 edge と解析範囲を結果に反映する。

完了条件: flow と dependency を混同せず、循環の根拠を示せる。不完全グラフから全面的な PASS を出さない。

## Increment 4: Semantic diff

- [x] 変更前後の requirements / design の組を比較する。
- [x] Component、依存、要件・mapping、ファイル・mapping の差分を実装する。
- [x] 解析不能領域の比較を抑制し、理由を報告する。

完了条件: 整形変更で差分が出ず、解析不能を削除と誤認しない。要件追加と対応先変更を区別できる。

## Increment 5: CLI と Human Review Focus

- [x] CLI 引数と終了コードの契約を確定・文書化する。
- [x] 共通 report model から JSON / Markdown を生成する。
- [x] 根拠付きの Focus を最大3件生成する。
- [x] 入力エラー、FAIL、UNKNOWN、PASS の一連の workflow を検証する。
- [x] サンプル入出力と実行手順を README に追加する。

完了条件: reviewer が全体の状態、主要変更、確認対象の行を report から辿れる。AI 未実行・未検査範囲が明示され、入力が変更されない。

## テスト戦略

rule の unit test は手組みの IR を入力にする。parser は Markdown fixture で独立に検証する。最後に CLI の少数の統合テストで接続を確認する。

| 対象 | 正常系 | 異常・境界・誤検出防止 |
|---|---|---|
| Requirement parser | 親要件と受入条件、複数要件 | コード例・日付・バージョン除外、重複 ID、ID 粒度不明、抽出ゼロ |
| Design parser | summary table、詳細 block、明示 bullet、節の並べ替え | summary-only、同名定義の曖昧さ、未対応見出し、optional section 省略 |
| M1 coverage | 全件対応、Flow・Contract・運用決定への対応 | 未参照、架空 ID、dangling mapping、ID 出現だけ、解析不足、親 ID を展開しない |
| M2 File Plan | 個別パス、複数ファイル対応、modified files、明示 pattern | 空欄、placeholder、対応欠落、暗黙 mapping は UNKNOWN、外部サービス除外、補助ファイルを Component と誤認しない |
| Boundary 補助検査 | 必須項目に本文がある | 空欄、placeholder-only、意味の妥当性は PASS と主張しない |
| Dependency 抽出 | Inbound 反転、Outbound、External、重複統合 | 方向不明、未定義参照、Sequence 往復を循環にしない、依存未記載と明示的な依存なし |
| M3 循環 | DAG、明示的に edge なし | 自己ループ、2点・多点循環、複数 SCC、不完全グラフ、循環を一律 FAIL にしない |
| Semantic diff | 各追加・削除・mapping 変更 | 並べ替え・整形・重複で差分なし、rename を推測しない、解析失敗を削除にしない、要件追加と mapping 変更を区別 |
| Focus | 根拠位置、最大3件、原因の集約 | 架空の AI issue を作らない、診断の詳細を失わない、3件に水増ししない |
| Report / CLI | JSON と Markdown の整合、baseline なし | 読み取り失敗、baseline 片方不足、UNKNOWN で PASS にしない、入力不変 |

## PoC 全体の受入条件

- 全 mechanical rule に unit test がある。
- 標準 template の許容する省略・directory pattern・summary-only 表現を正常または UNKNOWN として扱い、不当な FAIL を出さない。
- 全指摘から rule 出典と文書位置を追える。
- 抽出不能を「存在しない」、未検査を「問題なし」と表示しない。
- semantic diff と Focus により、変更された構造と確認対象の箇所を先に読める。
- AI / Code Graph / DB / 形式手法への依存なしで動作する。

## 後続計画

[次の実装計画](roadmap.md) に従い、EARS モデル生成、永続化した binding、小さな検証器を追加する。実文書での parser 検証と Code Conformance は、引き続き明示的な範囲・限界を持つ increment とする。
