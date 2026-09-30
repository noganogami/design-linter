# design-linter

cc-sdd の `design.md` に対して機械的な整合性検査と構造の semantic diff を行い、人間が確認すべき箇所を絞るための PoC です。

構造検査・semantic diff・Human Review Focus を生成する CLI を実装しています。AI Review は実行しません。

本体は **F# / .NET 10** です。self-contained の単一ファイルを生成でき、配布物の利用に .NET・Node・proto は不要です。EARS からの再現可能なモデル生成と要件充足の検証は今後追加します。skill は optional とする方針です。[次の実装計画](docs/roadmap.md) を参照してください。

## 実行

開発には proto で固定した .NET SDK 10.0.401 を使用します。`proto run dotnet --` により、PATH 上の別バージョンと区別します。

```sh
proto install dotnet
scripts/check.sh

proto run dotnet -- run --project src/DesignLinter -- \
  --requirements examples/payment/after/requirements.md \
  --design examples/payment/after/design.md \
  --before-requirements examples/payment/before/requirements.md \
  --before-design examples/payment/before/design.md
```

自分の spec を検査する場合は `--requirements` と `--design` にそのファイルを指定します。baseline は両方のオプションを省略できます。`--format json` で IR、解析診断、検査結果、差分を含む JSON を出力します。

`examples/payment/before/` と `after/` は差分の実演用スナップショットです。cc-sdd が変更前ファイルを生成するわけではありません。通常の検査は、cc-sdd が生成した2ファイルだけで実行できます。

```sh
proto run dotnet -- run --project src/DesignLinter -- \
  --requirements .kiro/specs/payment/requirements.md \
  --design .kiro/specs/payment/design.md
```

差分比較する場合だけ、Git の比較元 commit などから取得した同じ時点の `requirements.md` と `design.md` を `--before-requirements` / `--before-design` に渡します。現時点の CLI は Git 履歴を自動取得しません。新規 spec で比較元がない場合は、この2オプションを省略します。

出力は stdout、入力・実行エラーは stderr です。入力ファイルは変更しません。レポートを保存する場合は入力とは別のパスにリダイレクトしてください。

| 終了コード | 意味 |
|---|---|
| 0 | Mechanical PASS |
| 1 | Mechanical FAIL |
| 2 | 引数・入力読み取りエラー |
| 3 | Mechanical UNKNOWN または REVIEW_REQUIRED |

終了コードは現在の文書の mechanical checks に対応します。baseline の比較可否は JSON の `semanticDiff.status` と各 group に別途表示します。

## 配布物の作成

```sh
scripts/publish.sh osx-arm64
# Linux ARM64 用は scripts/publish.sh linux-arm64
./artifacts/osx-arm64/design-linter --help
```

実行ファイルにランタイムを同梱します。Linux ARM64 は .NET 未導入コンテナで検証済み、macOS ARM64 は開発機上で検証済みです。Windows・x64・macOS の署名／公証と公開配布は未対応です。OS のシステムライブラリと native library 展開先への書き込みは必要です。詳しくは [移行と配布の検証記録](docs/fsharp-migration.md) を参照してください。

## 対応範囲

- 英語見出しの固定 cc-sdd profile。本文は日本語でも利用できます。
- Requirement ID、明示的な設計要素・ファイル対応、Inbound / Outbound / External。
- 循環は `REVIEW_REQUIRED`。未解析を `PASS` にしません。
- 主要な Human Review Focus は最大3件。全 finding は詳細に残します。

自由文からの関係推測、Mermaid からの静的依存推測、依存方向制約の判定、AI Review、Code Graph は対象外です。詳しい構文と制限は [parser profile](docs/parser-profile.md) を参照してください。

## ドキュメント

- [仕様ドキュメントの入口](docs/README.md)
- [要件充足の検証・EARS モデル・optional skill](docs/verification-design.md)
- [F# と配布方針](docs/language-and-distribution.md)
- [次の実装計画](docs/roadmap.md)
- [cc-sdd 調査とレビュー観点の分類](docs/cc-sdd-review-analysis.md)
- [MVP 仕様](docs/mvp-spec.md)
- [実装計画とテスト戦略](docs/implementation-plan.md)
- [サンプルと検証方法](examples/README.md)

Mechanical PASS は、設計の正しさや実装開始の承認を意味しません。責務境界、要件解釈、trade-off、将来の変更への耐性は人間が判断します。
