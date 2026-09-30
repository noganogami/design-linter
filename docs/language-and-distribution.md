# 実装言語と配布方針

更新日: 2026-10-01

状態: F# への移行を完了。macOS ARM64 / Linux ARM64 の単一ファイルを生成し、Linux は .NET 未導入コンテナで検証した。[移行記録](fsharp-migration.md) を参照。

## 前提

- 開発は主にユーザー本人が行う。
- 利用者は周囲の人を想定する。利用者への .NET の事前インストールは避けたい。
- TypeScript / Python / F# に習熟しており、配布上の懸念が解消できれば F# を好む。
- 今後は EARS parser、型付きモデル、変換規則、検証器を中心に開発する。

## 選定理由

F# の discriminated union、pattern matching、型付きの関数による変換は、構文木、条件式、状態遷移、成功・反例・判定不能などの結果表現に適する。開発者が継続して扱いやすいことも選定理由とする。[F# discriminated unions](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/discriminated-unions)

| 候補 | 今回の判断 |
|---|---|
| TypeScript | 初期 PoC の言語。Markdown / JSON / CLI と参加しやすさに利点。移植の比較基準として利用 |
| Python | solver やアルゴリズム実験の候補だが、本体を移す決め手とはしない |
| F# | モデル操作との相性、開発者の習熟・選好、self-contained 配布を理由に採用 |
| Java / Scala | JVM 統一には利点があるが、optional な TLC のためだけに本体を JVM へ寄せない |

型の表現力だけで検証器の正しさや再現性が保証されるわけではない。変換規則・根拠・テストは言語と別に設計する。

## 配布

基本方針は self-contained の単一ファイル CLI。ランタイム同梱と単一ファイル化を両方明示し、通常の利用に .NET の事前インストールを要求しない。

| 対象 | 必要なもの |
|---|---|
| 開発者 | .NET SDK |
| 通常の利用者 | 対象 OS・CPU に対応した配布物と必要な OS 環境 |
| TLC 利用者 | 上記に加え、TLC と互換性のある Java 実行環境 |

[.NET 配布方式](https://learn.microsoft.com/en-us/dotnet/core/deploying/) と [single-file deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) に基づく。単一ファイルでも OS・CPU ごとの publish が必要であり、OS 側の依存が全てなくなるわけではない。必要に応じて native library の bundle / extraction 設定も確認する。

配布側の責務:

- 対応する OS / CPU ごとのビルドと起動確認。
- ランタイムを含む配布サイズの測定。
- ランタイム更新を反映した再配布。
- 必要なシステムライブラリや OS 最小条件の明記。
- 配布経路に応じたインストール・更新手順。署名などが必要かは対象環境の検証で決める。

最初は Native AOT と trimming を必須にしない。まず通常の self-contained 配布を成立させ、サイズ・起動時間が問題になった場合に最適化を検討する。

`dotnet tool install` を利用者向けの唯一の導入方法にはしない。Node / uv / Java を本体 CLI の起動ラッパーとして必須にすることもしない。

## 本体と外部検証器の境界

```text
F# CLI
  ├─ Markdown / EARS parser
  ├─ IR / binding / モデル変換
  ├─ deterministic verifier
  ├─ report / Human Review Focus
  └─ optional external verifier adapter
       └─ 例: Java プロセスで TLC を実行
```

TLC は JAR を Java で実行できるため、本体言語を Java に統一する必要はない。[TLC の実行方法](https://docs.tlapl.us/using%3Atlc%3Astart)

外部検証器を導入する際は、バージョン、timeout、メモリ、キャンセル、反例の取り込みを adapter の責務とする。通常検査に Java を要求しない。

## 移行の進め方

1. 小さな F# CLI を self-contained publish し、.NET 未導入の対象環境で実行する。
2. Markdown parser 候補の [Markdig](https://github.com/xoofx/markdig) で表・位置情報・code block・リストを確認する。ライブラリ採用はこの検証後に確定する。
3. 現行 fixture と期待結果を、言語に依存しない受入基準にする。
4. IR、parser、rule、semantic diff、report、CLI を段階的に移植する。
5. 意図した仕様変更と移植による差を区別し、差分を記録する。
6. 同等機能と配布検証の成立後に本体を切り替える。

移行中に TypeScript と F# を比較実行することは許容するが、最終的に TypeScript CLI + F# core の常設二言語構成にはしない。既存の誤判定を互換性のために保存する必要もない。修正は regression fixture として明示する。

## 確定した構成と残作業

- .NET 10 LTS / SDK 10.0.401 / `net10.0`。proto と global.json で固定する。
- Markdig 1.4.0、FSharp.SystemTextJson 1.4.36。依存は lock file で固定する。
- `osx-arm64` と `linux-arm64` を publish script の対象とする。
- JSON schemaVersion 1 と CLI の主要オプション・終了コードを維持する。
- self-contained、single-file、native library の自己展開を有効にする。trimming と Native AOT は使わない。

.NET 10 は2028年11月14日までサポートされる LTS として選定した。[公式サポートポリシー](https://dotnet.microsoft.com/en-us/platform/support/policy)。SDK バージョンは [公式リリース metadata](https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json) に基づく。

publish 時の成果物はローカルの `artifacts/` に生成する。公開リリース、署名／公証、更新経路、Windows・x64 対応は今後、利用環境に合わせて決める。利用者への導入完了までは主張しない。
