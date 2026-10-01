# 仕様ドキュメント

更新日: 2026-10-01

状態: PoC の F# 移行と ARM64 向け self-contained publish を完了。parser 0.3.0 では要件範囲・原因集約・保存済み補完定義の条件付き検査・optional skill を追加。EARS モデル生成と要件充足検証器は未実装。詳細は [移行記録](fsharp-migration.md) を参照。

今回の仕様は `docs/` で管理します。cc-sdd のインストールや spec approval metadata の生成は行っていません。

## 今後の方針と作業

1. [要件充足の検証と再現可能なモデル生成](verification-design.md): EARS、binding、検証器、optional skill の責務。
2. [実装言語と配布方針](language-and-distribution.md): F# 採用方針、.NET の事前導入を不要にする配布、移行方法。
3. [次の実装計画](roadmap.md): 現状・未実装範囲、phase ごとの作業・完了条件。

## 現行 PoC の仕様と履歴

1. [cc-sdd 調査](cc-sdd-review-analysis.md): source of truth、機械化できる範囲、仕様上の曖昧さ。
2. [MVP 仕様](mvp-spec.md): 入出力、IR、判定、semantic diff、Human Review Focus。
3. [実装計画](implementation-plan.md): 小さな increment と各検査の受入テスト。
4. [Parser profile](parser-profile.md): 実装で扱える記述形式と保守的な制限。
5. [実文書の UNKNOWN 対応](real-document-compatibility.md): 表記揺れ・範囲指定・原因集約・補完検証と残課題。

## 仕様の位置付け

- cc-sdd のルールを設計違反の根拠とします。
- parser、IR、出力形式、解析できない場合の振る舞いは、この製品の設計上の決定です。
- ユーザーが指定した循環検出や semantic diff は解析機能です。機能の存在を、新しい設計禁止事項の根拠にはしません。
- 適用する cc-sdd の revision と rule 出典を記録します。未対応のローカルカスタマイズを読み込んだだけで、検証済みとは扱いません。
- 仕様変更時は、受入条件とテスト計画も同時に更新します。

## 文書の関係

- MVP 仕様と実装計画は、現在の PoC の範囲・受入条件・テスト戦略を説明する。
- 拡張仕様は、モデル生成と要件充足検証の今後の方針を説明する。記載された成果物名や schema 例は、そのまま現行 CLI で使用できるものではない。
- 新しい方針では skill を検証準備にも利用する。既存の「AI Review は mechanical PASS 後」という制約は品質判断の AI Review に適用し、UNKNOWN を解消する binding 補完とは区別する。

## 範囲

現行 PoC は parser、Requirement coverage、Component / File Structure Plan consistency、Dependency graph 抽出、循環検出、semantic diff、Human Review Focus に限定します。拡張では検証を CLI に置き、skill なしでも人間の補完によって完結できる workflow にします。Code Graph と高度な形式手法は引き続き後続です。

## 補完 workflow

[利用 skill](../skills/design-linter-review/SKILL.md) と [補完定義の契約](../skills/design-linter-review/references/bindings.md)。CLI は非対話で、原文の判定と保存した解釈に基づく判定を分離します。
