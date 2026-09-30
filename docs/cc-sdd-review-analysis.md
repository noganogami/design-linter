# cc-sdd review analysis

## 調査基準

公式リポジトリ: https://github.com/gotalab/cc-sdd

調査日: 2026-09-30

固定 revision: `e2a0c671aef37a482404905b409ab5c27092b25b`

この文書は上記 revision の調査結果です。任意の cc-sdd バージョンやプロジェクト固有ルールへの対応を保証しません。

## Source of truth

| ID | ファイル | 確認した内容 |
|---|---|---|
| S1 | [kiro-spec-design/SKILL.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/agents/codex-skills/skills/kiro-spec-design/SKILL.md) | 草案、review gate、確定の順序。具体的な File Structure Plan |
| S2 | [kiro-validate-design/SKILL.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/agents/codex-skills/skills/kiro-validate-design/SKILL.md) | 任意の対話的レビュー、最大3件の重要指摘、GO/NO-GO |
| S3 | [design-review-gate.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/design-review-gate.md) | 要件、境界、実装可能性、mechanical checks |
| S4 | [design-review.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/design-review.md) | architecture alignment、保守性、型・interface、指摘の根拠 |
| S5 | [design-principles.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/design-principles.md) | 数値 ID、依存方向、Component、Contract、Mermaid |
| S6 | [design template](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/templates/specs/design.md) | 文書構造、summary-only Component、directory pattern |
| S7 | [requirements template](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/templates/specs/requirements.md) | 数字付きの要件見出しと受入条件 |
| S8 | [tasks template](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/templates/specs/tasks.md) / [tasks-generation.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/tasks-generation.md) | `_Boundary:_`、`_Depends:_`、要件参照、bounded task |
| S9 | [design-synthesis.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/design-synthesis.md) | generalization、build vs. adopt、simplification |
| S10 | [design-discovery-full.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/design-discovery-full.md) / [design-discovery-light.md](https://github.com/gotalab/cc-sdd/blob/e2a0c671aef37a482404905b409ab5c27092b25b/tools/cc-sdd/templates/shared/settings/rules/design-discovery-light.md) | 既存構造、外部依存、境界、互換性の調査 |

これらの skill は調査対象として読んだものであり、このリポジトリで実行した workflow ではありません。

## Review 観点の分類

Confidence は、必要な記述を解析できた場合の判定信頼度です。

| Review item | Automatable? | Method | Confidence | Implementation complexity | 根拠 |
|---|---|---|---|---|---|
| Requirement ID の存在・参照整合性 | 可 | 定義・参照集合の照合 | 高 | 低 | S1, S3, S5, S7 |
| Requirement と設計要素の対応 | 条件付き | 表・明示参照の名前解決 | 高。自由文は不明 | 中 | S3, S5, S6 |
| 設計が要件の意味を満たすか | AI補助＋人間 | 受入条件との意味的照合 | 中 | 高 | S3, S4 |
| Boundary の空欄・placeholder | 可 | セクション本文検査 | 高 | 低 | S3 |
| File Plan のパス・placeholder | 可 | ツリー・表・リスト解析 | 高 | 中 | S1, S3, S6 |
| Component と File Plan の対応 | 条件付き | 明示関係の照合 | 高。暗黙関係は不明 | 中 | S3, S6 |
| Boundary とファイル責務の意味的整合性 | AI補助＋人間 | 所有範囲の解釈 | 中 | 高 | S3 |
| 依存グラフ抽出 | 条件付き | Inbound / Outbound / External | 高。方向不明は不明 | 中 | S5, S6 |
| 循環の存在 | 可 | 強連結成分・自己ループ | 抽出範囲内で高 | 低 | ユーザー指定の解析機能 |
| 依存方向違反・禁止依存 | 条件付き、後続 | 明示制約と edge の比較 | 制約解決時は高 | 中〜高 | S3, S5 |
| 未定義依存先 | 条件付き | 内部・外部参照の名前解決 | 高。種別不明は不明 | 中 | S5, S6。PoC では解析診断 |
| Interface / Contract 参照整合性 | 条件付き、後続 | 定義・参照・提供元照合 | 中〜高 | 中 | S5, S6 |
| 型安全性・Contract の十分性 | 一部可、後続 | 構文検査＋意味的レビュー | 構文は高、意味は中 | 中〜高 | S4, S5 |
| 責務分割・不要な抽象化・既存構造との整合 | AI補助＋人間 | 境界・要件・既存実装との比較 | 中 | 高 | S3, S4, S9, S10 |
| テスト可能性・障害・移行の妥当性 | AI補助＋人間 | 振る舞いと制約の評価 | 中 | 高 | S1, S3, S4 |
| Trade-off・将来変更・リスク受容 | 人間が決定 | 代替案と文脈に基づく判断 | 自動確定しない | — | S4, S9 |

## 実装時に保持する区別

1. ID は `N.M` が標準。`Requirement 1` の受入条件1は `1.1`。`REQ-001` を標準とはしない。
2. 要件の対応先には Component、Contract、Flow、Data Model、運用上の決定が含まれる。
3. ID の本文出現と、有効な設計要素への mapping は別の事実。
4. Owner / Reviewers は任意。担当者欄の欠落を違反にしない。
5. Mermaid の edge はデータ／制御フローを表す。依存 edge として流用しない。
6. 循環の存在は検出できるが、調査範囲には全種類の循環を一律禁止する規則はない。PoC は循環を REVIEW_REQUIRED とする。
7. orphan Component は File Plan との対応を検査する。グラフの入次数ゼロだけで不要な Component と断定しない。
8. 全ファイルに Component 定義を要求しない。逆照合は File Plan に明記された Component 参照だけを対象とする。
9. 文書全体の TODO を一律違反にしない。必須項目の未確定と Open Questions を区別する。

## 仕様の緊張関係と保守的な扱い

- S3 は Component ごとの file path を要求する一方、S6 は大きな機能で directory＋pattern を許容する。pattern を保持し、明示的に導出できる対応だけを確定する。推測が必要なら UNKNOWN とする。
- S6 の Component summary の例には `1, 2` があるが、S5 は canonical `N.M` を要求する。親 ID を全受入条件に暗黙展開せず、粒度不明として診断する。
- S3 の Mechanical Checks に含まれる boundary と file structure の整合性は意味的判断を含む。存在検査と意味的レビューに分ける。
- S5 の layer order の矢印と依存 edge の向きは同一とは限らない。将来の制約 parser で区別し、PoC で任意の階層順を固定しない。
- S8 の task dependency は Component dependency と別種の関係。PoC は tasks.md を入力にしない。
